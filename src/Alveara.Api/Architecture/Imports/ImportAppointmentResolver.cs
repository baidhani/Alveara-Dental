using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A row read and resolved to the people and places it names: the scheduler's own request, the start as an instant and the duration that will apply.</summary>
internal sealed record ResolvedAppointment(int RowNumber, ScheduleAppointmentRequest Request, DateTimeOffset StartUtc, int DurationMinutes);

/// <summary>What reading one row gave: the resolved appointment, or the rejection (a problem with the row's own values or references).</summary>
internal sealed record ResolutionOutcome(int RowNumber, ResolvedAppointment? Resolved, ImportRowOutcome? Rejection);

/// <summary>
/// ALV-018-C02 slice 4: reads the rows of an appointment file and resolves them, shared by the dry-run (<see cref="ImportAppointmentEvaluator"/>) and the commit (<see cref="ImportAppointmentCommit"/>) so they can never read a row
/// differently. It stops short of the scheduling decisions, which belong to the scheduler (a start in the past, the provider's hours, conflicts). What it does, in the scheduler's own order and with nothing written:
/// 1. the row's own values (all problems at once, as fixed messages, never a value from the file): the patient's names and birth date, the provider, operatory and appointment type, a real practice-local start
///    (<c>yyyy-MM-dd HH:mm</c> or with a T), a duration the practice allows (the appointment type's own when the cell is empty), a note within its limit;
/// 2. the references: the patient must be exactly one registered, active, not merged-away patient with that name and birth date (patients are imported first); the provider, operatory and type must each be one active record
///    with that name (ignoring capitals and spacing); a missing, inactive or ambiguous reference is a problem on that field;
/// 3. the start must exist in the practice time zone (a daylight-saving gap, or a fold that happens twice, is refused).
/// Reference data is loaded once, not per row; only the patients with the birth dates of the rows asked for are loaded. No external call is made.
/// </summary>
internal sealed class ImportAppointmentResolver(AlveraDbContext db, IPracticeClock clock)
{
    private const int ChunkSize = 1_000;
    private static readonly string[] StartFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm"];

    private sealed record Named(Guid Id, string Name, bool Active, int DefaultMinutes = 0);

    private sealed record PatientRef(Guid Id, bool Active, bool Merged);

    private sealed record Parsed(int RowNumber, string FirstName, string LastName, DateOnly Dob, string Provider, string Operatory, string Type, DateTime Start, int? Duration, Dictionary<string, string> Errors);

    /// <summary>Resolves every row of the table, or only the rows whose numbers are given (the header is row 1).</summary>
    public async Task<List<ResolutionOutcome>> ResolveAsync(CsvTable table, IReadOnlyDictionary<string, string> mapping, IReadOnlySet<int>? onlyRows, CancellationToken ct)
    {
        var index = ImportPatientRows.FieldIndex(table, mapping);
        var parsed = new List<Parsed>();
        for (var i = 0; i < table.Rows.Count; i++)
            if (onlyRows is null || onlyRows.Contains(i + 2)) parsed.Add(Parse(i + 2, table.Rows[i], index));

        var providers = await LoadAsync(db.ProviderProfiles.AsNoTracking().Select(p => new Named(p.Id, p.StaffProfile!.DisplayName, p.IsActive && p.StaffProfile!.IsActive)), ct);
        var operatories = await LoadAsync(db.Operatories.AsNoTracking().Select(o => new Named(o.Id, o.Name, o.IsActive && (o.Location == null || o.Location.IsActive))), ct);
        var types = await LoadAsync(db.AppointmentTypes.AsNoTracking().Select(t => new Named(t.Id, t.Name, t.IsActive, t.DefaultDurationMinutes)), ct);
        var patients = await LoadPatientsAsync(parsed.Where(p => p.Dob != default).Select(p => p.Dob).Distinct().ToList(), ct);

        var outcomes = new List<ResolutionOutcome>(parsed.Count);
        foreach (var row in parsed)
        {
            var errors = row.Errors;
            var provider = Resolve(row.Provider, providers, errors, "provider", "provider");
            var operatory = Resolve(row.Operatory, operatories, errors, "operatory", "operatory");
            var type = Resolve(row.Type, types, errors, "appointmentType", "appointment type");
            var patient = row.Dob == default || row.FirstName.Length == 0 || row.LastName.Length == 0 ? null : ResolvePatient(row, patients, errors);
            if (errors.Count > 0) { outcomes.Add(Reject(row.RowNumber, ImportRowReason.InvalidFields, errors)); continue; }

            DateTimeOffset startUtc;
            try
            {
                startUtc = clock.FromPracticeLocal(row.Start).ToUniversalTime();
            }
            catch (LocalTimeConversionException)
            {
                outcomes.Add(Reject(row.RowNumber, ImportRowReason.InvalidFields, new() { ["start"] = "That time does not exist or is ambiguous (a daylight-saving change). Choose another time." }));
                continue;
            }

            var duration = row.Duration ?? type!.DefaultMinutes;
            outcomes.Add(new ResolutionOutcome(row.RowNumber, new ResolvedAppointment(row.RowNumber, new ScheduleAppointmentRequest(patient!.Id, provider!.Id, operatory!.Id, type!.Id, row.Start, row.Duration, NotesOf(table.Rows[row.RowNumber - 2], index)), startUtc, duration), null));
        }

        return outcomes;
    }

    private static string? NotesOf(string[] row, IReadOnlyDictionary<string, int> index) => index.TryGetValue("notes", out var i) && i >= 0 ? AppointmentScheduler.NormalizeNotes(row[i]) : null;

    private static ResolutionOutcome Reject(int row, string reason, Dictionary<string, string> errors) => new(row, null, new ImportRowOutcome(row, ImportRowStatus.Rejected, reason, errors, null, null));

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
