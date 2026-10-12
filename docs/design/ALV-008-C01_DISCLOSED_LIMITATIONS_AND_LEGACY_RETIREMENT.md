# ALV-008-C01 — Disclosed Limitations and a Recommendation to Retire the Legacy Completion Paths

**For:** the independent reviewer of ALV-008-C01
**Follows:** `docs/design/ALV-008-C01_DESIGN_PROPOSAL.md` and the reviewer's `docs/design/ALV-008-C01_DESIGN_REVIEW_RESPONSE.md` (APPROVED WITH CHANGES, 2026-10-11)
**Document status:** REQUEST FOR DECISION. No ALV-008-C01 code has been written. The implementation will follow the approved response unchanged; this document asks the reviewer to rule on four open points (R1–R4) so that the limitations they create are handled deliberately rather than discovered later.
**Prepared:** 2026-10-11, session CC-20261008-k4q9. Repository when written: `main` at `d44773a` (STORY-008 recorded complete; portal verified at `8da1119`).

**What is asked.** Section 6 is a response template. For R1–R4 reply APPROVE, APPROVE WITH CHANGE, or REJECT. Anything unanswered will be raised again rather than assumed.

---

## 1. Summary

The approved design is sound and will be implemented as written. While confirming it, the implementer identified three consequences that the reviewer accepted in principle but which deserve explicit rulings, and one structural problem that the approved decision D1 deliberately leaves open:

| # | Point | Severity | Recommendation |
|---|---|---|---|
| R1 | **The legacy completion paths remain reachable** and can create completions that bypass the chart, the charge, the provider and the encounter | High — it conflicts with Quality Gate D2 | Retire them in a dedicated, scheduled item before Gate D is evaluated (section 3) |
| R2 | **Performed time may not precede the item's recorded acceptance**, but the recorded acceptance is when staff entered it, not necessarily when the patient agreed | Medium — can block legitimate back-entry | Keep the rule now; add an explicit acceptance date in a follow-up; disclose (section 4) |
| R3 | **One ambiguous chart match refuses the whole set** | Low — mitigated by selection | Keep; make the review screen name the item and the matches (section 5) |
| R4 | **Whether the new operation should offer a way to adopt a procedure already completed through a legacy path** | Medium — those procedures are otherwise stuck | Defer to the retirement item (section 3.4) |

---

## 2. Facts these points rest on (verified in the code)

- The two legacy routes, `POST api/treatment-plans/{planId}/items/{itemId}/complete` and `POST api/treatment-plans/{planId}/complete-accepted`, write only a completion state row, an `ItemCompleted` history event and an audit entry (`TreatmentPlanService.States.cs`). They record the signed-in user and the time. They record **no provider, no encounter, no performed time, no chart change and no charge**.
- A completed state is final. Under the approved design the new operation refuses any item already completed as `items_not_completable` (`already_completed`), whichever path completed it.
- The new `CompletedProcedures` row is the authoritative record and is **immutable**. Amendments can only change provider, performed time and encounter, and only on a row that exists. A procedure completed through a legacy path has no such row, so it can never be amended, never receive its chart transition through the new operation, and never be auto-charged by it.
- The legacy routes are covered by the course story STORY-008 (portal-verified 2026-10-11 at `8da1119`) and by STORY-007 / ALV-007-C01 tests, which complete an item through them and then charge it by hand. The approved decision D1 keeps them behaviourally unchanged for that regression.
- A plan item's acceptance is stored as an append-only state row with `RecordedAtUtc` (the moment a staff member entered the answer) and the plan revision. There is no separate "date the patient agreed".
- `QUALITY_GATES.md`, **Gate D, criterion D2**, requires: *one or many planned procedures can complete atomically with exact source linkage, odontogram effects and one billable charge per billable completed procedure*, with the threshold **zero orphaned charges; zero missing charges; odontogram state matches**. Gate D is evaluated after item 37 (`ALV-016-C01`).

---

## 3. R1 — Retire the legacy completion paths

### 3.1 The problem

With the approved decision D1 the product has **two ways to complete a procedure**. One is authoritative (provider, encounter, performed time, chart effect, one charge, finalized record). The other is a state-only shortcut that is still reachable through the API and, to satisfy parent UI regression, through a labelled button on the Treatment plan tab.

