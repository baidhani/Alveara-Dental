# ALV-N024 R01: changed files

Base `09aacd9` (briefing review R02 stored, APPROVED for implementation) .. implementation `1c1184b` (the swept tree). Documents-only commits (addendum reviews, PROGRESS) are included in the range.

```
1c1184b ALV-N024: financial issuance and reconciliation integrity - one receipt per payment with replacement, payer eligibility, statement version 3 and reconciliation, billing screens
cfbbd2d ALV-N024: store addendum 02 review (APPROVED); record the Q-5ag carry-forward (no performed-by in billing) (documents only)
c7fb422 wip: ALV-N024 slice 6 part 2 - negative controls; control (e) concurrency tests check the invariants before the refusal code; addendum 02 (READCOMMITTEDLOCK necessity not claimed)
9f7811f wip: ALV-N024 slice 6 part 1 - billing-integrity real-backend spec (7/7); billing-ledger and billing-statements specs updated in source
e2ce256 wip: ALV-N024 slice 5 - billing screens: one receipt per payment and replacement, document status, version-3 statement view, payer list and default, responsibility and guarantor wording, field errors
c8ef075 wip: ALV-N024 slice 4 - payer eligibility (PD-18), responsibility nothing-changed, payment-method created flag, refunded-payment explanation; Q-5ag dropped by owner decision
c67f644 wip: ALV-N024 slice 3 - statement template version 3 (reversed-by, adjustment total), reconciliation identity, statement CSV as a live export
f34cd43 wip: ALV-N024 slice 2 - receipt service: one receipt per payment, replacement with a reason, per-payment lock before insert, list status, legacy notice
6641d81 ALV-N024: store addendum 01 review (APPROVED; documents only)
3912d22 ALV-N024: PROGRESS entry for slice 1 (documents only)
5e4f799 wip: ALV-N024 slice 1 - receipt replacement columns, per-payment receipt lock and guards 51294-51297, widened payer rule 51142; addendum 01 (direct-SQL race also ends with 51297)
```

```
 .alveara/reviews/ALV-N024/ADDENDUM-01.md           |    20 +
 .alveara/reviews/ALV-N024/ADDENDUM-02.md           |    23 +
 PROGRESS.md                                        |    43 +
 docs/design/ALV-N024_ADDENDUM_01.md                |    28 +
 docs/design/ALV-N024_ADDENDUM_02.md                |    52 +
 docs/design/ALV-N024_OWNER_DECISION_RECORD.md      |     4 +
 .../BillingDocumentBuilderTests.cs                 |     6 +-
 .../BillingDocumentReplacementMigrationTests.cs    |   210 +
 .../BillingDocumentReplacementTests.cs             |   130 +
 .../BillingDocumentServiceTests.cs                 |     8 +-
 .../BillingDocumentTemplateVersionServiceTests.cs  |    18 +-
 .../BillingPayerEligibilityTests.cs                |    85 +
 .../BillingReceiptConcurrencyTests.cs              |   154 +
 .../BillingReconciliationTests.cs                  |     5 +-
 .../BillingResponsibilityAndMethodAnswersTests.cs  |    72 +
 src/Alveara.Api.Tests/BillingStatementsApiTests.cs |     4 +-
 .../FinancialDocumentTemplateVersionTests.cs       |    43 +-
 .../OriginalFinancialDocumentBuilderV2.cs          |   172 +
 src/Alveara.Api.Tests/RealMigrationInventory.cs    |     1 +
 .../RealMigrationInventoryTests.cs                 |     2 +-
 .../Architecture/Billing/BillingLedgerMath.cs      |    10 +
 .../Architecture/Billing/BillingRules.cs           |    14 +-
 .../Billing/BillingService.Corrections.cs          |     4 +-
 .../Billing/BillingService.Documents.cs            |    84 +-
 .../Architecture/Billing/BillingService.Methods.cs |     6 +-
 .../Architecture/Billing/BillingService.Payers.cs  |    56 +
 .../Architecture/Billing/BillingService.Queries.cs |     7 +-
 .../Billing/BillingService.Receipts.cs             |   106 +
 .../Billing/BillingService.Responsibility.cs       |     6 +-
 .../Architecture/Billing/BillingService.cs         |     4 +-
 .../Architecture/Billing/FinancialDocument.cs      |     6 +
 .../Billing/FinancialDocumentBuilder.cs            |    38 +-
 .../Billing/FinancialDocumentContracts.cs          |    26 +-
 src/Alveara.Api/Controllers/BillingController.cs   |     5 +
 .../Controllers/BillingStatementsController.cs     |     8 +-
 src/Alveara.Api/Data/FinancialDocumentModel.cs     |     6 +
 ...ceiptReplacementAndPayerEligibility.Designer.cs | 10678 +++++++++++++++++++
 ...014_AddReceiptReplacementAndPayerEligibility.cs |   174 +
 .../Migrations/AlveraDbContextModelSnapshot.cs     |    18 +
 .../e2e/billing-integrity-real-backend.spec.ts     |   226 +
 .../e2e/billing-ledger-real-backend.spec.ts        |     6 +-
 .../e2e/billing-statements-real-backend.spec.ts    |     7 +-
 .../playwright.billing-integrity.config.ts         |    24 +
 src/alveara-client/playwright.config.ts            |     2 +-
 src/alveara-client/src/PatientHousehold.test.tsx   |     9 +
 .../src/pages/HouseholdGuarantorPanel.tsx          |    11 +-
 src/alveara-client/src/pages/billing/Billing.css   |     5 +
 .../src/pages/billing/BillingAdjustmentForm.tsx    |     8 +-
 .../src/pages/billing/BillingApplyForm.tsx         |     2 +-
 .../src/pages/billing/BillingCorrectForm.tsx       |    10 +-
 .../src/pages/billing/BillingDocumentView.tsx      |     5 +-
 .../src/pages/billing/BillingDocuments.tsx         |   134 +-
 .../src/pages/billing/BillingLedger.tsx            |     1 +
 .../src/pages/billing/BillingLedgerPanel.test.tsx  |    78 +-
 .../src/pages/billing/BillingMoveForm.tsx          |    12 +-
 .../src/pages/billing/BillingPanel.test.tsx        |     2 +-
 .../src/pages/billing/BillingPanel.tsx             |    26 +-
 .../src/pages/billing/BillingPaymentForm.tsx       |    35 +-
 .../src/pages/billing/BillingRefundForm.tsx        |    10 +-
 .../pages/billing/BillingResponsibilityPanel.tsx   |    21 +-
 .../src/pages/billing/BillingReverseForm.tsx       |     6 +-
 .../pages/billing/BillingStatementsPanel.test.tsx  |    61 +-
 .../src/pages/billing/BillingTimelineAging.tsx     |     1 +
 .../src/pages/billing/PaymentMethodsSettings.tsx   |     4 +-
 .../src/pages/billing/billingFailure.ts            |     7 +
 .../src/pages/billing/documentStatus.ts            |    13 +
 src/alveara-client/src/services/billingApi.ts      |    21 +-
 .../src/services/billingStatementsApi.ts           |    22 +-
 68 files changed, 12899 insertions(+), 206 deletions(-)
```
