using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: vital signs recorded during an encounter, attributed to the encounter, the patient, the time they were measured and who recorded them.
///
/// - A reading is never edited: a mistake is VOIDED (kept, with who and why) and the correct reading recorded again; the database refuses any other change.
/// - Readings are added only to an unsigned draft encounter; after finalizing, the only addition is an addendum.
/// - The client key makes a retried save return the first reading instead of adding a second; the same key with different values is refused, never merged.
/// - History and audit text carry no measurements (the values live in the vitals table, behind the clinical permissions).
/// </summary>
public class EncounterVitalsService(AlveraDbContext db, IPracticeClock clock)
{
    private EncounterReader Reader => new(db);

    public async Task<(EncounterDetail Detail, bool Created)> RecordAsync(Guid encounterId, VitalsInput input, string? clientKey, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var v = EncounterNoteRules.ValidateVitals(input, now);
        clientKey = EncounterRules.Clean(clientKey);
        if (clientKey is null) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["clientKey"] = "A request key is required so a retry never records the reading twice." });
        if (clientKey.Length > EncounterRules.KeyMax) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["clientKey"] = "The request key is too long." });

        var replay = await db.EncounterVitals.AsNoTracking().SingleOrDefaultAsync(x => x.EncounterId == encounterId && x.ClientKey == clientKey, ct);
        if (replay is not null) return (await ReplayAsync(replay, v, ct), false);

        var encounter = await ClinicalWrite.LoadOpenDraftAsync(db, encounterId, rowVersion, ct);
        var reading = new EncounterVitals
        {
            Id = Guid.NewGuid(), EncounterId = encounterId, PatientId = encounter.PatientId, MeasuredAtUtc = v.MeasuredAtUtc ?? now, SystolicMmHg = v.SystolicMmHg, DiastolicMmHg = v.DiastolicMmHg,
            PulseBpm = v.PulseBpm, RespirationsPerMinute = v.RespirationsPerMinute, TemperatureC = v.TemperatureC, OxygenSaturationPercent = v.OxygenSaturationPercent, WeightKg = v.WeightKg,
            HeightCm = v.HeightCm, Note = v.Note, ClientKey = clientKey, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.EncounterVitals.Add(reading);
        ClinicalWrite.Touch(encounter, actor, now);
        ClinicalWrite.Stage(db, encounter, EncounterEventTypes.VitalsRecorded, "EncounterVitalsRecorded", actor, now, "Vital signs recorded.");
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Encounter), encounterId, ct);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // the same key arrived twice at once: the first one won
            var winner = await db.EncounterVitals.AsNoTracking().SingleAsync(x => x.EncounterId == encounterId && x.ClientKey == clientKey, ct);
            return (await ReplayAsync(winner, v, ct), false);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsClinicalTrigger(ex))
        {
            throw new ClinicalException("encounter_finalized", "This encounter was finalized while you were editing it. Reload it; add an addendum to extend it.", 409);
        }
        return (await Reader.GetAsync(encounterId, ct), true);
    }

    /// <summary>Voids a reading entered by mistake. A reason is required; voiding a voided reading changes nothing.</summary>
    public async Task<EncounterDetail> VoidAsync(Guid encounterId, Guid vitalsId, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        reason = ClinicalRecordRules.CleanReason(reason);
        if (reason is null) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["reason"] = "Say why the reading is being voided." });
        var encounter = await ClinicalWrite.LoadOpenDraftAsync(db, encounterId, rowVersion, ct);
        var reading = await db.EncounterVitals.SingleOrDefaultAsync(x => x.Id == vitalsId && x.EncounterId == encounterId, ct) ?? throw new ClinicalException("vitals_not_found", "That reading was not found on this encounter.", 404);
        if (reading.VoidedAtUtc is not null) return await Reader.GetAsync(encounterId, ct);

        var now = clock.UtcNow;
        reading.VoidedAtUtc = now; reading.VoidedByUserId = actor; reading.VoidReason = reason;
        ClinicalWrite.Touch(encounter, actor, now);
        ClinicalWrite.Stage(db, encounter, EncounterEventTypes.VitalsVoided, "EncounterVitalsVoided", actor, now, "Vital signs voided as entered in error.");
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Encounter), encounterId, ct);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsClinicalTrigger(ex))
        {
            throw new ClinicalException("encounter_finalized", "This encounter was finalized while you were editing it. Reload it; add an addendum to extend it.", 409);
        }
        return await Reader.GetAsync(encounterId, ct);
    }

    private async Task<EncounterDetail> ReplayAsync(EncounterVitals existing, VitalsInput v, CancellationToken ct)
    {
        var same = existing.SystolicMmHg == v.SystolicMmHg && existing.DiastolicMmHg == v.DiastolicMmHg && existing.PulseBpm == v.PulseBpm && existing.RespirationsPerMinute == v.RespirationsPerMinute
                   && existing.TemperatureC == v.TemperatureC && existing.OxygenSaturationPercent == v.OxygenSaturationPercent && existing.WeightKg == v.WeightKg && existing.HeightCm == v.HeightCm
                   && existing.Note == v.Note && (v.MeasuredAtUtc is null || existing.MeasuredAtUtc == v.MeasuredAtUtc);
        if (!same) throw new ClinicalException("idempotency_key_reused", "That request key was already used for a different reading.", 409);
        return await Reader.GetAsync(existing.EncounterId, ct);
    }
}
