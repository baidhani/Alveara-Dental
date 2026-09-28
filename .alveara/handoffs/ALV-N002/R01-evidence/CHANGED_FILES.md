# ALV-N002 R01 — Changed Files

## Implementation commit (`7d312b47f9c451ff142e189f8430589658cd353f`)

| Group | Files | Why |
|---|---|---|
| Persistence | `src/Alveara.Api/Data/AlveraDbContext.cs`, `src/Alveara.Api/Migrations/*` | The single EF Core DbContext and initial schema migration. |
| Time | `src/Alveara.Api/Architecture/Time/PracticeClock.cs` | Practice-timezone ↔ UTC conversion with explicit DST handling. |
| Money | `src/Alveara.Api/Architecture/Money/Money.cs` | Decimal-backed, banker's-rounding money type. |
| Identity | `src/Alveara.Api/Architecture/Identity/IdentityEntities.cs` | `UserAccount`/`StaffProfile`/`ProviderProfile` schema-only separation. |
| Storage | `src/Alveara.Api/Architecture/Storage/IBlobStorage.cs` | Local-disk blob storage seam with integrity verification. |
| Backup | `src/Alveara.Api/Architecture/Backup/IBackupSnapshotProvider.cs` | Real `BACKUP DATABASE` seam for `ALV-N004`. |
| Background work | `src/Alveara.Api/Architecture/BackgroundWork/*` | Durable job queue/runner/heartbeat/hosted-service. |
| Measurement | `src/Alveara.Api/Architecture/Measurement/*` | Versioned, allow-listed measurement events. |
| Logging | `src/Alveara.Api/Architecture/Logging/PhiSafeLog.cs` | PHI-safe correlation-token convention. |
| API | `src/Alveara.Api/Controllers/SystemStatusController.cs`, `src/Alveara.Api/Program.cs` | System Status endpoint + DI wiring for everything above. |
| Client | `src/alveara-client/src/hooks/useSystemStatus.ts`, `src/alveara-client/src/pages/SystemStatusPage.{tsx,css,test.tsx}`, `src/alveara-client/e2e/system-status.spec.ts` | System Status page + its tests. |
| Client (additive) | `src/alveara-client/src/App.tsx`, `src/alveara-client/src/app/moduleRegistry.ts` | New route + nav entry only — no existing route/entry modified. |
| Tests | `src/Alveara.Api.Tests/*.cs` (11 new files) | All required-test coverage — see `TEST_RESULTS.md`. |
| Topology check | `tests/no-direct-db-access.test.mjs` | Repo-root direct-database-access topology check. |
| Project files | `src/Alveara.Api/Alveara.Api.csproj`, `src/Alveara.Api.Tests/Alveara.Api.Tests.csproj` | New NuGet package references (EF Core SqlServer/Design, Microsoft.Data.SqlClient). |

## Evidence commit (this commit, created after implementation)

| File | Why |
|---|---|
| `.alveara/handoffs/ALV-N002/R01.md` | Attempt-level handoff. |
| `.alveara/handoffs/ALV-N002/R01-evidence/*` | This attempt's evidence. |
| `.alveara/BUILD_STATE.md` | Updated with the new durable architecture facts. |
| `.alveara/EXECUTION_STATUS.json` | `ALV-N002` record moved to `AWAITING_REVIEW`. |

No `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` file was touched by either commit.
