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
$env:FORMS_E2E_OUT = "C:\tmp\forms-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
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

## STORY-005: clinical documentation walkthrough

`e2e/clinical-real-backend.spec.ts` is the un-mocked run for clinical documentation. Prepare a fresh database and start the API exactly as for the STORY-003
walkthrough above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:CLINICAL_E2E_OUT = "C:\tmp\clinical-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.clinical.config.ts
```

It signs in as a dentist, a hygienist, an assistant, a front-desk user, a billing user and a practice manager and shows: only the clinical team sees the Clinical tab
(front desk, billing and the practice manager are denied the page and the API; an assistant reads but cannot write); a dentist documenting an encounter - medical
history, dental history, an allergy with reaction and severity, a medication with dose and frequency - each saved as it is made and stored with who and when; a note
with sections still unaddressed refused at finalize by both the screen and the API, and "reviewed - none reported" completing it without inventing an entry; a
finalized note refusing every change while an addendum is added beside the untouched original (entries, reviews and the encounter's version unchanged); a response lost
after the server stored an addendum - the typed text stays and sending again leaves exactly one; a second clinician's stale screen refused with the conflict banner,
what they typed surviving the reload and then saving; a keyboard-only entry; another patient's encounters never appearing; and the audit log holding every change with
user and time and no clinical text - then axe scans of each screen and state in light and dark. Like the other real-backend specs it is excluded from the default mocked
`npm run test:e2e`.

## ALV-005-C01: clinical record, notes, templates, vitals and signing walkthrough

`e2e/clinical-companion-real-backend.spec.ts` is the un-mocked run for the clinical documentation companion. Prepare a fresh database and start the API exactly as for the
STORY-003 walkthrough above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:COMPANION_E2E_OUT = "C:\tmp\companion-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.clinical-companion.config.ts
```

It signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an assistant, a front-desk user and the administrator, and shows: allergies,
medications and history captured on the patient's record with who and when, and a status or correction keeping what it replaced in the item's history; "none known",
"unknown" and "not reviewed" as three different statements that create no item, adding an item withdrawing a stale statement, and a confirmed list going back to "needs
review" when an item changes; a stale record edit refused with the conflict banner and the typed values surviving the reload; a dentist configuring a note template (a
hygienist can read but not configure, and the API says which permission is missing); a template shaping an encounter's SOAP, progress and treatment notes with notes that save
themselves (on leaving the box and after a pause); signing refused by both the screen and the API until the required notes are written, naming each; vitals recorded with the
encounter and a wrong reading voided with a reason (kept, marked); a signed note locked on the screen and in the API, unsigned by another clinician, signed again and finalized
by keyboard with both signers named; a finalized note refusing every change while an amendment preserves the original and records who wrote it, when and what it amends; a
response lost after the server stored an amendment (the typed text and section stay and the retry adds nothing more); a stale note save refused with the typed note
surviving the reload; front desk denied everywhere and an assistant reading with no controls; and the audit log holding every change with user and time and no clinical text - then
axe scans of each new screen and state in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.

## STORY-004: appointment scheduling walkthrough

`e2e/schedule-real-backend.spec.ts` is the un-mocked run for scheduling. Prepare a fresh database and start the API exactly as for the STORY-003
walkthrough above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:SCHEDULE_E2E_OUT = "C:\tmp\schedule-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.schedule.config.ts
```

It configures the practice through the real configuration API (two providers working Monday-Friday 08:00-17:00, two operatories, two appointment types, a blocked lunch),
then signs in as a front-desk user and shows: an appointment with an available provider confirmed and listed; a double-booked provider refused in words (naming the
provider and the time already taken) with nothing booked; an operatory in use, a time outside working hours, blocked time and an incorrect duration each refused in words;
a back-to-back appointment accepted; a response lost after the server booked (the page says the outcome is unknown and "Book again" books exactly once); six simultaneous
requests for one slot producing exactly one appointment; the audit trail holding every booking and every refusal with a user and time and no patient details; a dentist who
can see the schedule but not book (the API returns 403) and billing with no Schedule page; and a keyboard-only booking - then axe scans of each state in light and dark.
Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.


## ALV-004-C01: calendar walkthrough

`e2e/calendar-real-backend.spec.ts` is the un-mocked run for the calendar. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough
above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:E2E_DB_NAME = "AlveraE2E"                      # the database the API is using: one step moves a booked appointment ten years back with a SQL UPDATE
$env:CALENDAR_E2E_OUT = "C:\tmp\calendar-e2e"       # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.calendar.config.ts
```

