using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Configuration;

public record AvailabilityWindow(DayOfWeek DayOfWeek, string StartLocal, string EndLocal);
public record ProviderAvailabilitySchedule(int Revision, IReadOnlyList<ProviderWeeklyAvailability> Windows);
public record LinkableAccount(Guid Id, string Username, string Role, bool IsDisabled, Guid? LinkedStaffId);

/// <summary>
/// ALV-N003: staff profiles, clinical provider profiles, the explicit optional account linkage,
/// and provider availability/blocked time. A login account, a staff profile, and a provider
/// profile stay three distinct records; linkage is an optional foreign key, validated when set.
/// Nothing here deletes: inactivation keeps every historical reference resolvable.
/// </summary>
public class StaffProviderService(AlveraDbContext db, IPracticeClock clock)
{
    // ---------- Staff ----------

    public Task<List<StaffProfile>> ListStaffAsync(bool includeInactive, CancellationToken ct) =>
        db.StaffProfiles.AsNoTracking().Where(s => includeInactive || s.IsActive).OrderBy(s => s.DisplayName).ToListAsync(ct);

    public async Task<StaffProfile> GetStaffAsync(Guid id, CancellationToken ct) =>
        await db.StaffProfiles.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw PracticeConfigurationService.NotFound("Staff profile");

