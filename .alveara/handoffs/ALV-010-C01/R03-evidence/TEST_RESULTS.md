# Test results - ALV-010-C01 R03 (Stage 1)

**Cadence classification: HIGH RISK** (a migration, restore validation, a permission, shared test infrastructure). The broad regression is required. **Under the two-stage policy this is a Stage 1 package: the full serial backend regression is `PENDING` by design and has not been run on this tree.** Nothing below claims it passed. The Stage 2 contract is frozen at the end of this file.

## What this attempt is

`R02` (implementation `be29f3b`-equivalent `17a2f07`) was returned `CHANGES_REQUIRED` on two findings (`.alveara/reviews/ALV-010-C01/R02.md`): R02-01 (the unchanged STORY-010 documents walkthrough fails on the dark-mode contrast of the "Manage form templates" link) and R02-02 (the test tree leaks storage folders). R03 restores the proven corrections: `src/` of implementation `a3b4bc92db59043a31ba089cc31dbe2fd4ce25ac` (`a3b4bc9`) has **no difference** from `f3eb57b` (`git diff f3eb57b a3b4bc9 -- src` is empty), the tree the R01 Stage 1 review approved. No new code was written.

## Fresh runs on this tree (the evidence the reviewer asked for)

Raw output is in `artifacts/focused-run/`. The working tree was clean when they started; at the end only the two generated, untracked walkthrough output folders existed, and they were removed (`artifacts/focused-run/summary.txt`, "tree-changes"). An earlier attempt at this run was discarded because a stopped script kept running and overlapped the next one; the run recorded here was started alone on a clean tree.

| Finding / purpose | Command | Result |
|---|---|---|
| R02-02: the cleanup is tested, and the storage folders do not grow | `dotnet test --filter` of `ApiHarnessStorageCleanupTests` (3), `DocumentTestStorageCleanupTests` (3), `DocumentStorageApiTests` (22), `DocumentBackupRestoreTests` (5), `DocumentOrphanServiceTests` (14) | **47 of 47 passed**, 0 failed, 0 skipped (2 m 54 s) |
| R02-02: before/after leak count for that run | `Test-Data\Storage` entry count | **3920 before, 3920 after the focused backend run, 3920 after everything: flat**; databases and SQL logins unchanged (`artifacts/focused-run/leak_*.txt`) |
| R02-01: STORY-010's walkthrough repaired | `playwright.documents.config.ts` on a fresh database (spec **unchanged**) | **14 of 14 passed**, all steps run, including the failure path, audit/history and database-immutability steps |
| R02-02: accessibility of the storage page's patient-documents link in its missing-file state | `playwright.documents-library.config.ts` (the spec scans the storage page, intact and missing, in light and dark) | **12 of 12 passed**, 0 critical or serious |
| Client types and documents screens | `npx tsc --noEmit`; `npx vitest run src/pages/documents` | tsc exit 0; **91 of 91 passed** |

## Earlier results on the identical tree (not repeated here)

`src/` equals `f3eb57b`'s, on which these ran (transcribed, see `artifacts/test-runs/earlier-reruns-on-the-identical-tree.txt`, and `R01-evidence/TEST_RESULTS.md` for the whole picture): the document classes 702 of 702 and the 29 API-harness classes 639 of 639 with the storage folder count unchanged; the whole client suite 1,544 of 1,544, `tsc`, build and lint exit 0, the mocked browser run 136 of 136, `node --test` 8 of 8; the other 28 real-backend walkthroughs (first sweep pass) all passing; negative controls 62 breakages, 60 killed, 2 documented non-gaps. The parent's backend classes (`DocumentSchemaTests` 53, `DocumentApiTests` 37, `DocumentImportServiceTests` 44, `DocumentLibraryServiceTests` 21, `DocumentSettingsServiceTests` 40) are inside that 702 and were not repeated on a3b4bc9.

## Broad regression: PENDING

The 4,294-test full serial backend run (2 h 41 m) was made on `be29f3b`, whose `src/` differs from this tree in the test-storage cleanup and the link styling. **It is historical evidence only and is not claimed as Stage 2 evidence.** The reviewer directed that no full run be made before Stage 1 approval.

## Frozen Stage 2 contract

To be run **once**, after `APPROVED_PENDING_REGRESSION`, on the closure-only Stage 1 commit, with nothing else running on the machine and no change between the closure and the run (any source, test, migration, script, configuration or general documentation change voids the approval).

1. Record before: the `Test-Data\Storage` entry count, user databases and SQL logins.
2. `Set-Location src\Alveara.Api.Tests; dotnet test` (adding only `--logger "trx;LogFileName=full_serial.trx" --results-directory <folder>` to keep the raw results). **Expected: 4,300 discovered (the 4,294 of the earlier run plus the 6 cleanup tests), 0 failed, 0 skipped.** Record the run commit, command, elapsed time, counts and the retained raw summary.
3. In `src\alveara-client`: `npx tsc --noEmit`, `npx vitest run` (expected 1,544 passed in 97 files), `npm run build`, `npm run lint` (exit 0), `npx playwright test` (expected 136 passed), and in the repository root `node --test tests/*.test.mjs` (expected 8 passed).
4. Record after: the same counts as step 1. **Expected: the `Test-Data\Storage` count is unchanged** (the cleanup's claim), databases and logins unchanged.
5. The Stage 2 evidence passes only with every expectation met, unless an exception is approved before packaging.

## Disclosures

- Three local commits (`20d4926`, `4ed2d14`, `be29f3b`) carry a `Co-Authored-By` trailer against the owner's standing instruction; they are not rewritten (a rewrite would change the commit the historical full run recorded). Unpushed.
- The parent's backend classes and the full client suite were not re-run on a3b4bc9; they ran on the identical tree, as stated above.
- The `.trx` of the historical full run (7 MB) is kept on the machine and is not packaged; the raw mutation output is not packaged.
- Two test databases (`AlveraTest_*`) were left by two earlier interrupted runs; they are not touched by this attempt and are not deleted.
