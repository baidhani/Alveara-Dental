# ALV-N023 — Pre-Implementation Briefing Review R02

**Documents reviewed:**

- `docs/design/ALV-N023_PRE_IMPLEMENTATION_BRIEF.md`, revision 2
- `docs/design/ALV-N023_SOURCE_EVIDENCE_APPENDIX.md`
- `docs/design/ALV-N023_FINDING_SURVEY.md`
- `docs/design/ALV-N023_OWNER_DECISION_RECORD.md`

**Decision:** **APPROVED FOR IMPLEMENTATION — focused-proof route only**

## R01 corrections resolved

1. **Deferred regression:** The package-level Stage 2 proposal is removed. `ALV-N023` uses `fullRegression: "DEFERRED_TO_ALV-N028"`; it may close after final focused-proof approval and does not receive a broad/full package-level run.
2. **Follow-up safety net:** The versioned `unscheduled:v2` key and immutable state/link/appointment-terminal inputs distinguish old closure history from a new waiting episode. The five required paths, seeded upgrade, closure notes, and idempotency checks are explicit. An appointment cancellation/no-show is no longer falsely described as advancing a link sequence.
3. **Finished-plan boundary:** Completed and Closed plans offer only Activity, revision history, patient copy, and the warned Revise-anyway correction path. Withdraw is unavailable and refused. Completed-procedure and billing records remain immutable.
4. **Derived state:** The ordered state/action/follow-up matrix gives a single deterministic server definition for Proposed, In progress, Completed, Closed, and Withdrawn, including all requested mixtures.

## Approved technical and product contract

The following are authorized exactly as documented in revision 2:

- The append-only `TreatmentPlanItemAppointmentLinks` migration, same-patient guard `51291`, append-only guard `51292`, and rollback-protection guard `51293`.
- A real appointment link as the only non-terminal booking state; a target date remains informational and never closes an unscheduled-treatment follow-up.
- Front desk/Office Manager link and unlink capability through `ManageAppointments`, limited to the same-patient administrative scheduling relationship. It must never grant treatment-plan, diagnosis, financial, acceptance, completion, revision, or other clinical editing authority.
- The exact contract changes for target-date confirmation, bulk unanswered-only decisions, rename reasons, finished-plan refusals, patient-copy reference code, PHI-minimized audit details, and the performed-after-encounter note.
- D-03's Diagnoses-card work remains out of ALV-N023.

## Required Stage 1 evidence

Implement only the approved briefing scope. Then provide one Stage 1 handoff at `AWAITING_REVIEW` containing the exact implementation SHA, changed-files inventory, results, durations, and evidence for every row in the source-to-acceptance table.

- Run only the listed focused groups, separately. Each group must be expected to finish within five minutes; request owner permission before any run expected to exceed that limit.
- Run the new fresh-database treatment-plan-integrity browser spec alone (target about two minutes).
- Perform the listed negative controls and preserve their restored-tree/hash evidence.
- Do not run neighbour walkthrough sweeps, a chain, Stage 2, or a broad/full regression. Record `fullRegression: "DEFERRED_TO_ALV-N028"` and `releaseCertified: false`.

No production scope beyond this briefing is authorized. Any new data invariant, permission, external integration, clinical rule, or state/action ambiguity must stop for review.
