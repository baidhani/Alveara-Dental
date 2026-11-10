# Owner Request: First-Run Setup (administrator and database)

Date recorded: 2026-11-09
Requested by: the product owner, while discussing ALV-N019 step S-09.
Status: **RECORDED, NOT AUTHORIZED.** This is a candidate decision, `PD-38`, next to R05 `PD-04` (staff onboarding model). It is not part of ALV-N019 and nothing here is built. R05 (approved) and the execution plan are not edited by this record.

## The request, in the owner's terms

1. **First-run administrator.** When the client starts and a person opens the host web page, the system checks whether an administrator account is configured. If not, this is a first run, and the page offers to create the first administrator. Today a script (`New-DevAdmin.ps1`) is needed (manual E2E step S-09, finding F-016 is the same pain for staff accounts).
2. **Additional item: starting from no database.** The system must also work when **no database exists yet**. The API initializes the database when the client calls an endpoint, so a person never has to run a database script first.

## Where it belongs (proposed, not decided)

- Primary home: **ALV-N027** (R05 PKG-14, onboarding), together with `PD-04` and finding F-016. Its pre-implementation briefing must include this request.
- Second home: **release engineering ALV-N013** (installer and upgrade). A real install has no `New-DevAdmin.ps1` or `Initialize-DevDatabase.ps1`. Paused until the owner clears release engineering.
- Not in ALV-N019. N019's S-09 stays the dev script plus the chain's own bootstrap (owner chose option B: wrong secret returns 401 and creates nothing).

## Facts from the repository that shape the design (read for this record; nothing was run)

- The first-admin endpoint exists: `POST /api/auth/bootstrap-admin` (`AuthController.cs:61`). It needs `AdminBootstrapSecret` (503 when not configured, 401 for a wrong secret, 409 once used).
- The API does **not** create or migrate the database when it starts. The database is created and migrated by `scripts/Initialize-DevDatabase.ps1` (`dotnet ef database update`).
- At least seven hosted background services read the database from startup (notifications, policy report drain, follow-up reminders, follow-up work, background jobs, deployment-invariant monitor, backup scheduler, `Program.cs` lines 155 to 262). With no database they cannot do their work.
- The production database must have `READ_COMMITTED_SNAPSHOT` and `ALLOW_SNAPSHOT_ISOLATION` on (`docs/operations/DATABASE_OPTIONS.md`); the dev script sets both when it creates the database.
- A least-privilege service identity is part of the deployment verification (`ops/deployment-verification/Invoke-ServerVerification.ps1`).

## Design questions that need the owner (and the reviewer) before any briefing is written

| # | Question | Why it matters |
|---|---|---|
| Q1 | Who may call the first-run endpoints? | An endpoint that creates a database or an administrator without sign-in is a takeover risk on a fresh install. Options: require the bootstrap secret (A), a one-time setup code written to the server console or a file (B), or open first-come (C, not recommended). |
| Q2 | Does the API run with permission to create a database? | `CREATE DATABASE` and `ALTER DATABASE` (snapshot options) are server-level rights. A least-privilege service account normally does not have them. Options: a separate, temporary setup identity; a setup mode that runs with elevated rights only during first run; or keep database creation in the installer and let the client only create the administrator. |
| Q3 | How does the API behave with no database? | A "setup mode" in which the background services and normal routes are off and only the status and setup endpoints answer, then a switch to normal mode after initialization. This changes how the host starts. |
| Q4 | Migrations at run time or by tool? | Today migrations are applied by `dotnet ef`. Applying them from the API is a change in how the production database is managed (CLAUDE.md lists database and production-infrastructure changes as escalation items). |
| Q5 | Failure and replay behavior | Initialization must be idempotent and safe to retry after a partial failure (database created but migration failed, or migration done but no admin). It needs a defined recovery path and an audit trail, and it must never run on a database that already holds data. |
| Q6 | What the status check reveals | The client needs only a yes/no ("set-up needed"). The answer should reveal nothing else about the server, and the setup endpoints must turn themselves off for good once setup is complete. |
| Q7 | Dev and chain behavior | The dev scripts and the accumulated-chain runner may keep creating their own databases and admin directly; the chain must still be able to start from an empty environment and must refuse `AlveraDev` and `Dev-Data`. |

## Recommended next steps (not started)

1. Keep ALV-N019 as approved.
2. When ALV-N027's briefing is written, include this request, answer Q1 to Q7 with options and a recommendation, and let the owner decide.
3. Because it touches database creation, startup, privileges and production infrastructure, treat it as an escalation (CLAUDE.md "Strategic decisions") and ask for the reviewer's view in the briefing.
