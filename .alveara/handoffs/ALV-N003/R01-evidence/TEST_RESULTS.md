# ALV-N003 R01 — Test Results

All commands run from the repository at implementation commit `848fce82e9b4d1b96059c5087b5dacea77ae1679`.

## Backend — `dotnet test`

```
cd src/Alveara.Api.Tests
dotnet build
dotnet test --no-build
```

**Result: 252 of 252 passing, 0 failed, 0 skipped** (duration 10 m 44 s). Baseline 199 (ALV-002-C01 R03) + 53 new:

| Class | New cases | What it proves |
|---|---|---|
| `PracticeConfigurationServiceTests` | 21 (11 facts, 10 theory rows) | practice first-save/update/version/stale-conflict, required name; single-active-location rule at service **and** database; location/operatory/appointment-type CRUD, uniqueness, duration boundaries (5, 30, 480 accepted; 0, 4, 7, -5, 485 rejected); inactivation + historical resolution; idempotent no-op; audit coupling (stale edit rolls back change *and* audit); FK `Restrict` refuses destructive delete; permission matrix |
| `StaffProviderServiceTests` | 22 (16 facts, 6 theory rows) | account/staff/provider distinct but linkable; link to missing/disabled/already-linked account rejected; later-disabled link kept; link-change audited; unique staff names; provider needs active staff, one per staff; inactivation ordering rules; inactivated provider still resolves with name + availability; hard delete refused by FK; availability persisted as wall-clock `TimeOnly`; invalid ranges/times rejected; overlap rejected and failed replace leaves schedule intact; blocked time local→UTC for CST and CDT; DST gap/overlap rejected; end-after-start; blocked time add/remove audited; same weekly window = same wall-clock hours in winter and summer; link-picker contents |
| `SchedulingConfigurationTests` | 3 | **scheduling-consumer integration smoke**: configure provider/operatory/appointment type/availability → immediately in the read model; inactive config excluded but resolvable; availability check honors hours, blocked time and slot boundaries |
| `ConfigurationApiTests` | 7 | 401/403 matrix across roles and endpoints (incl. `required` permission name); `ViewSchedule`-only access to the scheduling read model; CSRF required; stable validation/conflict error codes; stale edit → shared 409 problem and nothing changed; **no DELETE route** (405) for the five entity kinds; end-to-end HTTP configuration consumed by the scheduling snapshot and audited (entity types visible in the audit log API) |

## Backend — `dotnet build`

Clean, 0 errors.

## Frontend — `npx vitest run`

**Result: 23 test files, 101 tests, all passing** (70 carried forward + 31 new):

- `components/ConfigEntityPanel.test.tsx` (16): loading/empty/denied/error+retry states, search + show-inactive, inline validation (server not called), create, server message shown with form kept open, unsaved-change prompt (declined keeps text; accepted closes; no prompt when clean), dirty reported to host, **stale edit → `ConcurrencyConflictBanner` → reload shows current version**, inactivate/reactivate with no delete action, create blocked with reason, `validateFields` boundaries and create-only fields.
- `pages/ConfigurationHubPage.test.tsx` (13): all tabs present; time zone/currency read-only; 403 → permission-denied; tab-switch guard for unsaved edits; practice name required + version sent; scheduling preview from the read model; empty preview state; availability load/save (atomic PUT), overlap/inverted windows refused without a server call, DST-gap server message shown, no-providers state; `validateWindows` rules; **axe: no violations** on the practice tab, an open edit form, and the availability editor.
- `App.routeGuards.test.tsx` (+2): `/admin/configuration` denied without `ManagePracticeConfiguration` (nav link hidden); rendered for a practice manager (nav link shown).

## Frontend — `tsc --noEmit`, `npm run build`, `npm run lint`

`tsc --noEmit` clean. Production build clean. Lint exit 0 with 13 informational warnings: the 9 pre-existing plus 4 new `react(set-state-in-effect)` (same class already present in `AdminUsersPage`/`AuditLogPage`/etc.; one per new data-loading effect). No new warning kind.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('AlveraE2E') IS NOT NULL BEGIN ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E; END"
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
ConnectionStrings__Alveara="...AlveraE2E..." AdminBootstrapSecret="e2e-real-backend-secret" ASPNETCORE_URLS="http://localhost:5072" dotnet run --no-build

# second terminal, from src/alveara-client
E2E_BOOTSTRAP_SECRET="e2e-real-backend-secret" npx playwright test --config=playwright.auth.config.ts
```

**Result: 10 of 10 passing** (9 carried forward + 1 new: "practice configuration: set up the practice end to end, see it in the scheduling preview, and find it in the audit log"), against a fresh migrated database and a real running API process built from this attempt's code. Raw output: `R01-evidence/artifacts/playwright-real-backend/run-output.txt`.

Process note: during development the first run of the new Playwright test failed on an over-broad selector (the Operatory row's action buttons also match `cell` by name — a strict-mode violation in the *test*, not the app); the selectors were made exact and the suite was rerun against a fresh database. The final run above is against the final source (after a frontend-only module split for lint), not an earlier one.
