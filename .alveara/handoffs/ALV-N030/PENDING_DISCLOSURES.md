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
- **Scope note.** The target rules compare ROLE effective permissions. Account-level grants (`AccountPermissionGrants`, today only `ManageLoginPresentation`) are not part of that comparison; this is unchanged by ALV-N030.

## Deviations from the brief (approved set R04 + R06 + R07)
- **Identity call (slice 3).** The new identity fields and the forced-change exemption are on `GET /api/auth/permissions`, not on `whoami`. `whoami` is Administrator-only, and the client's identity call is `/permissions`. The exempt set is still exactly five actions.
- **G3 (slice 5a).** G3 reads MSBuild's publish file list (`ComputeResolvedFilesToPublishList`) instead of running a full `dotnet publish`. A full publish of Alveara.Api runs `npm ci` and `npm run build` (csproj lines 49-59).
- **Section 8 superseded by section 17 (R07).** Test support is hosted on ChainHost; no product start-up split and no TestHost executable.

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
