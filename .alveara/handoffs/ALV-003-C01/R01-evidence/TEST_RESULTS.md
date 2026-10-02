# ALV-003-C01 R01 — Test results

All results below are from the **final committed code** (implementation commit `fdafb00b37b717bb768265453b0309eee8ff5717`, working tree clean). Environment: Windows 11, .NET 10, SQL Server LocalDB, Node, Chromium via Playwright. Every backend test builds its own real SQL Server LocalDB database (real migrations, real constraints); nothing is mocked at the database.

## Commands and results

| # | Command (from) | Result |
|---|---|---|
| 1 | `dotnet test` (`src/Alveara.Api.Tests`) — full suite | **492 passed, 0 failed, 0 skipped** (22 m 36 s). TRX counters: total 492 / executed 492 / passed 492 / failed 0 |
| 2 | `npx vitest run` (`src/alveara-client`) | **319 passed, 0 failed** (37 files) |
| 3 | `npx tsc -b` | clean |
| 4 | `npm run build` | clean (Vite production build) |
| 5 | `npx oxlint` | no warnings in any file this story added or changed; two pre-existing warnings remain (`ConfigEntityPanel.tsx:104`, `AvailabilityTab.tsx:85`) |
| 6 | `npx playwright test` (default mocked suite) | **40 passed** (desktop + tablet) |
| 7 | `npx playwright test --config=playwright.workspace.config.ts` (real API + real LocalDB `AlveraE2E`, fresh database) | **14 passed** — the ALV-003-C01 walkthrough |
| 8 | `npx playwright test --config=playwright.patients.config.ts` (same real stack) | **7 passed** — STORY-003's original walkthrough, unchanged |
| 9 | `npx playwright test --config=playwright.auth.config.ts` | **12 passed** — `auth-real-backend.spec.ts` |
| 10 | `npx playwright test --config=gate-a/playwright.gate-a.config.ts` | **3 passed** — A2 axe on 10 routes × light/dark with hover/focus states, A6 seven-role navigation matrix, A2 verdict |
| 11 | `node --test tests/story-000-coexistence.test.mjs tests/no-direct-db-access.test.mjs` | **7 passed** |

**Total: 492 + 319 + 40 + 14 + 7 + 12 + 3 + 7 = 894 passing, 0 failing.**

## Backend — patient classes (per-result counts from the TRX)

| Class | Tests | Origin |
|---|---:|---|
| `PatientModelTests` | 14 | STORY-003 (unchanged) |
| `PatientRegistrationServiceTests` | 24 | STORY-003 (unchanged) |
| `PatientsApiTests` | 7 | STORY-003 (unchanged) |
| `PatientDuplicateWarningTests` | 12 | **new** |
| `PatientEditTests` | 14 | **new** |
| `PatientRelationshipTests` | 12 | **new** |
| `PatientDirectoryTests` | 7 | **new** |
| `PatientIdentityApiTests` | 15 | **new** |
| `PatientRequirementsTests` | 12 | **new** |
| **Patient total** | **117** | 45 parent + 72 new |

Remaining 375 tests are the pre-existing suite (identity, audit, concurrency, backup, configuration, migrations, etc.), all passing. 492 − 117 = 375; before this story the suite was 420 (375 + the 45 parent tests).

## Frontend — patient files (per-test counts from the vitest JSON report)

| File | Tests | Origin |
|---|---:|---|
| `pages/PatientRegistrationPage.test.tsx` | 9 | STORY-003 (unchanged) |
| `App.patientRoute.test.tsx` | 2 | STORY-003 (unchanged) |
| `PatientWorkspace.test.tsx` | 19 | **new** |
| `PatientHousehold.test.tsx` | 13 | **new** |
| `PatientRequirements.test.tsx` | 9 | **new** |
| `contexts/PatientContext.test.tsx` | 12 | **new** |
| `pages/PatientRegistrationDuplicates.test.tsx` | 6 | **new** |
| `App.patientWorkspaceRoutes.test.tsx` | 6 | **new** |
| `styles/patientWorkspaceContrast.test.ts` | 55 | **new** |
| **Patient total** | **131** | 11 parent + 120 new |

Before this story the frontend suite was 199 (188 + STORY-003's 11); 199 + 120 = 319. The new component tests run the real `<App />` (real router, shell, auth, patient context) against `src/test/fakePatientServer.ts`, an in-memory stand-in for the patient API that models response shapes and row-version checking only; the rules themselves are proven against the real API by the backend tests and the real-browser walkthrough.

## Real-browser walkthrough — what the 14 tests cover (`e2e/patient-workspace-real-backend.spec.ts`)
registers a family; likely-duplicate comparison then register-anyway, exact duplicate blocked; search by name/birth date/phone; workspace header + edit + history; two-user concurrent edit refused; household and guarantor independent; invalid relationships refused; inactive/active; patient switch on a slow connection; configurable email requirement (form, server, existing-patient edit, relax); keyboard-only registration and search; audit trail; dentist read-only; accessibility of register/details/not-found. Axe: 18 scans (9 screens/states × light and dark), **0 critical/serious** — `artifacts/playwright-real-backend/patient-workspace-e2e.json`.

## Problems met while verifying (all resolved; none left open)
1. **Parent regression caught two design errors before they shipped**: the shared validator threw a base exception type STORY-003's tests do not expect (fixed with a compatibility wrapper), and a "same phone + same surname" duplicate rule warned on STORY-003's own father/son case (rule removed; every likely rule now needs the same birth date).
2. **Registration race**: a simultaneous retry could reach the duplicate check after the first request committed and be reported as a duplicate instead of a replay; fixed; the simultaneous-registration/retry tests then passed 6 consecutive full-class runs, and the final full suite.
3. **Real-browser axe found two contrast failures in shared components** (success toast 4.45:1; conflict banner title 3.46:1) — see `HANDOFF` findings F1/F2 and the contrast tests that pin them.
4. **Real-browser run found the StrictMode load loop** that jsdom cannot reproduce; fixed with a regression test (verified to fail against the old code).
5. **Test mistakes of mine, corrected**: a lost second-user save (context closed before the save landed), ordering of "Smith" before "Smith-Jones", a surname 3 typos away, seed patients that were themselves likely duplicates, an auth rate-limit in test setup (raised for the test host only), and one test-file overwrite that I reverted from git so STORY-003's own route test file stayed unchanged.

## Not run
Gate A A1 (root Command Center) and A9 (recovery-key timing) specs and `gate-a-keygen-timing`: not touched by this story. No Gate B evaluation was run.
