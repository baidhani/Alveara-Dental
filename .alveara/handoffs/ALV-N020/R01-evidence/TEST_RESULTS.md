# ALV-N020 R01: test results (focused proof only; broad regression DEFERRED_TO_ALV-N028)

Every run below was started only with the product owner's explicit permission, 2026-11-11 to 2026-11-12, on this machine (Windows 11, .NET 10, Node, SQL Server LocalDB). Raw output is in `artifacts/focused-proof/` (originally written to `C:/tmp/n020-proof/`, outside the repository). Commands are run from the folder named.

## Build and static checks (final code, `17a4775`)
| Check | Command (folder) | Result |
|---|---|---|
| API build | `dotnet build Alveara.Api/Alveara.Api.csproj --nologo -v q` (`src/`) | 0 errors, 0 warnings |
| Test project build | `dotnet build Alveara.Api.Tests/Alveara.Api.Tests.csproj --nologo -v q` (`src/`) | 0 errors, 32 warnings (unchanged from before the story) |
| Type check | `npx tsc -b` (`src/alveara-client`) | clean |
| Lint | `npm run lint` (`src/alveara-client`) | 0 errors, 42 warnings (41 before the story + 1: `useIdentityEpoch` exported from `AuthContext.tsx`) |

## G1 and re-run B: client unit/component tests (2026-11-11)
`npx vitest run src/contexts/AuthContext.test.tsx src/pages/LoginPage.test.tsx src/pages/loginOutcome.test.ts src/contexts/PatientContext.test.tsx src/App.routeGuards.test.tsx src/App.disconnected.test.tsx src/App.unsavedNavigation.test.tsx src/app/useAppTheme.test.tsx src/pages/configuration/StaffLoginDialogs.test.tsx src/pages/AdminUsersPage.test.tsx src/app/AppShell.emptyRegistry.test.tsx src/app/AppShell.permissionAwareNav.test.tsx src/components/ConfigEntityPanel.test.tsx src/pages/ConfigurationHubPage.test.tsx src/pages/MfaSettingsPage.test.tsx` (`src/alveara-client`)
- First run: two harness failures (`App.disconnected` raced the redirect; `ConfigurationHubPage` lacked an `AuthProvider`); both fixed in the tests; the two files re-run 25 of 25; the full list 143 of 143 (`g1-vitest.txt`, `g1-rerun-two.txt`, `g1-vitest-final.txt`).
- Re-run B (after the F-012 fix), the same 15 files plus `src/hooks/useConnectionStatus.test.ts`: **150 of 150** (`rerunB-vitest.txt`).
- `App.routeGuards.test.tsx` changed afterwards (two-tab tests): its last run is G12 below.

## G2: server tests (2026-11-11)
`dotnet test --nologo --filter "<g2-filter.txt>" --logger "trx;LogFileName=g2.trx"` (`src/Alveara.Api.Tests`): StaffInactivationServiceTests 10, AccountServiceLoginTests 13, StaffProviderServiceTests 22, ConfigurationApiTests 8, AccountSafetyTests 24, AuthorizationArchitectureTests 5, FixtureOwnerGuardTests 10, RealMigrationInventoryTests 5. **97 of 97** in 65 s; leftover counts identical before and after (`g2/g2.trx`, `g2-output.txt`, `g2-leak-*.txt`). Classes changed later were re-run in G9-G11.

## G3: mocked Playwright (2026-11-11)
Mocked-API Playwright run of `e2e/shell.spec.ts` and `e2e/login-banner-contrast.spec.ts` from `src/alveara-client`: **28 of 28** (`g3-mocked.txt`; the exact command line was not saved with the output; the proxy ECONNREFUSED lines are expected, no API runs).

## G4: real-backend walkthroughs, each on a fresh database (2026-11-11)
`.\scripts\Invoke-RealBackendWalkthrough.ps1 -Config <config>` (repository root, PowerShell; `-SkipBuild` after the first): auth **12 of 12** (46 s), workspace **14 of 14** (53 s), permission-policy-admin **8 of 8** (69 s); leftovers none (`g4/SUMMARY.txt`). The runner wrote two `*-e2e-out` folders into `src/alveara-client`; they were moved into `g4/`.

## G5: accumulated chain (stage 0 + stage 1 + minimal stage 2), injected clock
`.\scripts\Invoke-AccumulatedChain.ps1 -ClockMode injected` (repository root, PowerShell); each run on its own throw-away `AlveraChain_<id>` database.
| Run | Date | Result |
|---|---|---|
| 1 | 2026-11-11 | F-037 step failed (it navigated before sign-in finished: test defect, fixed in `a6ff826`); later steps skipped (`chain-output.txt`, `run1-evidence/`) |
| 2 | 2026-11-12 | 17 passed; 1.11 failed (reloaded before sign-out finished: test defect, fixed in `41f6c5a`); 13.05 and 1.12 skipped (`chain-output-run2.txt`, `run2-evidence/`) |
| 3 | 2026-11-12 | **20 of 20 passed (5.4 min)**; evidence builder **22 PASS, 0 FAIL, 0 TRACKED_EXCEPTION, 0 SKIPPED_BLOCKED**; database dropped, run folder removed (`chain-output-run3.txt`, `run3-evidence/`) |
Run 3 steps: S-09a-d, C-01, B-01, B-02, 1.01-1.06, 2.01, F-037, 1.10, F-021, 1.11, 13.05, 1.12, E10, CLEAN. The builder marks F-037, F-021 and F-155 "stale tracked entry": they pass now and can leave the tracked-exceptions list. Its summary line says "stage 0" although stage 1 ran (a label in `scripts/chain/Build-ChainEvidence.mjs`, not changed).

