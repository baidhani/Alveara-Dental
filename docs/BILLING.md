# Billing: charges, payments, adjustments and financial history (STORY-007)

STORY-007 is the thin slice of REQ-014 ("charges, payments, and adjustments, preserving financial history"). The full ledger (guarantors, allocating a payment to specific charges, reversals and voids, configurable payment methods, one charge per completed procedure with correlation keys) is the companion `ALV-007-C01`, and creating the charge automatically when a procedure is completed is `ALV-008-C01`. This story builds only what its three acceptance criteria need and leaves room for those.

## What it is, in one paragraph

Each patient has one billing account. The account's history is a list of numbered **entries**, each a Charge, a Payment or an Adjustment, and entries are never edited or deleted: a mistake is corrected by a new entry. The balance is always computed from the entries (never stored, never taken from the screen). A charge is made for a procedure the clinic has recorded as completed, and its amount is the fee the treatment plan copied from the catalog when the procedure was proposed; the person billing never types it. Every entry is logged with the signed-in user and a timestamp in the same save as the entry.

## Acceptance, and how each is met

| Acceptance | Met by |
|---|---|
| A completed procedure, when billed, records the charge with the correct amount | `POST .../billing/charges` for a plan procedure whose state is Completed and which is not withdrawn. The server reads the amount from the plan procedure (the fee copied at proposal); the request carries no amount, and an amount sent anyway is ignored. A procedure that is not completed, withdrawn, another patient's, or already billed is refused (an identical repeat returns the existing charge). A database trigger repeats the rule: a charge must equal the procedure's stored fee. |
| A payment, when recorded, updates the balance and keeps the history | `POST .../billing/payments` (amount, method, optional note). A new Payment entry is appended; the balance read back is lower by that amount; every earlier entry is unchanged and each entry shows the balance owed after it. |
| All billing actions are logged with user and timestamp | One audit entry per entry, written in the same save; if it cannot be written nothing is stored (`save_failed`, 503). |

## Data (migration `AddBilling`)

| Table | Purpose | Rules the database enforces |
|---|---|---|
| `BillingAccounts` | one per patient: `PatientId` (unique), `CreatedAtUtc/ByUserId`, `UpdatedAtUtc`, `RowVersion` | never deleted (51118); patient and origin never change (51119) |
| `BillingEntries` | the history: `AccountId`, `PatientId`, `EntryNumber` (1, 2, 3, unique per account), `Kind` (Charge, Payment, Adjustment), `Amount` (decimal 18,2, signed), `PlanItemId` plus a copy of the procedure code and description (Charge only), `PaymentMethod` (Payment only), `Reason`, `IdempotencyKey`, who and when | append-only (51120); sign matches kind (Charge above zero, Payment below, Adjustment not zero); at most the fee ceiling (1,000,000.00); a charge carries its procedure and nothing else does; a payment carries one of the four methods; an adjustment carries a reason and a charge carries none; one charge per plan procedure; one entry per key and per number within an account; the entry's patient is its account's (51121); a charge bills a procedure of the same patient (51122), not withdrawn (51123), completed (51124), for exactly its fee (51125); no entry may leave the account owing less than nothing (51126) |

Triggers 51118-51126 belong to this story; **51127 and up are free.** The balance is `SUM(Amount)` over the account's entries: positive means the patient owes the practice. It is computed on read and carried with the account's row version, never stored in a column that could drift. Money uses the ALV-N002 `Money` type in code and `decimal(18,2)` in the database; no `double` or `float` anywhere. The column itself holds whole cents, and the service refuses more than two decimal places before the database sees it.

## Rules

- **Charge amount.** Always the plan procedure's stored fee; the request names the plan procedure only. This is what "incorrect billing amount" is protected by: there is no amount field for a person or a client to get wrong, and a later catalog fee change does not change what was billed.
- **What can be billed.** A plan procedure of the same patient whose state is Completed and which is not withdrawn (nor in a withdrawn plan), and not yet billed. Another patient's procedure gets exactly the answer an unknown one gets (404 `plan_item_not_found`).
- **Payment.** Amount above zero, in whole cents, at most the current balance (a payment cannot create a credit balance in this story; the message says what is owed), and a method from the fixed list (Cash, Check, Card, Other). A payment is applied to the patient's account balance; applying it to specific charges and guarantor accounts belongs to `ALV-007-C01`. Paying the wrong patient's account is prevented by the route naming the patient, the account being looked up for that patient only, and the account version the person saw having to match.
- **Adjustment.** A signed non-zero amount with a required reason; it cannot take the balance below zero. It is the only correction tool in this story; reversing or voiding a specific entry is `ALV-007-C01`.
- **Concurrency.** Recording a payment or adjustment carries the account version the person saw (every entry changes the account row, so the version moves). A stale version is refused as the shared 409 conflict and changes nothing; two people paying at once end with one payment and one conflict. A charge is protected by its unique plan procedure and key.
- **Idempotency.** Every create carries a key; the same key with the same content returns the account as it is, the same key with different content is refused (`idempotency_key_reused`, 409).
- **Audit.** Event types `BillingChargeRecorded`, `BillingPaymentRecorded`, `BillingAdjustmentRecorded`, entity `BillingAccount`, the signed-in user, the time and the amount; no patient name and no clinical wording.

