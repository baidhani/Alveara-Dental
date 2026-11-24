# ALV-N030 R01: next-story impact

## ALV-N027 (next, plan item 64)
- **Staff and users.** New staff are created only through "Add staff member" (`POST /api/config/staff-members`, profile and login together, temporary password). Any N027 work on the Staff, Providers or Security Administration screens builds on `PeopleTabs.tsx` (StaffTab), `StaffMemberDialogs.tsx` and `accountLabels.ts`.
- **Identity.** The identity call `/api/auth/permissions` now carries `mustChangePassword`, `accountKind`, `isPracticeOwner` and `practiceOwnerExists`; the client state carries the same.
- **Dashboard.** It now shows `PracticeOwnerNotice`, with the owner-approved wording for the Deployment Administrator.
- **Role defaults.** Default Front Desk permissions must stay a subset of the Office Manager's (`RoleSubsetInvariantTests`). Any N027 change to a default role set must keep that.

## The setup-wizard story (after ALV-N027)
- **Entry points.** The Practice Owner designation (`PracticeOwnerDesignations`; `isPracticeOwner` on the identity) and the notice text "will use the guided practice setup when it becomes available" are its entry points.

## ALV-N013 (installer, update and service supervision)
- **The installer:**
  - creates and migrates the database (the API never migrates);
  - issues the setup token with `Alveara.Api setup issue-token` and shows the setup page address (`SetupPageUrl`);
  - configures the service supervisor to restart on **exit code 75** (brief 16.5).
- **The production proof of the automatic restart is ALV-N013's** (R07 review requirement: kept apart from the R07 ChainHost evidence here). ALV-N030 proved the exit code and the restart only with the walkthrough runner, `Start-DevApi.ps1` and the S20 process test.

## ALV-N028 (broad regression)
- **What it must run:** see `PARENT_REGRESSION.md`.
- **Test-database state:** test databases are set up by default. Use `TestDatabaseFixture.ClearSetupCompletedAsync`, or the runner's `-DatabaseState Fresh`, for setup-mode scenarios.

## Findings left open
- **U-041** (the billing walkthrough assertion, pre-existing, unassigned).
- **U-018** (the flaky BillingStatementsPanel vitest).
