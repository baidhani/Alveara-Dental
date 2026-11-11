# ALV-N019 R03: acceptance evidence

R03 changes one acceptance statement's basis: **item 56's statement 4** ("dependency advisories and environment hygiene are investigated before any upgrade; upgrades require their own reviewed decision"). The reviewed decision exists (PD-26 = update, the R02 Stage 2 review, the scope ruling) and the upgrade is made. The other four statements are unchanged from R02 (`R02-evidence/ACCEPTANCE_EVIDENCE.md`). **Counted: 5 of 5** (unchanged), with the communication-stubs point the Stage 1 review already accepted.

| Requirement of the R02 Stage 2 review (R03 procedure) | Status | Evidence |
|---|---|---|
| 1. State the exact chosen update or pin, why it is the narrowest compatible remediation, and every project and lock-file change | **MET** | `R03.md` (the exact 2-line diff in `Alveara.Api.csproj`, the version rationale; no other project; the repository has no lock file); `CHANGED_FILES.md` |
| 2. Prove the restore graph resolves a non-vulnerable version and `dotnet list package --vulnerable --include-transitive` no longer reports F-001 | **MET** | `dotnet nuget why` shows 9.0.21 on every path; the vulnerability list reports nothing for `Alveara.Api`, `Alveara.Api.Tests` and `Alveara.Api.ChainHost`; the control with the old pin reports it (`artifacts/advisory/`, `artifacts/control/`) |
| 3. Rebuild and run focused API and chain coverage; update the advisory disposition to `fixed` only when proven; retain the advisory evidence | **MET** | build exit 0; 109 of 109 focused backend tests; injected and real chain runs, nine PASS each; the advisory report says `fixed` with the proof; the R02 evidence is untouched |
| 4. No unrelated npm or dev-dependency update | **MET** | the diff has one package reference; no `package.json` or lock file touched |

| Scope-ruling item | Status | Evidence |
|---|---|---|
| Focused R03 Stage 1 handoff with the exact package-reference diff and no unrelated package movement | **MET** | this package |
| restore, `dotnet list package --vulnerable` for the three projects, and `dotnet nuget why` for every relevant path | **MET** | `artifacts/advisory/` |
| Focused API and injected-chain coverage; disposition `fixed` only if the raw results prove it; R02 evidence preserved | **MET** | `TEST_RESULTS.md`; `git diff 068d454 56b05c0` touches no R01 or R02 evidence file |
| One new frozen Stage 2 on the unchanged R03 snapshot after approval, reusing the R02 shape with fresh results; retain the R02 test-identity baseline and account for any test-count change | **PREPARED, NOT RUN** | `stage2/` (baseline = the R02 `backend-tests.csv`, 5,697 ids; expected new ids: none; the one new test is a repository-level guard outside both counts); the run needs the product owner's explicit approval |

Negative control: the pin guard fails and the scanner reports F-001 again when the pin is reverted (`TEST_RESULTS.md`, rows 2 and 3).
