using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>What a dry-run decided about one data row, before it is saved (shared by the patient and appointment dry-runs).</summary>
internal sealed record ImportRowOutcome(int RowNumber, string Status, string? Reason, IReadOnlyDictionary<string, string>? Errors, IReadOnlyList<Guid>? Matched, int? DuplicateOfRow, bool KeepDecision = false);

/// <summary>
/// ALV-018-C02 slice 4a: judges every row of an appointment file against the scheduling rules WITHOUT scheduling anything, in bulk. The checks are the scheduler's own, in its own order (<see cref="AppointmentScheduler"/>):
/// 1. the row's own values (all problems at once, as fixed messages, never a value from the file): the patient's names and birth date, the provider, operatory and appointment type, a real practice-local start
///    (<c>yyyy-MM-dd HH:mm</c>), a duration the practice allows (the appointment type's own when the cell is empty), a note within its limit;
/// 2. the references: the patient must be exactly one registered, active, not merged-away patient with that name and birth date (patients are imported first); the provider, operatory and type must each be one active record
///    with that name (ignoring capitals and spacing); a missing, inactive or ambiguous reference is a problem on that field;
/// 3. the time: a start that exists in the practice time zone and is in the future (<c>start_in_past</c>), and a provider who is working then (<c>provider_unavailable</c>);
/// 4. the schedule: an appointment already on the schedule exactly as the row says (the same patient, provider, operatory and period) is Skipped (<c>already_scheduled</c>, or <c>duplicate_in_file</c> for an earlier row);
///    otherwise the provider, then the operatory, then the patient must not already hold any part of the period, counting the appointments on the schedule and the rows before this one that could still be scheduled
///    (half-open periods, so back-to-back is fine): <c>provider_double_booked</c>, <c>operatory_conflict</c>, <c>patient_double_booked</c>. A conflict with an earlier row names that row. Nothing is ever double-booked.
/// Existing appointments are loaded once for the file's whole time range and intervals are searched in sorted lists, so a large file is not a query for every row. The result for a row is Valid, Rejected or Skipped.
/// Failure modes handled: every value problem, every reference problem, the DST gap or fold, the past, an unavailable provider, conflicts with the schedule and within the file. Not handled: an appointment booked by
/// someone else after the dry-run (the commit schedules through the scheduler, which checks again). No external call is made.
/// </summary>
internal sealed class ImportAppointmentEvaluator(AlveraDbContext db, IPracticeClock clock)
{
    private const int ChunkSize = 1_000;
    private static readonly string[] StartFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm"];

    private sealed record Named(Guid Id, string Name, bool Active, int DefaultMinutes = 0);

    private sealed record PatientRef(Guid Id, bool Active, bool Merged);

    private sealed record Parsed(int RowNumber, string FirstName, string LastName, DateOnly Dob, string Provider, string Operatory, string Type, DateTime Start, int? Duration, Dictionary<string, string> Errors);

    private sealed record Interval(DateTimeOffset Start, DateTimeOffset End, int? Row);

    /// <summary>The periods held by one provider, operatory or patient, sorted by start, searched without scanning them all.</summary>
    private sealed class Held
    {
        private readonly List<Interval> _items = [];
        private TimeSpan _longest = TimeSpan.Zero;

        public void Add(Interval interval)
        {
            var at = _items.BinarySearch(interval, Comparer<Interval>.Create((a, b) => a.Start.CompareTo(b.Start)));
            _items.Insert(at < 0 ? ~at : at, interval);
            if (interval.End - interval.Start > _longest) _longest = interval.End - interval.Start;
        }

        /// <summary>The first held period that overlaps [start, end): any such period starts before <paramref name="end"/> and after start minus the longest period held.</summary>
        public Interval? Overlap(DateTimeOffset start, DateTimeOffset end)
        {
            var at = _items.BinarySearch(new Interval(end, end, null), Comparer<Interval>.Create((a, b) => a.Start.CompareTo(b.Start)));
            for (var i = (at < 0 ? ~at : at) - 1; i >= 0 && _items[i].Start > start - _longest; i--)
                if (_items[i].Start < end && _items[i].End > start) return _items[i];
            return null;
        }
    }

