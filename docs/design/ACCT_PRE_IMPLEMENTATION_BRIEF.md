# ACCT: accounts, Deployment Administrator and Practice Owner. Pre-implementation brief

- **Status:** **APPROVED FOR IMPLEMENTATION.** Sections 1 to 15 were approved at R04 (`.alveara/reviews/ACCT/BRIEFING-R04.md`, after R01 to R03). The section 16 addendum was approved at R06 (`.alveara/reviews/ACCT/BRIEFING-R06.md`, after R05). The section 17 addendum was approved at R07 (`.alveara/reviews/ACCT/BRIEFING-R07.md`); it supersedes section 8's hosting. **The approved briefing set is R04 + R06 + R07.** **Section 18 (ALV-N030-C01): the Practice Owner authorization invariant (U-049), ownership transfer and Deployment Administrator recovery - APPROVED FOR IMPLEMENTATION at R10 (`.alveara/reviews/ACCT/BRIEFING-R10.md`), after R08 and R09 CHANGES_REQUIRED. The approved revision is R10. Owner execution order: C01, then ALV-N030-C02 (MFA QR enrolment, then Deployment Administrator MFA enforcement), both before ALV-N027.** Boundary: database creation, SQL snapshot settings, applying migrations, a production service wrapper and update distribution stay with ALV-N013; the production automatic-restart proof is ALV-N013's dependency proof, not an ALV-N030 acceptance item. Revision history follows.
- **R04 changes (CR-6):**
  - the audit event `DeploymentAdministratorAssigned` is limited to the migration's single-candidate conversion; the superseded "administrator pick" wording is removed;
  - slice 5, the matrix and the evidence all use guards G1 to G8.
- **R03 changes:**
  - **CR-4:** the `MustChangePassword` contract made unambiguous, including how to recover a lost temporary password;
  - **CR-5:** the TestHost binds to loopback only. **No code before approval.**

**R02 changes** (see section 15 for how each correction is met):
- **CR-1:** no new profile-only staff. The profile-only create route is removed, and a labelled legacy path replaces it.
- **CR-2:** a separate `Alveara.Api.TestHost` executable replaces the `--test-support` startup path.
- **CR-3:** path 1 chosen. With several profile-less administrators, no Deployment Administrator is selected, and an administrator designates the owner directly.
- **Section 14:** questions resolved by the review.
- **R05 addendum (2026-11-24, owner-requested, PD-38):** section 16 adds setup mode with a controlled restart. The R04 approval stands for sections 1 to 15; review covers section 16 only, plus its test rows S18 to S22 and slice 7.
- **R06 (after `BRIEFING-R05.md`, CHANGES_REQUIRED):**
  - **CR-7:** section 16.2a sets the behaviour of the client files and the status route in every mode;
  - **CR-8:** the restart is an exit-code mechanism with a defined handoff to the supervisor (16.2b), proven by S20 and S22, with the production proof assigned to ALV-N013 (16.5).
- **R07 addendum (2026-11-24, owner-requested, slice 5 finding):** section 17 replaces section 8's hosting with test support on the existing `Alveara.Api.ChainHost`: no product start-up split and no second test executable, under the owner's seven boundaries. It also retargets guards G1 to G8, the harness migration and the TestHost wording in sections 12 and 16; everything else stands. **Approved at R07** (`.alveara/reviews/ACCT/BRIEFING-R07.md`, APPROVED_FOR_IMPLEMENTATION).
- **Story id:** **ALV-N030** (owner, 2026-11-24; plan item 63a; working name ACCT). **Approved for implementation:** `.alveara/reviews/ACCT/BRIEFING-R04.md`.
- **Session:** CC-20261123-q4vn, 2026-11-23.
- **Surveyed tree:** `d459025` (read-only).
- **Position:** next implementation item, before ALV-N027 (owner sequence: ACCT, ALV-N027, setup-wizard story, release readiness).

**Sources:**
- owner direction, unchanged: `docs/design/ALV-N027_PD-04_ACCOUNT_OWNER_AND_SETUP_ARCHITECTURE.md` section 1;
- owner rulings: section 2 of that file, plus section 5 (answered below);
- decision record: `docs/design/ALV-N027_OWNER_DECISION_RECORD.md`.

## 1. Owner decision record for this story
| # | Ruling (owner, 2026-11-23) |
|---|---|
| D-1 | Human staff accounts are created only together with their staff profile ("Add staff member" in Practice Configuration → Staff): profile, interactive account, the required one-to-one link, initial role, temporary password and a forced change at first sign-in, **in one operation**. |
| D-2 | Security Administration manages existing accounts' security properties (reset, enabled, disabled and locked state, role and permission changes within authority, lifecycle history, audit). It is not the normal creation surface. |
| D-3 | **Deployment Administrator:** account-only, Administrator-equivalent access (no separate matrix), no staff profile, never listed in staff, provider, scheduling or staff-identity selections. It is advisory-guided to create the first Practice Owner, and never has a fixed or disclosed password. |
| D-4 | **Practice Owner:** a protected designation, not a role. One active holder at a time. Ordinary Administrators cannot casually assign, remove or transfer it. Transfer is **out of scope** unless designed and approved separately. |
| D-5 | **Bootstrap:** a first-run setup page with a one-time token:
  - available only while no Deployment Administrator exists;
  - single use, expires after about 15 minutes;
  - shown only at installation or deployment time and never written to application logs;
  - typed into the page, never in a URL;
  - the endpoint is rate-limited with generic failures;
  - the token is invalidated permanently on success;
  - the operator sets the permanent password directly. |
| D-6 | **Test-only user-creation route:** a distinct route, not the former sign-up route. It is mapped only in the dedicated test environment **and** with an explicit test setting, and cannot be enabled in production, development, staging or a deployed configuration by an ordinary flag. Outside the test host it is not mapped at all (404), and a guard test proves this. It never appears in API documentation, navigation or deployment configuration. Existing tests may move to it. |
| D-7 | **Upgrade:**
  - an existing Administrator **without** a staff profile becomes the Deployment Administrator;
  - an Administrator **with** one stays an ordinary Administrator and is never silently made owner;
  - if no Practice Owner exists after upgrade, an active Administrator sees a notice and may designate an eligible active staff member with a linked account (themselves included) as the initial owner. This is confirmed and audited, and is a one-time transition step;
  - the owner is never chosen automatically (by account order, creation date or anything else). |
| D-8 | **Sign-up is removed completely.** `POST /api/auth/register` no longer exists and answers 404 like any unknown route. |
| D-9 | The guided Practice Setup wizard is a **separate follow-on story**, not in ACCT. ACCT only shows the guidance notices. |

## 2. Scope
**In:**
- the setup-token bootstrap (CLI issue, setup page and route);
- the Deployment Administrator kind;
- "Add staff member" (a combined create);
- temporary password and forced change;
- the Practice Owner designation (a first designation by the Deployment Administrator through "Add staff member", and the one-time upgrade designation step);
- removal of sign-up;
- the test-only provisioning route and the migration of the test harness to it;
- the dashboard notices;
- exclusion of the Deployment Administrator from staff and identity selections;
- audit events, migration and security tests.

**Out:**
- ownership transfer and removal (D-4);
- the setup wizard (D-9);
- the specialty list (ALV-N027 PD-31);
- U-033 (folder picker);
- the Windows installer itself (plan item ALV-N013). ACCT provides the CLI command the installer will call;
- MFA changes;
- a Practice Owner permission set (D-4: a designation, not a role).

## 3. Data and schema changes (one migration, plus a data step)
| Change | Detail |
|---|---|
| `UserAccounts.AccountKind` (tinyint, not null, default 0) | 0 = `Staff` (an interactive account for a human staff member), 1 = `DeploymentAdministrator`. A filtered unique index (`AccountKind = 1`) allows **at most one** Deployment Administrator. Role stays `Admin` for it, so permissions come from the existing Admin row and no new matrix exists (D-3). |
| `UserAccounts.MustChangePassword` (bit, not null, default 0) | **Set to 1 only by ACCT's temporary-password issuance**: "Add staff member" and the legacy-login action (CR-4). **Never set** by the existing administrator reset (`POST /api/auth/{id}/reset-password`, then `reset-password/complete`), because there the recipient chooses their own password. **Cleared** by a successful `POST /api/auth/password/change`, **or** by a successful reset-complete, since that is also a password the person chose. |
| `SetupTokens` (new table) | `Id`, `TokenHash` (SHA-256 of a 32-byte random token; the token itself is never stored), `IssuedAtUtc`, `ExpiresAtUtc` (issued + 15 min), `ConsumedAtUtc` (null until used), `FailedAttempts`. At most one unconsumed row: issuing a new token voids earlier ones. |
| `PracticeOwnerDesignations` (new table) | `Id`, `StaffProfileId` (FK, required), `DesignatedAtUtc`, `DesignatedByAccountId`, `Method` (`InitialByDeploymentAdministrator` or `UpgradeTransition`), `EndedAtUtc` (null while active). A filtered unique index on a constant column where `EndedAtUtc IS NULL` gives **exactly one active owner**, enforced by the database. Rows are never deleted; an update trigger refuses any change except setting `EndedAtUtc`, which only a future transfer story would do (ACCT has no code path that sets it). |
| `BootstrapState` | Kept, so existing databases keep their history. The new setup flow writes the same singleton for exactly-once behaviour under a race, as today. |
| `StaffProfiles.UserAccountId` | Already one-to-one where present (filtered unique index, `AlveraDbContext.cs:209`). Stays nullable for existing data (D-7: no rewrite of history). The rule "a new staff member has a linked account" is enforced by the combined create, not by a NOT NULL constraint. |
| `Role.Unassigned` | Kept in the enum because existing rows may hold it. Nothing creates it any more. Existing `Unassigned` accounts stay disabled and are listed in Security Administration as legacy, so an administrator can review them. |

**Data step on upgrade** (in the migration, deterministic, idempotent):
- **Deployment Administrator:** exactly the Admin accounts with no linked staff profile are candidates.
  - If there is one, it gets `AccountKind = 1`.
  - If there are **several** (R01 CR-3, **path 1 chosen**):
    - none is converted, and **no Deployment Administrator is selected at all**. The installation runs without one, which is allowed because the Deployment Administrator exists only to bootstrap a new installation;
    - the profile-less Administrators stay ordinary Administrators, labelled "legacy account without a staff profile" in Security Administration, with no change to their access;
    - an active Administrator designates the initial Practice Owner directly through the D-7 transition (`POST /api/practice-owner/initial`, section 4).
    - There is **no** route that selects a Deployment Administrator after upgrade; the only way one is created is the setup flow on a never-set-up installation.
  - If there are none, there is no Deployment Administrator, and the same D-7 transition applies.
  - **Why path 1:** it adds no second designation mechanism, and the Deployment Administrator's only purpose (guiding first-owner creation) is served by the D-7 notice to any active Administrator.
- **Practice Owner:** never set by the migration.
- **Pinning:** a migration test pins the step's behaviour for 0, 1 and 2 profile-less admins.

**Merge, export and registry guards** (lessons U-015, U-016, U-020, U-022):
- **`PracticeOwnerDesignations`** holds a `StaffProfileId`, not a patient id, so patient merge is unaffected.
- **Export catalog:** both new tables are added to `ExportDomainCatalog`, `SetupTokens` to `system_operations` (excluded) and `PracticeOwnerDesignations` to `accounts_and_credentials`.
- **Focused group:** `ExportDomainCatalogTests` and `PatientOwnedDataTests` run in the focused group.

