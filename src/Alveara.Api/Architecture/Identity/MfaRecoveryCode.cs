namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01: one-time MFA recovery codes, issued in a batch at enrollment. Stored hashed
/// (same PBKDF2 scheme as passwords) — never in plaintext, never logged — and each is usable
/// exactly once (<see cref="UsedAtUtc"/> is set atomically on redemption).
/// </summary>
public class MfaRecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}
