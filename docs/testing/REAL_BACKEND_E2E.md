# Real-backend end-to-end tests

Most of `src/alveara-client/e2e/` mocks the API via Playwright's `page.route()` — deliberately,
so those specs run against nothing but the built static client (`playwright.config.ts`'s
`webServer` only starts `vite preview`).

`e2e/auth-real-backend.spec.ts` is different: it makes genuine network calls to a real,
running `Alveara.Api` process backed by a real (migrated) SQL Server LocalDB database, with no
mocking anywhere. This exists specifically to satisfy ALV-001-C01's requirement to "exercise the
actual rendered application in a real browser against the real API/database" — component tests
and route-mocked browser tests don't prove that; this does.

It is **not** part of `npm run test:e2e` (which uses `playwright.config.ts`). Run it explicitly
with its own config, after starting both a real API and the Vite dev server yourself:

## 1. Prepare a fresh database

```powershell
cd src/Alveara.Api
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;Trusted_Connection=True;TrustServerCertificate=True"
```

Bootstrap is one-time per database — if you re-run the suite, drop and recreate `AlveraE2E`
first:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E;"
```

## 2. Start the real API against that database

```powershell
cd src/Alveara.Api
$env:ConnectionStrings__Alveara = "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;Trusted_Connection=True;TrustServerCertificate=True"
$env:AdminBootstrapSecret = "e2e-real-backend-secret"   # or your own value — pass the same one below
dotnet run --no-build
```

## 3. Run the spec

From `src/alveara-client`, in a second terminal (the config starts the Vite dev server for you,
which proxies `/api` to the API started above — see `vite.config.ts`):

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"   # must match step 2
npx playwright test --config=playwright.auth.config.ts
```

## What it proves

Registration cannot select a role, first-admin bootstrap works through the real UI/API, a real
TOTP secret is enrolled and confirmed through the rendered `MfaSettingsPage`, a real login pauses
for and completes an MFA challenge (the confirming code is computed independently in the test
itself via the Web Crypto API — not by calling into the app's own TOTP code — proving
interoperability, not just self-agreement), the security-administration UI lists/views/edits a
real user including the session-timeout control, and the permission-matrix page renders every
role from the real API response.

Evidence from an actual run is committed at
`.alveara/handoffs/ALV-001-C01/R02-evidence/artifacts/playwright-real-backend/`.
