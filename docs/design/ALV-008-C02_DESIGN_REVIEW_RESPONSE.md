# ALV-008-C02 — Independent Design Review Response

**Decision:** APPROVED WITH CHANGES

**Reviewed proposal:** `docs/design/ALV-008-C02_DESIGN_PROPOSAL.md`  
**Reviewed against:** Item 36 of `GPT Docs/Alveara_Dental_Execution_Plan_v4.md`, the engineering/product reference, and the current completion, billing, and odontogram implementation.  
**Scope:** This response authorizes design preparation only. It does not replace the required implementation handoff and independent review.

## Decision record

| Decision | Disposition | Binding instruction |
|---|---|---|
| D1 — retire legacy routes now | APPROVE | Retire both legacy production routes to the proposed safe `410 Gone` contract. Retain authentication, permission, and CSRF enforcement before the retired response. Delete the legacy public service methods so no production bypass remains. |
| D2 — retired-route controls and observability | APPROVE | Keep the proposed PHI-free `legacy_completion_retired_attempt` event and do not create clinical, financial, plan-state, audit-completion, or history records from a retired-route attempt. |
| D3 — remove quick completion and offer adoption | APPROVE | Remove both legacy quick-completion controls. Offer adoption only for a current, non-withdrawn legacy-completed item without a `CompletedProcedures` record. The server remains authoritative for eligibility. |
| D4 — adoption data model | APPROVE WITH CHANGES | Use a completion set with a stored adoption reason, rather than a separate adoption table or a new treatment-plan state/event. Apply the two required corrections below. |
| D5 — reuse authoritative completion rules | APPROVE | Enforce the same provider, encounter, performed-time, odontogram-resolution, billing, transactional, and concurrency rules as ALV-008-C01. Do not add an exception for legacy acceptance times. |
| D6 — adoption endpoint | APPROVE | Implement `POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` with `ManageTreatmentPlans` and CSRF. Keep clinical reads under `ViewClinicalDocumentation`. |
| D7 — reconciliation report | APPROVE WITH CHANGES | Implement the counts-only `GET api/completion/reconciliation` endpoint under `ViewAuditLog`, subject to the historical chart-evidence correction below. |
| D8 — regression strategy | APPROVE | Use the test-only fixture, committed test/walkthrough mapping table, and clearly labelled test-only browser SQL setup. Do not alter course-portal completion state. |

## Required corrections before implementation

### C1 — adoption request identity must bind the reason and operation

The adoption canonical request hash must contain all fields that distinguish an adoption request:

- a version tag;
- an explicit operation discriminator such as `adopt-completion`;
- the single plan-item identifier;
- normalized encounter, provider, and performed time; and
- the validated, trimmed adoption reason.

The same idempotency key must replay only when this entire canonical request is identical. A changed reason, operation type, provider, encounter, performed time, or item under the same key must return `idempotency_conflict` without writes.

Do not reuse `CompletionRules.RequestHash(CleanCompletion)` unchanged: it does not include an adoption discriminator or reason. Add a dedicated adoption request/cleaning/hash path, or extend the rules in an equivalently explicit and separately tested way.

Required tests include identical replay, changed-reason conflict, changed operational field conflict, and a key collision between a normal completion request and an adoption request for the same plan.

### C2 — reconciliation must use historical chart evidence, not only current state

The proposed `chartMismatch` definition based solely on a finding's current state conflicts with the proposal's own requirement that a later legitimate chart change must not make an earlier valid completion fail reconciliation.

For every completed procedure with outcome `Updated` or `AlreadyCompleted`, persist or deterministically derive durable evidence of the qualifying odontogram version observed or created at completion/adoption time. A stored finding-version number is an acceptable approach. The evidence must establish that:

- `Updated` created a valid completed finding state as part of the operation; and
- `AlreadyCompleted` observed a valid completed finding state at the operation time.

Define `chartMismatch` from that completion-time evidence. A later odontogram version must not count as a mismatch merely because the finding is no longer currently `Completed`. A missing, invalid, or inconsistent qualifying historical finding/version must count as a mismatch.

Required tests include:

1. adoption that updates an eligible finding;
2. adoption that finds an already-completed finding;
3. a legitimate later recorded chart-state change after either outcome, which leaves reconciliation clear; and
4. deliberately seeded absent or inconsistent completion-time chart evidence, which reconciliation reports as a mismatch.

## Additional implementation constraints

- Preserve the original legacy `TreatmentPlanItemStates` row, `ItemCompleted` event, and audit entry unchanged.
- The adoption command must not stage another completion state or another `ItemCompleted` plan event.
- Keep the existing atomicity rule: completion set, immutable completed-procedure record, chart effect, charge reuse/creation, plan row-version advance, and PHI-free adoption audit either all commit or all roll back.
- Preserve exactly-one-charge behavior through `IProcedureCharging`; reuse an existing manual charge for the plan item and never write a direct ledger entry.
- The handoff must include the complete parent-regression mapping table, repository-wide production caller search, reconciliation output, and the required serial/backend/frontend/browser evidence.

## Implementation authorization

The coding agent may implement ALV-008-C02 only in accordance with this response and the authoritative execution plan. Package the completed work at `AWAITING_REVIEW`; do not begin STORY-016 until independent implementation review is approved and durably closed.
