# ALV-007-C02 — Design Proposal for Independent Review

**Story:** ALV-007-C02 — Statements, Receipts, Refunds, Financial Corrections, and Account UX Hardening (execution item 35)
**Type:** Companion to course story STORY-007 (portal-verified)
**Document status:** PROPOSAL. No ALV-007-C02 code has been written. This document asks the reviewer to approve, amend or reject the design and the ten decisions in section 9 before implementation starts.
**Prepared:** 2026-10-12, session CC-20261008-k4q9
**Repository state when written:** `main` at `593ff2c` (pushed; local and remote level). Dependencies are all `COMPLETE`: STORY-007 (portal-verified at `3471727`), ALV-007-C01 (approved R01, closure `174aa88`) and ALV-008-C01 (approved R01, closure `593ff2c`). The next item after this one is 36 (`ALV-008-C02`, retire the legacy completion paths).

**How to respond.** Section 12 is a response template. For each decision D1–D10 reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead). Anything not answered is treated as "recommendation not yet approved" and will be raised again rather than assumed.

---

## 1. What the story requires (from the master execution plan, item 35)

**User story.** As billing staff, I want complete account operations and customer-facing financial documents so that balances, corrections, receipts and statements remain understandable after real completed treatment creates charges.

**Implementation scope.**
- Statement/invoice generation from preserved ledger history.
- Receipt generation for recorded payments.
- Refund workflow represented through preserving financial transactions/reversals.
- Void/reversal/correction reasons and attribution.
- Allocation correction without erasing original financial events.
- Financial timeline joining charges, payments, adjustments, reversals and procedure sources.
- Account aging/current balance presentation as derivable views where implemented.
- Print/export-safe statement and receipt representations.
- Guarantor responsibility presentation across account documents.

**UI/UX deliverables.** Statement/invoice preview and print; payment receipt preview/print; a refund/reversal/correction workflow with reason and confirmation; an account timeline with links back to completed procedure/charge sources; a clear distinction between original, reversed and replacement transactions.

**Security, audit and integrity.** Billing permissions; financial documents reflect ledger truth and **do not become an alternate source of balance**; refund/reversal/correction actions audited; original financial transactions preserved.

**Failure paths named.** A statement generated while the source changes; a refund exceeding the eligible payment "according to documented policy"; a duplicate refund/reversal submission; an allocation-correction conflict; a print/render/export failure.

**Acceptance / stop condition.** Statements and receipts can be generated from authoritative ledger history; a refund/reversal preserves the original transactions and a reconstructable balance; a duplicate submission cannot duplicate a refund/reversal; the account timeline links financial effects back to source completed care where applicable; guarantor responsibility is reflected consistently; the ALV-007-C01 and ALV-008-C01 regression suites stay green.

**Required tests.** Statement/receipt snapshot tests; refund/reversal tests; allocation-correction tests; duplicate-submit/idempotency tests; ledger reconciliation tests; the upstream financial and procedure regression suite.

**Quality Gate D criteria this story feeds (`.alveara/QUALITY_GATES.md`).**
- **D1:** the ledger reconciles from preserved transactions — an automated test, `sum(charges) − sum(payments/adjustments) = balance`, on a seeded set of **at least 50 transactions**, exact to the cent.
- **D3:** statement/receipt/refund/reversal/account UX hardening — a walkthrough of refund, reversal and statement generation on a seeded account with mixed history; **all corrections preserve full history, no destructive edit**.
- **D5:** financial histories survive duplicate submissions, corrections and stale edits without destructive overwrite.

**Out of scope (per the plan).** Integrated or online payment processing, dental insurance and clearinghouse workflows.

---

## 2. Verified current state (what exists today, with where to look)

Read from the code in this session.

