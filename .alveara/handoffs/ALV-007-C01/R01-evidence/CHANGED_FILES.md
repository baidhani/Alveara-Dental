# ALV-007-C01 R01 - Changed files

Implementation commit `9dd15c1712a1010eafc8c457c4a207b5cb2b625b` (43 files, 10,704 insertions, 100 deletions; about 4,400 of the insertions are the generated migration designer).

## Backend (`src/Alveara.Api`)
| File | Change |
|---|---|
| `Architecture/Billing/BillingLedgerMath.cs` | new: the ledger arithmetic (running balances, paid and open per charge, unapplied per payment, oldest-first allocation, and `Verify`) |
| `Architecture/Billing/BillingService.Ledger.cs` | new: allocation, applying a payment, reversal, ledger verification |
| `Architecture/Billing/BillingService.Responsibility.cs` | new: who is responsible, the guarantor summary |
| `Architecture/Billing/BillingService.Methods.cs` | new: payment-method configuration |
| `Architecture/Billing/BillingService.ProcedureCharge.cs` | new: `IProcedureCharging` and its implementation (the contract for ALV-008-C01) |
| `Architecture/Billing/BillingService.cs`, `.Queries.cs` | the payment path uses configured methods, the payer and allocations; the charge path split into shared `LoadBillableAsync` and `StageChargeAsync`; the account view carries `standing`, `allocations` and totals |
| `Architecture/Billing/BillingAccount.cs`, `BillingRules.cs` | the `Reversal` kind; the new entities; the new request and view records; the method-name and method rules |
| `Controllers/BillingController.cs` | the new routes and request shapes |
| `Data/BillingModel.cs`, `AlveraDbContext.cs`, `Program.cs` | model configuration, sets, registration of `IProcedureCharging` |
| `Migrations/20261011155619_AddBillingLedger.cs` (+ designer, snapshot) | the schema and triggers 51127-51143 |

## Backend tests (`src/Alveara.Api.Tests`)
`BillingLedgerSchemaTests.cs` (33), `BillingLedgerMathTests.cs` (17), `BillingLedgerServiceTests.cs` (23), `BillingResponsibilityServiceTests.cs` (22), `BillingProcedureChargeContractTests.cs` (10), `BillingLedgerApiTests.cs` (22): all new. `BillingTestBase.cs`: helpers added (no STORY-007 test changed by this attempt).

## Frontend (`src/alveara-client`)
| File | Change |
|---|---|
| `src/services/billingApi.ts` | the new types and calls, and shape guards for the secondary panels |
| `src/pages/billing/BillingPanel.tsx`, `BillingLedger.tsx`, `BillingPaymentForm.tsx` | extended: standing, actions, methods, payer, allocation |
| `src/pages/billing/AllocationChooser.tsx`, `BillingApplyForm.tsx`, `BillingReverseForm.tsx`, `BillingResponsibilityPanel.tsx`, `GuarantorSummary.tsx`, `PaymentMethodsSettings.tsx`, `billingLedgerView.ts` | new |
| `src/pages/billing/Billing.css` | styles (no custom colour for the standing text: it failed contrast in the dark theme) |
| `src/pages/billing/BillingLedgerPanel.test.tsx` | new, 21 tests |
| `e2e/billing-ledger-real-backend.spec.ts`, `playwright.billing-ledger.config.ts`, `playwright.config.ts` | the new walkthrough, its config, and its exclusion from the mocked run |

## Docs
`docs/BILLING_LEDGER.md` (new), `docs/BILLING.md` (pointer), `docs/testing/REAL_BACKEND_E2E.md` (the walkthrough), `PROGRESS.md`.

## Shared components of completed stories touched
`AlveraDbContext.cs`, `Program.cs` and `playwright.config.ts` (the normal wiring points); the STORY-007 billing service, controller, entities and model (extended, contract kept); `BillingTestBase.cs` (additive). No catalog, treatment-plan, patient or audit code was changed.

## Records commits that follow
This handoff and evidence (`.alveara/handoffs/ALV-007-C01/R01*`), `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json` and `PROGRESS.md`.
