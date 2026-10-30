# Gate E Stage 2 - frozen contract

Run once, after the Stage 1 review (`APPROVED_PENDING_REGRESSION`), on the Stage 1 closure commit, with a clean tree and nothing else running:

    sh .alveara/gates/E/stage2/run_stage2.sh 173051da01b1eababfb9bf10773283cfa3a6f74d <out-folder-outside-the-repository>

`evaluate_stage2.py` prints one PASS/FAIL line per criterion and `STAGE 2 QUALIFIES` or `STAGE 2 DOES NOT QUALIFY`; nothing else decides. `validate_evaluator.py` and `verify_runner_outputs.py` are the proofs that the evaluator and the runner do what is stated (`evaluator-validation.txt`, `runner-proof.txt`). The expected new and retired id files are empty on purpose: no product code changed since the ALV-N006 Stage 2 baseline (5,612 ids).
