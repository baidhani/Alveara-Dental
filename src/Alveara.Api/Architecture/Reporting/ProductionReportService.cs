using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>Production by one provider. <see cref="ProviderProfileId"/> is null for charges whose completed procedure is not known ("Not attributed"). Amounts are money: <see cref="Gross"/> charged, <see cref="Reversed"/> undone, <see cref="Net"/> the difference.</summary>
public sealed record ProductionProviderRow(Guid? ProviderProfileId, string ProviderName, int Charges, decimal Gross, decimal Reversed, decimal Net);

public sealed record ProductionCodeRow(string ProcedureCode, int Charges, decimal Gross, decimal Reversed, decimal Net);

/// <summary>
/// The production and charges report. <see cref="NetProduction"/> = <see cref="GrossCharges"/> - <see cref="ChargesReversed"/>. <see cref="Ledger"/> shows every line of the ledger for the period (so adjustments, collections and
/// loaded opening balances are visible beside production, never mixed into it) and the proof that the lines add up to the ledger. With a provider filter, only that production is reported and the proof covers it alone.
/// </summary>
public sealed record ProductionData(int ChargeEntries, decimal GrossCharges, decimal ChargesReversed, decimal NetProduction, IReadOnlyList<ProductionProviderRow> Providers, IReadOnlyList<ProductionCodeRow> Codes, FinancialLedgerLines Ledger);

/// <summary>
/// ALV-N006 slice 2: production and charges. Source: the ledger's Charge entries recorded in the range and the reversals of charges, dated by when they were recorded (not by the date of the procedure). A provider is the one
/// currently credited with the completed procedure that the charge bills (<see cref="FinancialLedger.EffectiveProviders"/>). The provider filter narrows production to those providers; the location filter is refused
/// (<c>filter_not_supported</c>) because a ledger entry has no location. Failure modes handled: nothing in the range (<c>NoData</c>), more rows than the limit (413), an unreconciled ledger is reported as such, an audit entry that
/// cannot be saved (503). Definition version 1.
/// </summary>
public class ProductionReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<ProductionReportService> log)
{
    public const int DefinitionVersion = 1;
    private const string NotAttributed = "Not attributed";