| Area | Fact | Where |
|---|---|---|
| Entries | Append-only `BillingEntries`, kinds `Charge`, `Payment`, `Adjustment`, `Reversal`; a charge is `> 0`, a payment `< 0`, an adjustment or reversal `<> 0`; a charge names its plan item (one charge per procedure, a unique index); entries carry a correlation id; a payment carries its method (kind plus the configured method) and who paid | `Architecture/Billing/BillingAccount.cs`, `Data/BillingModel.cs` |
| No credit | An entry may not leave the balance below zero (trigger 51126); a payment is at most the balance owed | `AddBilling` migration, `BillingService.cs` |
| Reversals | A reversal is a new entry **exactly** the opposite of one earlier entry, with a reason (trigger 51140); once per entry; never of a reversal; **a charge that payments are applied to cannot be reversed until those payments are** (trigger 51141); reversing a payment releases its allocations by arithmetic (reversed payments are excluded when counting what a charge has been paid) | `BillingService.Ledger.cs`, `BillingLedgerMath.cs` |
| Allocations | Append-only `BillingAllocations` (payment to charge, amount `> 0`, never more than the payment holds or the charge is, triggers 51127-51132); a payment can be applied later; there is no way to **move or reduce** an allocation | `BillingService.Ledger.cs`, `AddBillingLedger` migration |
| Math | `BillingLedgerMath` derives balance, what is reversed, what is applied, open charges, unapplied amounts and `Verify` (reconciliation) from the preserved history | `BillingLedgerMath.cs` |
| Responsibility | Billing records who is responsible for an account (append-only, from a date, with who and why); until recorded the account follows the patient's registered guarantor, else the patient; a guarantor summary lists the accounts they answer for | `BillingService.Responsibility.cs`, `GetGuarantorSummaryAsync` |
| Views | The account view separates `entries` (as recorded) from `standing` (what each stands for now), with allocations, unapplied total and open-charge total | `BillingRules.cs` (`BillingAccountView`) |
| Procedure source | A completed procedure's authoritative record (ALV-008-C01) carries the plan revision, provider, encounter and performed time (current values are the latest amendment's), joined to its charge by the plan item; a charge made by a completion carries the set's correlation id | `CompletedProcedures`, `CompletionSets`, `BillingEntries.CorrelationId` |
| UI | The Billing tab composes `BillingPanel` from small components (ledger table with per-row actions, payment and adjustment and reversal forms, allocation chooser, responsibility, guarantor table, payment-method settings) | `src/alveara-client/src/pages/billing/` |
| Triggers | Billing owns error numbers 51118-51143 and ALV-008-C01 51144-51156; **51157 and up are free** | `.alveara/BUILD_STATE.md` |
| Carried decisions | ALV-007-C01's handoff recorded: refunds and credit balances are unsupported; re-billing a reversed charge is unsupported (a reversed charge stays the procedure's one charge); a guarantor paying for several people records one payment per account | `.alveara/handoffs/ALV-007-C01/R01-evidence/NEXT_STORY_IMPACT.md` |

---

## 3. The gaps this story must close

1. A **refund** cannot be recorded: only a payment's exact reversal exists, which cannot return part of a payment and does not say how or to whom money was returned.
2. An **allocation cannot be corrected**: a payment applied to the wrong charge can only be undone by reversing the whole payment.
3. A **wrong payment or adjustment cannot be corrected in one step** with the original, the reversal and the replacement visibly linked.
4. **No documents**: there is no statement or receipt, no reproducible representation, no print or export.
5. **No timeline**: the entries are listed but not joined to the completed care, provider and encounter that produced a charge, nor to allocations and corrections.
6. **No aging** or current-balance presentation.
7. Guarantor responsibility is recorded but **not shown on any document**.

---

## 4. Proposed design

### 4.1 New and changed data (one migration)

**`BillingEntries` gains a kind and two links.** Kind `Refund` (money returned to a payer: amount `> 0`, which raises the balance owed, like a reversal does). Columns:
- `RefundsEntryId` — the payment being refunded (a Refund names exactly one payment; set only on a Refund).
- `ReplacesEntryId` — set when a payment or adjustment is recorded as the replacement of an entry just reversed (set only on a Payment or Adjustment).
The payment-method columns (`PaymentMethod`, `PaymentMethodId`) and `PaidByPatientId` become allowed on a Refund as well as a Payment (how it was returned, and to whom). Check constraints are replaced to know the new kind (the same approach ALV-007-C01 used for `Reversal`).

