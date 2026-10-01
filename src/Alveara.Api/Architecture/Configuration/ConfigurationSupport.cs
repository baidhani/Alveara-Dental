using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Configuration;

/// <summary>
/// ALV-N003: a configuration rule was violated. <see cref="Code"/> is the stable machine-readable
/// reason the UI maps to an inline message; <see cref="StatusCode"/> is the HTTP status the
/// controller returns (400 invalid input, 404 missing, 409 conflicts with existing state).
/// </summary>
public sealed class ConfigurationException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Audit event names for material configuration changes (written through the shared AuditService).</summary>
public static class ConfigurationAuditEvents
{
    public const string Created = "ConfigurationCreated";
    public const string Updated = "ConfigurationUpdated";
    public const string Inactivated = "ConfigurationInactivated";
    public const string Reactivated = "ConfigurationReactivated";
    public const string StaffAccountLinkChanged = "StaffAccountLinkChanged";
    public const string ProviderAvailabilityReplaced = "ProviderAvailabilityReplaced";
    public const string ProviderBlockedTimeAdded = "ProviderBlockedTimeAdded";
    public const string ProviderBlockedTimeRemoved = "ProviderBlockedTimeRemoved";
}

/// <summary>
/// The reusable configuration write pattern later domain stories extend with their own settings:
/// audit staged in the SAME SaveChanges as the change (so neither persists without the other),
/// optimistic concurrency via the caller's row version, and database unique-constraint violations
/// translated to a stable 409 rather than a raw exception.
/// </summary>
internal static class ConfigurationWrite
{
    /// <summary>Decodes the base64 row version a client echoes back; required on every update.</summary>
    public static byte[] ParseRowVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            throw new ConfigurationException("row_version_required",
                "The version you are editing is required so a concurrent change is never overwritten. Reload and try again.");
        }
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ConfigurationException("row_version_invalid", "The supplied version is not valid. Reload and try again.");
        }
    }

    /// <summary>Makes EF compare against the version the CALLER read, not whatever it just loaded.</summary>
    public static void ApplyExpectedVersion<T>(AlveraDbContext db, T entity, string? rowVersion)
        where T : class
    {
        var expected = ParseRowVersion(rowVersion);
        db.Entry(entity).Property("RowVersion").OriginalValue = expected;
    }

    public static void Audit(AlveraDbContext db, string eventType, string entityType, Guid entityId, Guid performedBy, string details, string? reason = null) =>
        AuditService.Record(db, eventType, entityType, entityId, performedBy, details, reason);

    public static async Task SaveAsync(AlveraDbContext db, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, entityType, entityId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A race the friendly pre-checks could not see: the database's own unique constraint won.
            throw new ConfigurationException("duplicate_configuration",
                "That conflicts with an existing record. Reload to see the current configuration.", 409);
        }
    }

    public static string RequireName(string? name, string what, int maxLength = 120)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ConfigurationException("name_required", $"{what} is required.");
        if (trimmed.Length > maxLength)
            throw new ConfigurationException("name_too_long", $"{what} must be {maxLength} characters or fewer.");
        return trimmed;
    }

    public static string? OptionalText(string? value, string what, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new ConfigurationException("text_too_long", $"{what} must be {maxLength} characters or fewer.");
        return trimmed;
    }
}
