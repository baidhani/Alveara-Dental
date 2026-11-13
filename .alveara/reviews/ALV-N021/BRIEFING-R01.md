# ALV-N021 pre-implementation briefing review — R01

**Reviewed artifact:** `docs/design/ALV-N021_PRE_IMPLEMENTATION_BRIEF.md`  
**Decision:** **CHANGES_REQUIRED — do not begin implementation or test execution**  
**Date:** 2026-11-12  
**Review scope:** Independent pre-implementation review under Execution Plan v4.

## What is good

The briefing correctly identifies the UTC date-only defect, distinguishes date-only values from instants, calls for server-side scheduling enforcement, and treats Calendar/Visit Board parity, authorization, history, and concurrency as one workflow rather than unrelated UI defects. It also correctly avoids authorizing a broad regression for this package.

## Blocking correction 1 — required source evidence is missing

The binding pre-implementation-briefing rule requires every relevant finding, question, and observation to reproduce its **full raw-log entry**, including the source heading and observed-result detail. Section 4 contains only identifiers and summary groupings. Section 5 and the register contain shortened excerpts, not the required source material.

Before resubmission, append an immutable source-evidence appendix that contains the complete raw entries from the approved Manual E2E source for every item in scope: PKG-04's F-030, F-100, F-107, F-118, F-184, F-133 and F-218; and every PKG-05 item listed in section 4, including F-044b through F-077 as applicable, Q-065a, F-170, and D-01a. Preserve each source heading, identifier, observation/result, severity where present, and recommendation/question verbatim. Do not replace this with a disposition-register excerpt, paraphrase, or link.

## Blocking correction 2 — the visit-step decision must be explicit and safer

The current matrix contains a sensible direction but leaves the user-facing visit sequence and its authority split ambiguous. In particular, "the clinical team moves to CheckedOut" is not an adequately defined step, and a completed visit must not depend on a role seeing an action that is hidden from it.

Revise the briefing to state the proposed workflow and testable action labels exactly as follows, then obtain the owner's confirmation of the open decisions:

1. **Normal path:** `Scheduled → Confirmed (optional) → Checked in → Ready → Seated → In treatment → Ready for checkout/Checked out → Completed`.
2. **No shortcuts:** remove `Checked in → In treatment` and `Checked in → Completed`; remove every completion path except `Checked out → Completed`. The UI never renders an unavailable later action as enabled.
3. **Action ownership:** front desk may confirm, check in, undo a check-in, and perform the administrative close (`Checked out → Completed`). A clinical participant may mark ready and seat a patient only if they hold the relevant chairside permission. The assigned clinician/provider may start treatment and end treatment / mark ready for checkout. A covering dentist may do those provider actions only when the server verifies the appointment-specific coverage/authority rule. No role may advance another provider's visit merely because it holds a broad chairside permission.
4. **User wording:** do not label a clinical action "Check out" if it represents the end of chairside treatment. Label it **End treatment / ready for checkout**. If the persisted state must remain `CheckedOut` for compatibility, the history and screen label must still explain that it is awaiting administrative completion.
5. **No dead ends:** each allowed actor sees exactly its next permitted action and a clear "awaiting [role/action]" status when another role must act. Calendar and Visit Board derive the same actions from the same server-authorized transition result.
6. **Corrections:** allow front desk to undo check-in only before the patient is seated or treatment begins; require a reason and record actor, from/to values, reason and timestamp in history/audit. Permit an authorized front-desk user or administrator to correct a no-show back to a schedulable state only with a required reason, full history/audit, and no silent alteration of the original no-show event. Rebook and Open patient must be offered on cancelled/no-show visits.
7. **Appointment-day rule:** ordinary check-in is available only on the appointment's practice-local calendar day. Do not implement an undefined "confirmation otherwise" exception. If an exceptional early/late check-in is required later, it needs a separately authorized permission, reason, audit, and explicit test.

The resubmission must map this matrix to every affected permission, default role grant, API operation, visible action, denial behavior, history entry, and concurrency/stale-view behavior. Include a complete transition table for every state/role combination, not only the happy path.

## Blocking correction 3 — owner decisions cannot remain implicit

The brief correctly lists PD-05, PD-06, F-044b, F-050, F-057 and F-073, but implementation of PKG-05 cannot start while its core policy decisions are undecided. Mark PKG-04 as independently implementable if desired, but either obtain and record the owner's decisions for PKG-05 or split it into a separate later attempt.

Recommended owner defaults for the next revision:

- retain the normal step sequence above, with no shortcuts;
- final administrative completion is front-desk owned, while the assigned clinician ends treatment/marks it ready for checkout;
- use the appointment-specific assigned clinician rule, with verified covering-dentist authority—not a role-wide ability to finish another provider's visit;
- make cancellation category required, using a controlled category plus optional free-text note; derive “short notice” from the cancellation time versus appointment time rather than trusting a manually chosen label;
- require a reason for undo-check-in and no-show correction;
- do not change allergy/medication authority in this story until PD-06 is separately decided and escalated.

## Required proof additions

The revised proof plan must explicitly prove:

- the full normal path, every prohibited shortcut, terminal-state behavior, and every correction path;
- server denial (not merely hidden buttons) for each unauthorized role and cross-provider action;
- a concurrent attempt to transition the same appointment, returning a clear stale/conflict outcome without duplicate history;
- Calendar/Board parity from the same underlying transition response;
- a user-facing, accessible explanation of the next responsible role when an action is unavailable;
- practice-local day boundaries for check-in, including a time-zone/DST boundary; and
- mandatory-reason persistence and immutable history/audit for undo/no-show/cancellation correction.

Keep the existing focused-first test policy: no chain run, broad suite, or other run expected to exceed five minutes without the owner's explicit permission. The Stage 1 review must approve the code/evidence plan before any frozen Stage 2 run.

## Resubmission path

Resubmit the corrected briefing as a new briefing review attempt. No implementation, tests, schema work, or migration is authorized by this review.