It needs `sqlcmd` on the path (the API refuses to book a start in the past, so the no-show demonstration arranges a long-past appointment directly in the database, and
says so). It configures the practice through the real configuration API, then signs in as front-desk users, a dentist and a billing user and shows: the day view placing
real appointments in the right provider column at the right time and size with working hours drawn and filters working; booking through the drawer with provider, operatory,
patient, working-hours and blocked-time conflicts each explained in words and nothing booked; a reschedule, a refused reschedule and the history that remembers where it
was; a second user's change making the first user's edit a reported conflict instead of an overwrite; six appointments racing into one slot producing one winner and two
users moving the same appointment producing one change; a cancellation with a reason staying on the calendar marked cancelled and freeing the slot; a no-show refused
before the start and recorded after it; patient overlap refused and the week view putting overlapping appointments side by side; a dentist who can look but not change (403)
and billing with no Calendar; a keyboard-only reschedule; the audit trail holding every action with user and time and no patient details or reasons - then axe scans of each
screen and state in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.


## STORY-011: patient flow walkthrough

`e2e/patient-flow-real-backend.spec.ts` is the un-mocked run for patient flow. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough
above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:FLOW_E2E_OUT = "C:\tmp\flow-e2e"       # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.flow.config.ts
```

It configures the practice through the real configuration API, then signs in as front-desk users, a dentist and a billing user and shows: a scheduled patient checked in
from the calendar drawer and then started and completed (the status in the drawer, on the calendar block and on the server); a checked-in patient going straight to completed;
out-of-order, repeated, stale and unknown moves, and cancel / no-show / reschedule refused after check-in; a second user moving the patient first (a repeat is a quiet no-op, a
different move is reported and not applied); six simultaneous check-ins producing one; a dentist who can see the flow but not move it (403); a keyboard-only check-in and
completion; the audit log holding every move with user and time and no patient details - with axe scans in light and dark. It needs no `sqlcmd`. Like the other real-backend specs it
is excluded from the default mocked `npm run test:e2e`.


## ALV-011-C01: visit board walkthrough

`e2e/visit-board-real-backend.spec.ts` is the un-mocked run for the visit board. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above
(including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:E2E_DB_NAME = "AlveraE2E"                       # the database the API is using: one step makes a seated appointment look left open from days ago with a SQL UPDATE
$env:BOARD_E2E_OUT = "C:\tmp\board-e2e"             # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.board.config.ts
```

It needs `sqlcmd` on the path (the API refuses to book a start in the past, so the "visit left open from an earlier day" is arranged directly in the database, and says so). It configures the practice
through the real configuration API, creates six staff (front desk x2, assistant, dentist, office manager, billing), five patients and two required form templates with real signed, started and missing
forms, then shows: the board with the form cue reporting the real signing state; one visit travelling all eight states across four different people (completion asked first) with a complete, attributed history;
a second patient refused in an occupied room with the refusal in words, then seated once the room is free; reassignment that never moves the booking, and the occupied-room refusal for a seated patient; a
second desk's change making the first desk's press a reported conflict; the open board updating by itself within its 15-second refresh (this step waits for it, so the run takes ~40 seconds); cancelled and
no-show apart from completed; a visit left open from an earlier day carried onto today's board with its room still blocked; a dentist who can only do chairside moves and billing with no way in, with STORY-011's own
endpoints unchanged; a keyboard-only move; the audit log holding every move once with user and time and no patient details - with axe scans of four board states in light and dark. Like the other real-backend specs
it is excluded from the default mocked `npm run test:e2e`.
