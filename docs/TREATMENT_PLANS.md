# Treatment plans (STORY-015, course item 28)

A dentist proposes catalog procedures for a patient's current diagnoses and sees the practice's fee estimate. This is the thin first slice: the plan's identity and its procedures. Phases, patient acceptance or decline, revision comparison, a patient-friendly print, reconciling the old diagnosis "treatment plan reference" text, completion and scheduling belong to the companion story `ALV-015-C01` and are **not** built here.

## What it is, in one paragraph

A **plan** belongs to one patient and is never deleted: it is `Proposed` or `Withdrawn` (final). Each **item** names one current diagnosis of that patient and one active catalog procedure (plus a tooth and surface where the procedure needs them), and **copies the fee of the exact catalog version in effect when the item is proposed** (it stores the procedure id, the version id and the fee). Items are immutable: a wrong one is withdrawn with a reason and the right one added. Every change appends an **event** and an audit entry in the same save.

## Data

| Table | Purpose | Rules the database itself enforces |
|---|---|---|
| `TreatmentPlans` | identity + current state | status `Proposed`/`Withdrawn`; title present, one line; a withdrawn plan carries who, when and why (and a proposed one carries none); no delete (trigger 51085); patient, key and creator never change (51086); a withdrawn plan is final (51087); idempotency key unique per patient |
| `TreatmentPlanItems` | the proposed procedures | no delete (51088); never edited, only withdrawn once with a reason (51089, 51090); same patient as the plan (51092); not added to a withdrawn plan (51093); a diagnosis of the same patient that is not withdrawn (51094, 51095); the stored fee and procedure are those of the catalog version the item names (51096) and that procedure is active (51097); tooth and surface fit the procedure's scope and dentition (51098); fee 0 to 1,000,000.00 with two decimals; tooth keys and surfaces from fixed lists; item key unique per plan |
| `TreatmentPlanEvents` | append-only history | change type from a fixed list (`Created`, `ItemAdded`, `ItemWithdrawn`, `Renamed`, `Withdrawn`); a reason on every withdrawal; a title on create and rename; no update or delete (51091) |

Trigger numbers 51085-51098 belong to this story; 51099 and up are free.

## Behaviour

- **Create** needs a title and at least one item (up to 50). Everything wrong is reported together (`validation_failed`, 400, `fieldErrors` keyed `title`, `items[0].diagnosisId`, `items[0].procedureId`, `items[0].toothKey`, `items[0].surface`). A diagnosis that does not exist and one that belongs to another patient are refused in the same words, so the answer never confirms that someone else's diagnosis exists. Repeating a create with the same idempotency key returns the plan already saved.
- **Add item** needs the plan's row version and carries its own idempotency key. A duplicate (same diagnosis, procedure and place) is refused until the first one is withdrawn. **Withdraw item**, **rename** and **withdraw plan** need the row version too; both withdrawals need a reason. Withdrawing an already-withdrawn plan is a safe replay (the first reason stands).
- **Estimate**: `estimateTotal` is the sum of the fees of the items that are not withdrawn. `estimateLabel` says what it is and is not: the practice's catalog fee, not an insurance estimate and not a guaranteed patient cost. The screen always shows it.
- **Fee copy**: a later change to the catalog fee does not change a plan that already exists (proven in the browser walkthrough: catalog $150, plan still $120).

## Endpoints

| Method and route | Needs | Purpose |
|---|---|---|
| `GET api/patients/{id}/treatment-plans?includeWithdrawn` | `ViewClinicalDocumentation` | the patient's plans with items and totals |
| `GET api/treatment-plans/{id}` | `ViewClinicalDocumentation` | one plan |
| `GET api/treatment-plans/{id}/history` | `ViewClinicalDocumentation` | the events, with who and when |
| `POST api/patients/{id}/treatment-plans` | `ManageTreatmentPlans` + CSRF | create |
| `POST api/treatment-plans/{id}/items` | `ManageTreatmentPlans` + CSRF | add a procedure |
| `POST api/treatment-plans/{id}/items/{itemId}/withdraw` | `ManageTreatmentPlans` + CSRF | withdraw a procedure |
| `POST api/treatment-plans/{id}/rename` | `ManageTreatmentPlans` + CSRF | rename |
| `POST api/treatment-plans/{id}/withdraw` | `ManageTreatmentPlans` + CSRF | withdraw the plan |

