# ALV-008-C01 — Design Proposal for Independent Review

**Story:** ALV-008-C01 — Atomic Procedure Completion, Clinical History, Odontogram, and Charge Generation (execution item 34)
**Type:** Companion to course story STORY-008 (verified complete by the course portal)
**Document status:** PROPOSAL. No ALV-008-C01 code has been written. This document asks the reviewer to approve, amend or reject the design and the eight decisions in section 9 before implementation starts.
**Prepared:** 2026-10-11, session CC-20261008-k4q9
**Repository state when written:** branch `main`; STORY-008 implementation `8da1119`; portal sync `29411bb`; STORY-008 completion record `d44773a` (local, not yet pushed). ALV-007-C01 is `COMPLETE` (approved as attempt R01). Dependencies named by the story are all complete: STORY-008, ALV-015-C01, ALV-N005, ALV-006-C01, ALV-007-C01.

**How to respond.** Section 12 is a response template. For each decision D1–D8 reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead). Anything not answered is treated as "recommendation not yet approved" and will be raised again rather than assumed.

---

## 1. What the story requires (from the master execution plan, item 34)

**User story.** As a clinician, I want one or more selected planned procedures completed as one trustworthy clinical operation so that clinical history, chart state, treatment-plan history and billing stay consistent.

**Implementation scope (verbatim intent).**
- Complete one or multiple selected accepted/planned procedures in a visit/encounter.
- Create authoritative completed-procedure records linked to the source plan/version.
- Preserve treatment-plan/version history.
- Update the relevant odontogram lifecycle.
- Create exactly one linked billable charge for each billable completed procedure through ALV-007-C01.
- Finalization/amendment semantics owned by the completed-procedure domain.
- Idempotent duplicate-submit protection.
- One transaction boundary for the selected set, so a failed coupled effect cannot leave the submitted set half-authoritative.
- Preserve provider, completion date/time, tooth/surface and encounter attribution.

**UI/UX deliverables.** A completion review screen listing every selected procedure, tooth/surface, provider/date and resulting charge effect; explicit confirmation of the submitted set; a clear failure state that never shows procedures as completed if the authoritative transaction rolled back; post-completion links to clinical history, odontogram and account ledger.

**Security/audit/integrity.** Clinical completion permission; financial-effect audit; a finalized completed procedure cannot be silently deleted or rewritten; charge creation goes through the billing service/idempotency contract, not ad-hoc ledger writes.

**Failure paths named by the story.** One charge creation fails in a multi-procedure completion; odontogram update fails; duplicate completion submission; stale treatment-plan version; selected procedure already completed/cancelled; audit/finalization persistence failure.

**Acceptance / stop condition.** Original STORY-008 tests still pass; one or multiple selected planned procedures complete in one authoritative operation; completed procedures stay linked to the exact source plan/version; odontogram updates when applicable; exactly one linked charge per billable completed procedure; duplicate/retry does not double-complete or double-charge; a failed submitted set leaves no mixed authoritative clinical/financial state.

**Required tests.** Original regression; single- and multi-procedure transactional completion; procedure→charge cardinality; idempotency/duplicate-submit; failure rollback; stale-plan concurrency.

**Quality Gate D criteria this story feeds (`.alveara/QUALITY_GATES.md`).**
- **D2:** one or many planned procedures complete atomically with exact source linkage, odontogram effects and one billable charge per billable completed procedure (zero orphaned charges, zero missing charges, odontogram state matches).
- **D5:** financial and completed-procedure histories survive duplicate submissions, corrections and stale edits without destructive overwrite.

**Out of scope (per the plan).** Insurance claim generation (may later subscribe to completed/billable state). Also not this story: statements, receipts, refunds and account UX hardening (ALV-007-C02, item 35).

---

## 2. Verified current state (what exists today, with where to look)

These were read from the code in this session, not inferred from prose.

