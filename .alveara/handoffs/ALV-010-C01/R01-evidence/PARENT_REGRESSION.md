# Parent regression - ALV-010-C01 R01

**Parent course story: `STORY-010`** (course-verified and recorded complete locally at `4f35077`, pushed; its completion contract is immutable). STORY-010's Done Means were run again on the final tree.

| Parent Done Means | Proof on this tree |
|---|---|
| A document is imported, then categorized: it is linked with the correct metadata | `DocumentImportServiceTests` (44), `DocumentApiTests` (37), `DocumentSchemaTests` (53), `DocumentLibraryServiceTests` (21): **unedited, passing in the full run**; STORY-010's walkthrough steps IMPORT and IMPORT (checked against the server): **14 of 14, spec unchanged** |
| A form is e-signed, then the signed version is preserved | the library still lists the signed form read-only (`DocumentLibraryServiceTests`, walkthrough step SIGNED FORM); `SignedFormSnapshotTests` (12) and `FormSigningTests` (26) |
| All document actions are logged with user and timestamp | `DocumentImportServiceTests`, `DocumentSettingsServiceTests`, `DocumentApiTests`; STORY-010's walkthrough step TRUST (every action in the log with user and time, no file name, description or patient, history numbers without a gap) |

## What was edited in STORY-010's own test files (disclosed)

- **Backend:** `DocumentPermissionTests.cs` gained 14 lines (new permission cases); no case of STORY-010 was changed. `DocumentTestBase.cs` and `SafetyTestBase.cs` (shared test support) gained a storage cleanup on dispose and a `virtual` marker (`5983636`).
- **Client:** `DocumentsPanel.test.tsx` and `DocumentSettingsPage.test.tsx` were changed (`DocumentsPanel.test.tsx` lost 57 lines, mostly fixtures and the scripted fake server, which moved to the shared `documentsTestHarness.ts` so the new test files can reuse them; the import now goes through a fake XMLHttpRequest because the upload shows progress; the filter control became a form). The assertions of STORY-010's behaviour were kept; one case was added to the settings test (the link to the form templates). This is a restructure of the parent's client tests, not a weakening, but it is **not** "no test file edited": the reviewer should read `git diff 05f3880 f3eb57b -- src/alveara-client/src/pages/documents/DocumentsPanel.test.tsx` if the distinction matters.
- STORY-010's real-backend walkthrough spec (`e2e/documents-real-backend.spec.ts`) was **not edited**.

Run results: `playwright.documents.config.ts` 14 of 14 (after fixing the contrast of a link this story added); the backend classes above pass in the 4,294-test run.
