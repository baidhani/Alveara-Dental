# Parent regression - ALV-010-C01 R03

**Parent course story: `STORY-010`** (complete locally, pushed; its completion contract is immutable).

| Parent Done Means | Proof on this tree |
|---|---|
| A document is imported, then categorized: it is linked with the correct metadata | `DocumentImportServiceTests` (44), `DocumentApiTests` (37), `DocumentSchemaTests` (53), `DocumentLibraryServiceTests` (21): unedited and passing on the identical tree (inside the 702-test run); STORY-010's walkthrough steps IMPORT and IMPORT (checked against the server): **14 of 14 on this tree, spec unchanged** |
| A form is e-signed, then the signed version is preserved | the library lists the signed form read-only (`DocumentLibraryServiceTests`); `SignedFormSnapshotTests` (12) and `FormSigningTests` (26); the unchanged walkthrough step for the signed form passes |
| All document actions are logged with user and timestamp | `DocumentImportServiceTests`, `DocumentSettingsServiceTests`, `DocumentApiTests`; STORY-010's walkthrough step TRUST passes (every action in the log with user and time, no file name, description or patient, history numbers without a gap) |

STORY-010's real-backend walkthrough: **14 of 14 passed on this tree** (all steps, including the failure path, TRUST and the database-immutability step that did not run in R02). Output `artifacts/focused-run/playwright.documents.txt`.

## What was edited in STORY-010's own test files (disclosed)

- **Backend:** `DocumentPermissionTests.cs` gained 14 lines (new permission cases); no case of STORY-010 was changed.
- **Client:** `DocumentsPanel.test.tsx` and `DocumentSettingsPage.test.tsx` were changed (`DocumentsPanel.test.tsx` lost 57 lines, mostly fixtures and the scripted fake server, which moved to the shared `documentsTestHarness.ts`; the import goes through a fake XMLHttpRequest because the upload shows progress; the filter control became a form; one case was added to the settings test). The assertions of STORY-010's behaviour were kept. This is a restructure of the parent's client tests, not "no test file edited"; see `git diff 05f3880 a3b4bc9 -- src/alveara-client/src/pages/documents/DocumentsPanel.test.tsx`.
- STORY-010's real-backend walkthrough spec was **not edited**.
