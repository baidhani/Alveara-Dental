# ALV-018-C01 R02 (text from R01, unchanged) - Parent regression (STORY-018)

> Attempt R02 note: unchanged since R01. Attempt R02 changes only the test-database fixture's holder diagnostic and its tests (commit `94e1347`); nothing this document rests on changed. The text below is R01's, kept so this package is self-contained.

The parent is the course story `STORY-018` (portal-verified 2026-10-26, completion `f158a5f`); its completion contract is immutable. Evidence that it still passes on the final tree:
- Backend: `PatientExportApiTests`, `PatientExportServiceTests`, `PatientImportApiTests`, `PatientImportServiceTests` and `PatientImportValidatorTests` pass in the 197-test focused run (`artifacts/test-runs/backend-focused.trx`) and in the 907-test `*ApiTests` run. None of these test files was modified after the STORY-018 completion (`git diff --name-status 7f8a3d4 HEAD -- src/Alveara.Api.Tests` shows only added files), and no STORY-018 backend file outside the new `Package` folder changed.
- Real backend: the STORY-018 walkthrough (`playwright.patient-portability.config.ts`) **7 of 7 passed** on a fresh database; in the package walkthrough, "STORY-018 STILL WORKS: the original Export patient file button downloads the JSON file in the documented layout" passed and its file is saved as `original-story-018-export.json`.
- Client: Vitest 1,742 of 1,742, including the STORY-018 export and import button and screen tests. The only client change to that code: the button uses the shared `saveFile` helper and `fileNameFrom` is exported with a fallback (file layout unchanged).

Result: pass. A full backend regression is Stage 2.
