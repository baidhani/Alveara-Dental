# ALV-N026 R02: parent and dependency regression

**Parent course story:** none (new production story), so parent regression is n/a.

**Broad regression:** `DEFERRED_TO_ALV-N028`. No broad regression was performed or is implied in R02.

**Dependency regression planned after the code review** (`TEST_RESULTS.md`):
- **P1:** the prescription and PDF classes.
- **P2:** the forms, signed-form snapshot and document classes the signed-form library change touches (FormsApiTests, SignedFormSnapshotTests, DocumentLibraryApiTests, DocumentClassificationAccessApiTests, DocumentApiTests).
- **W1-W4:** the four walkthroughs.

**Not run yet:** nothing has run on `fa9a9d5` (owner instruction). R01's accepted focused evidence stands for the R01 tree, `.alveara/handoffs/ALV-N026/R01-evidence/`.

**Known risk the R02 server change adds:** a user without `ViewSignedForms` no longer sees signed forms in the library. Default roles are unaffected, so a test elsewhere would notice only if it revokes `ViewSignedForms` and expects library entries.
