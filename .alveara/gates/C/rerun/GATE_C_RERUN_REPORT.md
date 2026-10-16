# Alveara Dental - Gate C (Clinical Core Ready) - Rerun Report

**LATE EVALUATION, RERUN after the independent review of the initial evaluation.** Gate C is a late evaluation after items 31-38 (see `docs/design/GATE_C_SEQUENCE_REVIEW_RESPONSE.md`). The initial evaluation (head `f455c2e`, evidence `eeb493b`, package `.alveara/dist/Alveara_Gate_C_eeb493b.zip`, report `.alveara/gates/C/GATE_C_REPORT.md`) was returned **BLOCKED / CHANGES_REQUIRED** by `.alveara/gates/C/reviews/eeb493b.md` on one finding. The initial report, evidence, package and review are preserved unchanged.

**Decision: PASS** - the single finding is corrected; thresholds, methods and environments are the predeclared C1-C8 rows in `.alveara/QUALITY_GATES.md` and were not changed. Subject to independent review of this rerun.

## The finding and its correction

**C-REV-01 - Gate C did not execute the mandatory full backend regression on its evaluation tree.** The initial report reused the 3686/3686 result from ALV-016-C01 R02 (`486e44c`) although Gate C adds `GateCHistoryIntegrityTests.cs` to the backend test project. The testing-cadence rule requires the full regression at every quality gate; a story-level run cannot substitute for it. The reviewer accepted that no product defect, course-story state or story reopening is involved.

**Correction.** The declared full serial backend command was run on the Gate C integrated head, with the C8 tooling in the tree and no rebuild during the run:

| | |
|---|---|
| Head | `aa85bc2735dc3b9366f2c96c4678d9a750fde6a2` (clean tree; contains the Gate C tooling `45abf51`, the initial evidence `eeb493b`, the initial report and the review record) |
| Command | in `src/Alveara.Api.Tests`: `dotnet build --nologo`, then `dotnet test --nologo --no-build --logger "trx;LogFileName=gatec_rerun_full.trx"` (serial, real SQL Server LocalDB) |
| Result | **3690 passed / 0 failed / 0 skipped** (3686 + the 4 Gate C integrity tests), 1 h 29 m |
| Gate C tests in the total | all four `GateCHistoryIntegrityTests` pass inside the run: `C8_signed_notes_and_their_addenda`, `C8_diagnoses_and_odontogram_history`, `C8_finalized_periodontal_records_and_sessions`, `C8_treatment_plan_revisions_state_history_and_completed_procedures` (`evidence/backend/results-by-class.md`, `evidence/backend/gatec_rerun_full.trx.gz`) |
| Build | exit 0 (`evidence/backend/dotnet-build.txt`) |
| Repository checks | `node --test tests/*.test.mjs`: **8 of 8** (`evidence/repo-checks.txt`) |
| Leak counts after the run | unchanged: AlveraTest 54, AlveraRestore 59, `alveara_` logins 273, `alveara_` databases 248 |

Evidence paths are relative to `.alveara/gates/C/rerun/evidence/`. Evidence commit: `5f8bb271ebc488b39e0ddbab14a1e670139e52be`.

## Criterion results

The criterion evidence (C1-C8 walkthroughs, the C8 attempts and mutation check, the frontend gates) is unchanged and was accepted as credible by the review; it is not rerun. The fresh full backend run adds to it:

| # | Result | Basis |
|---|---|---|
| C1 | **PASS** | Initial evidence (walkthroughs 9/9 and 9/9, C8 addendum path) plus the fresh full backend run, which includes the encounter, note, signing and addendum test classes. |
| C2 | **PASS** | Initial evidence (safety 11/11, flagged-patient strip 6/6) plus the fresh run (safety context and clearance classes). |
| C3 | **PASS** | Initial evidence (odontogram 11/11 and 11/11) plus the fresh run (odontogram classes). |
| C4 | **PASS** | Initial evidence (perio 10/10 and 12/12) plus the fresh run (periodontal classes). |
| C5 | **PASS** | Initial evidence (diagnoses 12/12, diagnoses-structure 12/12, treatment-plans 11/11) plus the fresh run (diagnosis classes). |
| C6 | **PASS** | Initial evidence (procedures 8/8, treatment-plans 11/11) plus the fresh run, which now itself contains the catalog-refusal tests (`TreatmentPlanSchemaTests`) instead of reusing them. |
| C7 | **PASS** | Initial evidence (treatment-plans 11/11, treatment-plans-complete 11/11, revision immutability attempts) plus the fresh run (treatment-plan revision classes). |
| C8 | **PASS** | Initial evidence (51 direct attempts, 50 rejected by named triggers, tables unchanged, mutation check) plus the four C8 tests passing inside the fresh full run. |

## Disclosures

- **The frontend gates and real-backend walkthroughs were not rerun.** The review did not find them deficient, and no code changed after them (`45abf51` to `aa85bc2` adds only the initial report, the review record, a one-line `tsc.txt`, and documentation/status files). The fresh full backend run is the only new evidence, as the review's required correction states.
- **The signed-but-not-finalized note** observation stands as design debt, accepted by the review as not a Gate C blocker. No story is proposed for it here.
- **Tablet-width walkthroughs** were not run (no Gate C criterion requires them); unchanged from the initial report.
- Walkthrough screenshots are not in the packages; unchanged from the initial report.

## Reproduce

From `src/Alveara.Api.Tests`: `dotnet build --nologo`, then `dotnet test --nologo --no-build --logger "trx;LogFileName=gatec_rerun_full.trx"` (serial; do not rebuild during the run). From the repository root: `node --test tests/*.test.mjs`. The initial report's Reproduce section covers the frontend gates and walkthroughs.

## Repository state and next action

Rerun head `aa85bc2735dc3b9366f2c96c4678d9a750fde6a2`; product code under test `45abf514a2e26aa7330ad8e8900330602f92afb1` (unchanged); evidence `5f8bb27`; the rerun record is the commit that adds this report. **Gate C is PASS on this rerun, subject to independent review.** Gate D and item 39 (`STORY-010`) must not begin until this rerun is independently approved and the approval is durably recorded in a closure commit. Nothing has been pushed.
