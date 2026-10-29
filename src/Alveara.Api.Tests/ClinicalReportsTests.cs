using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 3a, the treatment-plan status and completed-procedures reports and the records behind them, against a real SQL Server with plans and completions made only through the real services. Three plans today:
/// Ann's plan A (four items: two accepted and completed by Dr. One and Dr. Two, one accepted and scheduled, one withdrawn), Bo's plan B (one accepted, one declined) and Ann's plan C (one pending item; the plan itself
/// withdrawn). The item cells add up (7 items: 1 pending, 4 accepted of which 1 scheduled, 3 unscheduled and 2 completed, 1 declined, 1 withdrawn); an amendment moves a procedure to the right day and the right provider; the
/// provider filter narrows the procedures and is refused for plan status; every drill-down total equals its cell, pages never repeat or skip, and bad criteria get stable codes.
/// </summary>
public class ClinicalReportsTests : CompletionTestBase
{
    private ProviderProfile _one = null!, _two = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _one = await ProviderAsync("Dr. One");
        _two = await ProviderAsync("Dr. Two");
    }

    private async Task ArrangeAsync()
    {
        var a = await AcceptedPlanAsync(3);                                   // items 0, 1, 2 accepted; item 3 pending
        async Task<PlanView> CompleteAsync(PlanView p, int item, ProviderProfile who, string key)
        {
            await Completion(s => s.CompleteAsync(p.Id, RequestFor(p, [item], AnnEncounter, who.Id, key), S.Actor, default));
            return await PlanNowAsync(p.Id);
        }
        a = await CompleteAsync(a, 0, _one, "set-1");
        a = await CompleteAsync(a, 1, _two, "set-2");
        a = await Svc(s => s.ScheduleItemAsync(a.Id, a.Items[2].Id, true, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), S.Actor, default));
        a = await PlanNowAsync(a.Id);
        await Svc(s => s.WithdrawItemAsync(a.Id, a.Items[3].Id, "Not needed", a.RowVersion, S.Actor, default));

        var b = await AcceptedPlanAsync(1, patient: Bo, firstCode: 600);      // item 0 accepted, item 1 pending
        await Svc(s => s.DecideItemAsync(b.Id, b.Items[1].Id, "Declined", "Too costly", "Bo Kim", b.RowVersion, S.Actor, default));

        var c = await AcceptedPlanAsync(0, firstCode: 700);                    // one pending item
        await Svc(s => s.WithdrawAsync(c.Id, "Created in error", c.RowVersion, S.Actor, default));
    }

    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()));

    private async Task<ReportFilter> RangeAsync(AlveraDbContext db, int fromOffset = -1, int toOffset = 1, Guid[]? providers = null, Guid? location = null)
    {
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime);
        return await Resolver(db).ResolveAsync(new ReportFilterInput(today.AddDays(fromOffset).ToString("yyyy-MM-dd"), today.AddDays(toOffset).ToString("yyyy-MM-dd"), providers, location), default);
    }

    private TreatmentPlanStatusReportService Plans(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()), NullLogger<TreatmentPlanStatusReportService>.Instance);
    private CompletedProceduresReportService Procedures(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()), NullLogger<CompletedProceduresReportService>.Instance);
    private ClinicalDrillService Drill(AlveraDbContext db) => new(db, Resolver(db), Options.Create(new ReportOptions()), NullLogger<ClinicalDrillService>.Instance);

    private static PlanCell Cell(PlanStatusData d, string cell) => d.Cells.Single(c => c.Cell == cell);

    // ---------- treatment-plan status ----------

    [Fact]
    public async Task Plan_items_are_counted_in_exactly_one_state_and_the_counts_reconcile()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Plans(db).RunAsync(await RangeAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal((3, 2, 1, 7), (d.Plans, d.PlansProposed, d.PlansWithdrawn, d.Items));
        Assert.Equal(PlanItemCells.All, d.Cells.Select(c => c.Cell));
        Assert.Equal(new[] { 1, 4, 1, 1, 1, 3, 2 }, d.Cells.Select(c => c.Items));
        Assert.Equal(new[] { 100m, 400m, 100m, 100m, 100m, 300m, 200m }, d.Cells.Select(c => c.EstimatedFee));
        Assert.Equal(d.Items, Cell(d, "Pending").Items + Cell(d, "Accepted").Items + Cell(d, "Declined").Items + Cell(d, "Withdrawn").Items);
        Assert.Equal(Cell(d, "Accepted").Items, Cell(d, "AcceptedScheduled").Items + Cell(d, "AcceptedUnscheduled").Items);
        Assert.True(d.Reconciled);
        Assert.Equal(("treatment-plans", false, 3), (result.ReportId, result.NoData, result.RecordCount));
    }

    [Fact]
    public async Task Plans_created_outside_the_range_are_not_in_the_report_and_an_empty_range_says_so()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var yesterday = await Plans(db).RunAsync(await RangeAsync(db, -1, -1), S.Actor, default);

        Assert.True(yesterday.NoData);
        Assert.Equal((0, 0, true), (yesterday.Data.Items, yesterday.Data.Plans, yesterday.Data.Reconciled));
        Assert.Equal(7, yesterday.Data.Cells.Count);        // every cell is still a row
    }

    [Fact]
    public async Task Plan_status_refuses_a_provider_or_location_filter_and_records_nothing()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ReportException>(async () => await Plans(db).RunAsync(await RangeAsync(db, providers: [_one.Id]), S.Actor, default));
        Assert.Equal(("filter_not_supported", 400), (ex.Code, ex.StatusCode));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task Every_plan_item_drill_total_and_fee_equals_its_cell_and_pages_never_repeat()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Plans(db).RunAsync(filter, S.Actor, default)).Data;

        foreach (var cell in report.Cells)
        {
            var page = await Drill(db).PlanItemsAsync(filter, new PlanItemDrillCriteria(cell.Cell, null, null), S.Actor, default);
            Assert.Equal((cell.Items, cell.EstimatedFee, cell.Items), (page.Total, page.TotalEstimatedFee, page.Rows.Count));
        }
        var all = await Drill(db).PlanItemsAsync(filter, new PlanItemDrillCriteria(null, null, null), S.Actor, default);
        Assert.Equal((7, 700m), (all.Total, all.TotalEstimatedFee));

        var seen = new List<Guid>();
        Guid? after = null;
        var pages = 0;
        do
        {
            var page = await Drill(db).PlanItemsAsync(filter, new PlanItemDrillCriteria(null, 3, after), S.Actor, default);
            seen.AddRange(page.Rows.Select(r => r.ItemId));
            after = page.Next?.AfterId;
            pages++;
        } while (after is not null);
        Assert.Equal((3, 7), (pages, seen.Distinct().Count()));
    }

    // ---------- completed procedures ----------

    [Fact]
    public async Task Completed_procedures_are_counted_by_day_provider_and_code_and_each_cut_adds_up()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Procedures(db).RunAsync(await RangeAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal((2, 200m, true), (d.Procedures, d.EstimatedFee, d.Reconciled));
        Assert.Equal(new[] { 0, 2, 0 }, d.Days.Select(x => x.Procedures));
        Assert.Equal(new[] { ("Dr. One", 1, 100m), ("Dr. Two", 1, 100m) }, d.Providers.Select(p => (p.ProviderName, p.Procedures, p.EstimatedFee)));
        Assert.Equal(2, d.Codes.Count);
        Assert.All(d.Codes, c => Assert.Equal((1, 100m), (c.Procedures, c.EstimatedFee)));
        Assert.Equal(("completed-procedures", false, 2), (result.ReportId, result.NoData, result.RecordCount));
    }

    [Fact]
    public async Task An_amendment_moves_a_procedure_to_its_corrected_day_and_provider()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            var procedure = await db.CompletedProcedures.Where(p => p.ProviderProfileId == _two.Id).SingleAsync();
            var tomorrowNoon = Clock.FromPracticeLocal(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).Date.AddDays(1).AddHours(12));        // the database refuses a time before the plan existed, so the correction goes later
            db.CompletedProcedureAmendments.Add(AmendmentRow(procedure, 1, "am-1", _one.Id, tomorrowNoon));
            await db.SaveChangesAsync();
        }

        await using var check = Fixture.CreateContext();
        var d = (await Procedures(check).RunAsync(await RangeAsync(check), S.Actor, default)).Data;
        Assert.Equal(new[] { 0, 1, 1 }, d.Days.Select(x => x.Procedures));        // one moved to tomorrow
        Assert.Equal(new[] { ("Dr. One", 2) }, d.Providers.Select(p => (p.ProviderName, p.Procedures)));        // and is now Dr. One's
        Assert.True(d.Reconciled);
    }

    [Fact]
    public async Task The_provider_filter_narrows_every_view_and_a_location_filter_is_refused()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var two = (await Procedures(db).RunAsync(await RangeAsync(db, providers: [_two.Id]), S.Actor, default)).Data;

        Assert.Equal((1, 100m), (two.Procedures, two.EstimatedFee));
        Assert.Equal(new[] { "Dr. Two" }, two.Providers.Select(p => p.ProviderName));
        Assert.Equal(1, two.Codes.Sum(c => c.Procedures));
        Assert.Equal(1, two.Days.Sum(x => x.Procedures));

        var location = await Resolver(db).ResolveAsync(new ReportFilterInput("2030-01-01", "2030-01-02", null, await LocationAsync(db)), default);
        Assert.Equal("filter_not_supported", (await Assert.ThrowsAsync<ReportException>(() => Procedures(db).RunAsync(location, S.Actor, default))).Code);
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
    public async Task Every_procedure_drill_total_and_fee_equals_the_cell_it_came_from()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Procedures(db).RunAsync(filter, S.Actor, default)).Data;
        Task<ClinicalPage<ProcedureDrillRow>> List(ProcedureDrillCriteria c) => Drill(db).ProceduresAsync(filter, c, S.Actor, default);
        var none = new ProcedureDrillCriteria(null, null, null, null, null, null, null);

        Assert.Equal((report.Procedures, report.EstimatedFee), ((await List(none)).Total, (await List(none)).TotalEstimatedFee));
        foreach (var provider in report.Providers) Assert.Equal(provider.Procedures, (await List(none with { ProviderId = provider.ProviderProfileId })).Total);
        foreach (var code in report.Codes) Assert.Equal(code.Procedures, (await List(none with { CodeSystem = code.CodeSystem, Code = code.Code })).Total);
        foreach (var day in report.Days) Assert.Equal(day.Procedures, (await List(none with { Date = day.Date })).Total);
        var row = (await List(none with { ProviderId = _one.Id })).Rows.Single();
        Assert.Equal((_one.Id, 100m), (row.ProviderProfileId, row.EstimatedFee));
    }

    [Fact]
    public async Task Bad_drill_criteria_get_stable_codes_and_a_run_over_the_limit_is_refused()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var none = new ProcedureDrillCriteria(null, null, null, null, null, null, null);
        async Task<string> Code(Func<Task> action) => (await Assert.ThrowsAsync<ReportException>(action)).Code;

        Assert.Equal("invalid_cell", await Code(() => Drill(db).PlanItemsAsync(filter, new PlanItemDrillCriteria("Done", null, null), S.Actor, default)));
        Assert.Equal("invalid_limit", await Code(() => Drill(db).PlanItemsAsync(filter, new PlanItemDrillCriteria(null, 0, null), S.Actor, default)));
        Assert.Equal("invalid_code", await Code(() => Drill(db).ProceduresAsync(filter, none with { Code = "LOCAL-500" }, S.Actor, default)));
        Assert.Equal("invalid_cursor", await Code(() => Drill(db).ProceduresAsync(filter, none with { AfterId = Guid.NewGuid() }, S.Actor, default)));
        Assert.Equal("date_outside_range", await Code(() => Drill(db).ProceduresAsync(filter, none with { Date = "2020-01-01" }, S.Actor, default)));
        Assert.Equal("invalid_limit", await Code(() => Drill(db).ProceduresAsync(filter, none with { Limit = 501 }, S.Actor, default)));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));

        var small = new ReportOptions { MaxSourceRows = 1 };
        var ex = await Assert.ThrowsAsync<ReportException>(async () => await Procedures(db, small).RunAsync(filter, S.Actor, default));        // two procedures, limit one
        Assert.Equal(("report_too_large", 413), (ex.Code, ex.StatusCode));
    }

    [Fact]
    public async Task Each_run_is_recorded_without_any_name_and_a_failed_audit_save_refuses_the_report()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Plans(db).RunAsync(await RangeAsync(db), S.Actor, default);
            await Procedures(db).RunAsync(await RangeAsync(db), S.Actor, default);
            var audit = await db.AuditLogEntries.Where(e => e.EventType == AuditEventTypes.ReportViewed).Select(e => e.Details).ToListAsync();
            Assert.Contains(audit, d => d.StartsWith("Report treatment-plans:"));
            Assert.Contains(audit, d => d.StartsWith("Report completed-procedures:"));
            Assert.All(audit, d => Assert.DoesNotContain("Ann", d));
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var filter = await RangeAsync(failing);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Procedures(failing).RunAsync(filter, S.Actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
    }
}
