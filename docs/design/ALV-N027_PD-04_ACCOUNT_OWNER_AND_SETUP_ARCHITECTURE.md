# PD-04: account, Practice Owner and setup architecture

Status: **owner direction recorded; design input to the dedicated account story (working name ACCT).** Not yet a reviewed brief. No code before the ACCT brief is independently approved. Session CC-20261123-q4vn, 2026-11-23.

## 1. Owner direction (recorded unchanged, 2026-11-23)

> PD-04 architecture direction — replace the prior “Add user in Security Administration” proposal.
>
> Use these definitions consistently:
>
> - Account: authentication credentials, account state, roles and permissions.
> - Staff profile: a person’s practice identity and employment/provider details.
> - Staff member / user: one human staff profile linked to one interactive account.
>
> Normal human staff accounts must not be created separately from profiles. Practice Configuration → Staff is the home for creating them.
>
> Change “Add staff profile” to “Add staff member.” The flow creates, in one operation:
>
> - the staff profile;
> - an interactive login account;
> - their required one-to-one link;
> - initial role/permissions;
> - a temporary password;
> - mandatory password change at first sign-in.
>
> Security Administration remains the place for managing the security properties of existing accounts: password reset, enabled/disabled/locked state, role and permission changes within authority, account lifecycle history, and audit. It is not the normal account-creation surface.
>
> The initial deployment account is a special account-only Deployment Administrator:
>
> - It has the same effective access as an Administrator. Do not create a separate reduced permission matrix for it.
> - It has no staff profile and must not appear in staff, provider, scheduling, or staff-identity selections.
> - Its presentation experience must prominently guide it to create the first Practice Owner user.
> - It may still use normal practice-configuration screens if necessary. The guidance is advisory, not a technical block.
> - It must not ship with a disclosed or fixed password.
>
> The Deployment Administrator creates the first Practice Owner as a complete human staff member: linked profile plus account, temporary password, and forced password change at first sign-in.
>
> Practice Owner is a protected ownership designation, not a broad new role or general permission-matrix branch:
>
> - There is one active Practice Owner at a time.
> - The Practice Owner receives the guided initial Practice Setup wizard after first sign-in and password change.
> - Ordinary Administrators cannot casually assign, remove, or transfer ownership.
> - Ownership transfer must be a deliberate, confirmed, audited workflow and is outside the initial N027 implementation unless specifically designed and approved.
>
> When no Practice Owner exists, the Deployment Administrator dashboard must clearly say that no Practice Owner has been created and recommend creating one before configuring the practice. It must explain that manual configuration remains available but the guided setup wizard is reserved for the Practice Owner.
>
> The initial Practice Setup wizard is a separate follow-on story, not scope to silently add to N027. It will reuse existing Practice Configuration capabilities in a guided sequence:
>
> 1. Practice information, locations, and visit workflow.
> 2. Staff members: combined staff-profile and account creation.
> 3. Providers selected from eligible staff.
> 4. Operatories.
> 5. Appointment types.
> 6. Availability.
> 7. Review and final confirmation.
>
> For provider specialties, replace the previously proposed strict fixed list with a searchable standard specialty list plus an editable custom value. Preserve the selected standard value when applicable; require clear “Other/custom specialty” handling for non-standard text.
>
> Before code, revise the ALV-N027 pre-implementation brief to show the exact routes, schema/data changes, service rules, authorization, audit events, migration impact, forced-password behavior, anonymous registration shutdown behavior, and focused security-test matrix. Submit that revised brief for independent review.

## 2. Owner rulings after the direction (2026-11-23)
1. **Own story.** PD-04 is its own story, implemented immediately before ALV-N027. The order is: ACCT, then ALV-N027, then the setup wizard, then release readiness.
2. **Specialty.** A searchable standard list plus "Other / custom specialty":
   - a standard choice stores a stable code;
   - "Other" requires free text, shown labelled as custom;
   - no unrestricted editable combobox.
   (This is delivered in ALV-N027 under PD-31, not in ACCT.)
3. **Deployment Administrator bootstrap:** a first-run setup page with a one-time token, under these safeguards:
   - available only while no Deployment Administrator exists;
   - single-use token that expires after about 15 minutes;
   - the token is shown only at installation or deployment time and is never written to application logs;
   - the token is typed into the setup page, never placed in a URL query string or browser history;
   - the endpoint is rate-limited and gives generic failure responses;
   - the token is invalidated permanently once setup completes;
   - the operator sets the permanent password directly on the setup page, so there is no temporary credential to change;
   - the resulting account is account-only, has Administrator-equivalent access, and is guided (not forced) to create the first Practice Owner.
4. **Push** of the four documentation commits: done (`a142a1e`).

## 3. What the code does today (agent survey, read-only, at a142a1e)
- **Entities** (`Architecture/Identity/IdentityEntities.cs`):
  - `UserAccount` (Username, PasswordHash, Role, lockout, IsDisabled, SecurityStamp, MFA);
  - `StaffProfile` (optional `UserAccountId`, DisplayName, JobTitle, IsActive, prescriber fields);
  - `ProviderProfile` (StaffProfileId, free-text `Specialty`).
  The account-to-profile link is optional today, and there is no forced-password-change flag.
