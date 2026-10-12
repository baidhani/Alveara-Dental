# ALV-007-C02 R01 - Changed files

Implementation commit `bc25a94ae50639a5a46509c443ebf65b8f0992ab` (53 files). Generated from `git show --numstat`; the evidence and status commits only add records under `.alveara/`.

## Backend (19)
- `src/Alveara.Api/Architecture/Billing/BillingAccount.cs` (+7 -1)
- `src/Alveara.Api/Architecture/Billing/BillingLedgerMath.cs` (+47 -12)
- `src/Alveara.Api/Architecture/Billing/BillingRefundContracts.cs` (+20 -0)
- `src/Alveara.Api/Architecture/Billing/BillingRelease.cs` (+35 -0)
- `src/Alveara.Api/Architecture/Billing/BillingRules.cs` (+3 -3)
- `src/Alveara.Api/Architecture/Billing/BillingService.Corrections.cs` (+196 -0)
- `src/Alveara.Api/Architecture/Billing/BillingService.Documents.cs` (+290 -0)
- `src/Alveara.Api/Architecture/Billing/BillingService.Ledger.cs` (+24 -7)
- `src/Alveara.Api/Architecture/Billing/BillingService.Queries.cs` (+8 -6)
- `src/Alveara.Api/Architecture/Billing/BillingService.Refunds.cs` (+93 -0)
- `src/Alveara.Api/Architecture/Billing/BillingService.Responsibility.cs` (+2 -2)
- `src/Alveara.Api/Architecture/Billing/BillingService.Timeline.cs` (+78 -0)
- `src/Alveara.Api/Architecture/Billing/FinancialDocument.cs` (+50 -0)
- `src/Alveara.Api/Architecture/Billing/FinancialDocumentBuilder.cs` (+113 -0)
- `src/Alveara.Api/Architecture/Billing/FinancialDocumentContracts.cs` (+49 -0)
- `src/Alveara.Api/Controllers/BillingStatementsController.cs` (+113 -0)
- `src/Alveara.Api/Data/AlveraDbContext.cs` (+3 -0)
- `src/Alveara.Api/Data/BillingModel.cs` (+13 -4)
- `src/Alveara.Api/Data/FinancialDocumentModel.cs` (+75 -0)

## Migration (generated designer and snapshot included) (3)
- `src/Alveara.Api/Migrations/20261012133723_AddRefundsReleasesAndDocuments.Designer.cs` (+6265 -0)
- `src/Alveara.Api/Migrations/20261012133723_AddRefundsReleasesAndDocuments.cs` (+478 -0)
- `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` (+255 -5)

## Backend tests (11)
- `src/Alveara.Api.Tests/BillingDocumentBuilderTests.cs` (+133 -0)
- `src/Alveara.Api.Tests/BillingDocumentServiceTests.cs` (+280 -0)
- `src/Alveara.Api.Tests/BillingReconciliationTests.cs` (+244 -0)
- `src/Alveara.Api.Tests/BillingRefundConcurrencyTests.cs` (+119 -0)
- `src/Alveara.Api.Tests/BillingRefundMathTests.cs` (+131 -0)
- `src/Alveara.Api.Tests/BillingRefundMigrationTests.cs` (+90 -0)
- `src/Alveara.Api.Tests/BillingRefundSchemaTests.cs` (+523 -0)
- `src/Alveara.Api.Tests/BillingRefundServiceTests.cs` (+282 -0)
- `src/Alveara.Api.Tests/BillingSchemaTests.cs` (+1 -1)
- `src/Alveara.Api.Tests/BillingStatementsApiTests.cs` (+306 -0)
- `src/Alveara.Api.Tests/BillingTimelineAgingTests.cs` (+100 -0)

## Frontend (13)
- `src/alveara-client/src/pages/billing/Billing.css` (+14 -0)
- `src/alveara-client/src/pages/billing/BillingCorrectForm.tsx` (+101 -0)
- `src/alveara-client/src/pages/billing/BillingDocumentView.tsx` (+67 -0)
- `src/alveara-client/src/pages/billing/BillingDocuments.tsx` (+162 -0)
- `src/alveara-client/src/pages/billing/BillingLedger.tsx` (+19 -3)
- `src/alveara-client/src/pages/billing/BillingMoveForm.tsx` (+109 -0)
- `src/alveara-client/src/pages/billing/BillingPanel.tsx` (+24 -2)
- `src/alveara-client/src/pages/billing/BillingRefundForm.tsx` (+94 -0)
- `src/alveara-client/src/pages/billing/BillingStatementsPanel.test.tsx` (+341 -0)
- `src/alveara-client/src/pages/billing/BillingTimelineAging.tsx` (+60 -0)
- `src/alveara-client/src/pages/billing/billingLedgerView.ts` (+7 -2)
- `src/alveara-client/src/services/billingApi.ts` (+10 -1)
- `src/alveara-client/src/services/billingStatementsApi.ts` (+84 -0)

## Browser tests and config (3)
- `src/alveara-client/e2e/billing-statements-real-backend.spec.ts` (+396 -0)
- `src/alveara-client/playwright.billing-statements.config.ts` (+24 -0)
- `src/alveara-client/playwright.config.ts` (+1 -1)

## Docs (3)
- `docs/BILLING_LEDGER.md` (+1 -1)
- `docs/BILLING_STATEMENTS.md` (+59 -0)
- `docs/testing/REAL_BACKEND_E2E.md` (+13 -0)

## Records (1)
- `PROGRESS.md` (+7 -0)

## Files of earlier stories that this commit touches (all minimal)
- `src/Alveara.Api.Tests/BillingSchemaTests.cs`: one example literal ("Refund" to "Bogus"; see `PARENT_REGRESSION.md`).
- `src/Alveara.Api/Architecture/Billing/BillingAccount.cs`, `BillingRules.cs`, `BillingService.Ledger.cs`, `BillingService.Queries.cs`, `BillingService.Responsibility.cs`, `BillingLedgerMath.cs`: the refund kind, optional fields at the end of existing records, and every derived number now passing the releases (a call that left them out would be wrong once a release exists).
- `src/Alveara.Api/Data/AlveraDbContext.cs`, `BillingModel.cs`, `Migrations/AlveraDbContextModelSnapshot.cs`: the two new sets, the new columns and checks, the regenerated snapshot.
- `src/alveara-client/src/pages/billing/BillingPanel.tsx`, `BillingLedger.tsx`, `billingLedgerView.ts`, `Billing.css`, `src/services/billingApi.ts`: the new actions, standing text and optional fields.
- `src/alveara-client/playwright.config.ts`: the new real-backend spec is excluded from the mocked run, like its siblings.
