# ALV-N026 R03: acceptance evidence

The mapping of review R01's four items to plan item 63's criteria is unchanged from `R02-evidence/ACCEPTANCE_EVIDENCE.md`. Status of each item:

| Review item | Criterion | Proof run | Status |
|---|---|---|---|
| 1. F-107 PDF date of birth | 2 | P1, 64/64 on the R02 tree (includes the theory) | test passed; it is rerun as part of the restarted R03 proof |
| 2. Real-browser server-PDF print | 2 | W1 | **not proven**: R02's W1 stopped on a test-helper defect, corrected in R03 and not yet run |
| 3. Real-browser missing file | 1, 4 | W2 | not run yet |
| 4. Signed forms, integrity and visibility | 1 | P2, 119/119 on the R02 tree (includes the FormsApiTests override case); W3 and W4 | server part passed; browser part not run yet |

U-018, U-019 and U-021, and every R01 limitation, stay open as declared.