    public async Task<StaffProfile> CreateStaffAsync(string? displayName, string? jobTitle, Guid? userAccountId, Guid actor, CancellationToken ct)
    {
        var name = ConfigurationWrite.RequireName(displayName, "Display name");
        var title = ConfigurationWrite.OptionalText(jobTitle, "Job title", 80);
        if (await db.StaffProfiles.AnyAsync(s => s.DisplayName == name, ct))
            throw new ConfigurationException("staff_name_taken", "A staff profile with that display name already exists.", 409);
        if (userAccountId is not null) await ValidateAccountLinkAsync(userAccountId.Value, null, ct);

        var staff = new StaffProfile
        {
            Id = Guid.NewGuid(), DisplayName = name, JobTitle = title, UserAccountId = userAccountId, IsActive = true, CreatedAtUtc = clock.UtcNow,
        };
        db.StaffProfiles.Add(staff);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(StaffProfile), staff.Id, actor,
            userAccountId is null ? "Staff profile created." : "Staff profile created and linked to a login account.");
        await ConfigurationWrite.SaveAsync(db, nameof(StaffProfile), staff.Id, ct);
        return staff;
    }

    /// <summary>Updates name/title and the account link together; only a changed link is audited as one.</summary>
    public async Task<StaffProfile> UpdateStaffAsync(Guid id, string? displayName, string? jobTitle, Guid? userAccountId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var name = ConfigurationWrite.RequireName(displayName, "Display name");
        var title = ConfigurationWrite.OptionalText(jobTitle, "Job title", 80);
        var staff = await db.StaffProfiles.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw PracticeConfigurationService.NotFound("Staff profile");
        if (await db.StaffProfiles.AnyAsync(s => s.Id != id && s.DisplayName == name, ct))
            throw new ConfigurationException("staff_name_taken", "A staff profile with that display name already exists.", 409);

        var linkChanged = staff.UserAccountId != userAccountId;
        // Only a NEW link is validated: an existing link to an account that was later disabled is
        // kept as-is (the staff member's history still resolves), never silently severed.
        if (linkChanged && userAccountId is not null) await ValidateAccountLinkAsync(userAccountId.Value, id, ct);

        ConfigurationWrite.ApplyExpectedVersion(db, staff, rowVersion);
        staff.DisplayName = name;
        staff.JobTitle = title;
        staff.UserAccountId = userAccountId;
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Updated, nameof(StaffProfile), id, actor, "Staff profile updated.");
        if (linkChanged)
        {
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.StaffAccountLinkChanged, nameof(StaffProfile), id, actor,
                userAccountId is null ? "Login account link removed." : "Login account link set.");
        }
        await ConfigurationWrite.SaveAsync(db, nameof(StaffProfile), id, ct);
        return staff;
    }

    public async Task<StaffProfile> SetStaffActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var staff = await db.StaffProfiles.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw PracticeConfigurationService.NotFound("Staff profile");
        if (staff.IsActive == active) return staff;
        if (!active && await db.ProviderProfiles.AnyAsync(p => p.StaffProfileId == id && p.IsActive, ct))
            throw new ConfigurationException("staff_has_active_provider", "Inactivate this staff member's provider profile first.", 409);
        ConfigurationWrite.ApplyExpectedVersion(db, staff, rowVersion);
        staff.IsActive = active;
        ConfigurationWrite.Audit(db, active ? ConfigurationAuditEvents.Reactivated : ConfigurationAuditEvents.Inactivated,
            nameof(StaffProfile), id, actor, active ? "Staff profile reactivated." : "Staff profile inactivated.");
        await ConfigurationWrite.SaveAsync(db, nameof(StaffProfile), id, ct);
        return staff;
    }

    /// <summary>
    /// Accounts the link picker may show: every ENABLED account, plus any account that already has
    /// a staff link (even if since disabled, so an existing link still displays rather than vanishing
    /// from the picker). <see cref="LinkableAccount.LinkedStaffId"/> says who holds it, so the UI
    /// offers an account only when it is free or belongs to the profile being edited; the server
    /// re-validates every link regardless.
    /// </summary>
    public async Task<List<LinkableAccount>> ListLinkableAccountsAsync(CancellationToken ct)
    {
        var links = await db.StaffProfiles.AsNoTracking().Where(s => s.UserAccountId != null)
            .Select(s => new { StaffId = s.Id, AccountId = s.UserAccountId!.Value }).ToListAsync(ct);
        var staffByAccount = links.ToDictionary(l => l.AccountId, l => l.StaffId);
        var linkedIds = staffByAccount.Keys.ToList();
        var accounts = await db.UserAccounts.AsNoTracking()
            .Where(a => !a.IsDisabled || linkedIds.Contains(a.Id))
            .OrderBy(a => a.Username)
            .Select(a => new { a.Id, a.Username, a.Role, a.IsDisabled }).ToListAsync(ct);
        return accounts.Select(a => new LinkableAccount(
            a.Id, a.Username, a.Role.ToString(), a.IsDisabled, staffByAccount.TryGetValue(a.Id, out var staffId) ? staffId : null)).ToList();
    }

    /// <summary>The link must point at an existing, enabled account that no other staff profile already uses.</summary>
    private async Task ValidateAccountLinkAsync(Guid userAccountId, Guid? staffIdBeingEdited, CancellationToken ct)
    {
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == userAccountId, ct)
            ?? throw new ConfigurationException("account_not_found", "The selected login account does not exist.");
        if (account.IsDisabled)
            throw new ConfigurationException("account_inactive", "The selected login account is disabled and cannot be linked.");
        if (await db.StaffProfiles.AnyAsync(s => s.UserAccountId == userAccountId && s.Id != staffIdBeingEdited, ct))
            throw new ConfigurationException("account_already_linked", "That login account is already linked to another staff profile.", 409);
    }

    // ---------- Providers ----------

    public Task<List<ProviderProfile>> ListProvidersAsync(bool includeInactive, CancellationToken ct) =>
        db.ProviderProfiles.AsNoTracking().Include(p => p.StaffProfile)
            .Where(p => includeInactive || p.IsActive).OrderBy(p => p.StaffProfile!.DisplayName).ToListAsync(ct);

    /// <summary>Resolves a provider whether or not it is still active - historical references must keep working.</summary>
    public async Task<ProviderProfile> GetProviderAsync(Guid id, CancellationToken ct) =>
        await db.ProviderProfiles.AsNoTracking().Include(p => p.StaffProfile).SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw PracticeConfigurationService.NotFound("Provider profile");

    public async Task<ProviderProfile> CreateProviderAsync(Guid staffProfileId, string? specialty, Guid actor, CancellationToken ct)
    {
        var cleanSpecialty = ConfigurationWrite.RequireName(specialty, "Specialty", 80);
        var staff = await db.StaffProfiles.AsNoTracking().SingleOrDefaultAsync(s => s.Id == staffProfileId, ct)
            ?? throw new ConfigurationException("staff_not_found", "The selected staff profile does not exist.");
        if (!staff.IsActive)
            throw new ConfigurationException("staff_inactive", "The selected staff profile is inactive.");
        if (await db.ProviderProfiles.AnyAsync(p => p.StaffProfileId == staffProfileId, ct))
            throw new ConfigurationException("provider_exists", "That staff member already has a provider profile.", 409);

        var provider = new ProviderProfile { Id = Guid.NewGuid(), StaffProfileId = staffProfileId, Specialty = cleanSpecialty, IsActive = true, CreatedAtUtc = clock.UtcNow };
        db.ProviderProfiles.Add(provider);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(ProviderProfile), provider.Id, actor, "Provider profile created.");
        await ConfigurationWrite.SaveAsync(db, nameof(ProviderProfile), provider.Id, ct);
        return await GetProviderAsync(provider.Id, ct);
    }

    public async Task<ProviderProfile> UpdateProviderAsync(Guid id, string? specialty, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var cleanSpecialty = ConfigurationWrite.RequireName(specialty, "Specialty", 80);
        var provider = await db.ProviderProfiles.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw PracticeConfigurationService.NotFound("Provider profile");
        ConfigurationWrite.ApplyExpectedVersion(db, provider, rowVersion);
        provider.Specialty = cleanSpecialty;
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Updated, nameof(ProviderProfile), id, actor, "Provider profile updated.");
        await ConfigurationWrite.SaveAsync(db, nameof(ProviderProfile), id, ct);
        return await GetProviderAsync(id, ct);
    }

    public async Task<ProviderProfile> SetProviderActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var provider = await db.ProviderProfiles.Include(p => p.StaffProfile).SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw PracticeConfigurationService.NotFound("Provider profile");
        if (provider.IsActive == active) return provider;
        if (active && provider.StaffProfile is { IsActive: false })
            throw new ConfigurationException("staff_inactive", "This provider's staff profile is inactive. Reactivate it first.", 409);
        ConfigurationWrite.ApplyExpectedVersion(db, provider, rowVersion);
        provider.IsActive = active;
        ConfigurationWrite.Audit(db, active ? ConfigurationAuditEvents.Reactivated : ConfigurationAuditEvents.Inactivated,
            nameof(ProviderProfile), id, actor, active ? "Provider profile reactivated." : "Provider profile inactivated.");
        await ConfigurationWrite.SaveAsync(db, nameof(ProviderProfile), id, ct);
        return provider;
    }

    // ---------- Weekly availability (practice-local wall-clock) ----------

    public async Task<List<ProviderWeeklyAvailability>> GetAvailabilityAsync(Guid providerId, CancellationToken ct) =>
        (await GetAvailabilityScheduleAsync(providerId, ct)).Windows.ToList();

    /// <summary>The provider's weekly schedule together with the revision a later replacement must present.</summary>
    public async Task<ProviderAvailabilitySchedule> GetAvailabilityScheduleAsync(Guid providerId, CancellationToken ct)
    {
        var provider = await GetProviderAsync(providerId, ct);
        var windows = await db.ProviderWeeklyAvailabilities.AsNoTracking().Where(a => a.ProviderProfileId == providerId)
            .OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartLocal).ToListAsync(ct);
        return new ProviderAvailabilitySchedule(provider.AvailabilityRevision, windows);
    }

    /// <summary>
    /// Atomically replaces the provider's whole weekly schedule after validating every window.
    /// The schedule is a versioned aggregate: the caller must present the revision it read
    /// (<paramref name="expectedRevision"/>), and the revision bump, row replacement and audit entry
    /// commit in one transaction. A stale revision - including the race where two callers both read
    /// the same (even empty) schedule - is rejected as a <see cref="Concurrency.ConcurrencyConflictException"/>
    /// rather than merged or silently overwritten.
    /// </summary>
    public async Task<ProviderAvailabilitySchedule> ReplaceAvailabilityAsync(
        Guid providerId, IReadOnlyList<AvailabilityWindow> windows, int? expectedRevision, Guid actor, CancellationToken ct)
    {
        if (expectedRevision is null)
            throw new ConfigurationException("revision_required",
                "The schedule revision you are editing is required so a concurrent change is never overwritten. Reload and try again.");
        var provider = await db.ProviderProfiles.SingleOrDefaultAsync(p => p.Id == providerId, ct) ?? throw PracticeConfigurationService.NotFound("Provider profile");
        if (!provider.IsActive)
            throw new ConfigurationException("provider_inactive", "Availability cannot be changed for an inactive provider.", 409);

        var parsed = windows.Select(Parse).ToList();
        foreach (var day in parsed.GroupBy(w => w.DayOfWeek))
        {
            var ordered = day.OrderBy(w => w.StartLocal).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartLocal < ordered[i - 1].EndLocal)
                    throw new ConfigurationException("availability_overlap", $"Availability windows on {day.Key} overlap.");
            }
        }

        // Compare against the revision the CALLER read, and bump it: this UPDATE is what makes a
        // concurrent replacement lose (zero rows matched -> conflict) instead of both inserting.
        db.Entry(provider).Property(p => p.AvailabilityRevision).OriginalValue = expectedRevision.Value;
        provider.AvailabilityRevision = expectedRevision.Value + 1;

        var existing = await db.ProviderWeeklyAvailabilities.Where(a => a.ProviderProfileId == providerId).ToListAsync(ct);
        db.ProviderWeeklyAvailabilities.RemoveRange(existing);
        db.ProviderWeeklyAvailabilities.AddRange(parsed.Select(w => new ProviderWeeklyAvailability
        {
            Id = Guid.NewGuid(), ProviderProfileId = providerId, DayOfWeek = w.DayOfWeek, StartLocal = w.StartLocal, EndLocal = w.EndLocal,
        }));
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.ProviderAvailabilityReplaced, nameof(ProviderProfile), providerId, actor,
            $"Weekly availability replaced with {parsed.Count} window(s).");
        await ConfigurationWrite.SaveAsync(db, "ProviderAvailability", providerId, ct);
        return await GetAvailabilityScheduleAsync(providerId, ct);
    }

    private static (DayOfWeek DayOfWeek, TimeOnly StartLocal, TimeOnly EndLocal) Parse(AvailabilityWindow w)
    {
        if (!Enum.IsDefined(w.DayOfWeek))
            throw new ConfigurationException("invalid_day", "Day of week is not valid.");
        if (!TimeOnly.TryParseExact(w.StartLocal, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(w.EndLocal, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            throw new ConfigurationException("invalid_time", "Times must be in HH:mm format.");
        }
        if (end <= start)
            throw new ConfigurationException("invalid_range", "An availability window must end after it starts.");
        return (w.DayOfWeek, start, end);
    }

    // ---------- Blocked time ----------

    public async Task<List<ProviderBlockedTime>> ListBlockedTimeAsync(Guid providerId, CancellationToken ct)
    {
        await GetProviderAsync(providerId, ct);
        return await db.ProviderBlockedTimes.AsNoTracking().Where(b => b.ProviderProfileId == providerId).OrderBy(b => b.StartUtc).ToListAsync(ct);
    }

    /// <summary>
    /// Takes PRACTICE-LOCAL wall-clock start/end and converts through IPracticeClock - a time that
    /// does not exist (DST gap) or occurs twice (DST overlap) is rejected, never guessed.
    /// </summary>
    public async Task<ProviderBlockedTime> AddBlockedTimeAsync(Guid providerId, DateTime startLocal, DateTime endLocal, string? reason, Guid actor, CancellationToken ct)
    {
        var provider = await db.ProviderProfiles.SingleOrDefaultAsync(p => p.Id == providerId, ct) ?? throw PracticeConfigurationService.NotFound("Provider profile");
        if (!provider.IsActive)
            throw new ConfigurationException("provider_inactive", "Blocked time cannot be added for an inactive provider.", 409);

        DateTimeOffset startUtc, endUtc;
        try
        {
            startUtc = clock.FromPracticeLocal(startLocal);
            endUtc = clock.FromPracticeLocal(endLocal);
        }
        catch (LocalTimeConversionException ex)
        {
            throw new ConfigurationException("invalid_local_time", ex.Message);
        }
        if (endUtc <= startUtc)
            throw new ConfigurationException("invalid_range", "Blocked time must end after it starts.");

        var block = new ProviderBlockedTime
        {
            Id = Guid.NewGuid(), ProviderProfileId = providerId, StartUtc = startUtc, EndUtc = endUtc,
            Reason = ConfigurationWrite.OptionalText(reason, "Reason", 200), CreatedAtUtc = clock.UtcNow,
        };
        db.ProviderBlockedTimes.Add(block);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.ProviderBlockedTimeAdded, nameof(ProviderProfile), providerId, actor, "Blocked time added.");
        await ConfigurationWrite.SaveAsync(db, nameof(ProviderProfile), providerId, ct);
        return block;
    }

    public async Task RemoveBlockedTimeAsync(Guid providerId, Guid blockId, Guid actor, CancellationToken ct)
    {
        var block = await db.ProviderBlockedTimes.SingleOrDefaultAsync(b => b.Id == blockId && b.ProviderProfileId == providerId, ct)
            ?? throw PracticeConfigurationService.NotFound("Blocked time");
        db.ProviderBlockedTimes.Remove(block);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.ProviderBlockedTimeRemoved, nameof(ProviderProfile), providerId, actor, "Blocked time removed.");
        await ConfigurationWrite.SaveAsync(db, nameof(ProviderProfile), providerId, ct);
    }
}
