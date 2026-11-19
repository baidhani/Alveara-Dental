# ALV-N024 R02: test results (corrections only; 2026-11-19)

Implementation `a995bff` (on top of R01's `1c1184b`). As the R01 review instructed, only the directly affected tests were run. The full R01 evidence (`R01-evidence/TEST_RESULTS.md`, 338/338 server, 83/83 vitest, 58/58 repo, 7/7 browser) stands for the unchanged areas.

| Run | Result | Output |
|---|---|---|
| `dotnet build Alveara.Api.Tests` | ok | `artifacts/backend_build.txt` |
| Server: BillingDocumentReplacementMigrationTests, BillingPayerEligibilityTests, BillingLedgerSchemaTests, BillingResponsibilityServiceTests, BillingLedgerServiceTests | **90 / 90** (166 s) | `artifacts/server.txt`, `artifacts/server-tests.txt` |
| vitest `src/pages/billing/BillingLedgerPanel.test.tsx` (PowerShell) | **31 / 31** (9 s) | `artifacts/vitest.txt` |
| `billing-integrity-real-backend` alone, fresh database | **7 / 7** | `artifacts/walk_integrity.txt`, `artifacts/walkthrough/` (including `checkbox-geometry.json`) |
| The first F-196 browser run (before the CSS fix) | 1 failed (checkbox 12.25 px off its text line) | `artifacts/walk_integrity_failed_diagnostic.txt` |
| `npx tsc -b`, lint on the billing files | pass | - |

New tests: `R02_the_database_accepts_only_an_active_related_payer_for_payments_and_corrections` (direct SQL), `R02_an_inactive_guarantor_household_member_or_responsible_party_is_not_offered_and_is_refused_for_a_payment_and_a_correction` (service), two BillingLedgerPanel tests (F-207, F-195), and the PAYER-step geometry assertion (F-196).
