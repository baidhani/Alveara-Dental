# STORY-009 test results

| Layer | Command | Result |
|---|---|---|
| Backend, full serial | `dotnet test --nologo --logger "trx;LogFileName=full_serial.trx"` in `src/Alveara.Api.Tests` on the final tree (clean working tree, nothing else running) | **4,734 passed / 0 failed / 0 skipped** of 4,734, 36 m 23 s. The previous full run (ALV-N018 R02 Stage 2) had 4,628; this is those 4,628, unchanged and all passing, plus exactly 106 new tests (none missing, none changed). |
| Backend, this story's classes | `FollowUpSchemaTests` 38, `FollowUpApiTests` 27, `FollowUpReminderTests` 16, `FollowUpValidationTests` 12, `FollowUpPermissionTests` 11, `FollowUpReminderApiTests` 1, `FollowUpBackupRestoreTests` 1 | 106 cases, exactly the 106 new tests of the full run; all passing |
| Client | `npx vitest run` in `src/alveara-client` | **1,589 passed** of 1,589 (101 files; ALV-N018 left it at 1,572 in 100); `npx tsc --noEmit` 0; `npm run build` 0; `npm run lint` 0 with no finding in the new files |
| Client, mocked browser | `npx playwright test` | **140 passed** |
| Repository checks | `node --test tests/*.test.mjs` | **13 passed** |
| Real backend, browser | `.\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.follow-ups.config.ts` | **8 of 8 passed** on a real Chromium, real API and SQL Server (the runner sets the reminder pass to 3 seconds; 60 by default). The notifications walkthrough, which shares the notification page, also passes (10 of 10). |
| Restore drill | `FollowUpBackupRestoreTests` (inside the full run) | tasks, events, deliveries, the reminder notification, the audit entries and the guards survive a real backup and restore; a later change is not in the restore |

## What the walkthrough shows (`src/alveara-client/e2e/follow-ups-real-backend.spec.ts`)

A dentist checks the form (nothing is sent until it is valid), then creates a follow-up that is already due for the hygienist. Within about 3 seconds the hygienist finds a reminder in the notification center that carries no title and no note, and opens the patient's follow-ups from it. Completing the task makes the next one (due in 90 days, for the same person) and the history lists four entries with who and when (the reminder is the system's own). A change made from an out-of-date screen is explained, stores nothing and the reload shows the current task. Billing has no tab and is refused by the API; revoking `ManageFollowUps` from front desk removes its buttons on the next request. A reminder for a person who has left goes to the office manager and is recorded as a fallback. The audit trail names every action, with no task text. The screens pass axe in both themes.

## Negative controls (14 of 14 caught)

Each row is one deliberate break of one rule, followed by a rebuild and the 55 follow-up service, reminder and validation tests; the source was restored byte for byte afterwards.

| # | Break | Result |
|---|---|---|
| C1 | the reminder dedupe filter removed (a reminded task is picked again) | **survived the first run**: the database's unique key silently caught every repeat, so no test could tell. A test that a channel is never asked again for a reminder it delivered now kills it. |
| C2 | closed tasks are reminded too | caught (2 tests) |
| C3 | tasks not yet due are reminded | caught (3) |
| C4 | a changed due time does not arm a new reminder | caught (1) |
| C5 | no fallback: the reminder always goes to the assignee | caught (3) |
| C6 | completion ignores the row version | caught (1) |
| C7 | an edit ignores the row version | caught (1) |
| C8 | completion makes no recall | caught (4) |
| C9 | completion is not audited | caught (2) |
| C10 | a create is not idempotent (new id every time) | caught (2) |
| C11 | the assignee is not checked on create | caught (2) |
| C12 | a title can be any length | caught (2) |
| C13 | a closed task can be edited | caught (1) |
| C14 | a cancellation needs no reason | caught (2) |

## Disclosures

* The leak counts are identical before and after the full run, but both show one `AlveraTemplate_...` database (created 2026-10-23 20:36, before this run) and one template backup file in the test data folder. They are left over from one of my earlier focused runs that was cut short; this run did not create them. I have not deleted them without the owner's say-so.
* I wrote two wrong counts in PROGRESS.md while working (18 screen tests instead of 17; 16 reminder tests before the last one was added); both are corrected there.
* Not tested against a real email or text provider, because none exists (see `docs/FOLLOW_UPS.md`, "Email later").
