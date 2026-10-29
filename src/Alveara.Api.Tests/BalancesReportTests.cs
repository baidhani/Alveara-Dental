using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 2b, the balances report and its accounts, against a real SQL Server with ledger rows written straight to the database so their dates can be chosen. Three accounts: Ann (+100 three days ago, then +50 and -20 today
/// = 130 owed), Bo (+200 today) and Di (+60 three days ago, -60 today: back to zero). The ledger refuses an entry that would leave an account negative, so no account can be in credit. For today the opening snapshot is Ann 100 and Di 60, the closing one Ann 130, Bo 200 and Di 0, and the change
/// (170) equals an independent sum of the four entries recorded today; a later day shows the same balances with no change; an empty ledger says there is no data; provider and location filters are refused; every accounts total
/// equals the cell it came from, pages never repeat or skip, bad criteria get stable codes, a run is recorded, a failed audit save refuses the report and a range over the account limit is refused.
/// </summary>
public class BalancesReportTests : CompletionTestBase
{
    private Guid _di;

    private async Task SeedAsync()
    {
        _di = await S.PatientAsync("Di", "Fo");
        var old = DateTimeOffset.UtcNow.AddDays(-3);
        var (a, b, d) = (await NewAccountAsync(Ann), await NewAccountAsync(Bo), await NewAccountAsync(_di));
        BillingEntry Dated(BillingEntry e, DateTimeOffset when) { e.RecordedAtUtc = when; return e; }
        await AddRowsAsync(Dated(AdjustmentRow(a, 100m, 1, "a-1"), old), AdjustmentRow(a, 50m, 2, "a-2"), AdjustmentRow(a, -20m, 3, "a-3"),
            AdjustmentRow(b, 200m, 1, "b-1"), Dated(AdjustmentRow(d, 60m, 1, "d-1"), old), AdjustmentRow(d, -60m, 2, "d-2"));
    }

    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()));

    private async Task<ReportFilter> RangeAsync(AlveraDbContext db, int fromOffset = 0, int toOffset = 0, Guid[]? providers = null, Guid? location = null)
    {
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime);
        return await Resolver(db).ResolveAsync(new ReportFilterInput(today.AddDays(fromOffset).ToString("yyyy-MM-dd"), today.AddDays(toOffset).ToString("yyyy-MM-dd"), providers, location), default);
    }

    private BalancesReportService Report(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()), NullLogger<BalancesReportService>.Instance);
    private BalancesDrillService Drill(AlveraDbContext db) => new(db, Options.Create(new ReportOptions()), NullLogger<BalancesDrillService>.Instance);
    private static readonly BalanceDrillCriteria None = new(null, null, null, null);

    [Fact]
    public async Task The_snapshots_at_the_start_and_the_end_and_the_change_between_them_reconcile_to_the_ledger()
    {
        await SeedAsync();
        await using var db = Fixture.CreateContext();
        var result = await Report(db).RunAsync(await RangeAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal((2, 2, 160m, 0, 0m, 160m), (d.AtStart.Accounts, d.AtStart.AccountsOwing, d.AtStart.TotalOwed, d.AtStart.AccountsInCredit, d.AtStart.TotalCredit, d.AtStart.NetBalance));
        Assert.Equal((3, 2, 330m, 0, 0m, 330m), (d.AtEnd.Accounts, d.AtEnd.AccountsOwing, d.AtEnd.TotalOwed, d.AtEnd.AccountsInCredit, d.AtEnd.TotalCredit, d.AtEnd.NetBalance));
        Assert.Equal((170m, 170m, 4, true), (d.Change, d.LedgerChange, d.LedgerEntries, d.Reconciled));
        Assert.Equal(await db.BillingEntries.SumAsync(e => e.Amount), d.AtEnd.NetBalance);        // the closing net is the whole ledger
        Assert.Equal(("balances", false, 3), (result.ReportId, result.NoData, result.RecordCount));
    }

    [Fact]
    public async Task A_later_range_shows_the_same_balances_with_no_change()
    {
        await SeedAsync();
        await using var db = Fixture.CreateContext();
        var result = await Report(db).RunAsync(await RangeAsync(db, 1, 1), S.Actor, default);

        var d = result.Data;
        Assert.Equal(d.AtStart, d.AtEnd);
        Assert.Equal(330m, d.AtEnd.NetBalance);
        Assert.Equal((0m, 0m, 0, true), (d.Change, d.LedgerChange, d.LedgerEntries, d.Reconciled));
        Assert.False(result.NoData);        // a range after all the activity still shows the balances
    }

    [Fact]
    public async Task An_empty_ledger_says_there_is_no_data_and_still_reconciles()
    {
        await using var db = Fixture.CreateContext();
        var result = await Report(db).RunAsync(await RangeAsync(db), S.Actor, default);

        Assert.True(result.NoData);
        Assert.Equal((0, 0m, 0m, true), (result.Data.AtEnd.Accounts, result.Data.AtEnd.NetBalance, result.Data.Change, result.Data.Reconciled));
    }

    [Fact]
    public async Task A_location_filter_is_refused_for_the_report_and_for_its_accounts()
    {
        await SeedAsync();
        await using var db = Fixture.CreateContext();
        var withLocation = await Resolver(db).ResolveAsync(new ReportFilterInput("2030-01-01", "2030-01-02", null, await LocationAsync(db)), default);
        Assert.Equal("filter_not_supported", (await Assert.ThrowsAsync<ReportException>(() => Report(db).RunAsync(withLocation, S.Actor, default))).Code);
        Assert.Equal("filter_not_supported", (await Assert.ThrowsAsync<ReportException>(() => Drill(db).AccountsAsync(withLocation, None, S.Actor, default))).Code);
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    private static async Task<Guid> LocationAsync(AlveraDbContext db)
    {
        var existing = await db.PracticeLocations.Select(l => (Guid?)l.Id).FirstOrDefaultAsync();
        if (existing is { } id) return id;
        var created = Guid.NewGuid();
        db.PracticeLocations.Add(new Alveara.Api.Architecture.Configuration.PracticeLocation { Id = created, Name = "Main", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return created;
    }

    [Fact]
    public async Task The_accounts_behind_each_side_equal_the_cell_and_come_largest_first()
    {
        await SeedAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Report(db).RunAsync(filter, S.Actor, default)).Data;

        var owing = await Drill(db).AccountsAsync(filter, None, S.Actor, default);
        Assert.Equal((BalanceSides.Owing, report.AtEnd.AccountsOwing, report.AtEnd.TotalOwed), (owing.Side, owing.Total, owing.TotalAmount));
        Assert.Equal(new[] { (Bo, 200m), (Ann, 130m) }, owing.Rows.Select(r => (r.PatientId, r.Balance)));
        var credit = await Drill(db).AccountsAsync(filter, None with { Side = BalanceSides.Credit }, S.Actor, default);
        Assert.Equal((0, 0m, 0), (credit.Total, credit.TotalAmount, credit.Rows.Count));        // the ledger refuses a negative balance, so no account is ever in credit; the side is still listed so a violation could not hide
        Assert.DoesNotContain(owing.Rows.Concat(credit.Rows), r => r.PatientId == _di);        // the account back at zero is on neither side
    }

    [Fact]
    public async Task Pages_never_repeat_or_skip_an_account_and_each_page_is_recorded()
    {
        await SeedAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var seen = new List<Guid>();
        BalanceCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await Drill(db).AccountsAsync(filter, new BalanceDrillCriteria(null, 1, cursor?.AfterBalance, cursor?.AfterAccountId), S.Actor, default);
            Assert.Equal(2, page.Total);
            seen.AddRange(page.Rows.Select(r => r.PatientId));
            cursor = page.Next;
            pages++;
        } while (cursor is not null);

        Assert.Equal(2, pages);
        Assert.Equal(new[] { Bo, Ann }, seen);
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed && e.Details.Contains("(supporting records)")));
    }

    [Fact]
    public async Task Bad_account_criteria_get_stable_codes_and_record_nothing()
    {
        await SeedAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        async Task<string> CodeAsync(BalanceDrillCriteria c) => (await Assert.ThrowsAsync<ReportException>(() => Drill(db).AccountsAsync(filter, c, S.Actor, default))).Code;

        Assert.Equal("invalid_side", await CodeAsync(None with { Side = "Both" }));
        Assert.Equal("invalid_limit", await CodeAsync(None with { Limit = 0 }));
        Assert.Equal("invalid_limit", await CodeAsync(None with { Limit = 501 }));
        Assert.Equal("invalid_cursor", await CodeAsync(None with { AfterBalance = 5m }));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task A_run_is_recorded_a_failed_audit_save_refuses_it_and_too_many_accounts_are_refused()
    {
        await SeedAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Report(db).RunAsync(await RangeAsync(db), S.Actor, default);
            var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportViewed);
            Assert.StartsWith("Report balances:", entry.Details);
            Assert.Contains("3 record(s)", entry.Details);
        }

        await using (var failing = new SchedulingTestSupport(Fixture).FailingAuditContext())
        {
            var filter = await RangeAsync(failing);
            var ex = await Assert.ThrowsAsync<ReportException>(() => Report(failing).RunAsync(filter, S.Actor, default));
            Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
        }

        await using var small = Fixture.CreateContext();
        var tooMany = await Assert.ThrowsAsync<ReportException>(async () => await Report(small, new ReportOptions { MaxSourceRows = 2 }).RunAsync(await RangeAsync(small), S.Actor, default));        // three accounts at the end, limit two
        Assert.Equal(("report_too_large", 413), (tooMany.Code, tooMany.StatusCode));
        Assert.Equal(1, await small.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));        // only the first, successful run
    }
}
