# ALV-009-C02 R01 - Parent and dependency regression

**Result: verified (Stage 1 scope).** The parent course story is STORY-009 (follow-up tasks, reminders); the companion ALV-009-C01 (recall, work queue) is a dependency, as are ALV-010-C01 (documents) and ALV-N002 (background work). All of their suites passed unchanged on this tree.

| Contract | Result on the final tree |
|---|---|
| STORY-009: `FollowUpApiTests` 27, `FollowUpSchemaTests` 38, `FollowUpValidationTests` 12, `FollowUpPermissionTests` 11, `FollowUpReminderTests` 16, `FollowUpReminderApiTests` 1, `FollowUpBackupRestoreTests` 1 | all passed (inside the focused 1,606) |
| ALV-009-C01: `RecallCalculatorTests` 22, `WorkQueueSchemaTests` 46, `RecallJobTests` 13, `UnscheduledTreatmentJobTests` 21, `FollowUpReminderJobTests` 15, `WorkQueueApiTests` 44, `WorkQueueBackupRestoreTests` 1 | all passed |
| ALV-N002 job tests and the migration inventory | passed |
| Documents (ALV-010-C01) suites and backup/restore drills | passed |
| Every `*ApiTests` class | 872 passed |
| Client: STORY-009 and ALV-009-C01 screen tests (`FollowUpScreens`, `WorkQueueScreens`) | passed inside Vitest 1,647 |
| Walkthroughs: follow-ups 8, work queue 8, notifications 10 | passed |

Every baseline id (the ALV-009-C01 R02 Stage 2 run of 4,906) in a class the runs select by name is present and Passed; the derivation checks this (`artifacts/identity-derivation.txt`: missing 0, changed 0, not passed 0). The full 5,023-test comparison is the frozen Stage 2 contract.

## What this story changed in the parents' surface, and why it is safe
- `FollowUpKinds` gains `ReferralFollowUp` and `LabCaseDue`, and `FollowUpTasks` gains `SourceReferralCaseId` and `SourceLabCaseId`: the migration `AddExternalCases` widens the kind and source check constraints (the old kinds keep their old shape) and alters the follow-up triggers to know the new columns while keeping every old guard; `ExternalCaseSchemaTests` and the follow-up schema suites pass, and the migration rolls back over a database with no case data.
- `FollowUpView` gains two optional trailing fields (existing constructors and responses are unchanged); the work-queue page shows an "Open the case" link for such tasks and lists the two new kinds in its filter.
- `SystemTaskWriter.Create` takes two optional case ids; the recall and unscheduled-treatment callers are unchanged.
- The existing `FollowUpWorkHostedService` runs the new scheduler after the recall and treatment ones; a failure in the new one is logged and retried like the others.
- No client test of the parents was edited except the patient-tab list (a new tab).