**`BillingAllocationReleases`** — append-only: `Id`, `AccountId`, `PatientId`, `ReleaseNumber` (per account), `AllocationId`, `Amount > 0`, `Cause` (`Refund` or `Correction`), `RefundEntryId` (set exactly for `Refund`), `IdempotencyKey`, `ReleasedAtUtc`, `ReleasedByUserId`, `Reason`. A release is the explicit negative of part of an allocation. What a charge has been paid is the sum of its allocations **minus** their releases, excluding reversed payments (as today); what a payment has applied is likewise net of releases. Nothing is edited or deleted.

**`FinancialDocuments`** — append-only record that a statement or receipt was **issued**: `Id`, `AccountId`, `PatientId`, `DocumentType` (`Statement`, `Receipt`), `DocumentNumber` (per account and type, `S-1`, `R-1`...), `PaymentEntryId` (receipts), the **cut-offs** that fix its content (`AsOfEntryNumber`, `AsOfAllocationNumber`, `AsOfReleaseNumber`, `AsOfResponsibilitySequence`, `AsOfDate` for aging), `ContentHash` (SHA-256 of the canonical content), `IdempotencyKey`, `CorrelationId`, `IssuedAtUtc`, `IssuedByUserId`. It stores **no amounts or balances** (so it cannot become an alternate source of truth); it stores what is needed to regenerate the same document and to detect any difference.

### 4.2 Database enforcement (new triggers, numbers 51157 and up)

| Rule | Mechanism |
|---|---|
| `BillingAllocationReleases` and `FinancialDocuments` cannot be changed or deleted | `INSTEAD OF UPDATE, DELETE` triggers that `THROW` |
| A release never exceeds what its allocation still holds (allocation amount minus earlier releases) | trigger on insert |
| A release caused by a refund names the refund entry, a correction release names none; both belong to the allocation's account and patient | trigger on insert |
| The allocation caps of ALV-007-C01 (a payment cannot apply more than it holds, a charge cannot be paid more than it is) now count **net** of releases (the existing triggers 51130 and 51131 are replaced, keeping their numbers and messages) | trigger replacement |
| A refund is for a payment of the same account, is at most that payment's refundable amount (payment minus earlier refunds), and the payment is not reversed | trigger on insert (entry) |
| **A payment that has been refunded cannot be reversed**, and a reversed payment cannot be refunded (otherwise the same money would be returned twice) | trigger (extends the reversal triggers) |
| A refund's amount is a positive number, carries a reason and a method, and names its payment | check constraints |
| `ReplacesEntryId` names an entry of the same account that has been reversed, and is only on a Payment or Adjustment | trigger and check |
| Document numbers are unique per account and type; a receipt names a payment of its own account | unique index and trigger |

### 4.3 Operations (all idempotent by key, all in one transaction, all against the account's row version)

1. **Refund a payment** — `POST api/patients/{id}/billing/refunds`: `{ idempotencyKey, rowVersion, paymentEntryNumber, amount, reason, methodId, returnedToPatientId? }`. Documented **policy**: a refund is at most the payment's *refundable amount* (the payment minus refunds already made); a reversed payment cannot be refunded; a refunded payment cannot be reversed; a reason and a method are required; the refund first reduces the payment's **unapplied** portion and only then releases allocations, **newest allocation first** (so the oldest charges stay paid), creating one release row per touched allocation; the entry's amount is the refund, so the balance owed rises by exactly what was returned and the account still never goes below zero. The refund, its releases and its audit entry are one save; a failed audit write stores nothing.
2. **Move an application** (allocation correction) — `POST .../billing/allocations/move`: `{ idempotencyKey, rowVersion, paymentEntryNumber, from: [{chargeNumber, amount}], to: [{chargeNumber, amount}], reason }`: releases part of the payment's allocations to some charges (cause `Correction`) and applies the same total to others, in one save. The totals must match; the destination charges must have room; originals stay.
3. **Correct a payment or adjustment** — `POST .../billing/corrections`: `{ idempotencyKey, rowVersion, entryNumber, reason, replacement: { ...a payment or adjustment } }`: reverses the entry (the existing exact reversal, with its rules) and records the replacement with `ReplacesEntryId`, in one save. A charge is corrected by a reversal only (re-billing a reversed charge remains out of scope, see D4).
4. **Reversal** — unchanged (`POST .../reversals`), except that a refunded payment now refuses it with a stable code.

Each of these checks the account version first (a stale one is the shared 409), then the policy, then writes. A replay of the same key returns the saved result; the same key with a different request is a conflict (the same canonical-hash pattern as ALV-008-C01).