    public async Task<List<ImportRowOutcome>> EvaluateAsync(CsvTable table, IReadOnlyDictionary<string, string> mapping, CancellationToken ct)
    {
        var index = ImportPatientRows.FieldIndex(table, mapping);
        var parsed = new List<Parsed>(table.Rows.Count);
        for (var i = 0; i < table.Rows.Count; i++) parsed.Add(Parse(i + 2, table.Rows[i], index));

        var providers = await LoadAsync(db.ProviderProfiles.AsNoTracking().Select(p => new Named(p.Id, p.StaffProfile!.DisplayName, p.IsActive && p.StaffProfile!.IsActive)), ct);
        var operatories = await LoadAsync(db.Operatories.AsNoTracking().Select(o => new Named(o.Id, o.Name, o.IsActive && (o.Location == null || o.Location.IsActive))), ct);
        var types = await LoadAsync(db.AppointmentTypes.AsNoTracking().Select(t => new Named(t.Id, t.Name, t.IsActive, t.DefaultDurationMinutes)), ct);
        var patients = await LoadPatientsAsync(parsed.Where(p => p.Dob != default).Select(p => p.Dob).Distinct().ToList(), ct);
        var configuration = new SchedulingConfiguration(db, clock);
        var lookups = new AvailabilityLookups();                                         // each provider's hours and blocked times are read once for the whole file

        var candidates = new List<(Parsed Row, Guid Provider, Guid Operatory, Guid Patient, DateTimeOffset Start, DateTimeOffset End)>();
        var outcomes = new List<ImportRowOutcome>(parsed.Count);
        foreach (var row in parsed)
        {
            var errors = row.Errors;
            var provider = Resolve(row.Provider, providers, errors, "provider", "provider");
            var operatory = Resolve(row.Operatory, operatories, errors, "operatory", "operatory");
            var type = Resolve(row.Type, types, errors, "appointmentType", "appointment type");
            var patient = row.Dob == default || row.FirstName.Length == 0 || row.LastName.Length == 0 ? null : ResolvePatient(row, patients, errors);
            if (errors.Count > 0) { outcomes.Add(Rejected(row.RowNumber, ImportRowReason.InvalidFields, errors)); continue; }

            var duration = row.Duration ?? type!.DefaultMinutes;
            DateTimeOffset startUtc;
            try
            {
                startUtc = clock.FromPracticeLocal(row.Start).ToUniversalTime();
            }
            catch (LocalTimeConversionException)
            {
                outcomes.Add(Rejected(row.RowNumber, ImportRowReason.InvalidFields, new() { ["start"] = "That time does not exist or is ambiguous (a daylight-saving change). Choose another time." }));
                continue;
            }

            if (startUtc <= clock.UtcNow) { outcomes.Add(Rejected(row.RowNumber, ImportRowReason.StartInPast, new() { ["start"] = "An appointment must start in the future." })); continue; }
            var availability = await configuration.CheckProviderAvailabilityAsync(provider!.Id, startUtc, duration, ct, lookups);
            if (!availability.Available) { outcomes.Add(Rejected(row.RowNumber, ImportRowReason.ProviderUnavailable, new() { ["provider"] = $"The provider is not available at that time ({availability.Reason})." })); continue; }
            candidates.Add((row, provider.Id, operatory!.Id, patient!.Id, startUtc, startUtc.AddMinutes(duration)));
        }

        outcomes.AddRange(await ScheduleChecksAsync(candidates, ct));
        return outcomes.OrderBy(o => o.RowNumber).ToList();
    }

