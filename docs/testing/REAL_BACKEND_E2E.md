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

## STORY-003: patient registration walkthrough

`e2e/patient-registration-real-backend.spec.ts` is the same kind of un-mocked run for patient
registration. Start the API exactly as in step 2, **also setting
`AuthAttemptRateLimit__PermitLimit=500`** (the spec signs in an admin, a front-desk user and two
dentists from one address, which the default limit of 10 would cut off), then from
`src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:PATIENT_E2E_OUT = "C:\tmp\patient-e2e"   # optional: where the JSON results and screenshots go
npx playwright test --config=playwright.patients.config.ts
```

It signs in as a front-desk user and shows: an incomplete form prompts for every missing required
field (and stores nothing); a valid registration is read back from the real API with all its
demographics and contact details; registering the same person again is refused with the existing
record named; the form can be completed from the keyboard alone; the audit log holds one
`PatientRegistered` entry per registration with the user and a timestamp and no patient details;
and a dentist has no nav link, no page and a 403 from the API. It also runs axe over the form
(default, error and success states) in light and dark. Like `auth-real-backend.spec.ts`, it is
excluded from the default mocked `npm run test:e2e`.

## ALV-003-C01: patient identity workspace walkthrough

`e2e/patient-workspace-real-backend.spec.ts` is the un-mocked run for the patient identity workspace. Start the API exactly as for the STORY-003
walkthrough above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:WORKSPACE_E2E_OUT = "C:\tmp\workspace-e2e"   # optional: where the JSON results and screenshots go
npx playwright test --config=playwright.workspace.config.ts
```

It signs in as front-desk users, a practice administrator and a dentist and shows: a likely duplicate compared side by side and registered only after a
human chooses to (the existing record untouched), an exact duplicate blocked; search by name words, birth date and phone in any formatting; the
shared patient header staying in context across tabs; an edit saved with history; a stale edit by a second user refused without overwriting; household and
guarantor held independently (a guarantor outside the household); invalid relationships refused with the server's reason; inactivate/reactivate preserved
through edits and hidden/shown by the search filter; switching patients on a deliberately slow connection never showing the previous patient; a practice
requirement (email) set by an administrator, shown on the form and enforced by the server; keyboard-only registration and search; the audit trail; a dentist
who can read but not change patients; and axe scans of each screen and state in light and dark. Like the other real-backend specs it is excluded from the
default mocked `npm run test:e2e`.

## ALV-N010: versioned forms and consents walkthrough

`e2e/forms-real-backend.spec.ts` is the un-mocked run for forms and consents. Prepare a fresh database and start the API exactly as for the STORY-003
walkthrough above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:FORMS_E2E_OUT = "C:	mporms-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.forms.config.ts
```

It signs in as an office manager, a front-desk user, a dentist and a billing user and shows: a versioned template built through the UI (and refused to the front
desk); a form started for a patient, a draft that survives a reload, and a required answer blocking review; a template edit during completion leaving the draft on
its own version while offering the newer one; the review screen showing exactly what will be signed, missing signer details refused, and a keyboard-only
signature recording signer, relationship and template version; two further template versions leaving the signed copy and its fingerprint unchanged; the same
submit again returning the first result and a different key refused as already signed; a response lost after the server stored the signature - the screen
says the outcome is unknown and "Sign again" (same key) leaves exactly one signature; the front desk refused a void, the office manager voiding with a reason
while the signed copy stays visible; a dentist who can complete forms and billing who can only read them; and another patient's forms never appearing - then axe
scans of each screen and state in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.
