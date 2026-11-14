# ALV-N021 pre-implementation briefing review — R02

**Reviewed package:** `Alveara_Briefing_ALV-N021_cb7866b_R02.zip`  
**Package SHA-256:** `856329B8D1F0201D1413877FCCE930A0E0356282F2DABF2E18580D747D09B039`  
**Implementation commit declared by package:** `cb7866b`  
**Decision:** **CHANGES_REQUIRED — do not begin implementation or test execution**  
**Date:** 2026-11-13  
**Review scope:** Independent pre-implementation review under Execution Plan v4.

## R01 correction status

| R01 requirement | Result | Evidence |
|---|---|---|
| Full raw finding evidence | **Met** | `ALV-N021_SOURCE_EVIDENCE_APPENDIX.md` supplies the source hash, headings, primary raw entry, and cross-references for all 50 in-scope rows. This satisfies the binding evidence requirement. |
| Explicit visit workflow | **Partly met** | The normal path, state/action labels, role table, server-side available-actions result, day boundary, correction paths, and proof plan are substantially improved. The final completion rule remains contradictory, below. |
| Owner decisions recorded | **Not independently verified** | The package labels several new product decisions “owner-confirmed,” but provides no owner approval record. The agent’s statement is not sufficient evidence of an owner decision. |
| Focused-first test policy | **Met** | No run occurred. The briefing correctly defers broad regression to ALV-N028 and requires permission before a chain run over five minutes. |

## Blocking correction 1 — one final-completion owner, with no bypass

The briefing says the normal sequence ends with **“Ready for checkout - awaiting front desk”** and says that front desk performs the administrative close. However, sections 5.3, 5.5, 5.6, 6.1 and 8 add `CompleteOwnStartedVisit`, giving a dentist who started treatment a second direct `CheckedOut → Completed` route.

That is not the reviewed workflow. It restores two competing owners of the final step, makes “awaiting front desk” untrue for some visits, and risks repeating the role/dead-end confusion reported in F-065.

**Required change:** remove `CompleteOwnStartedVisit` from this story: no new permission, default grant, table row, API authorization branch, test, migration, policy-health row, or UI action for it. The only ordinary `CheckedOut → Completed` action is the administrative close by `UpdateVisitFlow` holders (FrontDesk, OfficeManager, Admin), subject to the same server-side policy. A provider ends treatment; a front-office user completes the visit. Any future exception needs a separately approved story and matrix.

## Blocking correction 2 — do not treat proposed features as owner-approved scope

The R02 briefing introduces and treats as decided several changes that were not part of the R01 ruling:

- configurable required/optional Confirm and Mark-ready settings;
- a new provider-reassignment permission, associated room reassignment behavior, two urgent notifications, and a notification migration;
- a cancellation-category migration and reporting change; and
- deferred stories ALV-N030 and ALV-N031.

These may be worthwhile, but the package cannot establish owner authorization by calling them “owner-confirmed.” Keep them explicitly **proposed** until the product owner approves each decision. In particular, `ALV-N030` and `ALV-N031` are not authorized, must not be added to the execution plan or status ledger, and must not be implemented as part of ALV-N021.

For the resubmitted ALV-N021 briefing, either:

1. attach a clear owner decision record for each expanded item and state its exact acceptance criteria; or
2. remove the item from ALV-N021 and leave it as a separately proposed deferred enhancement.

The following visit-flow decisions are already the reviewer’s recommended baseline and may be presented to the owner for confirmation:

- `Scheduled → Confirmed (optional only if explicitly approved) → Checked in → Ready → Seated → In treatment → Ready for checkout → Completed`;
- no CheckedIn-to-InTreatment, CheckedIn-to-Completed, or InTreatment-to-Completed shortcut;
- front office performs check-in, correction actions and the sole final administrative completion;
- the booked provider starts and ends treatment; another clinician requires a server-validated reassignment before treatment begins;
- undo check-in and correct no-show require a reason and add immutable events; and
- ordinary check-in is restricted to the appointment’s practice-local date.

## Blocking correction 3 — resolve the remaining policy ambiguity before PKG-05 work

The state table must not contain open implementation choices while calling the policy final. Revise it to state one rule for each item below, with the explicit owner record or with that item removed from ALV-N021:

1. Whether Confirm and Mark ready are fixed required steps or configurable practice settings. If settings remain, define their persistence, authorization, audit, initial default, rollout effect, and all state/action combinations without adding an unapproved product feature by implication.
2. Whether creating blocked time over an existing appointment is refused or is allowed only after a specifically authorized confirmation. The raw finding requires affected appointments to be listed and visibly flagged; a generic conflict check is not enough.
3. Whether provider/room reassignment, urgency notifications, and reportable cancellation categories are part of this attempt. If yes, each needs the owner’s direct approval and its full migration/integration acceptance contract.
4. The exact pre-no-show state restored by correction and how the system prevents a correction from reviving a now-conflicting or no-longer-valid slot.

## Approval path

After these corrections, split implementation under this one story into two sequential reviewed attempts:

1. **PKG-04 date/time integrity** first, as the briefing already identifies it as independent; then
2. **PKG-05 scheduling and visit workflow** only after the owner-decision record is present and the final transition table is internally consistent.

This sequencing contains the larger workflow risk and preserves the focused-first test policy. It does **not** authorize a chain run or broad/full regression.