## 4. Routes
| Route | Auth | Change |
|---|---|---|
| `POST /api/auth/register` | anonymous | **Removed** (D-8). The controller action, `RegisterRequest` and `AccountService.RegisterAsync` are deleted. The client has no sign-up screen (to confirm in the slice; none found in `pages/`). |
| `POST /api/auth/bootstrap-admin` | anonymous plus static secret | **Removed.** Replaced by the setup routes. `AdminBootstrapSecret` is no longer read. |
| `GET /api/setup/status` | anonymous | New. Returns only `{ setupAvailable: bool }`, which is true while no Deployment Administrator exists **and** the install has never been set up (`BootstrapState` absent). Nothing else is disclosed. |
| `POST /api/setup/deployment-administrator` | anonymous, token in the **JSON body** | New. Body: `token`, `username`, `password`. Rate-limited (`AuthAttempts` plus a dedicated stricter `SetupAttempts` policy). Every failure (bad, expired, used or missing token; setup no longer available) answers the same **400 `{error:"setup_failed"}`**, except password-policy and username validation, which are only checked **after** the token verifies, so they disclose nothing to someone without one. On success: in one transaction, the token is consumed, `BootstrapState` is written, the account is created (`Admin`, `AccountKind = 1`, `MustChangePassword = 0`, D-5 last point) and the presentation administrator is staged (as `StageBootstrapHolder` does today); then 201. |
| `POST /api/config/staff-members` | ManagePracticeConfiguration **and** ManageUsers, CSRF | New combined create (D-1). Body: display name, job title, username, role, optional provider fields left to the existing provider route. The server generates the temporary password and returns it **once** in the 201 body, never logged or audited. Profile, account (`AccountKind = 0`, enabled, `MustChangePassword = 1`) and link are made in one transaction. Idempotency-Key is required (replay returns the same result **without** the password again, only "already created"). The target-role rule reuses the ALV-N018 full-subset rule: an actor can only create a role whose permission set is a subset of its own; an Admin can create any. |
| `POST /api/config/staff` (profile-only create) | existing | **Removed** (R01 CR-1). The action is deleted, so the route answers 404 or 405 like any unmapped route, and no UI offers a profile-only create. `CreateStaffAsync` loses its public create path; the service keeps only the combined create. |
| `PUT /api/config/staff/{id}` | existing | Still edits display name, job title and prescriber fields. It **no longer accepts `UserAccountId`**: the field is dropped from the request contract, and a request that sends it gets 400 `link_not_editable`, so an update can't make or change a link. |
| `GET /api/config/linkable-accounts` | existing | **Removed.** It existed only to link existing accounts on create or update. |
| `POST /api/config/staff/{id}/legacy-login` | ManagePracticeConfiguration **and** ManageUsers, CSRF, Idempotency-Key | New, **legacy remediation only** (CR-1). For an existing **active** staff profile with **no** linked account, created before ACCT. It creates a new account plus the link plus a temporary password plus `MustChangePassword`, in one transaction, under the same rules as the combined create. It refuses with 409 if the profile already has an account. In the UI it is the action "Create a login for this legacy staff record", shown only on such rows, which are labelled "Legacy record without a login". It never links an *existing* account. |
| Legacy accounts with no profile (`Unassigned` self-registrations, extra profile-less admins) | Security Administration | Stay readable, auditable and able to be disabled. They are labelled "Legacy account without a staff profile". ACCT offers no linking path, so legacy accounts are only reviewed or disabled. |
| `POST /api/auth/password/change` | signed in, CSRF | New (no self-service change route exists today; only the reset-complete flow). Body: current password, new password. On success: `MustChangePassword = 0`, the security stamp rotates, the session is re-issued and an audit row is written. |
| `GET /api/auth/whoami` | signed in | Adds `mustChangePassword`, `accountKind`, `isPracticeOwner`, `practiceOwnerExists`. |
| `GET /api/practice-owner` | signed in, ManageUsers | New. Returns the active owner's display name and staff id, or none. |
| `POST /api/practice-owner/initial` | ManageUsers, CSRF | New (D-7 transition, and the first-owner path). It succeeds only while **no** designation row has ever existed (database check plus unique index), so a second call answers 409 `owner_already_designated`. Target: an active staff profile with a linked, enabled, `AccountKind = 0` account. Body includes `confirm: true`. |
| `POST /api/test-support/accounts` | anonymous | **Test host only** (D-6, section 8). |

The "Add staff member" form has an optional "Make this person the Practice Owner" checkbox, shown only while no owner exists. Ticking it calls `/practice-owner/initial` after the create, in the same UI step and as a separate audited action. That is how the Deployment Administrator creates the first owner (D-3).

## 5. Authorization rules
- **Deployment Administrator:**
  - permissions equal Admin's, because its role is `Admin`;
  - its `AccountKind` excludes it from staff, provider, scheduling and assignee lists and from actor-name pickers;
  - actor-name resolution (`ClinicalNames.ResolveAsync`) names it **"Deployment Administrator"** (it has no profile).
  - **It cannot be the Practice Owner**, because it has no staff profile.
- **Practice Owner:**
  - no extra permissions in ACCT;
  - the designation only drives the setup-wizard entry (next story) and is shown in Security Administration and Staff;
  - no route in ACCT ends or changes a designation.
- **"Add staff member"** and the legacy-login action need both ManagePracticeConfiguration (the profile) and ManageUsers (the account), because they create a login. An Office Manager without ManageUsers sees no create action at all (CR-1: there is no profile-only alternative). They can still edit existing staff details as today.
- **Account-only exceptions:** the Deployment Administrator, and legacy rows that existed before ACCT. Technical service accounts are a future, out-of-scope exception.
- **Forced change gate** (server-side). While `MustChangePassword = 1`, every authenticated request except the following answers **403 `{error:"password_change_required"}`**:
  - `GET /api/auth/whoami`;
  - `GET /api/auth/csrf-token`;
  - `POST /api/auth/password/change`;
  - `POST /api/auth/logout`;
  - the MFA challenge routes needed to finish sign-in.

  The gate lives in the `OnValidatePrincipal` query that already runs on every request (`Program.cs:342`), at no extra round trip. The client routes to a "Choose a new password" page; that redirect is UX only and the server gate is the control.

## 6. Service rules
- **Temporary password:**
  - 16 characters from an unambiguous alphabet, via `RandomNumberGenerator`;
  - stored only as the PBKDF2 hash;
  - returned once;
  - an administrator's password reset (existing `reset-password`) is **unchanged**, since it uses a reset token, not a temporary password. It **never sets** `MustChangePassword` (R01 ruling, CR-4). A successful reset-complete **clears** it, and it is the named recovery path for a lost temporary password (section 10).
- **Setup token:**
  - issued by a CLI command, `Alveara.Api setup issue-token`. The command runs the app's DbContext against the configured database, writes the hashed row, and prints the token **to the console only** (not through `ILogger`), plus the setup page URL;
  - refused (exit code 2, message) if a Deployment Administrator already exists or setup was completed;
  - re-running it voids the previous token. This is the installer step (ALV-N013 calls it); the dev script `New-DevAdmin.ps1` calls it and then the setup route.
- **Token verification:** the hash is compared in fixed time, the expiry is checked against the practice clock, and 5 failures void the token (`FailedAttempts`), so a new one must be issued.
- **Logging:**
  - no request or response body of the setup, change-password or staff-member routes is logged;
  - the request-logging middleware gets a redaction list for these paths. A test asserts that the token and passwords never appear in the captured logs.

## 7. Audit events (new `AuditEventTypes`)
| Event | When | Recorded | Never recorded |
|---|---|---|---|
| `SetupTokenIssued` | CLI issue | expiry | the token |
| `SetupTokenRejected` | failed setup attempt | reason class (count, not one row per attempt) | the token, the username tried |
| `DeploymentAdministratorCreated` | setup success | account id, username | password |
| `StaffMemberCreated` | combined create | account id, profile id, role, actor | temporary password |
| `TemporaryPasswordIssued` | same transaction | account id, actor | the password |
| `PasswordChanged` | self change | account id; whether it was forced | either password |
| `PasswordChangeRequiredRefusal` | gate refusal | counted per session, not per request | |
| `PracticeOwnerDesignated` | initial and transition designation | staff profile, account, actor, method | |
| `DeploymentAdministratorAssigned` | **only** the upgrade migration's deterministic conversion when exactly one profile-less Administrator exists (written by the migration's data step, actor "system (upgrade)", method `UpgradeSingleCandidate`). It is kept so that the change of an existing account's kind has an audit trail. **No route writes it**, and no route selects a Deployment Administrator after upgrade (path 1). | account, method | |
| `AccountRegistered` | kept as a constant | existing rows still read | no longer written |

## 8. The test-only provisioning route (D-6, revised for R01 CR-2)
- **Three projects, with references in one direction only:**
  - **`Alveara.Api`** (the product executable). It is unchanged except that its startup is split:
    - `Program.cs` calls a public `AlvearaApplication.CreateBuilder(args)` / `AlvearaApplication.Configure(app)` pair, moved out of the top-level statements without changing behaviour;
    - it has **no** reference to test support and **no** test-support startup argument or setting. An unknown argument such as `--test-support` is simply ignored by the configuration system, and **nothing reads it**; a guard test proves this.
  - **`Alveara.Api.TestSupport`** (class library). It holds `TestAccountsController` and its service, and references `Alveara.Api`.
  - **`Alveara.Api.TestHost`** (a separate executable). It references `Alveara.Api` and `Alveara.Api.TestSupport`:
    - its `Program` calls `AlvearaApplication.CreateBuilder`, adds the TestSupport assembly as an MVC application part, then calls `Configure`;
    - **at startup it refuses to run** (throws and exits non-zero) unless `ASPNETCORE_ENVIRONMENT=Test` **and** `TestSupport:Enabled=true`.
- **Who starts what:**
  - **backend tests:** `Alveara.Api.Tests` uses `WebApplicationFactory<Alveara.Api.TestHost.Program>`, so the test-support route exists only in that host;
  - **walkthrough and chain runners:** `Invoke-RealBackendWalkthrough.ps1` and `Invoke-AccumulatedChain.ps1` run `dotnet run --project src/Alveara.Api.TestHost` with `ASPNETCORE_ENVIRONMENT=Test` and `TestSupport__Enabled=true`, instead of the product API (today they run it as `Development`). The Vite proxy is unchanged;
  - **dev and production:** `Start-DevApi.ps1`, any publish profile and the future installer run only `Alveara.Api`.
- **The route:** `POST /api/test-support/accounts` with body `{ username, password, role, kind?, withStaffProfile?, mustChangePassword? }`. It creates an enabled account directly, so tests that need an Admin call it instead of the bootstrap.
- **Guard tests (CR-2 proof):**
  - **G1:** `WebApplicationFactory<Alveara.Api.Program>` (the product host) under the Development, Production **and Test** environments, **with** `TestSupport:Enabled=true` and the argument `--test-support` supplied, answers **404** on `POST /api/test-support/accounts`;
  - **G2:** `Alveara.Api.csproj` has no ProjectReference or PackageReference to TestSupport or TestHost (a project-file assertion);
  - **G3:** `dotnet publish src/Alveara.Api` output has no `Alveara.Api.TestSupport.dll` or `Alveara.Api.TestHost.dll`;
  - **G4:** the TestHost refuses to start without Test and `Enabled`, and maps the route only when both are set;
  - **G5:** a repository scan finds no reference to `Alveara.Api.TestHost` or `TestSupport__Enabled` in `Start-DevApi.ps1`, `New-DevAdmin.ps1`, deployment and installer scripts, `appsettings*.json` of the product, or any Docker or publish file; only the two runners and the tests;
  - **G6:** the OpenAPI document (if one is served by the product) does not list the route.
  - **G7 (CR-5):** the TestHost's start-up checks every configured listen address (`urls`, `ASPNETCORE_URLS`, `Kestrel:Endpoints:*:Url`, `--urls`) and **refuses to start** (non-zero exit, clear message) unless each host is `127.0.0.1`, `::1` or `localhost`:
    - `0.0.0.0`, `*`, `+`, `[::]`, a LAN address or a host name are all refused;
    - with no explicit address, it binds `http://127.0.0.1:<port>` itself and never relies on a default that could be wildcard;
    - under `WebApplicationFactory` (in-memory TestServer, no socket) there is no listen address, and the check passes. The test proves both cases.
  - **G8 (CR-5):** a script test asserts that `Invoke-RealBackendWalkthrough.ps1` and `Invoke-AccumulatedChain.ps1` start the TestHost with an explicit `--urls http://127.0.0.1:<port>` (and the Vite proxy targets that address). A start with `--urls http://0.0.0.0:<port>` exits non-zero.
  - CR-5 applies only to the TestHost. The product API has no test-support code at all (G1 to G3).
