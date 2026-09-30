namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// R03 (review finding ALV-001-C01-R02-01): the durable, server-side, one-time-consumable state
/// backing an in-flight MFA challenge. Previously the challenge was a purely self-contained,
/// Data-Protection-sealed token with no server-side record - nothing prevented the exact same
/// successful challenge-token/code submission from being replayed to mint a second session. This
/// row is what makes "consumed" a real, atomically-checked fact rather than an assumption. The
/// protected token still carries <c>ChallengeId</c> plus a copy of the SecurityStamp/expiry for a
/// fast, DB-free rejection of a tampered or already-expired-by-clock token, but this row is
/// authoritative for one-time use.
/// </summary>
public class MfaChallenge
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public Guid SecurityStamp { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
