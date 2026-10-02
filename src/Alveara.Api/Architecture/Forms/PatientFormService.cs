using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Idempotency;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Forms;

/// <summary>
/// ALV-N010: starting, completing, signing and voiding a patient's forms.
///
/// Signing is one atomic save: the immutable snapshot, the status change, the history event, the audit entry and the
/// idempotency receipt commit together or not at all - so an interrupted signature leaves a plain draft and nothing else, and
/// retrying with the same key is safe. A duplicate submit cannot create a second signed artifact: the same key replays the
/// first result, and the database allows only one snapshot per form. The snapshot is built from the draft exactly as stored
/// at that moment; the caller echoes the draft's row version (so a draft changed after the user reviewed it is a 409, not a
/// signature on different answers) and the template version id it was shown.
/// </summary>
public class PatientFormService(
    AlveraDbContext db, IPracticeClock clock, IMeasurementEventSink? measurements = null, ILogger<PatientFormService>? logger = null)
{
    private const string SignCommand = "form.sign";

    private PatientFormReader Reader => new(db);

    // ---------- start / draft ----------

    /// <summary>Starts a form for a patient on the template's current version. If an open draft for that template already exists it is returned instead (Created = false).</summary>
    public async Task<(PatientFormDetail Detail, bool Created)> StartAsync(Guid patientId, Guid templateId, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => new { p.Id, p.IsActive }).SingleOrDefaultAsync(ct)
            ?? throw new FormException("patient_not_found", "That patient was not found.", 404);
        if (!patient.IsActive) throw new FormException("patient_inactive", "This patient is inactive. Reactivate them before starting a new form.", 409);
        var template = await db.FormTemplates.SingleOrDefaultAsync(t => t.Id == templateId, ct) ?? throw new FormException("template_not_found", "That form template was not found.", 404);
        if (!template.IsActive) throw new FormException("template_inactive", "This form template is inactive.", 409);

        var open = await db.PatientForms.AsNoTracking().Where(f => f.PatientId == patientId && f.TemplateId == templateId && f.Status == FormStatuses.Draft).Select(f => f.Id).SingleOrDefaultAsync(ct);
        if (open != Guid.Empty) return (await Reader.GetAsync(open, ct), false);

        var version = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == template.CurrentVersionId, ct);
        var form = NewDraft(patientId, template, version, "{}", actor);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear(); // a simultaneous start by someone else: use theirs
            var winner = await db.PatientForms.AsNoTracking().Where(f => f.PatientId == patientId && f.TemplateId == templateId && f.Status == FormStatuses.Draft).Select(f => f.Id).SingleAsync(ct);
            return (await Reader.GetAsync(winner, ct), false);
        }
        return (await Reader.GetAsync(form.Id, ct), true);
    }

    /// <summary>Saves the entered values of a draft (partial answers are fine). Saving unchanged values changes nothing.</summary>
    public async Task<PatientFormDetail> SaveDraftAsync(Guid formId, IReadOnlyDictionary<string, string?>? responses, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var form = await LoadAsync(formId, ct);
        FormWrite.ApplyExpectedVersion(db, form, rowVersion);
        if (form.Status != FormStatuses.Draft) throw new FormException("form_not_draft", "Only a draft can be changed. A signed form is final; void it and complete a new one to correct it.", 409);

        var version = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == form.TemplateVersionId, ct);
        var errors = new Dictionary<string, string>();
        var clean = FormDefinition.NormalizeResponses(FormDefinition.ParseFields(version.FieldsJson), responses, requireComplete: false, errors);
        if (errors.Count > 0) throw new FormException("validation_failed", "Some answers need attention.", 400, errors);

        var json = FormDefinition.SerializeResponses(clean);
        if (json == form.ResponsesJson) { db.ChangeTracker.Clear(); return await Reader.GetAsync(formId, ct); }
        form.ResponsesJson = json;
        form.UpdatedAtUtc = clock.UtcNow;
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(PatientForm), formId, ct);
        return await Reader.GetAsync(formId, ct);
    }

    /// <summary>
    /// "Template changes during active completion": a draft stays on the version it was started on. This explicitly moves it to the
    /// template's current version - the old draft is voided (history keeps it) and a new draft is started with every answer that still fits.
    /// </summary>
    public async Task<(PatientFormDetail Detail, bool Moved)> RestartOnLatestAsync(Guid formId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var old = await LoadAsync(formId, ct);
        FormWrite.ApplyExpectedVersion(db, old, rowVersion);
        if (old.Status != FormStatuses.Draft) throw new FormException("form_not_draft", "Only a draft can be moved to a newer version.", 409);
        var template = await db.FormTemplates.AsNoTracking().SingleAsync(t => t.Id == old.TemplateId, ct);
        if (!template.IsActive) throw new FormException("template_inactive", "This form template is inactive.", 409);
        if (template.CurrentVersionId == old.TemplateVersionId) { db.ChangeTracker.Clear(); return (await Reader.GetAsync(formId, ct), false); }

        var oldVersion = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == old.TemplateVersionId, ct);
        var newVersion = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == template.CurrentVersionId, ct);
        var carried = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var newFields = FormDefinition.ParseFields(newVersion.FieldsJson).ToDictionary(f => f.Id);
        var oldFields = FormDefinition.ParseFields(oldVersion.FieldsJson).ToDictionary(f => f.Id);
        foreach (var (id, value) in FormDefinition.ParseResponses(old.ResponsesJson))
        {
            if (!newFields.TryGetValue(id, out var nf) || !oldFields.TryGetValue(id, out var of) || nf.Kind != of.Kind) continue;
            if (nf.Kind == FormFieldKinds.Choice && !nf.Options!.Contains(value)) continue;
            carried[id] = value;
        }

        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        old.Status = FormStatuses.Void; old.VoidedAtUtc = now; old.VoidedByUserId = actor; old.UpdatedAtUtc = now;
        old.VoidReason = $"Replaced by version {newVersion.VersionNumber} of the template.";
        FormWrite.Event(db, old, FormEventTypes.Superseded, actor, now, oldVersion.VersionNumber, old.VoidReason);
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(PatientForm), formId, ct);

        var fresh = NewDraft(old.PatientId, await db.FormTemplates.SingleAsync(t => t.Id == old.TemplateId, ct), newVersion, FormDefinition.SerializeResponses(carried), actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await Reader.GetAsync(fresh.Id, ct), true);
    }

    // ---------- sign ----------

    public async Task<(PatientFormDetail Detail, bool Created)> SignAsync(Guid formId, SignInput input, string? idempotencyKey, Guid actor, CancellationToken ct)
    {
        var key = (idempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 8 or > 100)
            throw new FormException("idempotency_key_required", "A signature needs an Idempotency-Key of 8-100 characters so a retried submit can never sign twice.", 400);
        var receiptKey = $"{formId:N}:{key}";

        var form = await LoadAsync(formId, ct);
        if (await IdempotencyGuard.AlreadyProcessedAsync(db, SignCommand, receiptKey, ct)) return await Replay(formId, ct);

        if (form.Status == FormStatuses.Void) throw new FormException("form_void", "This form has been voided.", 409);
        if (form.Status == FormStatuses.Signed) throw await AlreadySigned(formId, ct);
        FormWrite.ApplyExpectedVersion(db, form, input.RowVersion);
        if (input.TemplateVersionId != form.TemplateVersionId)
            throw new FormException("template_version_mismatch", "The form you reviewed is not the version this draft is on. Reload it and review again before signing.", 409);

        var version = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == form.TemplateVersionId, ct);
        var template = await db.FormTemplates.AsNoTracking().SingleAsync(t => t.Id == form.TemplateId, ct);
        var errors = new Dictionary<string, string>();
        var fields = FormDefinition.ParseFields(version.FieldsJson);
        var responses = FormDefinition.NormalizeResponses(fields, FormDefinition.ParseResponses(form.ResponsesJson).ToDictionary(k => k.Key, k => (string?)k.Value), requireComplete: true, errors);

        var signer = FormDefinition.NormalizeText(input.SignerName, 200, "signerName", "The signer's name", errors);
        var relationship = input.Relationship ?? string.Empty;
        string? note = null;
        if (!SignerRelationships.IsValid(relationship)) errors["relationship"] = "Say how the signer relates to the patient.";
        else if (relationship == "Other") note = FormDefinition.NormalizeText(input.RelationshipNote, 200, "relationshipNote", "Please describe the relationship", errors);
        var signature = FormDefinition.NormalizeText(input.SignatureText, 200, "signatureText", "The typed signature", errors);
        if (!input.Attested) errors["attested"] = "The signer must confirm the statement before signing.";
        if (errors.Count > 0) throw new FormException("validation_failed", "Some details are missing or need attention before this can be signed.", 400, errors);

        var now = clock.UtcNow;
        var snapshot = new SignedFormSnapshot
        {
            Id = Guid.NewGuid(), PatientFormId = formId, PatientId = form.PatientId, TemplateId = form.TemplateId, TemplateKey = template.Key, Category = template.Category,
            TemplateVersionId = version.Id, TemplateVersionNumber = version.VersionNumber, Title = version.Title, Body = version.Body, FieldsJson = version.FieldsJson,
            ResponsesJson = FormDefinition.SerializeResponses(responses), SignerName = signer, SignerRelationship = relationship, SignerRelationshipNote = note,
            SignatureMethod = "typed-name", SignatureText = signature, Attestation = FormDefinition.Attestation, SignedAtUtc = now, CapturedByUserId = actor,
            SnapshotSchemaVersion = FormDefinition.SnapshotSchemaVersion, SnapshotHash = string.Empty,
        };
        snapshot.SnapshotHash = FormDefinition.SnapshotHash(snapshot);

        db.SignedFormSnapshots.Add(snapshot);
        form.Status = FormStatuses.Signed; form.UpdatedAtUtc = now;
        FormWrite.Event(db, form, FormEventTypes.Signed, actor, now, version.VersionNumber, $"Signed by {signer} ({relationship}).");
        FormWrite.Audit(db, FormAuditEvents.FormSigned, formId, actor, $"Form '{template.Key}' version {version.VersionNumber} signed.");
        IdempotencyGuard.MarkProcessed(db, SignCommand, receiptKey);
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(PatientForm), formId, ct);
        }
        catch (DbUpdateException ex) when (IdempotencyGuard.IsDuplicateReceiptViolation(ex))
        {
            db.ChangeTracker.Clear();
            return await Replay(formId, ct); // the same submit raced itself: the other one won
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear(); // a second snapshot for this form was refused by the database
            if (await IdempotencyGuard.AlreadyProcessedAsync(db, SignCommand, receiptKey, ct)) return await Replay(formId, ct);
            throw await AlreadySigned(formId, ct);
        }

        await RecordMeasurementAsync(template.Category, ct);
        return (await Reader.GetAsync(formId, ct), true);
    }

    // ---------- void ----------

    /// <summary>
    /// Voids a draft (discard) or a signed form (controlled void). The signed snapshot, if any, is never touched - the form simply stops
    /// being current - and a reason is required. Voiding a form that is already void changes nothing. Only a caller who may void
    /// signed forms can void a signed one.
    /// </summary>
    public async Task<PatientFormDetail> VoidAsync(Guid formId, string? reason, string? rowVersion, bool canVoidSigned, Guid actor, CancellationToken ct)
    {
        var form = await LoadAsync(formId, ct);
        var errors = new Dictionary<string, string>();
        var why = FormDefinition.NormalizeText(reason, 400, "reason", "A reason", errors);
        if (errors.Count > 0) throw new FormException("validation_failed", "A reason is required.", 400, errors);
        if (form.Status == FormStatuses.Void) { db.ChangeTracker.Clear(); return await Reader.GetAsync(formId, ct); }
        if (form.Status == FormStatuses.Signed && !canVoidSigned)
            throw new FormException("void_not_permitted", "You do not have permission to void a signed form.", 403);
        FormWrite.ApplyExpectedVersion(db, form, rowVersion);

        var number = await db.FormTemplateVersions.Where(v => v.Id == form.TemplateVersionId).Select(v => v.VersionNumber).SingleAsync(ct);
        var wasSigned = form.Status == FormStatuses.Signed;
        var now = clock.UtcNow;
        form.Status = FormStatuses.Void; form.VoidedAtUtc = now; form.VoidedByUserId = actor; form.VoidReason = why; form.UpdatedAtUtc = now;
        FormWrite.Event(db, form, FormEventTypes.Voided, actor, now, number, why);
        FormWrite.Audit(db, FormAuditEvents.FormVoided, formId, actor, wasSigned ? "Signed form voided." : "Draft form discarded.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(PatientForm), formId, ct);
        return await Reader.GetAsync(formId, ct);
    }

    // ---------- helpers ----------

    private PatientForm NewDraft(Guid patientId, FormTemplate template, FormTemplateVersion version, string responsesJson, Guid actor)
    {
        var now = clock.UtcNow;
        var form = new PatientForm
        {
            Id = Guid.NewGuid(), PatientId = patientId, TemplateId = template.Id, TemplateVersionId = version.Id, Status = FormStatuses.Draft,
            ResponsesJson = responsesJson, StartedAtUtc = now, StartedByUserId = actor,
        };
        db.PatientForms.Add(form);
        FormWrite.Event(db, form, FormEventTypes.Started, actor, now, version.VersionNumber);
        FormWrite.Audit(db, FormAuditEvents.FormStarted, form.Id, actor, $"Form '{template.Key}' version {version.VersionNumber} started.");
        return form;
    }

    private async Task<PatientForm> LoadAsync(Guid id, CancellationToken ct) =>
        await db.PatientForms.SingleOrDefaultAsync(f => f.Id == id, ct) ?? throw new FormException("form_not_found", "That form was not found.", 404);

    private async Task<(PatientFormDetail, bool)> Replay(Guid formId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return (await Reader.GetAsync(formId, ct), false);
    }

    private async Task<FormException> AlreadySigned(Guid formId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var snapshotId = await db.SignedFormSnapshots.AsNoTracking().Where(s => s.PatientFormId == formId).Select(s => s.Id).SingleOrDefaultAsync(ct);
        return new FormException("already_signed", "This form has already been signed. The signed copy is final.", 409, existingId: snapshotId == Guid.Empty ? null : snapshotId);
    }

    private async Task RecordMeasurementAsync(string category, CancellationToken ct)
    {
        if (measurements is null) return;
        try
        {
            await measurements.RecordAsync("form.signed", 1, new { category = FormCategories.Slug(category), outcome = "success" }, ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or MeasurementEventValidationException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "form.signed measurement event was not recorded; the signature itself is stored.");
        }
    }
}
