using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.ExternalCases;

/// <summary>
/// ALV-009-C02: the list of outside parties (referral providers and lab vendors) the practice sends work to. A party is made once per kind and name (a repeat of the same request is answered with the party already made;
/// the same name with different contact details is <c>party_exists</c>, 409), edited with the version the person saw (a stale one is <c>party_changed</c>, 409), and made inactive rather than deleted so the cases
/// already sent to it keep it. Every write and its audit entry are one save. Who may call this is the controller's business.
/// </summary>
public class ExternalPartyService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<IReadOnlyList<ExternalPartyView>> ListAsync(string? kind, bool includeInactive, CancellationToken ct)
    {
        if (kind is not null && !ExternalPartyKinds.All.Contains(kind)) throw new ExternalCaseException("kind_invalid", "The kind is ReferralProvider or LabVendor.", 400);
        var query = db.ExternalParties.AsNoTracking().Where(p => (kind == null || p.Kind == kind) && (includeInactive || p.IsActive));
        return (await query.OrderBy(p => p.Name).ToListAsync(ct)).Select(View).ToList();
    }

    public async Task<ExternalPartyView> CreateAsync(Guid actor, CreatePartyRequest r, CancellationToken ct)
    {
        var kind = ExternalCaseValidation.Kind(r.Kind, ExternalPartyKinds.All, "kind_invalid", "a referral provider or a lab vendor");
        var name = ExternalCaseValidation.Required(r.Name, ExternalCaseRules.PartyNameMaxLength, "name_invalid", "the name");
        var contact = ExternalCaseValidation.Optional(r.ContactDetails, ExternalCaseRules.ContactMaxLength, "contact_invalid", "contact details");

        var existing = await db.ExternalParties.AsNoTracking().FirstOrDefaultAsync(p => p.Kind == kind && p.Name == name, ct);
        if (existing is not null) return Replay(existing, contact);

        var now = clock.UtcNow;
        var party = new ExternalParty { Id = Guid.NewGuid(), Kind = kind, Name = name, ContactDetails = contact, IsActive = true, CreatedAtUtc = now, CreatedByUserId = actor, UpdatedAtUtc = now };
        db.ExternalParties.Add(party);
        AuditService.Record(db, AuditEventTypes.ExternalPartyCreated, "ExternalParty", party.Id, actor, $"Outside party created ({kind}).");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            // a concurrent request made the same party: answer with it
            db.ChangeTracker.Clear();
            return Replay(await db.ExternalParties.AsNoTracking().FirstAsync(p => p.Kind == kind && p.Name == name, ct), contact);
        }
        return View(party);
    }

    public async Task<ExternalPartyView> UpdateAsync(Guid actor, Guid id, UpdatePartyRequest r, CancellationToken ct)
    {
        var version = ExternalCaseValidation.Version(r.RowVersion);
        var name = ExternalCaseValidation.Required(r.Name, ExternalCaseRules.PartyNameMaxLength, "name_invalid", "the name");
        var contact = ExternalCaseValidation.Optional(r.ContactDetails, ExternalCaseRules.ContactMaxLength, "contact_invalid", "contact details");
        var party = await LoadAsync(id, ct);
        if (name == party.Name && contact == party.ContactDetails) throw new ExternalCaseException("no_change", "Nothing was changed.", 409);
        if (name != party.Name && await db.ExternalParties.AsNoTracking().AnyAsync(p => p.Kind == party.Kind && p.Name == name && p.Id != id, ct))
            throw new ExternalCaseException("party_exists", "Another party of this kind already has that name.", 409);

        db.Entry(party).Property(p => p.RowVersion).OriginalValue = version;
        party.Name = name; party.ContactDetails = contact; party.UpdatedAtUtc = clock.UtcNow;
        AuditService.Record(db, AuditEventTypes.ExternalPartyChanged, "ExternalParty", party.Id, actor, "Outside party changed (name or contact details).");
        await SaveAsync(party.Id, ct);
        return View(party);
    }

    public async Task<ExternalPartyView> SetActiveAsync(Guid actor, Guid id, SetPartyActiveRequest r, CancellationToken ct)
    {
        var active = r.Active ?? throw new ExternalCaseException("active_required", "Say whether the party is active.", 400);
        var party = await LoadAsync(id, ct);
        if (party.IsActive == active) return View(party);                                           // already so: nothing to do, and no version needed
        var version = ExternalCaseValidation.Version(r.RowVersion);
        db.Entry(party).Property(p => p.RowVersion).OriginalValue = version;
        party.IsActive = active; party.UpdatedAtUtc = clock.UtcNow;
        AuditService.Record(db, AuditEventTypes.ExternalPartyChanged, "ExternalParty", party.Id, actor, active ? "Outside party made active." : "Outside party made inactive.");
        await SaveAsync(party.Id, ct);
        return View(party);
    }

    private async Task<ExternalParty> LoadAsync(Guid id, CancellationToken ct) =>
        await db.ExternalParties.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new ExternalCaseException("party_not_found", "That outside party was not found.", 404);

    private static ExternalPartyView Replay(ExternalParty existing, string? contact) =>
        existing.ContactDetails == contact ? View(existing) : throw new ExternalCaseException("party_exists", "A party of this kind with that name already exists.", 409);

    private async Task SaveAsync(Guid id, CancellationToken ct)
    {
        try { await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, "ExternalParty", id, ct); }
        catch (ConcurrencyConflictException) { throw new ExternalCaseException("party_changed", "Someone else changed this party. Reload it and try again.", 409); }
        catch (DbUpdateException ex) when (IsDuplicate(ex)) { throw new ExternalCaseException("party_exists", "Another party of this kind already has that name.", 409); }
    }

    private static bool IsDuplicate(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2627 or 2601 };

    internal static ExternalPartyView View(ExternalParty p) =>
        new(p.Id, p.Kind, p.Name, p.ContactDetails, p.IsActive, Convert.ToBase64String(p.RowVersion), p.CreatedAtUtc, p.UpdatedAtUtc);
}