| Area | Fact | Where |
|---|---|---|
| Plan content | Numbered, immutable revisions; each plan item stores diagnosis, procedure version, tooth, surface, fee copied from the catalog | `TreatmentPlanItem`, `TreatmentPlanRevision*` (ALV-015-C01) |
| Per-procedure progress | Append-only `TreatmentPlanItemStates` rows (Acceptance, Scheduling, Completion; Completed is final) with the plan revision at the time, who recorded and when | `TreatmentPlanService.States.cs` |
| STORY-008 completion | `CompleteItemAsync` (one procedure) and `CompleteAcceptedAsync` (all accepted open procedures, one save). Each writes a state row, an `ItemCompleted` history event and a PHI-free audit entry in the same save. Records only the signed-in user and time. No provider, no encounter, no chart update, no charge | `TreatmentPlanService.States.cs` |
| Clinical history | `GET api/patients/{id}/clinical-history`, derived on every read from the completion state rows; no table of its own | `TreatmentPlanService.ClinicalHistory.cs` |
| Charge contract | `IProcedureCharging.ChargeCompletedProcedureAsync(ProcedureChargeRequest(PatientId, PlanItemId, CorrelationId), actor, ct)`. Refuses to run unless the caller already opened a transaction on the same `AlveraDbContext`; saves the caller's staged changes inside that transaction; creates at most one procedure-linked charge per plan item (idempotent, save key `procedure-charge-{itemId}`); amount is always the plan item's stored fee | `BillingService.ProcedureCharge.cs` |
| Ledger rule on amount | A `Charge` entry must have `Amount > 0` (`CK_BillingEntries_Sign`). The catalog allows a fee of `0` ("enter 0 for no charge", `ProcedureRules.cs`). A zero-fee charge would therefore be refused by the database | `Data/BillingModel.cs`, `ProcedureRules.cs` |
| Existing manual charge | `POST api/patients/{id}/billing/charges` bills a completed plan procedure (course story STORY-007). Parent tests complete an item and then charge it by hand | `BillingService.cs`, `BillingController.cs` |
| Odontogram | `ToothFinding` has lifecycle `Existing`, `Diagnosed`, `Planned`, `Completed`. Allowed moves: Diagnosed→Planned, Diagnosed→Completed, Planned→Completed. `ChangeStateAsync` applies one move with a row-version check, a version row and an audit entry | `OdontogramService.cs`, `OdontogramRules.cs` |
| Diagnosis ↔ chart | A diagnosis can be linked to a tooth finding of the same patient (`DiagnosisLinks`, link type `Finding`). A plan item names its diagnosis | `DiagnosisService.Structure.cs`, `Diagnosis.cs` |
| Provider | `ProviderProfile` (staff profile, specialty, `IsActive`) | `Identity/IdentityEntities.cs` |
| Encounter | `Encounter` (patient, appointment, time, status Draft or Finalized) | `Clinical/Encounter.cs` |
| Transactions | No retrying execution strategy is configured, so a plain explicit transaction on the scoped context is valid | repository-wide search |
| Database triggers | Error numbers up to 51143 are used; **51144 and up are free** | `.alveara/BUILD_STATE.md` |

---

## 3. The gaps this story must close

1. There is no authoritative record of a completed procedure: provider, encounter, performed date/time and the exact plan revision are not stored together, and nothing says a completion is "finalized".
2. Completing a procedure does not touch the odontogram.
3. Completing a procedure does not create a charge; billing depends on a separate manual call.
4. Nothing completes a **selected set** with one idempotency key and one transaction covering the clinical and financial effects.
5. There is no amendment path that preserves the original.
6. A fee of zero cannot be charged (section 2), so "billable" must be defined.

---

## 4. Proposed design

### 4.1 Domain model (three new tables, no change to existing contracts)

**`CompletionSets`** — one row per accepted submission; the idempotency anchor.

| Column | Meaning |
|---|---|
| `Id`, `PatientId`, `PlanId` | identity and ownership |
| `IdempotencyKey` | caller's key; unique per plan |
| `RequestHash` | SHA-256 of the canonical request (sorted item ids, encounter, provider, performed time); detects the same key with a different payload |
| `CorrelationId` | the id passed to the charge contract and written to audit and logs |
| `PlanRevisionNumber` | the revision in force when submitted |
| `CreatedAtUtc`, `CreatedByUserId`, `ItemCount` | who and when |

