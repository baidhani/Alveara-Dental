# ALV-001-C01 R04 — Next Story Impact

Supersedes R03's `NEXT_STORY_IMPACT.md` with one addition; everything else there still applies unchanged.

## New for future stories to build on

- **A conditional counter/state reset must check its own affected-row count, not just its `WHERE` clause, whenever the same method proceeds to do further work on the assumption that the reset succeeded.** `BeginMfaEnrollmentAsync`'s correct-step-up path now follows the same discipline `LoginAsync` already established: `rowsAffected == 0` on a conditional `ExecuteUpdateAsync` means a concurrent request won a race, and the caller must stop and reload authoritative state rather than continue as if it had won. Any future reauthentication/step-up-style check should follow this same pattern, not just the initial lockout gate.
- **A conditional "consume once" update (challenge, token, one-time code) must run inside the same transaction as whatever audit/state change it gates**, not as a separately-committing call beforehand. `TryConsumeChallengeAsync`'s pattern (durable row, single conditional `ExecuteUpdateAsync`) from R03 remains the right shape for "must be usable exactly once" — R04's correction is transactional placement, not the consumption mechanism itself: open the transaction, then consume, then write the audit inside it.
- **A rejected replay/expired/revoked attempt on a security-sensitive one-time artifact should be audited, not silently dropped**, using an event type that never logs the artifact's own secret material (token, code, key) and is worded identically across the different rejection causes it doesn't distinguish publicly.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before `ALV-N009` begins.
