# ALV-017-C01 R03 - Changed files

Implementation range `c47ace7..f8d40e9` (2 commits):
- `src/Alveara.Api/Architecture/Permissions/PolicyReportDrain.cs` (new hosted service), `src/Alveara.Api/Program.cs` (one registration).
- `src/Alveara.Api.Tests/TestDatabaseFixture.cs` (3702 diagnostics, retry, test hook), `SchedulingApiHarness.cs` (host stop and wait, `ConfigureServices`), `TestDatabaseInUseTests.cs` (new, 5 tests), `RealMigrationInventory.cs` and `RealMigrationInventoryTests.cs` (the new class listed, group count 4 to 5).
- R03 changes no source: the implementation is the R02 one, `f8d40e9`. Evidence (later commits): `.alveara/handoffs/ALV-017-C01/R03.md`, `R03-evidence/`, BUILD_STATE, EXECUTION_STATUS, PROGRESS.md. The R02 package (`Alveara_Handoff_ALV-017-C01_f8d40e9_R02.zip`, rejected only for its manifest) and `R02.md`/`R02-evidence/` are preserved unchanged.
