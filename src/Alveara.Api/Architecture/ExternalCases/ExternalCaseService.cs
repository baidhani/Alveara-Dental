using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.ExternalCases;

/// <summary>
/// ALV-009-C02: referral and dental-laboratory cases. Every write does its case change, its history entry and its audit entry in ONE save, so any failure (the audit write included) changes nothing. A create is
/// idempotent by construction: the case id is made from the person, the patient and the Idempotency-Key, so a repeat meets the same primary key and answers with the case already made (a different request under the
/// same key is <c>key_reused</c>, 409). Every change after that carries the row version the person saw; a stale one is <c>case_changed</c> (409) and nothing is stored. A case names an active outside party of the
/// matching kind (<c>party_required</c>, <c>party_not_found</c>, <c>party_inactive</c>, <c>party_kind_invalid</c>), moves between statuses only as <see cref="ExternalCaseRules"/> allows, and is final once completed or cancelled
/// (<c>case_closed</c>). Documents are linked, never copied (see <c>ExternalCaseService.Documents.cs</c>). The follow-up task that chases an overdue case is made and closed by a durable job, not here. Who may call
/// this is decided by the permission attributes on the controller; this service decides what is allowed to be stored.
/// </summary>
public partial class ExternalCaseService(AlveraDbContext db, IPracticeClock clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).Date);

    // ---------- create ----------

    public async Task<ExternalCaseResult> CreateReferralAsync(Guid actor, Guid patientId, CreateReferralRequest r, string? idempotencyKey, CancellationToken ct)
    {
        var key = ExternalCaseValidation.Key(idempotencyKey);
        var reason = ExternalCaseValidation.Required(r.Reason, ExternalCaseRules.ReasonMaxLength, "reason_invalid", "the reason for the referral");
        var due = ExternalCaseValidation.Due(r.DueOn, Today);
        var notes = ExternalCaseValidation.Note(r.Notes);
        var partyId = r.PartyId ?? throw new ExternalCaseException("party_required", "Choose who the patient is being referred to.", 400);
        var id = ExternalCaseValidation.DeterministicId("referral", actor, patientId, key);

        var existing = await db.ReferralCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing is not null) return await ReplayAsync(existing, existing.PartyId == partyId && existing.Reason == reason && existing.DueOn == due && existing.EncounterId == r.EncounterId && existing.Notes == notes, ct);

        await RequirePatientAsync(patientId, ct);
        await RequirePartyAsync(partyId, ExternalPartyKinds.ReferralProvider, ct);
        if (r.EncounterId is { } enc) await RequireEncounterAsync(enc, patientId, ct);

        var now = clock.UtcNow;
        var c = new ReferralCase
        {
            Id = id, PatientId = patientId, PartyId = partyId, Reason = reason, Status = ExternalCaseRules.Draft, DueOn = due, EncounterId = r.EncounterId, Notes = notes,
            IdempotencyKey = key, CreatedAtUtc = now, CreatedByUserId = actor, UpdatedAtUtc = now,
        };
        db.ReferralCases.Add(c);
        AddEvent(ExternalCaseKinds.Referral, c.Id, ExternalCaseEventKinds.Created, actor, now, toStatus: ExternalCaseRules.Draft);
        AuditService.Record(db, AuditEventTypes.ReferralCaseCreated, "ReferralCase", c.Id, actor, $"Referral created for patient {patientId}.");
        return await SaveNewAsync(c, ct);
    }

    public async Task<ExternalCaseResult> CreateLabAsync(Guid actor, Guid patientId, CreateLabRequest r, string? idempotencyKey, CancellationToken ct)
    {
        var key = ExternalCaseValidation.Key(idempotencyKey);
        var title = ExternalCaseValidation.Required(r.Title, ExternalCaseRules.TitleMaxLength, "title_invalid", "what is being made");
        var due = ExternalCaseValidation.Due(r.DueOn, Today);
        var tooth = ExternalCaseValidation.ToothKey(r.ToothKey);
        var material = ExternalCaseValidation.Optional(r.Material, ExternalCaseRules.MaterialMaxLength, "material_invalid", "material");
        var shade = ExternalCaseValidation.Optional(r.Shade, ExternalCaseRules.ShadeMaxLength, "shade_invalid", "shade");
        var notes = ExternalCaseValidation.Note(r.Notes);
        var partyId = r.PartyId ?? throw new ExternalCaseException("party_required", "Choose the laboratory the work is going to.", 400);
        var id = ExternalCaseValidation.DeterministicId("lab", actor, patientId, key);

        var existing = await db.LabCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing is not null)
            return await ReplayAsync(existing, existing.PartyId == partyId && existing.Title == title && existing.DueOn == due && existing.ToothKey == (tooth ?? existing.ToothKey) && existing.TreatmentPlanItemId == r.TreatmentPlanItemId
                                              && existing.Material == material && existing.Shade == shade && existing.Notes == notes, ct);

        await RequirePatientAsync(patientId, ct);
        await RequirePartyAsync(partyId, ExternalPartyKinds.LabVendor, ct);
        if (r.TreatmentPlanItemId is { } item) tooth ??= await RequirePlanItemAsync(item, patientId, ct);

        var now = clock.UtcNow;
        var c = new LabCase
        {
            Id = id, PatientId = patientId, PartyId = partyId, Title = title, Status = ExternalCaseRules.Draft, DueOn = due, ToothKey = tooth, TreatmentPlanItemId = r.TreatmentPlanItemId,
            Material = material, Shade = shade, Notes = notes, IdempotencyKey = key, CreatedAtUtc = now, CreatedByUserId = actor, UpdatedAtUtc = now,
        };
        db.LabCases.Add(c);
        AddEvent(ExternalCaseKinds.Lab, c.Id, ExternalCaseEventKinds.Created, actor, now, toStatus: ExternalCaseRules.Draft);
        AuditService.Record(db, AuditEventTypes.LabCaseCreated, "LabCase", c.Id, actor, $"Lab case created for patient {patientId}.");
        return await SaveNewAsync(c, ct);
    }

    // ---------- edit ----------

    public async Task<ExternalCaseResult> UpdateReferralAsync(Guid actor, Guid id, UpdateReferralRequest r, CancellationToken ct)
    {
        var version = ExternalCaseValidation.Version(r.RowVersion);
        var reason = ExternalCaseValidation.Required(r.Reason, ExternalCaseRules.ReasonMaxLength, "reason_invalid", "the reason for the referral");
        var due = ExternalCaseValidation.Due(r.DueOn, Today);
        var notes = ExternalCaseValidation.Note(r.Notes);
        var partyId = r.PartyId ?? throw new ExternalCaseException("party_required", "Choose who the patient is being referred to.", 400);
        var c = await db.ReferralCases.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        RequireOpen(c);
        if (partyId != c.PartyId) await ChangePartyAsync(c, partyId, ExternalPartyKinds.ReferralProvider, ct);
        if (r.EncounterId is { } enc && enc != c.EncounterId) await RequireEncounterAsync(enc, c.PatientId, ct);
        if (partyId == c.PartyId && reason == c.Reason && due == c.DueOn && r.EncounterId == c.EncounterId && notes == c.Notes) throw NoChange();

        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;
        c.Reason = reason; c.DueOn = due; c.EncounterId = r.EncounterId; c.Notes = notes; c.UpdatedAtUtc = clock.UtcNow;
        AddEvent(ExternalCaseKinds.Referral, c.Id, ExternalCaseEventKinds.Edited, actor, c.UpdatedAtUtc);
        AuditService.Record(db, AuditEventTypes.ReferralCaseChanged, "ReferralCase", c.Id, actor, "Referral changed.");
        await SaveAsync(c.Id, ct);
        return new ExternalCaseResult(await ViewAsync(c, ct), false);
    }

    public async Task<ExternalCaseResult> UpdateLabAsync(Guid actor, Guid id, UpdateLabRequest r, CancellationToken ct)
    {
        var version = ExternalCaseValidation.Version(r.RowVersion);
        var title = ExternalCaseValidation.Required(r.Title, ExternalCaseRules.TitleMaxLength, "title_invalid", "what is being made");
        var due = ExternalCaseValidation.Due(r.DueOn, Today);
        var tooth = ExternalCaseValidation.ToothKey(r.ToothKey);
        var material = ExternalCaseValidation.Optional(r.Material, ExternalCaseRules.MaterialMaxLength, "material_invalid", "material");
        var shade = ExternalCaseValidation.Optional(r.Shade, ExternalCaseRules.ShadeMaxLength, "shade_invalid", "shade");
        var notes = ExternalCaseValidation.Note(r.Notes);
        var partyId = r.PartyId ?? throw new ExternalCaseException("party_required", "Choose the laboratory the work is going to.", 400);
        var c = await db.LabCases.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        RequireOpen(c);
        if (partyId != c.PartyId) await ChangePartyAsync(c, partyId, ExternalPartyKinds.LabVendor, ct);
        if (r.TreatmentPlanItemId is { } item && item != c.TreatmentPlanItemId) tooth ??= await RequirePlanItemAsync(item, c.PatientId, ct);
        if (partyId == c.PartyId && title == c.Title && due == c.DueOn && tooth == c.ToothKey && r.TreatmentPlanItemId == c.TreatmentPlanItemId && material == c.Material && shade == c.Shade && notes == c.Notes) throw NoChange();

        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;
        c.Title = title; c.DueOn = due; c.ToothKey = tooth; c.TreatmentPlanItemId = r.TreatmentPlanItemId; c.Material = material; c.Shade = shade; c.Notes = notes; c.UpdatedAtUtc = clock.UtcNow;
        AddEvent(ExternalCaseKinds.Lab, c.Id, ExternalCaseEventKinds.Edited, actor, c.UpdatedAtUtc);
        AuditService.Record(db, AuditEventTypes.LabCaseChanged, "LabCase", c.Id, actor, "Lab case changed.");
        await SaveAsync(c.Id, ct);
        return new ExternalCaseResult(await ViewAsync(c, ct), false);
    }

    // ---------- read ----------

    public async Task<ExternalCaseView> GetAsync(string kind, Guid id, CancellationToken ct) => await ViewAsync(await LoadAsync(kind, id, false, ct), ct);

    // ---------- shared pieces ----------

    private static ExternalCaseException NotFound() => new("case_not_found", "That case was not found.", 404);
    private static ExternalCaseException NoChange() => new("no_change", "Nothing was changed.", 409);

    private static void RequireOpen(IExternalCase c)
    {
        if (ExternalCaseRules.IsFinal(c.Status)) throw new ExternalCaseException("case_closed", $"That case is already {c.Status.ToLowerInvariant()} and cannot be changed.", 409);
    }

    private async Task<IExternalCase> LoadAsync(string kind, Guid id, bool track, CancellationToken ct)
    {
        if (kind == ExternalCaseKinds.Referral)
            return (track ? await db.ReferralCases.FirstOrDefaultAsync(c => c.Id == id, ct) : await db.ReferralCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)) ?? throw NotFound();
        if (kind == ExternalCaseKinds.Lab)
            return (track ? await db.LabCases.FirstOrDefaultAsync(c => c.Id == id, ct) : await db.LabCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)) ?? throw NotFound();
        throw new ExternalCaseException("kind_invalid", "The kind is Referral or Lab.", 400);
    }

    private static string KindOf(IExternalCase c) => c is LabCase ? ExternalCaseKinds.Lab : ExternalCaseKinds.Referral;

    private async Task RequirePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new ExternalCaseException("patient_not_found", "That patient was not found.", 404);
    }

    private async Task RequirePartyAsync(Guid partyId, string partyKind, CancellationToken ct)
    {
        var party = await db.ExternalParties.AsNoTracking().Where(p => p.Id == partyId).Select(p => new { p.Kind, p.IsActive }).FirstOrDefaultAsync(ct)
            ?? throw new ExternalCaseException("party_not_found", "That outside party was not found. Add it to the list first.", 400);
        if (party.Kind != partyKind) throw new ExternalCaseException("party_kind_invalid", partyKind == ExternalPartyKinds.LabVendor ? "Choose a laboratory." : "Choose a referral provider.", 400);
        if (!party.IsActive) throw new ExternalCaseException("party_inactive", "That outside party is no longer active. Choose another or ask for it to be made active.", 409);
    }

    private async Task ChangePartyAsync(IExternalCase c, Guid partyId, string partyKind, CancellationToken ct)
    {
        if (c.Status != ExternalCaseRules.Draft) throw new ExternalCaseException("party_locked", "The case has been sent, so it keeps its outside party.", 409);
        await RequirePartyAsync(partyId, partyKind, ct);
        c.PartyId = partyId;
    }

    private async Task RequireEncounterAsync(Guid encounterId, Guid patientId, CancellationToken ct)
    {
        if (!await db.Encounters.AsNoTracking().AnyAsync(e => e.Id == encounterId && e.PatientId == patientId, ct))
            throw new ExternalCaseException("encounter_invalid", "Choose a visit of this patient.", 400);
    }

    /// <summary>The plan item must be this patient's; its tooth is returned so a lab case for a planned tooth defaults to it.</summary>
    private async Task<string?> RequirePlanItemAsync(Guid itemId, Guid patientId, CancellationToken ct)
    {
        var item = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.Id == itemId && i.PatientId == patientId).Select(i => new { i.ToothKey }).FirstOrDefaultAsync(ct)
            ?? throw new ExternalCaseException("plan_item_invalid", "Choose a treatment of this patient.", 400);
        return item.ToothKey;
    }

    private void AddEvent(string kind, Guid caseId, string eventKind, Guid actor, DateTimeOffset at, string? fromStatus = null, string? toStatus = null, Guid? documentId = null, string? note = null) =>
        db.ExternalCaseEvents.Add(new ExternalCaseEvent
        {
            Id = Guid.NewGuid(), ReferralCaseId = kind == ExternalCaseKinds.Referral ? caseId : null, LabCaseId = kind == ExternalCaseKinds.Lab ? caseId : null, Kind = eventKind,
            FromStatus = fromStatus, ToStatus = toStatus, DocumentId = documentId, ActorUserId = actor, OccurredAtUtc = at, Note = note,
        });

    /// <summary>Saves a new case; a concurrent request with the same key that won the race is answered with the case it made.</summary>
    private async Task<ExternalCaseResult> SaveNewAsync(IExternalCase c, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await LoadAsync(KindOf(c), c.Id, false, ct);
            return await ReplayAsync(winner, SameRequest(winner, c), ct);
        }
        return new ExternalCaseResult(await ViewAsync(c, ct), false);
    }

    private static bool SameRequest(IExternalCase a, IExternalCase b) =>
        a.PartyId == b.PartyId && a.DueOn == b.DueOn && a.Notes == b.Notes && (a is ReferralCase ra ? ra.Reason == ((ReferralCase)b).Reason && ra.EncounterId == ((ReferralCase)b).EncounterId : ((LabCase)a).Title == ((LabCase)b).Title);

    private async Task<ExternalCaseResult> ReplayAsync(IExternalCase existing, bool same, CancellationToken ct) =>
        same ? new ExternalCaseResult(await ViewAsync(existing, ct), true)
             : throw new ExternalCaseException("key_reused", "That Idempotency-Key was already used for a different request.", 409);

    private async Task SaveAsync(Guid caseId, CancellationToken ct)
    {
        try { await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, "ExternalCase", caseId, ct); }
        catch (ConcurrencyConflictException) { throw new ExternalCaseException("case_changed", "Someone else changed this case. Reload it and try again.", 409); }
        catch (DbUpdateException ex) when (IsDuplicate(ex)) { throw new ExternalCaseException("case_changed", "Someone else changed this case. Reload it and try again.", 409); }
    }

    private static bool IsDuplicate(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2627 or 2601 };

    internal async Task<ExternalCaseView> ViewAsync(IExternalCase c, CancellationToken ct)
    {
        var party = await db.ExternalParties.AsNoTracking().Where(p => p.Id == c.PartyId).Select(p => new { p.Name, p.IsActive }).SingleAsync(ct);
        var kind = KindOf(c);
        var next = ExternalCaseRules.NextStatuses(kind, c.Status);
        var overdue = ExternalCaseRules.IsOverdue(c.Status, c.DueOn, Today);
        return c switch
        {
            ReferralCase r => new ExternalCaseView(kind, r.Id, r.PatientId, r.PartyId, party.Name, party.IsActive, r.Reason, r.Status, next, r.SentOn, r.ReceivedOn, r.DueOn, overdue, r.EncounterId, null, null, null, null, r.Notes,
                r.ClosureNote, r.ClosedAtUtc, Convert.ToBase64String(r.RowVersion), r.CreatedAtUtc, r.UpdatedAtUtc),
            LabCase l => new ExternalCaseView(kind, l.Id, l.PatientId, l.PartyId, party.Name, party.IsActive, l.Title, l.Status, next, l.SentOn, l.ReceivedOn, l.DueOn, overdue, null, l.ToothKey, l.TreatmentPlanItemId, l.Material, l.Shade, l.Notes,
                l.ClosureNote, l.ClosedAtUtc, Convert.ToBase64String(l.RowVersion), l.CreatedAtUtc, l.UpdatedAtUtc),
            _ => throw new InvalidOperationException("Unknown case type."),
        };
    }
}
