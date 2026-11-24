using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.TestSupport;

/// <summary>
/// The fixture request. <c>UserAccountId</c> links the new profile to a login the test already made. With no account, <c>LegacyUnlinked</c> must be
/// <c>true</c>: an unlinked profile is only ever a deliberate legacy/remediation fixture (a profile created before ALV-N030), never a stand-in for "Add staff member".
/// </summary>
public record CreateTestStaffProfileRequest(string? DisplayName, string? JobTitle = null, Guid? UserAccountId = null, bool LegacyUnlinked = false);

/// <summary>
/// ALV-N030 (brief 17, owner-approved R07 implementation extension, 2026-11-24): <c>POST /api/test-support/staff-profiles</c> replaces, for browser and chain
/// FIXTURES only, the removed profile-only create that linked an existing login. It exists only where <see cref="TestAccountsController"/> exists (ChainHost,
/// <c>Test</c> environment, <c>TestSupport:Enabled</c>, loopback; <see cref="TestSupportRegistration"/>), and the product never maps it. It is not a product path:
/// real staff come from "Add staff member". Profile and link are written in one save, after checking that the account exists and has no profile yet.
/// </summary>
[ApiController]
[Route("api/test-support/staff-profiles")]
internal sealed class TestStaffProfilesController(AlveraDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTestStaffProfileRequest? request, CancellationToken ct)
    {
        var name = (request?.DisplayName ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { error = "display_name_required" });
        if (request!.UserAccountId is null && !request.LegacyUnlinked) return BadRequest(new { error = "legacy_unlinked_required" });
        if (request.UserAccountId is { } accountId)
        {
            if (!await db.UserAccounts.AnyAsync(u => u.Id == accountId, ct)) return NotFound(new { error = "account_not_found" });
            if (await db.StaffProfiles.AnyAsync(s => s.UserAccountId == accountId, ct)) return Conflict(new { error = "account_already_linked" });
        }
        if (await db.StaffProfiles.AnyAsync(s => s.DisplayName == name, ct)) return Conflict(new { error = "staff_name_taken" });

        var staff = new StaffProfile
        {
            Id = Guid.NewGuid(), DisplayName = name, JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim(),
            UserAccountId = request.UserAccountId, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.StaffProfiles.Add(staff);
        try
        {
            await db.SaveChangesAsync(ct);   // one save: the profile and its link together (the filtered unique index on UserAccountId backs the check above)
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            return Conflict(new { error = "staff_profile_conflict" });
        }
        return StatusCode(StatusCodes.Status201Created, new
        {
            staff.Id, staff.DisplayName, staff.JobTitle, staff.UserAccountId, staff.IsActive, rowVersion = Convert.ToBase64String(staff.RowVersion),
        });
    }
}
