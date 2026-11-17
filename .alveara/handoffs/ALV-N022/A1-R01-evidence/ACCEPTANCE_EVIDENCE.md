# ALV-N022 A1-R01: acceptance evidence

Proof per row of the approved briefing section 10 (A1), per Addendum 02 outcome and per owner decision in A1's scope. R = `EncounterChartRoutingTests`, A = `EncounterChartApiTests`, S = `EncounterServiceTests`, CRS = `ClinicalRecordServiceTests`, P = `ChartSectionPanel.test.tsx`, N = `safetyApi.withSafetyNotice.test.ts`, G = `tests/safety-notice-guard.test.mjs`, W = `encounter-chart-real-backend.spec.ts`. All on implementation `6414ffa`.

The story's five plan acceptance items are not all in A1's scope (A2: finalized-encounter completion; A3: display findings, F-106, PD-09d). The record keeps `acceptance.passed` at 0 until the story closes.

## Briefing section 10, A1 server row

| Item | Proof | Status |
|---|---|---|
| Routing: encounter add, change, remove reach the chart with `EncounterId` for Allergy, Medication, MedicalHistory | R `Allergy_medication_and_medical_history_go_to_the_chart...` (every version has the encounter id; no EncounterEntry for them); R medication update; R removal; control C1, B2 | PROVEN |
| DentalHistory still writes `EncounterEntry` (boundary) | same R test; rewritten S entry-mechanism tests on dental | PROVEN |
| Allergy: case and inner-whitespace canonicalization of new input, case kept | R `A_new_allergy_name_is_trimmed...`, R `Inner_spaces_make_two_allergies_one_name...` | PROVEN |
| Active match refused with direction to update; nothing written | R `Adding_an_allergy_that_is_active...` (version count, encounter version and item unchanged); A duplicate body | PROVEN |
| Resolved/Inactive match refused with reactivate/update direction and its status; nothing written | R `Adding_an_allergy_that_is_resolved...` | PROVEN (Resolved; Inactive uses the same branch, not separately tested) |
| Reactivation through the explicit action requires a chosen status and appends one version with provenance | same R test (missing status -> `validation_failed`; StatusChanged recorded here); W step 4 | PROVEN |
| Cancel leaves the item unchanged | R active-match test (cancel = no further call); P `cancel closes the notice and changes nothing`; W step 3 (API read after Cancel) | PROVEN |
| Retried replay | R `Adding_the_same_allergy_with_the_same_values_again_is_a_quiet_repeat` | PROVEN |
| Concurrent double add leaves one record and the loser gets the existing-record direction | R `Two_simultaneous_chart_adds...` (loser `existingItem` = winner); R `Six_simultaneous_adds...` (encounter path: one record, one history row) | PROVEN |
| Historical rows unchanged | R finalized-provenance test (append-only refusal on update); R legacy test (legacy entry and mark kept) | PROVEN |
| Medication/medical-history match: no second item; confirm records the review with provenance; update appends a version | R `A_medication_already_on_the_chart_is_confirmed_current...`, R `..._is_updated_from_the_encounter...` | PROVEN (medication; medical history shares the code path, its boundary is in R inner-spaces) |
| Category boundary (BR-4): medication and history duplicates as before | R `Inner_spaces...` (spacing variants stay two items); control B3 caught | PROVEN |
| None reported: refused with an active item, allowed with only resolved items | S `On_the_chart_adding_an_item_withdraws...`; CRS `Marking_a_section_none_known_needs_no_active_item...`; A/W clinical spec (409 `section_has_active_items`); control C3 | PROVEN |
| Contradiction derivation: legacy mark and chart NoneKnown | R legacy test (mark made with an active item); R `This_encounters_none_reported_is_flagged...` (item added later); R `A_chart_none_known_standing_beside_an_active_item...` (and no flag without a none statement) | PROVEN |
| Provenance: a finalized encounter unchanged after a later chart change | R `A_finalized_encounter_keeps_what_it_recorded...`; S finalized test | PROVEN |
| Append-only refusal | same R test (update of a version row throws) | PROVEN |
| Auth matrix | A (401, 7 roles, CSRF on both new routes); existing EncountersApiTests role tests (rewritten) | PROVEN |
| M1 up and down, with counts and checksums | M1 not needed (Step 0 section 5) | NOT APPLICABLE |

## Briefing section 10, A1 client row

| Item | Proof | Status |
|---|---|---|
| Sections render chart, provenance and legacy items | P (chart list, "recorded in this encounter"); W; clinical spec | PROVEN (legacy rendering by server test + component code; no client test with legacy entries) |
| Duplicate 409 links to the existing item; Active: update; Resolved/Inactive: reactivate/update, no status preselected; cancel changes nothing | P (4 tests); W steps 3-4; control C8 | PROVEN |
| Encounter confirm-current/update for a medication or history match | P `for a medication already listed also offers to confirm...`; clinical spec (confirm current) | PROVEN |
| One banner | safety spec (no encounter strip; header banner above the sections); W | PROVEN |
| Refetch after each write path | N (announce after save only); G (all 12 mutations wrapped, no bypass; control C7); W (banner changes without reload after add, status change, reactivation) | PROVEN |
| Guard test | G | PROVEN |

## Briefing section 10, A1 browser row

| Item | Proof | Status |
|---|---|---|
| Allergy in an encounter, banner and Safety tab update without reload, duplicate refused | W steps 1-5 (Safety tab reached in-app, no page load) | PROVEN |

## Briefing negative controls (A1)

| Control | Result |
|---|---|
| Encounter-local duplicate check only | SURVIVED (B1): the unique index enforces it; disclosed |
| `EncounterId` dropped from the version | CAUGHT (B2) |
| Allergy rule applied to medications | CAUGHT (B3) |

## Owner decisions in A1 scope

| Decision | Proof |
|---|---|
| OD-01/02 chart is the single source; encounter actions update it with immutable provenance | routing, provenance rows above |
| OD-03 / Addendum 02 duplicate refusal, normalization, match outcomes | allergy rows above |
| OD-04 none reported only without an active item; contradictions flagged, never erased | none-reported and contradiction rows |
| OD-05 one banner refreshed after every write | client rows |
| Reuse `encounter_not_open` / `encounter_patient_mismatch` | R `An_item_of_another_patient...` (mismatch); trigger path maps to `encounter_not_open` (code; existing finalized refusals in S return `encounter_finalized` before the chart is reached) |
| Deployment allergy-collision preflight | NOT RUN (before deploy; see handoff) |
| F-105 allergy class, PD-09d, OD-10..15, A2, A3 | out of A1 scope; nothing built |
