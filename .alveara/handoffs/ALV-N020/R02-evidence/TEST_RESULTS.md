# ALV-N020 R02: test results (focused proof for the four corrections; broad regression DEFERRED_TO_ALV-N028)

All on 2026-11-12, on this machine. Raw output: `artifacts/focused-proof/` (originally `C:/tmp/n020-r02/`, outside the repository). Under the owner's rule of 2026-11-12, runs totalling 5 minutes or less need no separate permission; the chain and the runner self-test (together over 5 minutes) ran with the owner's explicit permission, twice (the first attempt failed at runner setup).

## Static checks (final code `e28282f`)
| Check | Command (folder) | Result |
|---|---|---|
| Type check | `npx tsc -b` (`src/alveara-client`) | clean |
| Lint | `npm run lint` (`src/alveara-client`) | 0 errors, **41 warnings** (the pre-story count; R01 had 42) |
| Chain spec type check | `npx tsc --ignoreConfig --noEmit --module esnext --moduleResolution bundler --target es2022 --strict --skipLibCheck --esModuleInterop --types node chain/stage-1.spec.ts` (`src/alveara-client`) | clean |
| Runner parse | PowerShell `Parser::ParseFile` on `scripts/Invoke-AccumulatedChain.ps1` | 0 parse errors |
| Test project build | `dotnet build Alveara.Api.Tests/Alveara.Api.Tests.csproj --nologo -v q` (`src/`) | 0 errors, 32 warnings (unchanged) |

## Client tests after the lint split (`734b922`)
`npx vitest run src/contexts/AuthContext.test.tsx src/contexts/PatientContext.test.tsx src/App.routeGuards.test.tsx src/pages/LoginPage.test.tsx src/App.disconnected.test.tsx src/app/useAppTheme.test.tsx` (`src/alveara-client`): **6 files, 64 of 64** (`c1-lint-split.txt`).

## Server: notification schema (`8162157`)
`dotnet test --no-build --nologo --filter "FullyQualifiedName~Alveara.Api.Tests.StaffInactivationNotificationSchemaTests"` (`src/Alveara.Api.Tests`): **8 of 8** in 25 s (the 6 R01 cases + the 2 document-link cases); leftover counts identical before and after (`s1-schema.txt`, `s1.trx`, `s1-leak_*.txt`).

## Server: architecture guard
`dotnet test --no-build --nologo --filter "FullyQualifiedName~Alveara.Api.Tests.AuthorizationArchitectureTests"`: **5 of 5** (`arch.txt`, `arch.trx`). Closes R01 disclosure 4 (not re-run after `InAppStaffInactivationAlert` was added).

## Chain: stage 0 + stage 1 + minimal stage 2, injected clock
`.\scripts\Invoke-AccumulatedChain.ps1 -ClockMode injected` (repository root, PowerShell).
| Attempt | Code | Result |
|---|---|---|
| 1 | `6e53928` | **setup failure after 61 s, no test ran**: "The specified wildcard character pattern is not valid: AI-Project-[Alveara-Dental]" (`Start-Process` resolved the bracketed current folder as a wildcard). The runner still dropped the database and removed the run folder (evidence builder: RUN FAIL, PW FAIL, CLEAN PASS). Fixed in `e28282f`. Output: `chain-attempt1/` |
| 2 | `e28282f` | **20 of 20 passed (5.8 min)**. Evidence builder: **24 PASS, 0 FAIL, 0 TRACKED_EXCEPTION, 0 SKIPPED_BLOCKED**, exit 0. Runner: "API control: stop -> stopped owned API process 44012; port 5072 free", "API control: start -> started owned API process 23468 with the same settings; healthy". Steps 13.05, 1.12, API-STOP, API-START PASS; E10 and CLEAN PASS. Output: `chain-output.txt`, `chain-evidence/` (evidence.json, runner-steps.json, playwright-report.json, api-control acks) |

## Runner self-test
`node --test tests/stabilization/chain-runner-resilience.test.mjs` (repository root): **8 of 8** after attempt 1 (28 s) and again after attempt 2 (37 s) (`chain-attempt1/runner-selftest.txt`, `runner-selftest.txt`). It covers the refusals (existing database, existing run folder, bad run id), the commit stamp and the API-killed-at-start-up cleanup; it does not exercise the Playwright launch, which the chain attempt 2 proves.

## Leftovers
`leak_counts.sh` before and after the chain + self-test: identical (`chain-leak_*.txt`).

## Not run (deferred to ALV-N028)
Everything in the ledger's ALV-N020 rows; no broad suite.
