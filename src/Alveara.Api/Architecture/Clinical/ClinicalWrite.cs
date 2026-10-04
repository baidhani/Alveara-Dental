using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// The rules every clinical write shares (STORY-005 encounters, ALV-005-C01 notes, vitals, templates and signing): the caller's row version, "only an open draft
/// changes", and staging the history row and audit entry beside the change so the caller saves once. The forms module has the same version rule with its own exception.
/// </summary>
internal static class ClinicalWrite
{
    public static void ApplyExpectedVersion(AlveraDbContext db, Encounter encounter, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ClinicalException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            db.Entry(encounter).Property(e => e.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ClinicalException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }

    /// <summary>Loads an encounter for a change. A finalized one is refused (an addendum is the only addition) and so is a signed draft (unsign it first).</summary>
    public static async Task<Encounter> LoadOpenDraftAsync(AlveraDbContext db, Guid encounterId, string? rowVersion, CancellationToken ct)
    {
        var encounter = await db.Encounters.SingleOrDefaultAsync(x => x.Id == encounterId, ct) ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        ApplyExpectedVersion(db, encounter, rowVersion);
        if (encounter.Status != EncounterStatuses.Draft)
            throw new ClinicalException("encounter_finalized", "This encounter is finalized and cannot be changed. Add an addendum to correct or extend it.", 409);
        if (encounter.SignedAtUtc is not null)
            throw new ClinicalException("encounter_signed", "This note is signed. Unsign it to keep editing, or finalize it.", 409);
        return encounter;
    }

    public static void Touch(Encounter encounter, Guid actor, DateTimeOffset now)
    {
        encounter.UpdatedAtUtc = now;
        encounter.UpdatedByUserId = actor;
    }

    public static void Stage(AlveraDbContext db, Encounter encounter, string eventType, string auditType, Guid actor, DateTimeOffset now, string detail)
    {
        db.EncounterEvents.Add(new EncounterEvent { Id = Guid.NewGuid(), EncounterId = encounter.Id, PatientId = encounter.PatientId, EventType = eventType, ActorUserId = actor, OccurredAtUtc = now, Detail = detail });
        AuditService.Record(db, auditType, nameof(Encounter), encounter.Id, actor, detail);
    }

    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>True when the error is one of the clinical triggers' own refusals (the service's check was raced past, for example by a finalize that committed in between).</summary>
    public static bool IsClinicalTrigger(DbUpdateException ex) => ex.InnerException is SqlException { Number: >= 51030 and <= 51048 };
}
