# ALV-001-C01 R01 — Changed Files

`git diff --stat 3e0a7b75cdc88af5e51978772a9f8b02e2a227b2..01afb667cb18d37643ae48ca3341477b05b04ed -- src` (STORY-001's implementation commit to this attempt's tip): 50 files changed, 3999 insertions(+), 289 deletions(-). No files outside `src/` were touched by this attempt (`.colaberry/`, `docs/`, `CLAUDE.md`, `GPT Docs/` changes visible in a full repo diff predate this attempt and belong to the reviewer's execution-plan update / platform sync, not this story).

## Backend — production code (`src/Alveara.Api`)

| File | Change |
|---|---|
| `Architecture/Identity/AccountService.cs` | Rewritten (+502/-…): registration role-lockdown, bootstrap, MFA, lockout rearm/concurrency-safety, timing-safe unknown-user, password reset, account enable/disable, session revocation |
| `Architecture/Identity/AuditLogEntry.cs` | New event type constants |
| `Architecture/Identity/BootstrapState.cs` | New |
| `Architecture/Identity/IdentityEntities.cs` | `UserAccount` gains `IsDisabled`, `SecurityStamp`, `MfaEnabled`, `MfaSecretProtected` |
| `Architecture/Identity/MfaRecoveryCode.cs` | New |
| `Architecture/Identity/PasswordResetToken.cs` | New |
| `Architecture/Identity/Pbkdf2PasswordHasher.cs` | Dummy-hash constant + test-visible call-count instrumentation |
| `Architecture/Identity/Permission.cs` | New |
| `Architecture/Identity/PermissionMatrix.cs` | New |
| `Architecture/Identity/RequireCsrfTokenAttribute.cs` | New |
| `Architecture/Identity/RequirePermissionAttribute.cs` | New |
| `Architecture/Identity/Role.cs` | `Unassigned` appended |
| `Architecture/Identity/Totp.cs` | New |
| `Controllers/AuthController.cs` | Rewritten/extended (+298/-…): every endpoint listed in `HANDOFF.md`'s Scope section |
| `Data/AlveraDbContext.cs` | New `DbSet`s + indexes for the three new entities |
| `Migrations/20260929173614_AddMfaSecurityAndBootstrap.{cs,Designer.cs}` | New migration |
| `Migrations/AlveraDbContextModelSnapshot.cs` | Updated to match |
| `Program.cs` | Data Protection, Antiforgery, cookie security attributes + `OnValidatePrincipal`, rate limiter |

## Backend — tests (`src/Alveara.Api.Tests`)

| File | Change |
|---|---|
| `AccountServiceAuditTests.cs` | New |
| `AccountServiceBootstrapTests.cs` | New |
| `AccountServiceLoginTests.cs` | Rewritten/extended |
| `AccountServiceMfaTests.cs` | New |
| `AccountServicePasswordResetTests.cs` | New |
| `AccountServiceRegistrationTests.cs` | Revised (role-lockdown assertions) |
| `AccountServiceRoleChangeTests.cs` | Revised (bootstrap-based admin setup) |
| `Alveara.Api.Tests.csproj` | `xunit.runner.json` wired as a copied output file |
| `AuthControllerPermissionMatrixTests.cs` | New |
| `AuthControllerRbacTests.cs` | Rewritten |
| `AuthControllerTests.cs` | Rewritten/extended |
| `AuthCookieAttributesTests.cs` | New |
| `AuthRateLimitTests.cs` | New |
| `IdentityTestHelpers.cs` | New |
| `MigrationUpgradeTests.cs` | Minor adjustment for the new migration |
| `PermissionMatrixTests.cs` | New |
| `xunit.runner.json` | New — disables collection parallelization (see `HANDOFF.md`'s explanation: the timing-parity proof depends on a process-wide counter) |

## Frontend (`src/alveara-client`)

| File | Change |
|---|---|
| `App.tsx` | New routes + `AuthProvider` wiring |
| `app/moduleRegistry.ts` | Security Administration nav entry |
| `components/PermissionDenied.tsx` | New |
| `contexts/AuthContext.tsx` | New |
| `pages/AdminUsersPage.{tsx,css,test.tsx}` | New |
| `pages/LoginPage.{tsx,css,test.tsx}` | New |
| `pages/MfaChallengePage.tsx` | New |
| `pages/ResetPasswordPage.tsx` | New |
| `services/authApi.ts` | New |
| `test/setup.ts` | Fixed shared fetch-mock `Response` reuse bug (see `HANDOFF.md`) |
