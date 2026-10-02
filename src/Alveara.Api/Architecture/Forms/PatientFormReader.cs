using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Forms;

/// <summary>ALV-N010: read side of patient forms - summaries, full detail and the document-library seam. Reads only; never changes anything.</summary>
public class PatientFormReader(AlveraDbContext db)
{
    public async Task<IReadOnlyList<PatientFormSummary>> ListForPatientAsync(Guid patientId, CancellationToken ct)
    {
        var forms = await db.PatientForms.AsNoTracking().Where(f => f.PatientId == patientId).OrderByDescending(f => f.StartedAtUtc).ToListAsync(ct);
        var list = new List<PatientFormSummary>();
        foreach (var f in forms) list.Add(await SummaryAsync(f, ct));
        return list;
    }

    public async Task<PatientFormDetail> GetAsync(Guid formId, CancellationToken ct)
    {
        var form = await db.PatientForms.AsNoTracking().SingleOrDefaultAsync(f => f.Id == formId, ct)
            ?? throw new FormException("form_not_found", "That form was not found.", 404);
        var version = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == form.TemplateVersionId, ct);
        var snapshot = await db.SignedFormSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PatientFormId == formId, ct);
        var events = await db.PatientFormEvents.AsNoTracking().Where(e => e.PatientFormId == formId).OrderBy(e => e.OccurredAtUtc).ToListAsync(ct);
        var summary = await SummaryAsync(form, ct);
        TemplateVersionView? newer = null;
        if (summary.NewerVersionAvailable)
        {
            var template = await db.FormTemplates.AsNoTracking().SingleAsync(t => t.Id == form.TemplateId, ct);
            newer = FormTemplateService.ToView(await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == template.CurrentVersionId, ct));
        }
        return new PatientFormDetail(
            summary, Convert.ToBase64String(form.RowVersion), FormTemplateService.ToView(version), FormDefinition.ParseResponses(form.ResponsesJson),
            snapshot is null ? null : ToView(snapshot),
            events.Select(e => new FormEventView(e.EventType, e.ActorUserId, e.OccurredAtUtc, e.TemplateVersionNumber, e.Detail)).ToList(), newer);
    }

    /// <summary>The seam ALV-010-C01 indexes: every signed form of a patient (voided ones included, flagged), newest first.</summary>
    public async Task<IReadOnlyList<SignedFormDocumentDescriptor>> SignedDocumentsAsync(Guid patientId, CancellationToken ct)
    {
        var rows = await (from s in db.SignedFormSnapshots.AsNoTracking()
                          join f in db.PatientForms.AsNoTracking() on s.PatientFormId equals f.Id
                          where s.PatientId == patientId
                          orderby s.SignedAtUtc descending
                          select new { s, f.Status }).ToListAsync(ct);
        return rows.Select(r => new SignedFormDocumentDescriptor(
            r.s.Id, r.s.PatientFormId, r.s.PatientId, r.s.TemplateKey, r.s.Category, r.s.Title, r.s.TemplateVersionNumber,
            r.s.SignedAtUtc, r.s.SnapshotHash, r.Status == FormStatuses.Void)).ToList();
    }

    internal static SnapshotView ToView(SignedFormSnapshot s) => new(
        s.Id, s.PatientFormId, s.TemplateVersionNumber, s.TemplateKey, s.Category, s.Title, s.Body, FormDefinition.ParseFields(s.FieldsJson),
        FormDefinition.ParseResponses(s.ResponsesJson), s.SignerName, s.SignerRelationship, s.SignerRelationshipNote, s.SignatureMethod, s.SignatureText,
        s.Attestation, s.SignedAtUtc, s.CapturedByUserId, s.SnapshotHash, FormDefinition.VerifyIntegrity(s));

    private async Task<PatientFormSummary> SummaryAsync(PatientForm f, CancellationToken ct)
    {
        var template = await db.FormTemplates.AsNoTracking().SingleAsync(t => t.Id == f.TemplateId, ct);
        var version = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == f.TemplateVersionId, ct);
        var snapshot = await db.SignedFormSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PatientFormId == f.Id, ct);
        var newer = f.Status == FormStatuses.Draft && template.IsActive && template.CurrentVersionId != f.TemplateVersionId;
        return new PatientFormSummary(
            f.Id, f.PatientId, f.TemplateId, template.Key, template.Category, version.Title, version.VersionNumber, f.Status, f.StartedAtUtc,
            snapshot?.SignedAtUtc, f.VoidedAtUtc, f.VoidReason, snapshot is not null, snapshot?.SignerName, snapshot?.SignerRelationship, newer);
    }
}
