# ALV-N005 R02 - Acceptance evidence

The five acceptance / stop-condition items of Execution Plan item 27. R01's mapping (`../R01-evidence/ACCEPTANCE_EVIDENCE.md`) was accepted by the review for every item; this attempt re-proves all five on the corrected tree and strengthens item 1, which is the item the finding concerned.

| # | Acceptance item | Evidence on the R02 tree | Status |
|---|---|---|---|
| 1 | A procedure with stable internal identity, **code/system/source**, description, fee, category and applicability can be created | The R01 tests (all still passing): `A_procedure_is_created_with_identity_code_system_description_category_applicability_and_fee`, `A_CDT_code_keeps_its_source_and_edition_and_a_local_code_has_none`, `An_external_code_set_must_name_its_source`, `The_same_code_in_two_code_systems_is_two_procedures`, `The_code_and_code_system_of_an_existing_procedure_cannot_be_changed`, trigger 51078. **New in R02, the provenance contract for every non-local code:** `Every_non_local_code_system_must_name_its_source...` (CDT and External, missing and blank), `A_local_code_accepts_neither_a_source_name_nor_an_edition`, `The_source_edition_is_optional_for_a_CDT_or_external_code_but_the_source_name_is_not`, `A_new_version_of_a_CDT_code_cannot_drop_its_source`; at the database `The_database_refuses_a_version_whose_source_does_not_fit_its_code_system` (triggers 51083 and 51084), `The_database_accepts_...`, `The_database_refuses_a_blank_source_name_or_edition`; at the API `A_CDT_or_external_procedure_without_a_source_is_refused...`; on screen the unit test for CDT and the browser step `a CDT code must name the licensed source it came from` (`07-cdt-needs-source.png`, `08-cdt-added.png`) | **Met** |
| 2 | Inactivation preserves historical references | Unchanged and re-run: `A_finding_linked_to_the_procedure_counts_as_usage_and_inactivating_confirms_then_leaves_the_link_alone`, `An_unreferenced_procedure_is_inactivated...`, `A_procedure_is_reactivated_with_its_history_intact`, `A_procedure_cannot_be_deleted` (51077); browser inactivate/reactivate | **Met** |
| 3 | Fee changes do not silently rewrite existing plan/charge history | Unchanged and re-run: immutable versions (51079), `A_fee_change_is_a_new_version_and_the_old_version_keeps_its_fee`, the fee-on-a-date and version-id snapshot tests, no backdating, stale edits change nothing. **Still proven at the catalog only:** no plan or charge exists yet to consume a snapshot (the review accepted this as appropriate for now) | **Met at the catalog** |
| 4 | The treatment-planning API can query active procedures | Unchanged and re-run: `Planning_offers_only_what_fits_the_tooth_and_surface_and_dentition`, the scheduled/inactive exclusions, the API filter tests, the browser's `/api/procedures/active` checks | **Met** |
| 5 | Later STORY-014 acceptance can pass without architectural rework | Unchanged and re-run: code/description/fee; an incorrect entry prompts correction (now including a missing source, named beside its field); every change audited with user id and time | **Met** |

## Review finding resolution
See `../R02.md`, "Finding ALV-N005-R01-01 - what was required and what was done" (the four required corrections, each mapped to its evidence), and `artifacts/test-runs/negative-controls.txt` (the new tests fail when either the service rule or the database trigger is removed).

## Required tests (from the prompt)
| Required test | Where (R02 additions in bold) |
|---|---|
| Catalog validation | `ProcedureCatalogRulesTests` (30), `ProcedureCatalogSchemaTests` (33), `ProcedureCatalogApiTests` (23), `ProcedureCatalogPage.test.tsx` (21) |
| Code-system/source/version/provenance test | The R01 provenance section plus **the R02 tests for CDT/External without a source, local with a source or an edition, the optional edition, dropping a source on a new version, the database triggers and the blank-text constraint** |
| Historical fee/version test | `ProcedureCatalogLifecycleTests` fee history (8) and the schema tests for 51079, 51081, 51082 |
| Reference/inactivation test | `ProcedureCatalogLifecycleTests` inactivate/reactivate (6, including the real odontogram link) |

Counts: acceptance items 5; met 5 (item 3 at the catalog, as accepted by the review).