- **Roles** (`Role.cs`): Dentist, Hygienist, Assistant, FrontDesk, Billing, OfficeManager, Admin, plus `Unassigned` (used by self-registration).
- **Anonymous registration:**
  - `POST /api/auth/register` (`AuthController.cs:43`, rate-limited `AuthAttempts`) creates a disabled `Unassigned` account (`AccountService.RegisterAsync`, line 84).
  - It is always open; there is no switch.
- **First administrator:**
  - `POST /api/auth/bootstrap-admin` (`AuthController.cs:69`) takes the username, password and a **static configured secret** (`AdminBootstrapSecret`), and creates an `Admin` account once.
  - The `BootstrapState` singleton primary key makes it exactly-once under a race.
  - It stages the sign-in presentation administrator (ALV-N020-C01).
  - There is no token expiry, no setup page, and the secret is long-lived configuration. The ruled design replaces this.
- **Staff creation:**
  - `POST /api/config/staff` (`ConfigurationController.cs:209`, ManagePracticeConfiguration) creates a profile and optionally links an existing account (`GET /api/config/linkable-accounts`).
  - Accounts come from register or bootstrap, so there is no combined create today.
- **Security Administration:**
  - `GET /api/auth/users`, with role, enabled, session-timeout, reset-password and revoke-sessions actions (`AuthController.cs:290-448`);
  - plus the ALV-N017/N018 grant and target rules (`AccountPermissions.cs`, `AccountTargetRules`).

## 4. Impact the ACCT brief must resolve (agent findings)
1. **The test harness depends on registration and the static bootstrap secret.**
   - `auth/register` is used in **77** client/e2e/chain files and **48** backend test files.
   - `bootstrap-admin` is used in **59** e2e/chain files and **22** backend test files.
   - The scripts `New-DevAdmin.ps1`, `Start-DevApi.ps1`, `Invoke-RealBackendWalkthrough.ps1` and `Invoke-AccumulatedChain.ps1` depend on the static bootstrap secret.
   - Shutting registration off and replacing the bootstrap changes how every one of these creates users. This is the largest cost in the story, and it reaches the expensive walkthrough and Stage 2 suites. See the open question in section 5.
2. **Existing installations** (the dev database, any already-bootstrapped database) have an `Admin` created by the old bootstrap and staff profiles without a required link.
   - The migration has to decide whether that admin becomes the Deployment Administrator or stays an ordinary administrator.
   - It must also decide what happens to existing `Unassigned` or unlinked accounts. The one-to-one link applies to new staff members; existing data is not rewritten without a rule.
3. **The Deployment Administrator** is an account with no staff profile. Every place that lists staff or providers, or resolves actor names (ALV-N025 `ClinicalNames.ResolveAsync`), must exclude it or name it safely.
4. **The Practice Owner designation** needs a store: exactly one active holder, enforced by the database (for example a single-row designation table with a unique key), and a refusal for ordinary administrators.
5. **Forced password change:**
   - a per-account flag;
   - while it is set, the session can only reach change-password, sign-out and whoami (a server-side gate, not just a client redirect);
   - the flag clears on success and the security stamp rotates.
6. **New audit events (proposed):**
   - DeploymentSetupCompleted;
   - StaffMemberCreated, carrying both the account and the profile id;
   - TemporaryPasswordIssued;
   - InitialPasswordChanged;
   - PracticeOwnerDesignated;
   - RegistrationRefused (counted, not one row per attempt).

## 5. Open questions for the owner (before the ACCT brief)
- **Q-ACCT-1, how tests create users once registration is off.** Recommended: a test-only provisioning route enabled only by an explicit test-environment setting that production never sets, plus a guard test proving the route answers 404 without that setting. This lets the 200+ test files change one helper each, not their flows. The alternative is that every test signs in as an administrator and uses "Add staff member", which is slower and touches every spec.
- **Q-ACCT-2, the existing administrator.** On upgrade, the existing bootstrap `Admin` becomes the Deployment Administrator only if it has no staff profile; otherwise it stays an ordinary administrator. Recommended.
- **Q-ACCT-3, registration shutdown.** `POST /api/auth/register` is removed outright, answering 404 like an unknown route, rather than kept behind a switch. Recommended, since nothing in the product needs it once "Add staff member" exists.

## 6. Owner answers to section 5 (2026-11-23)
- **Q-ACCT-1:** approved. A distinct test-only user-creation route, not the former sign-up route behind a switch. It is mapped only in the dedicated test environment with an explicit test setting, and cannot be enabled in production, development, staging or deployed configuration by an ordinary flag. Production does not map it (404). A guard test proves it is absent outside the test host. It never appears in API documentation, navigation or deployment configuration. Existing test helper calls may move to it.
- **Q-ACCT-2:** partly accepted:
  - an Administrator without a staff profile becomes the Deployment Administrator;
  - one with a profile stays an ordinary Administrator and is never silently made owner;
  - if no owner exists after upgrade, an active Administrator sees a notice and may designate an eligible active staff member with a linked account (themselves included). This is confirmed and audited, and is a one-time transition step. It never chooses automatically by account order or creation date.
- **Q-ACCT-3:** approved. The sign-up endpoint is removed completely (404). The test-only helper is separate and can never become a hidden production registration path.

The brief: `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`.
