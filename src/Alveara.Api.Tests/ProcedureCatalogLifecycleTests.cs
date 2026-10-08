using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Procedures;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N005 against real SQL Server: how a procedure changes over time. A fee change is a new version that never rewrites the old one; the fee in effect on any date is answerable; a scheduled
/// fee waits for its date; backdating is refused; every change needs a reason and the row version that was read; inactivating a procedure that other records refer to needs confirmation; and
/// every change is logged with who made it.
/// </summary>
public class ProcedureCatalogLifecycleTests : ProcedureTestBase
{
    private sealed class FixedUsage(int count) : IProcedureUsageSource
    {
        public string Name => "Test source";
        public Task<int> CountAsync(Guid procedureId, CancellationToken ct) => Task.FromResult(count);
    }

    // ---------- fee history ----------

    [Fact]
    public async Task A_fee_change_is_a_new_version_and_the_old_version_keeps_its_fee()
    {
        var v1 = await CreateAsync(Input(fee: 50m));
        var v2 = await ReviseAsync(v1, Like(v1, fee: 60m), "Annual fee review", Mar15);

        Assert.Equal(2, v2.Summary.CurrentVersionNumber);
        Assert.Equal(new[] { 50m, 60m }, v2.Versions.Select(v => v.Fee));
        Assert.Equal("Annual fee review", v2.Versions[1].Reason);
        Assert.Equal(60m, v2.Summary.Version.Fee);
    }

    [Fact]
    public async Task The_fee_in_effect_on_a_date_is_answerable_including_dates_before_a_change()
    {
        var v1 = await CreateAsync(Input(fee: 50m, effectiveFrom: Today.AddDays(-100)), Mar15);
        var v2 = await ReviseAsync(v1, Like(v1, fee: 70m, effectiveFrom: Today.AddDays(10)), "Planned increase", Mar15);

        Assert.Equal(50m, (await With(s => s.SnapshotAsync(v2.Summary.Id, Today.AddDays(5), default))).Fee);
        Assert.Equal(70m, (await With(s => s.SnapshotAsync(v2.Summary.Id, Today.AddDays(10), default))).Fee);
        Assert.Equal(50m, (await With(s => s.SnapshotAsync(v2.Summary.Id, Today.AddDays(-50), default))).Fee);
    }

    [Fact]
    public async Task A_scheduled_fee_waits_for_its_date_and_the_procedure_shows_as_Scheduled_before_any_version_starts()
    {
        var future = await CreateAsync(Input("FUT-1", effectiveFrom: Today.AddDays(30)));
        Assert.Equal("Scheduled", future.Summary.Status);
        Assert.Empty(await With(s => s.ActiveForPlanningAsync(null, null, null, null, null, null, default)));
        var later = await With(s => s.ActiveForPlanningAsync(null, null, null, null, null, Today.AddDays(30), default));
        Assert.Equal("FUT-1", Assert.Single(later).Code);
    }

    [Fact]
    public async Task A_new_version_cannot_start_in_the_past()
    {
        var v1 = await CreateAsync();
        var e = await Refused(() => ReviseAsync(v1, Like(v1, fee: 60m, effectiveFrom: Today.AddDays(-1))));
        Assert.True(e.FieldErrors.ContainsKey("effectiveFrom"));
        Assert.Single((await With(s => s.GetAsync(v1.Summary.Id, null, default))).Versions);
    }

    [Fact]
    public async Task A_change_needs_a_reason()
    {
        var v1 = await CreateAsync();
        var e = await Refused(() => ReviseAsync(v1, Like(v1, fee: 60m), reason: "  "));
        Assert.Equal(400, e.StatusCode);
        Assert.Single((await With(s => s.GetAsync(v1.Summary.Id, null, default))).Versions);
    }

    [Fact]
    public async Task Saving_without_changing_anything_adds_no_version()
    {
        var v1 = await CreateAsync();
        var same = await ReviseAsync(v1, Like(v1));
        Assert.Equal(1, same.Summary.CurrentVersionNumber);
        Assert.Single((await With(s => s.HistoryAsync(v1.Summary.Id, default))));
    }

