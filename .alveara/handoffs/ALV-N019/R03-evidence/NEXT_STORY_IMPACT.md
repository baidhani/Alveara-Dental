# ALV-N019 R03: next-story impact

Next scheduled item: ALV-N020 (item 57), not started and not authorized until ALV-N019 is `COMPLETE`.

- **Dependency graph:** the API now resolves `System.Security.Cryptography.Pkcs` 9.0.21 through a direct reference. A later package update of `Microsoft.Data.SqlClient` or the EF Core SQL Server provider should drop the pin only deliberately, once those packages themselves require a fixed Pkcs; `tests/stabilization/dependency-pin.test.mjs` fails if the pin is removed or lowered, as a prompt to check the scanner first.
- **Release engineering (ALV-N013):** still to record the minimum ASP.NET Core 10.0 runtime patch level (10.0.13 or later) for deployments, because the application output uses the framework's own Pkcs copy.
- **Test baseline for the next story:** the R02 Stage 2 `backend-tests.csv` (5,697 ids) stays the baseline; R03 adds no backend id. The new R03 Stage 2 run (after approval) produces the next baseline file.
- Everything else in the R01 and R02 next-story-impact notes still holds.
