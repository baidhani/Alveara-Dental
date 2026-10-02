using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Forms;

/// <summary>One field of a template version.</summary>
public record FormFieldDefinition(string Id, string Label, string Kind, bool Required, IReadOnlyList<string>? Options = null);

/// <summary>
/// ALV-N010: validation, canonical serialization and hashing for template content and signed snapshots. Everything that
/// ends up inside a hash is serialized here, with a fixed property order and ordinally sorted response keys, so the same
/// content always produces the same bytes - which is what lets a snapshot's integrity be re-verified at any later time.
/// </summary>
public static class FormDefinition
{
    public const int MaxTitle = 200;
    public const int MaxBody = 20000;
    public const int MaxFields = 40;
    public const int MaxLabel = 200;
    public const int MaxOptions = 20;
    public const int MaxOptionLength = 100;
    public const int MaxTextValue = 2000;
    public const int MaxLongTextValue = 10000;
    public const int SnapshotSchemaVersion = 1;

    /// <summary>The statement the signer agrees to when signing. Stored verbatim in every snapshot.</summary>
    public const string Attestation =
        "I have read this form and the answers shown are correct. I am signing it as the person identified above, " +
        "and I understand that typing my name here is my signature.";

    private static readonly Regex FieldIdPattern = new("^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled);
    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9-]{1,58}[a-z0-9]$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    // ---------- template content ----------

    public static string NormalizeKey(string? key, IDictionary<string, string> errors)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(k)) errors["key"] = "Use 3-60 lowercase letters, digits and hyphens (for example privacy-notice).";
        return k;
    }

    public static string NormalizeText(string? value, int max, string field, string label, IDictionary<string, string> errors, bool multiline = false)
    {
        var v = (value ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (v.Length == 0) errors[field] = $"{label} is required.";
        else if (v.Length > max) errors[field] = $"{label} must be {max} characters or fewer.";
        else if (!multiline && v.Contains('\n')) errors[field] = $"{label} must be on one line.";
        return v;
    }

    public static IReadOnlyList<FormFieldDefinition> NormalizeFields(IReadOnlyList<FormFieldDefinition>? fields, IDictionary<string, string> errors)
    {
        var list = new List<FormFieldDefinition>();
        if (fields is null) return list;
        if (fields.Count > MaxFields) errors["fields"] = $"A form can have at most {MaxFields} fields.";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count && i < MaxFields; i++)
        {
            var f = fields[i];
            var at = $"fields[{i}]";
            var id = (f.Id ?? string.Empty).Trim();
            if (!FieldIdPattern.IsMatch(id)) errors[$"{at}.id"] = "Field id: lowercase letters, digits and underscores, starting with a letter (max 40).";
            else if (!seen.Add(id)) errors[$"{at}.id"] = $"Field id '{id}' is used twice.";
            var label = (f.Label ?? string.Empty).Trim();
            if (label.Length == 0) errors[$"{at}.label"] = "Field label is required.";
            else if (label.Length > MaxLabel) errors[$"{at}.label"] = $"Field label must be {MaxLabel} characters or fewer.";
            var kind = f.Kind ?? string.Empty;
            if (!FormFieldKinds.All.Contains(kind)) errors[$"{at}.kind"] = "Field type must be text, longText, checkbox, choice or date.";
            IReadOnlyList<string>? options = null;
            if (kind == FormFieldKinds.Choice)
            {
                var o = (f.Options ?? []).Select(x => (x ?? string.Empty).Trim()).ToList();
                if (o.Count < 2 || o.Count > MaxOptions || o.Any(x => x.Length == 0 || x.Length > MaxOptionLength) || o.Distinct(StringComparer.Ordinal).Count() != o.Count)
                    errors[$"{at}.options"] = $"A choice needs 2-{MaxOptions} different options of up to {MaxOptionLength} characters.";
                options = o;
            }
            list.Add(new FormFieldDefinition(id, label, kind, f.Required, options));
        }
        return list;
    }

    public static string SerializeFields(IReadOnlyList<FormFieldDefinition> fields) => JsonSerializer.Serialize(fields, Json);

    public static IReadOnlyList<FormFieldDefinition> ParseFields(string fieldsJson) =>
        JsonSerializer.Deserialize<List<FormFieldDefinition>>(fieldsJson, Json) ?? [];

    public static string ContentHash(string title, string body, string fieldsJson) =>
        Sha256(JsonSerializer.Serialize(new { title, body, fieldsJson }, Json));

    // ---------- responses ----------

    /// <summary>Validates entered values against the version's fields. A draft may be incomplete; signing needs every required field.</summary>
    public static SortedDictionary<string, string> NormalizeResponses(
        IReadOnlyList<FormFieldDefinition> fields, IReadOnlyDictionary<string, string?>? responses, bool requireComplete, IDictionary<string, string> errors)
    {
        var clean = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var byId = fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
        foreach (var (id, raw) in responses ?? new Dictionary<string, string?>())
        {
            if (!byId.TryGetValue(id, out var field)) { errors[$"responses.{id}"] = "This form has no such field."; continue; }
            var value = (raw ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (value.Length == 0) continue; // an empty answer is the same as no answer
            var error = field.Kind switch
            {
                FormFieldKinds.Text => value.Length > MaxTextValue || value.Contains('\n') ? $"Use one line of up to {MaxTextValue} characters." : null,
                FormFieldKinds.LongText => value.Length > MaxLongTextValue ? $"Use up to {MaxLongTextValue} characters." : null,
                FormFieldKinds.Checkbox => value is "true" or "false" ? null : "Checked or not checked.",
                FormFieldKinds.Choice => field.Options!.Contains(value) ? null : "Choose one of the listed options.",
                FormFieldKinds.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : "Use a date as yyyy-MM-dd.",
                _ => "Unknown field type.",
            };
            if (error is not null) { errors[$"responses.{id}"] = error; continue; }
            if (field.Kind == FormFieldKinds.Checkbox && value == "false") continue; // unchecked == absent
            clean[id] = value;
        }
        if (requireComplete)
            foreach (var f in fields.Where(f => f.Required && !clean.ContainsKey(f.Id)))
                errors[$"responses.{f.Id}"] = f.Kind == FormFieldKinds.Checkbox ? "This must be checked." : "This is required.";
        return clean;
    }

    public static string SerializeResponses(IReadOnlyDictionary<string, string> responses) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string>(responses.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal), Json);

    public static IReadOnlyDictionary<string, string> ParseResponses(string responsesJson) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(responsesJson, Json) ?? [];

    // ---------- snapshot integrity ----------

    public static string SnapshotHash(SignedFormSnapshot s) => Sha256(JsonSerializer.Serialize(new
    {
        schema = s.SnapshotSchemaVersion, s.PatientFormId, s.PatientId, s.TemplateKey, s.Category, s.TemplateVersionId, s.TemplateVersionNumber,
        s.Title, s.Body, s.FieldsJson, s.ResponsesJson, s.SignerName, s.SignerRelationship, s.SignerRelationshipNote,
        s.SignatureMethod, s.SignatureText, s.Attestation, signedAt = s.SignedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
    }, Json));

    /// <summary>True when the stored content still hashes to the hash recorded at signing.</summary>
    public static bool VerifyIntegrity(SignedFormSnapshot s) => string.Equals(SnapshotHash(s), s.SnapshotHash, StringComparison.Ordinal);

    public static bool VerifyIntegrity(FormTemplateVersion v) => string.Equals(ContentHash(v.Title, v.Body, v.FieldsJson), v.ContentHash, StringComparison.Ordinal);

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
