# ALV-N026 R01: parent and dependency regression

**Parent course story:** none (new production story), so parent regression is n/a.

**Broad regression:** `DEFERRED_TO_ALV-N028`. No full backend suite, no full vitest, no mocked Playwright, no real-backend walkthrough and no chain were run.

**Dependency regression in the focused sweep** (TEST_RESULTS, 660/660 on `77caf04`, plus 93/93 on `baaeb51`) covered every class the change set touches:
- documents: library, storage, import, integrity, orphan, settings, permissions, external-case documents;
- backup and System Status;
- billing documents and statements;
- prescriptions: workflow, issues, PDF;
- treatment-plan copies;
- clinical record copies;
- the export catalog and the merge machinery: registry, the named exception, merge schema, the retired-record guard, merge-trigger integrity, merge API, unmerge.

The client gates (`tsc` for app and node, lint, `vite build`) passed, and the focused vitest gave 355/357, where the 2 failures are U-018 (pre-existing). The repository checks are 58/58 on `baaeb51`.

**Regressions of closed stories repaired here:**
- **U-020:** 7 tables were missing from the export catalog (from ALV-N020-C01 and ALV-N025).
- **U-022:**
  - `PatientRecordOpens` (ALV-N025) is now a named exception;
  - `TreatmentPlanItemAppointmentLinks` (ALV-N023) had no merge handling: a merge left its link rows on the merged-away record. It now has a migration and direct-SQL tests.

Both were proven to exist before S8 at `03831e5`.

**Known risks left for ALV-N028:**
- Six new migrations:
  - `AddDocumentClassificationAndManageDocuments`;
  - `AddBackupAlerts`;
  - `AddPrescriptionIssuesAndPrescriberDetails`;
  - `AddTreatmentPlanCopyIssues`;
  - `AddClinicalRecordCopyIssues`;
  - `AddTreatmentPlanItemAppointmentLinksMergeScope`.

  None of their Down paths was exercised. Four of them refuse once rows exist (51304, 51306, 51308, 51310). `AddDocumentClassificationAndManageDocuments` has no refusal, and `AddTreatmentPlanItemAppointmentLinksMergeScope` only restores triggers.
- **Behaviour changes other suites may notice:**
  - `ImportDocuments` no longer grants edit, inactivate or reactivate;
  - clinical documents now need `ViewClinicalDocumentation` to list, open or download, so a role without it no longer sees them;
  - the `/print` prescription route returns 410;
  - the browser `PrintPreview` was removed;
  - the patient export package's `treatment_plans` and `appointments` domains now carry the link history and the checkout outcomes;
  - merge previews list 58 moves.
- **Merge count tests** were edited for the 2 added patient-owned tables (PatientMergeApiTests, PatientMergeSchemaTests, PatientOwnedDataTests, RetiredRecordGuardTests).
- **Not run:** BackupRecoveryHardeningTests and RealProcessDatabaseOutageTests (long), the existing TreatmentPlanApiTests class, and every walkthrough that prints or opens a document.
