# ALV-N019 R04: changed files

Implementation commit `115821d9d9c1632a82e4131602472e46a0e215c2` (parent `c98157a`): 108 files, 6 added, 102 modified. The exact list with status letters is `artifacts/changed-files-name-status.txt`.

- **Added (6):** `scripts/Invoke-FixturePreflight.ps1`; `src/Alveara.Api.Tests/` `FixtureForcedPruningTests.cs`, `FixtureOwnerCleanupTests.cs`, `FixtureOwnerGuardTests.cs`, `SqlClientPoolPruner.cs`, `TestDatabaseFixture.Retry.cs`.
- **Modified, substantive (2 + PROGRESS):** `TestDatabaseFixture.cs`, `SafetyTestBase.cs`, `PROGRESS.md`.
- **Modified, mechanical owner conversion (99):** one `InitializeAsync` shape rewritten per owner class (see `R04.md`).
- **Not changed:** `src/Alveara.Api` (any file), any `.csproj` or package reference, `src/alveara-client`, `tests/`, the 9.0.21 Pkcs pin, configuration.

Control and evidence files are committed separately: `.alveara/` records, `R04.md`, `R04-evidence/`, the reviews.
