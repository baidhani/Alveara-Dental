# ALV-N005 R02 - Changed files

Implementation commit `0ef1ab491ea56aa935a52a4d55fc5c6535860b6e`: 12 files, 4,744 insertions, 4 deletions (4,544 of the insertions are the generated migration designer). The R01 files (`.alveara/handoffs/ALV-N005/R01.md`, `R01-evidence/`, the R01 ZIP) and the review decision (`.alveara/reviews/ALV-N005/R01.md`) are untouched.

## Backend - changed
- `src/Alveara.Api/Architecture/Procedures/ProcedureRules.cs` - **the correction**: a CDT procedure must name its source, like External (two added lines).
- `src/Alveara.Api/Data/ProcedureModel.cs` - registers trigger `TR_ProcedureVersions_Provenance` and adds check constraint `CK_ProcedureVersions_SourceText`.
- `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` - additions only.

## Backend - new
- `src/Alveara.Api/Migrations/20261008144126_AddProcedureProvenanceRule.cs` (+ `.Designer.cs`) - the check constraint and the trigger (51083, 51084); `Down` drops both.

## Backend tests - changed (19 new tests, 1 existing test corrected)
- `ProcedureCatalogRulesTests.cs` (+5), `ProcedureCatalogSchemaTests.cs` (+12, and the External duplicate-code test now supplies a source), `ProcedureCatalogApiTests.cs` (+2).

## Frontend - changed
- `src/alveara-client/src/pages/ProcedureCatalogPage.test.tsx` (+1 test).
- `src/alveara-client/e2e/procedures-real-backend.spec.ts` (+1 test: the CDT step).

## Documentation and records
- `docs/PROCEDURE_CATALOG.md` - the provenance rule, the new triggers, assumption 7 (edition optional).
- `PROGRESS.md` - the R02 entry.

## Evidence commit (separate; this attempt's second commit)
`.alveara/handoffs/ALV-N005/R02.md` and `R02-evidence/`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json` (ALV-N005 back to `AWAITING_REVIEW`, attempt R02), `docs/MILESTONES_2026-10-07.md` (status log).

## Not changed
`.alveara/HANDOFF_SCHEMA.md`, the runner configuration (serial), the R01 migration, any other existing migration or service, `.colaberry/*`, the root Command Center, any frontend page or service file (the page already required a source for every non-local code system).
