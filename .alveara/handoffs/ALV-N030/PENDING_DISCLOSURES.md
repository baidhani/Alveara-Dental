# ALV-N030: disclosures for the handoff (working list)

Collected while the slices are built so that nothing is lost before the handoff is written. Every item below must appear in the ALV-N030 handoff. Session CC-20261123-q4vn.

## Owner decisions applied during implementation
- **U-038, default-role change (owner, 2026-11-24).** `CollectPayments` is added to the default **Office Manager** permissions (`PermissionMatrix.cs`).
  - **Why:** ALV-N021 A2 (`ce6e9da`, OD-27) gave it to Front Desk only. That was an unintended default-role gap: the Office Manager stopped covering Front Desk, so the ALV-N017 target rules refused an Office Manager acting on a standard Front Desk account. `AccountSafetyTests` was red from then on (proven at `4e05be3`).
  - **What the Office Manager can do:** it could already take and apply payments through `ManageBilling` (`CollectPaymentsApiTests`), so its practical payment ability is unchanged. What changes is the default list, and with it account management of Front Desk.
- **Role-subset invariant (owner rule).** `default Front Desk permissions ⊆ default Office Manager permissions`, guarded by `RoleSubsetInvariantTests`.
  - There is no "Office Manager marker" permission.
  - The account-management safety rule stays based on effective permissions. If a practice grants Front Desk a permission the Office Manager lacks, those accounts stay protected (test `A_front_desk_role_given_a_permission_the_office_manager_lacks_stays_protected`).
  - The Office Manager can never hold the Dentist/Hygienist-only `CompleteOwnStartedVisit`, so a Dentist account stays protected (test `The_office_manager_can_never_hold_the_dentist_only_permission_so_a_dentist_account_stays_protected`).
  - The old Dentist case asked for an impossible grant; that test now targets a Hygienist.
- **Interpretation (owner, confirmed 2026-11-24).** "A custom Front Desk account" means a practice customising the Front Desk ROLE's permissions (policy overrides), which is what the tests use. The target rules compare role effective permissions.
- **Out of scope for ALV-N030 (owner).** Per-account grants (`AccountPermissionGrants`, today only `ManageLoginPresentation`) are not part of that comparison, and this story does not change that.

## Deviations from the brief (approved set R04 + R06 + R07)
- **Identity call (slice 3).** The new identity fields and the forced-change exemption are on `GET /api/auth/permissions`, not on `whoami`. `whoami` is Administrator-only, and the client's identity call is `/permissions`. The exempt set is still exactly five actions.
- **G3 (slice 5a).** G3 reads MSBuild's publish file list (`ComputeResolvedFilesToPublishList`) instead of running a full `dotnet publish`. A full publish of Alveara.Api runs `npm ci` and `npm run build` (csproj lines 49-59).
- **Section 8 superseded by section 17 (R07).** Test support is hosted on ChainHost; no product start-up split and no TestHost executable.

- **R07 implementation extension (owner-approved 2026-11-24).** A second fixture route exists: `POST /api/test-support/staff-profiles` with `{displayName, jobTitle?, userAccountId?, legacyUnlinked?}`.
  - **Why:** 33 browser specs created a staff profile LINKED to a login the spec had already made, through the removed profile-only create. The brief's plan ("Add staff member") cannot link an existing login.
  - **Protections:** it lives only in `Alveara.Api.TestSupport` (internal controller, explicit feature provider) behind the same three conditions; G1, G4 and the OpenAPI negative proof cover it, and G2/G3 cover its assembly.
  - **Rules:** with `userAccountId`, the account must exist and have no profile, and the profile and link are written in one save. Without one, `legacyUnlinked: true` is required, for legacy/remediation fixtures only.
  - **Not a product path:** the 6 specs that made an UNLINKED provider profile now use the real "Add staff member" (`POST /api/config/staff-members`).
