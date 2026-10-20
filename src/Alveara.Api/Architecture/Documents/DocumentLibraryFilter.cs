namespace Alveara.Api.Architecture.Documents;

/// <summary>Which documents of a patient the library shows. Every field is optional and they combine (all must match). Signed forms are listed only when nothing narrows to a document's own properties.</summary>
public sealed record DocumentLibraryFilter(Guid? CategoryId = null, Guid? EncounterId = null, string? ToothKey = null, string? RegionKey = null, DateOnly? From = null, DateOnly? To = null, string? Text = null,
    string? Status = null)
{
    public const string StatusActive = "Active";
    public const string StatusInactive = "Inactive";
    public const string StatusAll = "All";
    public const int TextMax = 100;
}
