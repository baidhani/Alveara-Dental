using System.Runtime.CompilerServices;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 5b (brief 17.4): the product no longer creates a staff profile on its own or links an existing account (new staff come from
/// <see cref="StaffMemberService"/>, profile and login together). Tests that need a profile as a FIXTURE - a provider's staff record, or a profile linked to an
/// account the test already made - write it directly here, with the same row and audit the old create wrote. Not a product path.
/// </summary>
public static class TestStaff
{
    private static readonly ConditionalWeakTable<StaffProviderService, AlveraDbContext> Contexts = new();

    /// <summary>A <see cref="StaffProviderService"/> whose context the fixture below can find.</summary>
    public static StaffProviderService Service(AlveraDbContext db, IPracticeClock clock)
    {
        var service = new StaffProviderService(db, clock);
        Contexts.AddOrUpdate(service, db);
        return service;
    }

    /// <summary>Writes an active staff profile (optionally linked to an existing account) and its "created" audit entry.</summary>
    public static async Task<StaffProfile> CreateStaffAsync(this StaffProviderService service, string displayName, string? jobTitle, Guid? userAccountId, Guid actor, CancellationToken ct)
    {
        if (!Contexts.TryGetValue(service, out var db))
            throw new InvalidOperationException("Create the StaffProviderService with TestStaff.Service so the fixture knows its database.");
        var staff = new StaffProfile
        {
            Id = Guid.NewGuid(), DisplayName = displayName.Trim(), JobTitle = string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle.Trim(), UserAccountId = userAccountId,
            IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.StaffProfiles.Add(staff);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(StaffProfile), staff.Id, actor,
            userAccountId is null ? "Test fixture: staff profile created." : "Test fixture: staff profile created and linked to a login account.");
        await ConfigurationWrite.SaveAsync(db, nameof(StaffProfile), staff.Id, ct);
        return staff;
    }
}
