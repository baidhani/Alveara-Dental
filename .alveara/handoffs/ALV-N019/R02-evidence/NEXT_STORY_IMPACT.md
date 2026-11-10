# ALV-N019 R02: next-story impact

Next scheduled item: ALV-N020 (item 57, R05 PKG-02 + PKG-03). Release engineering (ALV-N013, ALV-N014, ALV-N008) stays locked. Everything in R01's `NEXT_STORY_IMPACT.md` still holds; this adds what R02 changed.

## What later stories can rely on
- **The chain now has an injected clock.** `scripts/Invoke-AccumulatedChain.ps1` starts `Alveara.Api.ChainHost` by default. A stage that needs fixed or advancing time sets `CHAIN_CLOCK_UTC` (ISO instant) for the start; `ChainPracticeClock.Advance` exists for in-process use (tests); no HTTP route advances it yet (add one to `ChainHost` only, never to production, when a stage needs it, and cover it by a test like `ChainClockSeamTests`).
- **Only `IPracticeClock` is injected.** Authentication expiry, retry and back-off use real time. A package that needs those controlled needs its own pre-implementation review (O-1).
- **Evidence carries the measured clock mode.** Every record has `clockMode` read from the running host, and `clock-mode.json` holds the host's report. `-ClockMode real` runs the production API for comparison.
- **The E10 invariant** (`scripts/chain/E10Invariant.mjs`) is a reusable "this operation changed nothing" check on a snapshot of the chain database; a later stage can snapshot before and after another operation and reuse it.
- **The runner owns what it creates**: it refuses an existing database or run folder, drops and deletes only its own, labels a dirty tree, and has a `-InjectFault` hook for a self-test. Adding a fault (for example a kill at another point) means adding a value to that parameter and a test.
- **Mode detection**: a host is the chain host only if `/api/chain/clock-mode` returns JSON with a `mode`; production answers unknown GETs with its client page (HTML) or a 404.

## Interfaces and conventions changed
- New project `src/Alveara.Api.ChainHost` (in `Alveara.slnx`, referenced by the test project). It is test infrastructure and must never be referenced by `Alveara.Api`; `tests/stabilization/production-composition.test.mjs` enforces that.
- `ChainClockSeamTests` and `PatientMergeHostileInputTests` are new backend classes; the next baseline for the backend test ids must include them (6 and 67 tests).
- PowerShell 5.1 reminders from R01 still apply (case-insensitive `-match` and `-ne`, `-Encoding utf8` writes a BOM, the repository path contains `[ ]`). New: `powershell -Command` does not pass a script's exit code on by itself; end the command with `exit $LASTEXITCODE`, and handle a parameter-validation failure separately (no script exit code exists).

## Unresolved decisions that affect later items
- **PD-26 / F-001**: still open; the report in `docs/design/ALV-N019_DEPENDENCY_ADVISORY_REPORT.md` gives the evidence for either choice. Release engineering (ALV-N013) should record the minimum ASP.NET Core runtime patch level (10.0.13 or later).
- **The "AD" monogram** in the sidebar mark is the old initials; a product decision.
- **PD-38** (first-run administrator setup and database creation by the API): not authorised; unchanged.
- **`auth-real-backend` intermittency**: passed in every R02 sweep run; the R01 observations stand. ALV-N020 touches sign-in and the account area and should decide whether to fix the test or the page.
- **R05 6.5 vs PKG-15**: F-015 belongs to PKG-02 (ALV-N020), not PKG-15.

## Prompt changes relevant to the next item
None required by this story.
