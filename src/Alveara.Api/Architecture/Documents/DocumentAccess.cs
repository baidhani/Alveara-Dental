using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// ALV-N026 (PD-24): which classifications of document a caller may read. Every document endpoint already requires <see cref="Permission.ViewDocuments"/>; on top of that a Clinical document needs
/// <see cref="Permission.ViewClinicalDocumentation"/>. A document the caller may not read is treated as if it did not exist (404, left out of lists), so its existence is not disclosed.
/// <see cref="None"/> reads nothing, which is what an unparseable role gets.
/// </summary>
public sealed record DocumentReadScope(bool NonClinical, bool Clinical)
{
    public static readonly DocumentReadScope None = new(false, false);

    /// <summary>Reads every classification. For callers that are not acting for a person (system jobs, storage checks) and for tests of behaviour unrelated to visibility.</summary>
    public static readonly DocumentReadScope Everything = new(true, true);

    public bool Allows(string classification) => classification switch
    {
        DocumentClassifications.NonClinical => NonClinical,
        DocumentClassifications.Clinical => Clinical,
        _ => Clinical,   // anything unrecognized is treated as Clinical (deliberately conservative)
    };

    /// <summary>The classifications this scope reads, for a database filter.</summary>
    public List<string> Classifications
    {
        get
        {
            var list = new List<string>();
            if (NonClinical) list.Add(DocumentClassifications.NonClinical);
            if (Clinical) list.Add(DocumentClassifications.Clinical);
            return list;
        }
    }

    /// <summary>The ids of the categories this scope reads.</summary>
    public async Task<List<Guid>> ReadableCategoryIdsAsync(AlveraDbContext db, CancellationToken ct)
    {
        var readable = Classifications;
        return await db.DocumentCategories.AsNoTracking().Where(c => readable.Contains(c.Classification)).Select(c => c.Id).ToListAsync(ct);
    }

    /// <summary>Whether this scope reads the documents of <paramref name="categoryId"/>; an unknown category is not readable.</summary>
    public async Task<bool> ReadsCategoryAsync(AlveraDbContext db, Guid categoryId, CancellationToken ct)
    {
        var classification = await db.DocumentCategories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.Classification).SingleOrDefaultAsync(ct);
        return classification is not null && Allows(classification);
    }
}

/// <summary>ALV-N026 (PD-24): resolves a signed-in caller's <see cref="DocumentReadScope"/> and whether they may manage documents, from the effective permission policy (overrides included).</summary>
public class DocumentAccess(IEffectivePermissions permissions)
{
    public async Task<DocumentReadScope> ScopeAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(user.FindFirstValue(ClaimTypes.Role), out var role)) return DocumentReadScope.None;
        var nonClinical = await permissions.HasAsync(role, Permission.ViewDocuments, ct);
        var clinical = nonClinical && await permissions.HasAsync(role, Permission.ViewClinicalDocumentation, ct);
        return new DocumentReadScope(nonClinical, clinical);
    }

    /// <summary>ALV-N026 (Q-5bs): whether the caller may read referral and lab cases, and so see that a document is linked to one.</summary>
    public async Task<bool> ReadsCasesAsync(ClaimsPrincipal user, CancellationToken ct) =>
        Enum.TryParse<Role>(user.FindFirstValue(ClaimTypes.Role), out var role) && await permissions.HasAsync(role, Permission.ViewExternalCases, ct);
}

/// <summary>ALV-N026 (PD-24): the refusal for importing into, or moving a document into, a category the caller may not read.</summary>
public static class DocumentAccessErrors
{
    public const string CategoryNotPermittedCode = "category_not_permitted";

    public static DocumentException CategoryNotPermitted() =>
        new(CategoryNotPermittedCode, "You do not have access to documents in that category, so you cannot file a document there. Choose another category or ask an administrator.", 403);
}
