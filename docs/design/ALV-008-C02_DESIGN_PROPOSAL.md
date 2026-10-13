# ALV-008-C02 — Design Proposal for Independent Review

**Story:** ALV-008-C02 — Retire Legacy Completion Paths and Adopt Historical Completions (execution item 36)
**Type:** Companion to course story STORY-008 (portal-verified; its portal state is not touched by this story)
**Document status:** PROPOSAL. No ALV-008-C02 code has been written. This document asks the reviewer to approve, amend or reject the design and the eight decisions in section 8 before implementation starts.
**Prepared:** 2026-10-12, session CC-20261008-k4q9
**Repository state when written:** `main` at `83f6da5` (local; nine commits ahead of `origin/main`, not pushed). Dependencies are all `COMPLETE`: STORY-008 (portal-verified at `8da1119`), ALV-008-C01 (approved R01, closure `593ff2c`) and ALV-007-C02 (approved R02, closure `83f6da5`).

**How to respond.** Section 10 is a response template. For each decision D1–D8 reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead). Anything not answered is treated as "recommendation not yet approved".

---

## 1. What the story requires (from the master execution plan, item 36, as amended by the reviewer's earlier limitations response)

Retire the two STORY-008 state-only completion routes and their quick-complete UI as production pathways; add an explicit, one-procedure, audited **adoption** command for a procedure completed through a legacy path (provider, same-patient Draft or Finalized encounter, performed time, non-blank reason; creates the immutable completed-procedure record, applies the chart effect and calls `IProcedureCharging` in the same transaction; reuses an existing manual charge or creates exactly one); after the announced transition period the retired routes return a safe `410 Gone` pointing at the authoritative workflow and must no longer create any completion state; no hidden production bypass; preserve all immutable history (do not rewrite a legacy state row, an original audit event, a completed-procedure record or a ledger entry); adoption needs `ManageTreatmentPlans` plus CSRF, is idempotent and concurrency-safe, records PHI-free audit and structured events, and enforces the same provider, encounter, performed-time, odontogram, billing, idempotency and transaction rules as ALV-008-C01. Bulk historical migration is out of scope.

**Stop conditions (6).** (1) No production route or UI path can create a new state-only completion. (2) Every legacy-completed procedure can be explicitly adopted without rewriting its historical state or audit record. (3) Adoption creates or reuses exactly one linked charge per billable procedure, applies the required chart outcome and leaves no mixed state after a failure. (4) STORY-008 and STORY-007/ALV-007-C01 regression intent remains proven by mapped authoritative-workflow tests or clearly isolated test fixtures. (5) A repository-wide caller search finds no remaining production caller of the retired routes. (6) Gate D reconciliation evidence reports zero orphaned charges, zero missing charges among authoritative or adopted completed procedures, and matching odontogram state.

**Required tests.** Legacy route `410` and removed quick-complete UI; adoption success for an unbilled legacy completion, adoption reusing a manual charge, duplicate/retry, same-key conflict, stale and concurrent adoption, rollback after chart, billing and audit failure; parent-regression mapping evidence and the full upstream suites; the repository caller search; a real-browser adoption and retirement walkthrough.

## 2. Verified current state (what exists today)

- **The two legacy routes** are `POST api/treatment-plans/{planId}/items/{itemId}/complete` and `POST api/treatment-plans/{planId}/complete-accepted` in `TreatmentPlansController`, calling `TreatmentPlanService.CompleteItemAsync` and `CompleteAcceptedAsync` (`TreatmentPlanService.States.cs`). Each use already logs `legacy_completion_used` (route, outcome, status, trace id; no PHI), as the reviewer approved in ALV-008-C01 (R1a).
- **They write**: a new `TreatmentPlanItemStates` row (Completed), an `ItemCompleted` plan event and a `TreatmentPlanItemCompleted` audit entry per procedure, through the shared helper `StageCompletionsAsync`, which the authoritative workflow also uses. They do not record a provider, an encounter, a performed time, a chart effect or a charge.
- **The authoritative workflow** (`CompletedProcedureService.CompleteAsync`, ALV-008-C01) refuses a procedure that is already Completed (`RequireCompletable`), so a legacy-completed procedure can **never** be completed again that way; today it has a Completed state and nothing else (a charge only if someone billed it by hand).
- **Immutable records.** `CompletionSets` (the idempotency anchor: unique per plan and key, request hash, correlation id), `CompletedProcedures` (one per plan item, finalized on creation) and `CompletedProcedureAmendments` are protected by triggers 51144–51156. The performed-time rules are triggers 51153 (not before the plan was created) and 51154 (not before it was accepted, compared with the recorded acceptance time); 51150 requires the item to have a Completed state row and not be withdrawn.
- **The charge contract.** `IProcedureCharging.ChargeCompletedProcedureAsync` runs inside the caller's transaction, reuses an existing charge for the plan item (`created: false`) or creates exactly one at the plan's stored fee under the key `procedure-charge-{planItemId}`; a no-fee procedure completes with no charge.
- **Production UI.** The Treatment plan tab offers **Review and complete…** (authoritative), a labelled "Legacy completion" group with the **Complete accepted procedures** button, and a per-procedure **Mark X completed** form (also labelled legacy).
- **Callers of the legacy paths** (searched): production code is the controller, the service and the two frontend client functions (`completeItem`, `completeAcceptedProcedures`) with their two components. Tests: 24 backend test files use `CompleteItemAsync`, `CompleteAcceptedAsync` or the routes (most as arrangement for "completed, not yet billed", then billed by hand; the STORY-008 parent suites `TreatmentPlanCompleteAcceptedTests`, `TreatmentPlanCompleteAcceptedApiTests` and `TreatmentPlanCompletionApiTests` test the routes themselves), 4 frontend unit-test files, and 4 real-backend walkthroughs (`billing`, `billing-ledger`, `billing-statements`, `treatment-plans-complete-accepted`).
- **No production data exists** (the product has no deployed users); the legacy completions that exist are test and demo data.

