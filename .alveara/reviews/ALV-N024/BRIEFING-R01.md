# ALV-N024 — briefing R01 independent review

**Package:** `Alveara_Briefing_ALV-N024_7e0dfb8_R01.zip`  
**SHA-256:** `1F7B19E07F379C034818E009EA69F2E5F9ED4A8E6CAFB1EA570C9B8CF1810281`  
**Decision:** **CHANGES_REQUIRED**

The owner decisions are correctly captured: controlled receipt replacement, patient/guarantor/recorded-household payer eligibility, pre-charge responsibility recording, and deferral of payment-method settings to ALV-N027. The focused-proof route is also correctly used. No implementation is authorized until the following briefing corrections are made and independently approved.

## CR-1 — make “one receipt per payment” safe under concurrency

Guard 51295 as described is not sufficient by itself. Two first-time receipt inserts for the same payment can race under ordinary read-committed visibility: each trigger invocation may observe no committed prior receipt and both inserts may commit. The filtered unique index on `ReplacesDocumentId` protects two replacements of the *same original*, but does not protect two initial receipts.

Amend the design to provide a durable, transaction-scoped serialization boundary keyed by payment ID for **every receipt issuance**, including the first receipt and a replacement. The service and direct-SQL database path must enforce the same invariant. A database trigger may use a transaction-owned per-payment application lock (or an equivalently durable database mechanism); a service-only lock is not sufficient.

Add exact proofs:

- two concurrent fresh first-receipt attempts with distinct request/idempotency keys yield exactly one issued receipt;
- two concurrent replacement attempts yield exactly one replacement;
- the direct-SQL bypass path cannot create a second initial receipt;
- normal retry remains idempotent, and every failed contender leaves no partial document/audit state.

State the lock/transaction lifetime, the stable refusal or retry result, and cleanup behavior explicitly in sections 2, 4, 6, and 9.

## CR-2 — settle the historical-statement CSV version contract

The briefing says v1/v2 documents rebuild with their own builders, but also says a re-exported v1/v2 CSV gains new header formatting. That is acceptable only if the CSV is explicitly a live export derivative, not an immutable issued/document-hash artifact.

Choose and document one unambiguous rule before implementation:

1. CSV is a live export derivative: re-exports of every version may receive the safe header/time-format structure, while the issued document bytes and version-specific statement semantics remain unchanged; **or**
2. CSV is part of the historical issued rendering: v1/v2 exports remain byte-for-byte/version-for-version unchanged, and only v3 receives the new structure.

The recommended route is **1**, provided Step 0 confirms the CSV is not persisted or included in the immutable document hash. Add tests proving both the chosen CSV contract and that v1/v2 issued document rebuilds remain unchanged.

## Required resubmission

Revise the briefing and owner-decision record as needed, without code, migration, configuration, or test execution. Repackage as the next briefing attempt. The resubmission must show the precise receipt-concurrency mechanism and the finalized CSV version rule.

