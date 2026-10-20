using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// The rules for what a person types about a document (category, date, description), shared by import and edit so the two cannot disagree. Every problem is listed, not just the first, so the screen can mark each
/// field (docs/DOCUMENTS.md, "Metadata error").
/// </summary>
internal static class DocumentMetadataChecker
{
    public const int DescriptionMax = 500;
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);

    /// <param name="currentCategoryId">For an edit, the category the document already has: keeping it is allowed even after it was deactivated (a document keeps its category); choosing a deactivated one is not.</param>
    public static async Task<(DocumentCategory Category, string? Description)> CheckAsync(AlveraDbContext db, IPracticeClock clock, Guid? categoryId, DateOnly? documentDate, string? rawDescription,
        Guid? currentCategoryId, CancellationToken ct)
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

        if (problems.Count > 0) throw new DocumentException(DocumentErrorCodes.MetadataInvalid, "The document has entries that need correcting. Nothing was saved.", 400, problems);
        return (category!, description);
    }
}