**`CompletedProcedures`** — the authoritative record, one per plan item, immutable once written.

| Column | Meaning |
|---|---|
| `Id`, `PatientId`, `PlanId`, `PlanItemId` | `PlanItemId` is **unique**: a procedure can be completed once |
| `SetId` | the completion set that produced it |
| `PlanRevisionNumber` | the exact revision the item was completed under |
| `ProcedureVersionId` | the catalog version the plan copied (so the description and fee context are exact) |
| `ToothKey`, `Surface` | copied from the plan item |
| `EncounterId` | the visit it belongs to |
| `ProviderProfileId` | who performed it |
| `PerformedAtUtc` | when it was performed |
| `ToothFindingId` (nullable), `ChartOutcome` | the finding it moved and whether the chart was `Updated`, `AlreadyCompleted` or `NotApplicable` |
| `FinalizedAtUtc`, `FinalizedByUserId` | set at creation: a completed procedure is finalized the moment it exists |

**`CompletedProcedureAmendments`** — append-only corrections.

| Column | Meaning |
|---|---|
| `Id`, `CompletedProcedureId`, `PatientId`, `AmendmentNumber` | numbered per procedure, unique |
| `Reason` | required, non-blank |
| `ProviderProfileId`, `PerformedAtUtc`, `EncounterId` | the corrected values (each nullable = unchanged) |
| `AmendedAtUtc`, `AmendedByUserId` | who and when |

The **current** provider, time and encounter of a procedure are the latest amendment's values where present, otherwise the original's. The original row is never changed, so history is preserved.

The charge is **not** copied into these tables. The link to billing is the existing `BillingEntries.PlanItemId`, which already carries one charge per procedure.

### 4.2 Database enforcement (new triggers, numbers 51144 and up)

| Rule | Mechanism |
|---|---|
| `CompletionSets`, `CompletedProcedures`, `CompletedProcedureAmendments` cannot be updated or deleted | three `INSTEAD OF`/`AFTER` triggers that `THROW` (51144–51146) |
| A completed procedure's plan, patient, item, set and encounter belong together (same patient; item belongs to plan) | trigger (51147) |
| A completed procedure may only be inserted for an item whose latest state row is Completed | trigger (51148) |
| One completed procedure per plan item | unique index |
| One set per `(PlanId, IdempotencyKey)` | unique index |
| Amendment needs a reason, and numbers are unique per procedure | check constraint + unique index |

### 4.3 The operation

`CompletedProcedureService.CompleteProceduresAsync(planId, request, actor, ct)` where

```
request = { idempotencyKey, rowVersion, encounterId, providerId, performedAtUtc, itemIds[] }
```

Flow (one explicit transaction on the scoped `AlveraDbContext`):

1. **Validate without writing.** Plan exists and is not withdrawn; `rowVersion` matches the plan; encounter belongs to the patient (see D6); provider exists and is active; `performedAtUtc` is valid (D7); every selected item is on the plan, not withdrawn, accepted and not completed. If any item fails, nothing is written and the response names **each** failing item with a reason (`already_completed`, `withdrawn`, `not_accepted`, `not_on_plan`).
2. **Idempotency check.** Same plan and key: if the stored `RequestHash` equals this request, return the original result (`replayed: true`) with no writes; if it differs, refuse with `idempotency_conflict` (409).
3. **Begin the transaction** (`Database.BeginTransactionAsync`).
4. **Stage the set row**, then for each item **in item-number order**:
   a. the item's Completed state row, `ItemCompleted` history event and audit entry (the same effects as STORY-008, produced by the same internal helpers, not copied);
   b. the `CompletedProcedures` row;
   c. the odontogram effect (4.5);
   d. the charge through `IProcedureCharging` with the set's correlation id (4.6).
5. **Save and commit.** Any exception rolls back everything and clears the change tracker.
6. **Return** the refreshed plan and a per-item result: completed-procedure id, chart outcome, charge outcome (`created`, `existing`, `none`) with entry number and amount.