    public async Task<ReportResult<ProductionData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        if (filter.LocationId is not null) throw new ReportException("filter_not_supported", "Financial reports cannot be filtered by location: a ledger entry has none.", 400);
        var ledger = new FinancialLedger(db);
        string consistency;
        ProductionData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var batches = await ledger.OpeningBatchIdsAsync(ct);
            var classified = ledger.Classified(filter.StartUtc, filter.EndUtc, batches);
            var rows = await classified.Where(c => c.Line == FinancialLines.Charges || c.Line == FinancialLines.ChargeReversals)
                .GroupBy(c => new { c.PlanItemId, c.ProcedureCode, c.Line }).Select(g => new { g.Key.PlanItemId, g.Key.ProcedureCode, g.Key.Line, Entries = g.Count(), Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
            ReportSupport.GuardSize(rows.Count, options.Value);

            var itemIds = rows.Where(r => r.PlanItemId != null).Select(r => r.PlanItemId!.Value).Distinct().ToArray();
            var providerOf = itemIds.Length == 0 ? [] : await ledger.EffectiveProviders().Where(p => itemIds.Contains(p.PlanItemId)).ToDictionaryAsync(p => p.PlanItemId, p => p.ProviderProfileId, ct);
            Guid? ProviderFor(Guid? item) => item is { } id && providerOf.TryGetValue(id, out var p) ? p : null;

            var chosen = filter.ProviderIds.ToHashSet();
            var kept = chosen.Count == 0 ? rows : rows.Where(r => ProviderFor(r.PlanItemId) is { } p && chosen.Contains(p)).ToList();
            var attributed = providerOf.Values.Distinct().ToArray();
            var names = await db.ProviderProfiles.AsNoTracking().Where(p => attributed.Contains(p.Id)).Select(p => new { p.Id, Name = p.StaffProfile!.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            var providers = kept.GroupBy(r => ProviderFor(r.PlanItemId)).Select(g => Provider(g.Key, g.Key is { } id ? names.GetValueOrDefault(id, "Unknown provider") : NotAttributed, g.Select(r => (r.Line, r.Entries, r.Amount)))).OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ProviderProfileId).ToList();
            var codes = kept.GroupBy(r => r.ProcedureCode ?? "(no code)").Select(g => Code(g.Key, g.Select(r => (r.Line, r.Entries, r.Amount)))).OrderBy(c => c.ProcedureCode, StringComparer.Ordinal).ToList();
            var gross = kept.Where(r => r.Line == FinancialLines.Charges).Sum(r => r.Amount);
            var reversed = -kept.Where(r => r.Line == FinancialLines.ChargeReversals).Sum(r => r.Amount);
            var entries = kept.Where(r => r.Line == FinancialLines.Charges).Sum(r => r.Entries);

            var proof = chosen.Count == 0 ? await ledger.WholeLedgerAsync(filter.StartUtc, filter.EndUtc, batches, ct) : await ProviderProofAsync(ledger, filter, kept.Select(r => (r.Line, r.Entries, r.Amount)).ToList(), chosen.ToArray(), ct);
            data = new ProductionData(entries, gross, reversed, gross - reversed, providers, codes, proof);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.Production, false, filter, data.ChargeEntries, actor, ct);
        return new ReportResult<ProductionData>(ReportIds.Production, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.ChargeEntries, data.ChargeEntries == 0, data);
    }

    /// <summary>With a provider filter the proof covers the filtered production only: the lines against an independent sum of those providers' charges and the reversals of those charges.</summary>
    private async Task<FinancialLedgerLines> ProviderProofAsync(FinancialLedger ledger, ReportFilter filter, List<(string Line, int Entries, decimal Amount)> kept, Guid[] providers, CancellationToken ct)
    {
        var lines = FinancialLines.Production.Select(l => new FinancialLineTotal(l, kept.Where(k => k.Line == l).Sum(k => k.Entries), kept.Where(k => k.Line == l).Sum(k => k.Amount))).ToList();
        var items = ledger.EffectiveProviders().Where(p => providers.Contains(p.ProviderProfileId)).Select(p => p.PlanItemId);
        var charges = db.BillingEntries.AsNoTracking().Where(e => e.Kind == BillingKinds.Charge && e.PlanItemId != null && items.Contains(e.PlanItemId.Value)).Select(e => e.Id);
        var scope = db.BillingEntries.AsNoTracking().Where(e => e.RecordedAtUtc >= filter.StartUtc && e.RecordedAtUtc < filter.EndUtc
            && ((e.Kind == BillingKinds.Charge && e.PlanItemId != null && items.Contains(e.PlanItemId.Value)) || (e.Kind == BillingKinds.Reversal && e.ReversesEntryId != null && charges.Contains(e.ReversesEntryId.Value))));
        return FinancialLedger.Proof(lines, await scope.SumAsync(e => e.Amount, ct), await scope.CountAsync(ct), "ProviderProduction");
    }

    private static ProductionProviderRow Provider(Guid? id, string name, IEnumerable<(string Line, int Entries, decimal Amount)> rows)
    {
        var list = rows.ToList();
        var gross = list.Where(r => r.Line == FinancialLines.Charges).Sum(r => r.Amount);
        var reversed = -list.Where(r => r.Line == FinancialLines.ChargeReversals).Sum(r => r.Amount);
        return new ProductionProviderRow(id, name, list.Where(r => r.Line == FinancialLines.Charges).Sum(r => r.Entries), gross, reversed, gross - reversed);
    }

    private static ProductionCodeRow Code(string code, IEnumerable<(string Line, int Entries, decimal Amount)> rows)
    {
        var list = rows.ToList();
        var gross = list.Where(r => r.Line == FinancialLines.Charges).Sum(r => r.Amount);
        var reversed = -list.Where(r => r.Line == FinancialLines.ChargeReversals).Sum(r => r.Amount);
        return new ProductionCodeRow(code, list.Where(r => r.Line == FinancialLines.Charges).Sum(r => r.Entries), gross, reversed, gross - reversed);
    }
}
