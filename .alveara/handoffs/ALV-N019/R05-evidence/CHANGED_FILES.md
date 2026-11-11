# ALV-N019 R05: changed files

Implementation commit `c94ca0b` (parent `289eaad`). Product code, package and project files: none.

- `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs` (2 lines): register `FixtureForcedPruningTests` as infrastructure; group the class lookup by name.
- `src/Alveara.Api.Tests/LeastPrivilegeLoginCleanupTests.cs` (the injected wait polls until the server session is gone, bounded 10 s).
- `src/alveara-client/e2e/architecture-map.spec.ts` (1 line: wait for the map tab before reading the tab order).
- `PROGRESS.md` (the session entry).

The exact diff is `artifacts/R05_changes.diff`. Evidence and control files are committed separately.
