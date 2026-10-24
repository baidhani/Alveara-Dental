# ALV-009-C01 R02 - Parent regression (STORY-009)

> ALV-009-C01 R02 carries this file over from R01 unchanged in substance: R02 changes one existing client test and the evidence contract, and no product, schema, API or UI code (`git diff --name-status 4b12cc1 340624b -- src` is the single file `src/alveara-client/src/PatientWorkspace.test.tsx`). The R01 evidence stands; R02-specific evidence is in `TEST_RESULTS.md` and `artifacts/`.


**Result: verified (Stage 1 scope).** The STORY-009 completion contract is the 106 backend ids STORY-009 added to the 4,628-test ALV-N018 baseline (final STORY-009 full run: **4,734 passed**, `stage2/baseline-backend-tests.csv`), its client screen tests and its real-backend walkthrough.

| Part of the contract | Before this story | After this story (final tree) |
|---|---|---|
| `FollowUpApiTests` | 27 | 27 passed |
| `FollowUpSchemaTests` | 38 | 38 passed |
| `FollowUpValidationTests` | 12 | 12 passed |
| `FollowUpPermissionTests` | 11 | 11 passed |
| `FollowUpReminderTests` | 16 | 16 passed (unchanged; they run through the new job path via the inline wrapper) |
| `FollowUpReminderApiTests` | 1 | 1 passed |
| `FollowUpBackupRestoreTests` | 1 | 1 passed |
| Client `FollowUpScreens.test.tsx` | 17 | 17 passed (mock server and task defaults extended for the new fields; no assertion weakened) |
| Walkthrough `playwright.follow-ups.config.ts` | 8 | 8 passed after three locator fixes (below) |
| Every `*ApiTests` class (HTTP seam) | n/a | 860 passed |

No STORY-009 test id is missing, renamed or changed in outcome in either backend run (compared by id against the baseline for every class the runs cover). The full 4,896-test comparison is the frozen Stage 2 contract.

## What changed in the STORY-009 surface, and why it is safe
- Reminder delivery moved from a direct hosted-service loop to one durable job per task, due time and channel; the old entry point (`ProcessOnceAsync`) remains as an inline wrapper over the same job path, which is why the 16 reminder tests pass unchanged. The reminder rules (open and due, once per due time and channel, office-manager fallback, nobody to tell = no job and counted) are the same.
- A task may now be unassigned, prioritised and system-made; the existing create and edit paths still require an owner for tasks people make (`assignee_invalid`), and old rows were backfilled to Manual/Normal by the migration (`WorkQueueSchemaTests`).
- The follow-up triggers were altered to know the new columns; the old guards are still enforced (`FollowUpSchemaTests` 38 passed, restore drill passed).

## Finding in the STORY-009 walkthrough (fixed, test-side only)
After this story the patient follow-ups page also shows a timeline that names the same task, so three locators in `e2e/follow-ups-real-backend.spec.ts` became ambiguous (a bare list item filter, a title text match, and a button named "All", which also matches "Recall"). They now name the task list and use an exact button name. The assertions are unchanged.
