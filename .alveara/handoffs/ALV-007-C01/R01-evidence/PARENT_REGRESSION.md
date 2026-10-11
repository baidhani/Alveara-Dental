# ALV-007-C01 R01 - Parent regression

Parent course story `STORY-007` (portal-verified 2026-10-11T13:30:07Z at `3471727`, 3 of 3 criteria, 130 points). Its completion contract is immutable and must keep passing. Companion dependencies `ALV-003-C01` (the guarantor this story reads), `ALV-N005` (the catalog the plan's fee comes from) and `ALV-002-C01` (audit and concurrency primitives) are regressed with it, because this attempt reads patients' guarantors, extends the audit and concurrency use of the billing service, and changes nothing in the catalog.

| STORY-007 Done Means | Still proven by (all passing in this attempt's runs) |
|---|---|
| 1. Given a procedure is completed, when billed, the charge is recorded with the correct amount | `BillingServiceTests.A_completed_procedure_is_billed_at_the_fee_it_was_proposed_at_with_who_and_when`, `The_charge_is_the_plans_fee_even_after_the_catalog_fee_changes`, `A_procedure_that_cannot_be_billed_is_refused...`; `BillingApiTests.A_completed_procedure_is_billed_a_payment_lowers_the_balance...`; `BillingPanel.test.tsx` ("sends the procedure and a save key and no amount"); STORY-007 walkthrough "ACCEPTANCE 1" (the charge carries the plan's fee with no amount to type); and now also through the new contract (`BillingProcedureChargeContractTests`) |
| 2. Given a payment is made, when recorded, the balance updates and history is preserved | `BillingServiceTests.A_payment_lowers_the_balance_and_the_history_keeps_every_entry_as_it_was` (an earlier entry is identical after a later payment; this is the test that required derived numbers to be kept off the recorded entry), `BillingApiTests...`, `BillingPanel.test.tsx` ("sends the amount, method and note with the account version..."), STORY-007 walkthrough "ACCEPTANCE 2" |
| 3. All billing actions are logged with user and timestamp | `BillingServiceTests.Every_billing_action_is_audited_with_the_user_and_time_and_no_patient_or_clinical_wording`, the failed-audit tests, `BillingApiTests`, STORY-007 walkthrough "ACCEPTANCE 3"; the new actions follow the same rule (`BillingLedgerServiceTests`, `BillingResponsibilityServiceTests`, `BillingLedgerApiTests`) |

## Results
| What | Result |
|---|---|
| STORY-007 backend classes inside the full serial run | `BillingSchemaTests` 23, `BillingServiceTests` 29, `BillingApiTests` 23 (75 of 75, none edited by this attempt; `BillingServiceTests.cs` differs from the STORY-007 commit by one line, an analyzer fix in the earlier test-quality commit `b3651b6`); the whole run **2856 of 2856** |
| STORY-007 frontend | `BillingPanel.test.tsx` **15 of 15**, unchanged; the whole run **1346 of 1346** |
| STORY-007 walkthrough (`playwright.billing.config.ts`, spec unchanged) | **10 of 10** on a fresh database (its first attempt in the sweep failed, see below) |
| ALV-N005 procedure catalog (read by the plan code, not changed) | all catalog backend tests pass inside the full run; walkthrough `procedures` **8 of 8** |
| ALV-003-C01 patients and guarantor (read by the new responsibility code) | all patient tests pass inside the full run; walkthroughs `patients` 7 of 7 and `workspace` 14 of 14; the guarantor is only read by the new code; the tests set it directly in SQL and the walkthrough through the existing guarantor route |
| STORY-015 and ALV-015-C01 (the plan whose completed procedures are billed) | `treatment_plans` **11 of 11**, `treatment_plans_complete` **11 of 11** |
| The other 14 existing real-backend walkthroughs | auth 12, forms 10, schedule 10, calendar 12, flow 10, board 15, clinical 9, companion 9, safety 11, odontogram 11, odontogram longitudinal 11, perio 10, perio sessions 12, diagnoses 12, diagnoses structure 12 (the 17 existing total **187 of 187**) |
| Mocked browser suite and repository checks | **136 of 136** and **8 of 8** |

All 23 sweep runs: **238 of 238 passed**, each on its own fresh database (`artifacts/regression/summary.txt` and the per-walkthrough outputs).

## What changed for the parent, deliberately
- The account view gained fields (all additive) and the payment request accepts optional `allocations` and `paidByPatientId`; the STORY-007 request and response shapes still work and are still what its tests send.
- A payment now records the practice's configured method by name; the four built-in methods keep their STORY-007 names and kinds, so `"Cash"`, `"check"` and the rest behave as before, and the database check on the kind is unchanged.
- **The first sweep attempt of the STORY-007 walkthrough failed**: its assertion "no form before anything is billed" saw the new responsibility form (offered to people who can change the account even with no account). The decision was to change the new UI, not the parent's spec: the responsibility form is now offered once the account exists, like the payment and adjustment forms, with a line saying when it can be recorded. The walkthrough passes unchanged (10 of 10) and the ledger walkthrough still passes (11 of 11). The failed attempt's output is kept (`artifacts/regression/billing-first-attempt-FAILED-run-output.txt`).
- The recorded entry views are exactly as STORY-007 left them plus three immutable fields (`reversesEntryNumber`, `paymentMethodName`, `paidByName`); everything derived is in the new `standing` list. This is what keeps the STORY-007 "history preserved" test passing.

## Not regressed
The 5 s margin of the slowest frontend suites (a recurring flake in earlier attempts) was widened in the test-quality commit `b3651b6` and did not recur in the final runs; one earlier full run had a single unexplained failure in an unrelated file that passed alone and on rerun (disclosed in `TEST_RESULTS.md`).