Refusals carry a stable `error` code (`validation_failed`, `reason_required`, `row_version_required`, `row_version_invalid`, `plan_not_found`, `item_not_found`, `patient_not_found`, `plan_withdrawn` 409, `concurrency_conflict` 409, `save_failed` 503) and a message that is safe to show.

## Permissions

No new permission was invented. Reading needs `ViewClinicalDocumentation` (Dentist, Hygienist, Assistant, Admin). Changing needs `ManageTreatmentPlans` (Dentist, Admin) plus a CSRF token. **Billing, FrontDesk and OfficeManager cannot read plans** (a plan carries diagnosis wording); the companion story may revisit fee visibility for billing roles.

The authoring screen's procedure picker calls `GET api/procedures/active`, which needs `ViewBilling`. Dentist and Admin hold it. A Hygienist or Assistant can read plans but never calls the catalog: the read-only view is drawn from the plan response alone, which already carries each item's code, description, fee and diagnosis label.

## Audit and history

Every create, add, withdraw, rename and plan withdrawal writes a `TreatmentPlanEvent` and an audit entry (entity `TreatmentPlan`, actor user id, time) in **one save** with the change. Audit text contains no patient data. If the audit write fails, the whole save fails and nothing is stored (`save_failed`, 503, no internals).

## The screen

The patient workspace has a **Treatment plan** tab next to Diagnoses (`src/alveara-client/src/pages/treatmentplan/`). People who may change plans see a form (title, diagnosis, procedure with its fee, tooth and surface where needed) and, on each Proposed plan, Add procedure, Rename plan, Withdraw plan and Withdraw per procedure. Gaps are named beside their fields before anything is sent; a server refusal appears beside the field it is about, with what was typed kept; each attempt carries an idempotency key that is kept while the entry is unchanged. Withdrawn items and plans stay visible with who, when and why. Readers see the same plans with no controls.

## Failure-first notes

| Question | Answer |
|---|---|
| What if a save fails? | One transaction per change (row, event and audit together); nothing partial is stored. A unique-key race is reported as a concurrency conflict, not a 500. |
| Retries? | The screen keeps what was typed and the person retries; the server operations are idempotent (same create key returns the same plan; same add key returns the same item). No automatic retry loops. |
| Recovery when it cannot proceed? | Nothing is queued: the call is synchronous and the refusal says why. A wrong item is withdrawn with a reason and the right one added; a wrong plan is withdrawn. |
| Handled | validation, other-patient and withdrawn diagnoses, inactive procedures, tooth/surface fit, duplicates, races, stale versions, withdrawn plans, permission, CSRF, a failed audit write. |
| Not handled | Insurance estimates, discounts and fee schedules (the estimate is the catalog fee only). Billing roles reading plans. Everything listed under the companion story above. |

## Assumptions made (STORY-015)

1. Reuse `ViewClinicalDocumentation` and the existing `ManageTreatmentPlans` rather than add permissions.
2. The plan stores the catalog version id and fee at proposal time (the snapshot contract ALV-N005 built); no recalculation later.
3. Items are immutable; correction is withdraw-and-add, so history never needs to explain an edit.
4. The create form proposes one procedure; more are added afterwards (the API accepts up to 50 at creation).
5. The old diagnosis "treatment plan reference" text stays plain text with no link to these tables (the three diagnosis guard tests were updated to say so); reconciling it is the companion's job.
6. A database trigger must never require row N-1 to exist at insert time, because EF does not insert the rows of one save in a fixed order (a defect of exactly this kind was found and fixed while building this).

## Verification

Backend (real SQL Server): `TreatmentPlanServiceTests` (32), `TreatmentPlanLifecycleTests` (16), `TreatmentPlanSchemaTests` (57), `TreatmentPlanApiTests` (24). Frontend: `treatmentPlanRules.test.ts` (8) and `TreatmentPlansPanel.test.tsx` (17, vitest + axe). Browser: `e2e/treatment-plans-real-backend.spec.ts` via `playwright.treatment-plans.config.ts` (see `docs/testing/REAL_BACKEND_E2E.md`). Negative controls: `.alveara/handoffs/STORY-015/negative-controls.txt`.
