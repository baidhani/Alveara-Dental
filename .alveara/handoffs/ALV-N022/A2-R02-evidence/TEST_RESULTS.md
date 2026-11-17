# ALV-N022 A2-R02: test results (Stage 1 correction; 2026-11-17)

Implementation `66c92b9`. Windows 11, local SQL Server, Chromium. Each command under five minutes.

| Run | Result | Duration | Output |
|---|---|---|---|
| `dotnet test --filter` DiagnosisServiceTests, DiagnosisApiTests, DiagnosisSchemaTests, DiagnosisRulesTests, DiagnosisReconciliationTests, DiagnosisStructureServiceTests, DiagnosisStructureApiTests, DiagnosisStructureSchemaTests, DiagnosisStructureRulesTests, DiagnosisFinalizedEncounterGuardTests, DiagnosisFinalizedGuardMigrationTests, RealMigrationInventoryTests | 265 / 266, then the pinned count updated and `RealMigrationInventoryTests` re-run **5 / 5** -> **266 / 266** | 2 m 6 s; 0.4 s | `artifacts/backend-focused-tests.csv` |
| `Invoke-RealBackendWalkthrough.ps1 -Config playwright.finalized-encounter.config.ts` (fresh build) | **1 / 1** | 27 s | `artifacts/walkthrough/` |
| Negative controls G1, G2 | both caught | 83 s, 82 s | `artifacts/negative-controls/` |

Per class: DiagnosisApiTests 28, DiagnosisFinalizedEncounterGuardTests 3, DiagnosisFinalizedGuardMigrationTests 2, DiagnosisReconciliationTests 10, DiagnosisRulesTests 51, DiagnosisSchemaTests 28, DiagnosisServiceTests 39, DiagnosisStructureApiTests 15, DiagnosisStructureRulesTests 46, DiagnosisStructureSchemaTests 19, DiagnosisStructureServiceTests 20, RealMigrationInventoryTests 5.

## Negative controls (one at a time; restored from a byte backup; SHA-256 compared; each run builds its own template from the real migrations)
| # | Planted break | Test | Result |
|---|---|---|---|
| G1 | trigger condition `WHERE e.[Status] = N'Finalized'` -> `WHERE 1 = 0` | direct-insert test | caught (1 of 1 failed) |
| G2 | the 51287 catch matches nothing | interleaving test | caught (1 of 1 failed: the caller got a generic save failure, not `encounter_finalized`) |

A2-R01's results (completion, adoption and amendment classes 125/125; client 15/15) stand: no file they cover changed in R02.

## Not run
Stage 2 (reviewer-defined), full suites, mocked Playwright, other walkthroughs, chain (`DEFERRED_TO_ALV-N028`).
