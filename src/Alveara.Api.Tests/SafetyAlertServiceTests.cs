using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N011 alert behaviour, against real SQL Server: the alert lifecycle and its history (who, when, why), acknowledged-versus-resolved, the source and reason rules, no invented
/// alerts, PHI-free audit, and every failure path - a missing source, resolving without a reason, a stale edit or acknowledgement, a source that went away, and simultaneous writers.
/// </summary>
public class SafetyAlertServiceTests : SafetyTestBase
{
    // ---------- categories and nothing invented ----------

    [Theory]
    [InlineData(SafetyCategories.Condition)]
    [InlineData(SafetyCategories.Pregnancy)]
    [InlineData(SafetyCategories.Anticoagulant)]
    [InlineData(SafetyCategories.AdverseReaction)]
    [InlineData(SafetyCategories.Custom)]
    public async Task Each_alert_category_a_clinician_can_state_can_be_represented_with_its_source(string category)
    {
        var c = await AddAlertAsync("Something to know", category, SafetySeverities.Moderate, "Details here", "Told by the patient's cardiologist");
        var a = AlertNamed(c, "Something to know");
        Assert.Equal((category, SafetySeverities.Moderate, SafetyAlertStatuses.Active, SafetyOrigins.Alert), (a.Category, a.Severity, a.Status, a.Origin));
        Assert.Equal(("Told by the patient's cardiologist", "Dr. Okafor", 1), (a.Source, a.LastUpdatedByName, a.Revision));
        Assert.Equal(1, c.Summary.ActiveAlertCount);
    }

