# ALV-N002 R03 — Changed Files

Implementation commit `951b4250ff642be111a724a9a167620943a53fa3` (`ALV-N002 R03: fix all four R02 review findings`):

| File | Change |
|---|---|
| `src/Alveara.Api/Program.cs` | Adds static-file serving + SPA fallback for the built React client, gated on the build directory actually existing (N002-R02-01) |
| `src/Alveara.Api/Architecture/BackgroundWork/BackgroundJobRunner.cs` | `EnqueueAsync` handles a unique-constraint violation by re-querying the winning row (N002-R02-03); failure handling records `SafeErrorSummary` (exception type name + correlation token) instead of `ex.Message`, both persisted and logged (N002-R02-02); constructor takes an optional `ILogger<BackgroundJobRunner>?`; doc comment for `IBackgroundJobHandler.ExecuteAsync` no longer suggests unsafe check-marker-then-act for external effects |
| `src/Alveara.Api.Tests/BackgroundJobTests.cs` | New `ThrowsWithMessageJobHandler`, `CapturingLogger<T>`; new tests for concurrent enqueue and PHI-safe logging (persisted + logged) |
| `src/Alveara.Api.Tests/MigrationUpgradeTests.cs` | `FailureTestBrokenMigration.Up()` performs a real column-add before its failing statement; test renamed and extended with a `ColumnExistsAsync` helper to prove partial-rollback (N002-R02-04) |
| `src/Alveara.Api.Tests/StorageIsolationTests.cs` (new) | Proves the blob storage root is not web-reachable via HTTP even with static file serving active, and that the default storage root and client build path are structurally disjoint |
| `.alveara/BUILD_STATE.md` | Records the R03 architecture facts and honestly documents the LAN/service-account/no-internet limitations that remain open |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched (confirmed zero diff — see `PARENT_REGRESSION.md`).
