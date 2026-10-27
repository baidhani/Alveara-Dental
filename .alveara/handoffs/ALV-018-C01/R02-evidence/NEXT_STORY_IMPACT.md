# ALV-018-C01 R02 - Next-story impact

- **Item 52, `ALV-018-C02` (bulk import and migration workbench):** unaffected. The package format is documented (`docs/PATIENT_PORTABILITY.md`) and versioned (`alveara.patient-data-package` version 1), so a later importer can read it, but nothing here imports a package. STORY-018's single-patient import is unchanged.
- **Item 56, `ALV-N013` (Windows setup):** must create or verify both `READ_COMMITTED_SNAPSHOT` and `ALLOW_SNAPSHOT_ISOLATION` on the production database (`docs/operations/DATABASE_OPTIONS.md`); without the second the all-patients export reads table by table and says so in the manifest.
- **Permission policy:** three new permissions are registered; later stories that count permissions or assert the matrix must include them.
- **Test infrastructure:** the Stage 2 backend total moves from 5,136 to 5,334 (R01's 5,333 plus the one new test of R02).
