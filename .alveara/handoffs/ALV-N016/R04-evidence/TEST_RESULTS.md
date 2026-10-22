# Test results - ALV-N016 R04 (Stage 1, no source change)

**Cadence classification: HIGH RISK** (test infrastructure). **A Stage 1 package: the formal Stage 2 is `PENDING`; no full run was made for R04.** Source under review: the same as R03 plus nothing: the backend and tests are byte-identical to the failed shared run's (`ba51d2e`); the Command Center correction is the separate approved item `ALV-CONTROL-C01` (`69c6f7f`).

## Evidence that still applies, and why
| Evidence | Where | Why it still holds on the current tree |
|---|---|---|
| Focused 318 of 318 (template tests, inventory guard, 13 rollback classes on clones, 6 real-path classes, 5 ledger tests), 0 fallbacks, 32 real-path fixtures, counts unchanged | `../R03-evidence/artifacts/focused-r03/` | `git diff --name-only ba51d2e HEAD -- src/Alveara.Api src/Alveara.Api.Tests` is empty |
| Failed shared run: backend 4,324 of 4,324 in 2,055 s (34.2 min), template and cleanup criteria all passed; one failed line (mocked Playwright) | `../R03-regression-evidence/`, `../R03-regression.md` | preserved unchanged; **not claimed as final approval** (reviewer's R03 decision) |
| ALV-CONTROL-C01 approved and `COMPLETE`: Command Center labels; mocked Playwright 140 passed / 0 failed; repository checks 13; Vitest 1,544 in 97 files; frontend gate qualified | `.alveara/reviews/ALV-CONTROL-C01/R01.md`, `.alveara/handoffs/ALV-CONTROL-C01/R01-regression.md` | this is the corrected frontend the shared Stage 2 now runs against |

## Fresh checks made for this package
- Backend and backend-test source equivalence to the failed run: **0 files differ** (command above).
- Both shared evaluators and the reuse evaluator run, and behave as designed against the failed run's output (they fail it for the old Playwright and repository counts; the reuse evaluator holds on the equivalence lines and fails only on a dirty tree during drafting).

## How the shared-regression evidence is revalidated (the frozen plan)

**What changed since the failed shared Stage 2 (run commit `ba51d2e`):** only the Command Center, by the approved and `COMPLETE` item `ALV-CONTROL-C01` (implementation `69c6f7f`, `.alveara/reviews/ALV-CONTROL-C01/R01.md`): `assets/status-labels.js`, `assets/map.js`, `assets/app.js`, `assets/styles.css`, `index.html`, `tests/status-labels.test.mjs`, and the three e2e specs under `src/alveara-client/e2e/`, plus control records. **No file under `src/Alveara.Api` or `src/Alveara.Api.Tests` differs** (`git diff --name-only ba51d2e HEAD -- src/Alveara.Api src/Alveara.Api.Tests` is empty), so the backend, the template infrastructure and the billing correction are byte-identical to what the failed run measured.

**The frozen plan (default, "Option B"): one new physical shared full run**, exactly as the failed one, on the clean closure tree of this attempt and of the sibling attempt (both Stage 1 closure-only commits first):

```sh
sh .alveara/handoffs/ALV-N016/R04-evidence/stage2/run_stage2.sh 69c6f7fe0c4155338b8d752804878d44784348a6 <out-folder>
```

Raw outputs kept unchanged; **two independent evaluators**, `evaluate_stage2.py` (ALV-N016: 4,324 backend tests in 55 minutes or less, template use, the 5 ledger tests and D5, cleanup, plus the Command Center files equal to the approved `69c6f7f`, and the client counts now **Playwright 140 passed / 0 failed, repository checks 13 passed, Vitest 1,544 in 97 files**) and `evaluate_stage2_alv007c01.py` (ALV-007-C01: the same run, its own criteria); additive regression packages for both attempts naming the same run commit and output set; the failed R03/R02 Stage 2 is **not** claimed as final approval for either. Both evaluators were smoke-tested against the failed run's output: both run and both fail it (the old Playwright count, the old repository count).

**Requested alternative ("Option A", only if the reviewer approves it in writing): revalidate by equivalence plus a fresh frontend gate.** Because the backend and its tests are provably unchanged and the failed run's only failed line was the Playwright gate, the 34.2-minute backend result of the preserved run would be re-used for both attempts, and the Command Center correction would be re-qualified by a fresh frontend gate (ALV-CONTROL-C01's `run_frontend_gate.sh` and `evaluate_frontend_gate.py`) on the closure tree, with `stage2/evaluate_reuse.py` deciding the equivalence (preserved run had exactly one failed line, the Playwright gate; no backend or backend-test file differs; nothing outside the control records and the Command Center client differs; clean tree). It saves about 40 minutes of machine time and removes one chance of a random failure, **but it relies on a failed Stage 2 as evidence**, which the reviewer's R03 decision said must not be claimed as final approval, so it is offered as a question, not assumed. Default is Option B.

## Disclosures
- No full run was made for this package; the earlier unplanned full run (ALV-N016 R02 package) and the failed shared run are history, neither claimed as final evidence.
- Three older ALV-010-C01 commits carry a `Co-Authored-By` trailer against the owner's instruction (not rewritten); none of this story's commits has one.
