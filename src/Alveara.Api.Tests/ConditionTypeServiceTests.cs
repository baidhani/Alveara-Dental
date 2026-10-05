using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-006-C01 behaviour, against real SQL Server: the condition catalogue a practice can extend, and what it changes about recording a finding. A condition can be added, retired with a reason
/// and reactivated, each with who, when and why and a PHI-free audit entry in the same save; a retired condition stops NEW findings but every existing finding keeps reading as it did; a
/// condition must apply to the tooth's dentition (an implant is not recorded on a primary tooth); a tooth recorded as missing takes no further finding except an implant; and every failure
/// path - an unknown or retired condition, a wrong spelling, a stale change and simultaneous writers.
/// </summary>
public class ConditionTypeServiceTests : SafetyTestBase
{
    private Task<IReadOnlyList<ConditionTypeView>> Types(Func<ConditionTypeService, Task<IReadOnlyList<ConditionTypeView>>> action, Guid? actor = null) => WithDb(db => action(new ConditionTypeService(db, Clock)));
    private Task<ChartView> Chart(Func<OdontogramService, Task<ChartView>> action) => WithDb(db => action(new OdontogramService(db, Clock)));
    private Task<IReadOnlyList<ConditionTypeView>> CreateAsync(string code = "Fracture", string label = "Fracture", string scope = "Surface", string appliesTo = "Both", string effect = "None", Guid? actor = null) =>
        Types(s => s.CreateAsync(code, label, scope, appliesTo, effect, actor ?? S.Actor, default));
    private Task<ChartView> RecordAsync(string tooth, string? surface, string condition, string state = "Diagnosed", Guid? patient = null) =>
        Chart(s => s.RecordAsync(patient ?? Ann, tooth, surface, condition, state, S.Actor, default));
    private static ConditionTypeView Find(IReadOnlyList<ConditionTypeView> list, string code) => list.Single(t => t.Code == code);
    private static async Task<OdontogramException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<OdontogramException>(action);
    private async Task<List<(string Type, string Details, Guid? By)>> TypeAuditAsync() => await AuditAsync(nameof(ConditionType));

    // ---------- the catalogue ----------

    [Fact]
    public async Task The_catalogue_starts_with_the_six_course_conditions_all_active_and_lists_active_ones_first()
    {
        var list = await Types(s => s.ListAsync(default));
        Assert.Equal(6, list.Count);
        Assert.All(list, t => Assert.True(t.IsActive));
        Assert.All(list, t => Assert.Equal("System", t.CreatedByName));
        Assert.Equal(new[] { "Caries", "Crown", "Implant", "Missing tooth", "Restoration", "Root canal" }, list.Select(t => t.Label));
    }

    [Fact]
    public async Task A_new_condition_is_added_with_who_and_when_in_its_history_and_a_log_entry_that_does_not_name_it()
    {
        var list = await CreateAsync("Fracture", "Tooth fracture", "Surface", "Both", "None");
        var t = Find(list, "Fracture");
        Assert.Equal(("Tooth fracture", "Surface", "Both", "None", true, "Dr. Okafor"), (t.Label, t.Scope, t.AppliesTo, t.ToothEffect, t.IsActive, t.CreatedByName));
        Assert.Equal(7, list.Count);

        var history = await WithDb(db => new ConditionTypeService(db, Clock).HistoryAsync(t.Id, default));
        var created = Assert.Single(history);
        Assert.Equal((1, "Created", "Dr. Okafor", (string?)null), (created.EventNumber, created.ChangeType, created.ActorName, created.Reason));
        Assert.NotEqual(default, created.OccurredAtUtc);

        var audit = await TypeAuditAsync();
        var entry = Assert.Single(audit);
        Assert.Equal(("ConditionTypeCreated", (Guid?)S.Actor), (entry.Type, entry.By));
        Assert.DoesNotContain("Fracture", entry.Details);
    }

    [Fact]
    public async Task A_new_condition_can_be_recorded_on_a_surface_or_on_the_whole_tooth_as_its_scope_says()
    {
        await CreateAsync("Fracture", "Tooth fracture", "Surface");
        await CreateAsync("Abscess", "Abscess", "WholeTooth");
        var chart = await RecordAsync("16", "O", "Fracture");
        chart = await RecordAsync("26", null, "Abscess");
        Assert.Equal(new[] { ("Tooth fracture", "Surface", "O"), ("Abscess", "WholeTooth", (string?)null) }, chart.Findings.Select(f => (f.ConditionLabel, f.ConditionScope, f.Surface)));
        Assert.Equal(new[] { "Surface", "WholeTooth" }, (await WithDb(db => db.ToothFindings.AsNoTracking().OrderBy(f => f.ToothKey).Select(f => f.ConditionScope).ToListAsync())));      // the scope is stored on the finding
        var refused = await Refused(() => RecordAsync("36", null, "Fracture"));                      // a surface condition still needs a surface
        Assert.True(refused.FieldErrors.ContainsKey("surface"));
        Assert.True((await Refused(() => RecordAsync("36", "O", "Abscess"))).FieldErrors.ContainsKey("surface"));       // and a whole-tooth one takes none
    }

