using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: signing a draft encounter - the middle state between Draft and Finalized.
///
/// Signing is the clinician's attestation that the note is complete: it needs the same things finalizing needs (the four documentation sections addressed and every note
/// the template made required written) and refuses with the list of what is unresolved. A signed draft is locked (every change is refused with <c>encounter_signed</c>)
/// until it is finalized or unsigned; unsigning is allowed, audited, and keeps who signed and who unsigned in the history. Finalizing a signed or an unsigned draft both
/// work (STORY-005's finalize is unchanged). To the database a signed encounter is still a Draft; the lock is enforced by the service.
/// </summary>
public class EncounterSigningService(AlveraDbContext db, IPracticeClock clock)
{
    private EncounterReader Reader => new(db);

    public async Task<EncounterDetail> SignAsync(Guid encounterId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await db.Encounters.SingleOrDefaultAsync(x => x.Id == encounterId, ct) ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        if (encounter.Status != EncounterStatuses.Draft) throw new ClinicalException("encounter_finalized", "This encounter is already finalized.", 409);
        if (encounter.SignedAtUtc is not null) return await Reader.GetAsync(encounterId, ct); // a repeat: already signed
        ClinicalWrite.ApplyExpectedVersion(db, encounter, rowVersion);

        var detail = await Reader.GetAsync(encounterId, ct);
        EncounterReadiness.RequireReady(detail, "signed");

        var now = clock.UtcNow;
        encounter.SignedAtUtc = now; encounter.SignedByUserId = actor;
        ClinicalWrite.Touch(encounter, actor, now);
        ClinicalWrite.Stage(db, encounter, EncounterEventTypes.Signed, "EncounterSigned", actor, now, "Encounter signed.");
        return await SaveAsync(encounterId, ct);
    }

    public async Task<EncounterDetail> UnsignAsync(Guid encounterId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await db.Encounters.SingleOrDefaultAsync(x => x.Id == encounterId, ct) ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        if (encounter.Status != EncounterStatuses.Draft) throw new ClinicalException("encounter_finalized", "A finalized encounter cannot be unsigned; add an addendum.", 409);
        if (encounter.SignedAtUtc is null) return await Reader.GetAsync(encounterId, ct); // a repeat: already open
        ClinicalWrite.ApplyExpectedVersion(db, encounter, rowVersion);

        var now = clock.UtcNow;
        encounter.SignedAtUtc = null; encounter.SignedByUserId = null;
        ClinicalWrite.Touch(encounter, actor, now);
        ClinicalWrite.Stage(db, encounter, EncounterEventTypes.Unsigned, "EncounterUnsigned", actor, now, "Signature withdrawn; the draft is open for changes again.");
        return await SaveAsync(encounterId, ct);
    }

    private async Task<EncounterDetail> SaveAsync(Guid encounterId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Encounter), encounterId, ct);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsClinicalTrigger(ex))
        {
            throw new ClinicalException("encounter_finalized", "This encounter was finalized while you were editing it. Reload it.", 409);
        }
        return await Reader.GetAsync(encounterId, ct);
    }
}

/// <summary>What must be resolved before an encounter is signed or finalized, in one place so both refuse the same way and name the same things.</summary>
internal static class EncounterReadiness
{
    /// <summary>Throws a 409 <c>documentation_incomplete</c> naming every unresolved documentation section and every empty required note.</summary>
    public static void RequireReady(EncounterDetail detail, string verb)
    {
        if (detail.ReadyToSign) return;
        var errors = new Dictionary<string, string>();
        foreach (var k in detail.MissingSections) errors[k] = $"Record at least one entry or mark {EncounterRules.Label(k).ToLowerInvariant()} reviewed, none reported.";
        foreach (var n in detail.MissingNotes ?? []) errors["note:" + n] = $"Write the {NoteSections.Label(n).ToLowerInvariant()}; the template requires it.";
        var names = detail.MissingSections.Select(EncounterRules.Label).Concat((detail.MissingNotes ?? []).Select(NoteSections.Label));
        throw new ClinicalException("documentation_incomplete", $"The documentation is not complete and cannot be {verb}: " + string.Join(", ", names) + " still need attention.", 409, errors);
    }
}
