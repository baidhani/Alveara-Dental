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

    /// <summary>R02: true while this code belongs to a not-yet-confirmed enrollment/replacement.
    /// Pending codes are usable for nothing until <c>ConfirmMfaEnrollmentAsync</c> promotes them
    /// (clears this flag) and deletes the previously-active set in the same transaction — so
    /// starting (and abandoning) re-enrollment never invalidates the recovery codes a user
    /// already has in hand.</summary>
    public bool IsPending { get; set; }
}
