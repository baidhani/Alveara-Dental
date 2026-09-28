# ALV-N002 R04 — Changed Files

Implementation commit `b0ff9a4348231603f432a4f596f97763b8de23a8` (`ALV-N002 R04: fix publish bundling and framework PHI-safe logging`):

| File | Change |
|---|---|
| `src/Alveara.Api/Alveara.Api.csproj` | New `BuildClientForPublish`/`CopyClientBuildToPublishOutput` MSBuild targets: build the client and copy its `dist/` into `$(PublishDir)wwwroot` as part of `dotnet publish` (N002-R03-01) |
| `src/Alveara.Api/Program.cs` | Default client static-file path now resolves to `wwwroot` under the content root (matching real publish output), falling back to the repo-layout `dist/` for ordinary development; new `SafeExceptionHandlingMiddleware` registered first in the pipeline (N002-R03-02) |
| `src/Alveara.Api/Architecture/Logging/SafeExceptionHandlingMiddleware.cs` (new) | Catches any unhandled request exception, logs only its type name and a correlation id, never `Message`/`ToString()` |
| `src/Alveara.Api/appsettings.json`, `appsettings.Development.json` | Suppress `Microsoft.EntityFrameworkCore`, `.Database.Command`, `.Update`, and `Microsoft.AspNetCore.Diagnostics` logging categories, which previously let raw SQL exception text (including literal constraint-violation values) reach ordinary logs |
| `src/Alveara.Api.Tests/FrameworkLoggingPhiSafetyTests.cs` (new) | Proves a synthetic patient-like value never reaches any captured log record, across any category, through the real configured logging pipeline, during a genuine duplicate-key violation |
| `src/Alveara.Api.Tests/StorageIsolationTests.cs` | Updated the structural default-path test to match `Program.cs`'s new `wwwroot`-based default |
| `.alveara/BUILD_STATE.md` | Records the R04 facts (publish bundling fixed, framework logging fixed; LAN/ACL infrastructure still open) |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched (confirmed zero diff — see `PARENT_REGRESSION.md`).
