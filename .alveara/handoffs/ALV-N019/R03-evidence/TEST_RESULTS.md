# ALV-N019 R03: test results (Stage 1, focused)

Tree: the R03 implementation commit `56b05c0` content (checks ran with the pin in the working tree, then committed unchanged). Raw logs: `artifacts/`. No long run was made; the accepted R02 Stage 2 evidence is not repeated here.

| # | Command | Result |
|---|---|---|
| 1 | `node --test tests/stabilization/dependency-pin.test.mjs` | exit 0; **4 of 4 passed** |
| 2 | **Negative control:** pin set back to 9.0.18, same test | exit 1; 3 passed, **1 failed** (the guard catches the old pin) |
| 3 | `dotnet restore` of the Api project with the old pin, then `dotnet list package --vulnerable --include-transitive` | reports `System.Security.Cryptography.Pkcs 9.0.18`: High (GHSA-846f-f764-76mx) and Moderate (GHSA-2fmv-rv2c-59x7); NU1902 and NU1903 warn. The csproj was restored byte for byte (SHA-256 `9CA6AA1C...67520E` before and after). |
| 4 | `dotnet restore` of the test project (with the pin) | exit 0 |
| 5 | `dotnet nuget why Alveara.Api.csproj System.Security.Cryptography.Pkcs` | **9.0.21 on every path** (direct, via `Microsoft.Data.SqlClient`, via `Microsoft.EntityFrameworkCore.SqlServer`) |
| 6 | `dotnet list package --vulnerable --include-transitive` for `Alveara.Api`, `Alveara.Api.Tests`, `Alveara.Api.ChainHost` | exit 0; **"has no vulnerable packages"** for all three |
| 7 | `dotnet build` (test project, which builds the Api and the chain host) | exit 0 (53 s); the NU1902 and NU1903 warnings no longer print |
| 8 | `dotnet test --filter` `ChainClockSeamTests`, `BackupRecoveryHardeningTests`, `PatientMergeHostileInputTests`, `AccountServiceBootstrapTests` | exit 0; **109 passed, 0 failed, 0 skipped** (6 m 30 s) |
| 9 | `scripts/Invoke-AccumulatedChain.ps1 -SkipBuild -ClockMode injected` | exit 0; nine PASS, `clockMode: injected` (run `aba04970`) |
| 10 | `scripts/Invoke-AccumulatedChain.ps1 -SkipBuild -ClockMode real` | exit 0; nine PASS, `clockMode: real` (run `edda4362`) |

## Stage 2 contract (prepared, not run)
`R03-evidence/stage2/validate_evaluator.py`: **19 of 19** scenarios behave as stated (`evaluator-validation.txt`); `verify_runner_outputs.py`: every walkthrough output goes to the runner folder (`runner-proof.txt`). These run no test suite.

## Not run, stated plainly
The full backend suite, vitest, the mocked Playwright suite, the five walkthroughs and the repository checks: they belong to the new frozen Stage 2, which needs the product owner's explicit approval after Stage 1 approval. Leak counts were not taken (no broad run). The R02 evidence is untouched.
