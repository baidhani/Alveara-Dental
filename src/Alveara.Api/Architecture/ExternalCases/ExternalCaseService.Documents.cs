using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Architecture.ExternalCases;

public partial class ExternalCaseService
{
    /// <summary>
    /// Links an existing document of the case's own patient to the case: the document stays where it is and is shown in the case; nothing is copied. Linking the same document again is a quiet replay. A document
    /// that is not this patient's (or does not exist) is <c>document_invalid</c> (400); an inactive one is <c>document_inactive</c> (409); a finished case takes no more changes (<c>case_closed</c>, 409). The link, its
    /// history entry and its audit entry are one save.
    /// </summary>
    /// <remarks>ALV-N026 (PD-24): a document in a category the caller may not read is refused exactly like one of another patient (<c>document_invalid</c>), so its existence is not disclosed.
    /// <paramref name="scope"/> null means no restriction (callers not acting for a person); the controller always passes the caller's.</remarks>
    public async Task<DocumentLinkResult> LinkDocumentAsync(Guid actor, string kind, Guid caseId, Guid documentId, CancellationToken ct, DocumentReadScope? scope = null)
    {
        var c = await LoadAsync(kind, caseId, false, ct);
        RequireOpen(c);
        var doc = await db.Documents.AsNoTracking().Where(d => d.Id == documentId && d.PatientId == c.PatientId).Select(d => new { d.Status, d.CategoryId }).FirstOrDefaultAsync(ct);
        if (doc is null || (scope is not null && !await scope.ReadsCategoryAsync(db, doc.CategoryId, ct)))
            throw new ExternalCaseException("document_invalid", "Choose a document of this patient.", 400);
        if (await LinkExistsAsync(kind, caseId, documentId, ct)) return new DocumentLinkResult(documentId, true, true);
        if (doc.Status != DocumentStatuses.Active) throw new ExternalCaseException("document_inactive", "That document is inactive. Make it active in the document library first.", 409);

        var now = clock.UtcNow;
        db.ExternalCaseDocuments.Add(new ExternalCaseDocument
        {
            Id = Guid.NewGuid(), ReferralCaseId = kind == ExternalCaseKinds.Referral ? caseId : null, LabCaseId = kind == ExternalCaseKinds.Lab ? caseId : null,
            DocumentId = documentId, LinkedByUserId = actor, LinkedAtUtc = now,
        });
        AddEvent(kind, caseId, ExternalCaseEventKinds.DocumentLinked, actor, now, documentId: documentId);
        AuditService.Record(db, AuditEventTypes.CaseDocumentLinked, kind == ExternalCaseKinds.Lab ? "LabCase" : "ReferralCase", caseId, actor, $"Document {documentId} linked to a {kind.ToLowerInvariant()} case.");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();                                                               // a concurrent request linked it first: that is the answer
            return new DocumentLinkResult(documentId, true, true);
        }
        return new DocumentLinkResult(documentId, true, false);
    }

    /// <summary>Removes a link; the document itself is untouched. Unlinking a document that is not linked is a quiet replay. The history keeps the fact that it was linked and unlinked.</summary>
    public async Task<DocumentLinkResult> UnlinkDocumentAsync(Guid actor, string kind, Guid caseId, Guid documentId, CancellationToken ct)
    {
        var c = await LoadAsync(kind, caseId, false, ct);
        RequireOpen(c);
        var link = await db.ExternalCaseDocuments.FirstOrDefaultAsync(l => l.DocumentId == documentId && (kind == ExternalCaseKinds.Referral ? l.ReferralCaseId == caseId : l.LabCaseId == caseId), ct);
        if (link is null) return new DocumentLinkResult(documentId, false, true);

        var now = clock.UtcNow;
        db.ExternalCaseDocuments.Remove(link);
        AddEvent(kind, caseId, ExternalCaseEventKinds.DocumentUnlinked, actor, now, documentId: documentId);
        AuditService.Record(db, AuditEventTypes.CaseDocumentUnlinked, kind == ExternalCaseKinds.Lab ? "LabCase" : "ReferralCase", caseId, actor, $"Document {documentId} unlinked from a {kind.ToLowerInvariant()} case.");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();                                                               // a concurrent request removed it first
            return new DocumentLinkResult(documentId, false, true);
        }
        return new DocumentLinkResult(documentId, false, false);
    }

    /// <summary>The documents linked to a case, oldest link first. A link whose document can no longer be shown is flagged, never dropped, so a broken link is seen rather than silently missing.</summary>
    /// <remarks>ALV-N026 (PD-24): links to documents in categories the caller may not read are left out (as if absent), so neither the document nor its name is disclosed.
    /// <paramref name="scope"/> null means no restriction; the controller always passes the caller's.</remarks>
    public async Task<IReadOnlyList<CaseDocumentView>> DocumentsAsync(string kind, Guid caseId, CancellationToken ct, DocumentReadScope? scope = null)
    {
        await LoadAsync(kind, caseId, false, ct);
        var readable = (scope ?? DocumentReadScope.Everything).Classifications;
        var links = db.ExternalCaseDocuments.AsNoTracking().Where(l => kind == ExternalCaseKinds.Referral ? l.ReferralCaseId == caseId : l.LabCaseId == caseId);
        var rows = await (from l in links
                          join d in db.Documents.AsNoTracking() on l.DocumentId equals d.Id
                          join cat in db.DocumentCategories.AsNoTracking() on d.CategoryId equals cat.Id
                          where readable.Contains(cat.Classification)
                          join i in db.DocumentIntegrity.AsNoTracking() on d.Id equals i.DocumentId into integrity
                          from i in integrity.DefaultIfEmpty()
                          orderby l.LinkedAtUtc
                          select new { d.Id, d.OriginalFileName, Category = cat.Name, d.DocumentDate, d.Description, d.Status, Integrity = i == null ? null : i.Status, l.LinkedAtUtc, l.LinkedByUserId }).ToListAsync(ct);
        return rows.Select(x =>
        {
            var reason = x.Status != DocumentStatuses.Active ? "The document was made inactive."
                : x.Integrity == IntegrityStatuses.Missing ? "The document's file is missing from storage."
                : x.Integrity == IntegrityStatuses.Corrupt ? "The document's file failed its integrity check." : null;
            return new CaseDocumentView(x.Id, x.OriginalFileName, x.Category, x.DocumentDate, x.Description, x.Status, reason is not null, reason, x.LinkedAtUtc, x.LinkedByUserId);
        }).ToList();
    }

    /// <summary>The case's history, oldest first, with who did each thing.</summary>
    public async Task<IReadOnlyList<CaseEventView>> EventsAsync(string kind, Guid caseId, CancellationToken ct)
    {
        await LoadAsync(kind, caseId, false, ct);
        var events = db.ExternalCaseEvents.AsNoTracking().Where(e => kind == ExternalCaseKinds.Referral ? e.ReferralCaseId == caseId : e.LabCaseId == caseId);
        var rows = await (from e in events join u in db.UserAccounts.AsNoTracking() on e.ActorUserId equals u.Id into users from u in users.DefaultIfEmpty()
                          orderby e.OccurredAtUtc, e.Id select new CaseEventView(e.Id, e.Kind, e.FromStatus, e.ToStatus, e.DocumentId, e.ActorUserId, u == null ? null : u.Username, e.OccurredAtUtc, e.Note)).ToListAsync(ct);
        return rows;
    }

    private Task<bool> LinkExistsAsync(string kind, Guid caseId, Guid documentId, CancellationToken ct) =>
        db.ExternalCaseDocuments.AsNoTracking().AnyAsync(l => l.DocumentId == documentId && (kind == ExternalCaseKinds.Referral ? l.ReferralCaseId == caseId : l.LabCaseId == caseId), ct);
}
