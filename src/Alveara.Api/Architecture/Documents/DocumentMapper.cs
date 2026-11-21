using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>Builds the views of documents: the row, its category and the importer's display name. Shared by every document service so a document looks the same wherever it is read.</summary>
internal static class DocumentMapper
{
    /// <param name="includeCaseLinks">ALV-N026 (Q-5bs): also count the referral and lab cases that link each document; only for a caller who may read cases.</param>
    public static async Task<IReadOnlyList<DocumentView>> ViewsAsync(AlveraDbContext db, IReadOnlyList<PatientDocument> rows, CancellationToken ct, bool includeCaseLinks = false)
    {
        if (rows.Count == 0) return [];
        var categoryIds = rows.Select(r => r.CategoryId).Distinct().ToList();
        var categories = await db.DocumentCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.SelectMany(r => new Guid?[] { r.ImportedByUserId, r.InactivatedByUserId }), ct);
        var documentIds = rows.Select(r => r.Id).ToList();
        var integrity = await db.DocumentIntegrity.AsNoTracking().Where(i => documentIds.Contains(i.DocumentId)).ToDictionaryAsync(i => i.DocumentId, ct);
        var links = includeCaseLinks
            ? await db.ExternalCaseDocuments.AsNoTracking().Where(l => documentIds.Contains(l.DocumentId)).GroupBy(l => l.DocumentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct)
            : null;
        return rows.Select(r => new DocumentView(r.Id, r.PatientId, r.CategoryId, categories[r.CategoryId].Code, categories[r.CategoryId].Name, r.DocumentDate, r.Description, r.DuplicateOfDocumentId,
            r.OriginalFileName, r.DetectedType, r.DetectedType != "other", r.SizeBytes, r.Sha256, ClinicalNames.Name(names, r.ImportedByUserId)!, r.ImportedAtUtc, Convert.ToBase64String(r.RowVersion),
            r.EncounterId, r.ToothKey, r.RegionKey, r.Status, r.InactiveReason, ClinicalNames.Name(names, r.InactivatedByUserId), r.InactivatedAtUtc,
            integrity.TryGetValue(r.Id, out var check) ? check.Status : IntegrityStatuses.Unchecked, check?.CheckedAtUtc, links is null ? null : links.GetValueOrDefault(r.Id))).ToList();
    }

    public static async Task<DocumentView> ViewAsync(AlveraDbContext db, PatientDocument row, CancellationToken ct) => (await ViewsAsync(db, [row], ct))[0];
}
