namespace Alveara.Api.Architecture.Logging;

/// <summary>
/// PHI-safe diagnostic/logging convention for ALV-N002. Ordinary application logs must support
/// correlation/troubleshooting without becoming a shadow patient record. Rather than trusting
/// every call site to remember this, correlation helpers here only ever accept identifiers
/// (entity ids, correlation ids, counts) — never a free-text string that a caller might
/// accidentally populate with a patient name or clinical note.
/// </summary>
public static class PhiSafeLog
{
    /// <summary>
    /// A correlation token safe to log: an entity id plus an entity-type label, never any other
    /// field. Use this instead of interpolating a raw record/DTO into a log message.
    /// </summary>
    public static string Correlate(string entityType, Guid entityId) => $"{entityType}:{entityId:N}";

    /// <summary>
    /// The list of field-name fragments that must never appear as a value (only as a key/label)
    /// in an ordinary diagnostic log. Used by <c>PhiSafeLoggingTests</c> to scan call sites;
    /// documented here as the single source of truth for what counts as PHI-shaped content.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownPhiFieldNames =
    [
        "patientname", "firstname", "lastname", "dateofbirth", "ssn", "address",
        "phonenumber", "email", "diagnosis", "clinicalnote", "medication", "allergy",
    ];
}
