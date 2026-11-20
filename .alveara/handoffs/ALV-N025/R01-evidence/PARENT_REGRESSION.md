# ALV-N025 R01: parent and dependency regression

- **Parent course story:** none (new production story); parent regression n/a.
- **Broad regression:** `DEFERRED_TO_ALV-N028`. No full backend suite, no other real-backend walkthroughs and no chain were run.
- **Dependency regression in the focused sweep** (TEST_RESULTS, 487/487): every class the change set touches:
  - patient registration, identity, duplicates, merge, unmerge and household;
  - imports (dry run and commit);
  - portability (export file, import, package);
  - documents (download), MFA and account rotations (revocation), the permission policy, scheduling (the test-helper change);
  - the migration inventory for the four new migrations, with three merge-guard regressions of closed stories (U-015, U-016, U-017) repaired and proven;
  - the full vitest suite (2140/2141; the one failure is U-018, pre-existing at ALV-N024's closure, measured).
- **Known risks left for ALV-N028:**
  - tests elsewhere that register the same name with two birth dates and a shared contact detail would now be warned (F-145);
  - tests that sign in through the API and then roll back past `AddPatientAccessAudit` would be refused (51300, MIG-1);
  - the real-backend specs updated in source only (patient-registration, patient-workspace, patient-merge, patient-portability).