Attribution rules: the staff member who submitted is recorded as the finalizer; the provider and performed time are the clinical facts supplied; the encounter is the visit.

### 4.4 Concurrency and duplicates

- **Stale plan:** the request carries the plan's row version; a mismatch is a `ConcurrencyConflictException` (409) before any write, exactly as in STORY-008.
- **Two submissions racing for the same item:** resolved by existing and new unique constraints — `CompletedProcedures.PlanItemId`, `TreatmentPlanItemStates (ItemId, Sequence)`, the charge's idempotency key. The loser's save fails, the transaction rolls back, and the caller receives a conflict. Exactly one wins; no item is completed or charged twice.
- **Double click / retry with the same key:** the replay path in 4.3 step 2.
- **Retry with a new key after success:** refused as `items_not_completable` (`already_completed`), because items are already completed. It never creates a second charge: the charge contract is itself idempotent per plan item.

### 4.5 Odontogram effect

Chain: **plan item → its diagnosis → `DiagnosisLinks` of type `Finding` → an active finding of the same patient on the same tooth (and the same surface when the item has one)**.

- Exactly one matching active finding in state Diagnosed or Planned: move it to Completed using the same rule table as `ChangeStateAsync` (a stage-only variant extracted so it can run inside this transaction), with its version row and audit entry. `ChartOutcome = Updated`.
- Matching finding already Completed: no change, `AlreadyCompleted`.
- No linked finding, a whole-mouth procedure, or a finding in a state that cannot move: no chart change, `NotApplicable`, reported per item so the screen can say so honestly.
- More than one matching finding: the completion **fails the whole set** with `ambiguous_chart_finding` naming the item (a wrong chart update is worse than a refusal). See open question Q2.

An odontogram failure rolls the whole set back (a named failure path).

### 4.6 Billing effect

- **Billable** means the plan item's stored fee is **greater than zero** (D4). A zero-fee procedure completes and reports `charge: none` (no ledger entry), because the ledger refuses a zero charge.
- For a billable item the service calls `IProcedureCharging.ChargeCompletedProcedureAsync` inside its own transaction on the same context. The charge amount comes from the plan item; the request cannot carry one. No direct ledger writes.
- One charge per billable completed procedure is guaranteed by the existing per-plan-item idempotency; the new set and unique indexes add a second line of defence.
- **Financial-effect audit:** each charge already writes its own audit entry (ALV-007-C01); the set writes one additional PHI-free audit entry that states how many procedures completed and how many charges were created, with the correlation id, so a reviewer can trace one submission end to end.
- If any charge fails, the whole set rolls back (a named failure path). If a charge already exists for an item (for example one billed by hand earlier), it is reused (`existing`), never duplicated.

### 4.7 Finalization and amendment semantics

- A completed procedure is **finalized on creation**. There is no draft state in this story.
- It can never be deleted or rewritten (triggers, 4.2).
- `POST api/completed-procedures/{id}/amend` appends an amendment (provider, performed time and/or encounter) with a required reason and the record's row version for stale-edit detection. The original values stay readable.
- Amendments never touch the charge, the odontogram or the plan state. Undoing a completion (re-opening a completed procedure, reversing its charge as a consequence) is **not** in this story; billing already supports reversing a charge on its own terms.

### 4.8 Relationship to the STORY-008 contract (immutable)

Parent regression is a stop condition. The design therefore **does not change** the STORY-008 or STORY-007 endpoints or their results:
- `POST .../items/{itemId}/complete` and `POST .../complete-accepted` behave exactly as today (no chart effect, no auto-charge, no new record).
- The clinical history stays derived from completion state rows (so it still shows every completion), and **enriches** an entry with provider, performed time, encounter and charge number when a `CompletedProcedures` row exists for it.
- A procedure completed by the old paths has no `CompletedProcedures` row. The new operation refuses it as `already_completed`; amending it is not possible. This is a stated limitation (see D1 for the alternative).

### 4.9 API contract

