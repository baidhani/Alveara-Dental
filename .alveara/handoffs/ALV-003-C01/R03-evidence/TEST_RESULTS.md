# Test results - ALV-003-C01 R03 (Stage 1, test-only corrective)

**Cadence classification: LOW RISK** (one client unit test; no product, backend, schema, auth, money or build-configuration change). A focused Stage 1 package; **no backend run** (the reviewer's direction) and no full broad run before approval. After approval one **shared frontend-only gate** runs once on the frozen closure tree (below).

Implementation `30e2f69df452e85cac7dab06e907c5c9325cfc09`: one file changed, `src/alveara-client/src/pages/PatientRegistrationPage.test.tsx`.

## Why the test failed (root cause, found by reproducing it)
The reviewer's reading was that the post-`user.type` assertion was asserted too early. Reproducing it showed the race is one step earlier. A refused submit makes the page call `requestAnimationFrame` to move focus to the first invalid field (`focusFirstInvalid`, `PatientRegistrationPage.tsx`). The old test then did `user.type(City, "A")`, which is a click on City followed by a keystroke. If the focus frame runs **between** that click and that keystroke, focus leaves City, the "A" goes into First name, City's prompt (rightly) stays, and the assertion fails with exactly the message of the formal run (`expected document not to contain element, found <p ...>City is required.</p>`). A person cannot type before the focus has moved; a test can. Waiting only after typing would not fix it (the letter would already be in the wrong field), so the corrected test first waits for the visible transition (focus has reached the first invalid field) and then types, then waits for the prompt to clear.

## Negative control: the previous assertion can race (reviewer's requirement)
`artifacts/negative_control.test.tsx` runs the **previous** test body with the focus frame forced to land between the click and the keystroke: it **fails every time** with the formal run's message (`artifacts/negative_control_previous_assertion.txt`: 1 failed, "expected document not to contain element, found City is required."). Natural timing alone did not reproduce it on this machine in 14 delayed-frame trials, so the race is demonstrated by forcing the interleaving, not by luck. The excerpt of the formal run's own failure is `artifacts/failing-formal-run-excerpt.txt`.

## Results on the committed implementation (`30e2f69`)
| Command (from `src/alveara-client` unless stated) | Result |
|---|---|
| `npx vitest run src/pages/PatientRegistrationPage.test.tsx` | 10 passed (9 before + 1 new) |
| the same file 30 times in a row | **30 passed, 0 failed** (`artifacts/isolated_runs.txt`) |
| the corrected test and the new late-frame test (frame delayed 80 ms) | pass; the late-frame test would type into First name without the focus wait |
| `npx vitest run` (the full client suite) | **1,545 passed in 97 files** (1,544 before + the new test), 162 s, exit 0 (`artifacts/vitest_full.txt`) |
| `npx tsc --noEmit` | exit 0 |
| `npm run lint` | exit 0 (existing warnings unchanged) |
| `node --test tests/*.test.mjs` (repository root) | 13 passed |

## What changed and what did not
- Changed: only the test file. The corrected test waits for focus to reach First name before typing, asserts City holds "A", waits for the prompt to clear, and checks the other required prompts remain (only the field being fixed was cleared). A second test repeats it with the focus frame delayed 80 ms. The file's `afterEach` now also restores spies so the delayed-frame stub cannot leak into other tests.
- Not changed: `PatientRegistrationPage.tsx`, any product file, any backend file. `git diff --name-only 69c6f7f 30e2f69 -- src assets tests index.html` lists exactly the one test file.

## Frozen shared frontend-only gate (run once after the Stage 1 approvals and closure-only commits)
Files: `stage2/` in this folder. It serves ALV-003-C01 R03 and the pair (`ALV-N016` R04, `ALV-007-C01` R03), which share one run as the reviewer directed. On the clean closure tree, in this order:
```sh
sh .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/run_frontend_gate.sh 30e2f69df452e85cac7dab06e907c5c9325cfc09 <frontend-output>
python .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/evaluate_frontend_gate.py <frontend-output> 30e2f69df452e85cac7dab06e907c5c9325cfc09
python .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/evaluate_reuse.py ALV-N016 <closure-commit>
python .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/evaluate_reuse.py ALV-007-C01 <closure-commit>
```
All commands exit zero; **Vitest 1,545 tests in 97 files, Playwright 140 passed / 0 failed, repository checks 13 passed**; tree clean before and after; since the approved ALV-CONTROL-C01 implementation `69c6f7f` exactly one source file changed (the registration test); both reuse evaluations report `REUSE EQUIVALENCE HOLDS` (no backend or backend-test file differs from the preserved 4,324-test run `ba51d2e`). No backend run. A failed line means `CHANGES_REQUIRED`, and per the reviewer an unchanged rerun is not authorized.

## Disclosures
- The new test count (1,545) changes the frontend gate's expected Vitest count; the frozen evaluator here expects 1,545 (the ALV-CONTROL-C01 evaluator expected 1,544 and is not reused for this run).
- Other tests in the client suite use the same "click submit, then type" shape; none failed in the formal run and none is changed here (narrow scope). The same hazard could exist wherever a form moves focus in `requestAnimationFrame`; this is reported, not fixed.
- Three older ALV-010-C01 commits carry a `Co-Authored-By` trailer against the owner's standing instruction; not rewritten. This item's commits have none.