- **Chain stage 0 rewritten (5c).** S-09a/b/c were about the bootstrap route itself. They now prove that sign-up and bootstrap answer 404/405 and create nothing, that the fixture creates exactly one administrator, and that a duplicate is refused with 409. C-01 expects a `real` clock report in real-clock mode, because real-clock runs now use ChainHost too.
- **Runners (5c).**
  - Both start ChainHost in both clock modes, with `ASPNETCORE_ENVIRONMENT=Test`, `TestSupport__Enabled=true`, `CHAIN_URL=http://127.0.0.1:5072` and an explicit `CHAIN_CLOCK_MODE`.
  - `ASPNETCORE_URLS` is removed, and no bootstrap secret is set.
  - The walkthrough runner now also verifies a `real` clock report in real mode.
- **Vite dev proxy (5c).** The target changed from `http://localhost:5072` to `http://127.0.0.1:5072`. ChainHost listens on IPv4 loopback only, and Node may resolve `localhost` to `::1`. The dev API (Kestrel `localhost`) answers on both, so dev use is unaffected.
- **`auth-real-backend.spec.ts` (5c).** It proves sign-up and bootstrap are gone and adds the forced password change end to end through the real API (Add staff member, temporary password, gate, change). The browser proof of the setup PAGE is slice 6 (S14, with the page).
- **Known, pre-existing.** `tsc` reports two type errors in specs, in helper lines ALV-N030 did not touch: `procedures-real-backend.spec.ts` (api helper) and `auth-real-backend.spec.ts` (TOTP helper). They are identical in the committed versions; Playwright does not type-check.
- **For slice 7.** Databases set up by the fixture routes have no Deployment Administrator and no `BootstrapState`. Slice 7's setup mode must not put walkthrough databases into setup mode (decide there: fixture-created administrators mark setup complete, or ChainHost under Test is exempt).

- **5c walkthrough sample (owner-approved 2026-11-24), each run alone on the new ChainHost path.**
  - **Elapsed times:** auth 33 s; permission policy 37 s; reports 44 s; billing 34 s; prescriptions 43 s; visit board 1 min 17 s. The first auth run took 1 min 12 s with the build.
  - **Auth:** 8 relevant tests passed: sign-up and bootstrap gone, the fixture administrator, the forced password change, MFA, security administration, the permission matrix, and the audit log after U-040. **The remaining 5 auth tests are blocked by the obsolete "Add staff profile" client path (slice 6).** Slice 6 must replace it with "Add staff member" and then rerun the COMPLETE auth walkthrough. The other walkthroughs need no rerun unless slice 6 touches them (owner).
  - **Permission policy:** 7/7, the permission-management path.
  - **Reports:** 7/7, the staff-creation path through the real "Add staff member".
  - **Billing:** the fixture and setup steps passed. Its later assertion is blocked by pre-existing **U-041**, left unassigned (owner); no billing rerun is required for ALV-N030.
  - **Prescriptions:** 10/10, replacing billing as the complete operational proof for a staff profile linked to an existing login.
  - **Visit board:** 16/16 with the injected clock, the second operational role flow and the injected-clock runner path.
  - **Guards:** ChainHost and runner guards plus the existing ChainHost clock tests, 59/59.
  - **Out of the sample:** chain stage 0 and the accumulated chain were not run; they go to ALV-N028 (owner).
- **5c corrections found by the sample.**
  - My first spec rewrite missed 21 linked staff creates whose bodies held template literals; they are converted with brace counting, and no removed-route call remains.
  - Three specs (completion, patient flow, visit board) make one linked and one UNLINKED provider in a loop. The unlinked one now uses the real "Add staff member", as the owner's rule requires; the legacy flag is used nowhere.
- **U-040 (fixed in ALV-N030, selector only).** Since ALV-N025 slice 1 added the audit event-type filter, the auth walkthrough's bare text checks for `SessionTimeoutChanged` and `LoginSucceeded` also matched the filter's options. Both now target the table cell.

