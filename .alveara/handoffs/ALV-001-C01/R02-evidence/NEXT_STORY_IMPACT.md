# ALV-001-C01 R02 — Next Story Impact

Supersedes R01's `NEXT_STORY_IMPACT.md` with two additions; everything else there still applies unchanged (real session identity via `AuthContext`, the permission matrix as the authorization vocabulary, `RequirePermissionAttribute` as the server-side enforcement primitive, the fail-closed-to-signed-out convention, the currently-unconditional nav registry pending `ALV-N009`).

## New for `ALV-N009` and later stories to build on

- **The MFA pending-enrollment pattern** (`PendingMfaSecretProtected` / `MfaRecoveryCode.IsPending`, atomic `ExecuteUpdateAsync`-gated promotion on confirm) is the reusable shape for "start a sensitive change, only commit it once a second factor/confirmation succeeds." Any future story adding another step-up-confirmed change should follow the same pattern rather than mutating live state before confirmation.
- **MFA challenges (and, by the same mechanism, any future short-lived self-contained token issued via Data Protection) must be bound to `SecurityStamp`** at issue time and re-checked at completion time. This is now the established convention for "a token that should die the instant a stamp-rotating security event happens," and future stories issuing their own short-lived tokens should follow it.
- **`authApi.ts`'s `request()` now correctly handles empty-body success responses.** Any future frontend service function added to this file (or a similar one) calling an endpoint that returns `Ok()` with no body doesn't need special-casing anymore — this is handled generically.
- **Otp.NET is now a project dependency.** Any future story needing HOTP/TOTP (e.g. a hardware-token-adjacent factor) should reuse this library rather than adding a second one or hand-rolling again.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before `ALV-N009` begins.
