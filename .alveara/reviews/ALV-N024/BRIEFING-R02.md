# ALV-N024 — briefing R02 independent review

**Package:** `Alveara_Briefing_ALV-N024_871c4e9_R02.zip`  
**SHA-256:** `946AE52645BB24962DD14FEE1CAA561B922B7DD559B017D043BF9B73E63A6192`  
**Decision:** **APPROVED — implementation is authorized.**

## R01 corrections resolved

1. **Receipt concurrency:** Every receipt issuance is serialized by a transaction-owned, payment-keyed `sp_getapplock`; the database trigger takes the same lock for direct-SQL bypasses. `READCOMMITTEDLOCK` is correctly required under the repository's snapshot-read configuration so a waiting trigger re-reads the winner's committed row. The specified first-receipt, replacement, bypass, replay, rollback, timeout, and lock-release proofs are sufficient.
2. **CSV versioning:** The CSV is explicitly a live, non-persisted export derivative. It is outside `ContentHash` and every historical issued document still rebuilds using its own frozen version. Re-exported v1/v2 statements receive the current safe export structure but retain their historical statement semantics; their `ReversedBy` field is blank rather than recomputed. The stated tests make that boundary verifiable.

## Approved design decisions

- Receipt replacement applies only to receipts; a later statement is a new cut-off, not a replacement.
- `FinancialDocuments` receives the additive replacement self-reference/reason fields, single-replacement index, guards 51294–51297, and the refusal-to-Down condition described in section 4.
- For guard 51295, the newly inserted candidate must be excluded when determining whether a **prior other** receipt exists for the payment; its direct-SQL concurrency test is the proof that this comparison and the lock are implemented correctly.
- A payer may be the patient, recorded guarantor, recorded household member, or an already-recorded responsible party. “Verified” means recorded through the existing audited relationship workflow; there is no new verification flag or arbitrary patient search.
- Historic duplicate receipts remain unchanged and receive only the read-only legacy notice.
- Responsibility may be recorded before any charge, with no ledger/document side effect; payment-method settings remain in the patient billing area until ALV-N027.

## Implementation and evidence boundary

Implement only the R02 briefing and its companion documents. Run the focused Stage 1 groups separately under the existing five-minute policy, including the repeated concurrency proof and the isolated real-backend billing-integrity spec. Do not run a broad/full suite or package-level Stage 2 regression; ALV-N024 remains `DEFERRED_TO_ALV-N028` and is not release-certified.

Any newly discovered financial invariant, concurrency behavior, payer-access expansion, data migration issue, or document-version ambiguity must stop for independent review before code proceeds.