## 3. The gaps this story must close

1. A way to create a state-only completion still exists in the product (two routes, two controls).
2. A procedure completed that way cannot be given provider, encounter, performed time, chart effect or an authoritative charge, and cannot be amended.
3. There is no check that proves "every completed procedure has a charge, no charge is orphaned and the chart matches" (Gate D1/D2 reconciliation).
4. 24 test files and 4 walkthroughs depend on the legacy paths and need a deliberate, test-by-test disposition so the parent regression intent survives.

## 4. Proposed design

### 4.1 Retirement of the legacy routes (decisions D1, D2)
- Both routes keep their authentication, role and CSRF checks (anonymous 401, a role without `ManageTreatmentPlans` 403, a missing CSRF token 400, exactly as today) and then answer **`410 Gone`** with the stable body `{ error: "legacy_completion_retired", message, replacement: { completeProcedures: "POST api/treatment-plans/{planId}/complete-procedures", adopt: "POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion" } }`. They create no state, no event, no audit entry for a completion and touch no row.
- The controller handler stays only to return the `410`; **`CompleteItemAsync` and `CompleteAcceptedAsync` are deleted** from the service, so no code path can create a state-only completion. The shared `StageCompletionsAsync` stays (the authoritative workflow uses it).
- Each attempt is logged as the PHI-free structured event `legacy_completion_retired_attempt` (route, status 410, trace id), replacing `legacy_completion_used`, so usage after retirement stays observable.
- **No transition period and no configuration flag.** The prompt says "after an announced transition period"; the retirement was announced in ALV-008-C01 (the legacy labelling, the `legacy_completion_used` log and the reviewer's own limitations response), and there is no production traffic to protect. A date-driven or flag-driven switch would itself be a hidden bypass. The reviewer is asked to confirm (D1).

### 4.2 UI (decision D3)
- Remove the **Complete accepted procedures** group and the per-procedure **Mark X completed** form and delete `completeItem`, `completeAcceptedProcedures` and their components; **Review and complete…** is the only completion control.
- Add an **Adopt** action, offered only on a procedure that is Completed and has no completed-procedure record (the plan view already carries each procedure's completion state; readiness data from the server says which are legacy). It opens a small form: provider, the patient's encounter, performed time, and a required reason, with a plain statement of what it does ("records the missing attribution for a procedure already marked completed; moves the chart finding when one matches; creates or reuses its one charge; the original completion record is not changed") and the server's refusal shown beside the field. A result panel shows what was recorded.

### 4.3 The adoption command (decisions D4, D5, D6)
`POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` — `ManageTreatmentPlans` plus CSRF. Body: `{ idempotencyKey, rowVersion, encounterId, providerId, performedAtUtc, reason }`. Reads keep `ViewClinicalDocumentation` (no new permission).

**Eligible** when the item belongs to the plan, is not withdrawn, its current state is Completed, and **no** `CompletedProcedures` row names it. Anything else is refused with a stable code (`not_a_legacy_completion` when it is not completed or already has a record, `item_withdrawn`, `plan_item_not_found`).

**Rules reused unchanged from ALV-008-C01** (the same `CompletionRules`, `PerformedTimeProblem`, provider and encounter checks, `ResolveChartAsync` and `StageChartAsync`, `IProcedureCharging`): the provider is active; the encounter is the patient's own, Draft or Finalized, and is never changed; the performed time is at most five minutes ahead of the clock (inclusive) and not before the plan was created or before the recorded acceptance; the chart finding is found only through the plan's diagnosis link on the same tooth and surface (more than one match refuses with `ambiguous_chart_finding`); billable means a stored fee above zero.

**In one transaction** (all or nothing, two saves as in ALV-008-C01 so the database sees the rows in a valid order):
1. a `CompletionSets` row for the adoption (ItemCount 1, the caller's key and canonical request hash, a new correlation id, the plan revision) carrying the adoption **reason**;
2. the immutable `CompletedProcedures` row (plan, revision, item, catalog version, tooth and surface, encounter, provider, performed time, chart outcome and finding, who and when);
3. the chart effect through the odontogram service (an eligible finding moves to Completed with its usual history and audit; an already-completed finding is reported; no match is `NotApplicable`);
4. the charge through `IProcedureCharging` with the set's correlation id (an existing manual charge for the plan item is reused; otherwise exactly one is created; a no-fee procedure creates none);
5. the plan row's version is advanced in the same transaction (as ALV-008-C01 does), so a plan screen opened before the adoption is stale and the same-procedure race has one winner;
6. one PHI-free `CompletionAdopted` audit entry (counts, the correlation id, no names or clinical detail).

**Nothing historical is rewritten.** The legacy `TreatmentPlanItemStates` row, its `ItemCompleted` event and its original audit entry stay exactly as they are (the trigger 51150 already requires that Completed state row to exist, so adoption needs no new state row); no ledger entry is edited. The clinical history, which already joins `CompletedProcedures` to plan items, shows the adopted procedure's provider, performed time, encounter, charge and chart outcome with no change to its read code.

**Idempotency and concurrency.** The same key and canonical request replays the saved result with no writes (from any plan version); the same key with a different request is `idempotency_conflict`; the same procedure under a new key after success is `not_a_legacy_completion`; two adoptions of one procedure at once end with one winner because the plan version is checked and advanced atomically and the unique index on `CompletedProcedures.PlanItemId` is the backstop; the charge key `procedure-charge-{planItemId}` already makes a second charge impossible.

**Failure.** A failed chart move, charge or audit write rolls the whole transaction back (`save_failed`, 503, nothing recorded) and logs only the error class and correlation id.

### 4.4 Data (decision D4)
One small migration, **`AddCompletionAdoption`**: a nullable `CompletionSets.AdoptionReason` (max 500) with a check constraint (when present it is not blank and has no control characters), and one trigger (error number **51172**; 51173 and up stay free): a completion set that carries an adoption reason has `ItemCount = 1`. The reason is written with the row and, like the rest of the set, is protected by the existing append-only trigger 51144. `Down` refuses (a `THROW`) while any adopted set exists, and otherwise drops the column and the trigger; it is only possible before an adoption is recorded, the same stance as the earlier ledger migrations.

**Alternative considered and rejected:** a separate `CompletionAdoptions` table. It would repeat the idempotency key, request hash, correlation id and plan link that `CompletionSets` already holds, add a second place to read, and add triggers, for no benefit.

### 4.5 Reads
`CompletedProcedureView` and the completed-procedure detail gain optional `adopted` (bool) and `adoptionReason` (read from the set), so the history and the review screen can show "adopted from a legacy completion" with its reason. A new read-only **readiness for adoption** is not needed: the plan view already says a procedure is Completed, and the new `GET api/treatment-plans/{planId}/completion-readiness?itemIds=` already reports the legacy case; it gains a `legacyCompleted` flag per procedure so the screen need not infer it.

### 4.6 Gate D reconciliation (decision D7)
A read-only report, **`GET api/completion/reconciliation`** (`ViewAuditLog`, i.e. the administrator and the practice manager; counts only, no patient, plan or clinical detail), computed from the preserved rows:
- `legacyCompletedNotAdopted` — completed, non-withdrawn plan items with no completed-procedure record (the adoption backlog);
- `completedMissingCharge` — completed-procedure records whose plan fee is above zero and that have no charge naming the plan item;
- `completedWithUnexpectedCharge` — records whose fee is zero but that have a charge;
- `orphanedCharges` — procedure charges (entries naming a plan item) whose plan item has no completed state;
- `chartMismatch` — records whose outcome is `Updated` or `AlreadyCompleted` and whose finding's current state is not Completed;
- `duplicateCharges` — plan items with more than one charge (also impossible by the unique index; reported as proof).
The stop condition is all of those except the backlog being zero, and the backlog being zero once adopted. The report is the Gate D reconciliation evidence and is exercised by tests that seed each defect directly (so a regression to any of them would be caught), by the full adoption walk, and in the real-browser walkthrough.

### 4.7 Test strategy for the parent regression (decision D8)
- **A test-only fixture** (`LegacyCompletionFixture`, in the test project, not in the product) reproduces exactly what the old routes wrote by calling the same shared helper `StageCompletionsAsync` (plus the plan version touch), so the rows have the same shape the old service produced and no row format is duplicated. It is the only thing that can create a legacy completion, and only inside tests.
- **Arrangement-only uses** (the billing, billing-ledger, refund, document, timeline, source and reconciliation tests, `BillingTestBase`, the `PatientFlow`, `Visits` and `TreatmentPlanProcedureUsage` API tests, and so on) switch from `CompleteItemAsync` or the HTTP route to the fixture; their intent ("a procedure is completed and not yet billed, so billing bills it by hand") is unchanged and the legacy-completed procedure is a legitimate persistent state that adoption exists to resolve.
- **Tests of the legacy routes themselves** (`TreatmentPlanCompleteAcceptedTests`, `TreatmentPlanCompleteAcceptedApiTests`, `TreatmentPlanCompletionApiTests`, and the legacy cases in `TreatmentPlanStateServiceTests` and `CompletionApiTests`) are disposed one by one in a **mapping table** delivered with the handoff: each is either replaced by the authoritative-workflow test that already proves the same intent (for example "completing procedures records state, history and audit" is covered by `CompletedProcedureServiceTests`; "stale plan version is refused" by the completion's atomic version check), converted into a `410` test, or kept against the fixture where it tests a state-only completed procedure's consequences (billing eligibility, history display). Nothing is deleted without a named replacement.
- **Frontend unit tests** for the removed controls become tests that the controls are absent and that **Review and complete…** is the only completion action; the adopt form gets its own tests.
- **Real-backend walkthroughs** that completed procedures through the legacy HTTP route as arrangement (`billing`, `billing-ledger`, `billing-statements`) seed a legacy completion through a small SQL helper in the spec (the spec already has `E2E_DB_NAME`), clearly labelled test-only; `treatment-plans-complete-accepted-real-backend.spec.ts` (the STORY-008 walkthrough) is replaced by a **retirement and adoption walkthrough** (the controls are gone, a seeded legacy completion is adopted through the screen, the chart and charge follow, the failure path stores nothing, a second screen is stale, a read-only role is offered nothing).
- The course portal's STORY-008 verification state is not read or written by any of this.

### 4.8 Security, audit and observability
Adoption: `ManageTreatmentPlans` plus CSRF (a role without it 403; no CSRF 400; anonymous 401); reads keep `ViewClinicalDocumentation`; the reconciliation report `ViewAuditLog`. Audit entries (`CompletionAdopted`) carry counts and the correlation id only. Structured events: `completion_adopted` (correlation id, duration, outcome), `completion_adoption_not_recorded` (error class, correlation id), `legacy_completion_retired_attempt` (route, status, trace id). No patient, plan, clinical or SQL detail in any log, audit text or error body.

### 4.9 Failure-first answers
1. **What if adoption fails?** The transaction rolls back; nothing is recorded; the caller sees `save_failed` (503) and can retry with the same key. 2. **Retry?** Only by the caller, with the same key; there is no automatic retry. 3. **Recovery if exhausted?** The procedure stays legacy-completed and appears in the reconciliation backlog; adoption can be retried later. 4. **Handled:** wrong state, withdrawn, already adopted, ambiguous chart match, bad provider or encounter, bad performed time, stale plan, duplicate key with another request, concurrent adoption, failing chart, charge or audit write. **Not handled (by design):** an adoption whose true performed time is earlier than the recorded acceptance (see D5), bulk or automatic adoption.

## 5. Acceptance mapping and test plan

| Stop condition | Proof |
|---|---|
| 1. No route or UI path creates a state-only completion | `410` tests for both routes (including that no row, event or audit entry appears and the role/CSRF/401 contract is kept); the service methods no longer exist (compilation); UI tests that both controls are absent; the caller search (below) |
| 2. Every legacy completion can be adopted without rewriting history | adoption of a legacy-completed procedure for each of: unbilled, manually billed, no-fee, with a matching chart finding, already-completed finding, no match; the legacy state row, event and audit entry are byte-identical before and after; the clinical history shows the adopted detail |
| 3. Exactly one linked charge, chart applied, no mixed state on failure | reuse of a manual charge (`created: false`), creation of exactly one, no second charge on retry or race; rollback tests with a failing chart move, charge and audit write (counts of every table unchanged) |
| 4. Parent regression intent proven | the mapping table (every test, its disposition, its replacement) and the full upstream suites |
| 5. No production caller remains | a committed caller search (grep of the routes and the two method names over `src/` excluding tests, plus a repository test that fails if a production reference reappears) |
| 6. Gate D reconciliation | the report endpoint's tests (each defect seeded; all zero after a full walk) and its output in the handoff |

Also: idempotent replay, same-key conflict, stale plan, deterministic concurrent adoption (a hooked clock, as in ALV-007-C02), the role matrix, deterministic negative controls for each guard (including guards protected by another guard), the migration down/up, full serial backend, vitest, mocked Playwright, repository checks and the real-backend sweep with leak counts.

## 6. Alternatives considered and rejected
- **A configuration switch or date for the `410`.** A switch is a bypass; there is no production traffic to protect.
- **Keeping the legacy methods and gating only the routes.** Leaves a callable bypass in the service.
- **A separate adoption table**, **a new plan event type for adoption**, or **a new state row**: duplicate what `CompletionSets` and the existing Completed row already provide, and add schema to the immutable plan history.
- **Adopting every legacy completion by a migration script.** Out of scope and forbidden by the story (bulk migration).
- **An exception to the performed-time triggers for adoption.** See D5.

## 7. Risks and open questions
- A legacy completion's recorded acceptance time may be later than the real performed time (acceptance entered late). The adoption refuses a performed time before the recorded acceptance like every other completion; the clinician records the earliest honest time at or after it and says so in the reason. If the reviewer wants an audited override, it needs a new, explicit trigger exemption (a schema and policy decision) and is not proposed here.
- Removing the controls changes the screens that demos and the earlier STORY-008 walkthrough used; the mapping table and the replacement walkthrough cover this.
- 24 test files and 4 walkthroughs change arrangement; the risk is a silent change of intent, which the fixture (same row shapes by construction) and the mapping table are designed to prevent.
- The reconciliation report's `chartMismatch` depends on the odontogram's current finding state; a later legitimate chart change (a finding moved on after completion) must not count as a mismatch, so it is defined against the finding's state only when the recorded outcome says the completion moved or found it Completed and the finding has not since been changed by a later recorded state (to be pinned in the tests).

## 8. Decisions requested from the reviewer

| # | Decision | Recommendation |
|---|---|---|
| D1 | Retire both routes to `410 Gone` now, with no configuration flag or date, and delete the two service methods | Approve |
| D2 | Keep the authentication, role and CSRF contract on the retired routes; log `legacy_completion_retired_attempt` (route, status, trace id) | Approve |
| D3 | Remove both quick-complete controls and add an **Adopt** action and form on legacy-completed procedures | Approve |
| D4 | Model adoption as a completion set with a stored adoption reason (one migration, one nullable column, one trigger numbered 51172; `Down` refuses while an adoption exists), not a new table, event type or state row | Approve |
| D5 | Adoption enforces the same performed-time, provider, encounter, chart, billing and idempotency rules as ALV-008-C01 with **no** exemption for old acceptance times | Approve |
| D6 | The adoption endpoint is `POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` with `ManageTreatmentPlans` plus CSRF and the body in section 4.3 | Approve |
| D7 | Provide the read-only reconciliation report `GET api/completion/reconciliation` (`ViewAuditLog`, counts only) as the Gate D evidence | Approve |
| D8 | Parent-regression strategy: a test-only fixture built on the shared staging helper, a committed mapping table of every affected test and walkthrough, and SQL-seeded legacy completions in the real-browser specs | Approve |

## 9. Work plan and packaging
Steps (each tested before the next): (1) the migration, the adoption service and its contracts; (2) the `410` handlers, deleting the legacy service methods, and the fixture; (3) migrating the arrangement-only tests and writing the mapping table; (4) the adoption and reconciliation API and its tests, concurrency and rollback controls; (5) the UI (remove controls, adopt form) and its tests; (6) the walkthrough specs and the real-backend sweep; (7) the full evidence (serial backend, vitest, mocked Playwright, repository checks, negative controls, leak counts), the caller search and the reconciliation output; (8) docs, the handoff package, three commits and the ZIP, stopping at `AWAITING_REVIEW`.

## 10. Reviewer response template

| Decision | Disposition (APPROVE / APPROVE WITH CHANGE / REJECT) | Binding change or instruction |
|---|---|---|
| D1 | | |
| D2 | | |
| D3 | | |
| D4 | | |
| D5 | | |
| D6 | | |
| D7 | | |
| D8 | | |

Additional instructions or constraints:
