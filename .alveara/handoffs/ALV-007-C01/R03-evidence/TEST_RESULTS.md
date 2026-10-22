# Test results - ALV-007-C01 R03 (Stage 1, no source change)

**Cadence classification: HIGH RISK (money/billing, concurrency primitive).** A Stage 1 package: the formal Stage 2 is `PENDING`; no full run was made for R03. **No source or test file changed** since the failed shared run: `git diff --name-only ba51d2e HEAD -- src/Alveara.Api src/Alveara.Api.Tests` is empty; the correction (`37d2b08`) is unchanged.

## Evidence that still applies, and why
| Evidence | Where | Why it still holds |
|---|---|---|
| Five ledger read-consistency tests (4 of 5 fail on the old loader, all 5 pass on the fix); ledger-focused 409 of 409; descendants 1,379 of 1,379; assessment of the eight `REVALIDATION_REQUIRED` descendants | `../R02-evidence/` | backend and tests byte-identical |
| Failed shared run: backend 4,324 of 4,324 in 34.2 minutes, the 5 ledger tests and `D5_financial_history...` passed, baseline ids and outcomes preserved; one failed line (mocked Playwright) | `../R02-regression.md`, `../../ALV-N016/R03-regression-evidence/` | preserved unchanged; **not claimed as final approval** |
| `ALV-CONTROL-C01` approved and `COMPLETE`: the Command Center correction | `.alveara/reviews/ALV-CONTROL-C01/R01.md` | the corrected frontend the shared Stage 2 now runs against |

## How the shared-regression evidence is revalidated (the frozen plan)

**What changed since the failed shared Stage 2 (run commit `ba51d2e`):** only the Command Center, by the approved and `COMPLETE` item `ALV-CONTROL-C01` (implementation `69c6f7f`, `.alveara/reviews/ALV-CONTROL-C01/R01.md`): `assets/status-labels.js`, `assets/map.js`, `assets/app.js`, `assets/styles.css`, `index.html`, `tests/status-labels.test.mjs`, and the three e2e specs under `src/alveara-client/e2e/`, plus control records. **No file under `src/Alveara.Api` or `src/Alveara.Api.Tests` differs** (`git diff --name-only ba51d2e HEAD -- src/Alveara.Api src/Alveara.Api.Tests` is empty), so the backend, the template infrastructure and the billing correction are byte-identical to what the failed run measured.

**The frozen plan (default, "Option B"): one new physical shared full run**, exactly as the failed one, on the clean closure tree of this attempt and of the sibling attempt (both Stage 1 closure-only commits first):

```sh
sh .alveara/handoffs/ALV-N016/R04-evidence/stage2/run_stage2.sh 69c6f7fe0c4155338b8d752804878d44784348a6 <out-folder>
```

Raw outputs kept unchanged; **two independent evaluators**, `evaluate_stage2.py` (ALV-N016: 4,324 backend tests in 55 minutes or less, template use, the 5 ledger tests and D5, cleanup, plus the Command Center files equal to the approved `69c6f7f`, and the client counts now **Playwright 140 passed / 0 failed, repository checks 13 passed, Vitest 1,544 in 97 files**) and `evaluate_stage2_alv007c01.py` (ALV-007-C01: the same run, its own criteria); additive regression packages for both attempts naming the same run commit and output set; the failed R03/R02 Stage 2 is **not** claimed as final approval for either. Both evaluators were smoke-tested against the failed run's output: both run and both fail it (the old Playwright count, the old repository count).

**Requested alternative ("Option A", only if the reviewer approves it in writing): revalidate by equivalence plus a fresh frontend gate.** Because the backend and its tests are provably unchanged and the failed run's only failed line was the Playwright gate, the 34.2-minute backend result of the preserved run would be re-used for both attempts, and the Command Center correction would be re-qualified by a fresh frontend gate (ALV-CONTROL-C01's `run_frontend_gate.sh` and `evaluate_frontend_gate.py`) on the closure tree, with `stage2/evaluate_reuse.py` deciding the equivalence (preserved run had exactly one failed line, the Playwright gate; no backend or backend-test file differs; nothing outside the control records and the Command Center client differs; clean tree). It saves about 40 minutes of machine time and removes one chance of a random failure, **but it relies on a failed Stage 2 as evidence**, which the reviewer's R03 decision said must not be claimed as final approval, so it is offered as a question, not assumed. Default is Option B.

This attempt is judged by `stage2/evaluate_stage2_alv007c01.py` (ALV-007-C01 criteria: the same 4,324-test run, the 5 ledger tests and D5, template safety, cleanup, the updated client counts and the Command Center equivalence), independently of ALV-N016's evaluator, on the **one** physical run both attempts share. The tooling lives in `.alveara/handoffs/ALV-N016/R04-evidence/stage2/` (one run, two evaluators, as the reviewer directed).

## The eight descendants
They stay `REVALIDATION_REQUIRED`. Their targeted evidence (409, 1,379) and the shared run's full backend (4,324) support restoration; the reviewer's final review of the corrected shared Stage 2 decides it.

## Disclosures
No full run was made for this package. Three older ALV-010-C01 commits carry a `Co-Authored-By` trailer against the owner's instruction (not rewritten); none of this story's commits has one.
