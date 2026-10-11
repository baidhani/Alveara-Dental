# ALV-007-C01 R01 - Next-story impact

The next scheduled item is 33, `STORY-008` (a course story, executed in the course portal). Item 34, `ALV-008-C01` (atomic procedure completion, clinical history, odontogram and charge generation), is the story this attempt was built for, so most of this file is for it.

## For ALV-008-C01: making the charge
- **Call `IProcedureCharging.ChargeCompletedProcedureAsync(new ProcedureChargeRequest(patientId, planItemId, correlationId), actor, ct)`** from the completion, on the same `AlveraDbContext` and inside a transaction the completion opened (`await using var tx = await db.Database.BeginTransactionAsync()`; complete the procedure and write its clinical history in that transaction; call the contract; commit). It refuses to run outside a transaction (`InvalidOperationException`) and it saves whatever has been staged in that transaction before it checks the procedure is completed, so the completion need not save first.
- **It is idempotent per procedure.** A replayed completion returns the existing charge with `Created = false`; never create a charge any other way for a completed procedure (`RecordChargeAsync` is billing's own path and refuses a second charge for the same procedure: `already_billed`).
- **Reading the result:** `Created`, `Reversed` (a reversal has since undone the charge: it is still the procedure's one charge and is not made again), `ChargeEntryId`, `EntryNumber`, `Amount` (always the fee the plan copied when it proposed the procedure; there is no amount in the request), `CorrelationId`.
- **Refusals are `BillingException`s with stable codes:** `plan_item_not_found` (404), `item_withdrawn` and `not_completed` (409), `save_failed` (503, which leaves neither the charge nor the completion behind when the caller rolls back). Two completions racing for the same procedure end with one charge: the loser throws `ConcurrencyConflictException`; roll back and retry to receive the existing charge.
- **The contract test is the model to copy:** `BillingProcedureChargeContractTests` completes with `TreatmentPlanService.CompleteItemAsync` and charges inside one transaction on one context.
- **A procedure that is completed more than once** (STORY-015 allows completing only once; `Completed` is final) cannot be billed twice.
- **Declined or unfinished procedures are not billable.** Only a completed, not withdrawn plan procedure of that patient can be charged.

## For later billing stories (ALV-007-C02: statements, receipts, richer refunds)
- The ledger model they extend: entries (Charge, Payment, Adjustment, Reversal) never edited; allocations append-only; responsibility history; billing-owned methods. Derived numbers (`BillingLedgerMath`) are rebuilt from the history, so statements and receipts should call the same math rather than add up the screen's figures.
- Refunds and credit balances are not supported (a payment cannot exceed the balance); adding them changes the "no negative balance" rule (trigger 51126 and the service checks) and needs a decision.
- Re-billing a procedure whose charge was reversed needs a new rule: the unique index on `PlanItemId` currently makes a reversed charge the procedure's permanent charge.
- A guarantor paying for several people records one payment per account today; a spread across accounts is a new command.
- The payment kinds (Cash, Check, Card, Other) are fixed; named methods map to one. Processors and insurance are future.

## Shared infrastructure and conventions
- Trigger error numbers **51144 and up are free**. Billing owns 51118-51143.
- The account view separates what was recorded (`entries`) from what it stands for now (`standing`); keep that split in anything new that shows entries.
- A new secondary panel in the Billing tab should follow the pattern here: loaded quietly, validated for shape, left out (never an alert) when it cannot be read.
- Test helpers: `BillingTestBase` (a plan with a completed, an accepted and a withdrawn procedure; rows written straight to the database; `SqlRefusal`).

## UI extension points
The Billing tab (`src/pages/billing/`) composes `BillingPanel` from small components; the ledger table takes optional per-row actions; `PaymentMethodsSettings` is the pattern for domain-owned configuration.

## Unresolved decisions to carry
Re-billing a reversed charge; a patient picker for the responsible party; credit balances and refunds; whether a guarantor payment should be spread across several accounts in one step.

## Prompt changes recommended for the next scheduled items
- `ALV-008-C01`: name `IProcedureCharging` explicitly as the way to make the financial effect, and the rule that completion must call it inside its own transaction.
