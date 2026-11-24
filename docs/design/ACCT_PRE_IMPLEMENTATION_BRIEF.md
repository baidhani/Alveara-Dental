# ACCT: accounts, Deployment Administrator and Practice Owner. Pre-implementation brief

- **Status:** **APPROVED FOR IMPLEMENTATION.** Sections 1 to 15 were approved at R04 (`.alveara/reviews/ACCT/BRIEFING-R04.md`, after R01 to R03). The section 16 addendum was approved at R06 (`.alveara/reviews/ACCT/BRIEFING-R06.md`, after R05). Boundary: database creation, SQL snapshot settings, applying migrations, a production service wrapper and update distribution stay with ALV-N013; the production automatic-restart proof is ALV-N013's dependency proof, not an ALV-N030 acceptance item. Revision history follows.
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
- **R07 addendum (2026-11-24, owner-requested, slice 5 finding):** section 17 replaces section 8's hosting with test support on the existing `Alveara.Api.ChainHost`: no product start-up split and no second test executable, under the owner's seven boundaries. It also retargets guards G1 to G8, the harness migration and the TestHost wording in sections 12 and 16; everything else stands. **Slice 5 waits for review of section 17.**
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
