namespace Alveara.Api.Architecture.Forms;

/// <summary>
/// ALV-N010: a form/consent template. The template row is only the stable identity (key, category, active flag, which version
/// is current); the wording and fields live in immutable <see cref="FormTemplateVersion"/> rows. Editing a template never
/// updates a version - it appends a new one - so what a patient was shown can always be reconstructed.
/// Template wording is the practice's own; the system makes no claim that any wording is legally sufficient.
/// </summary>
public class FormTemplate
{
    public Guid Id { get; set; }
    /// <summary>Stable lowercase identifier, unique across templates (e.g. "privacy-notice").</summary>
    public required string Key { get; set; }
    public required string Category { get; set; }
    /// <summary>An inactive template cannot be started for a patient; forms already started or signed are unaffected.</summary>
    public bool IsActive { get; set; } = true;
    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>One immutable published version of a template. Never updated or deleted (enforced in the application and by a database trigger).</summary>
public class FormTemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public required string Title { get; set; }
    /// <summary>The text shown to the signer (plain text).</summary>
    public required string Body { get; set; }
    /// <summary>The field definitions, as canonical JSON (see <see cref="FormDefinition"/>).</summary>
    public required string FieldsJson { get; set; }
    /// <summary>SHA-256 over the canonical title, body and fields - proves the version was not altered.</summary>
    public required string ContentHash { get; set; }
    public string? ChangeNote { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// One patient's instance of a template: a draft being completed, a signed form, or a voided one. A draft is pinned to the
/// template version that was current when it was started; a later template edit never changes it (the user is offered the
/// newer version explicitly). Status is Draft, Signed or Void; a voided form keeps its signed snapshot (if it had one).
/// </summary>
public class PatientForm
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid TemplateVersionId { get; set; }
    public required string Status { get; set; }
    /// <summary>The entered values while a draft, keyed by field id.</summary>
    public required string ResponsesJson { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public Guid? StartedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// The signed artifact: an exact copy of the template version's wording and fields and the responses as shown at signing,
/// with the signer's identity/relationship and signature metadata. Immutable - never updated or deleted (enforced in the
/// application and by a database trigger); a later template edit, patient edit or void leaves it untouched. At most one
/// snapshot exists per form (unique index), which is what makes a duplicate submit unable to create a second artifact.
/// </summary>
public class SignedFormSnapshot
{
    public Guid Id { get; set; }
    public Guid PatientFormId { get; set; }
    public Guid PatientId { get; set; }
    public Guid TemplateId { get; set; }
    public required string TemplateKey { get; set; }
    public required string Category { get; set; }
    public Guid TemplateVersionId { get; set; }
    public int TemplateVersionNumber { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string FieldsJson { get; set; }
    public required string ResponsesJson { get; set; }
    public required string SignerName { get; set; }
    public required string SignerRelationship { get; set; }
    public string? SignerRelationshipNote { get; set; }
    /// <summary>How the signature was captured. Only "typed-name" exists; the column allows later methods without changing semantics.</summary>
    public required string SignatureMethod { get; set; }
    public required string SignatureText { get; set; }
    /// <summary>The attestation the signer agreed to, verbatim.</summary>
    public required string Attestation { get; set; }
    public DateTimeOffset SignedAtUtc { get; set; }
    public Guid? CapturedByUserId { get; set; }
    public int SnapshotSchemaVersion { get; set; }
    /// <summary>SHA-256 over the canonical snapshot content (see <see cref="FormDefinition.SnapshotHash"/>).</summary>
    public required string SnapshotHash { get; set; }
}

/// <summary>Status/history of one form: started, saved, signed, voided, newer version offered. Append-only; behind the same permission as the form.</summary>
public class PatientFormEvent
{
    public Guid Id { get; set; }
    public Guid PatientFormId { get; set; }
    public Guid PatientId { get; set; }
    public required string EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public int TemplateVersionNumber { get; set; }
    public string? Detail { get; set; }
}

public static class FormCategories
{
    public const string Privacy = "Privacy";
    public const string Financial = "Financial";
    public const string GeneralConsent = "GeneralConsent";
    public const string Treatment = "Treatment";
    public static readonly IReadOnlyList<string> All = [Privacy, Financial, GeneralConsent, Treatment];
    public static bool IsValid(string? v) => v is not null && All.Contains(v);
    /// <summary>The lowercase snake_case value used for measurement events.</summary>
    public static string Slug(string category) => category switch { GeneralConsent => "general_consent", _ => category.ToLowerInvariant() };
}

public static class FormStatuses
{
    public const string Draft = "Draft";
    public const string Signed = "Signed";
    public const string Void = "Void";
}

public static class FormFieldKinds
{
    public const string Text = "text";
    public const string LongText = "longText";
    public const string Checkbox = "checkbox";
    public const string Choice = "choice";
    public const string Date = "date";
    public static readonly IReadOnlyList<string> All = [Text, LongText, Checkbox, Choice, Date];
}

public static class SignerRelationships
{
    public const string Self = "Self";
    public static readonly IReadOnlyList<string> All = ["Self", "Parent", "Legal guardian", "Spouse", "Legal representative", "Other"];
    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}

public static class FormEventTypes
{
    public const string Started = "Started";
    public const string DraftSaved = "DraftSaved";
    public const string Signed = "Signed";
    public const string Voided = "Voided";
    public const string Superseded = "Superseded";
}

public static class FormAuditEvents
{
    public const string TemplateCreated = "FormTemplateCreated";
    public const string TemplateVersionPublished = "FormTemplateVersionPublished";
    public const string TemplateStatusChanged = "FormTemplateStatusChanged";
    public const string FormStarted = "PatientFormStarted";
    public const string FormSigned = "PatientFormSigned";
    public const string FormVoided = "PatientFormVoided";
}

/// <summary>A form operation was refused. <see cref="Code"/> is stable for the UI.</summary>
public class FormException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, Guid? existingId = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
    /// <summary>For already_signed: the id of the form's existing signed snapshot.</summary>
    public Guid? ExistingId { get; } = existingId;
}

/// <summary>Thrown when code (or a stray update) tries to change or delete a signed snapshot or a published template version.</summary>
public sealed class SignedRecordImmutableException() : InvalidOperationException(
    "Signed form snapshots and published template versions are immutable; correct a form by voiding it and completing a new one, and change a template by publishing a new version.");