- **Slice 6 (client).**
  - **Proof:** the complete auth walkthrough passes 13/13, which closes the 5c boundary. S14 passes 6/6. Evidence is in `6-walkthrough-evidence/`.
  - **For owner approval:** the Deployment Administrator's dashboard notice uses the brief's wording verbatim, which the brief marks "owner wording to approve".
  - **No link from the sign-in page:** the sign-in page does not link to `/setup`. The installer prints the setup page address, and `/setup` itself says when setup is already complete.
  - **Voluntary use:** `/change-password` also serves a voluntary change by anyone signed in, not only the forced one.
  - **Owner box:** "Make this person the Practice Owner" calls `POST /api/practice-owner/initial` after the create, as a separate audited action (brief section 4). If that call fails, the person stays added and a warning says so.
  - **Temporary password:** it is held only in page memory and never stored in the browser. "Copy" uses the clipboard; a copy failure is shown, not swallowed.
  - **Idempotency keys:** "Add staff member" uses `staff-member:<username>`, so a retried submission is a replay. The legacy login uses `legacy-login:<staffId>:<username>`.
  - **U-042 (fixed, selector only):** a pre-existing auth-walkthrough check from ALV-N026 S3.
  - **Unit suite:** 2176/2177 pass; the one failure is the known flaky U-018.