### 4.4 Statements and receipts (derived, reproducible, never a source of balance)

- **A document is a pure function of the ledger at its cut-offs.** The statement lists the entries up to `AsOfEntryNumber`, the allocations up to `AsOfAllocationNumber`, the releases up to `AsOfReleaseNumber` and the responsibility at `AsOfResponsibilitySequence`, with each entry's standing **as of that cut-off** (a reversal recorded later does not appear on an earlier statement). So a statement generated while the account is changing is internally consistent, and regenerating it later yields the same content. Capturing the cut-offs and reading the history happen in one read.
- **Preview** (`GET`) computes the document for the current cut-offs and records nothing. **Issue** (`POST`, idempotent by key) records a `FinancialDocuments` row with the cut-offs and the content hash and audits it. **Reprint** of an issued document recomputes from its stored cut-offs and compares the hash; a difference is reported as a `document_content_changed` error rather than printed (it should be impossible; it detects tampering or a bug).
- **Statement content:** the practice and the patient, who is responsible (see 4.7), the period or the cut-off, each entry with its kind, amount, running balance and standing (original, reversed by #n, reversal of #n, refund of #n, replacement of #n), allocations (which payment paid which charge), the balance owed, unapplied payments, and the aging buckets (4.6). The estimate wording rule of earlier stories applies: it says what the figures are (the practice's fees; no insurance estimate).
- **Receipt content:** the payment (number, date, amount, method, who paid), what it was applied to and what remains unapplied, refunds made against it, and the account balance at the receipt's cut-off. A later reversal or refund of that payment does not rewrite an issued receipt; reprinting shows the receipt as issued **plus** a visible note of what has happened to the payment since (computed, not stored).
- **Print and export:** an HTML representation with a print stylesheet (the browser prints or saves it as a PDF) and a CSV export of the statement lines. No PDF-generation library is added (that would be a new dependency and an escalation).

### 4.5 The account timeline (a derived view)

`GET .../billing/timeline`: the entries in order, each with kind, amount, running balance, standing, who recorded it and when and why, its allocations and releases, **and the source of a charge**: when the charge's plan item has an authoritative completed-procedure record, the procedure, provider, performed time, encounter and plan (with ids so the screen can link to the clinical history and the treatment plan); when it was completed the old way, "completed without a completion record" is shown plainly. Original, reversed, reversal, refund and replacement are distinguished in words (not colour alone).

### 4.6 Aging (a derived view)

Open charge amounts (the charge minus what has been paid net of releases, excluding reversed charges) bucketed **Current (0-30), 31-60, 61-90, over 90 days** as of a date (default today by the practice clock). The age of a charge is measured from the **performed time** of its completed procedure (the current value, so a correction moves it) when an authoritative record exists, otherwise from the date the charge was recorded (D8). Unapplied payments are shown separately, not netted into a bucket. Nothing is stored.

### 4.7 Guarantor responsibility on documents

A statement is addressed to the **responsible party at its cut-off** (recorded responsibility, else the registered guarantor, else the patient) and names the patient; a receipt shows who paid and the responsible party. A guarantor sees, beside their own account, a derived household summary of each account they answer for (the existing guarantor summary) with a link to each account's statement. There is **one statement per account** (the ledger's unit); a consolidated multi-account statement is out of scope (D7).

### 4.8 API

| Route | Permission | Notes |
|---|---|---|
| `POST api/patients/{id}/billing/refunds` | `ManageBilling` + CSRF | 4.3 (1) |
| `POST api/patients/{id}/billing/allocations/move` | `ManageBilling` + CSRF | 4.3 (2) |
| `POST api/patients/{id}/billing/corrections` | `ManageBilling` + CSRF | 4.3 (3) |
| `GET api/patients/{id}/billing/timeline` | `ViewBilling` | 4.5 |
| `GET api/patients/{id}/billing/aging?asOf=` | `ViewBilling` | 4.6 |
| `GET api/patients/{id}/billing/statement-preview` | `ViewBilling` | 4.4, records nothing |
| `GET api/patients/{id}/billing/receipt-preview?payment=` | `ViewBilling` | 4.4, records nothing |
| `POST api/patients/{id}/billing/documents` | `ManageBilling` + CSRF | issue a statement or receipt (idempotent) |
| `GET api/billing/documents/{id}` and `.../csv` | `ViewBilling` | reprint and export of an issued document (hash checked) |