| Method and route | Permission | Notes |
|---|---|---|
| `POST api/treatment-plans/{planId}/complete-procedures` | `ManageTreatmentPlans` + CSRF | body above; 200 with plan and per-item results |
| `GET api/patients/{patientId}/completed-procedures` | `ViewClinicalDocumentation` | current values, newest first |
| `GET api/completed-procedures/{id}` | `ViewClinicalDocumentation` | original, amendments, chart outcome, charge link |
| `POST api/completed-procedures/{id}/amend` | `ManageTreatmentPlans` + CSRF | `{ reason, providerId?, performedAtUtc?, encounterId?, rowVersion }` |

Stable error codes: `validation_failed` (400, with `fieldErrors`), `plan_not_found` / `patient_not_found` / `completed_procedure_not_found` (404), `concurrency_conflict`, `plan_withdrawn`, `items_not_completable` (with per-item reasons), `idempotency_conflict`, `ambiguous_chart_finding` (409), `save_failed` (503, no internals). Requests are validated at the boundary with explicit field errors; malformed input never reaches business logic.

The charge call is a **service-level** contract; the signed-in dentist does not need `ManageBilling` for the charge to be created, because the charge is a consequence of a clinical completion and uses the fee the plan stored. Billing permissions still govern every ledger action a person takes directly.

### 4.10 Security, audit and observability

- Authentication and role on every route; resource ownership (patient/plan/encounter/provider consistency) checked in the service and again by triggers.
- Audit entries are PHI-free and written in the same save as the effect (so a failed audit write completes nothing, as in STORY-008).
- Structured log event per submission with `correlation_id`, outcome, duration and counts; on failure only the error class and SQL error number, never plan, patient or SQL text (the existing `treatment_plan_save_failed` pattern).
- No secrets, no PHI in logs, no new external dependency.

### 4.11 UI

