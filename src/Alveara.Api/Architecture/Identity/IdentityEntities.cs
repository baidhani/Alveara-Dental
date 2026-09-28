namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// Three distinct entities per ALV-N002's identity-separation requirement. A person's login
/// account, their staff employment profile, and their clinical provider profile are related but
/// distinct concepts, linked explicitly rather than conflated. No authentication logic lives here
/// yet — that is STORY-001/ALV-001-C01's job; this story only establishes the schema boundary so
/// later scheduling/clinical attribution never has to depend on the auth table's identity.
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }
    public required string Username { get; set; }

    // Populated by STORY-001/ALV-001-C01. Nullable here because this story does not implement
    // authentication; it only reserves the column so the auth story doesn't have to migrate a
    // differently-shaped table later.
    public string? PasswordHash { get; set; }

    public int SessionTimeoutMinutes { get; set; } = 30;
    public DateTimeOffset CreatedAtUtc { get; set; }
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
