# ALV-N023 — Pre-Implementation Briefing Review R01

**Documents reviewed:**

- `docs/design/ALV-N023_PRE_IMPLEMENTATION_BRIEF.md`
- `docs/design/ALV-N023_SOURCE_EVIDENCE_APPENDIX.md`
- `docs/design/ALV-N023_FINDING_SURVEY.md`
- `docs/design/ALV-N023_OWNER_DECISION_RECORD.md`

**Decision:** **CHANGES_REQUIRED — documents only; do not implement or run tests yet**

## What is ready

The briefing has unusually good traceability. The source appendix identifies the v6 raw-log hash and reproduces the relevant full entries; the survey locates all 26 PKG-08 rows; and the decision record faithfully carries the owner's nine decisions, including the real appointment link and front-desk scheduling boundary.

The core direction is approved in principle:

- A target date is not a booking and cannot close the unscheduled-treatment follow-up.
- A genuine same-patient appointment link is the only non-terminal booking state.
- Front desk/Office Manager may make or remove the scheduling relationship under `ManageAppointments`, without authority to edit clinical/financial/answer/revision fields.
- The planned patient-copy reference, PHI-minimized audit details, appointment/date display, and no-inference completion note remain in scope.

Return a revised, documents-only briefing that resolves the four corrections below. Do not create code, migrations, tests, a package, or run any test command before that revision is independently approved.

## CR-1 — remove the package-level Stage 2 proposal

Section 9 proposes a reviewer-frozen Stage 2. ALV-N023 is within the binding post-Gate-E stabilization exception: it uses focused proof and its broad-regression obligation is consolidated into `ALV-N028`. Therefore:

- replace the proposed Stage 2 with `fullRegression: "DEFERRED_TO_ALV-N028"`;
- state that final focused-proof approval may close ALV-N023 without a package-level broad/full suite or Stage 2;
- retain the focused Stage 1 coverage, with every command/group expected to remain at or below five minutes unless the owner expressly permits it first; and
- do not add neighbour walkthrough sweeps merely because they are available. The required new browser spec may run alone under the stated approximately two-minute estimate.

## CR-2 — make the follow-up episode/key transition mechanically coherent

Sections 4.4 and 4.5 conflict. They say old-format tasks are treated as link sequence `0`, while also requiring a new task for a legacy item that has a previously **closed** old-format task and no link. If the old key and new key are equivalent, the old closure can suppress the required reopened task. In addition, cancellation/no-show changes an appointment status but currently does not append a plan-link row, so the proposed `linkSequence` does not itself advance on that event.

Before implementation, specify one exact, durable algorithm that covers all of these paths:

1. legacy `Scheduled` item with a closed old-format task and no link after upgrade;
2. a new link, then appointment cancellation;
3. a new link, then no-show;
4. explicit unlink with its required reason; and
5. relink to a later appointment.

For each path, state the exact new task identity/key, whether a prior task is closed/reopened/left historical, and why a prior closure cannot suppress the newly required safety-net task. The solution may use a clearly versioned new key format or a dedicated persisted episode/version, but must preserve old task history without overwriting it. Add direct tests for all five paths, including the seeded upgrade case. Do not claim cancellation creates a new link sequence unless the design actually writes an immutable event/row at cancellation.

## CR-3 — align finished-plan actions with the owner's PD-14 decision

The owner chose that a Completed plan becomes history/read-only except patient copy and an explicit warned **Revise anyway** path. Section 2.1 S9 and section 4.3 nevertheless retain **Withdraw plan** for Completed/Closed plans. That is a material state change outside the stated exception and risks making a completed historical plan appear retired.

Remove Withdraw from Completed and Closed plans. A correction must use the warned revision/amendment route already selected by the owner; immutable completion and billing behavior remain unchanged. Update the state/action tests accordingly. If the implementation needs a post-completion withdrawal capability, stop and request a separate owner decision rather than silently adding it.

## CR-4 — define PD-14’s derived-state matrix exactly

"Once work has started" is not a deterministic predicate. Add a table defining the result for at least:

- all live items unanswered;
- one or more accepted items with no appointment link/target date;
- accepted item with target date only;
- accepted item with a live appointment link;
- one or more completed items while other live items remain;
- every remaining live item completed;
- every remaining item declined/withdrawn (and mixtures of completed plus declined/withdrawn);
- empty plan; and
- withdrawn plan.

The table must define the displayed state, allowed actions, and whether the unscheduled-treatment follow-up is eligible for each row. It must preserve the owner rule that only every remaining live procedure completed yields **Completed**; terminal plans without that condition use a truthful **Closed** presentation. This table is required so the server, client, follow-up job, and browser tests cannot derive divergent meanings.

## Resubmission requirements

Return an R02 documents-only package/briefing that:

1. answers CR-1 through CR-4 in the four named documents as applicable;
2. preserves the unabridged source-evidence appendix and the owner’s words; and
3. updates the Stage 1 test matrix and expected changed-file inventory for the chosen follow-up algorithm and state table.

No test or production change is authorized by this review.
