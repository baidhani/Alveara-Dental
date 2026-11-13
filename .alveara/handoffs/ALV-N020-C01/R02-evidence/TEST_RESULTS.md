# ALV-N020-C01 R02: test results

2026-11-12. All runs under the owner's standing five-minute permission (execution plan); expected durations from this story's R01 timings. Raw output in `artifacts/focused-proof/`.

| # | Command (folder) | Expected | Actual | Result | Raw |
|---|---|---|---|---|---|
| 1 | `dotnet test --no-build --filter "FullyQualifiedName~LoginBackgroundCatalogConcurrencyTests"` (`src/Alveara.Api.Tests`) | < 1 min | 26 s | 3/3 | `r1.txt`, `r1.trx` |
| 2 | same after temporarily disabling `LoginBackgroundCatalogGuard.AcquireAsync` (negative control; guard restored, `cmp` identical) | < 1 min | 44 s | 2/3 pass, the two-uploads test fails | `nc1.txt` |
| 3 | `npx playwright test e2e/login-presentation-access.spec.ts --project=desktop` (`src/alveara-client`), before the CSS rule | < 30 s | 14 s | 4/6 (both forced-colours tests fail: image kept) | `a11y-run0.txt` |
| 4 | same after the CSS rule (`npm run build` first) | < 30 s | 22 s | 6/6 | `a11y-run1.txt` |
| 5 | `npx playwright test e2e/login-presentation.spec.ts e2e/login-presentation-access.spec.ts e2e/shell.spec.ts e2e/login-banner-contrast.spec.ts --project=desktop` | < 1 min | 38 s | 32/32 | `browser-all.txt` |
| 6 | `dotnet test --no-build --filter` over `LoginPresentationApiTests`, `LoginPresentationSchemaTests`, `RealMigrationInventoryTests`, `LoginBackgroundCatalogConcurrencyTests`, `LoginBackgroundBackupRestoreTests`, `LoginBackgroundPipelineTests` | ~1 min | 107 s wall (tests 51 s) | 46/46 | `server-group.txt` |
| 7 | `npx tsc -b`; `npm run lint` | - | - | tsc clean; lint 42 warnings with and without the R02 files (R01 recorded 41) | `lint.txt` |
| 8 | after removing the one lint warning the new spec first added: `npx playwright test e2e/login-presentation-access.spec.ts --project=desktop` | < 30 s | 5 s | 6/6 | (console only) |

Not run: the full backend suite, the full client suites, the chain, the walkthroughs (deferred to ALV-N028; obligations in `.alveara/PROGRAM_REGRESSION_LEDGER.md`).
