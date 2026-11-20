# ALV-N025 R01: next-story impact

- **ALV-N026 (documents, printing, backup):**
  - the MRN on printed patient documents, statements, receipts and prescriptions (OQ-2), with template versions;
  - PD-24 document visibility;
  - the Print buttons now call `POST /api/patients/{id}/print-events` first; new printed materials should do the same (the `PrintMaterial` type and the server's `PatientAccessRecorder.MaterialTypes`).
- **ALV-N027:** U-013 and U-014 (billing forms), and U-018 (the flaky `BillingStatementsPanel.test.tsx`), are unassigned and could fit there.
- **ALV-N028 (full regression):** see PARENT_REGRESSION's known risks. Also:
  - `PatientMergeSchemaTests` now runs green;
  - use `tsc -p tsconfig.app.json` (the root `tsconfig.json` checks nothing);
  - the four new migrations each have a data-preserving Down (two refuse, one keeps an archive table, one keeps normalised values).
- **Every future migration:**
  - a new column on a patient-owned table must be added to its merge-scope trigger by a text edit (U-015 and U-016 lessons);
  - a rollback past `AddPatientAccessAudit` is refused once any session or open row exists.
- **Every future patient insert** gets an MRN at save through `AlveraDbContext`. Raw SQL inserts into `Patients` must supply one.
- **Dev database:** `AlveraDev` needs the four migrations, which apply only with the owner's permission. `AddPatientCountry` normalises any recognizable Sex and State values, with History rows.