    [Theory]
    [InlineData("code", "")] [InlineData("code", "f")] [InlineData("code", "fracture")] [InlineData("code", "1Fracture")] [InlineData("code", "Frac ture")] [InlineData("code", "Frac-ture")]
    [InlineData("code", "Abcdefghijklmnopqrstuvwxyz0123456")]      // 33 characters
    [InlineData("label", "")] [InlineData("label", "   ")]
    [InlineData("scope", "Area")] [InlineData("scope", "surface")] [InlineData("appliesTo", "Adult")] [InlineData("toothEffect", "Gone")]
    public async Task A_condition_with_a_malformed_field_is_refused_naming_the_field_and_stores_nothing(string field, string value)
    {
        string code = "Fracture", label = "Fracture", scope = "Surface", applies = "Both", effect = "None";
        switch (field) { case "code": code = value; break; case "label": label = value; break; case "scope": scope = value; break; case "appliesTo": applies = value; break; default: effect = value; break; }
        var refused = await Refused(() => CreateAsync(code, label, scope, applies, effect));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey(field), string.Join(",", refused.FieldErrors.Keys));
        Assert.Equal(6, await S.CountAsync(db => db.ConditionTypes));
        Assert.Empty(await TypeAuditAsync());
    }

    [Fact]
    public async Task A_label_over_80_characters_is_refused()
        => Assert.True((await Refused(() => CreateAsync(label: new string('x', 81)))).FieldErrors.ContainsKey("label"));

    [Fact]
    public async Task Every_wrong_field_is_named_at_once()
    {
        var refused = await Refused(() => CreateAsync("x", "", "Area", "Adult", "Gone"));
        Assert.Equal(new[] { "appliesTo", "code", "label", "scope", "toothEffect" }, refused.FieldErrors.Keys.Order());
    }

    [Fact]
    public async Task The_same_code_in_another_case_is_a_duplicate_but_the_identical_condition_again_is_a_quiet_repeat()
    {
        await CreateAsync("Fracture", "Tooth fracture");
        var repeat = await CreateAsync("Fracture", "Tooth fracture");                              // a retried request
        Assert.Equal(7, repeat.Count);
        Assert.Single(await TypeAuditAsync());

        var differs = await Refused(() => CreateAsync("Fracture", "A different meaning"));
        Assert.Equal(("condition_exists", 409), (differs.Code, differs.StatusCode));
        var otherCase = await Refused(() => CreateAsync("FRACTURE", "Tooth fracture"));
        Assert.Equal("condition_exists", otherCase.Code);
        Assert.Equal("condition_exists", (await Refused(() => CreateAsync("Caries", "Caries again", "WholeTooth"))).Code);     // a seeded code cannot be redefined either
        Assert.Equal(7, await S.CountAsync(db => db.ConditionTypes));
    }

    [Fact]
    public async Task Six_simultaneous_creates_of_the_same_condition_store_it_once()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await CreateAsync("Fracture", "Tooth fracture"); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));                                          // identical retries all get the one condition
        Assert.Equal(7, await S.CountAsync(db => db.ConditionTypes));
        Assert.Equal(1, await S.CountAsync(db => db.ConditionTypeEvents.Where(e => e.Code == "Fracture")));
        Assert.Single(await TypeAuditAsync());
    }

    // ---------- retiring and reactivating ----------

    [Fact]
    public async Task Retiring_a_condition_needs_a_reason_and_stops_new_findings_but_every_existing_one_still_reads_as_it_did()
    {
        var created = Find(await CreateAsync("Fracture", "Tooth fracture"), "Fracture");
        await RecordAsync("16", "O", "Fracture");
        var refused = await Refused(() => Types(s => s.RetireAsync(created.Id, "  ", created.RowVersion, S.Actor, default)));
        Assert.Equal(("reason_required", 400), (refused.Code, refused.StatusCode));

        var list = await Types(s => s.RetireAsync(created.Id, "  Replaced by a more specific condition  ", created.RowVersion, Other, default));
        Assert.False(Find(list, "Fracture").IsActive);
        Assert.Equal("Fracture", list[^1].Code);                                                    // retired ones are listed after the active ones

        var blocked = await Refused(() => RecordAsync("26", "O", "Fracture"));
        Assert.Equal(("condition_inactive", 409), (blocked.Code, blocked.StatusCode));
        var chart = await Chart(s => s.ChartAsync(Ann, default));
        Assert.Equal(("Tooth fracture", "Diagnosed"), (Assert.Single(chart.Findings).ConditionLabel, chart.Findings[0].State));        // the existing finding is untouched
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindings));

        var history = await WithDb(db => new ConditionTypeService(db, Clock).HistoryAsync(created.Id, default));
        Assert.Equal(new[] { "Created", "Retired" }, history.Select(h => h.ChangeType));
        Assert.Equal(("Replaced by a more specific condition", "Hana Hygienist"), (history[1].Reason, history[1].ActorName));
        Assert.Equal(new[] { "ConditionTypeCreated", "ConditionTypeRetired" }, (await TypeAuditAsync()).Select(a => a.Type));
    }

    [Fact]
    public async Task Reactivating_a_condition_makes_it_available_again_and_both_repeats_are_quiet()
    {
        var created = Find(await CreateAsync(), "Fracture");
        var retired = Find(await Types(s => s.RetireAsync(created.Id, "Not used", created.RowVersion, S.Actor, default)), "Fracture");
        var again = await Types(s => s.RetireAsync(created.Id, "Not used", retired.RowVersion, S.Actor, default));                // retiring a retired one
        Assert.False(Find(again, "Fracture").IsActive);
        Assert.Equal(2, await S.CountAsync(db => db.ConditionTypeEvents.Where(e => e.Code == "Fracture")));

        var back = Find(await Types(s => s.ReactivateAsync(created.Id, "Needed again", retired.RowVersion, S.Actor, default)), "Fracture");
        Assert.True(back.IsActive);
        await RecordAsync("16", "O", "Fracture");                                                    // usable again
        await Types(s => s.ReactivateAsync(created.Id, null, back.RowVersion, S.Actor, default));    // reactivating an active one
        Assert.Equal(3, await S.CountAsync(db => db.ConditionTypeEvents.Where(e => e.Code == "Fracture")));
        var history = await WithDb(db => new ConditionTypeService(db, Clock).HistoryAsync(created.Id, default));
        Assert.Equal(new[] { "Created", "Retired", "Reactivated" }, history.Select(h => h.ChangeType));
        Assert.Equal("Needed again", history[2].Reason);
    }

    [Fact]
    public async Task A_stale_or_missing_version_is_refused_and_nothing_changes()
    {
        var created = Find(await CreateAsync(), "Fracture");
        await Types(s => s.RetireAsync(created.Id, "First", created.RowVersion, S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Types(s => s.ReactivateAsync(created.Id, null, created.RowVersion, Other, default)));       // still holding the old version
        Assert.Equal(("row_version_required", 400), await CodeAsync(() => Types(s => s.RetireAsync(created.Id, "x", null, S.Actor, default))));
        Assert.Equal(("row_version_invalid", 400), await CodeAsync(() => Types(s => s.RetireAsync(created.Id, "x", "not base64!", S.Actor, default))));
        Assert.Equal("condition_not_found", (await Refused(() => Types(s => s.RetireAsync(Guid.NewGuid(), "x", created.RowVersion, S.Actor, default)))).Code);
        Assert.False(await S.CountAsync(db => db.ConditionTypes.Where(c => c.Code == "Fracture" && c.IsActive)) > 0);
    }

    private static async Task<(string, int)> CodeAsync<T>(Func<Task<T>> action) { var e = await Assert.ThrowsAsync<OdontogramException>(action); return (e.Code, e.StatusCode); }

    [Fact]
    public async Task Six_simultaneous_retirements_from_one_version_apply_exactly_once()
    {
        var created = Find(await CreateAsync(), "Fracture");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await Types(s => s.RetireAsync(created.Id, "Not used", created.RowVersion, S.Actor, default)); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Contains("ok", outcomes);
        Assert.Equal(2, await S.CountAsync(db => db.ConditionTypeEvents.Where(e => e.Code == "Fracture")));
        Assert.Equal(1, (await TypeAuditAsync()).Count(a => a.Type == "ConditionTypeRetired"));
    }

    [Fact]
    public async Task The_history_of_an_unknown_condition_is_a_404()
        => Assert.Equal("condition_not_found", (await Refused(() => WithDb(db => new ConditionTypeService(db, Clock).HistoryAsync(Guid.NewGuid(), default)))).Code);

    // ---------- unknown, misspelled and dentition-specific conditions ----------

    [Theory]
    [InlineData("Cavity")] [InlineData("caries")] [InlineData("CARIES")] [InlineData("")] [InlineData(null)]
    public async Task An_unknown_or_misspelled_condition_is_refused_naming_the_field(string? condition)
    {
        var refused = await Refused(() => RecordAsync("16", "O", condition!));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey("condition"));
        Assert.Equal(0, await S.CountAsync(db => db.ToothFindings));
    }

    [Fact]
    public async Task An_implant_is_not_recorded_on_a_primary_tooth_but_is_on_a_permanent_one_and_other_conditions_apply_to_both()
    {
        var refused = await Refused(() => RecordAsync("54", null, "Implant", "Existing"));
        Assert.Equal("validation_failed", refused.Code);
        Assert.Contains("primary", refused.FieldErrors["condition"]);
        await RecordAsync("16", null, "Implant", "Existing");
        await RecordAsync("54", "O", "Caries");
        await RecordAsync("54", null, "Crown", "Existing");                                         // a crown on a primary tooth is ordinary
        await RecordAsync("16", "O", "Caries");
        Assert.Equal(4, await S.CountAsync(db => db.ToothFindings));
    }

    [Fact]
    public async Task A_condition_made_for_one_dentition_is_refused_on_the_other()
    {
        await CreateAsync("Stainless", "Stainless steel crown", "WholeTooth", "Primary");
        await CreateAsync("Veneer", "Veneer", "WholeTooth", "Permanent");
        await RecordAsync("55", null, "Stainless", "Existing");
        await RecordAsync("11", null, "Veneer", "Existing");
        Assert.Contains("permanent", (await Refused(() => RecordAsync("16", null, "Stainless", "Existing"))).FieldErrors["condition"]);
        Assert.Contains("primary", (await Refused(() => RecordAsync("51", null, "Veneer", "Existing"))).FieldErrors["condition"]);
    }

    // ---------- a tooth that is not there ----------

    [Fact]
    public async Task A_tooth_recorded_as_missing_takes_no_other_finding_but_an_implant_and_only_while_it_is_missing()
    {
        await RecordAsync("16", null, "Missing", "Existing");
        var refused = await Refused(() => RecordAsync("16", "O", "Caries"));
        Assert.Equal(("tooth_absent", 409), (refused.Code, refused.StatusCode));
        Assert.Equal("tooth_absent", (await Refused(() => RecordAsync("16", null, "Crown", "Planned"))).Code);
        await RecordAsync("26", "O", "Caries");                                                     // another tooth is unaffected
        await RecordAsync("16", null, "Implant", "Planned");                                        // an implant stands in for the missing tooth

        var chart = await Chart(s => s.ChartAsync(Ann, default));
        var missing = chart.Findings.Single(f => f.Condition == "Missing");
        await Chart(s => s.WithdrawAsync(missing.Id, "Entered on the wrong tooth", missing.RowVersion, S.Actor, default));
        await RecordAsync("16", "O", "Caries");                                                     // present again once the entry is withdrawn
        Assert.Equal(3, (await Chart(s => s.ChartAsync(Ann, default))).Findings.Count);          // the withdrawn missing-tooth entry is off the chart
    }

    [Theory]
    [InlineData("Planned", false)]       // a planned extraction does not make the tooth absent yet
    [InlineData("Diagnosed", false)]
    [InlineData("Existing", true)]
    [InlineData("Completed", true)]
    public async Task Only_a_missing_tooth_that_is_existing_or_completed_makes_the_tooth_absent(string state, bool blocks)
    {
        await RecordAsync("46", null, "Missing", state);
        if (blocks) Assert.Equal("tooth_absent", (await Refused(() => RecordAsync("46", "O", "Caries"))).Code);
        else Assert.Single((await RecordAsync("46", "O", "Caries")).Findings.Where(f => f.Condition == "Caries"));
    }

    [Fact]
    public async Task A_tooth_absent_rule_applies_to_primary_teeth_and_to_any_condition_made_with_the_absent_effect_and_does_not_reach_another_patient()
    {
        await CreateAsync("Extracted", "Extracted", "WholeTooth", "Both", "Absent");
        await RecordAsync("55", null, "Extracted", "Completed");
        Assert.Equal("tooth_absent", (await Refused(() => RecordAsync("55", "O", "Caries"))).Code);
        await RecordAsync("55", "O", "Caries", patient: Bo);                                        // the same tooth on another patient
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindings));
    }
}
