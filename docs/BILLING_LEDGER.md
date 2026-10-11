# Billing ledger: allocation, reversals, responsibility and payment methods (ALV-007-C01)

The companion to STORY-007 (`docs/BILLING.md`, whose contract is unchanged and still passes). It turns the thin billing slice into an authoritative ledger: payments are applied to charges, any entry can be undone by a new entry that is exactly its opposite, who is financially responsible for an account is recorded apart from the patient's identity, the practice's payment methods are configured by billing, the balance and every derived number can be rebuilt and verified from the preserved history, and `ALV-008-C01` has a contract for making the charge when it completes a procedure. Statements, receipts, richer refunds and operational hardening are `ALV-007-C02`; processors and insurance accounting stay future.

## What it is, in one paragraph

The account's history is still a list of numbered entries that are never edited or deleted. A new entry kind, **Reversal**, undoes one earlier entry (its amount is the opposite) and says why. **Allocations** say how much of a payment pays which charge; they too are only appended. Everything the screen shows beyond the entries as recorded (reversed, paid, open, applied, unapplied, the balance after each entry) is derived from that history by one piece of arithmetic (`BillingLedgerMath`) and can be rebuilt and checked at any time (`GET .../billing/verification`). An entry as recorded never changes; what it stands for now is delivered separately (`standing`).

## Data (migration `AddBillingLedger`)

