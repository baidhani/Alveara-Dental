# ALV-001-C01 R02 — Changed Files

`git diff --stat 01afb667cb18d37643ae48ca3341477b05b04ed..f5757719df03eb2e3a7759e0a89eefca71232739`: 38 files changed (27 in the implementation commit; the remainder are R01's own evidence/review files, already committed before this attempt began, included here only because they fall within the diff range). Implementation-commit-only files below.

## Backend — production code

| File | Change |
|---|---|
| `Alveara.Api.csproj` | Added `Otp.NET` package reference |
| `Architecture/Identity/AccountService.cs` | Pending-enrollment model, SecurityStamp-bound challenges, MFA/recovery audit events, `SetSessionTimeoutAsync`, AsNoTracking fixes |
| `Architecture/Identity/AuditLogEntry.cs` | 8 new event-type constants |
| `Architecture/Identity/IdentityEntities.cs` | `PendingMfaSecretProtected` |
| `Architecture/Identity/MfaRecoveryCode.cs` | `IsPending` |
| `Architecture/Identity/Totp.cs` | Rewritten as a thin `Otp.NET` wrapper |
| `Controllers/AuthController.cs` | `EnrollMfaRequest.CurrentPassword`, new exception handling, `PUT {userId}/session-timeout`, `AdminUserSummary.SessionTimeoutMinutes` |
| `Migrations/20260929193610_AddPendingMfaEnrollment.{cs,Designer.cs}` | New migration |
| `Migrations/AlveraDbContextModelSnapshot.cs` | Updated to match |

## Backend — tests

| File | Change |
|---|---|
| `AccountServiceMfaTests.cs` | 10 new tests (pending-enrollment lifecycle, challenge invalidation, audit) |
| `IdentityTestHelpers.cs` | `CreateAccountService(db, provider)` overload for shared-provider concurrency tests |

## Frontend

| File | Change |
|---|---|
| `App.tsx` | New routes: `/admin/users/:userId`, `/admin/permissions`, `/settings/mfa` |
| `app/moduleRegistry.ts` | Two new nav entries |
| `pages/AdminUserDetailPage.{tsx,test.tsx}` | New |
| `pages/AdminUsersPage.tsx` | "View" link per row |
| `pages/MfaSettingsPage.{tsx,css,test.tsx}` | New |
| `pages/PermissionMatrixPage.{tsx,css,test.tsx}` | New |
| `services/authApi.ts` | New calls (`enrollMfa`, `confirmMfa`, `getPermissionMatrix`, `setSessionTimeout`); fixed empty-body-response parsing bug |
| `e2e/auth-real-backend.spec.ts` | New — real-backend Playwright suite |
| `playwright.auth.config.ts` | New — config for the above |

## Docs

| File | Change |
|---|---|
| `docs/testing/REAL_BACKEND_E2E.md` | New — run procedure for the real-backend suite |
