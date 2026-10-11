# Complete treatment planning: phases, answers, revisions, patient copy and reference reconciliation (ALV-015-C01)

The companion to STORY-015 (`docs/TREATMENT_PLANS.md`, whose contract is unchanged and still passes). It adds what makes a plan something a patient can understand, accept and the practice can trace: phases, the patient's answer to each procedure, scheduling and completion, saved revisions that can be compared, a plain-language patient copy that prints, and tying the old free-text "treatment plan reference" on diagnoses to real plans.

## What it is, in one paragraph

A plan's **content** (title, clinical rationale, patient explanation, phases, and which phase each procedure sits in) is saved as numbered **revisions**: every change copies the latest revision and appends the next, and the database refuses any edit of an earlier one. A procedure's **state** (the patient's answer, scheduled, completed) is a separate append-only row per change; the current state is the latest row. Procedures themselves stay immutable (STORY-015). Old diagnosis references are tied to plans by a row **beside** the diagnosis, which keeps its text and history untouched.

## Data

| Table | Purpose | Rules the database itself enforces |
|---|---|---|
| `TreatmentPlans` (new columns) | `CurrentRevision`, optional `ReferenceCode` | revision number only goes up (51100); a reference code is set once (51099), is unique per patient (case-insensitively), never blank or multi-line |
| `TreatmentPlanRevisions` | one row per saved version: change type, title, rationale, explanation, reason, who and when | append-only (51101); unique number and history entry per plan; patient matches the plan's (51110) |
| `TreatmentPlanRevisionPhases` | the phases of one revision (number 1-12, name, planned-visit words, optional target date) | append-only (51102); same plan and patient as the revision (51111); unique number per revision |
| `TreatmentPlanRevisionItems` | which procedures a revision holds, which phase each sits in, whether withdrawn by then | append-only (51103); same plan and patient (51104); a procedure can only sit in a phase of its own revision (foreign key) |
| `TreatmentPlanItemStates` | the full state after each change: Acceptance (Pending/Accepted/Declined), Scheduling (Unscheduled/Scheduled, optional date), Completion (NotStarted/Completed), reason, who gave the answer, who entered it, the revision on screen | append-only (51105); a decline has a reason; scheduled or completed needs Accepted; a date only while Scheduled; same plan and patient (51106); nothing for a withdrawn procedure (51107) or plan (51108); Completed is final (51109); only an existing revision (51112) |
| `DiagnosisPlanResolutions` | a diagnosis's old reference tied to a plan: the original text, method, who and when | append-only (51113); one per diagnosis; diagnosis and plan are the same patient's (51114); the text equals the diagnosis's reference (51115) |
| `TreatmentPlanEvents` (widened) | nine new change types and a revision number | reasons required for a decline, a revision and the withdrawals |

Also: a completed procedure cannot be withdrawn (trigger 51117) and a diagnosis whose reference has been tied keeps that reference (trigger 51116, on `Diagnoses`). Triggers 51099-51117 belong to this story; 51118 and up are free. Migration `AddTreatmentPlanCompletion` gives every plan that already exists a first revision holding its current content.

## Behaviour

- **Revisions.** Creating a plan saves revision 1; adding or withdrawing a procedure and renaming each save the next. `POST api/treatment-plans/{id}/revise` lays out the rationale, the patient explanation, the phases and the place of every procedure in one revision. It needs a reason and the plan's current version, names every wrong field in one answer (`phases[1].name`, `placements[0].phaseNumber`...), keeps what is not sent, and sending what the plan already says saves nothing. A phase cannot be removed while a live procedure sits in it unless that procedure is moved in the same revision.
- **Answers.** Accepting or declining a procedure records the signed-in staff member who entered it, optionally the name of the person who gave the answer, the plan revision on screen and, for a decline, why. The call echoes the plan's version, so what is recorded is what was on screen; repeating the same answer is quiet. An answer can be changed later (every step is kept). Answering the **whole plan** applies to the procedures still Pending and is refused as `partial_acceptance_conflict` (409) when any holds the opposite answer, so a plan is never half-overwritten. Declining a scheduled procedure takes it off the schedule.
- **Scheduling and completion.** Only an accepted procedure can be scheduled (an optional date; it records a state and does not book an appointment) or completed. Completed is final: it cannot be re-answered, rescheduled or withdrawn.
- **History and comparison.** `GET .../revisions` lists every version with what produced it, who, when, why and what the plan came to; `.../revisions/{n}` returns one exactly as saved; `.../revisions/compare?from=&to=` names the differences (title, words, phases added, removed or renamed, procedures added, withdrawn or moved) and the estimate on each side with the signed change. Fees are the ones copied when each procedure was proposed, never today's catalog price.
- **Estimate.** The practice's catalog fee total, labelled by the server on every plan, revision, comparison and the patient copy as not an insurance estimate and not a guaranteed patient cost. The screen adds its source and limits (no insurance benefits, discounts or payment plans are applied; the total changes if the plan is revised). `acceptedEstimateTotal` is the part the patient has accepted.
- **Reconciliation.** `POST .../reconcile-references` (preview with `apply` false, resolve with true) matches each diagnosis's old text to a plan of **the same patient** by the plan's reference code or id (the code is set once with `POST .../reference-code`). A reference with no match, more than one match, or on a withdrawn diagnosis stays unresolved with the reason; nothing is guessed. `POST api/diagnoses/{id}/resolve-plan-reference` ties one to a plan a person chose; another patient's plan is refused exactly as an unknown plan is (404 `plan_not_found`), so the answer never reveals it exists. Tying is final and idempotent (one resolution per diagnosis; the same again is quiet, a different plan is `already_resolved`). The diagnosis then shows state "Resolved" with the plan, how it was tied, by whom and when, and its original text and history are unchanged.
- **Catalog usage (R02).** A plan procedure stores the procedure's id and the catalog version it was proposed under, so the catalog counts it: `GET api/procedures/{id}/usage` lists "Treatment plans" with the number of persisted plan procedures that refer to the procedure (withdrawn and completed ones, and those of every patient's plans, included), inactivating such a procedure is refused as `usage_confirmation_required` until the caller acknowledges the use, and the catalog page shows "Treatment plans: N" in its warning. After inactivation every existing plan still shows the procedure and the fee it copied; the inactive procedure is refused for new plan work. Only a number per source leaves the usage answer.
- **Patient copy.** A plain-language page (title, the patient explanation, procedures in stages with what each is for and its estimated fee, the patient's answers, the estimate and its caveats) with a Print button and print styles that print it alone in black on white. It leaves out the clinical rationale, who entered what, the revision history and anything withdrawn, and is not offered for a withdrawn plan.

## Endpoints

| Method and route | Needs |
|---|---|
| `GET api/treatment-plans/{id}/revisions`, `/revisions/{n}`, `/revisions/compare?from=&to=` | `ViewClinicalDocumentation` |
| `POST api/treatment-plans/{id}/revise` | `ManageTreatmentPlans` + CSRF |
| `POST api/treatment-plans/{id}/decision`, `POST api/treatment-plans/{id}/items/{itemId}/decision` | `ManageTreatmentPlans` + CSRF |
| `POST api/treatment-plans/{id}/items/{itemId}/schedule`, `.../complete` | `ManageTreatmentPlans` + CSRF |
| `POST api/treatment-plans/{id}/reference-code` | `ManageTreatmentPlans` + CSRF |
| `POST api/patients/{id}/treatment-plans/reconcile-references`, `POST api/diagnoses/{id}/resolve-plan-reference` | `ManageTreatmentPlans` + CSRF |

New stable codes: `revision_not_found`, `partial_acceptance_conflict`, `item_withdrawn`, `item_completed`, `item_not_accepted`, `no_procedures`, `reference_code_set`, `reference_code_in_use`, `already_resolved`, `diagnosis_not_found`, `diagnosis_withdrawn`, `treatment_plan_reference_resolved`. No new permission was invented.

## Audit

Every revise, answer (item and plan), schedule, unschedule, completion, reference code and resolution writes a history entry and a PHI-free audit entry (event type, entity, the signed-in user, time; never the patient's name, the answer-giver's name or clinical wording) in one save with the change. If the audit write fails, nothing is stored (`save_failed`, 503, no internals).

## Failure-first notes

| Question | Answer |
|---|---|
| What if a save fails? | One transaction per change; nothing partial is stored. Two people answering at once leave whole-numbered history (a unique `(item, sequence)`), and two reconciliations at once end with one resolution each. |
| Retries? | The screen keeps what was typed. The server is idempotent: the same answer, schedule or completion again, revising to what is already saved, and reconciling again change nothing. No automatic retry loops. |
| Recovery when it cannot proceed? | Nothing is queued; the refusal says why. A wrong answer is changed with a new row (history kept); a wrong plan content is revised; a wrong tie of an old reference is final by design (the original text is kept beside it). |
| Handled | validation per field, stale versions, withdrawn plans and procedures, opposite answers, completed procedures, duplicate and cross-patient reconciliation, a failed audit write, permissions, CSRF. |
| Not handled | Insurance estimates and benefits; patient-portal or signed acceptance (the answer is entered by staff); booking appointments from a schedule; undoing a tie of an old reference; resolving a diagnosis more than once. |

## Assumptions made (ALV-015-C01)

1. Acceptance is entered by staff on the patient's behalf (`ManageTreatmentPlans`), attributed to the signed-in account plus an optional name of who gave the answer; patient-portal acceptance is out of scope.
2. Revisions are copies, not diffs: each stores its whole layout, so any version reads as saved and any two compare without replaying history.
3. A decision needs the plan's current version; scheduling and completion do not (they are care already delivered, and are protected by the append-only sequence).
4. Procedure state lives in its own append-only table so the STORY-015 triggers that make procedures immutable are unchanged.
5. Old references are matched to the plan's reference code (case-insensitively) or its id, never fuzzily, and only within the same patient.
6. A resolution is final and one per diagnosis; the reference text of a resolved diagnosis is locked in the service and the database.
7. Matching also considers withdrawn plans (the plan identity still exists); a withdrawn diagnosis is skipped.
8. Phases are numbered 1-12 and the number is their identity across revisions; a number may be reused after a phase is removed.
9. The patient copy shows the current proposal only: withdrawn procedures and withdrawn plans are not included.
10. The scheduling state is a state and a date only; nothing in this story calls the appointment module.

## Limitations (recorded, not hidden)

- Billing, FrontDesk and OfficeManager still cannot read plans (STORY-015); the patient copy is therefore printed by clinical staff.
- No insurance estimate, benefit or discount; the figure is the practice's catalog fee when each procedure was proposed.
- A schedule is not an appointment; completion does not post a charge.
- The `@media print` layout is checked visually in the walkthrough (a screenshot under print media and a check that the rest of the page is hidden), not by a jsdom test.
- A tie of an old reference cannot be undone, and a diagnosis whose reference text is later wanted changed after tying must be withdrawn and re-recorded.

## Verification

Backend (real SQL Server): `TreatmentPlanRevisionSchemaTests` and `TreatmentPlanRevisionMigrationTests` (every trigger and constraint, the backfill and rollback), `TreatmentPlanRevisionServiceTests` (revisions, phases, comparison), `TreatmentPlanStateServiceTests` (answers, plan-wide answers, scheduling, completion, the failed-audit path), `DiagnosisReconciliationTests` (same-patient success, idempotency, history preservation, no-match and ambiguous retention, cross-patient denial, the lock), `TreatmentPlanCompletionApiTests` (roles, CSRF, refusals, the workflow, the estimate label). Frontend: `treatmentPlanLayout.test.ts`, `TreatmentPlanRevise.test.tsx`, `TreatmentPlanAnswers.test.tsx`, `TreatmentPlanRevisions.test.tsx`, `TreatmentPlanPatientCopy.test.tsx`, `TreatmentPlanReferences.test.tsx`, `DiagnosisPlanReference.test.tsx` (all with accessibility checks where a form or page opens). Browser: `e2e/treatment-plans-complete-real-backend.spec.ts` via `playwright.treatment-plans-complete.config.ts` (see `docs/testing/REAL_BACKEND_E2E.md`). Negative controls: `.alveara/handoffs/ALV-015-C01/R01-evidence/artifacts/test-runs/negative-controls.txt`.