    private async Task<List<ImportRowOutcome>> ScheduleChecksAsync(List<(Parsed Row, Guid Provider, Guid Operatory, Guid Patient, DateTimeOffset Start, DateTimeOffset End)> candidates, CancellationToken ct)
    {
        var outcomes = new List<ImportRowOutcome>(candidates.Count);
        if (candidates.Count == 0) return outcomes;
        var from = candidates.Min(c => c.Start);
        var to = candidates.Max(c => c.End);
        var existing = await db.Appointments.AsNoTracking().Where(a => a.Status == AppointmentStatuses.Scheduled && a.StartUtc < to && from < a.EndUtc)
            .Select(a => new { a.ProviderProfileId, a.OperatoryId, a.PatientId, a.StartUtc, a.EndUtc }).ToListAsync(ct);

        var byProvider = new Dictionary<Guid, Held>();
        var byOperatory = new Dictionary<Guid, Held>();
        var byPatient = new Dictionary<Guid, Held>();
        var same = new Dictionary<(Guid, Guid, Guid, DateTimeOffset, DateTimeOffset), int?>();          // an appointment exactly as a row says, from the schedule (null) or from an earlier row
        void Hold(Guid provider, Guid operatory, Guid patient, DateTimeOffset start, DateTimeOffset end, int? row)
        {
            var interval = new Interval(start, end, row);
            foreach (var (map, key) in new[] { (byProvider, provider), (byOperatory, operatory), (byPatient, patient) })
            {
                if (!map.TryGetValue(key, out var held)) map[key] = held = new Held();
                held.Add(interval);
            }

            same.TryAdd((provider, operatory, patient, start, end), row);
        }

        foreach (var a in existing) Hold(a.ProviderProfileId, a.OperatoryId, a.PatientId, a.StartUtc, a.EndUtc, null);

        foreach (var (row, provider, operatory, patient, start, end) in candidates)
        {
            if (same.TryGetValue((provider, operatory, patient, start, end), out var earlierRow))
            {
                outcomes.Add(earlierRow is null
                    ? new ImportRowOutcome(row.RowNumber, ImportRowStatus.Skipped, ImportRowReason.AlreadyScheduled, null, null, null)
                    : new ImportRowOutcome(row.RowNumber, ImportRowStatus.Skipped, ImportRowReason.DuplicateInFile, null, null, earlierRow));
                continue;
            }

            var clash = byProvider.GetValueOrDefault(provider)?.Overlap(start, end) is { } p ? (ImportRowReason.ProviderDoubleBooked, "The provider already has an appointment during that time.", p)
                : byOperatory.GetValueOrDefault(operatory)?.Overlap(start, end) is { } o ? (ImportRowReason.OperatoryConflict, "The operatory is already in use during that time.", o)
                : byPatient.GetValueOrDefault(patient)?.Overlap(start, end) is { } q ? (ImportRowReason.PatientDoubleBooked, "The patient already has an appointment during that time.", q)
                : default((string, string, Interval?));
            if (clash.Item3 is { } holder)
            {
                outcomes.Add(new ImportRowOutcome(row.RowNumber, ImportRowStatus.Rejected, clash.Item1, new Dictionary<string, string> { ["schedule"] = clash.Item2 }, null, holder.Row));
                continue;
            }

            Hold(provider, operatory, patient, start, end, row.RowNumber);
            outcomes.Add(new ImportRowOutcome(row.RowNumber, ImportRowStatus.Valid, null, null, null, null));
        }

        return outcomes;
    }

    private static ImportRowOutcome Rejected(int row, string reason, Dictionary<string, string> errors) => new(row, ImportRowStatus.Rejected, reason, errors, null, null);