    [Fact]
    public async Task An_edit_made_from_a_stale_copy_is_refused_as_a_conflict_and_changes_nothing()
    {
        var v1 = await CreateAsync(Input(fee: 50m));
        await ReviseAsync(v1, Like(v1, fee: 60m));                         // someone else saves first
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ReviseAsync(v1, Like(v1, fee: 99m)));   // v1.RowVersion is now stale
        Assert.Equal(60m, (await With(s => s.GetAsync(v1.Summary.Id, null, default))).Summary.Version.Fee);
    }

    [Fact]
    public async Task A_remembered_version_id_reads_back_exactly_what_was_in_force_then()
    {
        var v1 = await CreateAsync(Input(fee: 50m));
        await ReviseAsync(v1, Like(v1, fee: 60m));
        var first = (await With(s => s.GetAsync(v1.Summary.Id, null, default))).Versions[0];

        var snap = await With(s => s.VersionSnapshotAsync(first.VersionId, default));
        Assert.Equal((50m, 1, "USD"), (snap.Fee, snap.VersionNumber, snap.Currency));
        Assert.Equal(404, (await Refused(() => With(s => s.VersionSnapshotAsync(Guid.NewGuid(), default)))).StatusCode);
    }

    // ---------- inactivate / reactivate ----------

    [Fact]
    public async Task An_unreferenced_procedure_is_inactivated_with_a_reason_and_leaves_planning_but_keeps_its_history()
    {
        var created = await CreateAsync();
        var done = await With(s => s.InactivateAsync(created.Summary.Id, "No longer offered", false, created.Summary.RowVersion, S.Actor, default));

        Assert.False(done.Summary.IsActive);
        Assert.Equal("Inactive", done.Summary.Status);
        Assert.Empty(await With(s => s.ActiveForPlanningAsync(null, null, null, null, null, null, default)));
        Assert.Single((await With(s => s.ListAsync(null, null, null, "inactive", null, default))));
        Assert.Single(done.Versions);
    }

    [Fact]
    public async Task Inactivating_needs_a_reason()
    {
        var created = await CreateAsync();
        var e = await Refused(() => With(s => s.InactivateAsync(created.Summary.Id, null, false, created.Summary.RowVersion, S.Actor, default)));
        Assert.Equal(400, e.StatusCode);
        Assert.True((await With(s => s.GetAsync(created.Summary.Id, null, default))).Summary.IsActive);
    }

    [Fact]
    public async Task A_referenced_procedure_needs_confirmation_to_inactivate_and_the_refusal_carries_the_counts()
    {
        var created = await CreateAsync();
        var source = new FixedUsage(3);
        var e = await Refused(() => With(s => s.InactivateAsync(created.Summary.Id, "Retiring", false, created.Summary.RowVersion, S.Actor, default), null, source));

        Assert.Equal(("usage_confirmation_required", 409), (e.Code, e.StatusCode));
        Assert.NotNull(e.Usage);
        Assert.Equal(3, e.Usage!.Count);
        Assert.True((await With(s => s.GetAsync(created.Summary.Id, null, default))).Summary.IsActive);

        var confirmed = await With(s => s.InactivateAsync(created.Summary.Id, "Retiring", true, created.Summary.RowVersion, S.Actor, default), null, source);
        Assert.False(confirmed.Summary.IsActive);
    }

    [Fact]
    public async Task Usage_is_reported_per_source_as_numbers_only()
    {
        var created = await CreateAsync();
        var usage = await With(s => s.UsageAsync(created.Summary.Id, default), null, new FixedUsage(2));
        Assert.Equal(2, usage.Count);
        Assert.Contains(usage.BySource, b => b.Source == "Test source" && b.Count == 2);
    }

    [Fact]
    public async Task A_procedure_is_reactivated_with_its_history_intact()
    {
        var created = await CreateAsync();
        var off = await With(s => s.InactivateAsync(created.Summary.Id, "Paused", false, created.Summary.RowVersion, S.Actor, default));
        var on = await With(s => s.ReactivateAsync(created.Summary.Id, "Offered again", off.Summary.RowVersion, S.Actor, default));

        Assert.True(on.Summary.IsActive);
        Assert.Equal(new[] { "Created", "Inactivated", "Reactivated" }, (await With(s => s.HistoryAsync(created.Summary.Id, default))).Select(h => h.ChangeType));
    }

    [Fact]
    public async Task A_finding_linked_to_the_procedure_counts_as_usage_and_inactivating_confirms_then_leaves_the_link_alone()
    {
        var used = await CreateAsync(Input("USED-1"));
        var unused = await CreateAsync(Input("UNUSED-1"));
        var chart = await WithDb(db => new OdontogramService(db, Mar15).RecordAsync(Ann, "16", "O", "Caries", "Diagnosed", S.Actor, default));
        var finding = chart.Findings.Single();
        await WithDb(db => new OdontogramService(db, Mar15).LinkAsync(finding.Id, "Procedure", used.Summary.Id.ToString(), S.Actor, default));

        Assert.Equal(1, (await With(s => s.UsageAsync(used.Summary.Id, default))).Count);
        Assert.Equal(0, (await With(s => s.UsageAsync(unused.Summary.Id, default))).Count);          // only a link to THIS procedure counts

        var refused = await Refused(() => With(s => s.InactivateAsync(used.Summary.Id, "Retiring", false, used.Summary.RowVersion, S.Actor, default)));
        Assert.Equal(("usage_confirmation_required", 1), (refused.Code, refused.Usage!.Count));
        Assert.True((await With(s => s.GetAsync(used.Summary.Id, null, default))).Summary.IsActive);

        var done = await With(s => s.InactivateAsync(used.Summary.Id, "Retiring", true, used.Summary.RowVersion, S.Actor, default));
        Assert.False(done.Summary.IsActive);
        var after = await WithDb(db => new OdontogramService(db, Mar15).ChartAsync(Ann, default));
        Assert.Contains(after.Findings.Single().Links, l => l.LinkType == "Procedure" && l.Reference == used.Summary.Id.ToString());   // the record that referred to it is untouched
        Assert.Equal(done.Summary.Id, (await With(s => s.SnapshotAsync(used.Summary.Id, null, default))).ProcedureId);                  // and the procedure still reads as it did
    }

    // ---------- listing and planning ----------

    [Fact]
    public async Task The_list_filters_by_search_category_code_system_and_status()
    {
        await CreateAsync(Input("CLEAN-1", description: "Adult cleaning", category: "Preventive"));
        await CreateAsync(Input("FILL-1", description: "Composite filling", category: "Restorative"));
        await CreateAsync(Input("D2391", "CDT", "Resin filling", "Restorative", "ToothSurface", sourceName: "Licensed set"));

        Assert.Equal(new[] { "CLEAN-1" }, (await With(s => s.ListAsync("clean", null, null, null, null, default))).Select(r => r.Code));
        Assert.Equal(2, (await With(s => s.ListAsync(null, "Restorative", null, null, null, default))).Count);
        Assert.Equal(new[] { "D2391" }, (await With(s => s.ListAsync(null, null, "CDT", null, null, default))).Select(r => r.Code));
        Assert.Equal(3, (await With(s => s.ListAsync(null, null, null, "active", null, default))).Count);
        Assert.True((await Refused(() => With(s => s.ListAsync(null, null, null, "bogus", null, default)))).FieldErrors.ContainsKey("status"));
    }

    [Fact]
    public async Task Planning_offers_only_what_fits_the_tooth_and_surface_and_dentition()
    {
        await CreateAsync(Input("MOUTH-1", scope: "WholeMouth"));
        await CreateAsync(Input("ADULT-1", scope: "Tooth", dentition: "Permanent"));
        await CreateAsync(Input("CHILD-1", scope: "Tooth", dentition: "Primary"));
        await CreateAsync(Input("SURF-1", scope: "ToothSurface", dentition: "Both"));

        async Task<string[]> Offer(string? tooth, string? surface = null) =>
            (await With(s => s.ActiveForPlanningAsync(tooth, surface, null, null, null, null, default))).Select(p => p.Code).OrderBy(c => c).ToArray();

        Assert.Equal(new[] { "ADULT-1", "MOUTH-1", "SURF-1", "CHILD-1" }.OrderBy(c => c).ToArray(), await Offer(null));
        Assert.Equal(new[] { "ADULT-1", "SURF-1" }, await Offer("11"));
        Assert.Equal(new[] { "CHILD-1", "SURF-1" }, await Offer("55"));
        Assert.Equal(new[] { "SURF-1" }, await Offer("11", "I"));
        Assert.True((await Refused(() => With(s => s.ActiveForPlanningAsync("99", null, null, null, null, null, default)))).FieldErrors.ContainsKey("toothKey"));
        Assert.True((await Refused(() => With(s => s.ActiveForPlanningAsync(null, "I", null, null, null, null, default)))).FieldErrors.ContainsKey("surface"));
    }

    // ---------- audit ----------

    [Fact]
    public async Task Every_change_is_audited_and_recorded_as_an_event_with_who_and_when_and_no_patient_data()
    {
        var created = await CreateAsync();
        var revised = await ReviseAsync(created, Like(created, fee: 65m));
        await With(s => s.InactivateAsync(created.Summary.Id, "Paused", false, revised.Summary.RowVersion, S.Actor, default));

        var history = await With(s => s.HistoryAsync(created.Summary.Id, default));
        Assert.Equal(new[] { "Created", "Revised", "Inactivated" }, history.Select(h => h.ChangeType));
        Assert.All(history, h => Assert.Equal("Dr. Okafor", h.ActorName));
        Assert.Equal(new[] { 1, 2, 3 }, history.Select(h => h.EventNumber));

        var audit = await AuditAsync("ProcedureDefinition");
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.Equal(S.Actor, a.By));
        Assert.Contains(audit, a => a.Details.Contains("50.00") && a.Details.Contains("65.00"));
    }
}
