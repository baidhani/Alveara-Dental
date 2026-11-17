# ALV-N022 A2-R02: acceptance evidence

Against review A2-R01 CR-1 ("Required R02 correction"). Everything else: A2-R01 evidence, unchanged (`A2-R01-evidence/ACCEPTANCE_EVIDENCE.md`).

| CR-1 requirement | Proof | Status |
|---|---|---|
| Database-enforced guard rejecting a new diagnosis on a Finalized encounter | migration `20261117160917_AddDiagnosisFinalizedEncounterGuard`, trigger `TR_Diagnoses_EncounterNotFinalized`; direct-insert test; control G1 | PROVEN |
| 1. Same-patient and immutable-link protections preserved | `TR_Diagnoses_Links` untouched; DiagnosisSchemaTests 28/28 | PROVEN |
| 2. Only new inserts; correction/withdrawal unaffected | AFTER INSERT only; correction-and-withdrawal test | PROVEN |
| 3. Mapped to 409 `encounter_finalized` without SQL text or PHI | interleaving test (code, status, field, no "51287" in the message); control G2 | PROVEN |
| 4. Same-key replay preserved | replay test in DiagnosisServiceTests | PROVEN |
| Deterministic interleaving: no diagnosis, version or audit; `encounter_finalized` | interleaving test | PROVEN |
| Direct persistence coverage | direct-insert test | PROVEN |
| Migration up/down or upgrade coverage | DiagnosisFinalizedGuardMigrationTests (2) | PROVEN |
| Relevant focused diagnosis tests | 12 classes, 266/266 | PROVEN |
| Existing browser workflow | 1/1 | PROVEN |
| Completion/adoption/amendment rule unaltered | no change to those files (CHANGED_FILES) | AS REQUIRED |
