using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One metric for one period. <see cref="DenominatorCount"/> is the sample size; <see cref="Rate"/> is <c>null</c> (unavailable) when it is zero, never zero. Rounded to four decimal places.</summary>
public sealed record MetricValue(string Key, string Name, int DefinitionVersion, string Numerator, string Denominator, string Source, bool LowerIsBetter, string? SupportingRoute, int NumeratorCount, int DenominatorCount, decimal? Rate);

/// <summary>The measured metrics of a period, the things that cannot be measured (with the reason) and the sentence that a difference is not a cause.</summary>
public sealed record MeasurementData(DateTimeOffset AsOfUtc, IReadOnlyList<MetricValue> Metrics, IReadOnlyList<NotMeasurable> NotMeasurable, string Note);

/// <summary>
/// ALV-N006 slice 5a: the measured metrics of a period, from the application's own authoritative records (the ledger, appointments, tasks, encounters, merges) and, for scheduling conflicts, the privacy-safe measurement events. Each
/// metric follows its <see cref="MetricDefinition"/> exactly. Metrics that depend on the moment (the follow-up and note metrics) are judged at <c>asOf</c>. Practice-wide: a provider or location filter is refused
/// (<c>filter_not_supported</c>). Failure modes handled: more rows than the limit (413), an audit entry that cannot be saved (503). The run only reads.
/// </summary>
public class MetricsService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<MetricsService> log)
{
    public const int DefinitionVersion = 1;

    /// <summary>All the metrics for [start, end), without checks on the caller (used by the report and, in slice 5b, to take a baseline).</summary>
    public async Task<IReadOnlyList<MetricValue>> ComputeAsync(DateTimeOffset start, DateTimeOffset end, DateTimeOffset asOf, CancellationToken ct)
    {
        var values = new List<MetricValue>
        {
            Value(MetricKeys.SchedulingConflicts, await ConflictsAsync(start, end, ct)),
            Value(MetricKeys.BillingCorrections, await CorrectionsAsync(start, end, ct)),
            Value(MetricKeys.DuplicateMerges, await MergesAsync(start, end, ct)),
            Value(MetricKeys.UnfinalizedNotes, await NotesAsync(start, end, asOf, ct)),
            Value(MetricKeys.LateFollowUps, await FollowUpsAsync(start, end, asOf, ct)),
        };
        var (cancelled, noShow, booked) = await AppointmentsAsync(start, end, ct);
        values.Add(Value(MetricKeys.CancellationRate, (cancelled, booked)));
        values.Add(Value(MetricKeys.NoShowRate, (noShow, booked)));
        return values;
    }

    public async Task<ReportResult<MeasurementData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct, DateTimeOffset? asOfUtc = null)
    {
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "Measurement is practice-wide and cannot be filtered by provider or location.", 400);
        var asOf = asOfUtc ?? clock.UtcNow;
        string consistency;
        IReadOnlyList<MetricValue> metrics;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            metrics = await ComputeAsync(filter.StartUtc, filter.EndUtc, asOf, ct);
        }

        var records = metrics.Sum(m => m.DenominatorCount);
        await ReportSupport.AuditAsync(db, log, ReportIds.Measurement, false, filter, records, actor, ct);
        return new ReportResult<MeasurementData>(ReportIds.Measurement, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, records, records == 0, new MeasurementData(asOf, metrics, MetricDefinitions.Unmeasurable, MetricDefinitions.NoCausationNote));
    }

    private static MetricValue Value(string key, (int Numerator, int Denominator) counts)
    {
        var d = MetricDefinitions.Of(key);
        return new MetricValue(d.Key, d.Name, d.Version, d.Numerator, d.Denominator, d.Source, d.LowerIsBetter, d.SupportingRoute, counts.Numerator, counts.Denominator, counts.Denominator == 0 ? null : Math.Round(counts.Numerator / (decimal)counts.Denominator, 4));
    }

    private async Task<(int, int)> ConflictsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var inRange = db.MeasurementEvents.AsNoTracking().Where(e => e.OccurredAtUtc >= start && e.OccurredAtUtc < end);
        var scheduled = await inRange.CountAsync(e => e.EventName == "appointment.scheduled", ct);
        var rejected = await inRange.Where(e => e.EventName == "appointment.rejected").Select(e => e.PropertiesJson).ToListAsync(ct);
        ReportSupport.GuardSize(rejected.Count, options.Value);
        var conflicts = rejected.Count(json => CategoryOf(json) is { } c && MetricDefinitions.ConflictCodes.Contains(c));
        return (conflicts, scheduled + rejected.Count);
    }

    private static string? CategoryOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        }
        catch (JsonException)
        {
            return null;        // a stored event that is not valid JSON is not a conflict and is not counted as one
        }
    }

    private async Task<(int, int)> CorrectionsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var ledger = new FinancialLedger(db);
        var lines = await ledger.WholeLedgerAsync(start, end, await ledger.OpeningBatchIdsAsync(ct), ct);
        int Entries(string line) => lines.Lines.Single(l => l.Line == line).Entries;
        var reversals = Entries(FinancialLines.ChargeReversals) + Entries(FinancialLines.PaymentReversals) + Entries(FinancialLines.RefundReversals) + Entries(FinancialLines.AdjustmentReversals);
        var operations = Entries(FinancialLines.Charges) + Entries(FinancialLines.Payments) + Entries(FinancialLines.Refunds) + Entries(FinancialLines.Adjustments);
        return (reversals, operations);
    }

    private async Task<(int, int)> MergesAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        (await db.PatientMerges.AsNoTracking().CountAsync(m => m.MergedAtUtc >= start && m.MergedAtUtc < end, ct),
         await db.Patients.AsNoTracking().CountAsync(p => p.CreatedAtUtc >= start && p.CreatedAtUtc < end, ct));

    private async Task<(int, int)> NotesAsync(DateTimeOffset start, DateTimeOffset end, DateTimeOffset asOf, CancellationToken ct)
    {
        var oldEnough = asOf.AddDays(-MetricDefinitions.NoteGraceDays);
        var notes = db.Encounters.AsNoTracking().Where(e => e.EncounterAtUtc >= start && e.EncounterAtUtc < end && e.EncounterAtUtc < oldEnough);
        return (await notes.CountAsync(e => e.Status != EncounterStatuses.Finalized, ct), await notes.CountAsync(ct));
    }

    private async Task<(int, int)> FollowUpsAsync(DateTimeOffset start, DateTimeOffset end, DateTimeOffset asOf, CancellationToken ct)
    {
        var due = new FollowUpQueries(db).Tasks(start, end, asOf).Where(t => t.DueAtUtc < asOf);
        return (await due.CountAsync(t => t.Overdue || t.Late, ct), await due.CountAsync(ct));
    }

    private async Task<(int Cancelled, int NoShow, int Booked)> AppointmentsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var all = db.Appointments.AsNoTracking().Where(a => a.StartUtc >= start && a.StartUtc < end);
        return (await all.CountAsync(a => a.Status == AppointmentStatuses.Cancelled, ct), await all.CountAsync(a => a.Status == AppointmentStatuses.NoShow, ct), await all.CountAsync(ct));
    }
}
