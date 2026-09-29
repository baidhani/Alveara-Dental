namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01: password reset without ever storing a recoverable password. An admin issues a
/// one-time, short-lived, hashed token out-of-band (this story has no email/SMS delivery, and
/// the release explicitly requires offline capability); the holder uses it once to set a new
/// password. The raw token is returned to the admin exactly once, at issuance, and never
/// persisted or logged.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}
