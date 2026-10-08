# ALV-N005 R01 - Changed files

Implementation commit `4d4cb0ee1dc05c28b15f5a36f3bd15a5ac690826`: 33 files, 7,796 insertions, 1 deletion (4,540 of the insertions are the generated migration designer).

## Backend - new
- `src/Alveara.Api/Architecture/Procedures/ProcedureCatalog.cs` - entities (`ProcedureDefinition`, `ProcedureVersion`, `ProcedureEvent`) and the code-system, category, scope, dentition and change-type constants.
- `.../ProcedureRules.cs` - `ProcedureCatalogException`, the input and field records, every validation rule and limit.
- `.../ProcedureCatalogService.cs` - create, revise, inactivate, reactivate (idempotency, race handling, audit, events).
- `.../ProcedureCatalogService.Queries.cs` - list, get, history, usage, active-for-planning, snapshot by date, snapshot by version id.
- `.../ProcedureUsage.cs` - `IProcedureUsageSource` and the first source (odontogram procedure links).
- `.../ProcedureViews.cs` - response records.
- `src/Alveara.Api/Controllers/ProceduresController.cs` - the 11 endpoints, permissions, CSRF, error mapping.
- `src/Alveara.Api/Data/ProcedureModel.cs` - model configuration, indexes, check constraints, trigger registration.
- `src/Alveara.Api/Migrations/20261008050255_AddProcedureCatalog.cs` (+ `.Designer.cs`) - tables, constraints, triggers 51077-51082.

## Backend - modified
- `src/Alveara.Api/Data/AlveraDbContext.cs` - three `DbSet`s and `ProcedureModel.Configure`.
- `src/Alveara.Api/Program.cs` - registers the usage source and the service.
- `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` - additions only.

## Backend tests - new (84)
`ProcedureTestBase.cs`, `ProcedureCatalogRulesTests.cs` (25), `ProcedureCatalogLifecycleTests.cs` (17), `ProcedureCatalogSchemaTests.cs` (21), `ProcedureCatalogApiTests.cs` (21), all in `src/Alveara.Api.Tests/`.

## Frontend - new
- `src/alveara-client/src/services/proceduresApi.ts` - typed client.
- `src/alveara-client/src/pages/ProcedureCatalogPage.tsx` (+ `.css`) - the page.
- `src/alveara-client/src/pages/procedures/ProcedureForm.tsx`, `ProcedureRow.tsx`, `procedureText.ts`.
- `src/alveara-client/src/pages/ProcedureCatalogPage.test.tsx` - 20 tests.
- `src/alveara-client/e2e/procedures-real-backend.spec.ts` and `playwright.procedures.config.ts` - the real-backend walkthrough (6 tests).

## Frontend - modified
- `src/alveara-client/src/App.tsx` - the `/procedures` route (`ViewBilling`).
- `src/alveara-client/src/app/moduleRegistry.ts` - the "Procedures & fees" navigation entry.
- `src/alveara-client/playwright.config.ts` - excludes the new real-backend spec from the mocked run.

## Documentation and records
- `docs/PROCEDURE_CATALOG.md` (new) - design, behaviour, permissions, audit, failure-first notes, assumptions.
- `docs/testing/REAL_BACKEND_E2E.md` - the new walkthrough's section.
- `PROGRESS.md` - the ALV-N005 entry.

## Evidence commit (separate; this attempt's second commit)
`.alveara/handoffs/ALV-N005/R01.md` and `R01-evidence/`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json` (ALV-N005 to `AWAITING_REVIEW`), `.alveara/QUALITY_GATES.md` (note on C6), `docs/MILESTONES_2026-10-07.md` (status log).

## Not changed
`.alveara/HANDOFF_SCHEMA.md`, the runner configuration (`xunit.runner.json`, serial), any existing migration, any existing service, `.colaberry/*`, the root Command Center.
