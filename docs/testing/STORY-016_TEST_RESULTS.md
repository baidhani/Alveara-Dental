# STORY-016 test results (prescriptions)

**Risk declaration: HIGH.** The story writes patient-safety records, adds an audit event, appends two permissions to the shared matrix and adds a migration with an immutability trigger. Per the testing-cadence rule a high-risk story runs the full backend, full frontend, repository checks and the mocked browser suite, plus a real-backend walkthrough and negative controls for the new guards.

## Gates, exact commands and results

| Gate | Command | Result |
|---|---|---|
| Backend, full serial | `dotnet build --nologo` then, in `src/Alveara.Api.Tests`, `dotnet test --nologo --no-build --logger "trx;LogFileName=story016_full.trx"` (no parallelism, no rebuild during the run) | **3509 passed / 0 failed / 0 skipped** of 3509, 1 h 37 m (previous full run 3324; +185 from this story) |
| Story backend tests | `dotnet test --filter "FullyQualifiedName~Prescription\|FullyQualifiedName~AllergyCheck"` | 185 passed (rules and allergy 84, schema 43, service 25, API 24, permissions 9) |
| Frontend unit | `npx vitest run` | **1423 passed / 0 failed / 0 skipped** of 1423 (92 files; previous 1392 in 91; +31 in `PrescriptionsPanel.test.tsx`) |
| Type check, build, lint | `npx tsc --noEmit`, `npm run build`, `npm run lint` | exit 0 each (lint warnings only; the one in new code is `parseDose` exported beside a component) |
| Mocked browser | `npx playwright test` (default config) | **136 passed / 0 failed / 0 skipped** (unchanged; the new real-backend spec is in `testIgnore`) |
| Repository checks | `node --test tests/*.test.mjs` (repository root) | **8 passed / 0 failed / 0 skipped** |
| Real-backend walkthrough | `sh /c/tmp/run_e2e.sh <fresh db> playwright.prescriptions.config.ts PRESCRIPTIONS_E2E_OUT <dir>` | **10 passed / 0 failed / 0 skipped**; evidence in `docs/testing/prescriptions-walkthrough/` |
| Negative controls | see `docs/testing/PRESCRIPTIONS_NEGATIVE_CONTROLS.md` | 12 of 12 killed (one after adding a test) |

## Defects found by the story's own verification

- The real browser found the dose error text failing colour contrast in dark mode (a token that does not exist); fixed to the themed danger colour.
- The allergy refusal said "Nothing was saved." twice; fixed.
- Negative control B10 showed a replay was not protected from a re-check; test added.

## Housekeeping

Scratch databases dropped and generated output folders deleted; leak counts unchanged (AlveraTest 54, AlveraRestore 59, `alveara_` logins 273, `alveara_` databases 248).
