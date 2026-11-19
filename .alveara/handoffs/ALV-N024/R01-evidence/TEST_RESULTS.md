# ALV-N024 R01: test results (Stage 1 focused proof; 2026-11-19)

Swept tree **`1c1184b`** (the final implementation commit, `Story: ALV-N024`). One sweep, 08:37:55-08:48:33 (-06:00, 10 m 38 s). The working tree was clean before and after (`artifacts/summary.txt`). Environment: Windows 11, local SQL Server Express, Chromium. Route: focused proof only, `fullRegression: DEFERRED_TO_ALV-N028`, no package Stage 2.

| Run | Result | Duration | Output |
|---|---|---|---|
| `dotnet build Alveara.Api.Tests` | ok | 35 s | `artifacts/gates/backend_build.txt` |
| Server group A (7 classes) | **44 / 44** | 102 s | `artifacts/gates/groupA.txt`, `artifacts/backend-focused-tests.csv` |
| Server group B (9 classes) | **169 / 169** | 224 s | `artifacts/gates/groupB.txt` |
| Server group C (6 classes) | **125 / 125** | 182 s | `artifacts/gates/groupC.txt` |
| `npx tsc -b` | exit 0 | - | `artifacts/gates/tsc.txt` |
| `npm run lint` (oxlint) | exit 0 (0 errors; no new warning in the billing or household files) | - | `artifacts/gates/lint.txt` |
| `npm run build` | exit 0 | - | `artifacts/gates/build.txt` |
| `npx vitest run src/pages/billing src/PatientHousehold.test.tsx` (PowerShell) | **5 files 83 / 83** | 31 s (client gates together) | `artifacts/gates/vitest_focused.txt` |
| `node --test tests/*.test.mjs` (repo root) | **58 / 58** | 1 s | `artifacts/gates/repo_checks.txt` |
| `billing-integrity-real-backend` alone, fresh database (`-SkipBuild`) | **7 / 7** | 32 s | `artifacts/gates/walk_integrity.txt`, `artifacts/walkthrough/` |
| Leak counts before / after | identical (1 leftover test database, 1 template backup, both from before this story) | - | `artifacts/leak_before.txt`, `artifacts/leak_after.txt` |

The filters were class-exact: `FullyQualifiedName~Tests.<Class>.` per class, joined with `|`. Server total: **338 / 338**, listed one per row in `artifacts/backend-focused-tests.csv`.

**Group A:** BillingDocumentReplacementMigrationTests, BillingDocumentReplacementTests, BillingReceiptConcurrencyTests, BillingPayerEligibilityTests, BillingResponsibilityAndMethodAnswersTests, RealMigrationInventoryTests, BillingDocumentServiceTests.
**Group B:** BillingDocumentBuilderTests, FinancialDocumentTemplateVersionTests, BillingDocumentTemplateVersionServiceTests, BillingReconciliationTests, BillingStatementsApiTests, BillingTimelineAgingTests, BillingLedgerSchemaTests, BillingRefundSchemaTests, BillingRefundMigrationTests.
**Group C:** BillingLedgerServiceTests, BillingLedgerApiTests, BillingServiceTests, BillingRefundServiceTests, BillingResponsibilityServiceTests, TreatmentPlanAppointmentLinkMigrationTests (its roll-back passes through this story's Down).

## Negative controls (one at a time; byte backup; SHA-256 equal after restore; run before the final commit on the same code; addendum 02 approved their reading)
| Control | Change | Tests run | Result |
|---|---|---|---|
| (a) | service one-receipt check removed (`CheckReceiptRuleAsync` returns at once) | BillingDocumentReplacementTests | **2 of 3 fail**, at the preview step (a second preview is no longer refused). This proves the tests catch the removal; the guard 51295 on its own is proven by the direct-SQL BillingDocumentReplacementMigrationTests. |
| (b) | service payer check removed | BillingPayerEligibilityTests | **1 of 3 fails**: the stranger's payment is still refused, now by the database (`save_failed` 503 instead of `payer_not_eligible` 400; the widened 51142). This is the layering proof. |
| (c) | adjustment term removed (`LiveAdjustments` returns 0) | BillingReconciliationTests | **1 of 1 fails** (the property identity) |
| (d) | version 2 built with the version-3 facts | FinancialDocumentTemplateVersionTests | **3 of 24 fail**, including the frozen-v2 byte-for-byte test |
| (e) | service lock removed (`AcquireReceiptLockAsync` commented out) | BillingReceiptConcurrencyTests (strengthened, addendum 02) | **4 of 4 fail, each at the refusal-code assertion, after every invariant had passed in every round it reached**: exactly one receipt (two with the replacement), the audit count unchanged by the loser, the lock free, and in the busy test nothing written. Refusals seen: deadlock victim, stale-account conflict, `save_failed` (51297). This is the layering proof. |
| (f) | `READCOMMITTEDLOCK` removed from the trigger | BillingDocumentReplacementMigrationTests (20-round direct race) | **1 of 1 passed**. This is an **observation only** (addendum 02): it is not used as proof, and the hint's necessity is no longer claimed. |

The logs are in `artifacts/negative-controls/control_*.log`. `control_e.log` holds the re-run with the strengthened tests; the first run (which ended each test on the database exception before its count assertion) is described in addendum 02 section 4.

## New or changed tests (whole story)
- **Server, new:** BillingDocumentReplacementMigrationTests (7), BillingDocumentReplacementTests (3), BillingReceiptConcurrencyTests (4, 20 rounds each), BillingPayerEligibilityTests (3), BillingResponsibilityAndMethodAnswersTests (4); `OriginalFinancialDocumentBuilderV2.cs` (a verbatim frozen copy at `f34cd43` with its SHA-256).
- **Server, changed to the approved behaviour (never loosened):**
  - FinancialDocumentTemplateVersionTests: the unknown version moves 3 -> 4; v2 byte-for-byte and v3-delta tests added.
  - BillingDocumentTemplateVersionServiceTests: current version 3; the v1 receipt fixture is stored as a replacement; the v1 CSV follows the live-export contract.
  - BillingDocumentServiceTests: the stale version is 2; the CSV row layout.
  - BillingDocumentBuilderTests and BillingStatementsApiTests: the CSV header.
  - BillingReconciliationTests: the property identity with the server's AdjustmentTotal.
  - RealMigrationInventory(+Tests): 31 -> 32.
- **Client:**
  - BillingLedgerPanel.test (the payer list from the server; Q-5bf rewritten; new tests for nothing-changed, already-exists, adjustments line, refunded explanation and errors that clear on change);
  - BillingStatementsPanel.test (existing receipt opens, replacement needs a reason, document status, v3 view and focus; the F-122 wording);
  - BillingPanel.test (the F-122 wording);
  - PatientHousehold.test (F-204 names, F-203 shown up front).
- **Browser:** `billing-integrity-real-backend.spec.ts` (new, 7). `billing-ledger-real-backend` and `billing-statements-real-backend` are updated in source (the wording, payer label and CSV layout) and **not run**.
