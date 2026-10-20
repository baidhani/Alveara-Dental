using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>What was accepted: the category, the cleaned description and the clinical context (an encounter of the patient, and a tooth or a region, never both).</summary>
internal sealed record CheckedMetadata(DocumentCategory Category, string? Description, Guid? EncounterId, string? ToothKey, string? RegionKey);

/// <summary>
/// The rules for what a person types about a document (category, date, description and the clinical context), shared by import and edit so the two cannot disagree. Every problem is listed, not just the first, so the screen can mark each
/// field (docs/DOCUMENTS.md, "Metadata error").
/// </summary>
internal static class DocumentMetadataChecker
{
    public const int DescriptionMax = 500;
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);

    /// <param name="currentCategoryId">For an edit, the category the document already has: keeping it is allowed even after it was deactivated (a document keeps its category); choosing a deactivated one is not.</param>
    public static async Task<CheckedMetadata> CheckAsync(AlveraDbContext db, IPracticeClock clock, Guid patientId, Guid? categoryId, DateOnly? documentDate, string? rawDescription,
        Guid? encounterId, string? toothKey, string? regionKey, Guid? currentCategoryId, CancellationToken ct)
    {
        var problems = new List<DocumentProblem>();
        DocumentCategory? category = null;
        if (categoryId is null) problems.Add(new("category", "required", "Choose a category."));
        else
        {
            category = await db.DocumentCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null) problems.Add(new("category", "unknown", "That category does not exist."));
            else if (!category.IsActive && category.Id != currentCategoryId) problems.Add(new("category", "inactive", "That category is no longer in use. Choose another."));
        }

        if (documentDate is not { } date) problems.Add(new("documentDate", "required", "Enter the date of the document."));
        else if (date < EarliestDate) problems.Add(new("documentDate", "invalid", "Enter a date from 1900 onward."));
        else if (date > DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime)) problems.Add(new("documentDate", "future", "The date of a document cannot be in the future."));

        var description = string.IsNullOrWhiteSpace(rawDescription) ? null : rawDescription.Trim();
        if (description is not null && description.Length > DescriptionMax) problems.Add(new("description", "too_long", $"The description can be at most {DescriptionMax} characters."));
        else if (description is not null && description.Any(char.IsControl)) problems.Add(new("description", "invalid", "The description cannot contain control characters."));

        // the clinical context: the same tooth and region lists a diagnosis uses, one or the other, and an encounter that belongs to this patient
        var tooth = string.IsNullOrWhiteSpace(toothKey) ? null : toothKey.Trim();
        var region = string.IsNullOrWhiteSpace(regionKey) ? null : regionKey.Trim();
        if (tooth is not null && !ToothKeys.IsValid(tooth)) problems.Add(new("toothKey", "unknown_tooth", "Use the two-digit FDI tooth number, such as 16 or 55, or leave the tooth empty."));
        if (region is not null && !DiagnosisRegions.All.Contains(region)) problems.Add(new("regionKey", "unknown_region", $"Use one of {string.Join(", ", DiagnosisRegions.All)}, or leave the region empty."));
        else if (region is not null && tooth is not null) problems.Add(new("regionKey", "conflicts_with_tooth", "A document is about one tooth or about a region, not both. Remove the tooth or the region."));
        if (encounterId is { } wanted)
        {
            var owner = await db.Encounters.AsNoTracking().Where(e => e.Id == wanted).Select(e => (Guid?)e.PatientId).SingleOrDefaultAsync(ct);
            if (owner is null) problems.Add(new("encounter", "unknown", "That encounter was not found."));
            else if (owner != patientId) problems.Add(new("encounter", "wrong_patient", "That encounter belongs to another patient."));
        }

        if (problems.Count > 0) throw new DocumentException(DocumentErrorCodes.MetadataInvalid, "The document has entries that need correcting. Nothing was saved.", 400, problems);
        return new CheckedMetadata(category!, description, encounterId, tooth, region);
    }
}