    private Parsed Parse(int rowNumber, string[] row, IReadOnlyDictionary<string, int> index)
    {
        var errors = new Dictionary<string, string>();
        string? Cell(string field) => index.TryGetValue(field, out var i) && i >= 0 ? row[i] : null;
        string Text(string field, string label, int max)
        {
            var v = Cell(field)?.Trim();
            if (string.IsNullOrEmpty(v)) { errors[field] = $"{label} is required."; return ""; }
            if (v.Length > max) { errors[field] = $"{label} must be {max} characters or fewer."; return ""; }
            return v;
        }

        var first = Text("patientFirstName", "Patient first name", 80);
        var last = Text("patientLastName", "Patient last name", 80);
        var dob = default(DateOnly);
        var dobText = Cell("patientDateOfBirth")?.Trim();
        if (string.IsNullOrEmpty(dobText)) errors["patientDateOfBirth"] = "Patient date of birth is required.";
        else if (!DateOnly.TryParseExact(dobText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)) errors["patientDateOfBirth"] = "Patient date of birth must be a real date in the form yyyy-MM-dd.";

        var provider = Text("provider", "Provider", 200);
        var operatory = Text("operatory", "Operatory", 200);
        var type = Text("appointmentType", "Appointment type", 200);
        var start = default(DateTime);
        var startText = Cell("start")?.Trim();
        if (string.IsNullOrEmpty(startText)) errors["start"] = "Start is required.";
        else if (!DateTime.TryParseExact(startText, StartFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) errors["start"] = "Start must be a real practice time in the form yyyy-MM-dd HH:mm.";

        int? duration = null;
        var durationText = Cell("durationMinutes")?.Trim();
        if (!string.IsNullOrEmpty(durationText))
        {
            if (!int.TryParse(durationText, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)) errors["durationMinutes"] = "Duration must be a whole number of minutes.";
            else
            {
                try { PracticeConfigurationService.ValidateDuration(minutes); duration = minutes; }
                catch (ConfigurationException ex) { errors["durationMinutes"] = ex.Message; }
            }
        }

        try { AppointmentScheduler.NormalizeNotes(Cell("notes")); }
        catch (SchedulingException ex) { errors["notes"] = ex.Message; }
        return new Parsed(rowNumber, first, last, dob, provider, operatory, type, start, duration, errors);
    }

    private static string Key(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static async Task<ILookup<string, Named>> LoadAsync(IQueryable<Named> query, CancellationToken ct) => (await query.ToListAsync(ct)).ToLookup(n => Key(n.Name));

    /// <summary>The one record with this name, or a problem on the field (none, more than one, or inactive).</summary>
    private static Named? Resolve(string name, ILookup<string, Named> lookup, Dictionary<string, string> errors, string field, string label)
    {
        if (name.Length == 0 || errors.ContainsKey(field)) return null;
        var found = lookup[Key(name)].ToList();
        if (found.Count == 0) { errors[field] = $"No {label} has this name."; return null; }
        if (found.Count > 1) { errors[field] = $"More than one {label} has this name."; return null; }
        if (!found[0].Active) { errors[field] = $"This {label} is inactive."; return null; }
        return found[0];
    }

    private static PatientRef? ResolvePatient(Parsed row, ILookup<string, PatientRef> patients, Dictionary<string, string> errors)
    {
        if (errors.ContainsKey("patientFirstName") || errors.ContainsKey("patientLastName") || errors.ContainsKey("patientDateOfBirth")) return null;
        var found = patients[Patient.BuildDuplicateKey(row.FirstName, row.LastName, row.Dob)].ToList();
        if (found.Count == 0) { errors["patient"] = "No registered patient has this name and date of birth. Import or register the patient first."; return null; }
        if (found.Count > 1) { errors["patient"] = "More than one registered patient has this name and date of birth."; return null; }
        if (found[0].Merged) { errors["patient"] = "This patient's record was merged into another one."; return null; }
        if (!found[0].Active) { errors["patient"] = "This patient is inactive."; return null; }
        return found[0];
    }

    private async Task<ILookup<string, PatientRef>> LoadPatientsAsync(IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var all = new List<(string Key, PatientRef Ref)>();
        foreach (var chunk in dates.Chunk(ChunkSize))
        {
            var found = await db.Patients.AsNoTracking().Where(p => chunk.Contains(p.DateOfBirth)).Select(p => new { p.Id, p.FirstName, p.LastName, p.DateOfBirth, p.IsActive, Merged = p.MergedAtUtc != null }).ToListAsync(ct);
            all.AddRange(found.Select(p => (Patient.BuildDuplicateKey(p.FirstName, p.LastName, p.DateOfBirth), new PatientRef(p.Id, p.IsActive, p.Merged))));
        }

        return all.ToLookup(p => p.Key, p => p.Ref);
    }
}
