# ALV-N005 R02 - Next-story impact

Unchanged from `../R01-evidence/NEXT_STORY_IMPACT.md` (interfaces, assumptions, UI extension points, unresolved decisions), with these corrections and additions from attempt R02:

- **Provenance contract.** Any module or import that creates or revises a procedure must supply a non-blank **source name** for `CDT` and `External` codes (the edition is optional) and must supply neither for `Local` codes. The service refuses otherwise with `400 validation_failed` and `fieldErrors.sourceName`; the database refuses it too, so a script or a future licensed code-set import cannot store a CDT or external version without a source. A future licensed CDT import should create or revise procedures through `ProcedureCatalogService` with the licensed source name and the edition it loads; a new edition is a new version of the same procedure.
- **Schema.** Triggers 51077-51084 are used by the catalog; **51085 and up are free** (R01 said 51083 and up). The catalog migrations are `AddProcedureCatalog` and `AddProcedureProvenanceRule`.
- **Assumption added:** the source edition stays optional for non-local codes.
- **Prompt changes relevant to the next item.** None for `STORY-015`. The note from R01 stands: when the treatment-planning story is written, its prompt should name the snapshot / version-id contract so that "fee changes do not rewrite existing plan history" can be proven end to end.
