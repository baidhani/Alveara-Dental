# ALV-N020-C01 R01: test results

Every run on 2026-11-12. Route: "standing" = the owner's written five-minute permission (execution plan, 2026-11-12), "explicit" = the owner's explicit permission for that group. Expected durations come from documented history (the ALV-N019 R02 `backend-tests.csv` timings and earlier runs in this story). Raw output in `artifacts/focused-proof/` (originally `C:/tmp/n020c01/`).

| # | Command (folder) | Route | Expected | Actual | Result | Raw |
|---|---|---|---|---|---|---|
| 0 | `dotnet list package --include-transitive` and `--vulnerable --include-transitive` (`src/Alveara.Api`) | - | - | - | SkiaSharp 4.156.0 (+ NativeAssets.Win32/macOS 4.156.0); no vulnerable packages | `dependency-audit.txt` |
| 1 | `dotnet test --no-build --filter "...LoginBackgroundPipelineTests\|...LoginNoticeRulesTests"` (`src/Alveara.Api.Tests`) | standing | < 60 s | 9 s | 34/34 | `t1-pipeline.txt` |
| 2 | `... --filter "...LoginPresentationApiTests"` | standing | 3-4 min | 26 s | 6/7 (test expectation of a quoted file name) | `t2-api.txt`, `t2.trx` |
| 2b | same, after the fix | standing | ~30 s | 34 s | 7/7 | `t2b-api.txt`, `t2b.trx` |
| 3 | `... --filter "...LoginPresentationSchemaTests\|...RealMigrationInventoryTests"` | standing | ~40 s | 23 s | 11/11 | `t3-schema.txt`, `t3.trx` |
| 4 | `... --filter "...LoginBackgroundBackupRestoreTests\|...BackupServiceTests\|...BackupRestoreTests\|...DocumentBackupRestoreTests\|...AccountServiceBootstrapTests"` | standing | 2-3 min | 176 s | 50/51 (old-backup missing-class list) | `t4-backup.txt`, `t4.trx` |
| 4b | `... --filter "...BackupServiceTests"` after the expectation update | standing | ~40 s | 56 s | 20/20 | `t4b-backupservice.txt` |
| 5 | `... --filter "...LoginBackgroundCatalogTests"` | standing | < 15 s | 19 s | 3/3 | `t5-catalog.txt` |
| 6 | `... --filter "...AuthControllerMfaConfirmSessionTests\|...AuthControllerPermissionMatrixTests\|...AuthorizationOverheadComparison\|...PermissionPolicyPipelineTests\|...PermissionPolicyWriteTests\|...AuthorizationArchitectureTests"` | standing | ~1 min | 94 s | 103/103 | `t6-perm.txt`, `t6.trx` |
| 7 | `... --filter "...LoginPresentationApiTests\|...AuthControllerPermissionMatrixTests\|...AuthControllerRbacTests\|...AccountServiceBootstrapTests"` (after the installer statement) | standing | ~1 min | 59 s | 29/29 | `t7-bootstrap.txt` |
| 8 | `npx vitest run src/pages/login/LoginLayout.test.tsx src/pages/configuration/SignInScreenTab.test.tsx` (`src/alveara-client`) | standing | ~20 s | 11 s / 5 s | 17/18 then 18/18 | `c1-client.txt`, `c1b-client.txt` |
| 9 | `npx vitest run` gating + AuthContext, ConfigurationHubPage, LoginPage, App.routeGuards, App.disconnected, AdminUsersPage, BackupRecoveryPage, useAppTheme, loginContrast | standing | ~30 s | 17 s | 125/125 | `c2-client.txt` |
| 10 | `npm run build`; `npx playwright test e2e/login-presentation.spec.ts --project=desktop` (`TILE_GATE_OUT=C:/tmp/n020c01/tile-gate`) | standing | ~1 min | 9 s / 14 s | 8/12 (no main landmark) then 12/12 | `p1-browser.txt`, `p1b-browser.txt`, `tile-gate/` |
| 11 | `npx playwright test e2e/shell.spec.ts e2e/login-banner-contrast.spec.ts` | standing | ~20 s | 11 s | 28/28 | `p2-mocked.txt` |
| 12 | `python c01_negative_controls.py` (repository root; `scripts/` here) | explicit | ~7 min | 216 s | NC-1..NC-5 all fail as expected; restored client 12/12, server 30/30; byte-identical | `nc-output.txt`, `negative-controls/` |
| 13 | `.\scripts\Invoke-AccumulatedChain.ps1 -ClockMode injected` (repository root, PowerShell) | explicit | ~6.5 min | 377 s | 20/20; builder 24 PASS, 0 FAIL, 0 tracked | `chain-output.txt`, `chain-evidence/` |
| 14 | `.\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.auth.config.ts` | explicit | ~2 min | 47 s | 9 passed, 1 failed (history row lists the new backup class), 2 not run | `walk-auth.txt` |
| 14b | same, after the expectation update | standing | ~1 min | 47 s | 12/12 | `walk-auth-2.txt` |
| - | `npx tsc -b`; `npm run lint`; `dotnet build` API and test project | - | - | - | clean; lint 41 (unchanged); API 0 warnings; test project 0 errors, 32 warnings (unchanged) | `build*.txt` |

Leftover counts (`leak_counts.sh`) identical before and after runs 2, 3, 4, 6, 12-14 and 14b (`*-leak_before.txt` / `*-leak_after.txt`).

No run materially exceeded its estimate (run 6: 94 s against about 1 minute, under the five-minute bound).

## Not run (deferred to ALV-N028)
Full backend suite, full vitest and mocked Playwright suites, repository checks, every other walkthrough, a broad leak accounting.
