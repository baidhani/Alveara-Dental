# ALV-N020 R01: parent and dependency regression

- **Parent course story:** none (`parentRegression: "n/a"`).
- **Dependency:** ALV-N019 (COMPLETE, R07 approved, closure `72d2469`, pushed). Its stage-0 chain steps ran again inside every ALV-N020 chain run; run 3 passed all of them (S-09a-d, C-01, B-01, B-02, E10, CLEAN).
- **Earlier behavior deliberately changed:** two ALV-N009 route-guard tests now expect the dashboard after a first-visit deep link (the F-037 guardrail: no return destination for anyone but the same user after an expiry). The ALV-N009 refresh sequence guard and fixed expiry are unchanged (their tests pass in re-run B).
- **Broad regression:** `DEFERRED_TO_ALV-N028`; obligations in `.alveara/PROGRAM_REGRESSION_LEDGER.md`.
