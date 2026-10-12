# Completing procedures as one operation, with the chart, the charge and the record (ALV-008-C01)

The companion to STORY-008 (`docs/CLINICAL_HISTORY.md`), whose routes and behaviour are unchanged. It adds the way procedures are completed in the product: a selected set of one plan's procedures is completed as one all-or-nothing operation that records who did it, when and for which visit, moves the chart, makes the charge, and leaves an immutable record that can only be corrected by an appended amendment. Design and decisions: `docs/design/ALV-008-C01_DESIGN_PROPOSAL.md` and the two reviewer responses beside it.

## What it is, in one paragraph

`POST api/treatment-plans/{id}/complete-procedures` takes a save key, the plan's version as last read, an encounter, a provider, a performed time and the selected procedures, and in **one transaction** writes: the plan's new version, each procedure's Completed state, history event and audit entry, a completion set, one immutable **completed-procedure** record per procedure, the chart finding that moves to Completed (found only through the plan's diagnosis link), and one charge per billable procedure through `IProcedureCharging`. If anything fails, nothing is recorded and the caller is told so. `GET .../completion-readiness` tells the review screen, before anyone submits, what each procedure would do.

## Data (migration `AddCompletedProcedures`)

| Table | Purpose | Rules the database itself enforces |
|---|---|---|
| `CompletionSets` | one row per accepted submission; the idempotency anchor (key, SHA-256 of the canonical request, correlation id, plan revision) | append-only (51144); one per plan and key; hash and key shape; patient is the plan's (51156) |
| `CompletedProcedures` | the authoritative record: plan, **exact plan revision**, plan item, catalog version, tooth, surface, encounter, provider, performed time, chart outcome and finding, who finalized it | append-only (51145); **one per plan item**; carries its set's and its item's plan, patient, revision (51147) and its item's catalog version, tooth and surface (51148); the encounter and the chart finding are the patient's own, the finding on the procedure's tooth and surface (51149); only for a completed, not withdrawn plan item (51150); not performed before its plan existed (51153) or before the acceptance in force was recorded (51154); chart outcome and finding agree |
| `CompletedProcedureAmendments` | a numbered correction of the provider, performed time or encounter, with a reason, its own key and request hash | append-only (51146); numbered 1, 2, 3 with no gaps (51152); the patient's own encounter (51151); same time rules (51155); a reason and at least one correction; one per procedure and number or key |

Trigger numbers 51144 to 51156 belong to this story; **51157 and up are free** (billing owns 51118 to 51143). The charge is not copied: the procedure's one charge is the billing entry that names the same plan item.

## Behaviour

- **Selection and scope.** A non-empty, duplicate-free list (at most 50) of one plan's procedures. Each must be on the plan, not withdrawn, accepted and not completed; otherwise none is completed and every one is listed with its reason (`already_completed`, `withdrawn`, `not_accepted`).
- **Stale protection.** The plan's version is checked before anything is staged and again, atomically, by the save that advances it, so two sets overlapping on a procedure end with exactly one winner and the other a conflict (409). Item-state rows alone do not move the plan's version, so the completion touches the plan row itself.
- **Retries.** The same key with the same canonical request (sorted item ids, encounter, provider, performed time in UTC milliseconds) replays the saved result with no writes, even from an out-of-date screen. The same key with a different request is `idempotency_conflict` (409). The same procedures under a new key after success are refused as already completed.
- **Provider, encounter and time.** The provider must be active; the encounter must be the patient's, Draft or Finalized (the completion only records attribution; the encounter is never changed). The performed time may be at most **five minutes** ahead of the practice clock (inclusive; one millisecond more is refused), and may not precede the plan or the recorded acceptance of a procedure. The acceptance rule says so in its message: it compares with the time the acceptance was **recorded in the plan**, not when the patient may have agreed.
- **Chart.** Plan item → its diagnosis → an explicit diagnosis link to a finding of the same patient → on the procedure's tooth (and surface when the procedure names one). One Diagnosed or Planned finding moves to Completed with the same rules, history and audit entry as a manual change; an already completed finding is reported as such; no linked finding, whole-mouth work or an ineligible finding leaves the chart alone (`NotApplicable`); **more than one** finding refuses the whole set (`ambiguous_chart_finding`, naming the procedure and the number of matches). A finding is never guessed from tooth and surface alone. The chart's own rules still apply (completing an extraction while other findings stand on the tooth is refused).
- **Charge.** A procedure is billable when its stored plan fee is more than zero; the ledger refuses a zero charge, so a no-fee procedure completes with `charge: none` and makes no entry (a set of only no-fee procedures opens no account). A billable procedure is charged only through `IProcedureCharging`, in this transaction, with the set's correlation id and **no amount from the caller**; a charge billing already holds for the procedure is reused.
- **Finalization and corrections.** A completed-procedure record is final when created. `POST api/completed-procedures/{id}/amend` appends a correction against the amendment the caller last saw (a stale one is a conflict; two racing for one number leave one winner), with its own key and request hash (an identical retry returns the original; the same key with a different request is a conflict). A correction that changes nothing is refused. It never touches the plan's state, the chart or the charge.
- **Readiness.** `GET api/treatment-plans/{id}/completion-readiness` (optionally `?itemIds=`) reads only and uses the same refusal rule and the same chart resolution as the completion: per procedure whether it is ready (and why not), the chart effect and number of matches, whether it is billable, whether a charge exists; plus the active providers and the patient's encounters. The review screen shows this and never works the rules out itself.
- **Clinical history.** `GET api/patients/{id}/clinical-history` (STORY-008) now adds, for a procedure completed this way, its current provider, performed time, encounter, charge and chart outcome; for one completed the old way those fields are null.
- **Legacy routes.** `POST .../items/{id}/complete` and `POST .../complete-accepted` are unchanged (state-only completions). They are legacy compatibility paths: each use is logged as a structured, PHI-free `legacy_completion_used` event (route, outcome, status, trace id). `ALV-008-C02` retires them.
- **Audit and logs.** Each procedure's completion keeps its `TreatmentPlanItemCompleted` audit entry; each set adds `CompletionSetRecorded` (counts and the correlation id, no clinical detail); each correction adds `CompletedProcedureAmended`. Structured logs carry the correlation id, outcome and duration, and on failure only the error class and SQL error number.

