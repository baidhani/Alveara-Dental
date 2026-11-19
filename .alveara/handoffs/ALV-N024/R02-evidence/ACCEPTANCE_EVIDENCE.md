# ALV-N024 R02: acceptance evidence (corrections)

Rows changed from R01's `R01-evidence/ACCEPTANCE_EVIDENCE.md`. Every other row stands as in R01.

| Id | Criterion | Server proof | Client / browser proof | Status |
|---|---|---|---|---|
| F-209, F-205 (PD-18, OQ-1), review CR-1 | One payer rule at every layer: the patient, or an ACTIVE registered guarantor, recorded household member or recorded-responsible party; anyone else (including an inactive related person) is refused for a payment and a correction | BillingPayerEligibilityTests (4, including R02); BillingDocumentReplacementMigrationTests R02 (51142, direct SQL, payments and corrections); control (b) from R01 | BillingLedgerPanel.test (list, default); spec PAYER | PROVEN |
| F-207, review CR-2 | Another billing or document action clears an older status line | - | BillingLedgerPanel.test "clears an older status line..." | PROVEN |
| F-195, review CR-2 | Two distinct explanatory sentences | - | BillingLedgerPanel.test "explains the account history and the timeline..." | PROVEN |
| F-196, review CR-2 | The checkbox keeps its own size beside its text, in a row, on the text line | - | spec PAYER geometry (`checkbox-geometry.json`); the CSS fix in R02 | PROVEN (the R01 fix was ineffective; fixed in R02) |
