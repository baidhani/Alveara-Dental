# Parent regression - ALV-010-C01 R02

**Parent course story: `STORY-010`** (complete locally, pushed; its completion contract is immutable). **The parent regression is NOT clean on this tree.**

| Parent Done Means | Proof on this tree |
|---|---|
| A document is imported, then categorized: it is linked with the correct metadata | `DocumentImportServiceTests` (44), `DocumentApiTests` (37), `DocumentSchemaTests` (53), `DocumentLibraryServiceTests` (21) unedited and passing in the full run. STORY-010's walkthrough steps IMPORT and IMPORT (checked against the server) ran **before** the failing step and passed. |
| A form is e-signed, then the signed version is preserved | the library lists the signed form read-only (`DocumentLibraryServiceTests`); `SignedFormSnapshotTests` (12), `FormSigningTests` (26); the walkthrough step for the signed form ran before the failing step and passed |
| All document actions are logged with user and timestamp | `DocumentImportServiceTests`, `DocumentSettingsServiceTests`, `DocumentApiTests`. STORY-010's walkthrough step TRUST **did not run** (it is one of the three after the failing step), so the walkthrough's proof of this is missing on this tree |

## The failure

`playwright.documents.config.ts` (STORY-010's walkthrough, spec unchanged): **10 passed, 1 failed, 3 did not run.** Failing step `SETTINGS: ...`: the axe scan of Document settings in dark mode, `color-contrast` on `p:nth-child(3) > a[href$="form-templates"]` (the "Manage form templates" link this story added). The steps that did not run: the failure-path step, TRUST and the immutability step. Output: `artifacts/sweep/walkthroughs/playwright.documents.txt`. On the abandoned `f3eb57b` tree the same walkthrough passed 14 of 14.

## What was edited in STORY-010's own test files (disclosed)

- **Backend:** `DocumentPermissionTests.cs` gained 14 lines (new permission cases); no case of STORY-010 was changed.
- **Client:** `DocumentsPanel.test.tsx` and `DocumentSettingsPage.test.tsx` were changed (`DocumentsPanel.test.tsx` lost 57 lines, mostly fixtures and the scripted fake server, which moved to the shared `documentsTestHarness.ts`; the import goes through a fake XMLHttpRequest because the upload shows progress; the filter control became a form; one case was added to the settings test). The assertions of STORY-010's behaviour were kept. This is a restructure of the parent's client tests, not "no test file edited"; see `git diff 05f3880 17a2f07 -- src/alveara-client/src/pages/documents/DocumentsPanel.test.tsx`.
- STORY-010's real-backend walkthrough spec was **not edited**.
