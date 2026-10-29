using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 2a, the production and collections reports and the ledger entries behind them, against a real SQL Server and a ledger made only through the real services. Ann's account: three procedures completed
/// (two by Dr. One, one by Dr. Two; fee 100 each, so three charges), a cash payment of 150 and a card payment of 40, the card payment reversed, 20 refunded from the cash payment, a late fee of 10, and the third charge reversed;
/// Bo has a 75 opening balance loaded by an import batch. The lines add up to the ledger (net change 155) and to an independent sum of it; the opening balance is its own line and never production or collections; production
/// is split by the provider currently credited (an amendment moves it) and by procedure code; collections are in money by method; the provider filter narrows production and is refused for collections; and every drill-down total
/// equals the cell it came from, pages never repeat or skip, and bad criteria get stable codes.
/// </summary>
public class FinancialReportsTests : CompletionTestBase
{
    private ProviderProfile _one = null!, _two = null!;
    private Guid _batch;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _one = await ProviderAsync("Dr. One");
        _two = await ProviderAsync("Dr. Two");
    }

    /// <summary>Entries 1 to 3 are the charges, 4 the cash payment (150), 5 the card payment (40), 6 reverses 5, 7 refunds 20 of 4, 8 is a +10 adjustment, 9 reverses charge 3.</summary>
    private async Task ArrangeLedgerAsync()
    {
        var plan = await AcceptedPlanAsync(3);
        async Task<PlanView> CompleteAsync(PlanView p, int item, ProviderProfile who, string key)
        {
            await Completion(s => s.CompleteAsync(p.Id, RequestFor(p, [item], AnnEncounter, who.Id, key), S.Actor, default));
            return await PlanNowAsync(p.Id);
        }
        plan = await CompleteAsync(plan, 0, _one, "set-1");
        plan = await CompleteAsync(plan, 1, _two, "set-2");
        await CompleteAsync(plan, 2, _one, "set-3");

        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        account = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(150m, "Cash", null, account.RowVersion, "pay-cash"), S.Actor, default));
        account = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(40m, "Card", null, account.RowVersion, "pay-card"), S.Actor, default));
        account = await Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(5, "Entered in error", account.RowVersion, "rev-card"), S.Actor, default));
        account = await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(4, 20m, "Cash", "Patient request", account.RowVersion, "refund-1"), S.Actor, default));
        account = await Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(10m, "Late fee", account.RowVersion, "adj-1"), S.Actor, default));
        await Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(3, "Entered in error", account.RowVersion, "rev-charge"), S.Actor, default));

        _batch = Guid.NewGuid();
        await using (var db = Fixture.CreateContext())
        {
            db.ImportBatches.Add(new ImportBatch
            {
                Id = _batch, SourceKind = ImportSourceKind.OpeningBalances, SourceLabel = "Old system", Status = ImportBatchStatus.Committed, Policy = ImportBatchPolicy.CommitValidRows, FileBlobId = Guid.NewGuid(),
                FileSha256 = new string('a', 64), FileSizeBytes = 100, RowCount = 1, ColumnCount = 3, ColumnNamesJson = "[\"A\"]", CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await Bill(s => s.RecordOpeningBalanceAsync(Bo, 75m, "ob-1", _batch, S.Actor, default));
    }

    private ReportFilterResolver Resolver(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()));

    private async Task<ReportFilter> TodayAsync(AlveraDbContext db, Guid[]? providers = null, Guid? location = null)
    {
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime);
        return await Resolver(db).ResolveAsync(new ReportFilterInput(today.AddDays(-1).ToString("yyyy-MM-dd"), today.AddDays(1).ToString("yyyy-MM-dd"), providers, location), default);
    }

    private ProductionReportService Production(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()), NullLogger<ProductionReportService>.Instance);
    private CollectionsReportService Collections(AlveraDbContext db) => new(db, Clock, NullLogger<CollectionsReportService>.Instance);
    private FinancialDrillService Drill(AlveraDbContext db, ReportOptions? options = null) => new(db, Options.Create(options ?? new ReportOptions()), NullLogger<FinancialDrillService>.Instance);
    private static readonly FinancialDrillCriteria None = new(null, null, null, null, null, null);

    private static decimal Line(FinancialLedgerLines ledger, string line) => ledger.Lines.Single(l => l.Line == line).Amount;

    [Fact]
    public async Task The_lines_add_up_to_the_ledger_and_the_opening_balance_is_its_own_line()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var result = await Production(db).RunAsync(await TodayAsync(db), S.Actor, default);

        var ledger = result.Data.Ledger;
        Assert.Equal(FinancialLines.All, ledger.Lines.Select(l => l.Line));
        Assert.Equal(new[] { 300m, -100m, -190m, 40m, 20m, 0m, 10m, 0m, 75m, 0m }, ledger.Lines.Select(l => l.Amount));
        Assert.Equal((155m, 155m, true, "WholeLedger"), (ledger.LinesTotal, ledger.LedgerTotal, ledger.Reconciled, ledger.Scope));
        Assert.Equal((10, 10), (ledger.LinesEntries, ledger.LedgerEntries));         // Ann's nine entries and Bo's opening balance: the counts agree
        Assert.Equal(await db.BillingEntries.CountAsync(), ledger.LedgerEntries);
        Assert.Equal(0, ledger.Lines.Single(l => l.Line == FinancialLines.Unclassified).Entries);
        Assert.Equal(75m, Line(ledger, FinancialLines.OpeningBalances));
    }

    [Fact]
    public async Task Production_is_gross_charges_less_reversals_split_by_the_provider_credited_and_by_procedure_code()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var result = await Production(db).RunAsync(await TodayAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal((3, 300m, 100m, 200m), (d.ChargeEntries, d.GrossCharges, d.ChargesReversed, d.NetProduction));
        Assert.Equal(("production", false, d.ChargeEntries), (result.ReportId, result.NoData, result.RecordCount));
        Assert.Equal(new[] { "Dr. One", "Dr. Two" }, d.Providers.Select(p => p.ProviderName));
        Assert.Equal(new[] { (2, 200m, 100m, 100m), (1, 100m, 0m, 100m) }, d.Providers.Select(p => (p.Charges, p.Gross, p.Reversed, p.Net)));
        Assert.Equal(d.NetProduction, d.Providers.Sum(p => p.Net));
        Assert.Equal(d.NetProduction, d.Codes.Sum(c => c.Net));
        Assert.Equal(3, d.Codes.Count);
        Assert.Equal(100m, d.Codes.Single(c => c.Reversed == 100m).Gross);        // the reversed charge's code carries its reversal
    }

    [Fact]
    public async Task A_provider_amendment_moves_the_production_to_the_provider_now_credited()
    {
        await ArrangeLedgerAsync();
        await using (var db = Fixture.CreateContext())
        {
            var procedure = await db.CompletedProcedures.Where(p => p.ProviderProfileId == _two.Id).SingleAsync();
            db.CompletedProcedureAmendments.Add(AmendmentRow(procedure, 1, "am-1", _one.Id));
            await db.SaveChangesAsync();
        }

        await using var check = Fixture.CreateContext();
        var d = (await Production(check).RunAsync(await TodayAsync(check), S.Actor, default)).Data;
        Assert.Equal(new[] { "Dr. One" }, d.Providers.Select(p => p.ProviderName));
        Assert.Equal((3, 300m, 100m, 200m), (d.Providers[0].Charges, d.Providers[0].Gross, d.Providers[0].Reversed, d.Providers[0].Net));
        Assert.Equal(200m, d.NetProduction);        // the total did not change, only who it belongs to
    }

    [Fact]
    public async Task The_provider_filter_narrows_production_and_the_proof_covers_that_production_alone()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var two = (await Production(db).RunAsync(await TodayAsync(db, [_two.Id]), S.Actor, default)).Data;

        Assert.Equal((1, 100m, 0m, 100m), (two.ChargeEntries, two.GrossCharges, two.ChargesReversed, two.NetProduction));
        Assert.Equal(new[] { "Dr. Two" }, two.Providers.Select(p => p.ProviderName));
        Assert.Equal(("ProviderProduction", true, 100m, 100m), (two.Ledger.Scope, two.Ledger.Reconciled, two.Ledger.LinesTotal, two.Ledger.LedgerTotal));
        Assert.Equal(new[] { FinancialLines.Charges, FinancialLines.ChargeReversals }, two.Ledger.Lines.Select(l => l.Line));

        var one = (await Production(db).RunAsync(await TodayAsync(db, [_one.Id]), S.Actor, default)).Data;
        Assert.Equal((2, 200m, 100m, 100m), (one.ChargeEntries, one.GrossCharges, one.ChargesReversed, one.NetProduction));
        Assert.True(one.Ledger.Reconciled);

        var location = await Resolver(db).ResolveAsync(new ReportFilterInput("2030-01-01", "2030-01-02", null, (await db.PracticeLocations.Select(l => (Guid?)l.Id).FirstOrDefaultAsync()) ?? await AddLocationAsync(db)), default);
        Assert.Equal("filter_not_supported", (await Assert.ThrowsAsync<ReportException>(() => Production(db).RunAsync(location, S.Actor, default))).Code);
    }

    private static async Task<Guid> AddLocationAsync(AlveraDbContext db)
    {
        var id = Guid.NewGuid();
        db.PracticeLocations.Add(new Alveara.Api.Architecture.Configuration.PracticeLocation { Id = id, Name = "Main", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Collections_are_in_money_by_method_and_net_of_reversals_and_refunds()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var result = await Collections(db).RunAsync(await TodayAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal((2, 190m, 40m, 20m, 130m), (d.PaymentEntries, d.PaymentsReceived, d.PaymentsReversed, d.Refunded, d.NetCollections));
        Assert.Equal(new[] { "Card", "Cash" }, d.Methods.Select(m => m.Method));
        Assert.Equal(new[] { (40m, 40m, 0m, 0m), (150m, 0m, 20m, 130m) }, d.Methods.Select(m => (m.Received, m.Reversed, m.Refunded, m.Net)));
        Assert.Equal(d.NetCollections, d.Methods.Sum(m => m.Net));
        Assert.True(d.Ledger.Reconciled);
        Assert.Equal(75m, Line(d.Ledger, FinancialLines.OpeningBalances));        // the loaded balance is visible beside collections and counted in neither
        Assert.Equal(("collections", false), (result.ReportId, result.NoData));
    }

    [Fact]
    public async Task Collections_refuse_a_provider_or_location_filter_rather_than_ignore_it()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var withProvider = await TodayAsync(db, [_one.Id]);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Collections(db).RunAsync(withProvider, S.Actor, default));
        Assert.Equal(("filter_not_supported", 400), (ex.Code, ex.StatusCode));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task A_range_with_nothing_recorded_says_so_and_the_entries_are_dated_by_when_they_were_recorded()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var past = await Resolver(db).ResolveAsync(new ReportFilterInput("2020-01-01", "2020-01-31", null, null), default);
        var production = await Production(db).RunAsync(past, S.Actor, default);
        var collections = await Collections(db).RunAsync(past, S.Actor, default);

        Assert.True(production.NoData);
        Assert.Equal((0m, 0m, true), (production.Data.GrossCharges, production.Data.Ledger.LedgerTotal, production.Data.Ledger.Reconciled));
        Assert.Equal(10, production.Data.Ledger.Lines.Count);        // every line is still a row
        Assert.True(collections.NoData);
        var yesterday = DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime).AddDays(-1).ToString("yyyy-MM-dd");
        Assert.True((await Production(db).RunAsync(await Resolver(db).ResolveAsync(new ReportFilterInput(yesterday, yesterday, null, null), default), S.Actor, default)).NoData);        // everything was recorded today
    }

    [Fact]
    public async Task Each_run_is_recorded_without_any_name_and_a_failed_audit_save_refuses_the_report()
    {
        await ArrangeLedgerAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Production(db).RunAsync(await TodayAsync(db), S.Actor, default);
            await Collections(db).RunAsync(await TodayAsync(db), S.Actor, default);
            var audit = await db.AuditLogEntries.Where(e => e.EventType == AuditEventTypes.ReportViewed).ToListAsync();
            Assert.Equal(2, audit.Count);
            Assert.Contains(audit, e => e.Details.StartsWith("Report production:"));
            Assert.Contains(audit, e => e.Details.StartsWith("Report collections:"));
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var filter = await TodayAsync(failing);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Production(failing).RunAsync(filter, S.Actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
    }

    // ---------- the supporting entries ----------

    [Fact]
    public async Task Every_drill_down_total_and_amount_equals_the_cell_it_came_from()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var filter = await TodayAsync(db);
        var production = (await Production(db).RunAsync(filter, S.Actor, default)).Data;
        var collections = (await Collections(db).RunAsync(filter, S.Actor, default)).Data;
        Task<FinancialPage> Entries(string report, FinancialDrillCriteria c) => Drill(db).EntriesAsync(report, filter, c, S.Actor, default);

        foreach (var line in FinancialLines.Production)
        {
            var cell = production.Ledger.Lines.Single(l => l.Line == line);
            var page = await Entries(ReportIds.Production, None with { Line = line });
            Assert.Equal((cell.Entries, cell.Amount), (page.Total, page.TotalAmount));
        }
        foreach (var line in FinancialLines.Collections)
        {
            var cell = collections.Ledger.Lines.Single(l => l.Line == line);
            var page = await Entries(ReportIds.Collections, None with { Line = line });
            Assert.Equal((cell.Entries, cell.Amount), (page.Total, page.TotalAmount));
        }
        foreach (var provider in production.Providers)
        {
            var page = await Entries(ReportIds.Production, None with { ProviderId = provider.ProviderProfileId });
            Assert.Equal(provider.Net, page.TotalAmount);
        }
        var card = await Entries(ReportIds.Collections, None with { Method = "Card" });
        Assert.Equal((2, 0m), (card.Total, card.TotalAmount));        // the card payment and its reversal
        var both = await Entries(ReportIds.Production, None);
        Assert.Equal((4, 200m), (both.Total, both.TotalAmount));
        Assert.All(both.Rows, r => Assert.NotEqual(Guid.Empty, r.PatientId));
    }

    [Fact]
    public async Task Pages_never_repeat_or_skip_an_entry_and_each_page_is_recorded()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var filter = await TodayAsync(db);
        var seen = new List<Guid>();
        FinancialCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await Drill(db).EntriesAsync(ReportIds.Production, filter, new FinancialDrillCriteria(null, null, null, 3, cursor?.AfterRecordedAtUtc, cursor?.AfterId), S.Actor, default);
            Assert.Equal(4, page.Total);
            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.Next;
            pages++;
        } while (cursor is not null);

        Assert.Equal(2, pages);        // 3 + 1
        Assert.Equal(4, seen.Distinct().Count());
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed && e.Details.Contains("(supporting records)")));
    }

    [Fact]
    public async Task Bad_drill_criteria_get_stable_codes()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var filter = await TodayAsync(db);
        async Task<string> CodeAsync(string report, FinancialDrillCriteria c) => (await Assert.ThrowsAsync<ReportException>(() => Drill(db).EntriesAsync(report, filter, c, S.Actor, default))).Code;

        Assert.Equal("invalid_line", await CodeAsync(ReportIds.Production, None with { Line = FinancialLines.Payments }));
        Assert.Equal("invalid_line", await CodeAsync(ReportIds.Collections, None with { Line = FinancialLines.Charges }));
        Assert.Equal("invalid_line", await CodeAsync(ReportIds.Production, None with { Line = FinancialLines.OpeningBalances }));        // the opening balance is not production
        Assert.Equal("filter_not_supported", await CodeAsync(ReportIds.Collections, None with { ProviderId = _one.Id }));
        Assert.Equal("filter_not_supported", await CodeAsync(ReportIds.Production, None with { Method = "Cash" }));
        Assert.Equal("invalid_limit", await CodeAsync(ReportIds.Production, None with { Limit = 0 }));
        Assert.Equal("invalid_cursor", await CodeAsync(ReportIds.Production, None with { AfterId = Guid.NewGuid() }));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task A_range_over_the_row_limit_is_refused_and_nothing_is_recorded()
    {
        await ArrangeLedgerAsync();
        await using var db = Fixture.CreateContext();
        var small = new ReportOptions { MaxSourceRows = 2 };
        var ex = await Assert.ThrowsAsync<ReportException>(async () => await Production(db, small).RunAsync(await TodayAsync(db), S.Actor, default));        // three charge rows, limit two
        Assert.Equal(("report_too_large", 413), (ex.Code, ex.StatusCode));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }
}
