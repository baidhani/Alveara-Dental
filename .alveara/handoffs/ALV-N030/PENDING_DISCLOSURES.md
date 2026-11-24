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