## Endpoints

| Method and route | Needs |
|---|---|
| `GET api/patients/{patientId}/billing` (account, balance, entries with the balance after each) | `ViewBilling` |
| `GET api/patients/{patientId}/billing/billable-procedures` (completed, not yet billed: code, description, tooth, surface, fee only) | `ViewBilling` |
| `POST .../billing/charges`, `.../payments`, `.../adjustments` | `ManageBilling` + CSRF |

`ViewBilling` is held by the administrator, practice manager, billing, dentist and front desk; `ManageBilling` by billing, the practice manager and the administrator. The billable-procedures list exists because Billing cannot read treatment plans (STORY-015); it returns only what billing needs and no clinical wording. No new permission was added. Stable codes: `validation_failed` (with `fieldErrors`), `row_version_required`, `row_version_invalid`, `patient_not_found`, `plan_item_not_found`, `not_completed`, `item_withdrawn`, `already_billed`, `nothing_owed`, `idempotency_key_reused`, `save_failed` (503), plus the shared 409 concurrency conflict.

## Screen

The patient workspace's **Billing** tab (shown with `ViewBilling`): the balance owed (the server's figure), the completed procedures ready to bill (a "Bill CODE" button each, with the fee shown, no amount to type), the account history oldest first with the balance after each entry, a payment form and an adjustment form (drawn with `ManageBilling`; the payment form only while something is owed). Gaps are named before anything is sent; a refusal shows beside its field with what was typed kept; every failure says "Not recorded"; a retry carries the same key so it cannot record twice; a stale account shows the shared conflict banner.

## Failure-first notes

| Question | Answer |
|---|---|
| What if a save fails? | One transaction per entry (entry, the account row's new version, audit); nothing partial. Any failure other than a duplicate or a conflict, including a failed audit write, stores nothing and answers `save_failed` (503) without internals; the log carries the error class and SQL error number only (`billing_save_failed`). |
| Retries? | The screen keeps what was typed and the key; the server is idempotent by key. No automatic retry loops. |
| Recovery when it cannot proceed? | Nothing is queued; the refusal says why. A wrong entry is corrected by a new Adjustment with a reason (history kept). |
| Handled | wrong or missing amount, a procedure that is not billable, a second charge for the same procedure, an over-payment, an adjustment that breaks the balance rule, a stale account version, two people at once, a failed audit write, permissions, CSRF. |
| Not handled (companion) | allocating a payment to specific charges, guarantors, reversals and voids, configurable payment methods, insurance, statements, refunds, credit balances. |

## Assumptions made (STORY-007)

1. A payment is applied to the patient's account balance, not to specific charges; a payment above the balance is refused rather than creating a credit.
2. An adjustment is the only correction; it cannot make the balance negative.
3. Billing is by the person with `ManageBilling`; the charge is made for a procedure the clinic has already marked completed in the plan (the automatic charge on completion is `ALV-008-C01`).
4. The four payment methods are fixed; making them configurable belongs to the billing domain in `ALV-007-C01`.
5. The account is created when something is first billed, and a payment or adjustment needs something already on it.
6. The account version rather than a balance is what a payment echoes, so any change by anyone (not only a change to the balance) makes a stale screen reload before it records.

## Limitations (recorded, not hidden)

- No insurance, statements, refunds, credit balances, write-offs by policy, or per-charge allocation (`ALV-007-C01`); a charge is made one completed procedure at a time.
- The negative-balance rule is also a database trigger, but the trigger reads committed rows, so two simultaneous payments are stopped by the account version in the service, not by the trigger alone.
- Entries show the actor's staff display name, or "Staff member" for an account without a staff profile; the audit log carries the user id.
- The catalog's usage guard (ALV-N005) is unaffected: a charge copies the procedure's code and description and refers to the plan procedure, not to the catalog entry.

## Verification

Backend against real SQL Server: `BillingSchemaTests` (every database rule going around the service: each check constraint, uniqueness, every trigger 51118-51126 with its exact error number), `BillingServiceTests` (the amount from the plan, the history kept, every refusal, repeats, stale versions, two people at once, audit, the failed audit write), `BillingApiTests` (401 on every route, the role matrix, CSRF, per-field refusals, the whole workflow, the 503). Frontend: `BillingPanel.test.tsx` (vitest and axe). Browser: `e2e/billing-real-backend.spec.ts` via `playwright.billing.config.ts` (see `docs/testing/REAL_BACKEND_E2E.md`). Negative controls (each restored byte-identical and then run again): the charge no longer the plan's fee, the over-payment rule removed, the account version no longer checked, the payment audit removed, a payment reaching any patient's account, and database trigger 51125 weakened; each was caught by the tests above. The migration's rollback was verified on a scratch database (the two tables and four triggers go and come back).