## G6: negative controls for the release-blocking fixes (2026-11-11)
`python n020_negative_controls.py` (script in `artifacts/focused-proof/scripts/`; each mutation pattern is checked before anything runs). Results (`g6/SUMMARY.txt`):
- NC-1, F-037 fix removed (same-user check in `loginOutcome.ts`, identity clear in `PatientContext.tsx`): 5 of 44 fail.
- NC-2, F-155/F-014 fix removed (a refresh failure ends the session; 5xx mapping removed): 14 of 45 fail.
- NC-3, F-021 fix removed (the account step skipped): 5 of 10 fail.
- Restored: client 73 of 73, server 10 of 10; all files byte-identical.
- A first attempt (`g6-aborted-attempt/`) stopped on a line-ending pattern mismatch after its first mutation was written; files were restored and only the restore checks ran; the script was rewritten with the pre-check.

## G7: F-012 re-time (2026-11-11)
API stopped, Vite running, three fresh loads of `/login`: banner after 15,745 / 15,431 / 15,507 ms (REPRODUCED); after the fix 589 ms and two more loads in 0.3-0.6 s (`g7/f012-timing*.json`).

## G9: migration and alert (2026-11-12)
`dotnet test --nologo --filter "FullyQualifiedName~StaffInactivationAlertTests|FullyQualifiedName~StaffInactivationNotificationSchemaTests|FullyQualifiedName~StaffInactivationServiceTests|FullyQualifiedName~NotificationSchemaTests|FullyQualifiedName~FollowUpSchemaTests|FullyQualifiedName~RealMigrationInventoryTests|FullyQualifiedName~Alveara.Api.Tests.MigrationTests|FullyQualifiedName~MigrationUpgradeTests|FullyQualifiedName~ConfigurationApiTests"` (`src/Alveara.Api.Tests`): **125 of 130** (199 s, includes the one-time template rebuild for the new migration). The 5 failures had two causes, both fixed in `32ccf40`: the alert key's hyphens refused by `CK_Notifications_SourceKey` (4 tests) and the inventory count 22 -> 23 (1). Client `npx vitest run src/Notifications.test.tsx`: **9 of 9**. Leftovers none. NC-4 in that run was inconclusive (the alert tests already failed) and is in `g8/`.

## G10: re-run after the fix and NC-4 (2026-11-12)
Same command with `--filter "FullyQualifiedName~StaffInactivationAlertTests|FullyQualifiedName~StaffInactivationNotificationSchemaTests|FullyQualifiedName~StaffInactivationServiceTests|FullyQualifiedName~RealMigrationInventoryTests"`: **24 of 24** (26 s); leftovers none. NC-4 (`n020_nc4.py`: the record-then-alert call removed): 3 of 13 fail; restored 13 of 13; byte-identical (`g10/`).

## G11: added acceptance tests (2026-11-12)
- `npx vitest run src/services/authApi.loginTimeout.test.ts src/App.routeGuards.test.tsx`: login-timeout tests 2 of 2; the two new two-tab tests failed (my mock crashed the permission page's delegation card), fixed in `17a4775`.
- `dotnet test --nologo --filter "FullyQualifiedName~StaffInactivationAlertTests|FullyQualifiedName~ConfigurationApiTests"`: **13 of 13** (25 s). Leftovers none (`g11/`).

## G12: route guards and NC-5 (2026-11-12)
`npx vitest run src/App.routeGuards.test.tsx`: **15 of 15**. NC-5 (`n020_nc5.py`: the two-tab navigation in `AppShell.tsx` removed): the different-person two-tab test fails (1 of 15); restored 15 of 15; byte-identical (`g12/`).

## Last result per changed test file (final code)
| Test file | Last run | Result |
|---|---|---|
| AuthContext, LoginPage, loginOutcome, PatientContext, App.disconnected, useAppTheme, StaffLoginDialogs, AdminUsersPage, ConfigurationHubPage, useConnectionStatus | re-run B | all passed (150 of 150 in the set) |
| App.routeGuards | G12 | 15 of 15 |
| authApi.loginTimeout | G11 | 2 of 2 |
| Notifications | G9 | 9 of 9 |
| AccountServiceLoginTests | G2 | 13 of 13 |
| StaffInactivationServiceTests, StaffInactivationNotificationSchemaTests, RealMigrationInventory(Tests) | G10 | 24 of 24 in the set |
| StaffInactivationAlertTests, ConfigurationApiTests | G11 | 13 of 13 |
| chain/stage-1.spec.ts | G5 run 3 | 20 of 20 with stage 0 |
| e2e/auth-real-backend.spec.ts | G4 | 12 of 12 |

## Not run (deferred to ALV-N028; see the ledger)
Full backend suite, full vitest/build, the whole mocked Playwright suite, repository checks, every other walkthrough, leak accounting over a broad run.
