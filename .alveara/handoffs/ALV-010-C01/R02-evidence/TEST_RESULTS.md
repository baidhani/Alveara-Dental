# Test results - ALV-010-C01 R02

**Cadence classification: HIGH RISK** (a migration, restore validation, a permission, shared test infrastructure). The full backend regression is required and its result is below.

**This attempt reports a failing parent walkthrough. Read "What fails" first.**

## What this attempt is

`R01` was reviewed and given a conditional Stage 1 approval for implementation `f3eb57b` (`.alveara/reviews/ALV-010-C01/R01-stage1.md`). After the approval, at the owner's instruction, the code under `src/` was put back **exactly** to the tree the 2 h 41 m full backend run had tested (`be29f3b`), which removes three post-run commits' changes (test-storage cleanup, two link-contrast fixes, one extra accessibility scan). That makes the implementation of this attempt `17a2f073b3cd49b99a9de73a0949dea12ca521fc` (`17a2f07`), a commit whose `src/` has **no difference** from `be29f3b` (`git diff be29f3b 17a2f07 -- src` is empty). `R01` and its ZIP are unchanged history.

## What fails

| Failing run | Detail |
|---|---|
| **STORY-010's real-backend walkthrough** (`playwright.documents.config.ts`, spec `documents-real-backend.spec.ts`, **unchanged**) | **10 passed, 1 failed, 3 did not run.** The failing step is `SETTINGS: the administrator adds and renames categories, sets the size limit and blocks and unblocks a type; built-in types are locked`: the axe scan of the Document settings screen in **dark mode** reports a `color-contrast` violation on the "Manage form templates" link that this story added (`p:nth-child(3) > a[href$="form-templates"]`). The three steps after it did not run. Raw output: `artifacts/sweep/walkthroughs/playwright.documents.txt`. |

This is a real defect in a link this story added, and it makes **acceptance item 1 (original STORY-010 tests still pass) not met** on this tree. A fix exists in history (`f3eb57b`, which passed the same walkthrough 14 of 14) but is deliberately **not** part of this attempt.

## Which commit each run used

| Run | Commit | Is the production code the same as this attempt's? |
|---|---|---|
| Full serial backend | `be29f3bae2d2eb8f1d8adfa09fc10684f3df12bf` | `src/` identical to `17a2f07` (the test project too) |
| Sweep (client gates and 30 walkthroughs) | `598363615796b9033bdfbad7009ce3530632e2c7` (`5983636`) | Backend production code and all of `src/alveara-client` identical to `17a2f07`; only files in the backend **test** project differ (the storage cleanup), which affect no walkthrough |
| Mocked Playwright second run (after one catalog entry) | the tree of `f3eb57b` | The catalog entry (`.alveara/story_catalog.json`) is in this attempt's tree; the tree also had two documents-page link styles that this attempt does not have. The mocked suite has no test of those pages. |

## Results

