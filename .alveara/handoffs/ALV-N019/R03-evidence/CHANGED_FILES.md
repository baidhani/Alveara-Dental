# ALV-N019 R03: changed files

`git diff --name-status 068d454 56b05c0` (the Stage 2 review closure to the R03 implementation commit): 3 files.

## Product project (1)
- M `src/Alveara.Api/Alveara.Api.csproj` (+2 lines: a comment and `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="9.0.21" />`; nothing else)

## Repository tests (1)
- A `tests/stabilization/dependency-pin.test.mjs` (4 tests; outside the backend count and the frozen `tests/*.test.mjs` glob)

## Design records (1)
- M `docs/design/ALV-N019_DEPENDENCY_ADVISORY_REPORT.md` (F-001 moved to `fixed`, with the change and the proof)

Not changed: every other project, `Program.cs`, any source file, any existing test, any script, any lock or `package.json` file, and all R01 and R02 handoff, evidence and review files. Outside this list: the R03 handoff and evidence files and the frozen R03 Stage 2 contract (`.alveara/handoffs/ALV-N019/R03*`), added by the evidence commit.