On the patient's Treatment plan tab, beside the existing **Complete accepted procedures** button (kept; see D2), a new **Review and complete…** button opens the completion review screen:
- a table of every accepted, open, non-withdrawn procedure with a checkbox, code and description, tooth/surface, and its **resulting charge effect** (`Charge $120.00` or `No charge`) and **chart effect** (`Tooth 16 finding will be marked Completed`, `Chart not applicable`, or a named ambiguity that blocks the set);
- provider (active providers only), performed date and time (default: now), and encounter (the patient's eligible encounters);
- an explicit **Confirm completion of N procedures** that shows the total charge;
- on success: a summary with links to the clinical history, the odontogram and the account ledger;
- on any failure: an alert that says **nothing was completed**, with what was entered kept; the screen never shows procedures as completed unless the server confirmed the whole set; a stale plan shows the existing conflict banner.
Accessible by construction (labels, roles, keyboard, contrast in both themes), checked with axe in unit and real-browser tests.

### 4.12 Failure-first answers (the four questions)

1. **What happens if it fails?** Nothing is stored; the caller is told so with a stable code.
2. **Retry?** Not automatic. A user retry with the same key is safe (replay returns the original result); with a new key after success it is refused, never duplicated.
3. **Recovery when it keeps failing?** Manual intervention; the structured log carries the correlation id, error class and SQL error number for triage.
4. **Handled vs not.** Handled: each failure path in section 6. Not handled: a connection that drops after the server committed but before the answer arrived (the retry with the same key returns the committed result, which is the designed recovery); a completion of care that was never planned.

---

## 5. Parent-regression strategy

- Run the full STORY-008 backend, frontend and real-browser tests unchanged.
- Run STORY-007 and ALV-007-C01 billing tests unchanged (they complete an item and charge it by hand, which the design leaves intact).
- Run the ALV-015-C01 and STORY-015 treatment-plan tests unchanged.
- The full serial backend suite, the frontend suite, the mocked browser suite (136), the repository checks (8) and the real-backend sweep of all existing walkthroughs must pass, with database and login leak counts unchanged.
- Any test that must change is listed in the handoff with the reason; the intent is zero.

---

## 6. Failure paths → design → test

| Failure path | Behaviour | Proving test |
|---|---|---|
| One charge fails in a multi-procedure completion | whole set rolls back; no state row, record, chart change or charge remains | fail the charge (trigger on the ledger) for the second of three items; assert all counts unchanged |
| Odontogram update fails | whole set rolls back | make the chart write fail; assert unchanged |
| Duplicate submission | same key and payload: original result, no writes; same key and other payload: 409 | submit twice (sequential and concurrent) |
| Stale plan version | 409, nothing written | rename the plan between read and submit |
| Item already completed or withdrawn or not accepted | 409 naming each item and reason; nothing written | mixed selections |
| Audit or finalization persistence failure | whole set rolls back; `save_failed` 503 with no internals | trigger refusing the set's audit entry |
| Two racing sets for one item | exactly one wins, other is a conflict, one charge | concurrent submissions |
| Ambiguous chart match | refused with the item named; nothing written | two findings matching |

---

## 7. Acceptance mapping and test plan

| Acceptance criterion | Evidence planned |
|---|---|
| Original STORY-008 tests still pass | unchanged STORY-008 backend, frontend and walkthrough tests |
| One or multiple selected procedures complete in one authoritative operation | service and API tests (single, many, subset of a plan), real-browser walkthrough |
| Linked to the exact source plan/version | assertions on `PlanId`, `PlanRevisionNumber`, `ProcedureVersionId`, after a later revision |
| Odontogram updates when applicable | tests for Updated / AlreadyCompleted / NotApplicable / ambiguous |
| Exactly one linked charge per billable completed procedure | cardinality test across a set, a replay and a manual pre-existing charge; zero-fee case; Gate D2 "zero orphaned, zero missing" reconciliation |
| Duplicate/retry does not double-complete or double-charge | idempotency and concurrency tests |
| A failed set leaves no mixed state | the rollback tests in section 6, each asserting every table's count |

Additional planned evidence: schema tests for each trigger and constraint; migration Down/Up check; negative controls (mutate the set transaction, the replay check, the one-charge rule, the chart move, the immutability triggers, and confirm each is caught, then restore byte-identical); frontend unit tests with axe; a real-browser walkthrough with a failure path and a stale second screen; the full sweep with leak counts. Everything is packaged per `.alveara/HANDOFF_SCHEMA.md` (implementation commit, evidence commit, status commit, ZIP) and ends at `AWAITING_REVIEW`.

---

## 8. Alternatives considered and rejected

- **Delegate the legacy endpoints to the new operation.** Rejected (see D1): it changes behaviour the parent stories' tests rely on.
- **Store the charge inside the completed-procedure record.** Rejected: duplicates ledger truth; the existing `PlanItemId` link is authoritative.
- **Mutable completed-procedure rows with an "edited" flag.** Rejected: violates "cannot be silently rewritten".
- **Allow a zero-amount charge by changing the ledger rule.** Rejected: ALV-007-C01's contract is approved and immutable.

---

## 9. Decisions requested from the reviewer

**D1 — Legacy completion endpoints.**
*Recommend:* leave `complete` and `complete-accepted` exactly as they are; the new operation is the product path; the history is enriched when a new record exists.
*Alternative:* make the legacy endpoints delegate to the new operation.
*Consequence of the alternative:* completion without provider/encounter becomes impossible or needs nullable attribution, and the STORY-007 tests that complete an item and charge by hand would change behaviour.

**D2 — The existing "Complete accepted procedures" button.**
*Recommend:* keep it (its tests and the verified course walkthrough stay valid) and add "Review and complete…" next to it.
*Alternative:* replace it with the review screen and update the STORY-008 UI tests.
*Consequence:* two entry points to explain; mitigated by clear labels and a note that the quick button records no provider, encounter, chart change or charge.

**D3 — Amendment semantics.**
*Recommend:* include an append-only amendment table now (provider, performed time, encounter, with a required reason).
*Alternative:* finalize-only now, amendments later.
*Consequence:* the story names "finalization/amendment semantics", so deferral leaves a stated bullet open.

**D4 — What "billable" means with a zero fee.**
*Recommend:* billable means fee > 0; a zero-fee procedure completes with `charge: none`.
*Alternatives:* refuse zero-fee completions (blocks legitimate no-charge care); change the ledger to allow zero charges (alters an approved contract).

**D5 — How a completed procedure finds its chart finding.**
*Recommend:* plan item → diagnosis → linked finding on the same tooth/surface; none = not applicable; several = refuse the set.
*Alternatives:* add an explicit finding reference to plan items (a schema change to plans); match by tooth and surface alone (can move the wrong finding).

**D6 — Which encounters a completion may attach to.**
*Recommend:* required; must belong to the same patient; **Draft** encounters only (completion happens during the visit; a finalized encounter is a signed record).
*Alternatives:* allow Finalized too; make the encounter optional.

**D7 — Provider and performed-time rules.**
*Recommend:* provider required and active; performed time required, not in the future (5-minute clock tolerance), not before the item was accepted, not before the plan was created.
*Alternative:* looser or stricter limits.

**D8 — Permission model and scope of one operation.**
*Recommend:* reuse `ManageTreatmentPlans` (dentist, administrator) for completion and amendment, `ViewClinicalDocumentation` for reads; one operation covers **one plan** (a visit that spans several plans is several submissions).
*Alternatives:* a new dedicated permission; allow selection across plans in one submission.

---

## 10. Risks and open questions

**Risks.**
- **Zero-fee rule** (D4) is a real ledger constraint found while preparing this proposal, not a hypothetical.
- **Chart ambiguity** (D5) depends on how consistently diagnoses are linked to findings in real data; the refusal is deliberately conservative.
- **Two entry points** (D2) could confuse users until the quick button is retired in a later story.
- **Effort:** comparable to ALV-007-C01 (a migration, a transaction service, an API, a review screen, ~100 tests, a sweep and a handoff).

**Open questions.**
- **Q1.** Should the performed time be a date only or a date and time? (Proposed: date and time.)
- **Q2.** Is refusing an ambiguous chart match acceptable, or should the clinician be able to choose the finding on the review screen? (Proposed: refuse now; choosing is a later enhancement.)
- **Q3.** Should the post-completion summary also offer "bill now / view statement"? (Proposed: link to the ledger only; statements belong to ALV-007-C02.)

**Not verified yet (will be verified during implementation, and reported either way).**
- That the odontogram's stage-only extraction preserves every existing `ChangeStateAsync` behaviour and test.
- That `IProcedureCharging` behaves correctly when called several times in one transaction with the context already holding staged changes (the contract's own tests cover one call; the multi-call case is new).
- Exact row-version behaviour of the plan when only state rows change (STORY-008 relies on it; this story adds more writes under the same check).

---

## 11. Work plan and packaging

1. Migration, three tables, constraints and triggers (51144+), schema tests.
2. Service and transaction, odontogram stage-only extraction, charge integration, idempotency, rollback.
3. API routes, contract tests, role and CSRF tests.
4. History enrichment (read side only).
5. Review screen, frontend tests with axe.
6. Real-browser walkthrough (success, failure path, stale second screen, zero-fee, ambiguity), mocked suite, full serial backend run, real-backend sweep, negative controls, migration Down/Up.
7. Docs, handoff package per `.alveara/HANDOFF_SCHEMA.md`: implementation commit, evidence commit, status commit, validated ZIP; state `AWAITING_REVIEW`.

Work is paced in small reversible steps; nothing is pushed without the owner's instruction; no secrets or real patient data are introduced.

---

## 12. Reviewer response template

```
ALV-008-C01 design review — decision: APPROVED / APPROVED WITH CHANGES / CHANGES REQUIRED

D1 legacy endpoints:        APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D2 existing button kept:    APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D3 amendments included:     APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D4 zero-fee = no charge:    APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D5 chart linkage:           APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D6 encounter rule:          APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D7 provider/time rules:     APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
D8 permissions and scope:   APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___

Open questions Q1–Q3: answers or "proceed with the proposal".
Additional requirements or risks the reviewer wants covered: ___
```