Stable codes: the existing billing codes plus `refund_exceeds_refundable`, `payment_reversed`, `payment_refunded`, `allocation_move_mismatch`, `document_content_changed`, `idempotency_conflict`, `save_failed` (503, no internals). Every request is validated at the boundary with explicit field errors.

### 4.9 UI (Billing tab)

An **Account timeline** (replacing nothing: the existing ledger table stays for the parent contract, the timeline is an additional view with source links); **Refund**, **Move application** and **Correct payment** actions on the relevant rows, each a form with a reason and an explicit confirmation that states the effect in words ("Refund $40.00 of payment 3: payment 3's application to charge 2 is reduced by $25.00 and the balance owed rises by $40.00"); **Statement** and **Receipt** previews with Issue and Print and CSV; an **Aging** card. Every failure says nothing was recorded and keeps what was entered. A document that cannot render says so and never shows a partial one.

### 4.10 Security, audit and observability

Billing permissions only (no new permission, D9); CSRF on every write; PHI-free audit entries (kind, entry numbers and amounts, no names or clinical wording) in the same save as the effect, a failed audit write storing nothing; issuing and reprinting a document are audited; structured logs with correlation ids and error classes only. Documents contain patient-identifying content by nature, so they are generated only on request, never logged, and the CSV and HTML carry no data the screen would not show to the same user.

### 4.11 Failure-first answers

1. **If it fails:** nothing is stored (one transaction); the caller is told (`save_failed`, 503) and may repeat the same request.
2. **Retry:** the same key and request replays; a repeated refund or correction can never duplicate (key plus the unique constraints plus the refundable-amount rule).
3. **Recovery:** manual; logs carry the correlation id, error class and SQL error number.
4. **Handled:** stale account, refund above the refundable amount, refund of a reversed payment, reversal of a refunded payment, move with mismatched totals or no room, replay, two simultaneous refunds of one payment (one wins, the other sees the reduced refundable amount or a conflict), a document requested while entries are being added (cut-offs), a document whose hash no longer matches, a render or export failure (no partial output, nothing recorded for a preview). **Not handled:** online refunds or card processors; re-billing a reversed charge; a consolidated multi-account statement.

---

## 5. Parent regression strategy

