# ALV-015-C01 R02 - Changed files

Implementation commit `3fbb79852c39a0712ecdcfed1917c687ecf3526a` (13 files, +344/-6):

| File | Change |
|---|---|
| `src/Alveara.Api/Architecture/Treatment/TreatmentPlanProcedureUsageSource.cs` | new: read-only usage source "Treatment plans" |
| `src/Alveara.Api/Program.cs` | registers the source beside the odontogram one (1 line) |
| `src/Alveara.Api.Tests/TreatmentPlanProcedureUsageTests.cs` | new: 5 service tests |
| `src/Alveara.Api.Tests/TreatmentPlanProcedureUsageApiTests.cs` | new: 4 API tests with production wiring |
| `src/Alveara.Api.Tests/TreatmentPlanTestBase.cs` | `CatalogFor` builds the catalog with both sources |
| `src/Alveara.Api.Tests/TreatmentPlanServiceTests.cs` | one STORY-015 test acknowledges the use when inactivating |
| `src/Alveara.Api.Tests/TreatmentPlanRevisionServiceTests.cs` | one analyzer warning fixed (`Assert.Single`) |
| `src/alveara-client/src/pages/ProcedureCatalogPage.test.tsx` | +1 test |
| `src/alveara-client/e2e/treatment-plans-complete-real-backend.spec.ts` | +1 walkthrough test |
| `docs/PROCEDURE_CATALOG.md`, `docs/TREATMENT_PLAN_COMPLETION.md`, `docs/testing/REAL_BACKEND_E2E.md` | documented |
| `PROGRESS.md` | R02 entry |

Shared components of completed stories touched: `Program.cs` (one registration line) and one STORY-015 test file edit. No migration, no schema, no frontend production code.

The records commits that follow add this handoff and evidence (`.alveara/handoffs/ALV-015-C01/R02*`), `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json` and `PROGRESS.md`.
