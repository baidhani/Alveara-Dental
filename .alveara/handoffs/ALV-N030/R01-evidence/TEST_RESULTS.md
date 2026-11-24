# ALV-N030 R01: test results (Stage 1, focused proof)

All results below were obtained during the build, on the commit named in each row, and are reported as they were. No run was made for the handoff itself (owner, 2026-11-24: "No further evidence run is needed before Stage 1"). **No broad regression and no accumulated-chain run is claimed**: `fullRegression: DEFERRED_TO_ALV-N028`, not release-certified.

## Backend (xUnit, focused groups)

| # | Commit (slice) | Group | Result | Time |
|---|---|---|---|---|
| 1 | `50d8ef9` (slice 1) | AccountBootstrapMigrationTests + ExportDomainCatalogTests + PatientOwnedDataTests + RealMigrationInventoryTests | 23/23 | 35 s |
| 2 | `f09919d` (slice 2) | DeploymentSetupServiceTests + DeploymentSetupApiTests | 9/9. The first run was 8/9: the removed routes answer 405 when a client build exists (GET-only SPA fallback); the test now accepts 404 or 405 and checks that no account was created. | 26 s |
| 3 | `6824f84` (slice 3) | PasswordChangeGateTests + DeploymentSetup* | 15/15 | 36 s |
| 4 | `a3ba1e9` (slice 4) | StaffMemberAndOwnerTests + PasswordChangeGateTests + DeploymentSetup* + StaffProviderServiceTests + ExportDomainCatalogTests + PatientOwnedDataTests | 58/58. The first run was 57/58 (a test bug: it seeded a second Deployment Administrator, which the database rightly refused). | 48 s |
| 5 | `4109925` (5a) | TestSupportGuardTests + ChainClockAdvance/ChainClockSeam/ChainHostClockParse/TimeCultureGuard + slice 2-4 groups | 103/104. The failure was pre-existing **U-037**, later fixed (row 8). | 1 m 5 s |
| 6 | `3cdb338` (5b, **owner-approved run over 5 minutes**) | The 37 directly edited classes plus a sample of the shared-base users, in 5 groups: A auth+identity, B staff+config+scheduling, C API domain, D backup, E sample | A 104/112, B 208/208, C 166/166, D 68/69, E 98/98. In A, 6 LoginPresentationApiTests failures were a fixture gap (the reply lacked `presentationAdministrator`/`notice`); fixed, 7/7. The remaining 3 failures were proven pre-existing at `4e05be3` in a scratch worktree: **U-038** (2) and **U-039** (1). | about 16 min |
| 7 | `3cdb338` | LoginPresentationApiTests after the fixture fix | 7/7 | 26 s |
| 8 | `906ddee` (U-037, U-039) | TimeCultureGuardTests; then treatment-plan copy, state and link-merge, clearances, unscheduled treatment, prescription PDF text, culture guard, backup coverage | 24/24; 141/141 | 2 m 18 s |
| 9 | `2d81752` (U-038) | AccountSafetyTests + RoleSubsetInvariantTests + VisitWorkflowPermissionTests + PermissionMatrixTests + CollectPaymentsApiTests + EffectivePermissionServiceTests + PermissionPolicyStoreTests + AuthControllerPermissionMatrixTests | 100/100 | 1 m 1 s |
| 10 | `c867544` (5c) | TestSupportGuardTests (G1-G8 incl. the staff-profile route) | 39/39 | 45 s |
| 11 | `0418dc6` (5c) | TestSupportGuardTests + ChainClock* + ChainHostClockParse | 59/59 | 38 s |
| 12 | `c5f86af` (slice 7), first run | StartupModeTests + DeploymentSetup* + TestSupportGuardTests + AccountSafetyTests + LoginPresentationApiTests + StaffMemberAndOwnerTests + PasswordChangeGateTests | 100/106. **The run took 8 m 8 s against a 3-4 minute estimate**; it ran in the background and could not be stopped at a safe point. All 6 failures had one cause: the deployment guard refused `/api/setup` in Setup mode. The guard was fixed and ChainHost's shutdown crash was fixed (rows 13-15). | 8 m 8 s |
| 13 | `c5f86af` | StartupModeTests + DeploymentSetupApiTests | 10/10. The first rerun was 9/10: S20 exited with 0xE0434352 (a shutdown crash in ChainHost), fixed. | 1 m 32 s |
| 14 | `c5f86af` | StartupModeTests.S20 (2 tests, real processes) | 2/2 | 36 s |
| 15 | `c5f86af` | RestartLoopScriptTests (S22) + TestSupportGuardTests | 43/43 | 2 m 42 s |

### Last run versus later changes (read this before relying on a row)
- **S12 and S15 last ran before slice 7.** Rows 1 and 4 (the migration tests for 0, 1 and 2+ profile-less administrators, and the export catalog and patient-owned registry) were not re-run after slice 7. Slice 7 changed `TestDatabaseFixture`: every test database now gets a setup-completed `BootstrapStates` row.
  - The migration data step does not read `BootstrapStates`, so no change is expected.
  - **This is reasoned, not proven.**
- **Indirect users of the shared bases are deferred.** These are the users of the changed shared test bases (`SchedulingTestSupport`, `SafetyTestBase`, `CompletionTestBase`, `SchedulingApiHarness`, `TestDatabaseFixture`, `TestAccounts`). Beyond the samples above, they are deferred to ALV-N028's broad regression and recorded in the regression ledger.
- **`44545eb`, the final implementation commit, changes only the Deployment Administrator notice text and the S14 check for it, and was not run** (owner instruction). The vitest notice test compares against the same constant.

## Client (vitest, tsc, lint)

| Commit | Check | Result |
|---|---|---|
| `62ebdda` (slice 6) | `tsc -p tsconfig.app.json --noEmit` | clean |
| `62ebdda` | oxlint | no new warnings in the changed files |
| `62ebdda` | AccountBootstrap.test.tsx | 17/17 |
| `62ebdda` | full vitest | 2176/2177 (200 s). The failure is the known flaky **U-018** (BillingStatementsPanel). |
| `c5f86af` (slice 7) | AccountBootstrap.test.tsx | 23/23 |
| `c5f86af` | full vitest | 2181/2183 (205 s). The 2 failures are the two tests named in **U-018**. |
| `c5f86af` | tsc; oxlint | clean; the one new warning in StartupGate was fixed |
| `c867544` (5c) | `tsc` over every changed browser spec | no new errors. The 2 errors in untouched helper lines are identical at HEAD. |

## Real-backend walkthroughs
See `DEMO_EVIDENCE.md`: the 5c sample with its substitutions, the slice 6 auth and S14 runs, and the slice 7 S14/S21 runs.
