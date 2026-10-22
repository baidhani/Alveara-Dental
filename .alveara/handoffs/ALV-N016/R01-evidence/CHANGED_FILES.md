# Changed files - ALV-N016 R01

Implementation commit `3ae51e23adbd7769a0bafb71fd16731b1eee3466`; the story's commits are `7c6b40f`, `10ba376` and `3ae51e2`. Files changed from `3cabd4b` (the commit that recorded the reviewer's approval of the plan): 27; **none under `src/Alveara.Api/` (no production code)** and `xunit.runner.json` is unchanged. `A` = added, `M` = modified.

## New test infrastructure and its tests (6)
- `A` `src/Alveara.Api.Tests/RealMigrationInventory.cs`
- `A` `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs`
- `A` `src/Alveara.Api.Tests/RealMigrationTestDatabaseFixture.cs`
- `A` `src/Alveara.Api.Tests/RealMigrationsAttribute.cs`
- `A` `src/Alveara.Api.Tests/TestDatabaseTemplate.cs`
- `A` `src/Alveara.Api.Tests/TestDatabaseTemplateTests.cs`

## Modified shared test infrastructure (2)
- `M` `src/Alveara.Api.Tests/SafetyTestBase.cs`
- `M` `src/Alveara.Api.Tests/TestDatabaseFixture.cs`

## Test classes put on the real-migration path (the inventory) (18)
- `M` `src/Alveara.Api.Tests/BillingRefundMigrationTests.cs`
- `M` `src/Alveara.Api.Tests/CompletedProcedureMigrationTests.cs`
- `M` `src/Alveara.Api.Tests/CompletionAdoptionSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/CompletionChartEvidenceTests.cs`
- `M` `src/Alveara.Api.Tests/DiagnosisSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/DiagnosisStructureSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/DocumentLibrarySchemaTests.cs`
- `M` `src/Alveara.Api.Tests/DocumentSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/MigrationTests.cs`
- `M` `src/Alveara.Api.Tests/MigrationUpgradeTests.cs`
- `M` `src/Alveara.Api.Tests/OdontogramCatalogueSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/PerioMigrationTests.cs`
- `M` `src/Alveara.Api.Tests/PrescriptionSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/PrescriptionWorkflowSchemaTests.cs`
- `M` `src/Alveara.Api.Tests/TestDatabaseLocationTests.cs`
- `M` `src/Alveara.Api.Tests/TestDatabaseTeardownTests.cs`
- `M` `src/Alveara.Api.Tests/TestDatabaseTransientRetryTests.cs`
- `M` `src/Alveara.Api.Tests/TreatmentPlanRevisionMigrationTests.cs`

## Repository records (1)
- `M` `PROGRESS.md`
