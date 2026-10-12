# ALV-008-C01 Design Review Response

**Decision:** APPROVED WITH CHANGES
**Date:** 2026-10-11
**Authority:** The approved decisions are incorporated into Item 34 of `GPT Docs/Alveara_Dental_Execution_Plan_v4.md`. That plan now governs implementation; this response explains the decision.

## Required decisions

| Decision | Direction |
|---|---|
| D1 — Legacy completion endpoints | Keep the existing `complete` and `complete-accepted` routes behaviorally unchanged only for the verified STORY-008/STORY-007 regression contract. Treat them as legacy compatibility paths: do not add new callers or new product UI for them, and do not represent their state-only completion as the authoritative ALV-008-C01 completion workflow. |
| D2 — Existing quick-complete button | Keep it only if needed by parent UI regression. Make the new review-and-complete flow visually primary. If the quick control remains visible, label it as legacy completion and state that it does not record provider, encounter, chart, or charge effects. |
| D3 — Amendments | Implement append-only amendments now. Do not use a row version on immutable `CompletedProcedures`; use expected latest-amendment sequence plus amendment idempotency key/request hash and a unique sequence per completed procedure. Amendments change only provider, performed time, and encounter attribution. |
| D4 — Zero fee | A stored plan-item fee greater than zero is billable. A zero-fee item completes successfully with `charge: none` and creates no ledger entry. |
| D5 — Chart linkage | Resolve only through plan item → diagnosis → explicit linked finding, with same-patient/tooth/surface checks. No match is `NotApplicable`; more than one eligible match is `ambiguous_chart_finding` and rolls back the set. Never infer a finding from tooth/surface alone. |
| D6 — Encounter | Encounter is required, same-patient, and may be Draft or Finalized. Completion records attribution only; it never changes a finalized encounter. |
| D7 — Provider and time | Require an active provider and a practice-clock-validated performed time. It cannot be materially future-dated or precede plan creation or item acceptance. Define and test the clock-tolerance boundary. |
| D8 — Permissions and scope | Use `ManageTreatmentPlans` plus CSRF for write operations and `ViewClinicalDocumentation` for reads. One submitted set is limited to one plan. |

## Additional mandatory implementation constraints

1. The selected-set transaction must check and advance a plan-level concurrency token, or a dedicated completion-aggregate version, in the same transaction. Existing item-state rows alone do not advance the plan row version.
2. Require a non-empty, duplicate-free item list. Canonicalize the completion-set request hash from sorted item IDs and normalized encounter, provider, and UTC performed time.
3. The same idempotency key with the same canonical request replays the saved result. The same key with a different request returns `idempotency_conflict`.
4. Every billable selected item must call `IProcedureCharging` on the same scoped context and inside the same transaction. Do not write ledger rows directly or accept a caller-selected charge amount.
5. Any failed chart effect, charge, audit record, finalization persistence, or other coupled effect must roll back the entire submitted set with no residual state, completed-procedure, chart, charge, or audit record.
6. The original completed-procedure record is immutable and finalized at creation. Amendments never alter plan state, chart state, or charge history.

## Required evidence beyond Item 34’s original test list

- Duplicate item-ID refusal.
- Same-key identical replay and same-key different-payload conflict.
- Stale completion token and racing overlapping submitted sets.
- A mixed set of billable and zero-fee items.
- Rollback after a late coupled failure, proving no orphaned state, chart transition, ledger charge, or audit record.
- Amendment replay and concurrent amendment refusal.
- Draft and Finalized encounter attribution without encounter mutation.
- Legacy endpoint regression and proof that the new product UI does not direct users to those compatibility paths.

Do not begin implementation by revising the completed-procedure semantics above. Implement against the updated Item 34 and package the work at `AWAITING_REVIEW` for independent review.