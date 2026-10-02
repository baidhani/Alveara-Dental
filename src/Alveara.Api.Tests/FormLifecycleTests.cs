using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Patients;
using Xunit;
using static Alveara.Api.Tests.FormTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-N010: starting forms, template changes while a form is being completed, voiding/discarding, and the document-library seam.</summary>
public class FormLifecycleTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private FormTestSupport _s = null!;
    private TemplateDetail _template = null!;
    private Guid _patient;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new FormTestSupport(_fixture);
        _template = await _s.CreateTemplateAsync();
        _patient = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private Guid TemplateId => _template.Template.Id;

    private async Task<TemplateDetail> PublishV2Async(TemplateContent content)
    {
        var current = await _s.ReloadTemplateAsync(TemplateId);
        await using var db = _fixture.CreateContext();
        return await _s.Templates(db).PublishVersionAsync(TemplateId, content, current.Template.RowVersion, _s.Actor, default);
    }

    // ---------- starting ----------

    [Fact]
    public async Task Starting_a_form_pins_it_to_the_templates_current_version_and_is_audited_and_recorded_in_history()
    {
        var form = await _s.StartAsync(_patient, TemplateId);

        Assert.Equal(FormStatuses.Draft, form.Summary.Status);
        Assert.Equal(_template.Template.Current!.Id, form.Version.Id);
        Assert.Empty(form.Responses);
        Assert.Equal([FormEventTypes.Started], form.Events.Select(e => e.EventType));
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(a => a.EventType == FormAuditEvents.FormStarted);
        Assert.Equal(_s.Actor, audit.PerformedByUserAccountId);
        Assert.Contains("privacy-notice", audit.Details);
    }

    [Fact]
    public async Task Starting_the_same_form_twice_returns_the_open_draft_instead_of_a_second_one()
    {
        var first = await _s.StartAsync(_patient, TemplateId);
        var second = await _s.StartAsync(_patient, TemplateId);

        Assert.Equal(first.Summary.Id, second.Summary.Id);
        Assert.Equal(1, await _s.CountAsync(db => db.PatientForms));
    }

    [Fact]
    public async Task Eight_simultaneous_starts_leave_one_open_draft()
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => (await _s.StartAsync(_patient, TemplateId)).Summary.Id));

        Assert.Single(ids.Distinct());
        Assert.Equal(1, await _s.CountAsync(db => db.PatientForms));
    }

    [Fact]
    public async Task A_signed_form_does_not_block_starting_a_new_one_of_the_same_template()
    {
        await _s.SignAsync(await _s.ReadyAsync(_patient, TemplateId));

        var again = await _s.StartAsync(_patient, TemplateId);

        Assert.Equal(FormStatuses.Draft, again.Summary.Status);
        Assert.Equal(2, await _s.CountAsync(db => db.PatientForms));
    }

    [Fact]
    public async Task Different_patients_have_separate_forms_of_the_same_template()
    {
        var other = await _s.PatientAsync("Bo", "Kim");
        var a = await _s.StartAsync(_patient, TemplateId);
        var b = await _s.StartAsync(other, TemplateId);
        Assert.NotEqual(a.Summary.Id, b.Summary.Id);
        await using var db = _fixture.CreateContext();
        Assert.Single(await _s.Reader(db).ListForPatientAsync(_patient, default));
    }

    [Fact]
    public async Task An_unknown_patient_or_template_an_inactive_template_and_an_inactive_patient_are_refused()
    {
        await using var db = _fixture.CreateContext();
        Assert.Equal("patient_not_found", (await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).StartAsync(Guid.NewGuid(), TemplateId, _s.Actor, default))).Code);
        Assert.Equal("template_not_found", (await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).StartAsync(_patient, Guid.NewGuid(), _s.Actor, default))).Code);

        var p = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == _patient);
        await new PatientEditService(db, Clock).SetActiveAsync(_patient, false, Convert.ToBase64String(p.RowVersion), _s.Actor, default);
        Assert.Equal("patient_inactive", (await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).StartAsync(_patient, TemplateId, _s.Actor, default))).Code);

        var t = await _s.ReloadTemplateAsync(TemplateId);
        await _s.Templates(db).SetActiveAsync(TemplateId, false, t.Template.RowVersion, _s.Actor, default);
        var other = await _s.PatientAsync("Bo", "Kim");
        Assert.Equal("template_inactive", (await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).StartAsync(other, TemplateId, _s.Actor, default))).Code);
    }

    // ---------- template changes during active completion ----------

    [Fact]
    public async Task A_template_edit_during_completion_does_not_change_the_draft_and_the_draft_can_still_be_signed_on_its_own_version()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await PublishV2Async(Content("Privacy notice", "Version two wording.", [new FormFieldDefinition("acknowledged", "I have read it", FormFieldKinds.Checkbox, true)]));

        var during = await _s.ReloadAsync(ready.Summary.Id);
        Assert.Equal(1, during.Summary.TemplateVersionNumber);
        Assert.Equal("We protect your information.", during.Version.Body);
        Assert.True(during.Summary.NewerVersionAvailable);
        Assert.Equal(2, during.NewerVersion!.VersionNumber);

        var (signed, _) = await _s.SignAsync(during);
        Assert.Equal(1, signed.Snapshot!.TemplateVersionNumber);
        Assert.Equal("We protect your information.", signed.Snapshot.Body);
        Assert.Equal("Annie", signed.Snapshot.Responses["nickname"]);
    }

    [Fact]
    public async Task Moving_a_draft_to_the_newer_version_keeps_the_answers_that_still_fit_and_records_the_old_draft_as_replaced()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await PublishV2Async(Content("Privacy notice", "Version two wording.", [
            new FormFieldDefinition("acknowledged", "I have read it", FormFieldKinds.Checkbox, true),
            new FormFieldDefinition("nickname", "Preferred name", FormFieldKinds.Text, false),
            new FormFieldDefinition("contact", "Preferred contact", FormFieldKinds.Choice, false, ["Text message", "Phone"]), // "Email" no longer exists
        ]));

        await using var db = _fixture.CreateContext();
        var (moved, didMove) = await _s.Forms(db).RestartOnLatestAsync(ready.Summary.Id, ready.RowVersion, _s.Actor, default);

        Assert.True(didMove);
        Assert.NotEqual(ready.Summary.Id, moved.Summary.Id);
        Assert.Equal(2, moved.Summary.TemplateVersionNumber);
        Assert.Equal(FormStatuses.Draft, moved.Summary.Status);
        Assert.Equal("true", moved.Responses["acknowledged"]);
        Assert.Equal("Annie", moved.Responses["nickname"]);
        Assert.False(moved.Responses.ContainsKey("contact")); // the old choice is not an option any more
        var old = await _s.ReloadAsync(ready.Summary.Id);
        Assert.Equal(FormStatuses.Void, old.Summary.Status);
        Assert.False(old.Summary.WasSigned);
        Assert.Contains(old.Events, e => e.EventType == FormEventTypes.Superseded);
        Assert.Equal(1, await _s.CountAsync(d => d.PatientForms.Where(f => f.Status == FormStatuses.Draft)));
    }

    [Fact]
    public async Task Moving_a_draft_that_is_already_on_the_latest_version_changes_nothing()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await using var db = _fixture.CreateContext();
        var (same, didMove) = await _s.Forms(db).RestartOnLatestAsync(ready.Summary.Id, ready.RowVersion, _s.Actor, default);
        Assert.False(didMove);
        Assert.Equal(ready.Summary.Id, same.Summary.Id);
        Assert.Equal(1, await _s.CountAsync(d => d.PatientForms));
    }

    [Fact]
    public async Task A_stale_draft_cannot_be_moved_to_the_newer_version()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await PublishV2Async(Content(body: "Version two wording."));
        await _s.FillAsync(ready, ("nickname", "Changed meanwhile"));

        await using var db = _fixture.CreateContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.Forms(db).RestartOnLatestAsync(ready.Summary.Id, ready.RowVersion, _s.Actor, default));
        Assert.Equal(FormStatuses.Draft, (await _s.ReloadAsync(ready.Summary.Id)).Summary.Status);
    }

    // ---------- void / discard ----------

    [Fact]
    public async Task A_draft_can_be_discarded_with_a_reason_and_a_voided_form_can_no_longer_be_signed_or_edited()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db).VoidAsync(ready.Summary.Id, "Started on the wrong visit", ready.RowVersion, canVoidSigned: false, _s.Actor, default);

        var voided = await _s.ReloadAsync(ready.Summary.Id);
        Assert.Equal(FormStatuses.Void, voided.Summary.Status);
        Assert.False(voided.Summary.WasSigned);
        Assert.Equal("Started on the wrong visit", voided.Summary.VoidReason);

        Assert.Equal("form_void", (await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(voided))).Code);
        await using var db2 = _fixture.CreateContext();
        Assert.Equal("form_not_draft", (await Assert.ThrowsAsync<FormException>(() => _s.Forms(db2).SaveDraftAsync(voided.Summary.Id, new Dictionary<string, string?>(), voided.RowVersion, _s.Actor, default))).Code);
        // ...and the front desk can simply start a fresh draft.
        Assert.Equal(FormStatuses.Draft, (await _s.StartAsync(_patient, TemplateId)).Summary.Status);
    }

    [Fact]
    public async Task Voiding_needs_a_reason()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).VoidAsync(ready.Summary.Id, "   ", ready.RowVersion, true, _s.Actor, default));
        Assert.Contains("reason", ex.FieldErrors.Keys);
        Assert.Equal(FormStatuses.Draft, (await _s.ReloadAsync(ready.Summary.Id)).Summary.Status);
    }

    [Fact]
    public async Task Only_someone_who_may_void_signed_forms_can_void_a_signed_one()
    {
        var (signed, _) = await _s.SignAsync(await _s.ReadyAsync(_patient, TemplateId));

        await using (var db = _fixture.CreateContext())
        {
            var ex = await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).VoidAsync(signed.Summary.Id, "Because", signed.RowVersion, canVoidSigned: false, _s.Actor, default));
            Assert.Equal("void_not_permitted", ex.Code);
            Assert.Equal(403, ex.StatusCode);
        }
        Assert.Equal(FormStatuses.Signed, (await _s.ReloadAsync(signed.Summary.Id)).Summary.Status);
    }

    [Fact]
    public async Task Voiding_twice_changes_nothing_the_second_time()
    {
        var (signed, _) = await _s.SignAsync(await _s.ReadyAsync(_patient, TemplateId));
        await using var db = _fixture.CreateContext();
        var first = await _s.Forms(db).VoidAsync(signed.Summary.Id, "Wrong chart", signed.RowVersion, true, _s.Actor, default);
        var second = await _s.Forms(db).VoidAsync(signed.Summary.Id, "Different reason", signed.RowVersion, true, _s.Actor, default);

        Assert.Equal("Wrong chart", second.Summary.VoidReason);
        Assert.Equal(first.RowVersion, second.RowVersion);
        Assert.Equal(1, await _s.CountAsync(d => d.AuditLogEntries.Where(a => a.EventType == FormAuditEvents.FormVoided)));
    }

    [Fact]
    public async Task Voiding_a_signed_form_is_audited_without_the_reason_text_or_any_answers()
    {
        var (signed, _) = await _s.SignAsync(await _s.ReadyAsync(_patient, TemplateId));
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db).VoidAsync(signed.Summary.Id, "PATIENT-SAID-SOMETHING-PRIVATE", signed.RowVersion, true, _s.Actor, default);

        await using var read = _fixture.CreateContext();
        var audit = await read.AuditLogEntries.SingleAsync(a => a.EventType == FormAuditEvents.FormVoided);
        Assert.Equal(_s.Actor, audit.PerformedByUserAccountId);
        Assert.DoesNotContain("PRIVATE", audit.Details);
        Assert.Null(audit.Reason);
    }

    [Fact]
    public async Task A_correction_is_a_void_plus_a_new_signed_form_and_both_stay_in_the_history()
    {
        var (first, _) = await _s.SignAsync(await _s.ReadyAsync(_patient, TemplateId));
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db).VoidAsync(first.Summary.Id, "Wrong nickname recorded", first.RowVersion, true, _s.Actor, default);
        var redo = await _s.FillAsync(await _s.StartAsync(_patient, TemplateId), ("acknowledged", "true"), ("nickname", "Ann"));
        var (second, _) = await _s.SignAsync(redo);

        await using var read = _fixture.CreateContext();
        var list = await _s.Reader(read).ListForPatientAsync(_patient, default);
        Assert.Equal(2, list.Count);
        Assert.Equal([FormStatuses.Signed, FormStatuses.Void], list.Select(f => f.Status));
        Assert.Equal("Ann", second.Snapshot!.Responses["nickname"]);
        Assert.Equal("Annie", (await _s.ReloadAsync(first.Summary.Id)).Snapshot!.Responses["nickname"]);
    }

    // ---------- document-library seam ----------

    [Fact]
    public async Task The_document_library_seam_lists_signed_forms_with_their_hash_and_flags_voided_ones_without_exposing_answers()
    {
        var (a, _) = await _s.SignAsync(await _s.ReadyAsync(_patient, TemplateId));
        var financial = await _s.CreateTemplateAsync("financial-policy", FormCategories.Financial);
        var (b, _) = await _s.SignAsync(await _s.ReadyAsync(_patient, financial.Template.Id));
        await _s.StartAsync(await _s.PatientAsync("Other", "Person"), TemplateId); // another patient's draft is not listed
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db).VoidAsync(a.Summary.Id, "Wrong chart", a.RowVersion, true, _s.Actor, default);

        await using var read = _fixture.CreateContext();
        var docs = await _s.Reader(read).SignedDocumentsAsync(_patient, default);

        Assert.Equal(2, docs.Count);
        var doc = docs.Single(d => d.TemplateKey == "financial-policy");
        Assert.Equal(b.Snapshot!.Id, doc.SnapshotId);
        Assert.Equal(b.Snapshot.SnapshotHash, doc.SnapshotHash);
        Assert.Equal(FormCategories.Financial, doc.Category);
        Assert.False(doc.IsVoided);
        Assert.True(docs.Single(d => d.TemplateKey == "privacy-notice").IsVoided);
        Assert.Equal("application/vnd.alveara.signed-form+json", SignedFormDocumentDescriptor.ContentType);
        Assert.DoesNotContain(typeof(SignedFormDocumentDescriptor).GetProperties(), p => p.Name is "ResponsesJson" or "Responses" or "SignerName");
    }
}
