using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>What the caller sends to set a baseline: the practice's own label and the first and last day (practice-local, <c>yyyy-MM-dd</c>) of a completed period.</summary>
public sealed record SetBaselineInput(string? Name, string? From, string? To);

public sealed record BaselineMetricView(string MetricKey, int DefinitionVersion, int NumeratorCount, int DenominatorCount, decimal? Rate);

public sealed record BaselineView(Guid Id, string Name, string From, string To, string TimeZoneId, DateTimeOffset AsOfUtc, DateTimeOffset SetAtUtc, Guid SetByUserId, IReadOnlyList<BaselineMetricView> Metrics);

/// <summary><see cref="Created"/> is false when the same key had already made this exact baseline (a repeat of the request).</summary>
public sealed record SetBaselineResult(BaselineView Baseline, bool Created);

/// <summary>
/// ALV-N006 slice 5b: sets, lists and reads baselines. A baseline is a COMPLETED period (it ends before today in practice time) measured once and stored with the definition version of each metric, never changed. The same
/// idempotency key with the same request returns the baseline already made; with a different request it is refused (409 <c>key_reused</c>). The audit entry carries the period and the number of metrics, never the name.
/// Failure modes handled: a missing or over-long key or name, a bad or over-long range, a period that is not over yet (<c>period_not_complete</c>), a repeated request, two simultaneous identical requests (the unique key
/// decides, the loser returns the winner's baseline), a save that fails (503, nothing stored).
/// </summary>
public class MeasurementBaselineService(AlveraDbContext db, ReportFilterResolver resolver, MetricsService metrics, IPracticeClock clock, ILogger<MeasurementBaselineService> log)
{
    public async Task<SetBaselineResult> SetAsync(string? key, SetBaselineInput input, Guid actor, CancellationToken ct)
    {
        key = key?.Trim();
        if (string.IsNullOrEmpty(key)) throw new ReportException("idempotency_key_required", "Send an Idempotency-Key so a repeated request cannot make a second baseline.", 400);
        if (key.Length > BaselineLimits.KeyMax) throw new ReportException("idempotency_key_too_long", $"The key can be at most {BaselineLimits.KeyMax} characters.", 400);
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ReportException("name_required", "Give the baseline a name.", 400);
        if (name.Length > BaselineLimits.NameMax) throw new ReportException("name_too_long", $"The name can be at most {BaselineLimits.NameMax} characters.", 400);
        var filter = await resolver.ResolveAsync(new ReportFilterInput(input.From, input.To, null, null), ct);

        var existing = await FindByKeyAsync(key, ct);
        if (existing is not null) return Replay(existing, name, filter);

        var today = DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime);
        if (filter.To >= today) throw new ReportException("period_not_complete", "A baseline must be a period that is already over. Choose days before today.", 400);

        var now = clock.UtcNow;
        IReadOnlyList<MetricValue> values;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
            values = await metrics.ComputeAsync(filter.StartUtc, filter.EndUtc, now, ct);

        var baseline = new ReportBaseline
        {
            Id = Guid.NewGuid(), Name = name, PeriodFrom = filter.From, PeriodTo = filter.To, PeriodStartUtc = filter.StartUtc, PeriodEndUtc = filter.EndUtc, TimeZoneId = filter.TimeZoneId, AsOfUtc = now,
            IdempotencyKey = key, SetByUserId = actor, SetAtUtc = now,
            Metrics = [.. values.Select(v => new ReportBaselineMetric { Id = Guid.NewGuid(), MetricKey = v.Key, DefinitionVersion = v.DefinitionVersion, NumeratorCount = v.NumeratorCount, DenominatorCount = v.DenominatorCount, Rate = v.Rate })],
        };
        db.ReportBaselines.Add(baseline);
        AuditService.Record(db, AuditEventTypes.ReportBaselineSet, nameof(ReportBaseline), baseline.Id, actor, $"Report baseline set for {filter.From:yyyy-MM-dd} to {filter.To:yyyy-MM-dd}: {baseline.Metrics.Count} metric(s) stored.", null, baseline.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(key, ct);                                  // a simultaneous identical request may have won the unique key
            if (winner is not null) return Replay(winner, name, filter);
            log.LogError("event=report_baseline_save_failed outcome=failure error_class={ErrorClass}", ex.GetType().Name);
            throw new ReportException("save_failed", "The baseline could not be saved. Nothing was stored. Try again.", 503);
        }

        return new SetBaselineResult(View(baseline), true);
    }

    public async Task<IReadOnlyList<BaselineView>> ListAsync(int? limit, CancellationToken ct)
    {
        var take = limit is null ? 50 : limit is >= 1 and <= 200 ? limit.Value : throw new ReportException("invalid_limit", "The page size must be between 1 and 200.", 400);
        var rows = await db.ReportBaselines.AsNoTracking().Include(b => b.Metrics).OrderByDescending(b => b.SetAtUtc).ThenBy(b => b.Id).Take(take).ToListAsync(ct);
        return rows.Select(View).ToList();
    }

    public async Task<BaselineView> GetAsync(Guid id, CancellationToken ct) =>
        View(await db.ReportBaselines.AsNoTracking().Include(b => b.Metrics).SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new ReportException("baseline_not_found", "There is no baseline with that id.", 404));

    private Task<ReportBaseline?> FindByKeyAsync(string key, CancellationToken ct) => db.ReportBaselines.AsNoTracking().Include(b => b.Metrics).SingleOrDefaultAsync(b => b.IdempotencyKey == key, ct);

    private static SetBaselineResult Replay(ReportBaseline existing, string name, ReportFilter filter) =>
        existing.Name == name && existing.PeriodFrom == filter.From && existing.PeriodTo == filter.To
            ? new SetBaselineResult(View(existing), false)
            : throw new ReportException("key_reused", "That Idempotency-Key was used for a different baseline.", 409);

    internal static BaselineView View(ReportBaseline b) =>
        new(b.Id, b.Name, b.PeriodFrom.ToString("yyyy-MM-dd"), b.PeriodTo.ToString("yyyy-MM-dd"), b.TimeZoneId, b.AsOfUtc, b.SetAtUtc, b.SetByUserId,
            [.. b.Metrics.OrderBy(m => m.MetricKey, StringComparer.Ordinal).Select(m => new BaselineMetricView(m.MetricKey, m.DefinitionVersion, m.NumeratorCount, m.DenominatorCount, m.Rate))]);
}
