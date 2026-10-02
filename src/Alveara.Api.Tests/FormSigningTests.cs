using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Idempotency;
using Alveara.Api.Architecture.Measurement;
using Xunit;
using static Alveara.Api.Tests.FormTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-N010: completing and signing a form - the happy path, every refusal, duplicate submit, and an interrupted signature.</summary>
public class FormSigningTests : IAsyncLifetime
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

    // ---------- happy path ----------

    [Fact]
    public async Task A_versioned_form_can_be_started_completed_and_signed()
    {
        var started = await _s.StartAsync(_patient, TemplateId);
        Assert.Equal(FormStatuses.Draft, started.Summary.Status);
        Assert.Equal(1, started.Summary.TemplateVersionNumber);

        var ready = await _s.FillAsync(started, ("acknowledged", "true"), ("nickname", "Annie"), ("contact", "Phone"));
        var (signed, created) = await _s.SignAsync(ready, input: SignInput(ready, "Ann Lee", "Self"));

        Assert.True(created);
        Assert.Equal(FormStatuses.Signed, signed.Summary.Status);
        var snap = signed.Snapshot!;
        Assert.Equal("Ann Lee", snap.SignerName);
        Assert.Equal("Self", snap.SignerRelationship);
        Assert.Equal("typed-name", snap.SignatureMethod);
        Assert.Equal(FormDefinition.Attestation, snap.Attestation);
        Assert.Equal(_template.Template.Current!.Id, signed.Version.Id);
        Assert.Equal(1, snap.TemplateVersionNumber);
        Assert.Equal("Phone", snap.Responses["contact"]);
        Assert.True(snap.IntegrityVerified);
        Assert.Equal(_s.Actor, snap.CapturedByUserId);
    }

    [Fact]
    public async Task The_signed_snapshot_preserves_exactly_the_wording_fields_and_answers_that_were_shown()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var (signed, _) = await _s.SignAsync(ready);

        var snap = signed.Snapshot!;
        Assert.Equal("Privacy notice", snap.Title);
        Assert.Equal("We protect your information.", snap.Body);
        Assert.Equal(3, snap.Fields.Count);
        Assert.Equal(new Dictionary<string, string> { ["acknowledged"] = "true", ["contact"] = "Email", ["nickname"] = "Annie" }, snap.Responses);
        Assert.Equal("privacy-notice", snap.TemplateKey);
        Assert.Equal(FormCategories.Privacy, snap.Category);
    }

    [Fact]
    public async Task A_signer_other_than_the_patient_is_recorded_with_the_relationship()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var (signed, _) = await _s.SignAsync(ready, input: SignInput(ready, "Pat Lee", "Parent"));
        Assert.Equal("Pat Lee", signed.Snapshot!.SignerName);
        Assert.Equal("Parent", signed.Snapshot.SignerRelationship);
        Assert.Null(signed.Snapshot.SignerRelationshipNote);

        var other = await _s.PatientAsync("Bo", "Kim");
        var ready2 = await _s.ReadyAsync(other, TemplateId);
        var (signed2, _) = await _s.SignAsync(ready2, input: SignInput(ready2, "Sam Roe", "Other", note: "Family friend with written authority"));
        Assert.Equal("Family friend with written authority", signed2.Snapshot!.SignerRelationshipNote);
    }

    [Fact]
    public async Task History_names_the_signer_the_actor_the_time_and_the_template_version_and_the_audit_log_stays_free_of_answers()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var (signed, _) = await _s.SignAsync(ready, input: SignInput(ready, "Ann Lee", "Self"));

        var evt = signed.Events.Single(e => e.EventType == FormEventTypes.Signed);
        Assert.Equal(_s.Actor, evt.ActorUserId);
        Assert.Equal(1, evt.TemplateVersionNumber);
        Assert.Contains("Ann Lee", evt.Detail);
        Assert.InRange(evt.OccurredAtUtc, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal([FormEventTypes.Started, FormEventTypes.Signed], signed.Events.Select(e => e.EventType));

        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.Where(a => a.EventType == FormAuditEvents.FormSigned).SingleAsync();
        Assert.Equal(_s.Actor, audit.PerformedByUserAccountId);
        Assert.Equal(signed.Summary.Id, audit.TargetUserAccountId);
        Assert.Contains("version 1", audit.Details);
        Assert.DoesNotContain("Ann", audit.Details);
        Assert.DoesNotContain("Annie", audit.Details);
        Assert.InRange(audit.TimestampUtc, before, DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Signing_records_a_privacy_safe_measurement_event_with_only_the_category_and_outcome()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db, new MeasurementEventSink(db)).SignAsync(ready.Summary.Id, SignInput(ready), "measure-key-1", _s.Actor, default);

        await using var read = _fixture.CreateContext();
        var evt = await read.MeasurementEvents.SingleAsync(e => e.EventName == "form.signed");
        Assert.Equal("""{"category":"privacy","outcome":"success"}""", evt.PropertiesJson);
        Assert.Equal(1, evt.SchemaVersion);
    }

    [Fact]
    public async Task A_measurement_failure_never_undoes_a_signature()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await using (var db = _fixture.CreateContext())
            await _s.Forms(db, new ThrowingSink()).SignAsync(ready.Summary.Id, SignInput(ready), "measure-key-2", _s.Actor, default);
        Assert.Equal(FormStatuses.Signed, (await _s.ReloadAsync(ready.Summary.Id)).Summary.Status);
    }

    private sealed class ThrowingSink : IMeasurementEventSink
    {
        public Task RecordAsync(string eventName, int schemaVersion, object properties, CancellationToken cancellationToken = default) =>
            throw new MeasurementEventValidationException("simulated");
    }

    // ---------- validation / missing signer identity ----------

    [Fact]
    public async Task A_missing_signer_name_or_relationship_is_refused_and_the_form_stays_a_draft()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);

        var ex = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, input: SignInput(ready, signer: "  ", relationship: "")));

        Assert.Equal("validation_failed", ex.Code);
        Assert.Contains("signerName", ex.FieldErrors.Keys);
        Assert.Contains("relationship", ex.FieldErrors.Keys);
        Assert.Equal(FormStatuses.Draft, (await _s.ReloadAsync(ready.Summary.Id)).Summary.Status);
        Assert.Equal(0, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Theory]
    [InlineData("Grandmother-in-law")]
    [InlineData("self")]
    public async Task A_relationship_outside_the_list_is_refused(string relationship)
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, input: SignInput(ready, relationship: relationship)));
        Assert.Contains("relationship", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task The_other_relationship_needs_a_description()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, input: SignInput(ready, relationship: "Other", note: null)));
        Assert.Contains("relationshipNote", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task Signing_needs_a_typed_signature_and_the_attestation()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var noSignature = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, input: SignInput(ready) with { SignatureText = "" }));
        Assert.Contains("signatureText", noSignature.FieldErrors.Keys);
        var notAttested = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, input: SignInput(ready, attested: false)));
        Assert.Contains("attested", notAttested.FieldErrors.Keys);
        Assert.Equal(0, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Fact]
    public async Task A_required_answer_that_is_missing_blocks_signing_and_names_the_field()
    {
        var started = await _s.StartAsync(_patient, TemplateId);
        var partial = await _s.FillAsync(started, ("nickname", "Annie")); // the required checkbox is not ticked

        var ex = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(partial));

        Assert.Contains("responses.acknowledged", ex.FieldErrors.Keys);
        Assert.Equal(0, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Fact]
    public async Task A_draft_accepts_partial_answers_and_refuses_values_that_do_not_fit_the_field()
    {
        var started = await _s.StartAsync(_patient, TemplateId);

        var bad = await Assert.ThrowsAsync<FormException>(() => _s.FillAsync(started, ("contact", "Carrier pigeon"), ("nobody", "x"), ("acknowledged", "maybe")));
        Assert.Contains("responses.contact", bad.FieldErrors.Keys);
        Assert.Contains("responses.nobody", bad.FieldErrors.Keys);
        Assert.Contains("responses.acknowledged", bad.FieldErrors.Keys);

        var partial = await _s.FillAsync(started, ("nickname", "Annie"));
        Assert.Equal("Annie", partial.Responses["nickname"]);
    }

    [Fact]
    public async Task Saving_the_same_answers_again_changes_nothing()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var again = await _s.FillAsync(ready, ("acknowledged", "true"), ("nickname", "Annie"), ("contact", "Email"));
        Assert.Equal(ready.RowVersion, again.RowVersion);
    }

    // ---------- the draft is a stable thing to sign ----------

    [Fact]
    public async Task A_draft_changed_after_it_was_reviewed_cannot_be_signed_with_the_old_review()
    {
        var reviewed = await _s.ReadyAsync(_patient, TemplateId);
        await _s.FillAsync(reviewed, ("acknowledged", "true"), ("nickname", "Changed by someone else after review"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.SignAsync(reviewed));

        Assert.Equal(0, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Fact]
    public async Task A_signature_for_a_template_version_other_than_the_one_reviewed_is_refused()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, input: SignInput(ready) with { TemplateVersionId = Guid.NewGuid() }));
        Assert.Equal("template_version_mismatch", ex.Code);
    }

    // ---------- duplicate submit / idempotency ----------

    [Fact]
    public async Task Submitting_the_same_signature_twice_returns_the_same_signed_form_and_creates_one_artifact()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);

        var (first, createdFirst) = await _s.SignAsync(ready, "retry-key-0001");
        var (second, createdSecond) = await _s.SignAsync(ready, "retry-key-0001"); // exact retry, stale row version and all

        Assert.True(createdFirst);
        Assert.False(createdSecond);
        Assert.Equal(first.Snapshot!.Id, second.Snapshot!.Id);
        Assert.Equal(first.Snapshot.SnapshotHash, second.Snapshot.SnapshotHash);
        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
        Assert.Equal(1, await _s.CountAsync(db => db.PatientFormEvents.Where(e => e.EventType == FormEventTypes.Signed)));
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == FormAuditEvents.FormSigned)));
        Assert.Equal(1, await _s.CountAsync(db => db.IdempotencyReceipts));
    }

    [Fact]
    public async Task Signing_an_already_signed_form_with_a_different_key_is_refused_and_points_at_the_existing_copy()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        var (first, _) = await _s.SignAsync(ready, "first-key-00001");

        var ex = await Assert.ThrowsAsync<FormException>(() => _s.SignAsync(ready, "another-key-0002"));

        Assert.Equal("already_signed", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(first.Snapshot!.Id, ex.ExistingId);
        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Fact]
    public async Task Eight_simultaneous_submits_of_one_signature_create_exactly_one_signed_artifact()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { var (d, created) = await _s.SignAsync(ready, "simultaneous-key"); return (created ? "created" : "replayed", d.Snapshot?.Id); }
            catch (ConcurrencyConflictException) { return ("conflict", (Guid?)null); }
            catch (FormException ex) { return (ex.Code, (Guid?)null); }
        }));

        Assert.Equal(1, outcomes.Count(o => o.Item1 == "created"));
        Assert.All(outcomes, o => Assert.Contains(o.Item1, new[] { "created", "replayed", "conflict", "already_signed" }));
        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
        Assert.Equal(1, await _s.CountAsync(db => db.PatientFormEvents.Where(e => e.EventType == FormEventTypes.Signed)));
    }

    [Fact]
    public async Task Eight_simultaneous_submits_with_different_keys_still_create_exactly_one_signed_artifact()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try { await _s.SignAsync(ready, $"distinct-key-{i:D4}"); }
            catch (Exception ex) when (ex is ConcurrencyConflictException or FormException) { }
        }));

        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
        Assert.Equal(FormStatuses.Signed, (await _s.ReloadAsync(ready.Summary.Id)).Summary.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task A_signature_without_a_usable_idempotency_key_is_refused(string? key)
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<FormException>(() => _s.Forms(db).SignAsync(ready.Summary.Id, SignInput(ready), key, _s.Actor, default));
        Assert.Equal("idempotency_key_required", ex.Code);
    }

    // ---------- interrupted signature ----------

    [Fact]
    public async Task An_interrupted_signature_leaves_a_plain_draft_with_nothing_half_written_and_the_same_retry_then_succeeds()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);

        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Forms(failing).SignAsync(ready.Summary.Id, SignInput(ready), "interrupted-key", _s.Actor, default));

        var after = await _s.ReloadAsync(ready.Summary.Id);
        Assert.Equal(FormStatuses.Draft, after.Summary.Status);
        Assert.Null(after.Snapshot);
        Assert.Equal(ready.RowVersion, after.RowVersion);
        Assert.Equal(0, await _s.CountAsync(db => db.SignedFormSnapshots));
        Assert.Equal(0, await _s.CountAsync(db => db.IdempotencyReceipts));
        Assert.Equal([FormEventTypes.Started], after.Events.Select(e => e.EventType));

        var (signed, created) = await _s.SignAsync(ready, "interrupted-key");
        Assert.True(created);
        Assert.Equal(FormStatuses.Signed, signed.Summary.Status);
        Assert.Equal(1, await _s.CountAsync(db => db.SignedFormSnapshots));
    }

    [Fact]
    public async Task A_cancelled_request_never_commits_a_partial_signature()
    {
        var ready = await _s.ReadyAsync(_patient, TemplateId);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await using (var db = _fixture.CreateContext())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _s.Forms(db).SignAsync(ready.Summary.Id, SignInput(ready), "cancelled-key1", _s.Actor, cts.Token));

        Assert.Equal(FormStatuses.Draft, (await _s.ReloadAsync(ready.Summary.Id)).Summary.Status);
        Assert.Equal(0, await _s.CountAsync(db => db.SignedFormSnapshots));
        Assert.Equal(0, await _s.CountAsync(db => db.IdempotencyReceipts));
    }

    [Fact]
    public async Task The_receipt_is_scoped_to_the_form_so_the_same_client_key_on_another_form_is_a_separate_signature()
    {
        var otherPatient = await _s.PatientAsync("Cy", "Poe");
        var a = await _s.ReadyAsync(_patient, TemplateId);
        var b = await _s.ReadyAsync(otherPatient, TemplateId);

        var (_, createdA) = await _s.SignAsync(a, "shared-client-key");
        var (_, createdB) = await _s.SignAsync(b, "shared-client-key");

        Assert.True(createdA);
        Assert.True(createdB);
        Assert.Equal(2, await _s.CountAsync(db => db.SignedFormSnapshots));
        Assert.Equal(2, await _s.CountAsync(db => db.IdempotencyReceipts.Where(r => r.CommandType == "form.sign")));
        Assert.True(await IdempotencyGuardProbe(a.Summary.Id, "shared-client-key"));
    }

    private async Task<bool> IdempotencyGuardProbe(Guid formId, string key)
    {
        await using var db = _fixture.CreateContext();
        return await IdempotencyGuard.AlreadyProcessedAsync(db, "form.sign", $"{formId:N}:{key}");
    }
}
