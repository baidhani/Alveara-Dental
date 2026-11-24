using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.TestSupport;

public sealed class TestAccountRejectedException(string code, int statusCode = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record CreatedTestAccount(Guid Id, string Username, string Role, string AccountKind, Guid? StaffProfileId, bool MustChangePassword);

/// <summary>
/// Writes the fixture account (and, when asked, a linked active staff profile named after the username) in one save. No audit row: this is fixture setup, not a
/// product action, and the host that serves it is a test host. A Deployment Administrator must have the Admin role; the database allows only one.
/// </summary>
public sealed class TestAccountService(AlveraDbContext db)
{
    public async Task<CreatedTestAccount> CreateAsync(CreateTestAccountRequest? request, CancellationToken ct)
    {
        var username = (request?.Username ?? "").Trim();
        if (username.Length == 0) throw new TestAccountRejectedException("username_required");
        if (string.IsNullOrEmpty(request!.Password)) throw new TestAccountRejectedException("password_required");
        if (!Enum.TryParse<Role>(request.Role?.Trim(), ignoreCase: true, out var role) || !Enum.IsDefined(role)) throw new TestAccountRejectedException("role_invalid");
        var kind = AccountKind.Staff;
        if (request.Kind is { Length: > 0 } k && !Enum.TryParse(k.Trim(), ignoreCase: true, out kind)) throw new TestAccountRejectedException("kind_invalid");
        if (kind == AccountKind.DeploymentAdministrator && (role != Role.Admin || request.WithStaffProfile)) throw new TestAccountRejectedException("kind_invalid");

        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(request.Password), Role = role, IsDisabled = false,
            AccountKind = kind, MustChangePassword = request.MustChangePassword, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        StaffProfile? staff = null;
        if (request.WithStaffProfile)
        {
            staff = new StaffProfile { Id = Guid.NewGuid(), DisplayName = username, UserAccountId = account.Id, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
            db.StaffProfiles.Add(staff);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            throw new TestAccountRejectedException("account_conflict", 409);
        }
        return new CreatedTestAccount(account.Id, account.Username, role.ToString(), kind.ToString(), staff?.Id, account.MustChangePassword);
    }
}