## Endpoints

| Route | Permission | Notes |
|---|---|---|
| `POST api/treatment-plans/{planId}/complete-procedures` | `ManageTreatmentPlans` + CSRF | body `{ idempotencyKey, rowVersion, encounterId, providerId, performedAtUtc, itemIds[] }`; 200 with the plan and each procedure's record, chart outcome and charge |
| `GET api/treatment-plans/{planId}/completion-readiness` | `ManageTreatmentPlans` | reads only |
| `GET api/patients/{patientId}/completed-procedures` | `ViewClinicalDocumentation` | current values, newest performed first |
| `GET api/completed-procedures/{id}` | `ViewClinicalDocumentation` | originals, current values, every amendment, chart and charge |
| `POST api/completed-procedures/{id}/amend` | `ManageTreatmentPlans` + CSRF | `{ idempotencyKey, expectedAmendmentNumber, providerId?, performedAtUtc?, encounterId?, reason }` |

Refusals carry a stable `error`, a message safe to show and `fieldErrors`; refusals about particular procedures (`items_not_completable`, `ambiguous_chart_finding`) also list them in `items`. Codes: `validation_failed` (400), `row_version_required` (400), `plan_not_found` / `patient_not_found` / `completed_procedure_not_found` (404), the shared concurrency conflict (409), `plan_withdrawn`, `items_not_completable`, `ambiguous_chart_finding`, `chart_update_refused`, `idempotency_conflict` (409), `save_failed` (503, no internals).

## The screen

On a patient's Treatment plan tab, **Review and complete…** is the primary action. It opens a review: a table of every procedure that has not been withdrawn with its tooth and surface, what completing will do to the chart and what will be charged (all from the readiness; a procedure that is not ready says why and cannot be ticked), a provider, an encounter, a performed time, a live summary and an explicit **Confirm completion of N procedures**. The request carries the readiness's version and a save key that a retry reuses. The result is shown only after the server confirmed the whole set, with links to the clinical history, the odontogram and the account ledger. Every failure says **Nothing was completed** and keeps what was entered. The old "Complete accepted procedures" button is kept for the verified STORY-008 contract, in a group labelled **Legacy completion** that says it records no provider, encounter, chart change or charge; the per-procedure "Mark X completed" button (ALV-015-C01) is the same legacy route and its confirmation says so too.

## Failure-first notes

1. **If it fails:** nothing is stored (one transaction); the caller is told so (`save_failed`, 503) and may repeat the same request.
2. **Retry:** not automatic. A user retry with the same request replays; after success a new key is refused as already completed; a connection that drops after the commit is recovered by the same replay.
3. **After repeated failure:** manual intervention; the log carries the correlation id, error class and SQL error number.
4. **Handled:** stale or missing version, withdrawn plan, empty or duplicate selection, a procedure not completable, wrong encounter or provider, a time outside the rules, an ambiguous chart match, a refused chart rule, a failed chart write, a failed charge, a failed audit write in either save, a replay, two simultaneous sets, a stale or racing correction. **Not handled:** completing care that was never planned; undoing a completion.

## Assumptions made

- One request completes items of **one plan**; a visit that spans plans is several submissions.
- The performed time is compared with the **recorded** acceptance and plan-creation times, to the millisecond (the request's time is cut to whole milliseconds in UTC, the stored acceptance time is exact), so a performed time equal to a recorded acceptance with extra precision can fall a fraction of a millisecond short and be refused.
- A readiness answer and a submission are separate reads; the server judges the submission again.

## Limitations (recorded, not hidden)

- **Legacy completions** (STORY-008 routes) produce state-only completions with no record, chart move or charge; the new operation refuses such a procedure as already completed and it cannot be amended. `ALV-008-C02` retires the routes and adopts those completions explicitly.
- **The acceptance rule compares with the time staff recorded the answer**, not when the patient agreed, so a completion dated before a late-entered acceptance is refused; an explicit "agreed on" date is future work.
- **An ambiguous chart match refuses the whole set;** the person unticks that procedure or corrects the diagnosis links. Choosing among findings on the screen is future work.
- Completed is final: there is no undo; a reversed charge keeps its procedure billed (ALV-007-C01).
- Insurance claims, statements, receipts and refunds are other stories.
