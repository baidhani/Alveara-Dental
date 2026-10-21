# Acceptance evidence - ALV-010-C01 R03 (7 of 7)

The Execution Plan v4 prompt (item 40) lists seven acceptance items and six required tests. All seven are met on this tree; the broad regression is pending (Stage 2).

| # | Acceptance item | Evidence |
|---|---|---|
| 1 | Original STORY-010 tests still pass | STORY-010's backend classes (`DocumentSchemaTests` 53, `DocumentApiTests` 37, `DocumentImportServiceTests` 44, `DocumentLibraryServiceTests` 21, `DocumentSettingsServiceTests` 40) are unedited and passed on the identical tree (inside the 702-test document run); STORY-010's real-backend walkthrough (`documents-real-backend.spec.ts`, spec unchanged) **passes 14 of 14 on this tree** (fresh run, `artifacts/focused-run/playwright.documents.txt`), which resolves finding R02-01. The client tests `DocumentsPanel.test.tsx` and `DocumentSettingsPage.test.tsx` were moved onto a shared harness (see `PARENT_REGRESSION.md`) and pass (91 documents client tests passed fresh). |
| 2 | Representative image/PDF/document types can be safely categorized and viewed | `DocumentPathSafetyTests` (61) and STORY-010's `DocumentImportServiceTests`: PNG, JPEG, PDF, text and other types are detected by content, categorized and refused when they claim a type they are not; STORY-010's walkthrough imports a PNG, a PDF and a text file through the screen and checks that files are served as downloads except PDF and PNG, shown inline under `Content-Security-Policy: sandbox`; the new walkthrough shows the thumbnail view (a picture drawn, a PDF named in words), Preview, and the filters (`artifacts/walkthrough/`). `DocumentLibraryFilterTests` (24) covers thumbnails (type and size limits, no log entry). |
| 3 | Encounter/tooth-region metadata works | `DocumentContextServiceTests` (28) at the service, `DocumentLibraryApiTests` (29) over HTTP (import, edit, clear, wrong values named field by field, another patient's encounter refused), `DocumentLibrarySchemaTests` (46) at the database (check constraints and the trigger that keeps an encounter on the document's own patient), client tests `DocumentLibraryFeatures.test.tsx`, and the new walkthrough step IMPORT (a document tied to an encounter and a tooth, one to an oral region, the server holding exactly that) and FILTERS (by tooth, region, encounter, text, date). The encounter picker reads a documents-scoped list so the importers (who cannot read clinical notes) can use it. |
| 4 | Unsafe filenames/content types are rejected or safely normalized according to policy | `DocumentPathSafetyTests` (61): path parts stripped, device names, traversal, blocked and program types, a claimed type not matching the content; STORY-010's `DocumentImportServiceTests` and walkthrough (a blocked type, a web page, a program under a harmless name, a PNG named `.pdf` and an empty file each refused with nothing stored). The stored identity is a generated GUID; the user's file name is metadata only. 12 breakages of these rules were killed (`artifacts/negative-controls/`). |
| 5 | Template edit after signing does not alter signed content | `SignedFormSnapshotTests` (12, one added here: the document library's signed-form listing keeps the same snapshot and hash after the template is edited to a new version and then inactivated), `FormSigningTests` (26), `FormLifecycleTests` (17) and the database-bypass tests of the snapshot; STORY-010's walkthrough shows a signed form listed read-only and unchanged after its template is edited. |
| 6 | Failed import does not leave orphaned authoritative metadata/blob state | `DocumentImportServiceTests` (a failure after the file was written removes the file in the same request; a failed metadata save leaves no record; repeating the request is safe), `DocumentOrphanServiceTests` (14: an interrupted import's stray file is listed only when older than an hour and removed only by an explicit selection, re-checked under a lock, logged before deletion), `DocumentIntegrityServiceTests` (16), `DocumentStorageApiTests` (22); STORY-010's walkthrough failure path (the log write made to fail: nothing imported, same click works afterwards); the new walkthrough UNREFERENCED FILES step (stray file listed when old, a fresh one never, Cancel removes nothing, removal logged). |
| 7 | A backup/restore drill proves representative stored documents survive recovery | `DocumentBackupRestoreTests` (5): documents, history, status changes, event numbers and storage results survive a backup and restore byte for byte; the recovered application serves content and thumbnails with a clean storage check; a backup that lacks a document's file, or holds a file that differs from the recorded hash, fails restore validation (new check `restored_documents_have_their_files`, which closes a real gap: restore used to trust such a backup); an empty library passes. Existing backup suites with them 126 of 126; 3 of 4 breakages of the new check killed, the 4th redundant. |

## The prompt's required tests

| Required test | Test(s) |
|---|---|
| Original regression | item 1 above |
| File type/size/path-safety tests | `DocumentPathSafetyTests` (61), `DocumentImportServiceTests` (size limit and types), `DocumentSettingsServiceTests` |
| Blob-integrity/missing-file test | `DocumentIntegrityServiceTests` (16), `DocumentStorageApiTests` (22): missing file `404 file_missing`, wrong size `409 file_corrupt`, hash check, nothing served; the new walkthrough MISSING FILE step (a file taken from disk is reported missing, the library says so, opening is refused with no path in the reply, intact again when put back) |
| Signed snapshot regression | item 5 |
| Transactional import/recovery test | item 6 |
| Backup/restore document round-trip | item 7 |

## The prompt's failure paths

Unsupported/mismatched/oversize file, path traversal or malicious name, invalid metadata: items 3 and 4. Storage write succeeds but metadata fails, or the reverse: item 6. Corrupt or missing blob: the blob-integrity row. Signature interrupted: `FormSigningTests.An_interrupted_signature_leaves_a_plain_draft...` and the cancelled-request test. Template changed after signing: item 5. Backup/restore omits a document asset: item 7.

## Security, audit and data-integrity requirements

Document permissions: `ImportDocuments`, `ViewDocuments` (STORY-010) and the new `ManageDocumentStorage` (Admin and Office manager), tested for every role (`DocumentPermissionTests`, `DocumentLibraryApiTests`, `DocumentStorageApiTests`); inactivation held by Admin and Office manager. Audit: imports, edits, opens, inactivation, reactivation, storage checks and each orphan removal are logged with who and when and **no file name, reason or patient** (new walkthrough step THE LOG; `DocumentStatusService` tests). Storage paths and identifiers are never taken from the caller. Diagnostic logs carry counts and opaque ids only.


## The R02 findings, resolved

| Finding | Resolution | Evidence |
|---|---|---|
| ALV-010-C01-R02-01: STORY-010's walkthrough fails on the dark-mode contrast of "Manage form templates" | the link (and the storage page's "Open the patient's documents" link) use the information colour (`.alv-documents__link`) | STORY-010's walkthrough **14 of 14**; the library walkthrough scans the storage page in its missing-file state in light and dark (**12 of 12**, 0 critical or serious) |
| ALV-010-C01-R02-02: the restored test tree leaks storage folders | `TestStorageCleanup` removes the storage folder of every document test and of every API test host on dispose; a failed removal is raised, not hidden | `DocumentTestStorageCleanupTests` (3) and `ApiHarnessStorageCleanupTests` (3) in the focused run of 47; `Test-Data\Storage` count 3920 before, 3920 after the focused backend run and 3920 at the end |
