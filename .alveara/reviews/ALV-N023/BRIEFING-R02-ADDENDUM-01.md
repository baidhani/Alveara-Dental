# ALV-N023 — briefing R02 addendum 01 independent review

**Review scope:** `docs/design/ALV-N023_ADDENDUM_01.md`, against the already approved ALV-N023 briefing revision 2 and its review at `.alveara/reviews/ALV-N023/BRIEFING-R02.md`.

**Decision:** **APPROVED — implementation may resume.**

## What this approval changes

The addendum is an authoritative amendment to the R02 briefing. It resolves three state/action and audit ambiguities that correctly stopped Slice 2 work.

1. An empty plan (all items withdrawn, no completed care) remains truthfully labelled **Closed — no procedures remain**, but is correctable. It may add a procedure, rename with a reason, withdraw eligible items, and withdraw the plan.
2. An all-declined plan with no completed care remains truthfully labelled **Closed — all procedures declined**, but is correctable. Its answer may change; accepting an item returns the derived state to **In progress**. It may use the applicable open-plan actions, including withdrawal.
3. A plan that contains completed care remains locked. `Completed`, and `Closed` plans containing completed care, allow only history/activity, revision history, patient copy, and the warned **Revise anyway** path. They do not permit ordinary withdrawal.
4. Audit detail for an answer must remain PHI-free: it must not contain the answering patient/representative's name or an entity identifier. The existing staff actor remains recorded. The actual named respondent remains only in the authorized treatment-plan answer/history record. Because source category is not a structured field today, this implementation may state only that the respondent is recorded on the plan; it must not guess a category from free text.

## Required implementation interpretation

The amendment supersedes the contrary generalizations in the R02 briefing, notably S9, C3, section 4.6 rows 2 and 4, and the corresponding state/audit proof rows. The server must derive both the displayed state **and** allowed-action flags from whether the plan contains completed care; the client must render those server-supplied flags rather than infer that every `Closed` plan is read-only.

Tests must cover all of the following:

- empty/no-completed-care: labelled Closed but add/rename/withdraw remains available;
- all-declined/no-completed-care: labelled Closed but answer correction and withdrawal remain available, with re-acceptance becoming In progress;
- any completed-care plan: ordinary withdrawal remains refused and the warned revision route remains the correction path;
- audit text excludes the respondent name and identifier while authorized answer history preserves the entered name.

## Test and handoff boundary

Resume implementation from the paused Slice 2 state. Do not run a broad/full regression or any package-level Stage 2 run for this amendment. ALV-N023 remains on the approved `DEFERRED_TO_ALV-N028` focused-proof route; every focused command/group must remain within the existing five-minute policy unless the owner grants prior permission.

