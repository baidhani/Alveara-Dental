# ALV-N009 R02 — Test Results

All commands run from the repository at implementation commit `c7ed14c321b1f167baa4b4e58646aa4d9d1a2c64`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 178 of 178 passing, 0 failed, 0 skipped.** Unchanged from R01 — no backend file was touched this attempt; all three corrections are client-only.

## Frontend — `npx vitest run`

**Result: 19 test files, 57 tests, all passing** (54 carried forward from R01 + 3 new this attempt).

New this attempt, in `contexts/AuthContext.test.tsx`:

- `R02: crosses the authoritative session expiry and unmounts protected state (was: only cleared the warning flag)` — fake-timer test proving the fix for ALV-N009-R01-01.
- `R02: revalidates a signed-in session on a bounded poll and clears state when access was revoked elsewhere` — fake-timer test proving the fix for ALV-N009-R01-02.

New this attempt, in `App.routeGuards.test.tsx`:

- `R02: returns a caller to the page they were denied even when reauthentication requires MFA` — proving the fix for ALV-N009-R01-03.

## Frontend — `npx tsc -b`

Clean, exit code 0.

## Frontend — `npx vite build`

Clean production build, 62 modules transformed.

## Frontend — `npx oxlint`

Exit code 0. Same two `react(set-state-in-effect)` informational warnings as R01 (`AuthContext.tsx`, now at updated line numbers), no new warning kind.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('AlveraE2E') IS NOT NULL BEGIN ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E; END"
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
$env:ConnectionStrings__Alveara = "...AlveraE2E..."; $env:AdminBootstrapSecret = "e2e-real-backend-secret"
dotnet run --no-build

# second terminal, from src/alveara-client
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
npx playwright test --config=playwright.auth.config.ts
```

**Result: 8 of 8 passing**, unchanged in assertion from R01, against a genuinely fresh, migrated database and a real running API process. Raw output: `R02-evidence/artifacts/playwright-real-backend/run-output.txt`.

No new real-backend Playwright tests were added this attempt. The three corrected behaviors (expiry sign-out, bounded-poll revalidation, MFA deep-link redirect) are timer/polling-driven; a real-browser test would need to either wait out real wall-clock minutes or fake the browser's system clock mid-session, which Playwright does not support cleanly against a real backend session cookie's own expiry. They are instead covered deterministically with Vitest fake timers, matching the review's own required-test wording ("add a deterministic fake-timer test", "add a deterministic test that changes the mocked server authorization state"). This Playwright run exists solely to prove the existing 8 real-backend flows still pass unmodified against the corrected code.