| Suite | Command | Result |
|---|---|---|
| Backend, full serial | `dotnet test` (Alveara.Api.Tests) | **4,294 of 4,294 passed**, 0 failed, 0 skipped, 2 h 41 m, on `be29f3b` whose `src/` equals this attempt's |
| Frontend, full | `npx vitest run` | **1,544 of 1,544 passed** (97 files) |
| Type check, build, lint | `npx tsc --noEmit`, `npm run build`, `npm run lint` | exit 0, built, exit 0 (existing warnings) |
| Mocked browser | `npx playwright test` | first pass 134 passed, 2 failed (the Architecture Map's story catalog lacked ALV-N015, which predates this story); with the catalog entry, **136 of 136** (run on the `f3eb57b` tree, see above) |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8 passed** |
| Real-backend walkthroughs, all 30 configs, each on a fresh database | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config ...` | **319 checks passed, 1 failed, 3 did not run**; 29 of 30 configurations pass completely, `documents` (STORY-010's) fails (see above) |
| Negative controls | scripted breakages | **62 breakages, 60 killed, 2 documented non-gaps** (`artifacts/negative-controls/negative-controls.txt`, unchanged from R01: they ran against the production code, which this attempt shares) |

### Real-backend walkthroughs (first sweep pass, the tree this attempt contains)

| Walkthrough | Result |
|---|---|
| `gate-c_c2` | 6 passed |
| `auth` | 12 passed |
| `billing` | 10 passed |
| `billing-ledger` | 11 passed |
| `billing-statements` | 9 passed |
| `board` | 15 passed |
| `calendar` | 12 passed |
| `clinical` | 9 passed |
| `clinical-companion` | 9 passed |
| `completion` | 11 passed |
| `completion-adoption` | 8 passed |
| `diagnoses` | 12 passed |
| `diagnoses-structure` | 12 passed |
| `documents` | 10 passed - **FAILED**: 1 failed;   3 did not run;   10 passed (17.5s) |
| `documents-library` | 12 passed |
| `flow` | 10 passed |
| `forms` | 10 passed |
| `odontogram` | 11 passed |
| `odontogram-longitudinal` | 11 passed |
| `patients` | 7 passed |
| `perio` | 10 passed |
| `perio-sessions` | 12 passed |
| `prescription-workflow` | 15 passed |
| `prescriptions` | 10 passed |
| `procedures` | 8 passed |
| `safety` | 11 passed |
| `schedule` | 10 passed |
| `treatment-plans` | 11 passed |
| `treatment-plans-complete` | 11 passed |
| `workspace` | 14 passed |

The new `documents-library` walkthrough passes 12 of 12 on this code. On this code it does not scan the storage page in its missing-file state (that scan was added in the abandoned `f3eb57b`), so the "Open the patient's documents" link on that page has **not** been contrast-checked and is likely to have the same defect as the failing link.

### Backend test classes (counts from the full run's results file)

DocumentLibrarySchemaTests 46, DocumentContextServiceTests 28, DocumentLibraryApiTests 29, DocumentIntegrityServiceTests 16, DocumentStorageApiTests 22, DocumentOrphanServiceTests 14, DocumentLibraryFilterTests 24, DocumentPathSafetyTests 61, DocumentBackupRestoreTests 5, DocumentPermissionTests 17, SignedFormSnapshotTests 12. STORY-010's backend classes (DocumentSchemaTests 53, DocumentApiTests 37, DocumentImportServiceTests 44, DocumentLibraryServiceTests 21, DocumentSettingsServiceTests 40) were not edited. Frontend: DocumentLibraryFeatures.test.tsx 24 and DocumentStoragePage.test.tsx 16 are new; STORY-010's `DocumentsPanel.test.tsx` and `DocumentSettingsPage.test.tsx` were moved onto a shared harness (see PARENT_REGRESSION.md).

## Leak counts (`artifacts/leak/`)

| | before | after the full backend run |
|---|---|---|
| user databases | 1 | 1 |
| `AlveraTest_*` / `AlveraRestore_*` / `AlveraWalk_*` / `alveara_*` databases | 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 |
| SQL logins | 0 | 0 |
| entries in `Test-Data\Storage` | 3,641 | **3,914** |

**The storage-folder count is not flat on this code.** The full run left 273 more folders (the document tests, and the API tests through one shared `Storage\app` folder, leave their storage behind; about 3,500 folders since STORY-010). A fix (`5983636`) exists in history and is deliberately not part of this attempt. Databases and logins are flat. Nothing on disk was deleted.

## Disclosures

- **The attempt's code was chosen by the owner's instruction, not by the sweep.** The two defects above (a failing parent walkthrough and a leaking test suite) were known and fixed on another tree; this attempt carries the tree without the fixes.
- Three local commits (`20d4926`, `4ed2d14`, `be29f3b`) carry a `Co-Authored-By` trailer against the owner's standing instruction; they are not rewritten (a rewrite would change the commit the full run recorded). Unpushed.
- The Stage 1 decision for R01 (`APPROVED_PENDING_REGRESSION`) was for `f3eb57b`, a different tree; it is not claimed for this attempt.
- No `.trx` is packaged (7 MB); `backend-tests.csv` lists every test, outcome and duration. The raw mutation-script output is not packaged.
- The walkthrough screenshots are those committed at `be29f3b`, i.e. taken on this UI.
