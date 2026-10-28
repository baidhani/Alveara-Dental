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
        var resolved = await new ImportAppointmentResolver(db, clock).ResolveAsync(table, mapping, null, ct);
        var configuration = new SchedulingConfiguration(db, clock);
        var lookups = new AvailabilityLookups();                                         // each provider's hours and blocked times are read once for the whole file

        var candidates = new List<(int Row, Guid Provider, Guid Operatory, Guid Patient, DateTimeOffset Start, DateTimeOffset End)>();
        var outcomes = new List<ImportRowOutcome>(resolved.Count);
        foreach (var r in resolved)
        {
            if (r.Rejection is not null) { outcomes.Add(r.Rejection); continue; }
            var a = r.Resolved!;
            if (a.StartUtc <= clock.UtcNow) { outcomes.Add(Rejected(a.RowNumber, ImportRowReason.StartInPast, new() { ["start"] = "An appointment must start in the future." })); continue; }
            var availability = await configuration.CheckProviderAvailabilityAsync(a.Request.ProviderId, a.StartUtc, a.DurationMinutes, ct, lookups);
            if (!availability.Available) { outcomes.Add(Rejected(a.RowNumber, ImportRowReason.ProviderUnavailable, new() { ["provider"] = $"The provider is not available at that time ({availability.Reason})." })); continue; }
            candidates.Add((a.RowNumber, a.Request.ProviderId, a.Request.OperatoryId, a.Request.PatientId, a.StartUtc, a.StartUtc.AddMinutes(a.DurationMinutes)));
        }

        outcomes.AddRange(await ScheduleChecksAsync(candidates, ct));
        return outcomes.OrderBy(o => o.RowNumber).ToList();
    }

    private async Task<List<ImportRowOutcome>> ScheduleChecksAsync(List<(int Row, Guid Provider, Guid Operatory, Guid Patient, DateTimeOffset Start, DateTimeOffset End)> candidates, CancellationToken ct)
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
                    ? new ImportRowOutcome(row, ImportRowStatus.Skipped, ImportRowReason.AlreadyScheduled, null, null, null)
                    : new ImportRowOutcome(row, ImportRowStatus.Skipped, ImportRowReason.DuplicateInFile, null, null, earlierRow));
                continue;
            }

            var clash = byProvider.GetValueOrDefault(provider)?.Overlap(start, end) is { } p ? (ImportRowReason.ProviderDoubleBooked, "The provider already has an appointment during that time.", p)
                : byOperatory.GetValueOrDefault(operatory)?.Overlap(start, end) is { } o ? (ImportRowReason.OperatoryConflict, "The operatory is already in use during that time.", o)
                : byPatient.GetValueOrDefault(patient)?.Overlap(start, end) is { } q ? (ImportRowReason.PatientDoubleBooked, "The patient already has an appointment during that time.", q)
                : default((string, string, Interval?));
            if (clash.Item3 is { } holder)
            {
                outcomes.Add(new ImportRowOutcome(row, ImportRowStatus.Rejected, clash.Item1, new Dictionary<string, string> { ["schedule"] = clash.Item2 }, null, holder.Row));
                continue;
            }

            Hold(provider, operatory, patient, start, end, row);
            outcomes.Add(new ImportRowOutcome(row, ImportRowStatus.Valid, null, null, null, null));
        }

        return outcomes;
    }

    private static ImportRowOutcome Rejected(int row, string reason, Dictionary<string, string> errors) => new(row, ImportRowStatus.Rejected, reason, errors, null, null);
}
