# ALV-017-C01 R01 - Next-story impact

Next in the plan: item 50, STORY-018. **Not started.**

- **Every new patient-owned table must be registered.** `PatientOwnedDataTests` fails the build for a table or column with a patient id that is not in the registry (`PatientOwnedData`) or listed as bookkeeping, and `PatientMergeSchemaTests`/`RetiredRecordGuardTests` check that each such table carries the merge-scope trigger and the retired-record check. A new patient-owned table therefore needs: the registry entry, the migration helper that adds the `_MergeScope` trigger (and the retired-record check), and, if it has an immutable trigger, the `/*patient-merge*/` allowance for the patient-id column.
- **The merged-away record refuses new records** (database error 51235, answered 409 `patient_merged`). Any new write path for a patient must treat that as a normal outcome.
- **Undo is limited by newer records.** A record made after a merge that depends on rows that would move back stops the undo. New features that attach to a chart should be added to the registry so that the ledger and the undo see them.
- **Shared files touched:** `AuditLogEntry` (three new event types), `SafeExceptionHandlingMiddleware` (51235), `PatientWrite`, `PatientsController` (detail exposes `mergedAtUtc`), the patient workspace and details panel (read-only when merged away).