Consequences while both exist:
1. **Gate D2 cannot be passed honestly.** A completed procedure with no charge is a "missing charge" by the gate's own definition. The gate's threshold is zero. Labelling the button does not change what the API allows.
2. **A clinician can create an irreversible half-state.** Completed is final. A procedure completed through the shortcut can never be completed again, so the new operation can never give it a provider, an encounter, a chart transition or an automatic charge. Only billing's manual charge can bill it.
3. **Two sources of truth for "completed".** The clinical history is derived from completion state rows (so it shows both kinds) and is only *enriched* when an authoritative record exists. Reports and any future claim generation would have to treat the two kinds differently.
4. **Teaching and safety cost.** Every user and every future caller must know which of two buttons or routes is the real one.

### 3.2 Why not retire them inside ALV-008-C01

- The reviewer's approved decision D1 and the updated Item 34 explicitly keep them behaviourally unchanged for the verified STORY-008 / STORY-007 contract. Changing that inside this story reverses an approved decision.
- STORY-008 is a **course story verified by the portal**. Removing or changing its routes changes evidence the course already accepted; the execution plan says that if a required change would invalidate a verified course criterion the work must stop and be explained. Retirement needs an explicit decision about how that verification is treated, which belongs with the owner and reviewer, not inside a feature story.
- STORY-007 and ALV-007-C01 tests complete an item through the legacy route and charge it manually. Retiring the route means rewriting those tests to use the new operation; that is a contract-level change to two approved stories.
- Retirement also needs a way to deal with procedures **already** completed through the legacy routes (3.4). Putting both the new operation and a data-adoption path in one story enlarges its risk and review surface.

### 3.3 Recommended approach: staged retirement

**Stage 1 — inside ALV-008-C01 (additive; does not alter the approved contract).**
- The new review flow is visually primary; the quick-complete button is secondary and labelled "Legacy completion", with a visible sentence that it records no provider, encounter, chart or charge effect (as already approved).
- *Optional, asks the reviewer (R1a):* emit a structured log event `legacy_completion_used` (correlation id, route, outcome; no PHI) when either legacy route succeeds, and add a `Deprecation: true` response header with a pointer to the new route. These are additive, change no response body, status code or stored data, and give the owner evidence of how often the shortcut is used before it is removed. The implementer recommends adding them; the reviewer's D1 says "behaviorally unchanged", so permission is requested rather than assumed.

**Stage 2 — a dedicated item, scheduled before Gate D is evaluated** (suggested id `ALV-008-C02 — Retire legacy completion`, placed after `ALV-008-C01` or at the latest before item 37 closes). Proposed scope:
1. A decision, recorded by the owner and reviewer, on how the course story's evidence is treated when its routes are removed (for example: STORY-008's acceptance remains proven by the new operation's tests, and the course story's tests are migrated to it with a recorded mapping).
2. Migrate the STORY-008, STORY-007, ALV-007-C01 and ALV-015-C01 tests and walkthroughs that call the legacy routes to the new operation (or to a test-only fixture that writes the same state, if a test needs a completed procedure but is not about completion).
3. Remove the legacy routes (or make them return `410 Gone` with a pointer, for one release) and remove the quick-complete button. Make `CompleteItemAsync` and `CompleteAcceptedAsync` internal helpers of the new operation or delete them.
4. The adoption path in 3.4.
5. Evidence: parent regression re-run end to end; a search proving no remaining caller; Gate D2 reconciliation across the whole test dataset (zero missing charges, zero orphaned charges).

### 3.4 R4 — Procedures already completed through a legacy path

Any procedure completed before retirement has a final completion state and no authoritative record. Retirement must not strand them. Proposed: an explicit, audited **"adopt completed procedure"** command that, for an already-completed item, requires provider, encounter and performed time (with the same validation rules as the new operation), creates the immutable `CompletedProcedures` row linked to the existing completion state row, applies the chart effect, and calls `IProcedureCharging` (idempotent, so an existing manual charge is reused, never duplicated). It would live in the retirement item, not in ALV-008-C01. In the current repository this affects development data only; the design must still be correct for real data.

### 3.5 Risks of retirement and how they are limited

