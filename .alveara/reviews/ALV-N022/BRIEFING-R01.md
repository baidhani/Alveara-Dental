# ALV-N022 — Pre-implementation Briefing Review R01

**Package:** `Alveara_Briefing_ALV-N022_0a19afc_R01.zip`  
**Package SHA-256:** `969965653A75863FA13B3B8197BF985B22683AB35CA5AB5A4FF2AB92078FA907`  
**Documents reviewed:** finding survey, decision request, owner decision record revision 1, scope matrix revision 1, and pre-implementation briefing revision 1.  
**Decision:** **CHANGES_REQUIRED** — briefing only; no implementation is authorized.

## What is sound and retained

- The survey correctly identifies the chart/encounter split as the root cause of the stale safety presentation and duplicate/visibility defects.
- The A1/A2/A3 sequence is sensible and keeps the high-impact record change independently reviewable.
- Preserving legacy encounter rows rather than silently backfilling them as current chart facts is the right default.
- Deferring F-105's allergy-class feature, any external drug API, and any drug-interaction/dose/contraindication decision logic is consistent with the owner direction.
- The proposed finalized-encounter boundary, focused proof plan, deferred broad regression, and explicit non-semantic handling of missing-tooth display are sound directions.

## Required revisions for briefing R02

### BR-1 — Amend the authoritative plan and R05 disposition before treating F-105 as deferred

The current Execution Plan item 59 still makes the allergy-class check part of ALV-N022's scope and requires PD-12 before dependent behavior. R05 likewise lists F-105 as an implementation acceptance group. The briefing alone cannot silently override either authority.

For R02, update the Execution Plan and the active R05 stabilization/disposition material to state precisely:

- F-105 / PD-12 is **DEFERRED, not removed**;
- ALV-N022 retains only the direct recorded-name warning and must never present no match as a safety/compatibility/approval verdict;
- no drug-class seed, external drug API, interaction, contraindication, dose, compatibility, or prescribing-decision functionality is authorized in ALV-N022; and
- the deferred follow-on is the provider-facing Medication Reference and Provider Lookup proposal, which requires its own approved design and may not send patient identity or chart context externally.

The updated master-plan wording must preserve the deferred item as a release limitation, rather than allowing the story to claim the allergy-class feature was delivered.

### BR-2 — Correct decision authority and remove unapproved behavior from implementation scope

The decision record marks the owner approval as selected and describes OD-01 through OD-08 as the owner's answers. The owner has directed the F-105 deferral, but has not independently approved every listed implementation detail or the proposed items.

For R02:

- mark only decisions expressly supplied by the owner as **DECIDED**;
- retain OD-10 through OD-15 as **PROPOSED / not built**;
- remove the signed-draft behavior from A2's implementation and focused-test contract unless OD-13 receives an explicit owner decision; and
- do not characterize a proposed decision as owner approval merely because it appears in a draft record.

The briefing may be reviewed and approved with owner decisions that are clearly framed as product-record integrity decisions. It must not claim a clinical sign-off that does not exist.

### BR-3 — Keep A1 within the owner-approved data scope

The owner direction covers allergies, medications, and medical history. The draft expands that change to **dental history** without a separate decision or demonstrated root-cause need.

Either:

1. remove dental history from A1, preserving its existing model and behavior; or
2. add a separate, evidence-backed decision showing why it must move now, its exact migration/reader impact, and its acceptance tests.

Do not extend the shared-model observation into unapproved production scope.

### BR-4 — Limit normalized duplicate enforcement to allergies

OD-03 authorizes refusal of an exact normalized **active allergy** duplicate. Section 4.1 instead says the normalization function is used on “every chart write,” which could unexpectedly reject legitimate repeated medication, medical-history, or dental-history entries.

For R02, make the rule exact:

- normalize names consistently where display/search needs it;
- enforce the new active-duplicate refusal only for allergy items; and
- leave duplicate semantics for other chart categories unchanged unless separately decided and proven.

Add the corresponding category-boundary tests to A1.

### BR-5 — Make legacy “Add to chart” an explicit confirmation, not an implicit assertion of a current active fact

The no-backfill rule is correct. However, the proposed one-click action would create a current chart item from a historical legacy encounter entry. It must not silently assert that an old item remains active.

For R02, specify that the action opens a confirmation/add flow showing the historical text and encounter date, requires the clinician to select/confirm the current status and submit it as a new chart action, and preserves the link to the historical entry. It must never automatically copy the legacy entry, default it to active without confirmation, or change the legacy entry.

### BR-6 — Make Step 0 and the scope matrix truthful gates, not pre-committed implementation claims

Many Group B findings in the survey remain `CONFIRMED (register)`, meaning they are reported from manual testing but not yet verified in code. The scope matrix calls them “Implemented,” and the Step 0 catch-all list (`F-081, F-086, F-089, F-067...`) is not an adequate item-by-item gate.

For R02:

- rename the matrix column/status from **Implemented** to **Planned for implementation** until each item is verified;
- enumerate every register-only finding and question that is proposed for A1/A2/A3, including F-095 and the scope of F-090/F-098 pickers; and
- require the attempt to stop and return a briefing addendum if a verification is `NOT_REPRODUCED`, belongs to a different package, or would require a new rule/schema/permission/API beyond this briefing.

This prevents a manual observation from silently turning into a different feature during implementation.

## R02 package and test boundary

R02 is documentation and planning only. It must contain the revised decision record, scope matrix, briefing, and the authoritative-plan/R05 updates. Do not modify product code, migration files, tests, configuration, or external integrations. Do not run focused, browser, chain, broad, or full regression tests for this briefing revision.

## Status

ALV-N022 remains in planning. No implementation attempt, migration, external medication lookup, allergy-class logic, or long regression run is authorized until R02 is independently approved.