| Table | Purpose | Rules the database itself enforces |
|---|---|---|
| `BillingEntries` (extended) | new kind `Reversal`; `ReversesEntryId`, `CorrelationId`, `PaidByPatientId`, `PaymentMethodId` | a reversal names the entry it undoes and nothing else does; an entry is reversed at most once (unique index); a reversal needs a reason; payer and method are for payments only; a reversal is on an entry of the same account (51138), never of a reversal (51139), exactly the opposite amount (51140), never of a charge that payments are applied to (51141); a payer is the patient or someone recorded as responsible (51142); a payment's method is active and of the payment's kind (51143) |
| `BillingAllocations` | how much of one payment pays one charge: payment, charge, amount, `AllocationNumber` (unique per account), a command key | append-only (51127); from a payment to a charge (51128) within one account and patient (51129); never more than the payment holds (51130) nor than the charge is (51131), counting only allocations whose payment has not been reversed; never to or from a reversed entry (51132) |
| `BillingResponsibilities` | who is responsible for an account from a date (the account's own patient or another patient), with who and why; the latest is current | append-only (51133); same account and patient (51134); the responsible patient is active (51135) |
| `BillingPaymentMethods` | the practice's payment methods: a name (unique whatever its case) and one of four kinds (Cash, Check, Card, Other); the four built-ins are seeded with fixed ids | never deleted (51136); name and kind never change (51137); a built-in stays active |

Triggers 51118-51143 belong to billing (STORY-007 and this story); **51144 and up are free.** `Down` removes the new tables, columns and checks, and cannot succeed once a Reversal entry exists (financial history is append-only, so the old kind list cannot be restored over it; noted in the migration).

## Rules (documented, tested at three levels: service, database and API)

- **Allocation.** A payment is applied to the charges it names, or to the **oldest open charges first** when it names none. It is never applied to a charge for more than that charge has left, nor for more than the payment holds. Whatever is not applied (a payment larger than the open charges because an adjustment raised the balance, or one that named only some) stays **unapplied**, is shown, and can be applied later (`apply`). A payment still cannot be more than the balance owed.
- **Reversal.** Any charge, payment or adjustment can be undone once by a new entry that is exactly its opposite and carries a required reason. Reversing a payment **releases** its allocations (they stay on record, marked released, and stop counting). A charge that payments are applied to cannot be reversed until they are (`charge_has_payments`, 409). A reversal cannot be reversed. A reversal that would leave the account owing less than nothing is refused. A reversed charge is still the procedure's one charge and is never made again.
- **Responsibility.** Billing records who is responsible for an account (the patient or another active patient) from a date that is not in the future and not before the one it replaces. Until billing records one, the account follows the guarantor registered on the patient (ALV-003-C01) and failing that the patient. The two are independent: recording a responsible party never changes the registered guarantor and changing the registered guarantor never rewrites a recorded responsibility. Naming the party who is already responsible changes nothing. A **guarantor summary** lists every account a person answers for with each account's own ledger balance and a total. A payment can be recorded as paid by the patient or by someone recorded as responsible for the account.
- **Payment methods.** Four are built in and always active. Billing adds named methods that each map to one of the four kinds (the kind is what the ledger reports on) and can stop using one, which only stops it being chosen for new payments; old payments keep showing it. A method is never deleted or renamed. Adding the same name and kind again is quiet; two people adding the same name at once end with one method.
- **Idempotency and concurrency.** Every create carries a key; the same key with the same content returns the account as it is, the same key with different content is refused (`idempotency_key_reused`). Payments, applications, adjustments, reversals and responsibility changes echo the account version the person saw; a stale one is the shared 409 conflict. Two people reversing the same entry at once end with one reversal (the other is told it is already reversed or gets the conflict).
- **Audit.** `BillingPaymentApplied`, `BillingEntryReversed`, `BillingResponsibilityChanged`, `BillingPaymentMethodAdded` and `BillingPaymentMethodChanged` join the three STORY-007 events, each with the signed-in user, the time and the reason where there is one, in the same save as the change; no patient names. A failed audit write stores nothing (`save_failed`, 503).

## Verification of the history

`GET api/patients/{id}/billing/verification` rebuilds the balance and every derived number from the preserved entries and allocations and reports anything that breaks a rule: entry numbers that are not consecutive, a reversal that is not the opposite of an earlier entry or undoes a reversal, an entry reversed twice, a balance below zero at any point, an allocation that is not from a payment to a charge, a payment applied for more than it holds, a charge paid for more than it is, a reversed charge still being paid, and any difference between the rebuilt balance and the database's own sum. A consistent history returns an empty list. A test switches an allocation trigger off, as only a database administrator could, writes an over-allocation, and shows the verification naming it.

## Contract for ALV-008-C01: one charge per billable completed procedure

`IProcedureCharging.ChargeCompletedProcedureAsync(ProcedureChargeRequest(patientId, planItemId, correlationId), actor)` (registered in DI; implemented by `BillingService`).

- **Call it** on the same `AlveraDbContext` as the completion and inside a transaction the caller opened; it refuses (`InvalidOperationException`) to run outside one, because a charge that could outlive a failed completion (or the reverse) would break the atomicity ALV-008-C01 promises. It saves what the caller staged in that transaction (so the completed state exists when the charge's rules are checked), adds the charge and its audit entry and saves again; the caller commits or rolls everything back together.
- **Idempotent:** the charge's save key is derived from the plan procedure, so there is exactly one charge per procedure however many times a completion is replayed; a repeat returns the existing charge with `Created = false` (and `Reversed` when a reversal has since undone it).
- **The amount** is always the fee the plan copied from the catalog when it proposed the procedure; the request has no amount.
- **Refusals** are `BillingException`s with stable codes: `plan_item_not_found` (404; another patient's looks the same), `item_withdrawn` and `not_completed` (409), `save_failed` (503, including a failed audit write, which leaves neither the charge nor the completion behind). Two callers racing for the same procedure end with one charge; the loser gets a `ConcurrencyConflictException`, rolls back and retries to receive the existing charge.
- **Correlation:** the caller's id is stored on the charge and its audit entry.

## Endpoints added

| Method and route | Needs |
|---|---|
| `GET api/patients/{id}/billing/verification`, `.../responsibility`, `.../guarantor-summary`, `GET api/billing/payment-methods?includeInactive=` | `ViewBilling` |
| `POST .../billing/payments` (now with optional `allocations` and `paidByPatientId`), `.../payments/apply`, `.../reversals`, `.../responsibility`, `POST api/billing/payment-methods`, `.../{id}/active` | `ManageBilling` + CSRF |

The account view gains `entries[].reversesEntryNumber`, `paymentMethodName` and `paidByName` (immutable facts) and, separately, `standing[]` (derived: reversed, paid, open, applied, unapplied per entry), `allocations[]`, `unappliedTotal` and `openChargeTotal`. New stable codes: `nothing_to_apply`, `payment_reversed`, `payment_not_found`, `entry_not_found`, `charge_has_payments`, `already_reversed`, `cannot_reverse_a_reversal`, `method_exists`, `built_in_method`, `method_not_found`, plus field errors `allocations[n]`, `allocations`, `paidBy`, `name`, `kind`, `responsiblePatientId`, `effectiveFrom`. No new permission was added.

## Screen

The Billing tab gains: what each entry stands for beside it (paid and open on a charge, applied and unapplied on a payment, "Reversed by entry N" with the entry struck through, "Reverses entry N" on the reversal); "Reverse entry N" and "Apply payment N" actions (only for people who can change the account, and only where they can apply); a reversal form that needs a reason; a payment form with the practice's methods, a "Who paid" choice and a "Choose which charges this pays" option listing only open charges; who is responsible, with its history and a form (the patient or the registered guarantor, and always the party currently responsible); a guarantor's table of accounts with a total; and a payment-method section. Secondary panels are left out, without an alert, when they cannot be read.

## Failure-first notes

| Question | Answer |
|---|---|
| What if a save fails? | One transaction per command (the entry or allocations, the account row's new version, the audit entry). Any failure other than a duplicate or a conflict, including a failed audit write, stores nothing and answers `save_failed` (503) without internals. |
| Retries? | The screen keeps what was typed and the key; the server is idempotent by key. No automatic retry loops. |
| Recovery when it cannot proceed? | Nothing is queued; the refusal says why (for example which payments to reverse first). A wrong entry is undone by a reversal or corrected by an adjustment; the history is kept. |
| Handled | duplicate charge, payment, application or reversal; over- and under-allocation; a stale account; reversal conflicts (already reversed, a reversal, a charge still paid, an overdraw); an invalid or non-completed procedure; a failed audit write; two people at once; permissions; CSRF. |
| Not handled | statements and receipts, refunds and credit balances, insurance, a payment spread across several people's accounts in one step, re-billing a procedure whose charge was reversed. |

## Assumptions made (ALV-007-C01)

1. A payment is applied to one account; a guarantor paying for several people records one payment per account.
2. The default allocation is oldest open charge first, and what cannot be applied stays unapplied (an adjustment can raise the balance without a charge to pay).
3. A charge with payments applied to it must have them reversed first (refuse rather than silently re-allocate).
4. A reversal can itself never be reversed; the correction is a new charge, payment or adjustment.
5. Responsibility defaults to the registered guarantor without writing a row, so STORY-007 accounts and counts are unchanged until billing records one.
6. The four payment kinds are fixed (the ledger reports on them); methods are named aliases of a kind.
7. Recording who is responsible opens the account if it does not exist yet.

## Limitations (recorded, not hidden)

- **A reversed charge keeps its procedure "billed"** (one charge per procedure, enforced by a unique index that cannot ignore reversed rows). Re-billing a procedure whose charge was reversed needs a later story.
- The responsibility form offers the patient and the registered guarantor (and the current party); naming any other patient means setting the guarantor in registration first (there is no patient picker in billing).
- A reversal needs the account version, so a person working from an out-of-date screen reloads first.
- No refunds, credit balances, statements or receipts (`ALV-007-C02`); no processors or insurance.
- `Down` of the migration cannot succeed once a reversal exists.

## Verification

Backend against real SQL Server: `BillingLedgerSchemaTests` (every new check, uniqueness and trigger going around the service, each with its exact error number), `BillingLedgerMathTests` (the arithmetic without a database, including seeded random histories checked after every step), `BillingLedgerServiceTests` (allocation, application, reversal, payer, methods, audit, reconstruction and a tampered history), `BillingResponsibilityServiceTests`, `BillingProcedureChargeContractTests` (the contract as `ALV-008-C01` will use it, including rollback, replay, racing and a failed audit write), `BillingLedgerApiTests` (roles, CSRF, refusals, the workflow, the 503, the DI registration). Frontend: `BillingLedgerPanel.test.tsx` (vitest and axe). Browser: `e2e/billing-ledger-real-backend.spec.ts` via `playwright.billing-ledger.config.ts` (see `docs/testing/REAL_BACKEND_E2E.md`). The STORY-007 tests are unchanged and pass.