| Risk | Limit |
|---|---|
| Course verification is affected | Owner/reviewer decision first (3.3 stage 2, step 1); no routes removed until it is recorded |
| Parent tests are rewritten and lose their original intent | Mapping recorded test-by-test; parent regression re-run before and after |
| Legacy-completed data is stranded | Adoption command (3.4) before the routes are removed |
| An outside caller depends on the routes | `410 Gone` for one release; usage evidence from the optional Stage 1 log event |

### 3.6 The ask

**R1:** Approve scheduling a dedicated retirement item before Gate D is evaluated, with the staged approach above. **R1a (optional):** Permit the additive deprecation signals (log event and header) in ALV-008-C01 without breaking the approved "behaviourally unchanged" contract.

---

## 4. R2 — The acceptance-time rule

**The approved rule:** the performed time may not precede plan creation or the item's acceptance.

**The limitation.** "Acceptance" in the data is the time a staff member *entered* the patient's answer (`RecordedAtUtc`). There is no field for when the patient actually agreed. A realistic case: care is delivered on Tuesday, the dentist records the acceptance on Wednesday while catching up, and tries to complete with Tuesday as the performed time. The completion is refused because Tuesday precedes Wednesday's recorded acceptance. Today there is no way to correct the acceptance's date.

**Why the rule is still useful.** It stops completions dated before the patient could have agreed, which is a clinical-integrity guard and is easy to reason about.

**Options.**
1. *Keep the rule as approved and disclose it (recommended for ALV-008-C01).* The refusal message states the reason and the earliest allowed time; the handoff lists it as a limitation.
2. *Compare only against plan creation, dropping the acceptance check.* Removes the friction but also the guard.
3. *Keep the rule and add an optional "agreed on" date to acceptance (a later story).* Fixes the root cause: the rule then compares against when the patient actually agreed, with the staff-entered time kept as the audit time.
4. *Allow a back-dated performed time only with a mandatory reason.* Flexible, but adds a bypass that needs its own review.

**Recommendation:** option 1 now, option 3 as a follow-up recorded in the plan's later items. **R2:** approve this, or choose another option.

---

## 5. R3 — An ambiguous chart match refuses the whole set

**The approved rule:** more than one eligible finding for a plan item is `ambiguous_chart_finding` and rolls back the entire set.

**The limitation.** A single ambiguous item blocks the other procedures submitted with it.

**Mitigations already available.** The clinician can untick the ambiguous item and complete the others, because selection is a subset. Fixing the diagnosis links resolves the ambiguity for the rest.

**Requested handling.** The review screen must (a) detect the ambiguity before submission using the same rule the server applies, (b) name the affected procedure and list the matching findings, (c) tell the clinician to untick it or correct the diagnosis links, and (d) never submit a set it already knows will be refused. The server remains the authority and still refuses.

**Not now.** Letting the clinician choose among the matching findings on the screen would be useful but changes the contract (the request would carry a finding id) and is left to a later story.

**R3:** approve the screen behaviour and the deferral.

---

## 6. Reviewer response template

```
ALV-008-C01 limitations and legacy retirement — decision: APPROVED / APPROVED WITH CHANGES / CHANGES REQUIRED

R1  Schedule a dedicated legacy-retirement item before Gate D is evaluated (staged approach):
    APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
R1a Permit additive deprecation signals (log event legacy_completion_used and a Deprecation header)
    in ALV-008-C01:                                 APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
R2  Acceptance-time rule: keep now, add an "agreed on" date later:
                                                     APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
R3  Ambiguous chart match: pre-submit detection on the review screen, server refusal unchanged:
                                                     APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___
R4  "Adopt completed procedure" command belongs to the retirement item, not ALV-008-C01:
                                                     APPROVE / APPROVE WITH CHANGE: ___ / REJECT: ___

If R1 is approved: where should the retirement item sit in the plan (after item 34, or later but before item 37 closes)?
Additional requirements or risks the reviewer wants covered: ___
```

---

## 7. What does not change

Everything in the reviewer's approved response stands. ALV-008-C01 will be implemented exactly as approved and packaged at `AWAITING_REVIEW`. Nothing in this document changes the legacy routes' behaviour inside ALV-008-C01, except where R1a is approved.