- **Slice 7 (section 16: setup mode and the controlled restart).**
  - **As the brief says:** the mode is decided before `builder.Build()` by `StartupModeProbe`.
    - Settings: a 15 s connection and command timeout, and up to 3 retries with 1/2/4 s backoff on connection-level failures only. A missing database or a refused login is answered NotInstalled at once. It never migrates.
    - Outside Normal mode, the nine application background services are not registered, and `StartupModeMiddleware` answers 503 `{error}` on `/api` except what the mode allows.
    - The status route returns exactly the four 16.2a bodies. A successful setup registers `Response.OnCompleted`, which leads to exit code 75 and a graceful stop.
    - Each start logs `startup_mode`, without the connection string. EF's design-time tools skip the probe (`EF.IsDesignTime`).
  - **Product change not named in the brief:** `DeploymentGuardMiddleware` now exempts `/api/setup`, as it exempts `/api/auth`, `/api/health` and backup.
    - **Why:** in Setup mode the invariant monitor, a background service, is not registered, so the guard refused the setup routes the mode allows.
    - **Why it is safe:** the setup routes read and write nothing that depends on the deployment settings.
  - **Restart seam:** `ISetupRestart` (the product's `ProcessSetupRestart`) is a DI seam. Backend tests replace it with a recorder so that a successful setup neither stops the test host nor sets the test run's exit code. ChainHost keeps the real one.
  - **Status within one process:** the status route answers from the process's mode. In the same process, after a successful setup, it still says "setup" until the restart; the restarted process says "normal".
  - **R07 retargeting (17.6):**
    - S20 runs a real ChainHost process. ChainHost now stops when the application stops and returns `Environment.ExitCode`.
    - A crash during shutdown (a disposed cancellation source cancelled by the process-exit handler) used to replace the code. It was found by S20 and fixed.
    - The guard test checks Program.cs's `app.Run()` shape and ChainHost's `return Environment.ExitCode`.
  - **Test data, not product:**
    - Every backend test database is marked as set up (a `BootstrapStates` row, `TestDatabaseFixture`), so ordinary tests run in Normal mode. Setup tests call `ClearSetupCompletedAsync`.
    - `TestAccounts.SeedFirstAdmin` now treats an administrator as "first" when no sign-in presentation administrator has been staged yet.
    - The walkthrough runner gained `-DatabaseState SetUp|Fresh|UpgradeRequired|NotInstalled` (default SetUp, which marks the database set up), and the chain runner marks its database as set up.
  - **Runner restart:** Playwright now runs as a watched process. On ChainHost exit 75 the runner restarts it on the same URL and database (at most 3 times); any other host exit fails the run. The readiness wait accepts any answer from the status route.
  - **Dev:** `Start-DevApi.ps1` runs the API through `scripts/RestartLoop.ps1`. It restarts only on 75 (bounded at 10), the first start builds, restarts use `--no-build`, and the script exits with the last code.
  - **Client:**
    - `StartupGate` asks the status route first. It shows the installer or upgrade message, sends Setup mode to `/setup`, and otherwise renders as before; a failed check never blocks the app.
    - The setup page waits through the restart (polling every 2 s, giving up after 120 s).
    - The gate asks again when the page leaves `/setup`; S14 Fresh found the stale-mode bug and it is fixed.
  - **Tests:**
    - **S18:** "one migration behind" is made by removing the newest `__EFMigrationsHistory` row, which is what the probe reads; no real down-migration is run.
    - **S21:** the "unreachable server gives up within its bound" case is not a browser test. The probe's bounds are in code, and the S18 tests cover a missing database, which is decided at once. An unreachable-server timing test (about 1 minute) was left out to respect the time budget.
  - **Results:**
    - The first slice 7 focused backend group ran **8 min 8 s**, against my 3 to 4 minute estimate. It ran in the background, so it could not be stopped at a safe point; this is disclosed.
    - After the fixes: StartupModeTests + DeploymentSetupApiTests 10/10; S20 2/2; RestartLoopScriptTests + TestSupportGuardTests 43/43.
    - Client: vitest 2181/2183 (the 2 failures are the known flaky U-018); AccountBootstrap.test.tsx 23/23.
    - Walkthroughs: S14 Fresh 4/4 (one restart on exit 75); S14 SetUp 1/1; S21 NotInstalled and UpgradeRequired 1/1 each; auth 13/13. Evidence is in `7-walkthrough-evidence/`.

## Assumptions
- **Minimum password length.** 12 characters for the Deployment Administrator's setup password and for a self-service change. The app had no password-length rule before.
- **Gate scope.** The forced-change gate applies only to endpoints that require a signed-in user.
- **Picker exclusion.** The message recipient list counts as a person picker, so the Deployment Administrator is excluded from it as well as from follow-up assignees.
- **Permission checks.** ManageUsers is checked inside the staff-member actions, because `RequirePermission` cannot be stacked.

## Findings fixed in ALV-N030 (owner assignments)
- **U-036:** four ALV-N025 classes added to `RealMigrationInventory` (slice 1).
- **U-037:** the invariant culture is now named at 9 fixed-format date lines in 7 files. Five of them named no culture at all (ALV-N022 A3, ALV-N023); the other two files used a correct local variable that the guard did not recognise (ALV-N026).
- **U-039:** the backup coverage test builds its included-class list from `ManagedAssetClasses.All`.
- **U-038:** see above.

## Removed tests (their feature is gone; replacements named)
- **Sign-up and bootstrap:**
  - all of `AccountServiceBootstrapTests` (4) and `AccountServiceRegistrationTests` (5);
  - 6 sign-up and bootstrap tests in `AuthControllerTests`;
  - the sign-up trimming test in `AccountServiceLoginTests`.
  - Replaced by S1 (the routes give 404/405) and the setup input checks in `DeploymentSetupTests`.
- **Profile-only create and link edits:**
  - 3 linking tests in `StaffProviderServiceTests`;
  - the link-swap test in `StaffInactivationServiceTests`;
  - the link-picker test.
  - Replaced by S16 in `StaffMemberAndOwnerTests`.

## Test evidence still to complete
- The indirect users of the changed shared test bases (`SchedulingTestSupport`, `SafetyTestBase`, `CompletionTestBase`, `SchedulingApiHarness`) are deferred to ALV-N028's broad regression. Slice 5b ran the 37 directly edited classes plus a sample (owner-approved).
- **R07 evidence vs ALV-N013 evidence (review requirement).** The R07 ChainHost evidence (G1 to G8, runners) must be presented apart from ALV-N013's installer and service-supervisor proof, which is not built.