    [Fact]
    public async Task A_patient_with_nothing_recorded_has_no_alerts_and_none_is_invented_to_fill_the_gap()
    {
        var c = await ContextAsync();
        Assert.Empty(c.Entries);
        Assert.Empty(c.Resolved);
        Assert.Empty(c.Clearances);
        Assert.Equal((0, 0, 0, 0, 0, (string?)null), (c.Summary.ActiveAlertCount, c.Summary.ActiveAllergyCount, c.Summary.CurrentMedicationCount, c.Summary.OpenClearanceCount, c.Summary.UnacknowledgedAlertCount, c.Summary.HighestSeverity));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlerts));
        Assert.Equal(3, c.Gaps.Count);                                    // what is not established is a gap, not an alert
        Assert.All(c.Gaps, g => Assert.Contains("Nothing listed here does not mean none", g.Message));
    }

    [Theory]
    [InlineData("Allergy")]
    [InlineData("Medication")]
    [InlineData("Clearance")]
    [InlineData("condition")]
    [InlineData("")]
    public async Task Allergies_medications_and_clearances_are_not_alert_categories_and_an_unknown_one_is_refused(string category)
    {
        var refused = await RefusedAsync(() => AddAlertAsync(category: category));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey("category"));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlerts));
    }

    [Fact]
    public async Task An_alert_with_no_source_is_refused_and_stores_nothing()
    {
        var refused = await RefusedAsync(() => AddAlertAsync(source: "   "));
        Assert.Equal(("source_required", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey("sourceNote"));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlerts));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlertVersions));
        Assert.Empty(await AuditAsync(nameof(SafetyAlert)));
    }

    [Fact]
    public async Task A_missing_source_among_other_problems_is_reported_with_all_of_them()
    {
        var refused = await RefusedAsync(() => AlertsAsync(s => s.CreateAsync(Ann, "Nope", "", null, "Urgent", null, null, S.Actor, default)));
        Assert.Equal("validation_failed", refused.Code);
        Assert.Equal(new[] { "category", "severity", "sourceNote", "title" }, refused.FieldErrors.Keys.Order());
    }

    [Fact]
    public async Task Limits_are_enforced_and_text_at_the_limit_is_kept()
    {
        var tooLong = await RefusedAsync(() => AddAlertAsync(title: new string('t', SafetyRules.TitleMax + 1), detail: new string('d', SafetyRules.DetailMax + 1), source: new string('s', SafetyRules.SourceMax + 1)));
        Assert.Equal(new[] { "detail", "sourceNote", "title" }, tooLong.FieldErrors.Keys.Order());
        var c = await AddAlertAsync(new string('t', SafetyRules.TitleMax), detail: new string('d', SafetyRules.DetailMax), source: new string('s', SafetyRules.SourceMax));
        Assert.Single(c.Entries);
    }

    [Fact]
    public async Task An_unknown_patient_is_refused_and_one_patients_alert_never_shows_on_another()
    {
        Assert.Equal("patient_not_found", (await RefusedAsync(() => AddAlertAsync(patient: Guid.NewGuid()))).Code);
        await AddAlertAsync();
        Assert.Empty((await ContextAsync(Bo)).Entries);
        await Assert.ThrowsAsync<SafetyException>(() => ContextAsync(Guid.NewGuid()));
    }

    // ---------- lifecycle and history ----------

    [Fact]
    public async Task An_alert_can_be_changed_resolved_and_reopened_and_every_step_keeps_who_when_and_why()
    {
        var c = await AddAlertAsync("Anticoagulant therapy", SafetyCategories.Anticoagulant, SafetySeverities.High);
        var a = AlertNamed(c, "Anticoagulant therapy");
        c = await AlertsAsync(s => s.UpdateAsync(a.Id, "Warfarin", "INR 2.5", SafetySeverities.Critical, "Cardiology letter 2030-01-02", "Dose and risk confirmed", a.RowVersion, Other, default));
        a = AlertNamed(c, "Warfarin");
        Assert.Equal((SafetySeverities.Critical, "INR 2.5", "Hana Hygienist", 2), (a.Severity, a.Detail, a.LastUpdatedByName, a.Revision));

        c = await AlertsAsync(s => s.ResolveAsync(a.Id, "Therapy stopped by the cardiologist", a.RowVersion, S.Actor, default));
        a = AlertNamed(c, "Warfarin");
        Assert.Equal((SafetyAlertStatuses.Resolved, "Therapy stopped by the cardiologist", "Dr. Okafor"), (a.Status, a.ResolutionReason, a.ResolvedByName));
        Assert.NotNull(a.ResolvedAtUtc);
        Assert.Empty(c.Entries);                                          // no longer active...
        Assert.Single(c.Resolved);                                        // ...but still there, with who and why

        c = await AlertsAsync(s => s.ReopenAsync(a.Id, "Restarted in March", a.RowVersion, Other, default));
        a = AlertNamed(c, "Warfarin");
        Assert.Equal((SafetyAlertStatuses.Active, (string?)null, (DateTimeOffset?)null, 4), (a.Status, a.ResolutionReason, a.ResolvedAtUtc, a.Revision));

        var history = await WithDb(db => new SafetyAlertService(db, Clock).HistoryAsync(a.Id, default));
        Assert.Equal(new[] { 1, 2, 3, 4 }, history.Versions.Select(v => v.VersionNumber));
        Assert.Equal(new[] { SafetyChangeTypes.Created, SafetyChangeTypes.Changed, SafetyChangeTypes.Resolved, SafetyChangeTypes.Reopened }, history.Versions.Select(v => v.ChangeType));
        Assert.Equal(new[] { "Anticoagulant therapy", "Warfarin", "Warfarin", "Warfarin" }, history.Versions.Select(v => v.Title)); // what it said before is still there
        Assert.Equal(new[] { SafetyAlertStatuses.Active, SafetyAlertStatuses.Active, SafetyAlertStatuses.Resolved, SafetyAlertStatuses.Active }, history.Versions.Select(v => v.Status));
        Assert.Equal(new string?[] { null, "Dose and risk confirmed", "Therapy stopped by the cardiologist", "Restarted in March" }, history.Versions.Select(v => v.Reason));
        Assert.Equal(new[] { "Dr. Okafor", "Hana Hygienist", "Dr. Okafor", "Hana Hygienist" }, history.Versions.Select(v => v.ActorName));
    }

    [Fact]
    public async Task Resolving_an_alert_needs_a_reason_and_a_blank_one_changes_nothing()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        foreach (var blank in new string?[] { null, "", "   " })
        {
            var refused = await RefusedAsync(() => AlertsAsync(s => s.ResolveAsync(a.Id, blank, a.RowVersion, S.Actor, default)));
            Assert.Equal(("reason_required", 400), (refused.Code, refused.StatusCode));
            Assert.True(refused.FieldErrors.ContainsKey("reason"));
        }
        Assert.Equal(SafetyAlertStatuses.Active, AlertNamed(await ContextAsync(), "Prosthetic heart valve").Status);
        Assert.Single(await WithDb(db => db.SafetyAlertVersions.Where(v => v.AlertId == a.Id).ToListAsync()));
        var resolved = AlertNamed(await AlertsAsync(s => s.ResolveAsync(a.Id, "ok", a.RowVersion, S.Actor, default)), "Prosthetic heart valve");
        Assert.Equal("reason_required", (await RefusedAsync(() => AlertsAsync(s => s.ReopenAsync(a.Id, "", resolved.RowVersion, S.Actor, default)))).Code); // reopening needs a reason too
        Assert.Equal(SafetyAlertStatuses.Resolved, AlertNamed(await ContextAsync(), "Prosthetic heart valve").Status);
    }

    [Fact]
    public async Task A_resolved_alert_cannot_be_edited_until_it_is_reopened_and_resolve_and_reopen_repeats_are_quiet()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        var resolved = AlertNamed(await AlertsAsync(s => s.ResolveAsync(a.Id, "Entered in error", a.RowVersion, S.Actor, default)), "Prosthetic heart valve");
        Assert.Equal("alert_resolved", (await RefusedAsync(() => AlertsAsync(s => s.UpdateAsync(a.Id, "New", null, SafetySeverities.Low, "x", null, resolved.RowVersion, S.Actor, default)))).Code);

        var again = AlertNamed(await AlertsAsync(s => s.ResolveAsync(a.Id, "Entered in error", resolved.RowVersion, Other, default)), "Prosthetic heart valve");
        Assert.Equal((resolved.RowVersion, resolved.ResolvedByName), (again.RowVersion, again.ResolvedByName)); // the first resolution stands
        var reopened = AlertNamed(await AlertsAsync(s => s.ReopenAsync(a.Id, "Not an error", resolved.RowVersion, S.Actor, default)), "Prosthetic heart valve");
        var reopenedAgain = AlertNamed(await AlertsAsync(s => s.ReopenAsync(a.Id, "Again", reopened.RowVersion, S.Actor, default)), "Prosthetic heart valve");
        Assert.Equal(reopened.RowVersion, reopenedAgain.RowVersion);
        Assert.Equal(3, (await WithDb(db => new SafetyAlertService(db, Clock).HistoryAsync(a.Id, default))).Versions.Count);
    }

    [Fact]
    public async Task Saving_unchanged_values_and_creating_the_same_alert_again_change_nothing_and_a_different_one_with_the_same_title_is_refused()
    {
        var c = await AddAlertAsync("Pregnant", SafetyCategories.Pregnancy, SafetySeverities.High, "20 weeks");
        var a = AlertNamed(c, "Pregnant");
        var same = await AlertsAsync(s => s.UpdateAsync(a.Id, "Pregnant", "20 weeks", SafetySeverities.High, "Reported by the patient at intake", null, a.RowVersion, S.Actor, default));
        Assert.Equal(a.RowVersion, AlertNamed(same, "Pregnant").RowVersion);
        var retried = await AddAlertAsync("Pregnant", SafetyCategories.Pregnancy, SafetySeverities.High, "20 weeks");
        Assert.Single(retried.Entries);
        Assert.Equal(1, await S.CountAsync(db => db.SafetyAlerts));

        var different = await RefusedAsync(() => AddAlertAsync("Pregnant", SafetyCategories.Pregnancy, SafetySeverities.Low, "Different"));
        Assert.Equal(("duplicate_alert", 409), (different.Code, different.StatusCode));
        var other = AlertNamed(await AddAlertAsync("Latex sensitivity", SafetyCategories.Pregnancy, SafetySeverities.Moderate), "Latex sensitivity");
        Assert.Equal("duplicate_alert", (await RefusedAsync(() => AlertsAsync(s => s.UpdateAsync(other.Id, "Pregnant", null, SafetySeverities.Low, "x", null, other.RowVersion, S.Actor, default)))).Code); // renaming into another
    }

    [Fact]
    public async Task A_resolved_alert_can_be_stated_again_but_not_reopened_while_an_active_twin_exists()
    {
        var a = AlertNamed(await AddAlertAsync("Prosthetic heart valve"), "Prosthetic heart valve");
        await AlertsAsync(s => s.ResolveAsync(a.Id, "Wrong chart", a.RowVersion, S.Actor, default));
        await AddAlertAsync("Prosthetic heart valve");                                      // a new, correct one
        Assert.Equal(2, (await ContextAsync()).Entries.Concat((await ContextAsync()).Resolved).Count(e => e.Title == "Prosthetic heart valve"));
        var current = await WithDb(db => db.SafetyAlerts.AsNoTracking().SingleAsync(x => x.Id == a.Id));
        Assert.Equal("duplicate_alert", (await RefusedAsync(() => AlertsAsync(s => s.ReopenAsync(a.Id, "Back", Convert.ToBase64String(current.RowVersion), S.Actor, default)))).Code);
    }

    // ---------- acknowledged is not resolved ----------

    [Fact]
    public async Task Acknowledging_an_alert_records_that_the_person_saw_it_and_never_resolves_it()
    {
        var a = AlertNamed(await AddAlertAsync("Anticoagulant therapy", SafetyCategories.Anticoagulant, SafetySeverities.Critical), "Anticoagulant therapy");
        Assert.False(a.AcknowledgedByMe);
        Assert.Equal(1, (await ContextAsync()).Summary.UnacknowledgedAlertCount);

        var c = await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default));
        var after = AlertNamed(c, "Anticoagulant therapy");
        Assert.Equal((true, SafetyAlertStatuses.Active, a.RowVersion), (after.AcknowledgedByMe, after.Status, after.RowVersion)); // seen, still active, nothing changed
        Assert.NotNull(after.AcknowledgedAtUtc);
        Assert.Equal((1, 0), (c.Summary.ActiveAlertCount, c.Summary.UnacknowledgedAlertCount));
        Assert.Single(c.Entries);
        Assert.Empty(c.Resolved);
        Assert.Equal(1, (await WithDb(db => new SafetyAlertService(db, Clock).HistoryAsync(a.Id, default))).Versions.Count);   // acknowledging added no version
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlerts.Where(x => x.Status == SafetyAlertStatuses.Resolved)));
        var audit = await AuditAsync(nameof(SafetyAlert));
        Assert.Contains(audit, e => e.Type == "SafetyAlertAcknowledged" && e.Details.Contains("still active"));
    }

    [Fact]
    public async Task An_acknowledgement_belongs_to_one_person_and_one_revision_and_a_change_means_it_must_be_seen_again()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default));
        Assert.False(AlertNamed(await ContextAsync(user: Other), "Prosthetic heart valve").AcknowledgedByMe);   // someone else has not seen it
        Assert.True(AlertNamed(await ContextAsync(user: S.Actor), "Prosthetic heart valve").AcknowledgedByMe);

        var changed = AlertNamed(await AlertsAsync(s => s.UpdateAsync(a.Id, "Prosthetic heart valve", "Mechanical, 2019", SafetySeverities.Critical, a.Source, null, a.RowVersion, Other, default)), "Prosthetic heart valve");
        Assert.False(changed.AcknowledgedByMe);                          // the new revision has not been seen by the person who saw the old one
        Assert.Equal(1, (await ContextAsync(user: S.Actor)).Summary.UnacknowledgedAlertCount);
        await AlertsAsync(s => s.AcknowledgeAsync(a.Id, changed.Revision, S.Actor, default));
        Assert.True(AlertNamed(await ContextAsync(user: S.Actor), "Prosthetic heart valve").AcknowledgedByMe);
        Assert.Equal(2, await S.CountAsync(db => db.SafetyAlertAcknowledgements));
    }

    [Fact]
    public async Task Acknowledging_an_old_revision_is_refused_so_nobody_acknowledges_what_changed_under_them()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        await AlertsAsync(s => s.UpdateAsync(a.Id, "Prosthetic heart valve", "Now critical", SafetySeverities.Critical, a.Source, null, a.RowVersion, Other, default));
        var refused = await RefusedAsync(() => AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default)));
        Assert.Equal(("alert_changed", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlertAcknowledgements));
        Assert.Equal("validation_failed", (await RefusedAsync(() => AlertsAsync(s => s.AcknowledgeAsync(a.Id, null, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task Acknowledging_twice_is_quiet_a_resolved_alert_has_nothing_to_acknowledge_and_a_missing_alert_is_not_found()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default));
        await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default));
        Assert.Equal(1, await S.CountAsync(db => db.SafetyAlertAcknowledgements));
        Assert.Equal(1, (await AuditAsync(nameof(SafetyAlert))).Count(e => e.Type == "SafetyAlertAcknowledged"));

        var resolved = AlertNamed(await AlertsAsync(s => s.ResolveAsync(a.Id, "Entered in error", a.RowVersion, S.Actor, default)), "Prosthetic heart valve");
        Assert.Equal("alert_resolved", (await RefusedAsync(() => AlertsAsync(s => s.AcknowledgeAsync(a.Id, resolved.Revision, Other, default)))).Code);
        Assert.Equal("alert_not_found", (await RefusedAsync(() => AlertsAsync(s => s.AcknowledgeAsync(Guid.NewGuid(), 1, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task Six_people_acknowledging_at_once_each_leave_one_acknowledgement_and_the_alert_stays_active()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        var users = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var outcomes = await Task.WhenAll(users.SelectMany(u => new[] { u, u }).Select(u => Task.Run(async () =>
        {
            try { await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, u, default)); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));
        Assert.Equal(6, await S.CountAsync(db => db.SafetyAlertAcknowledgements));
        Assert.Equal(SafetyAlertStatuses.Active, AlertNamed(await ContextAsync(), "Prosthetic heart valve").Status);
    }

    // ---------- the source ----------

    [Fact]
    public async Task An_alert_can_cite_the_clinical_record_item_it_is_about_and_the_link_is_provenance_only()
    {
        var item = await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Atrial fibrillation");
        var c = await AddAlertAsync("Atrial fibrillation", SafetyCategories.Condition, SafetySeverities.High, sourceItem: item.Id, source: "Clinical record: medical history");
        var a = AlertNamed(c, "Atrial fibrillation");
        Assert.Equal((item.Id, false), (a.SourceItemId, a.NeedsAttention));
        Assert.Equal(1, await S.CountAsync(db => db.ClinicalRecordItems));        // nothing was copied or changed in the record
    }

    [Fact]
    public async Task A_source_item_that_does_not_exist_belongs_to_another_patient_or_was_removed_is_refused()
    {
        Assert.Equal("source_not_found", (await RefusedAsync(() => AddAlertAsync(sourceItem: Guid.NewGuid()))).Code);
        var boItem = await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Asthma", patient: Bo);
        Assert.Equal("source_patient_mismatch", (await RefusedAsync(() => AddAlertAsync(sourceItem: boItem.Id))).Code);
        var item = await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Epilepsy");
        await WithDb(db => new ClinicalRecordService(db, Clock).RemoveInErrorAsync(item.Id, Convert.ToBase64String(item.RowVersion), "Wrong chart", null, S.Actor, default));
        Assert.Equal("source_not_found", (await RefusedAsync(() => AddAlertAsync(sourceItem: item.Id))).Code);
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlerts));
    }

    [Fact]
    public async Task When_the_source_item_is_later_resolved_or_removed_the_alert_is_flagged_for_review_not_silently_changed()
    {
        var item = await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Atrial fibrillation");
        await AddAlertAsync("Atrial fibrillation", sourceItem: item.Id, source: "Clinical record: medical history");
        var resolvedItem = await WithDb(db => new ClinicalRecordService(db, Clock).SetStatusAsync(item.Id, ClinicalItemStatuses.Resolved, Convert.ToBase64String(item.RowVersion), "Treated", null, S.Actor, default));
        var a = AlertNamed(await ContextAsync(), "Atrial fibrillation");
        Assert.Equal((SafetyAlertStatuses.Active, true), (a.Status, a.NeedsAttention));      // still active - a person decides
        Assert.Contains("now resolved", a.AttentionReason);
        Assert.Equal(1, (await ContextAsync()).Summary.NeedsAttentionCount);
        Assert.NotNull(resolvedItem);

        var item2 = await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Epilepsy");
        await AddAlertAsync("Epilepsy", sourceItem: item2.Id, source: "Clinical record: medical history");
        await WithDb(db => new ClinicalRecordService(db, Clock).RemoveInErrorAsync(item2.Id, Convert.ToBase64String(item2.RowVersion), "Wrong chart", null, S.Actor, default));
        Assert.Contains("was removed", AlertNamed(await ContextAsync(), "Epilepsy").AttentionReason);
    }

    // ---------- audit ----------

    [Fact]
    public async Task Every_change_is_audited_with_user_and_time_and_no_audit_text_contains_the_alert_content()
    {
        var a = AlertNamed(await AddAlertAsync("HIV positive", SafetyCategories.Condition, SafetySeverities.High, "On treatment since 2015", "Disclosed to Dr. Okafor"), "HIV positive");
        var c = await AlertsAsync(s => s.UpdateAsync(a.Id, "HIV positive", "Undetectable", SafetySeverities.High, a.Source, "Latest lab", a.RowVersion, Other, default));
        a = AlertNamed(c, "HIV positive");
        await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default));
        await AlertsAsync(s => s.ResolveAsync(a.Id, "Entered on the wrong chart", a.RowVersion, S.Actor, default));
        a = AlertNamed(await ContextAsync(), "HIV positive");
        await AlertsAsync(s => s.ReopenAsync(a.Id, "It was correct after all", a.RowVersion, Other, default));

        var audit = await AuditAsync(nameof(SafetyAlert));
        Assert.Equal(new[] { "SafetyAlertCreated", "SafetyAlertChanged", "SafetyAlertAcknowledged", "SafetyAlertResolved", "SafetyAlertReopened" }, audit.Select(e => e.Type));
        Assert.Equal(new Guid?[] { S.Actor, Other, S.Actor, S.Actor, Other }, audit.Select(e => e.By));
        var text = string.Join(" ", audit.Select(e => e.Details));
        foreach (var secret in new[] { "HIV", "treatment", "Undetectable", "Disclosed", "lab", "wrong chart", "correct after all", "Condition", SafetyCategories.Condition }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_change_is_not_made_and_the_same_request_works_on_retry()
    {
        await using (var failing = S.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new SafetyAlertService(failing, Clock).CreateAsync(Ann, SafetyCategories.Condition, "Asthma", null, SafetySeverities.Low, "Reported", null, S.Actor, default));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlerts));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlertVersions));

        var a = AlertNamed(await AddAlertAsync("Asthma"), "Asthma");
        await using (var failing = S.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new SafetyAlertService(failing, Clock).ResolveAsync(a.Id, "Resolved", a.RowVersion, S.Actor, default));
        Assert.Equal(SafetyAlertStatuses.Active, AlertNamed(await ContextAsync(), "Asthma").Status);
        await using (var failing = S.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new SafetyAlertService(failing, Clock).AcknowledgeAsync(a.Id, a.Revision, S.Actor, default));
        Assert.Equal(0, await S.CountAsync(db => db.SafetyAlertAcknowledgements));
        Assert.True(AlertNamed(await AlertsAsync(s => s.AcknowledgeAsync(a.Id, a.Revision, S.Actor, default)), "Asthma").AcknowledgedByMe);
    }

    // ---------- stale and simultaneous ----------

    [Fact]
    public async Task A_stale_edit_resolve_or_reopen_is_refused_and_the_other_writers_change_stands()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        await AlertsAsync(s => s.UpdateAsync(a.Id, "Prosthetic heart valve", "First editor", SafetySeverities.Critical, a.Source, null, a.RowVersion, S.Actor, default));  // first editor, from version V
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AlertsAsync(s => s.ResolveAsync(a.Id, "Second editor", a.RowVersion, Other, default)));                   // second, still on V
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AlertsAsync(s => s.UpdateAsync(a.Id, "Prosthetic heart valve", "Stale", SafetySeverities.Low, a.Source, null, a.RowVersion, Other, default)));
        var now = AlertNamed(await ContextAsync(), "Prosthetic heart valve");
        Assert.Equal(("First editor", SafetyAlertStatuses.Active), (now.Detail, now.Status));
        Assert.Equal(2, (await WithDb(db => new SafetyAlertService(db, Clock).HistoryAsync(a.Id, default))).Versions.Count);   // the refused edits left nothing behind
        Assert.Equal(2, (await AuditAsync(nameof(SafetyAlert))).Count);
    }

    [Fact]
    public async Task A_missing_malformed_or_unknown_alert_version_is_refused_before_anything_changes()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        Assert.Equal("row_version_required", (await RefusedAsync(() => AlertsAsync(s => s.ResolveAsync(a.Id, "x", null, S.Actor, default)))).Code);
        Assert.Equal("row_version_invalid", (await RefusedAsync(() => AlertsAsync(s => s.ResolveAsync(a.Id, "x", "not base64!", S.Actor, default)))).Code);
        Assert.Equal("alert_not_found", (await RefusedAsync(() => AlertsAsync(s => s.ResolveAsync(Guid.NewGuid(), "x", a.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("alert_not_found", (await RefusedAsync(() => WithDb(db => new SafetyAlertService(db, Clock).HistoryAsync(Guid.NewGuid(), default)))).Code);
    }

    [Fact]
    public async Task Six_simultaneous_creates_of_the_same_alert_store_it_once()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await AddAlertAsync("Pregnant", SafetyCategories.Pregnancy, SafetySeverities.High); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));                 // identical retries all get the one alert
        Assert.Equal(1, await S.CountAsync(db => db.SafetyAlerts));
        Assert.Equal(1, await S.CountAsync(db => db.SafetyAlertVersions));
    }

    [Fact]
    public async Task Six_simultaneous_resolves_from_one_version_resolve_it_exactly_once()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await AlertsAsync(s => s.ResolveAsync(a.Id, "Resolved", a.RowVersion, S.Actor, default)); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Contains("ok", outcomes);
        Assert.Equal(2, (await WithDb(db => new SafetyAlertService(db, Clock).HistoryAsync(a.Id, default))).Versions.Count);
        Assert.Equal(1, (await AuditAsync(nameof(SafetyAlert))).Count(e => e.Type == "SafetyAlertResolved"));
    }

    [Fact]
    public async Task An_alert_is_never_deleted_resolving_it_keeps_every_row()
    {
        var a = AlertNamed(await AddAlertAsync(), "Prosthetic heart valve");
        await AlertsAsync(s => s.ResolveAsync(a.Id, "Resolved", a.RowVersion, S.Actor, default));
        Assert.Equal((1, 2), (await S.CountAsync(db => db.SafetyAlerts), await S.CountAsync(db => db.SafetyAlertVersions)));
    }
}
