# ALV-N025 R01: acceptance evidence

Against the plan item 62 acceptance (`GPT Docs/Alveara_Dental_Execution_Plan_v4.md`) and briefing revision 3 (approved by `BRIEFING-R03`), as amended by the three reviewed documents and the owner decision record. Evidence sources:
- **Server:** `artifacts/backend-focused-tests.csv` (487/487).
- **Client:** vitest on `f0c474b`.
- **Browser:** `identity-audit-integrity-real-backend` 6/6 (`artifacts/walkthrough/`).
- **Controls:** C1-C11.

## The five acceptance criteria (EXECUTION_STATUS `acceptance.total` = 5)
| # | Criterion | Proof | Status |
|---|---|---|---|
| 1 | Permission Matrix behaviour equals actual API and UI access (incl. Q-5cl's access portion) | PermissionPolicyWriteTests `Q5cl_an_office_manager_reads_the_matrix_but_the_api_refuses_every_change_to_it`; PermissionPolicyScreens.test (read-only matrix without the grant permission); spec MATRIX (`05-manager-matrix.png`) | PROVEN |
| 2 | Audit views are useful without disclosing protected content | AuditLogApiTests (names, filters, an opaque History reference, no values, name or contact data; BR-02 for a ViewAuditLog-only account); AuditLogPage.n025.test; PatientWorkspace.test (History refused without ViewPatientRecords); spec HISTORY LINK (`03`, `04`); C4, C7 | PROVEN |
| 3 | Patient identity, import and merge behaviour preserves data and records the actor and reason | registration override reason and ids audited (PatientDuplicateWarningTests); the different-birth-date rule (C5); MRN preserved across merge, unmerge and rollback (MrnTests, MrnMigrationTests; C1, C2, C8, C9); merge identity kept (PatientMergeServiceTests MRG1); merge-guard repairs (MergeTriggerIntegrityTests; C10, C11); demographics normalised with a system History row (DemographicsMigrationTests); household dissolve audited (HouseholdDissolveTests); import multi-status (ImportPatientDryRunTests F144); spec REGISTRATION, MRN (`01`, `02`) | PROVEN |
| 4 | PD-20 resolved: patient-access trail | the briefing section 4 design plus SES-1. AuditSessionLifecycleTests (8: one sid and deadline, exactly-once sign-out, expiry and revocation, the 20-round race, 51301, SES-1 non-blocking, MFA re-issue, authentication independent of the table); PatientAccessTrailTests (6: once per session, SES-2, 51299, prints, EXP-2 one event per export path, Down 51300); DocumentApiTests DOC1; recordPrint.n025.test; spec ACCESS TRAIL, SIGN-OUT; C3, C6 | PROVEN (the expiry-after-sign-out half is server-side only; owner decision) |
| 5 | PD-21 resolved: Medical Record Number | the MRN rollback addendum and MRN-EX. MrnTests (10), MrnMigrationTests (5); PatientWorkspace.test MRN; spec MRN; C1, C2, C8, C9 | PROVEN |

## Findings and owner decisions
| Id | Delivered as | Proof |
|---|---|---|
| U-011, F-029, F-149, F-172 | One actor-name rule | AuditLogApiTests names; PermissionPolicyScreens (changedByName); Clinical.test ("by Dee Dentist") |
| F-150, F-174, F-041, F-040 | Audit page heading, filters, History link, guarantor and household detail | AuditLogApiTests; AuditLogPage.n025.test |
| F-126, F-127, F-128, F-212 | Observability | ObservabilityClassificationTests |
| U-015 | Retired-record guard restored on Appointments | RetiredRecordGuardTests U015 (2) |
| D-02a, F-036 | Register-anyway reason, ids audited | PatientDuplicateWarningTests; PatientRegistrationDuplicates.test; spec REGISTRATION |
| F-145, F-210 | Different-birth-date rule in registration, import and the scan | PatientDuplicateWarningTests, PatientDuplicateScannerTests, ImportPatientDryRunTests; C5 |
| F-148, F-211 | Not-a-duplicate reason label; picker detail | PatientMergeScreens.test; PatientHousehold.test |
| PD-21, EXP-1, MRN-RB, MRN-EX | MRN | above |
| U-016, U-017 | Merge-guard repairs (4th migration) | MergeTriggerIntegrityTests (6); PatientMergeSchemaTests; C10, C11. U-016 "cannot change" proofs for the four completeness columns (owner decision) |
| F-032, F-033, DEM-1 to DEM-4 | Demographics and Country | DemographicsValidationTests (19 cases), DemographicsMigrationTests (2), PatientExportServiceTests; PatientDemographics.n025.test |
| PD-20, SES-1, SES-2, EXP-2, DOC-1, OQ-1 | Access trail | above |
| F-202, OQ-5 | Household dissolve | HouseholdDissolveTests (2) |
| F-042 | In-page confirmations | PatientWorkspace.test and PatientHousehold.test (declined, confirmed, browser dialog never used) |
| F-147, F-144 | Import wording; multi-status filter | ImportPatientDryRunTests F144; ImportBatchPage.test |
| Q-5cb, MRG-1 | Survivor identity kept; explanation shown | PatientMergeServiceTests MRG1; PatientMergeScreens.test |
| Q-5cl (access) | Matrix read-only equals API | criterion 1 |
| OQ-2 | MRN on printed documents | Deferred to ALV-N026 (owner) |
| OQ-3 | Blocked duplicates not logged | No change (owner) |
| U-018 | Pre-existing flaky ALV-N024 client test | Recorded, UNASSIGNED (owner); not changed |
