# ALV-001-C01 R02 — Demo Evidence

Unlike R01's `curl`-driven demo against the raw API, this attempt's primary demo evidence is a real Chromium browser, running the actual built React application, making genuine network calls to a real `Alveara.Api` process backed by a real (freshly migrated) LocalDB database — directly satisfying the R01 review's finding that component tests and route-mocked browser tests do not fulfill "exercise the actual rendered application in a real browser against the real API/database."

## Setup

```
cd src/Alveara.Api
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;Trusted_Connection=True;TrustServerCertificate=True"
$env:ConnectionStrings__Alveara = "...AlveraE2E..."
$env:AdminBootstrapSecret = "e2e-real-backend-secret"
dotnet run --no-build
```

```
cd src/alveara-client
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
npx playwright test --config=playwright.auth.config.ts
```

## Result

```
Running 5 tests using 1 worker

  ok 1 … an unauthenticated caller cannot select a role at registration (285ms)
  ok 2 … bootstraps the first admin via the real API and signs in (839ms)
  ok 3 … enrolls a real TOTP factor and completes an MFA challenge end to end (1.4s)
  ok 4 … security administration: list users, view detail, change role, set session timeout (403ms)
  ok 5 … permission matrix is visible to the admin and shows every role (109ms)

  5 passed (5.3s)
```

Full raw output: `artifacts/playwright-real-backend/run-output.txt`.

## What each test actually proves happened, in a real browser

1. **No role field anywhere in the UI** — `page.goto("/login")`, asserts no role selector exists; registration itself isn't exposed as a public flow (self-service accounts are provisioned by an admin), which is the acceptance criterion made visually verifiable.
2. **Real bootstrap + real login** — a real `POST /api/auth/bootstrap-admin` call (via Playwright's own request context, still same-session), then a real form-fill-and-submit login through the rendered `LoginPage`, landing on the real `Dashboard`.
3. **Real MFA enrollment → confirmation → challenge, end to end** — clicks the real "Set up an authenticator app" button, reads the real Base32 secret and 10 recovery codes rendered on screen, computes a TOTP code **independently in the browser via the Web Crypto API** (not by calling into the app's own TOTP code at all), submits it through the real confirm form, sees "MFA is now active on your account." Then clears the session cookie, logs in again through the real form, is genuinely routed to `/mfa-challenge` (password alone no longer suffices), computes a second independent code, submits it, and lands on the real `Dashboard` — a real, browser-rendered, second-factor-gated login.
4. **Security administration, real detail view, real session-timeout write** — navigates to the real user list, clicks a real "View" link to the real detail page, fills and submits the new session-timeout field, sees the real "Session timeout updated." confirmation.
5. **Permission matrix** — navigates to the real permission-matrix page and confirms all seven story-defined roles render as columns, sourced from the live `GET /api/auth/permission-matrix` response.

## A genuine bug this run caught

The first attempt at this run failed at step 3 with `SyntaxError: Failed to execute 'json' on 'Response': Unexpected end of JSON input`, surfaced as "Could not confirm MFA. Please try again." in the rendered UI. Root cause: `authApi.ts`'s shared `request()` helper called `res.json()` unconditionally on any non-`204` success response, but `POST /api/auth/mfa/confirm` (and `POST /api/auth/logout`) return `200` with a genuinely **empty** body (`return Ok();` in `AuthController.cs`, not `Ok(someObject)`). No mocked test in this repo's suite exercised that exact combination (every mock in the vitest/component tests supplied a JSON body), so it was invisible until a real browser made a real request against the real endpoint. Fixed by reading the response as text first and parsing only when non-empty; re-ran the full suite (5/5 passing) and the full vitest suite (40/40 passing) afterward to confirm no regression.

## Disposable artifacts

The `AlveraE2E` LocalDB database, the throwaway `admin-<timestamp>` account it contains, and the local API/Vite dev-server processes used to generate this evidence are disposable session-local artifacts — not committed, contain no real data.
