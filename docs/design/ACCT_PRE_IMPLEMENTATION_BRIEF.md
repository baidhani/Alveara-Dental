# ACCT: accounts, Deployment Administrator and Practice Owner. Pre-implementation brief

- **Status:** DRAFT for independent review. **No code before approval.**
- **Story id:** to be assigned by the owner (working name ACCT).
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
| `UserAccounts.MustChangePassword` (bit, not null, default 0) | Set by "Add staff member" and by an administrator's password reset (see section 6). Cleared only by a successful change. |
| `SetupTokens` (new table) | `Id`, `TokenHash` (SHA-256 of a 32-byte random token; the token itself is never stored), `IssuedAtUtc`, `ExpiresAtUtc` (issued + 15 min), `ConsumedAtUtc` (null until used), `FailedAttempts`. At most one unconsumed row: issuing a new token voids earlier ones. |
| `PracticeOwnerDesignations` (new table) | `Id`, `StaffProfileId` (FK, required), `DesignatedAtUtc`, `DesignatedByAccountId`, `Method` (`InitialByDeploymentAdministrator` or `UpgradeTransition`), `EndedAtUtc` (null while active). A filtered unique index on a constant column where `EndedAtUtc IS NULL` gives **exactly one active owner**, enforced by the database. Rows are never deleted; an update trigger refuses any change except setting `EndedAtUtc`, which only a future transfer story would do (ACCT has no code path that sets it). |
| `BootstrapState` | Kept, so existing databases keep their history. The new setup flow writes the same singleton for exactly-once behaviour under a race, as today. |
| `StaffProfiles.UserAccountId` | Already one-to-one where present (filtered unique index, `AlveraDbContext.cs:209`). Stays nullable for existing data (D-7: no rewrite of history). The rule "a new staff member has a linked account" is enforced by the combined create, not by a NOT NULL constraint. |
| `Role.Unassigned` | Kept in the enum because existing rows may hold it. Nothing creates it any more. Existing `Unassigned` accounts stay disabled and are listed in Security Administration as legacy, so an administrator can review them. |

**Data step on upgrade** (in the migration, deterministic, idempotent):
- **Deployment Administrator:** exactly the Admin accounts with no linked staff profile are candidates.
  - If there is one, it gets `AccountKind = 1`.
  - If there are **several**, none is converted automatically, since a choice would violate "never silently choose". The upgrade designation notice then also lists them, and an Administrator picks one, confirmed and audited.
  - If there are none, there is no Deployment Administrator, and the owner transition (D-7) applies.
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
| `POST /api/config/staff` (profile-only create) | existing | **Kept** for a staff profile **without** a login (a staff member who never signs in). The UI label becomes "Add staff profile (no login)" under an "Add staff member" primary action. The `UserAccountId` link-existing-account field is **removed** from create; linking existing legacy accounts stays possible only in Security Administration. *Reviewer note: if the owner wants profile-only creation removed entirely, this row changes.* |
| `GET /api/config/linkable-accounts` | existing | Excludes the Deployment Administrator. |
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
- **"Add staff member":** needs both ManagePracticeConfiguration (the profile) and ManageUsers (the account), because it creates a login. An Office Manager without ManageUsers sees only "Add staff profile (no login)".
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
  - an administrator's password reset (existing `reset-password`) is **unchanged**, since it uses a reset token, not a temporary password. *Flag for review:* should a reset also set `MustChangePassword`? Recommended no: the reset-complete flow already makes the user choose their own password.
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
| `DeploymentAdministratorAssigned` | upgrade: migration auto (one candidate) or administrator pick (several) | account, method | |
| `AccountRegistered` | kept as a constant | existing rows still read | no longer written |

## 8. The test-only provisioning route (D-6)
- **Where it lives:**
  - the controller lives in a **separate assembly, `Alveara.Api.TestSupport`**, which the production API project does not reference;
  - only the test host loads it, through `WebApplicationFactory` configuration in `Alveara.Api.Tests` and a `--test-support` startup path used only by the walkthrough and chain runners;
  - production and deployed builds do not contain the code, so no flag can switch it on.
