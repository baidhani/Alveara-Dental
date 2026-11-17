# ALV-N022 A2-R01: test results (Stage 1 focused proof; 2026-11-17)

Implementation `6348276`; every run on that tree (the commit was made after the runs with no change in between; the verbose vitest re-run is on the committed tree). Windows 11, local SQL Server, Chromium.

| Run | Result | Duration | Output |
|---|---|---|---|
| `dotnet test --filter` DiagnosisServiceTests, DiagnosisApiTests, CompletedProcedureServiceTests, CompletedProcedureAdoptionServiceTests, CompletedProcedureAmendmentTests | **125 / 125** | 2 m 36 s | `artifacts/backend-focused-tests.csv` (from the trx) |
| `npx vitest run DiagnosisFormEncounters.test.tsx TreatmentPlanAdoptCompletion.test.tsx --reporter=verbose` | **15 / 15** | seconds | `artifacts/vitest-a2-selection.txt` |
| `Invoke-RealBackendWalkthrough.ps1 -Config playwright.finalized-encounter.config.ts` (fresh build, fresh database) | **1 / 1** | 30 s | `artifacts/walkthrough/` |
| Negative controls N1, N2 | both caught | 92 s, 84 s | `artifacts/negative-controls/` |

Per class: DiagnosisServiceTests 39, DiagnosisApiTests 28, CompletedProcedureServiceTests 19, CompletedProcedureAdoptionServiceTests 25, CompletedProcedureAmendmentTests 14.

## New or changed tests
- DiagnosisServiceTests: `A_diagnosis_on_a_finalized_encounter_is_refused_before_any_write_and_the_encounter_is_unchanged` (409, field, no diagnosis/version/audit, encounter status/row version/finalize stamp and history unchanged); `A_diagnosis_on_a_draft_encounter_is_recorded`; `A_retried_save_of_a_diagnosis_recorded_before_the_encounter_was_finalized_replays_it_and_writes_nothing` (and a new key is refused). Replaces the earlier allow-test.
- CompletedProcedureAdoptionServiceTests: `An_adoption_may_name_a_draft_or_a_finalized_encounter_and_the_encounter_is_never_changed` (Draft, Finalized).
- CompletedProcedureAmendmentTests: `A_correction_can_name_a_draft_encounter_and_does_not_change_it`.
- Client: `DiagnosisFormEncounters.test.tsx` (draft only + message + default; all finalized -> nothing to choose; no finalized -> no message); adoption picker "still offers both a draft and a finalized encounter, each with its status".
- Browser: `finalized-encounter-real-backend.spec.ts` (1 test).

## Negative controls (one at a time; restored from a byte backup; SHA-256 compared)
| # | Planted break | Test | Result |
|---|---|---|---|
| N1 | `if (encounterStatus == EncounterStatuses.Finalized)` -> `if (false)` in DiagnosisService | the refusal test | caught (1 of 1 failed) |
| N2 | completion context also refuses `encounter.Status == "Finalized"` | `A_draft_or_a_finalized_encounter_can_be_named...` | caught (Finalized case failed, Draft case passed) |

## Not run
Stage 2 (to be defined by the reviewer), full suites, mocked Playwright, other walkthroughs, chain (`DEFERRED_TO_ALV-N028`).
