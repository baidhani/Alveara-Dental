# ALV-001-C01 R03 — Changed Files

`git diff --stat 150643c40f32a1e1c671a948cb84f8c0054115ca..3203c05f843f9c5b98968dba5bc9ee3da820cab7`: 11 files changed, 931 insertions(+), 34 deletions(-).

## Backend — production code

| File | Change |
|---|---|
| `Architecture/Identity/AccountService.cs` | Durable `MfaChallenge` consumption (`IssueMfaChallenge`, `TryConsumeChallengeAsync`), shared lockout helpers (`RecordFailedAuthenticationAttemptAsync`, `RearmAndCheckLockoutAsync`), `LoginAsync` refactored to use them, step-up check now throttled/lockout-coupled |
| `Architecture/Identity/AuditLogEntry.cs` | New `MfaStepUpFailed` event type |
| `Architecture/Identity/MfaChallenge.cs` | New entity |
| `Controllers/AuthController.cs` | `mfa/enroll` gained `[EnableRateLimiting("AuthAttempts")]` and an `AccountLockedOutException` catch |
| `Data/AlveraDbContext.cs` | `DbSet<MfaChallenge>` + index |
| `Migrations/20260930124949_AddMfaChallengeConsumption.{cs,Designer.cs}` | New migration |
| `Migrations/AlveraDbContextModelSnapshot.cs` | Updated to match |

## Backend — tests

| File | Change |
|---|---|
| `AccountServiceMfaTests.cs` | 9 new tests (replay/concurrency/expiry/revocation, step-up throttling) |
| `AuthRateLimitTests.cs` | 1 new HTTP-level throttling test |

## Frontend

| File | Change |
|---|---|
| `e2e/auth-real-backend.spec.ts` | 1 new test reproducing the reviewer's live replay finding |

No frontend production code (`src/alveara-client/src/`) was changed this attempt.
