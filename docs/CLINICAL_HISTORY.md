# Completing an accepted plan and the clinical history (STORY-008)

Course story `STORY-008` (REQ-013 in part): a clinician completes the procedures a patient accepted, and the patient's clinical history shows them. It builds on treatment planning (`docs/TREATMENT_PLANS.md`, `docs/TREATMENT_PLAN_COMPLETION.md`), whose contract is unchanged.

## What it is, in one paragraph

`POST api/treatment-plans/{id}/complete-accepted` completes **every accepted, not yet completed, not withdrawn procedure** of a plan in one all-or-nothing save. Each procedure gets exactly what completing it alone gets today: a new state row (Completion = Completed), an `ItemCompleted` history event and a PHI-free audit entry, all in the same save. `GET api/patients/{id}/clinical-history` lists the completed care, read back from those same state rows. There is no new table and nothing to keep in step.

## Behaviour

- **Completion.** The request carries the plan's row version; a stale version is a conflict (409) and completes nothing. A withdrawn plan is refused (`plan_withdrawn`). A plan with no accepted procedure is refused (`nothing_to_complete`, 409). When every accepted procedure is already completed the call changes nothing and answers with the plan as it stands, so a replay or a double click is safe. Declined, pending and withdrawn procedures are never touched.
- **Failure.** If any part of the save fails (including the audit write) nothing is stored and the caller gets `save_failed` (503) with no internals. Two simultaneous completions end with each procedure completed exactly once: the loser is a conflict.
- **Clinical history.** One entry per completed procedure: plan, procedure code and the description as it stood when the plan was made, tooth, surface, who completed it, when, and the plan revision in force then. Newest first. It is derived on every read, so completed care stays listed when its plan is later revised or withdrawn.
- **Permissions.** Completing needs `ManageTreatmentPlans` (dentist, administrator) and a CSRF token. Reading the history needs `ViewClinicalDocumentation` (administrator, dentist, hygienist, assistant).
- **Screen.** On the patient's Treatment plan tab, "Complete accepted procedures" appears for people who can change plans when something accepted is still open. A confirmation lists exactly what will be completed and says completing is final; nothing is sent until it is confirmed. A "Clinical history" section under the plans lists the completed care and refreshes after every change. An empty list says none has been recorded, never that no care was given; a failed load says it could not load.

## How the three acceptance criteria are met

| Criterion | Where |
|---|---|
| An accepted plan, when completed, updates the clinical history | `CompleteAcceptedAsync` then `ClinicalHistoryAsync`; `TreatmentPlanCompleteAcceptedTests`, `TreatmentPlanCompleteAcceptedApiTests` (criteria test), real-browser walkthrough |
| A revised plan keeps the original | Revisions are whole copies and earlier ones are immutable by trigger (ALV-015-C01); proven again after completion in `Completed_care_stays_in_the_history_and_the_earlier_revision_is_kept...` and the walkthrough (revision 1 still readable with all three procedures; history still says revision 1) |
| Every change is logged with user and timestamp | A history event and an audit entry per procedure, in the same save as the change; the walkthrough reads the plan log back (who and when on every entry) |

## Failure-first notes

1. **If it fails:** nothing is stored; the caller is told so (`save_failed`, 503) and may repeat the same call.
2. **Retry:** the caller retries by clicking again; a replay after success is quiet, so a retry can never double-complete. There is no automatic retry.
3. **After repeated failure:** manual intervention; the server logs the error class and SQL error number (never plan, patient or SQL text) as `treatment_plan_save_failed`.
4. **Handled:** stale version, missing version, withdrawn plan, nothing accepted, withdrawn procedure, failed audit write, two simultaneous completions, replay. **Not handled:** a connection that drops after the server saved but before the answer arrived (the screen says nothing was completed and a reload shows the truth); a patient with no completed care is simply an empty list.

## Not in this story (belongs to ALV-008-C01)

Charges for completed procedures (via `IProcedureCharging`), the odontogram update, completing a chosen subset, a completed-procedure entity with its own provider and date, and a review-and-confirm screen. Today a completion records who and when from the signed-in account.

## Limitations

- The history lists a procedure by the description in force when the plan was made, not today's catalog wording.
- A procedure completed by mistake cannot be undone (completed is final by design); correcting it belongs with the later completion story.
- The history shows planned care that was completed. Care that was never planned is not in it.