- **Harness migration:**
  - backend tests create users through a helper in `IdentityTestHelpers`;
  - e2e specs call `register` and `bootstrap-admin` **inline** (77 and 59 files; there is no shared helper in `e2e/support`). Each is replaced by a new shared helper `e2e/support/testAccounts.ts`. The edit is mechanical, the flows don't change, and the test scenarios stay as they are;
  - **auth-real-backend.spec.ts**, which tests sign-up itself, is rewritten to prove sign-up is gone (404) and that the new setup and change-password flows work.

## 9. Client changes
- **Setup page** (`/setup`, unauthenticated):
  - shown only when `setupAvailable`;
  - fields for the setup token, username, password and confirmation;
  - the token is a form field (never read from the URL);
  - success goes to sign-in.
- **Choose a new password page:** forced when `mustChangePassword`.
- **Practice Configuration → Staff:** the primary action becomes "Add staff member" (username, role, the optional owner checkbox while none exists). The temporary password is shown once in a copyable, dismiss-to-hide panel with "Give this password to the person; they must change it when they first sign in." There is no profile-only create (CR-1). Existing profiles without a login are labelled "Legacy record without a login" and offer "Create a login for this legacy staff record". The "link an existing account" picker is removed from the create and edit forms.
- **Dashboard notices:**
  - for the Deployment Administrator with no owner: "No Practice Owner has been created yet. We recommend creating one before you configure the practice: add the owner as a staff member and tick Practice Owner. You can still configure the practice by hand; the guided setup is reserved for the Practice Owner." (owner wording to approve);
  - for an Administrator after an upgrade with no owner: the transition notice with a "Designate the Practice Owner" action (eligible staff list, confirm step).
- **Security Administration:** shows "Deployment Administrator" and "Practice Owner" labels; has no create action; legacy `Unassigned` accounts are labelled as legacy.

## 10. Failure-first answers
- **Setup fails mid-transaction:** nothing is committed and the token is not consumed (the transaction covers all of it). Retrying with the same token is safe while it is unexpired.
- **Two setup requests race:** the `BootstrapState` primary key plus the filtered unique index on `AccountKind = 1` make exactly one succeed. The other gets the generic 400.
- **Combined create fails:** one transaction, so there is never a profile without its account or an account without its link. An Idempotency-Key replay doesn't create twice.
- **The one-time temporary password is lost** (CR-4). This covers a lost response, a closed panel, or the person forgetting it before first sign-in.
  - The original is **never shown again**. Only its hash exists, and an idempotent replay returns "already created" with no password.
  - **Recovery path:** the existing administrator reset. A holder of `IssuePasswordResets` uses "Issue password reset" on the account in Security Administration (`POST /api/auth/{id}/reset-password`) and gets a new one-time reset token. The person completes it at `reset-password/complete`, choosing their own password. That clears `MustChangePassword` and rotates the security stamp, and the old temporary password stops working.
  - The UI's temporary-password panel says so: "If this password is lost, issue a password reset in Security Administration."
- **Two owner designations race:** the unique index makes one win. The other gets 409.
- **Forced-change user loses the page:** sign in again and the gate still applies.
- **Lost setup token:** re-run the CLI. The old one is voided.

## 11. Focused security test matrix
| # | Test | Layer |
|---|---|---|
| S1 | `POST /api/auth/register` and `/api/auth/bootstrap-admin` answer 404 | API |
| S2 | Setup succeeds once with a valid token; the second attempt with the same token, an expired token (clock advanced 16 min), a wrong token, or any attempt once a Deployment Administrator exists all give the identical 400 body | API |
| S3 | 5 failed attempts void the token | API |
| S4 | A concurrent setup race gives exactly one account | API, DB |
| S5 | The token never appears in captured logs, audit rows or the URL (the page posts JSON) | API, browser |
| S6 | The setup route is rate-limited | API |
| S7 | `setup issue-token` refuses once set up; re-issue voids the old token | CLI |
| S8 | The Deployment Administrator has Admin's effective permissions and is absent from staff, providers, linkable accounts, assignee pickers and scheduling lists | API |
| S9 | Combined create: one transaction (fault injection after the profile insert leaves nothing), idempotent replay, temporary password not in logs or audit, role subset rule (an Office Manager without ManageUsers gets 403; a non-Admin cannot create an Admin) | API |
| S17 | CR-4:
  - a password reset never sets `MustChangePassword` (issue plus complete on a normal account leaves it 0);
  - for a staff member with the flag set and the temporary password lost: a replay of the create returns no password; issue a reset, then complete it; the flag is now 0, the stamp has rotated, the old temporary password fails to sign in, and the new one succeeds with no gate;
  - the reset route still needs `IssuePasswordResets` (403 without it). | API |
| S10 | The forced-change gate: every non-allowlisted route answers 403 while it is set (a sample of 10 representative routes, plus a reflection test that the allowlist is exactly the five listed); a change clears it and rotates the stamp (the old cookie is rejected) | API |
| S11 | Practice Owner: the first designation succeeds; a second gets 409; the target must be active staff with a linked, enabled staff account; the Deployment Administrator cannot be designated; non-ManageUsers gets 403; the database refuses a second active row (direct SQL) and any update other than `EndedAtUtc` | API, DB |
| S12 | Upgrade data step with **0, 1 and 2+** profile-less admins: 1 becomes the Deployment Administrator; 0 and 2+ get none, the 2+ admins keep Admin access, are labelled legacy, and no route can later select one; the D-7 owner designation works in all three; no owner is ever set by the migration | migration, API |
| S13 | Test-support guards G1 to G8 (section 8) | API, build, repo scan |
| S16 | CR-1:
  - `POST /api/config/staff` answers 404 or 405 and creates nothing (row count unchanged);
  - `PUT /api/config/staff/{id}` with `UserAccountId` answers 400 and the link is unchanged;
  - `GET /api/config/linkable-accounts` answers 404;
  - legacy-login works only on an active profile without a login: a profile that already has an account gets 409, and an inactive one is refused;
  - existing unlinked profiles and accounts still list, read and appear in the audit log. | API |
| S14 | Browser: setup page to sign-in; Add staff member to temporary password to first sign-in to forced change to dashboard; the Deployment Administrator's dashboard notice; the upgrade notice | Playwright |
| S15 | `ExportDomainCatalogTests` and `PatientOwnedDataTests` green with the new tables | API |

Happy path, failure path, boundary cases (token expiry exactly at 15:00, a 5th versus 6th attempt) and idempotency are all covered above.

