namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// Three distinct entities per ALV-N002's identity-separation requirement. A person's login
/// account, their staff employment profile, and their clinical provider profile are related but
/// distinct concepts, linked explicitly rather than conflated.
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }
    public required string Username { get; set; }

    // STORY-001: PBKDF2 hash, never the plaintext password. Nullable only because ALV-N002
    // seeded this column before this story existed; every account this story creates has one.
    public string? PasswordHash { get; set; }

    public Role Role { get; set; }

    // STORY-001 account-lockout state. A failed login increments the counter; reaching the
    // configured threshold sets LockedOutUntilUtc, after which further attempts — even with the
    // correct password — are rejected until that time passes. A successful login resets both.
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedOutUntilUtc { get; set; }

    public int SessionTimeoutMinutes { get; set; } = 30;
    public DateTimeOffset CreatedAtUtc { get; set; }

    // ALV-001-C01 additions below.

    /// <summary>A disabled account can never authenticate, regardless of password/MFA/lockout state.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// Regenerated on password change, MFA change, disable, or role change. Embedded as a claim
    /// in the session cookie and checked on every request (see CookieAuthenticationOptions'
    /// OnValidatePrincipal) — a stamp mismatch means the session was issued before a
    /// security-relevant change and is rejected, giving real server-side "sign out everywhere"
    /// without needing a server-side session store.
    /// </summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    public bool MfaEnabled { get; set; }

    /// <summary>
    /// The TOTP secret, protected at rest via ASP.NET Core's Data Protection API (never stored or
    /// logged in plaintext). Null until MFA enrollment completes.
    /// </summary>
    public string? MfaSecretProtected { get; set; }

    /// <summary>
    /// R02 (review finding ALV-001-C01-R01-01): a newly generated secret sits here, inert, from
    /// the moment enrollment/replacement begins until a correct code confirms it. The active
    /// <see cref="MfaSecretProtected"/>/<see cref="MfaEnabled"/> state is never touched until that
    /// confirmation succeeds, so starting (and abandoning) re-enrollment can never disable an
    /// already-established factor.
    /// </summary>
    public string? PendingMfaSecretProtected { get; set; }
}

/// <summary>An employment/staff record — may or may not have a login (e.g. a biller might not).</summary>
public class StaffProfile
{
    public Guid Id { get; set; }
    public Guid? UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// A clinical provider profile — the entity scheduling/clinical records actually attribute care
/// to. Linked to a staff profile (a provider is also staff) but modeled separately because not
/// every staff member is a clinical provider, and future stories (multi-location, credentialing)
/// attach data here that has no meaning for non-clinical staff.
/// </summary>
public class ProviderProfile
{
    public Guid Id { get; set; }
    public Guid StaffProfileId { get; set; }
    public StaffProfile? StaffProfile { get; set; }
    public required string Specialty { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
