# ALV-N019 R02: changed files

`git diff --name-status 6b083cd 54806a3` (R01 implementation commit to R02 implementation commit), leaving out the control files and the R01 handoff, review and status records that were committed in between: 18 files. A = added, M = modified. Nothing deleted or renamed. `src/Alveara.Api` (the production project) has **no** change in this range, and `Program.cs` is untouched.

Outside this list: `4570651` (control data: ten stabilization stories added to `.alveara/story_catalog.json`, 50 lines), `b16591f` (the R01 review closure, control files only), and the R02 handoff and status commits that record this attempt.

## Chain host (src/Alveara.Api.ChainHost, new project) (5)
- A `src/Alveara.Api.ChainHost/Alveara.Api.ChainHost.csproj`
- A `src/Alveara.Api.ChainHost/ChainHostFactory.cs`
- A `src/Alveara.Api.ChainHost/ChainHostMain.cs`
- A `src/Alveara.Api.ChainHost/ChainPracticeClock.cs`
- A `src/Alveara.Api.ChainHost/ClockModeEndpoint.cs`

## Backend tests (src/Alveara.Api.Tests) (3)
- M `src/Alveara.Api.Tests/Alveara.Api.Tests.csproj`
- A `src/Alveara.Api.Tests/ChainClockSeamTests.cs`
- A `src/Alveara.Api.Tests/PatientMergeHostileInputTests.cs`

## Solution file (1)
- M `src/Alveara.slnx`

## Client browser spec (chain) (1)
- M `src/alveara-client/chain/stage-0.spec.ts`

## Scripts (runner, safety gate, E10 invariant) (3)
- M `scripts/Invoke-AccumulatedChain.ps1`
- M `scripts/chain/ChainSafety.ps1`
- A `scripts/chain/E10Invariant.mjs`

## Repository tests (3)
- A `tests/stabilization/chain-runner-resilience.test.mjs`
- A `tests/stabilization/e10-invariant.test.mjs`
- A `tests/stabilization/production-composition.test.mjs`

## Design records (advisory report) (1)
- M `docs/design/ALV-N019_DEPENDENCY_ADVISORY_REPORT.md`

## Progress log (1)
- M `PROGRESS.md`