## 12. Slices (each reviewed before its tests run, per the owner's rule)
1. Schema, migration and data step, with S12 and S15.
2. Setup token CLI, setup route, removal of register and bootstrap: S1 to S7.
3. Forced-change gate, change-password route and whoami fields: S10.
4. Combined create, legacy-login, removal of profile-only create and linking, Deployment Administrator exclusion, Practice Owner routes: S8, S9, S11, S16.
5. The startup split (`AlvearaApplication`, no behaviour change), the TestSupport library and the TestHost executable, guards **G1 to G8** (S13; G7 and G8 are the loopback-only binding and the runners' explicit loopback URLs); then the **harness migration**: backend tests to `WebApplicationFactory<TestHost.Program>`, the 77+59 e2e files, and the runners started on the TestHost.
6. Client: setup page, change-password page, Add staff member, notices, Security Administration labels: vitest plus S14.

## 13. Test cost (owner's 5-minute rule)
- Slices 1 to 4: focused backend groups, each expected to take under 5 minutes, run without asking.
- **Slice 5 touches every e2e spec.** Proving the harness migration needs the walkthrough suites, which take far longer than 5 minutes. I will **ask before** any such run and propose a sample (the auth, permission-policy and two domain walkthroughs) for the focused proof, deferring the full set to ALV-N028's regression.
- No Stage 2 or full-suite run without explicit approval.

## 14. Questions resolved by review R01
1. **Profile-only staff creation:** removed for new staff (CR-1, sections 4 and 5).
2. **Administrator password reset:** unchanged. The existing reset-complete flow does not set `MustChangePassword`; the recipient chooses their own password.
3. **Several profile-less admins:** path 1 (CR-3, section 3).
4. **Test-support isolation:** a separate TestHost executable (CR-2, section 8).
5. **Dashboard notice wording:** directionally approved, subject to copy-editing. It stays advisory: manual configuration remains available, and the wizard is reserved for the Practice Owner.

## 15. R01 corrections: where each is met
| Correction | Sections | Tests |
|---|---|---|
| CR-1: no new profile-only staff; legacy remediation path; account-only exceptions limited | 2, 4, 5, 9 | S16, S9 |
| CR-2: a structurally separate test host; the product neither references nor recognizes test support | 8 | S13 (G1 to G8) |
| CR-3: the several-admins path specified (path 1), no silent choice | 3, 4 | S12 |
| CR-4: the `MustChangePassword` contract; lost temporary password recovered by the existing reset, never shown again | 3, 6, 10 | S17 |
| CR-5: TestHost loopback-only; runners use explicit loopback URLs | 8 | S13 (G7, G8) |

## 16. R05 addendum: setup mode and controlled restart (PD-38)

### 16.1 Owner rulings (2026-11-24)
PD-38 (`docs/design/OWNER_REQUEST_FIRST_RUN_SETUP.md`) asked that a person never has to run a database script. The owner ruled:
- **Q2, who creates the database:** the **installer or upgrade tool only, run automatically**. It creates the database, enables `READ_COMMITTED_SNAPSHOT` and `ALLOW_SNAPSHOT_ISOLATION`, applies the migrations, creates and configures the least-privilege API database identity, and starts the application with only that identity. **The running API never holds server-level SQL rights and never keeps a setup SQL login.**
- **Q4, migrations on upgrade:** **detect and refuse.** The API never auto-migrates a live database. With pending migrations, normal routes and background services stay off and the application shows an upgrade-required state. The installer or upgrade command takes the backup checkpoint and applies the migrations, and normal start-up follows only after that succeeds.
- **Q3, setup mode:** **yes, with a controlled restart after completion.** Setup mode exists **only** when the database is present and current but no Deployment Administrator exists. A missing database or pending migrations is **not** setup mode; those are installation, repair and upgrade states that belong to the installer.
- **Placement:** ALV-N030 stays focused on account bootstrap. Database provisioning, the upgrade command and its backup checkpoint belong to the deployment and installer architecture, **ALV-N013**.

### 16.2 What ALV-N030 builds (API side only)
**Start-up readiness check.** Before the host is built, one read decides the host's **mode** for the life of the process. The read has an explicit 15 s timeout and up to 3 retries with backoff, on connection-level failures only.

| Condition | Mode | What answers |
|---|---|---|
| Database unreachable or does not exist | `NotInstalled` | Every **API** route (`/api/*`) answers **503** `{error:"installation_incomplete"}`. Static client files still load (see 16.2a). The client shows "DentalKeep is not installed yet. Run the installer." No hosted service is registered. |
| Database exists, and EF reports pending migrations (`GetPendingMigrations`, read-only) | `UpgradeRequired` | Every **API** route answers **503** `{error:"upgrade_required"}`. Static client files still load. The client shows "An upgrade is waiting. An administrator must run the upgrade tool." No hosted service is registered. **The API never calls `Migrate()`.** |
| Current schema, no Deployment Administrator, **and** no `BootstrapState` | `Setup` | Only `GET /api/setup/status`, `POST /api/setup/deployment-administrator`, `GET /api/auth/csrf-token` and the static client files. Every other API route answers **503** `{error:"setup_required"}`. **No hosted service is registered**: notifications, follow-ups, the backup scheduler, background jobs, audit expiry, the policy drain, the invariant monitor and the rest. |
| Current schema, and set up | `Normal` | Today's behaviour, unchanged |

- The mode is chosen **before** `builder.Build()`, and hosted services and routes are registered for that mode only. Nothing switches inside a running process, which is the owner's reason for the restart.
- `NotInstalled` and `UpgradeRequired` only make the API refuse to run without the installer. ALV-N013 builds what resolves them (provisioning, the upgrade command, the backup). The API needs the check anyway to tell setup mode apart from a broken install.

**16.2a Client files and the status route in every mode (CR-7).**
- **Static client files and the single-page-app fallback** (every non-`/api` path serves `index.html`) are mapped **in all four modes**. They hold no data, so the browser can always load the app and show the right screen. Only `/api/*` routes are subject to the mode's 503.
- **`GET /api/setup/status`** is mapped deliberately **in all four modes**, anonymous, rate-limited, never cached (`Cache-Control: no-store`). Its whole body is one of:

| Mode | Status | Body |
|---|---|---|
| `NotInstalled` | 503 | `{ "mode": "installation_incomplete" }` |
| `UpgradeRequired` | 503 | `{ "mode": "upgrade_required" }` |
| `Setup` | 200 | `{ "mode": "setup", "setupAvailable": true }` |
| `Normal` | 200 | `{ "mode": "normal", "setupAvailable": false }` |

  - Nothing else is ever in it: no database name, server, connection detail, migration names or counts, versions or credentials. A test asserts the exact key sets.
- **On start, the client calls the status route first** and renders one of three screens (installation incomplete, upgrade required, or setup) before any sign-in. The `/api/*` 503 bodies elsewhere carry only `error`, with no detail.

**16.2b Controlled restart: mechanism and supervisor handoff (CR-8).**
- **Restart request.** After a successful setup, the route registers `HttpContext.Response.OnCompleted(...)`, so it fires only once the 201 response has been sent. That callback:
  1. sets the process-level restart request: `Environment.ExitCode = 75` (the constant `StartupMode.RestartRequestedExitCode`);
  2. then calls `IHostApplicationLifetime.StopApplication()`.
- **How the code reaches the process.** `Program` ends with `await app.RunAsync();` and returns nothing, so .NET exits the process with `Environment.ExitCode`. The TestHost's `Program` does the same. A guard test asserts both entry points keep that shape: a `Main` that returns its own value would silently drop the code.
- **A failed setup** never registers the callback: the exit code stays 0 and the host keeps running.
- **Propagation:**
  - **TestHost:** the process exits with 75. The two runners watch for exit 75, restart it on the same loopback URL and database, and treat any other exit as a failure of the run.
  - **`Start-DevApi.ps1`:** runs the API in a loop, restarting **only** on exit code 75 and stopping on any other code, and passing that code on as its own exit code.
  - **Production (ALV-N013):** see 16.5.

**Controlled restart.**
- After `POST /api/setup/deployment-administrator` commits and its 201 response has been sent, the process exits with code **75** ("restart requested") through the mechanism in 16.2b. Shutdown is graceful, and there is no work in flight because no hosted service ran.
- The process supervisor starts it again, and it comes up `Normal`:
  - in production, the Windows service recovery that ALV-N013's installer configures;
  - in dev, a restart loop in `Start-DevApi.ps1`, only on code 75;
  - in tests, the TestHost harness and the runners restart it.
- The client's setup page shows "Setup is complete. DentalKeep is restarting…" and checks `GET /api/setup/status` every 2 s until it answers `setupAvailable: false`, then goes to sign-in. After 120 s it gives up with "DentalKeep did not come back. Check that the service is running."
- If nothing restarts the process, it has simply stopped, so nothing runs in a half state. The next manual start comes up `Normal`, because setup completion is recorded in the database.

**Logging.** Each start-up logs its decision once, as a structured `event=startup_mode` line with the mode and the pending-migration count. It never includes the connection string or credentials.

### 16.3 Failure-first answers
- **The setup transaction fails:** nothing commits, there is no restart, and the API stays in `Setup`. The token stays valid until it expires.
- **The commit succeeds but the response is lost before the restart:** the restarted API comes up `Normal`. The client's check sees `setupAvailable: false` and goes to sign-in, and the operator signs in with the password they just chose.
- **The database becomes unreachable after start-up:** the mode doesn't change. Today's per-request error handling applies; the mode is decided only at start-up.
- **A migration is added but not applied** (an upgrade done without the tool): the API starts `UpgradeRequired`, refuses, and never touches the data.

### 16.4 Tests added (rows for the section 11 matrix)
| # | Test | Layer |
|---|---|---|
| S18 | Mode decision: no database gives `NotInstalled`. A pending migration (a test database migrated to the second-to-last migration) gives `UpgradeRequired`, and **the migration is still pending afterwards** (the API never migrates). A current schema with no Deployment Administrator gives `Setup`. A set-up install gives `Normal`. | API, DB |
| S19 | In `Setup`, `NotInstalled` and `UpgradeRequired`:<ul><li>the built host has no hosted services;</li><li>a sample of 10 normal API routes answers 503 with that mode's error;</li><li>`/` and a deep link such as `/patients/x` serve the client's `index.html` (200);</li><li>`GET /api/setup/status` answers exactly the 16.2a body for each of the four modes, with exact key sets, no detail, and `no-store`;</li><li>in `Setup`, the setup route works.</li></ul> | API |
| S20 | Controlled restart, run as a **real TestHost process** (not in-memory):<ul><li>a successful setup returns 201 to the client **first**, then the process exits with code exactly **75**;</li><li>a failed setup (wrong token) returns 400 and the process is still running 5 s later, with no restart request;</li><li>a new process on the same database starts `Normal`;</li><li>a guard test keeps both `Program` entry points returning nothing.</li></ul> | API, process |
| S21 | Browser, **all three rendered states**:<ul><li>**installation incomplete:** the TestHost pointed at a database that does not exist; the screen shows the installer message and no sign-in;</li><li>**upgrade required:** a database one migration behind; the upgrade message shows;</li><li>**setup, restarting to normal:** setup page, then "restarting…", then sign-in, with the runner restarting the TestHost on code 75.</li></ul>Against an unreachable server, the readiness check gives up within its timeout and retry bound. | Playwright, API |
| S22 | `Start-DevApi.ps1` restart loop: a stub that exits 75 once and then 0 is restarted exactly once; exit 1 is not restarted; the script's own exit code is the last one | script |

### 16.5 Slice and scope
- **Slice 7** (after slice 6): the readiness check, mode-based registration, the restart, the dev script's restart loop and the client's restart page; then tests S18 to S22.
- **Owned by ALV-N013** (recorded there, not built here): database creation and its snapshot options, applying migrations, the least-privilege identity, the upgrade command with its backup checkpoint, and calling the setup-token command at install.
- **ALV-N013 dependency contract (CR-8).** ALV-N013 configures the production supervisor to **restart the service on the controlled exit code 75**. Other exits follow its own deployment recovery policy: code 0 is a deliberate stop with no restart; any other non-zero code is a crash, with a bounded restart count and an alert.
  - Windows service recovery actions cannot tell exit codes apart and, by default, ignore a clean non-zero exit. So ALV-N013 must state its mechanism, for example a small service host or wrapper that reads the code, or recovery actions with "failure actions on non-crash failures" enabled plus a recovery policy that is safe for any code.
  - **Required ALV-N013 proof:** on the installed Windows service, a setup completed through the page leads to an automatic restart into `Normal` mode with no operator step. An injected non-75 exit follows the crash policy and does not count as a controlled restart.
  - ALV-N030 delivers the exit code and its tests (S20 to S22). The production-supervisor proof is ALV-N013's acceptance.
- **Dev:** `Initialize-DevDatabase.ps1` keeps provisioning AlveraDev as the dev stand-in for the installer, unchanged.

## 17. R07 addendum: test-support hosting on ChainHost (owner-requested, 2026-11-24)

**Why this addendum.** Slice 5 found that section 8 rests on a wrong premise. It assumed the walkthrough and chain runners start the product API, so it planned a split of the product's start-up (`AlvearaApplication.CreateBuilder` / `Configure`) and a new `Alveara.Api.TestHost` executable. In fact both runners already start a separate test executable, **`src/Alveara.Api.ChainHost`** (ALV-N019 R02, decisions O-1 and **PD-32**: "the UNCHANGED production application ... Program.cs untouched"). ChainHost hosts the product through `WebApplicationFactory<Program>` on a real Kestrel port and adds its test-only routes (clock mode, clock advance) through startup filters. The runners start the product API itself only in their "real clock" mode. Following section 8 literally would break PD-32 and leave two test hosts that drift apart.

**Owner ruling (2026-11-24):** extend ChainHost; build **no** second TestHost executable; review this addendum before any slice 5 code. Non-negotiable boundaries, as the owner stated them:
1. `Alveara.Api` remains unchanged and contains no test-support route, setting, or start-up branch.
2. `Alveara.Api.TestSupport` is created as a test-only library.
3. Only `Alveara.Api.ChainHost` and backend-test infrastructure may reference it.
4. ChainHost refuses to enable the route unless **all** are true: test environment, explicit `TestSupport:Enabled`, and loopback binding.
5. The account-creation route must not be reachable in a normal API process or a non-loopback deployment.
6. ChainHost's real-clock mode is explicit and used where the walkthrough needs real time; the existing injected-clock behaviour stays available for chain scenarios that need it.
7. G1 to G8 are retargeted so they prove these boundaries, including negative checks against the production application.

**What this supersedes.** Section 17 replaces section 8 (hosting, guards, harness migration) and the "TestHost" wording in section 12 (slice 5) and section 16 (S20, S21, 16.5 "Dev"). The test-only route's **contract** (section 8, "The route") is unchanged. Everything else stands.

### 17.1 Projects and references
| Project | Kind | References | Holds |
|---|---|---|---|
| `Alveara.Api` | product executable | no test project | **Unchanged by slice 5.** No test-support code, setting, argument or branch; `Program.cs` is not split. |
| `Alveara.Api.TestSupport` (new) | class library | `Alveara.Api` | `TestAccountsController` (`POST /api/test-support/accounts`), its service, and `TestSupportRegistration`, the single place that decides whether the route is mapped (17.2). |
| `Alveara.Api.ChainHost` | test executable (existing) | `Alveara.Api`, `Alveara.Api.TestSupport` | Gains the TestSupport application part through `TestSupportRegistration`, and an explicit clock mode (17.3). |
| `Alveara.Api.Tests` | test project (existing) | `Alveara.Api`, `Alveara.Api.ChainHost`, `Alveara.Api.TestSupport` | Guard tests G1 to G8 and the backend tests (17.4). |

No other project may reference `Alveara.Api.TestSupport`; G2 scans every `.csproj`.

### 17.2 When the route exists (boundaries 4 and 5)
`TestSupportRegistration` adds the TestSupport assembly as an MVC application part **only if all three conditions hold**. Otherwise it adds nothing, so `POST /api/test-support/accounts` is an unmapped path (404, or 405 from the GET-only SPA fallback).
1. **Test environment:** the host environment name is exactly `Test`.
2. **Explicit opt-in:** `TestSupport:Enabled` is `true`. The runners set `TestSupport__Enabled=true`.
3. **Loopback binding:** every configured listen address has the host `127.0.0.1`, `::1` or `localhost`. The addresses checked are `urls`, `ASPNETCORE_URLS`, `Kestrel:Endpoints:*:Url`, `--urls` and ChainHost's own `CHAIN_URL`.
   - `0.0.0.0`, `*`, `+`, `[::]`, a LAN address or another host name fail the check.
   - In-memory `TestServer` (the backend tests) has no socket, so it passes.

**Start-up refusal.** ChainHost does not run half-configured:
- with `TestSupport:Enabled=true`, if condition 1 or 3 fails, ChainHost **refuses to start**: non-zero exit and a clear message naming the failed condition;
- if `TestSupport:Enabled` is absent or false, ChainHost starts as it does today, without the route;
- ChainHost binds `CHAIN_URL` itself. Its default changes from `http://localhost:5072` to **`http://127.0.0.1:5072`**, and it refuses a non-loopback `CHAIN_URL` in every case, because it is a test host (boundary 5).

**The `Test` environment and the product's environment-dependent settings.**
- **The two settings:** the product has exactly two (`Program.cs:315` and `:428`, both `IsDevelopment()`):
  - the sign-in cookie's `SecurePolicy` is `SameAsRequest` only in Development (`Always` otherwise);
  - the OpenAPI document is mapped only in Development.
- **What ChainHost does under `Test`** (it runs as `Development` today):
  - it **post-configures** the cookie options to `SameAsRequest`, because it serves plain HTTP on loopback; without this no session cookie would be sent back. This is a ChainHost registration, not a product change;
  - the OpenAPI document is not mapped. G6 then holds trivially; it is also checked against the product in Development, where it is mapped;
  - ChainHost sets the logging levels of `appsettings.Development.json` for `Test`, so walkthrough output stays as quiet as today.
- **Nothing else changes:** no other product code reads the environment name. Survey of `IsDevelopment`, `IsEnvironment`, `IsProduction` and `EnvironmentName` in `src/Alveara.Api`, at slice 5.

### 17.3 Clock modes (boundary 6)
- **`CHAIN_CLOCK_MODE` is required** and is `injected` or `real`. A missing or other value exits 2 with a message.
  - **`injected`** keeps today's behaviour exactly: the `ChainPracticeClock`, the clock-mode report, and the clock-advance route behind `CHAIN_CONTROL_TOKEN`.
  - **`real`** swaps nothing: the product's own `IPracticeClock` runs, the clock-advance route is not mapped, and the clock-mode report says `real`.
- **Runners:** `Invoke-RealBackendWalkthrough.ps1` and `Invoke-AccumulatedChain.ps1` start **ChainHost in both of their modes**; today their real-clock mode starts the product API.
  - They pass `CHAIN_CLOCK_MODE` from their existing `-ClockMode` parameter, plus `ASPNETCORE_ENVIRONMENT=Test`, `TestSupport__Enabled=true` and `CHAIN_URL=http://127.0.0.1:<port>`.
  - They stop setting `AdminBootstrapSecret`.
  - The Vite proxy targets `http://127.0.0.1:<port>`.
- **Dev and production** (`Start-DevApi.ps1`, publish, the future installer) run only `Alveara.Api`. Nothing changes there.

### 17.4 Harness migration
- **Backend tests** create accounts **directly in the test database** through `IdentityTestHelpers`, as slices 2 to 4 already do, not through the test-only route.
  - They keep `WebApplicationFactory<Alveara.Api.Program>`. The route is exercised only by the guard tests (G4) and the browser runners.
  - Fixtures that need a profile linked to an account write both rows through the helper.
  - Once no test uses them, these internal leftovers are **deleted**: `AccountService.RegisterAsync` and `BootstrapFirstAdminAsync`, `LegacyAccountRequests.cs`, the profile-only `StaffProviderService.CreateStaffAsync`, and the link-editing `UpdateStaffAsync`.
  - Tests whose subject was sign-up, the bootstrap secret or the link picker are removed or rewritten to prove the removal (S1, S16).
- **Browser specs** use a new shared `e2e/support/testAccounts.ts`. The survey at slice 5 found 49 files that call `register`, 48 that call `bootstrap-admin` and 33 that call the removed `POST /api/config/staff`; section 8's 77 and 59 counted occurrences, not files.
  - `createAccount(request, { username, password, role, kind?, withStaffProfile?, mustChangePassword? })` calls `POST /api/test-support/accounts`.
  - Staff fixtures that used the profile-only create call the **product** route `POST /api/config/staff-members` through `addStaffMember(...)`. They then exercise the real create; the spec ignores the login it makes.
  - The edit is mechanical: flows and scenarios stay as they are.
  - `auth-real-backend.spec.ts` is rewritten as section 8 said: sign-up is 404, and setup and the forced change work.
- **Chain scenarios** (`chain/`) get the same helper.

### 17.5 Guards, retargeted (boundary 7)
| # | Proof | Layer |
|---|---|---|
| G1 | The **product** host, `WebApplicationFactory<Alveara.Api.Program>`, under `Development`, `Production` and `Test`, with `TestSupport:Enabled=true` and the argument `--test-support`, answers 404 or 405 on `POST /api/test-support/accounts` and creates no account. | API |
| G2 | Project-file scan: `Alveara.Api.csproj` references no test project, and `Alveara.Api.TestSupport` is referenced only by `Alveara.Api.ChainHost.csproj` and `Alveara.Api.Tests.csproj`. A repository scan finds no `TestSupport` type or namespace in `src/Alveara.Api`. | build, repo scan |
| G3 | `dotnet publish src/Alveara.Api` output contains no `Alveara.Api.TestSupport.dll` and no `Alveara.Api.ChainHost.dll`. | build |
| G4 | ChainHost's `TestSupportRegistration` maps the route only when all three conditions hold; each of the 7 other combinations gives 404 or 405 and no account. With `Enabled=true` and an environment other than `Test`, ChainHost refuses to start. Under all three, the route creates exactly the requested account. | API |
| G5 | Repository scan: `TestSupport__Enabled` and `Alveara.Api.ChainHost` appear only in the two runners, the tests and ChainHost itself. They never appear in `Start-DevApi.ps1`, `New-DevAdmin.ps1`, deployment or installer scripts, the product's `appsettings*.json`, or any Docker or publish file. | repo scan |
| G6 | The product's OpenAPI document (Development) does not list `/api/test-support/*`. | API |
| G7 | Loopback. ChainHost refuses to start (non-zero exit, message) when `CHAIN_URL`, `--urls` or `ASPNETCORE_URLS` is `0.0.0.0`, `*`, `+`, `[::]`, a LAN address or a host name. It starts with `127.0.0.1`, `::1` or `localhost`, and the default `CHAIN_URL` is `127.0.0.1`. Every form is checked through the decision function, plus one real process start with `0.0.0.0` that must exit non-zero. | API, process |
| G8 | Script test: both runners start ChainHost with `ASPNETCORE_ENVIRONMENT=Test`, `TestSupport__Enabled=true`, an explicit `CHAIN_CLOCK_MODE` and a `127.0.0.1` `CHAIN_URL`, and the Vite proxy targets the same address. Neither runner starts `Alveara.Api.dll` any more or sets `AdminBootstrapSecret`. | script |

### 17.6 Section 16 retargeting
- S20 and S21 run against a **ChainHost** process with `CHAIN_CLOCK_MODE=real`, not a TestHost.
- Setup mode (slice 7) is decided by the **product's** start-up code, so ChainHost exercises it unchanged.
- The restart on exit code 75 stays the runner's job, as before.

### 17.7 Test cost and slice order
Slice 5 is split so that each step ends green on a focused group under 5 minutes:
- **5a:** `Alveara.Api.TestSupport`, the ChainHost registration and clock modes, and guards G1 to G7. G3 runs one `dotnet publish`, about 1 minute.
- **5b:** the backend test migration, one group of files at a time; each group's tests run as it moves. Then the internal leftovers are deleted.
- **5c:** `testAccounts.ts`, the spec and chain edits, both runners, and G8. Proving 5c needs walkthrough suites that take longer than 5 minutes. As section 13 already says, I will **ask before** that run and propose a sample (the auth, permission-policy and two domain walkthroughs), with the full set deferred to ALV-N028's regression.

### 17.8 Failure-first answers
- **Route enabled by mistake in a shared environment:** it needs three independent conditions plus a loopback-only binding, and the product cannot map it at all (G1 to G3).
- **ChainHost started with a wildcard address:** it refuses to start (G7).
- **Cookie policy under `Test` forgotten:** every walkthrough sign-in fails at once, so the fault is loud, not silent. A ChainHost test also asserts that the cookie is issued over HTTP under `Test`.
- **A test still uses a removed route:** it fails with 404 or 405 in its focused group. Slice 5b's per-group runs catch it before the leftovers are deleted.

## 18. R08 addendum: ALV-N030-C01, the Practice Owner authorization invariant, ownership transfer and Deployment Administrator recovery (U-049, owner-requested security correction, 2026-11-25)

**Why this addendum.** The owner's manual walkthrough of the completed ALV-N030 (2026-11-24; findings U-043 to U-049, commit `50daabe`) designated a Practice Owner with the Dentist role. That owner could not configure the practice. Sections 1 to 17 allow any role:
- section 5 gives the Practice Owner "no extra permissions in ACCT";
- section 2 leaves "a Practice Owner permission set" out of scope;
- D-4 leaves transfer out of scope.

The owner ruled that this is a **security/authorization defect**: the Practice Owner authorization invariant is not enforced. It is corrected as **ALV-N030-C01** (owner, 2026-11-25), under this addendum and an independent review, **before ALV-N027 begins**. ALV-N030 stays COMPLETE (review R02) and is not reopened. Refusing every path that breaks the invariant makes **ownership transfer** necessary, so the owner moved transfer into scope. Transfer is reserved to the Deployment Administrator, so the addendum also defines **recovery** for an installation without a usable Deployment Administrator.

**Surveyed tree:** `d7b8f9b` (read-only). This text replaces the first R08 draft of section 18 (same commit series, never reviewed).

**R10 changes** (review `.alveara/reviews/ACCT/BRIEFING-R09.md`, CHANGES_REQUIRED; it closed R08-1 to R08-4, and the resolved decisions are not reopened):
- **R09-1:** the initial-designation actor check is authoritative **inside** the serializable transaction, under a documented lock order (18.3a, E1a, S11b).
- **R09-2:** a transfer carries `expectedDesignationId`, and a stale request is refused with 409 `owner_changed` (18.5, S35).

**R09 changes** (review `.alveara/reviews/ACCT/BRIEFING-R08.md`, CHANGES_REQUIRED; its "Decisions confirmed" stand unchanged):
- **R08-1:** the initial designation now requires the **acting** account to have role `Admin` (18.3 E1a, S11b).
- **R08-2:** the checkout rule admits `Admin` **alongside** Dentist and Hygienist, always with the active booked-provider link; other roles stay refused (18.9, S29).
- **R08-3:** the migration audit row has a deterministic id and an exact `NOT EXISTS` predicate (18.7, S30).
- **R08-4:** no owner walkthrough is required for C01. Acceptance is focused automated tests. The C02 targeted real-authenticator walkthrough stays required before enforcement (18.6a, 18.11, 18.13).

### 18.1 Owner decisions (2026-11-25), as stated
**U-049, option (b):**
1. The initial Practice Owner must have the **Administrator** account role. This is the permanent authorization model, not a temporary measure pending the setup wizard.
2. PracticeOwner stays a protected designation: **not a role and not an independent permission source**. No owner-specific permission overlay.
3. A Practice Owner remains a complete linked user:
   - a login account with the Administrator role;
   - a staff profile with the person's real job title and credentials (credential fields arrive with U-048; C01 adds none);
   - a provider profile where applicable;
   - the Practice Owner designation.
4. Selecting "Make this person the Practice Owner" in the initial-owner creation flow presets the account role to Administrator, explains why, and prevents submission with any other role.
5. **The server** enforces that a designated Practice Owner has an active linked staff profile, an active login account and the Administrator role. Client validation alone is insufficient.
6. Existing pre-release data with a non-Administrator Practice Owner, including the walkthrough's Maya record, is handled explicitly and disclosed. An invalid owner configuration is never left silently.
7. Provider eligibility is based on the staff/provider record and appropriate professional classification, **not on the login role**. An owner may have an Administrator account while remaining a Dentist staff member and provider.
8. The future guided setup wizard is available only to the designated Practice Owner and uses the normal permission checks. No second owner-only permission system.
9. The Deployment Administrator remains account-only and administratively capable. The Practice Owner is the linked staff user expected to configure and operate the practice.
10. U-049 records the failure to enforce this invariant; it is resolved before ALV-N027 begins.

**Briefing questions:**
- **Q1 (b).** Disabling the active Practice Owner's login is **refused** with `owner_transfer_required` and "Transfer the Practice Owner designation before disabling this account." The same rule applies to a role downgrade and to staff-profile inactivation. **Ownership transfer is added to ALV-N030-C01**; it can no longer stay out of scope.
- **Transfer requirements:**
  - authorized actor: the Deployment Administrator only;
  - destination: an active linked staff member with an enabled Administrator account;
  - source and destination cannot be the same;
  - end the old designation and create the new one in one transaction;
  - never expose a state with zero or two active owners;
  - require recent authentication, confirmation and a reason;
  - revoke the former owner's open sessions;
  - audit actor, former owner id, new owner id, reason and time, with no names in the audit details;
  - include an atomic transfer-and-disable-former-owner option;
  - protect against concurrent transfers;
  - define recovery for installations that currently have no Deployment Administrator.
- **Q2 (a).** The U-046 dashboard panel is **out of C01** and stays with the other UI improvements. C01 changes only the atomic server operation and the Staff tab.
- **Q3 (a).** Server checks and tests, enforced inside transactions with appropriate locks and concurrency tests. **No new cross-table trigger.**
- **Q4.** The story id is **ALV-N030-C01**.

**Q5 to Q8 (owner, 2026-11-25), as stated:**
- **Q5:** Require password and MFA when MFA is enabled. Ownership transfer is a highly privileged online action. If MFA is configured, bypassing it would weaken its purpose. If the MFA device is lost, use the separate console recovery procedure from Q6.
- **Q6:** (a) Turn MFA off through recovery. The recovery must revoke all sessions and recovery codes, rotate the security stamp, require MFA re-enrollment at next sign-in, use a short-lived one-time console token, and create an audit event without recording the token.
- **Q7:** (a) IDs only. Remove the person's name from stored audit text. Resolve display names only when authorized users read the audit log.
- **Q8:** (a) Keep the Administrator role. Transfer changes only the designation. Role changes remain a separate audited operation. The atomic emergency option may transfer ownership and disable the former owner, but it should not change their role.

**Execution order and MFA enforcement (owner, 2026-11-25, after the owner's manual MFA test; finding U-050), as stated:**
1. Complete ALV-N030-C01 without mandatory MFA enrollment.
2. Immediately create ALV-N030-C02: MFA QR enrollment and Deployment Administrator enforcement.
3. Complete C02 before starting ALV-N027 or other planned feature development.

C02 has two controlled stages:
- **Stage 1:** implement the local QR provisioning flow, manual-key fallback, Copy control, security protections, and automated/browser tests.
- **Stage 2:** after Stage 1 is proven, activate mandatory MFA enrollment for newly created or recovered Deployment Administrators. The enforcement must not be enabled merely because the QR code has been coded. The QR must first be shown to contain a valid otpauth:// payload and successfully enroll an authenticator; this can be a short targeted check, not another full manual E2E run.

**Consequence for C01:** the Q6 requirement "require MFA re-enrollment at next sign-in", and Q6b (enforcement for a Deployment Administrator created by recovery), move to **C02 Stage 2**. C01 recovery still turns MFA off, revokes all sessions and recovery codes, rotates the security stamp, and audits without the token; it does **not** add the enrolment gate (18.6a).

**U-046 (2026-11-24):** owner creation is a **single server call** that creates the staff member with their login **and** designates them, atomically, both or neither.

### 18.2 What changes in sections 1 to 17
| Section | Was | Becomes |
|---|---|---|
| 1 (D-4), 2, 5 | Practice Owner: no extra permissions; transfer out of scope; "no route ends or changes a designation" | Still no owner permission source. **The owner's login must hold the Administrator role** (18.3). **Transfer is in scope** (18.5); it is the only route that ends a designation. |
| 3 (schema) | `PracticeOwnerDesignationMethod`: `InitialByDeploymentAdministrator`, `UpgradeTransition`; `SetupTokens` serves first-run setup only | Adds method **`Transfer`**. `SetupTokens` gains **`Purpose`** (`Setup` = 0, the default, so existing rows keep their meaning; `Recovery` = 1) (18.6). No other table changes. Trigger 51311 is unchanged, because transfer only sets `EndedAtUtc` on the old row, which it already permits. |
| 4 (routes) | owner creation = two client calls | `POST /api/config/staff-members` accepts `practiceOwner: true` (18.4); **new** `POST /api/practice-owner/transfer` (18.5); **new** recovery route and CLI command (18.6) |
| 6 (service rules) | owner target: active staff with a linked, enabled `Staff` account | adds **role = Admin**, checked in the designating transaction (18.3) |
| 9 (client) | owner checkbox on "Add staff member" | the checkbox presets and locks the role; plus the transfer dialog and the recovery page (18.8) |
| 11 (matrix) | S11 | S11 extended, plus S23 to S38 (18.10) |
| 16 (startup modes) | the setup routes only in Setup mode | the recovery routes are answered in **Normal** mode only (18.6) |
| 7 (audit) | `PracticeOwnerDesignated` text names the person | ids only; names resolved when the audit log is read (18.10, Q7) |

Everything else stands.

### 18.3 The invariant and where the server enforces it
**Owner invariant (OI):** the holder of the active designation has:
- an **active** staff profile;
- a **linked** account of kind `Staff`;
- that account **enabled**;
- that account's role **`Admin`**.

| # | Path | Current behaviour (`d7b8f9b`) | Rule in C01 |
|---|---|---|---|
| E1 | `PracticeOwnerService.DesignateInitialAsync` (existing staff) | checks active, linked, enabled, kind `Staff`; any role | adds role `Admin`; otherwise 409 **`owner_must_be_administrator`**. The target account is read with `UPDLOCK` inside the existing serializable transaction, so a concurrent role change waits. |
| E1a | **Actor** of `POST /api/practice-owner/initial` (R08-1) | the route requires the `ManageUsers` permission only, which a non-Administrator can hold through the matrix or an override | the acting account must itself have role **`Admin`**, be enabled, and (as today) hold `ManageUsers`. This covers the Deployment Administrator and ordinary Administrators. Anyone else → 403 **`owner_designation_requires_administrator`**, audited through `RecordRefusalAsync`, nothing written. **R09-1, the authoritative check is inside the transaction:** a preliminary read before the transaction may refuse early, but it is not the control. Inside the serializable designation transaction, the actor's account row and the target's account row are locked in the order of 18.3a. Then the actor's role (`Admin`), enabled state and account kind (`Staff` or `DeploymentAdministrator`) are re-checked under that lock, from the stored row, not the session claim. A concurrent role downgrade or disable of the actor either commits first (the designation then sees it and is refused) or waits until the designation commits (the actor was eligible when it designated). An ineligible committed actor can never designate. 18.4's single call uses the same shared designation method, so it gets the same in-transaction actor check. |
| E2 | `StaffMemberService.CreateAsync` with `practiceOwner: true` (18.4) | n/a | role must be `Admin`, else 400 **`owner_must_be_administrator`** before anything is written. Creating an Administrator login is already limited to Administrators (403 `target_is_admin`). |
| E3 | `AccountService.ChangeRoleAsync` | any change; last-admin guard only | changing the **current owner's** account away from `Admin` → 409 **`owner_transfer_required`**, refusal audited, nothing changes |
| E4 | `StaffInactivationService.InactivateAsync` and `StaffProviderService.SetStaffActiveAsync` | the owner's profile can be inactivated (the first also disables the login) | inactivating the **owner's** profile → 409 **`owner_transfer_required`**, refusal audited, nothing changes. Unlink needs an inactive profile, so it is unreachable for the owner (S27 pins it). |
| E5 | `AccountService.SetAccountEnabledAsync` (disable) and `DisableForStaffInactivationAsync` | allowed | disabling the **current owner's** login → 409 **`owner_transfer_required`**, refusal audited, nothing changes. The way to remove a compromised owner at once is the **atomic transfer-and-disable** (18.5). |

- **Error body for E3 to E5:** `{error:"owner_transfer_required", message:"Transfer the Practice Owner designation before disabling this account."}`. The message is the owner's wording for disabling; for a role change or an inactivation the client says "changing this account's role" or "inactivating this staff member" in place of "disabling this account".
- **How each check works:** it runs inside the operation's existing transaction, after the row lock it already takes (`LoadLockedAsync` for accounts; the staff row for inactivation). The active designation row is read with `UPDLOCK, HOLDLOCK`, so a concurrent transfer (18.5) and the refused operation serialize: either the transfer commits first and the former owner is no longer protected, or the refusal stands.
- **Refusal audit rows:** the existing `RecordRefusalAsync` with rule `owner_transfer_required` (account operations), plus a configuration refusal row for E4.

### 18.3a Lock order for every owner-affecting operation (R09-1)
Every operation that can create, move or break the designation takes its locks in this order, inside its own transaction:
1. **The administrator guard** (`AdminAvailabilityGuard.AcquireAsync`: `sp_getapplock`, exclusive, owned by the transaction, 10 s timeout answering `AdminGuardBusy`).
2. **The designation rows:** the active designation, `UPDLOCK, HOLDLOCK` (for an initial designation, the serializable "never designated" range read).
3. **The account rows involved, in ascending `Id` order:** the actor, the target, the source and the destination, as applicable (`UPDLOCK`, the existing `LoadLockedAsync` pattern).
4. **The staff profile rows involved,** in ascending `Id` order.

**Operations covered:**
- the initial designation (E1, E1a);
- the single owner-creation call (18.4);
- transfer (18.5);
- role change (E3), staff inactivation (E4) and disable (E5);
- recovery's reset of the Deployment Administrator (18.6).

Role change, disable and inactivation already take the guard first. C01 adds it as the first lock of designation, single-call creation, transfer and recovery. Because the guard is one exclusive application lock taken first by all of them, no two of these operations hold row locks at the same time, so no deadlock cycle can form between them. The fixed row order covers any other reader that takes row locks.

### 18.4 The single owner-creation call (U-046 decision)
- **Route:** `POST /api/config/staff-members` gains an optional `practiceOwner` (boolean, default false). Permissions are unchanged: ManagePracticeConfiguration **and** ManageUsers, CSRF, Idempotency-Key.
- **With `practiceOwner: true`, in one serializable transaction:**
  1. Run the E2 check.
  2. Insert the profile and the `Staff` account (role `Admin`, `MustChangePassword = 1`), and link them.
  3. Insert the designation, with the same "never designated" check as `DesignateInitialAsync`, through one internal method extracted from `PracticeOwnerService` (shared, not copied).
  4. Write the audit rows: `StaffMemberCreated`, `TemporaryPasswordIssued`, `PracticeOwnerDesignated`.

  **Any failure rolls back everything.**
- **Idempotency:** a replay with the same key returns the same result, without the password.
- **Errors:**
  - an owner already designated: 409 `owner_already_designated`, nothing created;
  - a username or name already taken: the existing 409s, nothing created.
- **Client:** the Staff tab sends one call. The `addStaffMember` + `designatePracticeOwner` sequence and its partial-failure messages are removed. The U-046 panel is not part of C01 (Q2).

### 18.5 Ownership transfer
**Route:** `POST /api/practice-owner/transfer`, CSRF, Idempotency-Key required.
- **Body:**
  - `expectedDesignationId`: the id of the active designation the operator saw when the dialog loaded (R09-2);
  - `newOwnerStaffProfileId`;
  - `reason` (required, trimmed, 10 to 500 characters);
  - `confirm` (must be `true`);
  - `currentPassword` (step-up);
  - `mfaCode` (required when the actor has MFA on, Q5);
  - `disableFormerOwner` (boolean, default false).
- **Response:** 200, the new designation plus `formerOwnerDisabled`.

**Authorization:**
1. The caller is authenticated and its account is the **Deployment Administrator** (`AccountKind = DeploymentAdministrator`, enabled). Any other caller → **403 `transfer_not_permitted`**, whatever its role. This is a check on the kind of account, written in the route: not a permission in the matrix, not an override, and not delegable.
2. **Recent authentication (Q5):** both factors when MFA is on.
   - **Password:** the existing step-up `AccountService.ReauthenticateAsync` (ALV-N004, used by backup recovery) checks `currentPassword` through the login lockout boundary. A wrong password → 403 `reauthentication_failed`, audited by the existing `SensitiveActionStepUpFailed`.
   - **MFA code:** when the Deployment Administrator's account has `MfaEnabled`, the body must also carry `mfaCode`. It is checked with the same TOTP verification as the sign-in MFA challenge (`Totp.Verify` against the account's protected secret), and failures count through the same lockout boundary.
     - A missing code → 403 `mfa_required`.
     - A wrong code → 403 `reauthentication_failed`, audited as `MfaStepUpFailed`.
     - **A recovery code is not accepted here.** A lost authenticator goes through console recovery (18.6).
   - The step-up is checked **before** the transaction opens, so a failed step-up locks nothing.
3. `confirm` must be `true` → otherwise 400 `confirmation_required`.

**Rules,** checked in the transaction after the locks:

| Code | When |
|---|---|
| 409 `no_active_owner` | there is no active designation; use the initial designation instead |
| 409 **`owner_changed`** | (R09-2) the locked active designation's id is not `expectedDesignationId`: the owner changed since the dialog was loaded. Nothing is changed. The client reloads the current owner and asks the operator to confirm again. |
| 400 `transfer_same_owner` | the destination is the current owner |
| 409 `owner_must_be_administrator` | the destination breaks OI: not an active staff profile, no linked enabled `Staff` account, or not role `Admin` |
| 400 `validation_failed` | the reason is missing or outside 10 to 500 characters |

**One transaction** (serializable):
1. Take the locks in the 18.3a order: the administrator guard, then the active designation (`UPDLOCK, HOLDLOCK`), then the actor, former owner and destination accounts by ascending id, then their staff profiles.
2. **Stale-intent check (R09-2):** the locked active designation's `Id` must equal `expectedDesignationId`, otherwise 409 `owner_changed`. A second transfer confirmed against the same owner therefore never "proceeds from the new owner": it is refused, with no mutation.
3. Re-check every rule above on the locked rows, including the actor still being the enabled Deployment Administrator.
4. Set `EndedAtUtc` on the old designation, then insert the new one (`Method = Transfer`, `DesignatedByAccountId` = the Deployment Administrator). Both are in one transaction: the filtered unique index is checked per statement, the trigger permits only `EndedAtUtc`, and other readers (read-committed snapshot) see either the old owner or the new one. **Zero or two active owners are never visible.**
5. **Former owner's sessions:** rotate the security stamp and revoke open sessions (the existing `BumpSecurityStamp` + `AuditSessionStore.RevokeOpenAsync`, as on a role change), so their next request re-authenticates and sees the new state.
6. **If `disableFormerOwner`:** disable the former owner's account through the existing disable logic (last-administrator guard: the Deployment Administrator and the new owner are both Administrators, so it always passes; audit `AccountDisabled`). The former owner's **role is not changed** (Q8): they stay an Administrator. A role change remains a separate, audited operation, which E3 no longer blocks once they are not the owner. The emergency option only transfers and disables.
7. **Audit:** one `PracticeOwnerTransferred` row. The details carry **ids only**: actor account id, former owner staff profile id, new owner staff profile id, the reason, the UTC time, and `formerOwnerDisabled`. No names. The reason is the operator's own text; the dialog asks for it without patient or personal details (18.8).
8. Commit. On any failure nothing changes, including the disable and the session revocation, because all of it runs inside the same transaction.

**Idempotency:** the key is bound to (actor, key), and the stored **request fingerprint** covers `expectedDesignationId`, `newOwnerStaffProfileId`, `reason` and `disableFormerOwner` (never the password or MFA code). A replay with the same key and fingerprint returns the original result, even though the designation has changed since; that is the replay, not a new transfer. The same key with a different fingerprint → 409 `idempotency_key_reused`.

**What transfer does not do:**
- **Create the new owner.** They must already exist as an Administrator staff member: Staff tab, "Add staff member" with role Administrator.
- **Change any permission.** It moves only the designation; both people's access is their role's.

### 18.6 Deployment Administrator recovery
**When it is needed:** transfer needs a usable Deployment Administrator. An installation may have none:
- (R1) an upgrade with 0 or 2+ profile-less Administrators (section 3, path 1);
- (R2) its Deployment Administrator is disabled;
- (R3) its Deployment Administrator's password is lost, or the account is locked out.

**The design:** the same trust model as first-run setup. Whoever can run the installer's command on the server machine holds the authority. It reuses the setup-token machinery; no new authentication system.

- **CLI command** `Alveara.Api recovery issue-token`, the counterpart of `setup issue-token`. It works only once the installation is set up (Normal state); before setup it says to use first-run setup.
  - It issues a one-time recovery token: 32 random bytes, shown once on the console only; its SHA-256 is stored with `Purpose = Recovery`; it lasts 15 minutes and voids earlier unused recovery tokens.
  - The token is never logged and never audited.
  - Audit: `RecoveryTokenIssued`, with the expiry only.
- **Route** `POST /api/recovery/deployment-administrator`, unauthenticated. It is answered in **Normal** mode only; the startup-mode middleware keeps it closed in the other modes.
  - Rate-limited like the setup route: 5 per 5 minutes per address.
  - Every token failure gives the same 400 `recovery_failed`.
  - The five-failure void uses the same atomic counter as R02 CR-1.
  - Body: `{token, username, newPassword}`. The password must be at least 12 characters.
- **What it does** (one transaction):
  - **No Deployment Administrator exists (R1):** create one. Account-only, role `Admin`, `AccountKind = DeploymentAdministrator`, the chosen username and password, no forced change. Audit `DeploymentAdministratorRecovered` with method `Created`. The filtered unique index keeps it to one, even under a race.
  - **One exists (R2, R3):** the username must match it, or `recovery_failed` with the same body. Then:
    - set the new password;
    - enable the account;
    - clear the lockout;
    - rotate the security stamp and revoke its open sessions;
    - **(Q6)** if MFA is on: turn it off (clear the secret and any pending secret) and **revoke every MFA recovery code**. The re-enrolment requirement is **C02 Stage 2**, not C01 (18.6a).

    Audit `DeploymentAdministratorRecovered` with method `Reset`, ids only, plus whether MFA was turned off. **The token, the username tried and the password are never recorded.**
  - A **created** Deployment Administrator (R1) is not forced to enrol in C01; C02 Stage 2 covers newly created and recovered Deployment Administrators (18.6a).
- **What recovery never does:**
  - touch the Practice Owner, any staff account or any permission;
  - create a second Deployment Administrator;
  - work without the console token.
- **Client:** a plain recovery page at `/recovery`, with the same fields and messages as the setup page. It is linked from nowhere in the app; the CLI prints its address, as `setup issue-token` does.
- **Dev:** `scripts/New-DevRecoveryToken.ps1` (dev stand-in, like `New-DevAdmin.ps1`).

### 18.6a MFA enrolment enforcement: moved to ALV-N030-C02 (owner, 2026-11-25)
C01 adds **no** mandatory MFA enrolment, no `MustEnrollMfa` column and no enrolment gate. Mandatory enrolment for newly created or recovered Deployment Administrators is **ALV-N030-C02 Stage 2**. It is activated only after C02 Stage 1 (local QR provisioning, manual-key fallback, Copy control) is proven: the QR is shown to contain a valid `otpauth://` payload and to enrol an authenticator. C02 has its own brief and review. **Required C02 gate (R08-4):** after Stage 1's implementation and automated tests pass, a short targeted walkthrough with a real authenticator app, scanning the QR and completing enrolment, must pass before Stage 2's enforcement is enabled. C01 must leave nothing that C02 has to undo: recovery's MFA reset (18.6) is written so that C02 can add "and require enrolment" in the same transaction.

### 18.7 Existing data (decision 6)
**What exists today** (read-only check, 2026-11-25):
- **AlveraDev:** the ALV-N030 migration is not applied (84 migrations, no `PracticeOwnerDesignations` table), so it holds no owner.
- **The walkthrough database `AlveraManualN030`,** which held the Dentist-role owner "Dr. Maya Owner": dropped on 2026-11-25 at the owner's instruction.
- **Test databases** are made fresh per fixture. The single leftover `AlveraTest_b62446e8…` predates ALV-N030.
- **Deployments:** no release has shipped.

**Handling:**
1. **Migration data step,** in C01's migration. It finds an active designation whose holder breaks OI.
   - It **changes nothing**: no automatic promotion, because a migration must not grant authority.
   - It writes one `PracticeOwnerInvariantViolation` audit row (staff profile id, the failing conditions, actor "system (upgrade)").
   - **Idempotency (R08-3).** The row's primary key `AuditLogEntries.Id` is **deterministic**: the first 16 bytes of SHA-256 over the UTF-8 text
     `alveara.owner-invariant-violation:v1:{designationId:N}:{staffProfileId:N}:{staffIsActive}:{userAccountId:N or "none"}:{accountIsDisabled or "none"}:{accountRole or "none"}:{accountKind or "none"}`,
     where the booleans are `0` or `1`, the enum values are their names, and the ids use the lower-case `N` format. This is the **state fingerprint**: the designation and every OI input, nothing time-dependent.
   - **The insert** is a single SQL statement: `INSERT INTO [AuditLogEntries] (...) SELECT ... WHERE NOT EXISTS (SELECT 1 FROM [AuditLogEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @derivedId)`. The primary key is the backstop.
     - The **same state** executed again gives the same id, so no second row.
     - A **different** violation state (for example the role changed from Dentist to Billing, or the profile was inactivated) gives a new id, so a new row.
     - Earlier rows are never updated or deleted; audit history stays immutable.
   - The fingerprint text is not stored. The row's details hold the staff profile id and the failing condition names only (no names, Q7).
   - **How it runs:** the step is SQL in the migration, so it runs on `dotnet ef database update` and in the installer (ALV-N013). The same statement is exposed as one internal method, `OwnerInvariantAudit.RecordIfNewAsync(db, ct)`, so the test can run it twice on a seeded database without re-applying the migration. The migration calls the SQL; the test calls the method; both share one SQL string constant.
2. **Runtime report.** `GET /api/auth/me` gains `practiceOwnerStatus`: `none`, `valid` or `degraded`. For an Administrator, `degraded` shows a dashboard notice naming the failing condition and the fixes:
   - change the owner's role to Administrator (E3 refuses only changes **away** from Admin);
   - re-enable the account;
   - or have the Deployment Administrator transfer ownership (transfer from a degraded owner is allowed, because only the destination must meet OI).
3. **Disclosure:** the handoff states the Maya record and the empty environments above, with the commands used.

### 18.8 Client
- **Staff tab, "Add staff member":**
  - ticking **"Make this person the Practice Owner"** sets **Role** to **Administrator** and disables the selector;
  - hint: "The Practice Owner signs in with the Administrator role so they can configure and run the practice. Their job title and provider profile still describe their clinical work.";
  - one server call (18.4); the server is the control.
- **"Designate the Practice Owner"** (after an upgrade): non-Administrators are listed but disabled, with "Change this person's role to Administrator in Security Administration first."
- **Security Administration:**
  - **For the owner's account:** the role selector, Disable and (in Staff) Inactivate are disabled, with "This person is the Practice Owner. Transfer the Practice Owner designation first."; the server refuses regardless (E3 to E5).
  - **For the Deployment Administrator only,** a **"Transfer Practice Owner"** action opens a dialog with:
    - the current owner (the dialog keeps the active designation's id and sends it as `expectedDesignationId`);
    - a picker of eligible destinations (active Administrator staff members, the current owner excluded);
    - a reason box, with the hint "Do not include patient or personal details.";
    - "Also disable the former owner's login";
    - the current password;
    - a confirm checkbox: "I am transferring the Practice Owner designation to this person."

    Errors show in the dialog and keep the input. Nobody else sees the action.
- **Dashboard:** the `degraded` notice (18.7).
- **`/recovery`:** the recovery page (18.6).

### 18.9 Provider eligibility (decision 7)
**Survey result.** One server rule decides a clinical action by **login role**:
- `CheckoutRecorder.AuthorizeAsync`, outcome "Not clinically required", requires `role is Role.Dentist or Role.Hygienist` plus a link to the visit's provider. An owner with an Administrator login who is the booked dentist would be refused.

These need no change:
- `VisitMovePolicy` already admits `Admin` linked to the booked provider for "In treatment" and "Checked out";
- "Completed" admits any holder of UpdateVisitFlow;
- `PermissionProtection.RoleLimited` is a matrix rule for working roles, and Admin is fixed.

**Change (R08-2, narrow):**
- "Not clinically required" is allowed when **both**:
  - the actor's role is **Dentist, Hygienist or Admin** (C01 adds `Admin`);
  - the actor's account is linked to the visit's booked provider profile, and that profile is **active**.
- **Front Desk, Assistant, Billing and Office Manager stay refused,** even if a provider profile is (wrongly) linked to their login.
- Unlinked Dentists, Hygienists and Administrators stay refused.

**Why not drop the role part entirely:** `ProviderProfile.Specialty` is free text, and today a provider profile can be attached without proving a Dentist or Hygienist qualification. So the provider record cannot yet stand alone as the professional classification. **ALV-N027** may replace the role part only once it supplies, and proves, a controlled professional classification. Until then, decision 7 is met for the owner (an Administrator linked to their own provider profile) without widening the action to other roles. U-048 adds licence fields.

### 18.10 Audit events
| Event | When | Details (ids only) |
|---|---|---|
| `PracticeOwnerTransferred` (new) | 18.5 | actor account, former owner staff profile, new owner staff profile, reason, UTC time, former-owner-disabled flag |
| `RecoveryTokenIssued` (new) | 18.6 CLI | expiry |
| `RecoveryTokenRejected` (new) | the fifth failure voids a recovery token | token row id |
| `DeploymentAdministratorRecovered` (new) | 18.6 route | account id, method `Created` or `Reset`, MFA turned off, recovery codes revoked (count); never the token, username tried or password |
| `PracticeOwnerInvariantViolation` (new) | 18.7 migration | staff profile, failing conditions |
| refusal rows (existing mechanism) | E3 to E5 | rule `owner_transfer_required` |
| `PracticeOwnerDesignated` (existing) | E1, 18.4 | **Q7: ids only from C01 on** (staff profile id as the entity, actor, method). Stored rows are append-only and are not rewritten; no pre-release environment holds one (18.7). |
| `AccountDisabled`, session revocation (existing) | transfer with `disableFormerOwner` | unchanged |

**Name resolution at read time (Q7).** Stored owner, transfer and recovery rows hold ids only. `AuditLogReader` (ALV-N025) already resolves the actor's and an account target's names for callers with ViewAuditLog. C01 extends it for these event types:
- it resolves the `StaffProfile` entity id;
- it resolves the former and new owner staff profile ids, which are stored in fixed keys of the details (`formerOwnerStaffProfileId=…; newOwnerStaffProfileId=…`) and returned as `FormerOwnerName` and `NewOwnerName`.

Names are never written back into the stored row. Callers without ViewAuditLog already get 403.

### 18.11 Tests (rows for the section 11 matrix)
| Row | Proves | Layer |
|---|---|---|
| S11 (extended) | E1: an eligible non-Administrator **target** is refused 409 `owner_must_be_administrator`, nothing written; an Administrator staff member succeeds | API |
| S11b | E1a (R08-1): an Office Manager given `ManageUsers` (matrix grant) designating an eligible **Administrator** staff member → 403 `owner_designation_requires_administrator`, no designation row, a refusal audit row; the same request by an Administrator and by the Deployment Administrator succeeds (separate databases). **Race (R09-1):** an Administrator's designation runs concurrently with (a) a role downgrade and (b) a disable of that same actor, started by a second Administrator. In each case either the designation commits and the competing change committed after it, or the designation is refused (403) with no designation row. A test hook between the preliminary check and the transaction forces the downgrade to commit in that gap, and the designation must then be refused. **An actor that was ineligible at commit time never designates.** | API, service, concurrency |
| S23 | 18.4: `practiceOwner:true` with role Admin creates profile, account, link and designation in one transaction (three audit rows); any other role → 400 and **nothing** written | API |
| S24 | 18.4 both-or-neither: a fault injected after the account insert rolls back everything; an owner already designated → 409 and no staff or account row | service |
| S25 | 18.4 idempotency and races: the same key twice → one row set and no second password; two keys at once → exactly one owner, and the loser created nothing | service, concurrency |
| S26 | E3: the owner's role cannot leave Admin (409 `owner_transfer_required`, refusal audited); a non-owner Admin's role changes as before; a degraded owner's role can change **to** Admin | API |
| S27 | E4: inactivating the owner's profile is refused through both services; unlink is unreachable | API |
| S28 | E5: disabling the owner is refused through both disable paths, with the exact error body | API |
| S29 | 18.9 (R08-2): an Admin-login owner linked to the visit's active booked provider may record "Not clinically required"; a Dentist and a Hygienist linked the same way still may. Refused: an unlinked Admin; an unlinked Dentist; an Admin linked to an **inactive** provider profile; and Front Desk, Assistant, Billing and Office Manager logins each linked to the booked provider profile (four cases). | API |
| S30 | 18.7 (R08-3), on a seeded non-Admin owner: the step writes exactly one `PracticeOwnerInvariantViolation` whose `Id` equals the expected derived id (recomputed in the test from the documented formula); it changes no role. Running it a **second** time on the same state adds no row. After the seed changes **state** (role Dentist → Billing), it adds exactly **one** new row with a different id, and the first row is byte-identical. A valid owner writes nothing. `practiceOwnerStatus` is `degraded` until fixed, then `valid`. | migration, service, API |
| S31 | 18.5 happy path: the Deployment Administrator transfers. Afterwards one active designation (the new one), the old one ended with the same timestamp, method `Transfer`, one `PracticeOwnerTransferred` row whose details contain the three ids and the reason and **no display name or username**, and the former owner's sessions revoked (their next request is 401) | API |
| S32 | 18.5 authorization: an ordinary Administrator, the current owner and a non-Admin are all refused 403 `transfer_not_permitted`; a wrong `currentPassword` → 403, audited, and counts toward lockout. **With MFA on (Q5):** a missing `mfaCode` → 403 `mfa_required`; a wrong code → 403, audited `MfaStepUpFailed`, counts toward lockout; a recovery code is refused; the right password and code succeed. **With MFA off,** the password alone succeeds. Missing `confirm` or reason → 400. Nothing changes in any refused case. | API |
| S33 | 18.5 destination rules: same as source, inactive, unlinked, disabled, non-Admin, the Deployment Administrator itself, no active owner → each refused, nothing changes | API |
| S34 | 18.5 `disableFormerOwner`: the former owner is disabled in the same transaction (one `AccountDisabled`); a fault injected after the designation insert leaves the old owner active, not disabled and with sessions intact | service |
| S35 | 18.5 concurrency (R09-2): two transfers with **different idempotency keys**, both carrying the **same** `expectedDesignationId` (owner A), to different destinations B and C, started together. Exactly **one** commits; the other answers 409 `owner_changed` with no mutation (no second designation, no disable, no session revocation, no transfer audit row). A reader polling the active designation during the race never sees zero or two. A transfer whose `expectedDesignationId` is already stale before it starts → 409 `owner_changed`. A transfer racing a role change on the destination: either the transfer is refused or the role change is refused, never both committed. | service, concurrency |
| S36 | 18.5 idempotency: the same key replays the same result with no second audit row; the same key with a different body → 409 | API |
| S37 | 18.6 recovery: the CLI refuses before setup. With no Deployment Administrator, a valid token creates one (one only, under two concurrent requests). With a disabled or locked one, a valid token plus its username resets password and lockout, enables it and revokes its sessions; a wrong username → the same `recovery_failed`. Five failures void the token (atomic, as R02 CR-1). The token never appears in logs or audit. The route answers 503 outside Normal mode. **Q6 reset with MFA on:** the secret and pending secret are cleared; every recovery code is revoked; the stamp rotates and all sessions close; one `DeploymentAdministratorRecovered` row holds no token, username tried or password; the account then signs in with the new password alone (no enrolment gate in C01). | API, CLI |
| S39 | (moved to ALV-N030-C02 Stage 2: the enrolment gate) | - |
| S40 | Q7: new `PracticeOwnerDesignated`, `PracticeOwnerTransferred` and `DeploymentAdministratorRecovered` rows contain no display name or username in `Details`; `GET /api/audit-log` for a ViewAuditLog holder returns `FormerOwnerName` and `NewOwnerName` resolved; without ViewAuditLog → 403 | API |
| S38 | 18.8 client (vitest): the owner checkbox locks Administrator with the hint; the owner's Disable, Inactivate and role controls are disabled with the transfer message; the Transfer action is shown only to the Deployment Administrator; the dialog requires reason, password and confirm, and asks for an MFA code when the Deployment Administrator has MFA on; it keeps input on error; the recovery page's messages | vitest |

**Test cost:** focused groups only, each expected under 5 minutes (owner rule). No broad regression: `fullRegression` stays DEFERRED_TO_ALV-N028.

**Browser automation and walkthroughs (R08-4):**
- **C01 acceptance rests on automated tests only:** the focused API, service and concurrency rows above, plus browser automation.
  - The **S14** browser spec is updated for the locked Administrator role and run once (about 1 minute).
  - A **new targeted browser spec** covers transfer (the Deployment Administrator opens the dialog, transfers with reason, password and confirm; the former owner's session ends) and recovery (a console-issued token resets a disabled Deployment Administrator, who then signs in). It is run once and expected under 3 minutes.
- **No owner walkthrough is required for C01.** Any general manual inspection is at the owner's discretion.
- **ALV-N030-C02 keeps its required gate:** after the QR implementation and its automated tests pass, a short targeted walkthrough with a real authenticator app (scan the QR, complete enrolment) must pass **before** mandatory Deployment Administrator MFA enforcement is enabled (18.6a).

### 18.12 Questions
- **Q5 to Q8:** answered by the owner, 2026-11-25 (18.1).
- **Q6b:** resolved by the owner's execution order (2026-11-25): enforcement for newly created or recovered Deployment Administrators is ALV-N030-C02 Stage 2 (18.6a).

### 18.13 Slices (each shown before its tests run)
1. **Invariant:** E1, E1a, E2 to E5, refusal audits; S11, S11b, S26, S27, S28.
2. **Single call:** 18.4 and the Staff tab call; S23, S24, S25.
3. **Transfer:** route, service, two-factor step-up, method `Transfer`, audit, ids-only audit text and read-time name resolution; S31 to S36, S40.
4. **Recovery:** `Purpose` column, CLI, route, mode rule, MFA reset, dev script; S37.
5. **Existing data and provider eligibility:** migration data step, `practiceOwnerStatus`, the CheckoutRecorder rule; S29, S30.
6. **Client:** 18.8; S38; the S14 spec update and the targeted transfer/recovery browser spec, one run each. No owner walkthrough (R08-4).

### 18.14 Failure-first answers
| What fails | Behaviour |
|---|---|
| Two transfers race | The guard and the active designation's update lock serialize them. The second sees that the designation id no longer matches its `expectedDesignationId` and is refused 409 `owner_changed`; stale intent never moves ownership again. Never zero or two owners. |
| The initial designation races a downgrade or disable of its actor | The actor is re-checked under its row lock inside the designation transaction (18.3a). An ineligible actor at commit time is refused. |
| A transfer races a role change, disable or inactivation of either person | Both take the designation lock, then account or staff locks in a fixed order. One waits; the loser sees the committed state and is refused if OI or E3 to E5 would break. No deadlock cycle: a fixed lock order, and an existing `AdminGuardBusy` timeout answer if the guard is held. |
| A transfer fails part way | One transaction: no ended designation without a new one, no disable or session revocation without the transfer. |
| The Deployment Administrator is unavailable | Recovery (18.6) from the server console, then transfer. |
| The recovery token leaks | It works only for 15 minutes, once, from a route rate-limited to 5 per 5 minutes. It only resets or creates the Deployment Administrator, all audited. Issuing a new token voids it. |
| The migration finds a violation | Nothing changes; one audit row; an Administrator notice with the fix paths. The upgrade never stops. |
| The client sends a mismatched role or hides nothing | The server refuses (400, 403 or 409). Client controls are UX only. |