- **Second condition:** registration also requires `ASPNETCORE_ENVIRONMENT=Test` (a new dedicated environment name) **and** `TestSupport:Enabled=true`. A startup check throws if the assembly is loaded under any other environment, so it fails closed.
- **Walkthrough and chain runners** change their API environment from `Development` to `Test` (`Invoke-RealBackendWalkthrough.ps1:103`, `Invoke-AccumulatedChain.ps1`), and the Vite proxy is unchanged.
- **The route:** `POST /api/test-support/accounts` with body `{ username, password, role, kind?, withStaffProfile?, mustChangePassword? }`. It creates an enabled account directly, so tests that need an Admin call it instead of the bootstrap.
- **Guard tests:**
  - the production `Program` host (Development and Production environments) answers 404 on the route;
  - the API project file has no reference to `Alveara.Api.TestSupport`;
  - the published output (`dotnet publish`) has no `Alveara.Api.TestSupport.dll`;
  - the OpenAPI document (if one is served) does not list it.
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
- **Practice Configuration → Staff:** the primary action becomes "Add staff member" (username, role, the optional owner checkbox while none exists). The temporary password is shown once in a copyable, dismiss-to-hide panel with "Give this password to the person; they must change it when they first sign in."
- **Dashboard notices:**
  - for the Deployment Administrator with no owner: "No Practice Owner has been created yet. We recommend creating one before you configure the practice: add the owner as a staff member and tick Practice Owner. You can still configure the practice by hand; the guided setup is reserved for the Practice Owner." (owner wording to approve);
  - for an Administrator after an upgrade with no owner: the transition notice with a "Designate the Practice Owner" action (eligible staff list, confirm step).
- **Security Administration:** shows "Deployment Administrator" and "Practice Owner" labels; has no create action; legacy `Unassigned` accounts are labelled as legacy.

## 10. Failure-first answers
- **Setup fails mid-transaction:** nothing is committed and the token is not consumed (the transaction covers all of it). Retrying with the same token is safe while it is unexpired.
- **Two setup requests race:** the `BootstrapState` primary key plus the filtered unique index on `AccountKind = 1` make exactly one succeed. The other gets the generic 400.
- **Combined create fails:** one transaction, so there is never a profile without its account or an account without its link. An Idempotency-Key replay doesn't create twice.
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
| S10 | The forced-change gate: every non-allowlisted route answers 403 while it is set (a sample of 10 representative routes, plus a reflection test that the allowlist is exactly the five listed); a change clears it and rotates the stamp (the old cookie is rejected) | API |
| S11 | Practice Owner: the first designation succeeds; a second gets 409; the target must be active staff with a linked, enabled staff account; the Deployment Administrator cannot be designated; non-ManageUsers gets 403; the database refuses a second active row (direct SQL) and any update other than `EndedAtUtc` | API, DB |
| S12 | Upgrade data step with 0, 1 and 2 profile-less admins; no owner is ever set by the migration | migration |
| S13 | The test-support guard: 404 under Development and Production; no project reference; not in the publish output | API, build |
| S14 | Browser: setup page to sign-in; Add staff member to temporary password to first sign-in to forced change to dashboard; the Deployment Administrator's dashboard notice; the upgrade notice | Playwright |
| S15 | `ExportDomainCatalogTests` and `PatientOwnedDataTests` green with the new tables | API |

Happy path, failure path, boundary cases (token expiry exactly at 15:00, a 5th versus 6th attempt) and idempotency are all covered above.

## 12. Slices (each reviewed before its tests run, per the owner's rule)
1. Schema, migration and data step, with S12 and S15.
2. Setup token CLI, setup route, removal of register and bootstrap: S1 to S7.
3. Forced-change gate, change-password route and whoami fields: S10.
4. Combined create, Deployment Administrator exclusion and Practice Owner routes: S8, S9, S11.
5. The test-support assembly and guard (S13), then the **harness migration**: backend helpers, the 77+59 e2e files, the runners' environment.
6. Client: setup page, change-password page, Add staff member, notices, Security Administration labels: vitest plus S14.

## 13. Test cost (owner's 5-minute rule)
- Slices 1 to 4: focused backend groups, each expected to take under 5 minutes, run without asking.
- **Slice 5 touches every e2e spec.** Proving the harness migration needs the walkthrough suites, which take far longer than 5 minutes. I will **ask before** any such run and propose a sample (the auth, permission-policy and two domain walkthroughs) for the focused proof, deferring the full set to ALV-N028's regression.
- No Stage 2 or full-suite run without explicit approval.

## 14. Points for the reviewer
1. Keeping profile-only staff creation (section 4) versus removing it.
2. Whether an administrator's password reset should also force a change (section 6; recommended no).
3. Several profile-less admins on upgrade means none is auto-converted (section 3).
4. Isolating the test-support code in a separate assembly as the "cannot be enabled by a flag" mechanism (section 8).
5. The dashboard notice wording needs the owner's approval.