The STORY-007 and ALV-007-C01 contracts stay: existing routes, request and response shapes (new fields are additive and optional), the exact-reversal rule and the entry rules are unchanged; the only changes to existing database rules are the **net-of-releases** arithmetic in triggers 51130 and 51131 (identical results when there are no releases) and the two refund/reversal guards. The STORY-007 and ALV-007-C01 backend, frontend and walkthrough suites, and the ALV-008-C01 suites (a completion's charge appears on the timeline with its source), run unchanged. Any test that must change is listed in the handoff with the reason; the intent is zero.

---

## 6. Failure paths → design → test

| Failure path | Behaviour | Proving test |
|---|---|---|
| Statement generated while the source changes | the document is a function of captured cut-offs; later entries and reversals do not appear on it; reprint is identical | add entries between preview and issue and after issue; compare content and hash |
| Refund exceeds the eligible payment | refused with `refund_exceeds_refundable` naming the refundable amount; nothing stored | refund above the payment, above payment minus an earlier refund, of a reversed payment |
| Duplicate refund or reversal submission | the same key replays; a different request under the key conflicts; a second refund cannot exceed the refundable amount | repeat sequentially and concurrently |
| Allocation-correction conflict | stale account version is a 409; a move to a charge with no room or with unequal totals is refused | concurrent moves, mismatched totals |
| Print/render/export failure | no partial document; a preview records nothing; an issue that fails stores nothing | a forced render failure; a failed audit write on issue |
| Refund then reversal of the same payment | refused (`payment_refunded`) | direct database and service tests |

---

## 7. Acceptance mapping and test plan

| Acceptance | Evidence planned |
|---|---|
| Statements and receipts can be generated from authoritative ledger history | snapshot tests of the canonical content at several cut-offs; hash stability; the reprint check; walkthrough preview, issue, print and CSV |
| Refund/reversal preserves the original transactions and a reconstructable balance | refund and correction tests that assert every earlier entry and allocation is byte-identical afterwards and that `Verify` reports no finding; **a D1 reconciliation test over at least 50 transactions including refunds, corrections and moves, exact to the cent** |
| Duplicate submission cannot duplicate a refund/reversal | idempotency and concurrency tests (D5) |
| The timeline links financial effects back to source completed care | tests that a completion's charge shows its procedure, provider, performed time and encounter, and that an old-way completion says so; the walkthrough follows a link |
| Guarantor responsibility is reflected consistently | statement, receipt and household summary tests for the default, a recorded and a changed responsibility |
| ALV-007-C01 and ALV-008-C01 suites stay green | the full serial backend suite, frontend suite and the whole real-backend sweep |

Additional planned evidence: schema tests for every new trigger and check (written around the service); migration Down and Up before data; fifteen or more negative controls (mutate each rule: refundable amount, newest-first release, net-of-releases caps, the reversal guard, the cut-off filters, the hash comparison, idempotency, the move totals, aging buckets and basis) with at least one deterministic test per guard that another guard also covers (the lesson of the ALV-008-C01 survivor); frontend tests with axe; a real-browser walkthrough with a failure path and a stale second screen; the full sweep with leak counts. Packaged per `.alveara/HANDOFF_SCHEMA.md`; the story ends at `AWAITING_REVIEW`.

---

## 8. Alternatives considered and rejected

- **Partial refunds as adjustments.** Rejected: an adjustment has no link to the payment, cannot say how money was returned, and would make a statement unable to show "refund of payment 3".
- **Reversing a payment partially.** Rejected: ALV-007-C01's approved rule is that a reversal is exactly the opposite of one entry.
- **Deriving which allocations a refund releases instead of storing releases.** Rejected: a derived "newest first" rule would silently change what a historical charge was paid if the rule is ever revisited; explicit release rows keep history exact and also serve allocation correction.
- **Storing the statement text or amounts.** Rejected: it would become a second source of balance (the story forbids it); only cut-offs and a hash are stored.
- **A PDF library.** Rejected for now: a new dependency; browser print covers print and PDF-by-print.
- **Allowing credit balances.** Rejected: it changes the approved "no negative balance" rule; a refund here only returns money that the ledger already counts as paid, and never creates credit.

---

## 9. Decisions requested from the reviewer

**D1 — Refund model.** *Recommend:* a new `Refund` entry kind plus explicit append-only `BillingAllocationReleases`; the refund names its payment. *Alternatives:* full reversal only (no partial refunds); a derived release rule with no release rows. *Consequence of the alternatives:* no partial refund, or history that depends on a rule.

**D2 — Refund policy.** *Recommend:* refundable = payment minus earlier refunds; reversed payments cannot be refunded; a refunded payment cannot be reversed; reason and method required; unapplied portion first, then newest allocation first; a refund never creates a credit. *Alternatives:* oldest-first release; allow refunding more than the payment (credit). 

**D3 — Allocation correction.** *Recommend:* "move an application" as release plus new allocation in one transaction, with matching totals. *Alternative:* only reverse the whole payment and re-apply.

**D4 — Replacement and re-billing.** *Recommend:* "correct a payment or adjustment" = exact reversal plus a linked replacement in one step (`ReplacesEntryId`); a wrong **charge** stays reversal-only, and re-billing a reversed charge remains deferred (carried from ALV-007-C01). *Alternative:* allow a replacement charge for the same procedure (changes the one-charge-per-procedure index and the ALV-008-C01 charge contract).

**D5 — Document model.** *Recommend:* derived at captured cut-offs, plus an append-only issued-document record with number, cut-offs and a content hash (no amounts). *Alternatives:* ephemeral documents with no record (no numbering or audit trail); stored snapshot content (a second source of truth).

**D6 — Print and export.** *Recommend:* HTML with a print stylesheet plus CSV, no new library. *Alternative:* a PDF library (escalation: new dependency).

**D7 — Statement scope.** *Recommend:* one statement per account, addressed to the responsible party, plus a derived household summary with links. *Alternative:* a consolidated multi-account statement for a guarantor.

**D8 — Aging basis and buckets.** *Recommend:* Current (0-30), 31-60, 61-90, over 90 days; age from the completed procedure's performed time when an authoritative record exists, else from the date the charge was recorded; unapplied payments shown separately. *Alternative:* age always from the recorded date.

**D9 — Permissions.** *Recommend:* `ViewBilling` for previews, timeline, aging and reprint; `ManageBilling` plus CSRF for issuing, refunds, moves and corrections; no new permission. *Alternative:* a separate permission for refunds.

**D10 — Reasons and attribution.** *Recommend:* keep the required free-text reason of ALV-007-C01 and show who recorded each entry, when and why on the timeline and documents; a refund additionally records how and to whom it was returned (method and, when it is not the patient, the payer). *Alternative:* a controlled vocabulary of reason categories (more screens, more tests).

---

## 10. Risks and open questions

**Risks.**
- **Trigger changes to approved rules.** Triggers 51130 and 51131 are replaced to count net of releases; the ALV-007-C01 schema tests must pass unchanged (identical results when there are no releases) and negative controls must show the new arithmetic is exercised.
- **Size:** larger than ALV-007-C01 (a migration with a new kind, two tables and trigger changes; three operations; two document types; a timeline and aging; a UI; ~150-200 tests; a walkthrough; a sweep; negative controls; a handoff). It will be built in small reversible steps and may need more than one session.
- **Legacy completions:** a charge for a procedure completed the old way has no completion record, so its timeline entry says so; this is correct until `ALV-008-C02` adopts those completions.

**Open questions.**
- **Q1.** Should a statement offer a *period* (from and to dates) as well as a cut-off, or only a cut-off? (Proposed: a cut-off, with an optional "since" that only filters the lines shown, never the balance.)
- **Q2.** Should a receipt be an issued record with a number (proposed) or derive its number from the payment's entry number with no record?
- **Q3.** When a refund returns money paid by someone other than the patient (a guarantor), is the default payee that payer? (Proposed: yes, editable.)

**Not verified yet (will be verified during implementation, and reported either way).**
- That replacing triggers 51130 and 51131 with net-of-releases arithmetic leaves every ALV-007-C01 schema test passing unchanged.
- That the existing Billing tab components can host the timeline and the new forms without growing past the size rule (a file near 300 lines is split before it is extended).
- How the browser's print output behaves for a statement with many lines (page breaks); this will be checked in the real browser.

---

## 11. Work plan and packaging

1. Migration: the `Refund` kind and links, `BillingAllocationReleases`, `FinancialDocuments`, constraints and triggers (51157+), schema tests.
2. `BillingLedgerMath` and `Verify` extended for refunds and releases (pure, with unit tests), then the refund, move and correct operations.
3. The derived views: timeline, aging, statement and receipt content (pure functions of cut-offs), issue and reprint with the hash check.
4. API routes and contract tests.
5. UI: timeline, the three forms, statement and receipt preview and print, aging.
6. Tests: the D1 reconciliation set (50+ transactions), idempotency and concurrency, failure rollback, the parent regression; a real-browser walkthrough with a failure path and a stale second screen; the full serial backend run, frontend gates, the sweep, negative controls, migration Down and Up.
7. Docs and the handoff package per `.alveara/HANDOFF_SCHEMA.md` (implementation commit, evidence commit, status commit, validated ZIP); state `AWAITING_REVIEW`.

Work is paced in small reversible steps; nothing is pushed without the owner's instruction; no secrets or real patient data are introduced.

---

## 12. Reviewer response template

```
ALV-007-C02 design review — decision: APPROVED / APPROVED WITH CHANGES / CHANGES REQUIRED

D1  refund model (Refund kind + release rows):        APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D2  refund policy:                                     APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D3  allocation correction (move):                      APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D4  replacement of payments/adjustments; charges stay reversal-only:  APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D5  document model (cut-offs + issued record + hash):  APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D6  print/export (HTML + CSV, no new library):         APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D7  statement scope (one per account):                 APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D8  aging basis and buckets:                           APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D9  permissions (no new permission):                   APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D10 reasons and attribution:                           APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___

Open questions Q1-Q3: answers or "proceed with the proposal".
Additional requirements or risks the reviewer wants covered: ___
```
