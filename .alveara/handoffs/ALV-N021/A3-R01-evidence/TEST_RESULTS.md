# ALV-N021 A3-R01: test results

2026-11-16, Windows 11, SQL Server (each backend test on its own cloned database), Chromium. Every run had the product owner's Go after the code was shown; each was expected at five minutes or less (one pair of slice-2 runs together took about 6 minutes against my 1-2 minute estimate, each under five; disclosed). No broad or full regression (`DEFERRED_TO_ALV-N028`).

| Slice | Commit | Run | Actual | Result | Negative control | Raw |
|---|---|---|---|---|---|---|
| 1 zone in the session | `3fc39b0` | `PracticeTimeZoneSessionTests` + `AuthControllerPermissionMatrixTests` | 28 s | **14 / 14** | session publishes the raw configured id -> the Windows-configuration test fails; restored by hash, 6/6 | console (PROGRESS.md) |
| 2 local-input parser, five routes | `073a419` | `PracticeLocalInputTests` + `PracticeLocalInputApiTests` + `FollowUpApiTests` + `CompletionApiTests` | 2 m 31 s | **116 / 116** | both-forms check removed -> 5 route cases + 1 unit test fail; restored by hash, 37/37 | `artifacts/focused-proof/backend-slice2.txt` |
| 2 (other routes) | `073a419` | `CompletionAdoptionApiTests` + `ClinicalRecordApiTests` | 1 m 13 s | **51 / 51** | - | console |
| 3 client helpers, session state, fakes | `65f8654` | vitest 26 files; the 21 fake-patient-server users; mocked Playwright 7 changed specs | 22 s; 27 s; 33 s | **313 / 313; 455 / 455; 54 / 54** | formatInstant falls back to the browser zone -> the no-zone test fails; restored, 9/9 | console |
| 4 screens | `4aa9a41` | full vitest; full mocked Playwright | 164 s; 85 s | **2037 / 2039** (2 = my CSS edit, below) then contrast + app 105/105; **165 passed, 29 skipped** | showInstant falls back to the browser zone -> 2 tests fail; restored, 15/15 | console |
| 5 today and inputs | `0316621` | full vitest | 164 s | **2045 / 2045** | sessionToday falls back to the browser day -> 3 tests fail; restored, 28/28 | `artifacts/focused-proof/vitest-full-slice5.txt` |
| 6 guard | `3e19831` | `tests/practice-time-guard.test.mjs`; all repo checks; full mocked Playwright | <1 s; seconds; 93 s | **4 / 4; 53 / 53; 165 passed, 29 skipped** | a browser-zone instant format and a browser "today" planted in `AuditLogPage.tsx` each fail the guard naming the line; restored by hash | `artifacts/focused-proof/guard-negative-controls.txt`, `mocked-playwright-slice6.txt` |
| 7a two browser zones | `b8756f1` | `practice-time-zones-real-backend.spec.ts` (runner, real API and database) | 25 s | **2 / 2** | - (the unit controls cover the fallback) | `artifacts/two-zone-walkthrough/` |

Type-check (`tsc --noEmit -p tsconfig.app.json`) clean after every client slice; strict e2e type-check of the changed specs clean; lint 0 errors and 42 warnings throughout (unchanged from before A3). Backend build clean.

## Failures on the way (all mine, all fixed before the result above)

- Slice 2: none of the product; my estimate of 1-2 minutes for the run pair was wrong (about 6 minutes in total).
- Slice 4: the full client run had 2 failures in `patientWorkspaceContrast.test.ts`: my edit script used a replace-all, which changed the existing `.alv-patient-header__button` colour to `inherit` as well as the new rule. That one rule was restored; contrast and app tests 105/105.
- Slice 7a: the first walkthrough run (63 s with the build) failed on my locator matching two list items; narrowed to the follow-ups list as `follow-ups-real-backend.spec.ts` does; rerun 2/2.

## Two-zone evidence (slice 7a)

| Browser zone | Browser's own day | Practice day | Report "Last day" | Follow-up typed 2030-01-14T09:30 stored as | shown as |
|---|---|---|---|---|---|
| America/Chicago | 2026-11-16 | 2026-11-16 | 2026-11-16 | 2030-01-14T15:30:00Z | 1/14/2030, 9:30:00 AM |
| Asia/Tokyo | **2026-11-17** | 2026-11-16 | **2026-11-16** | 2030-01-14T15:30:00Z | 1/14/2030, 9:30:00 AM |

## Not run

The full backend suite, all other walkthroughs, the accumulated chain (`DEFERRED_TO_ALV-N028`, ledger rows added). Browsers other than Chromium.
