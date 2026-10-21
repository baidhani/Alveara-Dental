# Test results - ALV-010-C01 R01

**Cadence classification: HIGH RISK** under the risk-based regression cadence (Prompt Rule 16): the story adds a migration (`AddDocumentLibrary`) and changes backup/restore validation, a permission, and shared test infrastructure. The full regression is therefore mandatory with this handoff and was run. This is a single-stage handoff (the two-stage protocol of schema v3 applies to stories after this one).

Real SQL Server (`.\SQLEXPRESS`, data under `C:\.alvera-data\Test-Data`) for backend and real-backend runs. Raw output is under `artifacts/`.

## Which commit each run used (read this first)

| Run | Commit it ran on |
|---|---|
| Full serial backend | `be29f3bae2d2eb8f1d8adfa09fc10684f3df12bf` (`be29f3b`) |
| Sweep, first pass: client gates and 29 walkthroughs | `598363615796b9033bdfbad7009ce3530632e2c7` (`5983636`) |
| Reruns after two fixes: tsc, vitest, build, lint, mocked Playwright, the STORY-010 documents walkthrough, the library walkthrough | the working tree that became the implementation commit `f3eb57b9b6548f7e8fd800269011ff75ccb6e628` (`f3eb57b`) |
| Filtered backend runs after the test-storage cleanup | `598363615796b9033bdfbad7009ce3530632e2c7` and the same tree |

Commits after `be29f3b` changed **test infrastructure and UI link styling only** (`5983636`: test storage cleanup; `3bd8ede`: one story-catalog entry; `f3eb57b`: two link classes, a CSS rule, one more axe scan). The full backend suite was not repeated on `f3eb57b`; the document suite (702) and the 29 API-harness classes (639) were re-run after the cleanup changes and passed, and no non-test backend file changed after `be29f3b`.

## Results

