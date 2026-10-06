# Acceptance evidence - ALV-013-C01 R01 (7 of 7)

The Execution Plan v4 prompt lists seven acceptance items; the status record carried a planned total of six from an earlier plan version, so this attempt records **7 of 7** and the status record is updated to 7.

| # | Acceptance item | Evidence |
|---|---|---|
| 1 | Original STORY-013 tests still pass | The four STORY-013 backend classes (`DiagnosisRulesTests`, `DiagnosisSchemaTests` with its migration class, `DiagnosisServiceTests`, `DiagnosisApiTests`) and the frontend `diagnosisRules.test.ts`, `DiagnosisApi.test.ts` and `Diagnoses.test.tsx` pass in the full runs with **no test file edited**; the **original STORY-013 real-backend walkthrough is unchanged and passes 12 of 12** (`artifacts/parent-regression-walkthrough/`); see `PARENT_REGRESSION.md` |
| 2 | A diagnosis can be stored structurally even when no external code system is configured | `DiagnosisStructureRulesTests` (an entry with no coding is accepted and comes out `Manual`), `DiagnosisStructureServiceTests.A_diagnosis_is_stored_structurally_when_no_coding_system_is_configured...`, `DiagnosisStructureSchemaTests.A_diagnosis_with_no_coding_is_stored...` (database level), `DiagnosisStructureApiTests.Coding_source_and_region_round_trip_over_http_with_no_coding_system_needed`, `DiagnosisStructure.test.tsx` "stays simple with no coding", walkthrough step NO CODING NEEDED |
| 3 | Optional code, system, display and provenance survive a round trip when present | Service test `Coding_source_and_region_survive_a_round_trip_through_the_record_the_read_the_list_and_the_history` and the replay test (the same key and entry replays, a different code under it is refused); API round trip over HTTP (record, read, list, history); the typed client against the fake (`DiagnosisStructureApi.test.ts`); walkthrough step CODING AND PROVENANCE ROUND TRIP (typed on screen, read back through the API and the history). "Display" is the existing diagnosis label (decision 3 in `HANDOFF.md`) |
| 4 | Tooth, region and finding linkage works where applicable | Rules (tooth or region, never both), schema (`CK_Diagnoses_Region`; `DiagnosisLinks` with triggers 51075 and 51076), service (links to a finding and a periodontal chart of the same patient, quiet repeats, who and when), API workflow, the screen (chips; a link picker offering only the patient's own findings and charts), walkthrough step LINKS with real findings and charts. Mutants M3 and M6 killed |
| 5 | A diagnosis is traceable to its encounter and treatment plan through a persisted typed forward reference when a real plan does not yet exist | The patient and encounter links and the forward reference are STORY-013's (parent tests unchanged); this attempt carries the reference, with each step's own actor and time, through amend, resolve, reactivate and withdraw (`DiagnosisStructureServiceTests.An_amendment_carries_the_treatment_plan_reference_and_its_provenance_through_untouched_and_it_stays_unresolved`, the API workflow checking every history step, walkthrough step AMENDMENT). Mutant M10 killed |
| 6 | A treatment-plan forward reference never fabricates a plan, uses an existence lookup, or prevents the diagnosis being truthfully shown as unresolved | No treatment-plan table, API or record exists (`DiagnosisStructureSchemaTests.The_treatment_plan_reference_still_has_no_foreign_key_and_no_treatment_plan_table_exists`; `TreatmentPlan` is not a link type); a request to mark it resolved or validated is refused on record, correct and amend (`DiagnosisStructureServiceTests.A_request_to_mark_the_treatment_plan_reference_resolved_is_refused_on_every_path...`, the API test, the typed client test, walkthrough step TREATMENT PLAN REFERENCE); the screen labels it unresolved and states it proves nothing (`Diagnoses.test.tsx` and `DiagnosisStructure.test.tsx`). Mutant M7 killed |
| 7 | Amendment preserves the prior value and attribution, including forward-reference provenance | `DiagnosisStructureServiceTests.An_amendment_changes_the_structure_keeps_the_prior_values_in_the_history_with_who_when_and_why_and_is_logged`, the API workflow (Recorded, Amended, Resolved, Reactivated, Withdrawn with reasons and actors), the history table on screen with a column for region, coding and source at each step, walkthrough step AMENDMENT (the replaced tooth is still in the first history row). Mutant M4 killed |

## Failure paths from the prompt

| Failure path | Handled and tested by |
|---|---|
| Invalid context link | `link_target_not_found` (404, the same words for another patient's record and a missing one) in the service and API tests, trigger 51076 in `DiagnosisStructureSchemaTests`, the screen's refused-link test, walkthrough LINKS |
| Unknown or unsupported coding system | rules (named refusal), database check `CK_Diagnoses_CodingSystem`, API 400 listing every problem, screen problem list; mutants M1 killed |
| A diagnosis referenced by a current or unresolved treatment-plan reference cannot be destructively deleted | trigger 51071 (STORY-013) and `DiagnosisStructureServiceTests.A_diagnosis_with_a_treatment_plan_reference_or_links_is_never_deleted...`; links are append-only (51075) |
| Attempt to resolve or validate a treatment-plan reference before the domain exists | refused with `treatmentPlanReferenceState:not_supported` on record, correct and amend; mutant M7 |
| Stale edit | the shared 409 concurrency conflict on amend, resolve, reactivate and withdraw; screen keeps what was typed; walkthrough STALE AMENDMENT |
| Attempt to silently replace an authoritative diagnosis used downstream | amendment requires a reason and writes a history entry; a withdrawn diagnosis refuses amend, resolve, reactivate and link (409); mutants M4 and M5 |

## Required tests (from the prompt)

- Original regression - item 1.
- Structured diagnosis serialization and validation tests - `DiagnosisStructureRulesTests` (46), `diagnosisStructureRules.test.ts`, `DiagnosisStructureApi.test.ts`.
- Context-link validation - `DiagnosisStructureSchemaTests`, `DiagnosisStructureServiceTests`, `DiagnosisStructureApiTests`.
- Amendment and history tests - service, API, screen and walkthrough.
- Coding and provenance round-trip tests - item 3.
- Treatment-plan forward-reference persistence, display, audit and history, and no-premature-resolution tests - items 5 and 6, plus the audit tests (events carry the user and time and none of what was diagnosed or coded; walkthrough TRUST).
