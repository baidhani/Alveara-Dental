# Prescription workflow (ALV-016-C01)

**Requirement:** REQ-016 - the system must support prescriptions with medication, dosage, and allergy checks. This companion story completes the prescription record on top of STORY-016 (`docs/PRESCRIPTIONS.md`), which is unchanged.
**Where it lives:** the patient workspace's **Prescription composer** tab (`/patients/:id/prescription-composer`), beside STORY-016's **Prescriptions** tab (the quick entry form, kept so that STORY-016's tests and real-browser walkthrough still pass exactly).
**Who can use it:** reading and printing need `ViewPrescriptions` (dentist, hygienist, assistant, admin). Every change needs `ManagePrescriptions` (dentist, admin) and a CSRF token. Front desk, billing and the office manager have no tab and are refused (403). No permission was added.

## What it does, and what it deliberately does not

A prescription is written as a **draft**, **finalized** when complete, and from then on only **printed, cancelled, discontinued or amended**; it is never edited or deleted.

**There is no drug-knowledge source in this application.** It therefore does **not** check drug interactions and does **not** judge whether a dose, a quantity or a number of refills is clinically or legally appropriate. The options say `decisionSupportConfigured: false`, a banner on the screen says so permanently, and every check result and every print carries the same statement. What it does check is structure, by rules written down in `PrescriptionWorkflowRules` and `PrescriptionRules`:

- the medication, dose, unit, frequency and review note are judged by STORY-016's rules, unchanged (they are called, not copied);
- the **instructions** are 1 to 500 characters;
- the **quantity** is a positive number with at most 2 decimals in a known unit (tablet, capsule, mL, g, tube, bottle, inhaler, unit), not above a typing-error ceiling of 1000;
- the **refills** are a whole number from 0 to 12 (typing-error ceiling).

The ceilings catch slipped keys; they are **not** clinical or legal limits, and no controlled-substance or jurisdiction rule is modelled. The messages and the limitation text say so.

## Lifecycle

| Step | What happens | Recorded as |
|---|---|---|
| Draft | Saved incomplete or complete; edited while a draft; discarded (kept, never deleted) | `PrescriptionDrafts` (the only mutable table), audit `PrescriptionDraftCreated`/`Changed`/`Discarded` |
| Acknowledge | The prescriber confirms the allergy and medication lists as they are now | audit `PrescriptionDraftSafetyAcknowledged` |
| Finalize | The STORY-016 allergy check and review rule run unchanged, plus instructions, quantity and refills are required; one save writes the prescription, its details, a Finalized event, the draft's closing and the audit entry | `Prescriptions` + `PrescriptionDetails` + `PrescriptionEvents`, audit `PrescriptionRecorded` |
| Print | Content built on the server from the immutable record, with a content hash | audit `PrescriptionPrinted` |
| Cancel / Discontinue | A reason is required and kept with the prescription | event Cancelled / Discontinued, audit `PrescriptionCancelled` / `PrescriptionDiscontinued` (never the reason) |
| Amend | A new draft with the same content; finalizing it replaces the original, which becomes Superseded (never edited) | audit `PrescriptionAmendmentStarted`, `PrescriptionRecorded`, `PrescriptionSuperseded` |

The status is worked out from the events: Active unless one ending event exists (Cancelled, Discontinued or Superseded). A prescription saved by STORY-016 has no events and is Active; it has no instructions, quantity or refills, so it says so, **cannot be printed** (`print_incomplete`) and is amended by filling in the rest.

## The allergy and medication lists, and a change in them

A draft keeps a **snapshot** of the active allergies and current medications, and how far each list can be relied on, as the prescriber **last acknowledged**. Editing the draft never refreshes it. When the lists change (an allergy or medication added or changed, or a list becomes "needs review" or "unknown"):

- the draft shows what changed, in words;
- finalizing is refused (409 `safety_context_changed`, with the added and removed lines) until the prescriber presses "I have read the changes";
- an acknowledgement is only accepted for the lists the screen actually showed: it carries the fingerprint that was shown, and a different live fingerprint is refused again.

Independently, finalizing re-runs the allergy check against the live lists: a name match with a recorded allergy, or a list that is not established (never reviewed, unknown, or changed since it was confirmed), needs the prescriber's own words (409 `allergy_review_required`). An unreadable safety context refuses the save (503 `allergy_check_unavailable`): the check is never skipped. The allergy check compares names only and is never a clearance.

## How the promises are kept

| Promise | How |
|---|---|
| Nothing is lost or overwritten | Only a Draft is mutable; the database refuses deleting a draft (51175), changing a finalized or discarded draft (51176), changing a draft's patient, key or amended prescription (51177), changing details (51178) and changing or deleting events (51180). A stale edit is refused through the row version (409 `concurrency_conflict`). |
| Incorrect data is named, not saved | Every problem is listed with its field (`validation_failed`, 400); the database repeats the rules as check constraints. |
| Finalization is all-or-nothing | The prescription, details, events, the draft's closing and the audit entries are one save; if the log fails nothing is saved (`save_failed`, 503). |
| Repeats are quiet | Create and amend carry a key; finalizing a finalized draft returns the prescription it became; cancelling a cancelled prescription changes nothing; simultaneous finalizations or cancellations make one. |
| An amendment stays inside one patient | triggers 51179 and 51181 |
| One ending per prescription | unique filtered indexes: one Finalized event and at most one ending event |
| Audit never holds clinical content | audit text is fixed wording with the user, time and correlation id; reasons stay with the event |

## API

Reads (`ViewPrescriptions`): `GET /api/prescription-workflow/options`, `GET /api/patients/{id}/prescription-drafts`, `GET /api/prescription-drafts/{id}` (the draft and its safety check), `GET /api/patients/{id}/prescription-records`, `GET /api/prescriptions/{id}`, `GET /api/prescriptions/{id}/print` (a read that is logged; if the log cannot be written nothing is returned).
Changes (`ManagePrescriptions` + CSRF): `POST /api/patients/{id}/prescription-drafts`, `PUT /api/prescription-drafts/{id}`, `POST /api/prescription-drafts/{id}/check` | `/acknowledge-safety` | `/finalize` | `/discard`, `POST /api/prescriptions/{id}/amend` | `/cancel` | `/discontinue`.
Refusals carry `error`, `message`, `problems`, `assessment` and `detail`: `validation_failed`, `row_version_required`, `row_version_invalid` (400); `patient_not_found`, `draft_not_found`, `prescription_not_found` (404); `allergy_review_required`, `safety_context_changed`, `idempotency_key_reused`, `draft_closed`, `prescription_not_active`, `print_incomplete`, `concurrency_conflict` (409); `allergy_check_unavailable`, `save_failed`, `print_failed` (503). There is no route that edits or deletes a prescription or deletes a draft.

## Failure modes handled, and not

Handled: malformed or incomplete entry, allergy match, unestablished allergy list, an allergy or medication list that changed, unreadable safety context, failed audit write (finalize, create, cancel, print), replay, conflicting replay, concurrent saves, stale edit, an amendment whose original ended, print of an incomplete prescription, missing permission, missing CSRF token. Retry policy: none server-side (single local transactions); the client may repeat a request safely.
Not handled (out of scope, as the prompt says): drug interactions and clinical dose appropriateness, eRx and pharmacy connectivity, controlled-substance and jurisdiction rules, encounter linkage. The two Prescriptions tabs coexist because STORY-016's contract is immutable; retiring the quick entry form is a later decision.

## Schema (migration `AddPrescriptionWorkflow`)

`PrescriptionDrafts`, `PrescriptionDetails`, `PrescriptionEvents` with check constraints mirroring the rules and triggers 51175 to 51181 (51182 and up are free). Rolling the migration back refuses (51178) once any of the three tables holds a row.