| Suite | Command | Result |
|---|---|---|
| Backend, full serial | `dotnet test` (Alveara.Api.Tests) | **4,294 of 4,294 passed**, 0 failed, 0 skipped, 2 h 41 m, on `be29f3b` |
| Backend, document classes after cleanup | filter `Document` | **702 of 702 passed** |
| Backend, API-harness classes after cleanup | filter of 29 classes | **639 of 639 passed** |
| Frontend, full | `npx vitest run` | **1,544 of 1,544 passed** (97 files) |
| Type check, build, lint | `npx tsc -b`, `npm run build`, `npm run lint` | exit 0, built, exit 0 (28 warnings of the existing kinds) |
| Mocked browser | `npx playwright test` | **136 of 136 passed** (first pass 134 + 2 failed: catalog lacked ALV-N015, since fixed) |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8 passed** |
| Real-backend walkthroughs, every config in the repository, each on a fresh database | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config ...` | **323 of 323 passed across 30 walkthroughs** (table below) |
| Negative controls | scripted breakages | **62 breakages, 60 killed, 2 documented non-gaps** (`artifacts/negative-controls/negative-controls.txt`) |

### Real-backend walkthroughs (final result of each)

| Walkthrough | Result |
|---|---|
| `gate-c_c2` | 6 of 6 |
| `auth` | 12 of 12 |
| `billing` | 10 of 10 |
| `billing-ledger` | 11 of 11 |
| `billing-statements` | 9 of 9 |
| `board` | 15 of 15 |
| `calendar` | 12 of 12 |
| `clinical` | 9 of 9 |
| `clinical-companion` | 9 of 9 |
| `completion` | 11 of 11 |
| `completion-adoption` | 8 of 8 |
| `diagnoses` | 12 of 12 |
| `diagnoses-structure` | 12 of 12 |
| `documents` | 14 of 14 |
| `documents-library` | 12 of 12 |
| `flow` | 10 of 10 |
| `forms` | 10 of 10 |
| `odontogram` | 11 of 11 |
| `odontogram-longitudinal` | 11 of 11 |
| `patients` | 7 of 7 |
| `perio` | 10 of 10 |
| `perio-sessions` | 12 of 12 |
| `prescription-workflow` | 15 of 15 |
| `prescriptions` | 10 of 10 |
| `procedures` | 8 of 8 |
| `safety` | 11 of 11 |
| `schedule` | 10 of 10 |
| `treatment-plans` | 11 of 11 |
| `treatment-plans-complete` | 11 of 11 |
| `workspace` | 14 of 14 |

`documents` (STORY-010's, spec unchanged) failed its first pass on a dark-mode contrast violation in the "Manage form templates" link added by this story (10 passed, 1 failed, 3 did not run); fixed and rerun 14 of 14. The new `documents-library` walkthrough is 12 of 12 with axe scans (light and dark) of the library list, the thumbnail view and the storage page (intact and missing-file states): 0 critical or serious.

### New and changed backend test classes (counts from the full run's results file)

DocumentLibrarySchemaTests 46, DocumentContextServiceTests 28, DocumentLibraryApiTests 29, DocumentIntegrityServiceTests 16, DocumentStorageApiTests 22, DocumentOrphanServiceTests 14, DocumentLibraryFilterTests 24, DocumentPathSafetyTests 61, DocumentBackupRestoreTests 5, DocumentPermissionTests 17 (cases added), SignedFormSnapshotTests 12 (one added). After the full run: DocumentTestStorageCleanupTests 3 and ApiHarnessStorageCleanupTests 3 (in the 702 and 639 runs above). STORY-010's backend classes (DocumentSchemaTests 53, DocumentApiTests 37, DocumentImportServiceTests 44, DocumentLibraryServiceTests 21, DocumentSettingsServiceTests 40) were not edited.

### New and changed frontend tests

DocumentLibraryFeatures.test.tsx 24, DocumentStoragePage.test.tsx 16 (new); DocumentsPanel.test.tsx and DocumentSettingsPage.test.tsx (STORY-010's) were moved onto a shared test harness and the new filter form and upload; see PARENT_REGRESSION.md.

## Leak counts (`artifacts/leak/`)

| | before | after the full backend run | final |
|---|---|---|---|
| user databases (SQL Server) | 1 | 1 | 1 |
| `AlveraTest_*` / `AlveraRestore_*` / `AlveraWalk_*` / `alveara_*` databases | 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 |
| SQL logins (other than sa) | 0 | 0 | 0 |
| entries in `Test-Data\Storage` | 3,641 | 3,914 | 3,920 |

The storage folder **grew by 273 during the full run**: the document tests (and the API tests, through one shared `Storage\app` folder) left their storage behind, 3,500 folders since STORY-010. That was a leak in this story's and STORY-010's test classes; it is fixed (`5983636`) and the count was **flat (3,920) across every run after the fix**, including the 702-test and 639-test filtered runs and the whole walkthrough sweep. The folders already on disk were **not deleted** (they are listed, and removing them is the owner's call).

## Failure and rework recorded honestly

- Full backend run: no failure.
- First pass of the client gates: the mocked-browser story-catalog test failed because ALV-N015 (added to the ledger before this story began) had no catalog entry; this predates the story and is fixed by one entry.
- First pass of the walkthroughs: the STORY-010 documents walkthrough failed on a dark-mode contrast violation of a link this story added; fixed.
- The walkthroughs written for this story found three real defects before the sweep (empty encounter picker for the office manager; the previous file's tooth kept for the next import; card links failing dark-mode contrast); fixed, with tests.

## Disclosures

- **Commit trailers:** three local commits (`20d4926`, `4ed2d14`, `be29f3b`) carry a `Co-Authored-By` trailer that the owner's standing instruction forbids. The owner decided not to rewrite them (a rewrite would change the commit the long full run recorded). They are unpushed.
- The full backend run was not repeated on the final commit (see the table at the top).
- No `.trx` is packaged (7 MB); `backend-tests.csv` lists every test, outcome and duration.
- The raw mutation-script output is not packaged; the record is the per-step PROGRESS.md entries summarised in `artifacts/negative-controls/negative-controls.txt`.
- The full backend run and the sweep ran one after another with nothing else running; the filtered reruns ran back to back.
