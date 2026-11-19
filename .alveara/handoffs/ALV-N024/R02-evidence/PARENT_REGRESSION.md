# ALV-N024 R02: parent and dependency regression

- **Parent:** n/a (a new production story). Broad regression is `DEFERRED_TO_ALV-N028`; not release-certified.
- **Dependency for these corrections:** every class that exercises the payer rule 51142 or payer eligibility (90/90), the billing panel component tests (31/31) and the billing-integrity spec (7/7). The R01 dependency evidence (338/338) stands for the unchanged areas.
- **Not run:** the full suite, the Stage 1 sweep and Stage 2 (as the R01 review instructed).
