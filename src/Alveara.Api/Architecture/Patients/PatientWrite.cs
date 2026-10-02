using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// The shared write pattern for changing an existing patient (ALV-003-C01): the caller must echo the row version it read, so a
/// stale edit is rejected as the shared 409 concurrency conflict instead of silently overwriting someone else's change;
/// the audit entry and the history rows are staged in the SAME SaveChanges as the change; and a unique-key race is reported
/// as a duplicate, not a raw exception. Audit text never contains patient values - history rows (in the patient record's
/// own table, behind the same permission) carry the old/new values.
/// </summary>
internal static class PatientWrite
{
    public static void ApplyExpectedVersion<T>(AlveraDbContext db, T entity, string? rowVersion) where T : class
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new PatientException("row_version_required", "The version you are editing is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new PatientException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }

    public static void Touch(Patient patient, DateTimeOffset now, Guid actor)
    {
        patient.UpdatedAtUtc = now;
        patient.UpdatedByUserId = actor;
    }

    public static void History(AlveraDbContext db, Guid patientId, DateTimeOffset now, Guid actor, string changeType, string field, string? oldValue, string? newValue) =>
        db.PatientHistory.Add(new PatientHistoryEntry
        {
            Id = Guid.NewGuid(), PatientId = patientId, ChangedAtUtc = now, ChangedByUserId = actor,
            ChangeType = changeType, FieldName = field, OldValue = Clip(oldValue), NewValue = Clip(newValue),
        });

    public static void Audit(AlveraDbContext db, string eventType, Guid patientId, Guid actor, string details) =>
        AuditService.Record(db, eventType, nameof(Patient), patientId, actor, details);

    public static async Task SaveAsync(AlveraDbContext db, Guid patientId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Patient), patientId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new PatientException("duplicate_patient", "A patient with the same name and date of birth is already registered.", 409);
        }
    }

    private static string? Clip(string? value) => value is { Length: > 400 } ? value[..400] : value;
}
