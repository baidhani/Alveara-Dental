# ALV-003-C01 — Independent Review R03, Stage 1

**Decision:** `APPROVED_PENDING_REGRESSION`  
**Reviewed:** 2026-10-22  
**Handoff:** `Alveara_Handoff_ALV-003-C01_30e2f69_R03.zip`  
**Package SHA-256:** `D9931CF26655FB974A2DD293224022FC5975D97041B43A1D79F2B01D11861ECA`

## Basis for approval

1. The implementation changes only `PatientRegistrationPage.test.tsx`; no registration product, backend, API, schema, authorization, configuration, or build behavior changed.
2. The investigation identifies the actual race. The page’s refused-submit `requestAnimationFrame` can take focus between the test’s City click and its keystroke, placing the character in First name. Waiting only for the post-typing assertion would not address that interleaving.
3. The corrected test waits for the visible focus transition before typing, verifies the typed value reached City, then awaits the message transition. The additional delayed-frame case and mock cleanup directly cover the hazard.
4. The prior test body fails under a forced interleaving, while the corrected file passes 30 repeated runs and the full client suite passes 1,545 tests in 97 files. TypeScript, lint, and the 13 repository checks also pass.

## Required shared Stage 2

Make a closure-only control-record commit for this decision. Then run the frozen frontend gate **once** on the common clean closure tree:

```sh
sh .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/run_frontend_gate.sh 30e2f69df452e85cac7dab06e907c5c9325cfc09 <frontend-output>
python .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/evaluate_frontend_gate.py <frontend-output> 30e2f69df452e85cac7dab06e907c5c9325cfc09
python .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/evaluate_reuse.py ALV-N016 <closure-commit>
python .alveara/handoffs/ALV-003-C01/R03-evidence/stage2/evaluate_reuse.py ALV-007-C01 <closure-commit>
```

All three evaluators must qualify: Vitest **1,545 tests in 97 files**, mocked Playwright **140 passed / 0 failed**, repository checks **13 passed / 0 failed**, and both reuse evaluations `REUSE EQUIVALENCE HOLDS`. Do not run the backend suite.

## Attachment decision for the two blocked stories

The qualifying shared frontend evidence may be submitted as an **additive Stage 2 rerun attached to `ALV-N016 R04` and `ALV-007-C01 R03`**. Their Stage 1 reviews and their preserved backend-equivalence evidence remain valid; their only blocking line was this corrected frontend test. Do not create paperwork-only attempts `ALV-N016 R05` or `ALV-007-C01 R04`.

Submit three additive regression packages referencing the same run commit and raw output: one for ALV-003-C01 R03 and one each for ALV-N016 R04 and ALV-007-C01 R03. Final approval remains independent for each story.
