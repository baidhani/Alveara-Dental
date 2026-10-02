using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Forms;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-011-C01 against real SQL Server: check-in readiness reports which required forms are really complete, never fabricates completion, and is read-only.
/// ALV-N010's own form tests are unchanged and run alongside these.
/// </summary>
public class CheckInReadinessTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private FormTestSupport _s = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new FormTestSupport(_fixture);
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<TemplateDetail> RequireAsync(TemplateDetail t, bool required = true)
    {
        await using var db = _fixture.CreateContext();
        return await _s.Templates(db).SetRequiredAtCheckInAsync(t.Template.Id, required, t.Template.RowVersion, _s.Actor, default);
    }

    private async Task<CheckInReadiness> ReadinessAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        return await new CheckInReadinessService(db).ForPatientAsync(patient, default);
    }

    private async Task<PatientFormDetail> SignedAsync(Guid patient, Guid templateId) => (await _s.SignAsync(await _s.ReadyAsync(patient, templateId))).Detail;

    // ---------- what readiness reports ----------

    [Fact]
    public async Task When_the_practice_requires_nothing_the_patient_is_ready_and_nothing_is_reported_as_required()
    {
        await _s.CreateTemplateAsync(); // exists but is not marked required

        var r = await ReadinessAsync(_ann);

        Assert.Equal((true, 0, 0), (r.Ready, r.RequiredCount, r.CompleteCount));
        Assert.Empty(r.Items);
    }

    [Fact]
    public async Task A_required_form_the_patient_has_not_started_is_missing_and_the_patient_is_not_ready()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());

        var r = await ReadinessAsync(_ann);

        Assert.Equal((false, 1, 0), (r.Ready, r.RequiredCount, r.CompleteCount));
        var item = Assert.Single(r.Items);
        Assert.Equal((t.Template.Id, "privacy-notice", "Privacy notice", ReadinessStatuses.Missing, 1, (int?)null), (item.TemplateId, item.TemplateKey, item.Title, item.Status, item.CurrentVersionNumber, item.SignedVersionNumber));
    }

    [Fact]
    public async Task A_draft_is_in_progress_and_viewing_or_saving_a_form_never_makes_it_complete()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        var draft = await _s.ReadyAsync(_ann, t.Template.Id); // every answer filled in, but not signed

        Assert.Equal(ReadinessStatuses.InProgress, Assert.Single((await ReadinessAsync(_ann)).Items).Status);
        await _s.ReloadAsync(draft.Summary.Id); // viewing it
        await using (var db = _fixture.CreateContext()) await _s.Reader(db).ListForPatientAsync(_ann, default);

        var r = await ReadinessAsync(_ann);
        Assert.Equal((false, ReadinessStatuses.InProgress), (r.Ready, r.Items[0].Status));
        Assert.Equal(FormStatuses.Draft, (await _s.ReloadAsync(draft.Summary.Id)).Summary.Status);
    }

    [Fact]
    public async Task A_form_signed_on_the_current_version_is_complete_and_the_patient_is_ready()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        await SignedAsync(_ann, t.Template.Id);

        var r = await ReadinessAsync(_ann);

        Assert.Equal((true, 1, 1), (r.Ready, r.RequiredCount, r.CompleteCount));
        Assert.Equal((ReadinessStatuses.Complete, 1, (int?)1), (r.Items[0].Status, r.Items[0].CurrentVersionNumber, r.Items[0].SignedVersionNumber));
    }

    [Fact]
    public async Task A_signature_on_older_wording_is_never_reported_as_complete_until_the_current_version_is_signed()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        await SignedAsync(_ann, t.Template.Id);
        await using (var db = _fixture.CreateContext())
            await _s.Templates(db).PublishVersionAsync(t.Template.Id, FormTestSupport.Content(body: "We have updated how we protect your information."), (await _s.ReloadTemplateAsync(t.Template.Id)).Template.RowVersion, _s.Actor, default);

        var after = await ReadinessAsync(_ann);
        Assert.Equal((false, ReadinessStatuses.SignedEarlierVersion, 2, (int?)1), (after.Ready, after.Items[0].Status, after.Items[0].CurrentVersionNumber, after.Items[0].SignedVersionNumber));

        await SignedAsync(_ann, t.Template.Id); // signs version 2
        var done = await ReadinessAsync(_ann);
        Assert.Equal((true, ReadinessStatuses.Complete, (int?)2), (done.Ready, done.Items[0].Status, done.Items[0].SignedVersionNumber));
    }

    [Fact]
    public async Task A_draft_on_the_new_version_shows_in_progress_not_the_older_signature()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        await SignedAsync(_ann, t.Template.Id);
        await using (var db = _fixture.CreateContext())
            await _s.Templates(db).PublishVersionAsync(t.Template.Id, FormTestSupport.Content(body: "Updated wording."), (await _s.ReloadTemplateAsync(t.Template.Id)).Template.RowVersion, _s.Actor, default);
        await _s.StartAsync(_ann, t.Template.Id);

        Assert.Equal(ReadinessStatuses.InProgress, (await ReadinessAsync(_ann)).Items[0].Status);
    }

    [Fact]
    public async Task A_voided_signed_form_no_longer_counts_and_the_patient_goes_back_to_missing()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        var signed = await SignedAsync(_ann, t.Template.Id);
        Assert.True((await ReadinessAsync(_ann)).Ready);

        await using (var db = _fixture.CreateContext())
            await _s.Forms(db).VoidAsync(signed.Summary.Id, "Signed by the wrong person", signed.RowVersion, canVoidSigned: true, _s.Actor, default);

        var r = await ReadinessAsync(_ann);
        Assert.Equal((false, ReadinessStatuses.Missing), (r.Ready, r.Items[0].Status));
    }

    [Fact]
    public async Task A_form_marked_signed_without_its_signed_copy_is_not_complete()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        var draft = await _s.ReadyAsync(_ann, t.Template.Id);
        await using (var db = _fixture.CreateContext())
        {
            (await db.PatientForms.SingleAsync(f => f.Id == draft.Summary.Id)).Status = FormStatuses.Signed; // a status with no immutable copy behind it
            await db.SaveChangesAsync();
        }

        var r = await ReadinessAsync(_ann);

        Assert.Equal((false, ReadinessStatuses.Missing), (r.Ready, r.Items[0].Status)); // not fabricated as complete
    }

    [Fact]
    public async Task Another_patients_signature_never_counts_and_an_inactive_template_never_blocks()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        var bo = await _s.PatientAsync("Bo", "Kim");
        await SignedAsync(bo, t.Template.Id);

        Assert.Equal(ReadinessStatuses.Missing, (await ReadinessAsync(_ann)).Items[0].Status);
        Assert.Equal(ReadinessStatuses.Complete, (await ReadinessAsync(bo)).Items[0].Status);

        await using (var db = _fixture.CreateContext())
            await _s.Templates(db).SetActiveAsync(t.Template.Id, false, (await _s.ReloadTemplateAsync(t.Template.Id)).Template.RowVersion, _s.Actor, default);
        var r = await ReadinessAsync(_ann);
        Assert.Equal((true, 0), (r.Ready, r.RequiredCount)); // the only required template is inactive, so nothing can be required
    }

    [Fact]
    public async Task Several_required_forms_are_listed_in_order_with_an_honest_count()
    {
        var privacy = await RequireAsync(await _s.CreateTemplateAsync("privacy-notice", FormCategories.Privacy));
        var financial = await RequireAsync(await _s.CreateTemplateAsync("financial-policy", FormCategories.Financial, FormTestSupport.Content("Financial policy", "How we bill.")));
        await _s.CreateTemplateAsync("not-required", FormCategories.Treatment, FormTestSupport.Content("Optional consent", "Optional."));
        await SignedAsync(_ann, privacy.Template.Id);
        await _s.StartAsync(_ann, financial.Template.Id);

        var r = await ReadinessAsync(_ann);

        Assert.Equal((false, 2, 1), (r.Ready, r.RequiredCount, r.CompleteCount));
        Assert.Equal([("financial-policy", ReadinessStatuses.InProgress), ("privacy-notice", ReadinessStatuses.Complete)], r.Items.Select(i => (i.TemplateKey, i.Status))); // by category then key
    }

    [Fact]
    public async Task An_unknown_patient_is_a_404_and_never_reported_as_ready()
    {
        await RequireAsync(await _s.CreateTemplateAsync());
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<FormException>(() => new CheckInReadinessService(db).ForPatientAsync(Guid.NewGuid(), default));
        Assert.Equal(("patient_not_found", 404), (ex.Code, ex.StatusCode));
    }

    [Fact]
    public async Task Reading_readiness_for_many_patients_gives_each_patient_their_own_answer_and_writes_nothing()
    {
        var t = await RequireAsync(await _s.CreateTemplateAsync());
        var bo = await _s.PatientAsync("Bo", "Kim");
        var cy = await _s.PatientAsync("Cy", "Poe");
        await SignedAsync(_ann, t.Template.Id);
        await _s.StartAsync(bo, t.Template.Id);
        var writesBefore = await _s.CountAsync(db => db.AuditLogEntries) + await _s.CountAsync(db => db.PatientForms) + await _s.CountAsync(db => db.PatientFormEvents);

        await using var db = _fixture.CreateContext();
        var all = await new CheckInReadinessService(db).ForPatientsAsync([_ann, bo, cy], default);

        Assert.Equal([ReadinessStatuses.Complete, ReadinessStatuses.InProgress, ReadinessStatuses.Missing], [all[_ann].Items[0].Status, all[bo].Items[0].Status, all[cy].Items[0].Status]);
        foreach (var p in new[] { _ann, bo, cy }) Assert.Equivalent(await ReadinessAsync(p), all[p]); // the batch and the single read agree
        Assert.Equal(writesBefore, await _s.CountAsync(d => d.AuditLogEntries) + await _s.CountAsync(d => d.PatientForms) + await _s.CountAsync(d => d.PatientFormEvents));
    }

    // ---------- marking a template as required ----------

    [Fact]
    public async Task Marking_a_template_required_is_audited_with_user_and_time_publishes_no_version_and_a_repeat_changes_nothing()
    {
        var t = await _s.CreateTemplateAsync();
        Assert.False(t.Template.RequiredAtCheckIn);

        var required = await RequireAsync(t);

        Assert.True(required.Template.RequiredAtCheckIn);
        Assert.Equal((1, t.Template.Current!.ContentHash), (required.Template.VersionCount, required.Template.Current!.ContentHash)); // no new version
        await using (var db = _fixture.CreateContext())
        {
            var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == FormAuditEvents.TemplateRequirementChanged);
            Assert.Equal((_s.Actor, t.Template.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
            Assert.Contains("privacy-notice", audit.Details);
            Assert.InRange(audit.TimestampUtc, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddSeconds(5));
        }

        await using (var db = _fixture.CreateContext()) // the same setting again, even with the old version
            Assert.Equal(required.Template.RowVersion, (await _s.Templates(db).SetRequiredAtCheckInAsync(t.Template.Id, true, t.Template.RowVersion, _s.Actor, default)).Template.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == FormAuditEvents.TemplateRequirementChanged)));
    }

    [Fact]
    public async Task A_stale_version_is_the_shared_conflict_and_an_unknown_template_is_a_404()
    {
        var t = await _s.CreateTemplateAsync();
        await using (var db = _fixture.CreateContext())
            await _s.Templates(db).SetActiveAsync(t.Template.Id, false, t.Template.RowVersion, _s.Actor, default); // someone else changed it first

        await using (var db = _fixture.CreateContext())
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.Templates(db).SetRequiredAtCheckInAsync(t.Template.Id, true, t.Template.RowVersion, _s.Actor, default));
        Assert.False((await _s.ReloadTemplateAsync(t.Template.Id)).Template.RequiredAtCheckIn);

        await using (var db = _fixture.CreateContext())
        {
            var ex = await Assert.ThrowsAsync<FormException>(() => _s.Templates(db).SetRequiredAtCheckInAsync(Guid.NewGuid(), true, "AAAA", _s.Actor, default));
            Assert.Equal(("template_not_found", 404), (ex.Code, ex.StatusCode));
        }
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_requirement_does_not_change()
    {
        var t = await _s.CreateTemplateAsync();
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Templates(failing).SetRequiredAtCheckInAsync(t.Template.Id, true, t.Template.RowVersion, _s.Actor, default));

        Assert.False((await _s.ReloadTemplateAsync(t.Template.Id)).Template.RequiredAtCheckIn);
        Assert.True((await RequireAsync(t)).Template.RequiredAtCheckIn); // and the same request works once logging is back
    }
}
