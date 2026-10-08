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

## ALV-N011: patient safety walkthrough

`e2e/safety-real-backend.spec.ts` is the un-mocked run for patient-safety alerts, clearances and the live-board indicator. Prepare a fresh database and start the API exactly as for
the STORY-003 walkthrough above (including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:SAFETY_E2E_OUT = "C:\tmp\safety-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.safety.config.ts
```

It configures a practice with one provider and books a visit for each of three patients, signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an
assistant, a front-desk user, a practice manager, a billing user and the administrator, and shows: an empty chart with no alerts invented and what is NOT established said in words;
allergies and medications read from the clinical record (a milder or resolved item not shown as active, an unrecorded severity flagged and never guessed); an alert stated with its
source (refused without one, on screen and in the API), acknowledged by a second clinician and shown to be still active, the old revision refused after a change, resolved only with a
reason and kept with who, when and why, reopened, with the full history; the clearance workflow from requested to received without its document (said in words) to the document
attached later to resolved, a clearance still waiting refused; the safety strip in the patient header and above the documentation sections of an open encounter; the live visit board
giving authorized roles only "Safety alert on file" and "Clearance open" - two booleans, with no diagnosis, allergy or medication anywhere in the response - and front desk and the practice
manager nothing at all; a stale alert edit refused with the conflict banner and the typed text surviving the reload; a keyboard-only acknowledgement; front desk, practice manager and
billing denied everywhere and an assistant reading and acknowledging but not changing; and the audit log holding every step with user and time and no safety content - then axe scans of
each screen and state in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.

## STORY-006: interactive odontogram walkthrough

`e2e/odontogram-real-backend.spec.ts` is the un-mocked run for the odontogram. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above (including
`AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:ODONTOGRAM_E2E_OUT = "C:\tmp\odontogram-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.odontogram.config.ts
```

It signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an assistant, a front-desk user, a billing user and the administrator, creates two patients, and
shows: an empty chart with the 32 permanent teeth, "nothing recorded" (never healthy) and the patient-safety strip in view; a condition recorded on a selected tooth in each of the four
states and saved exactly as chosen, with the tooth stored as its FDI key whatever number the chart shows; a treatment planned by one person and completed by another, the chart following and
the history showing each step with who and when; a wrong entry withdrawn with a required reason and kept in the history; a wrong tooth, a Universal number, a surface that does not exist on
the tooth, a surface on a whole-tooth condition, an unknown condition or state, and a finding recorded again in a different state each refused with the field named, and the same finding
again a quiet repeat; a stale change refused with the conflict banner, the other person's change standing, and a reload letting the first person finish; a keyboard-only select and plan;
front desk and billing denied everywhere and an assistant reading but not changing; and the audit log holding every record, state change and withdrawal with user and time and no clinical
content - then axe scans of the chart, a tooth with its forms open, and the 768 px tablet layout (no horizontal scroll), in light and dark. Like the other real-backend specs it is excluded
from the default mocked `npm run test:e2e`.

## ALV-006-C01: full odontogram walkthrough

`e2e/odontogram-longitudinal-real-backend.spec.ts` is the un-mocked run for the extended odontogram. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above
(including `AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:ODONTOGRAM_LONGITUDINAL_E2E_OUT = "C:\tmp\odontogram-longitudinal-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.odontogram-longitudinal.config.ts
```

It signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an assistant, a front-desk user, a practice manager and the administrator, creates a child and an
adult, and shows: one patient with findings on permanent and primary teeth, drawn as permanent, primary or mixed (52 teeth in four arches), surface-specific findings on primary teeth, and
switching the view changing nothing (the same findings before and after, and the primary finding listed rather than hidden when it is not drawn); an implant not offered for, and refused on, a
primary tooth, and a missing tooth taking no other finding (refused with a message) until its entry is withdrawn, with an implant allowed; a tooth that cannot become missing while other findings stand on it (a planned missing tooth beside a caries refused when completed, in the screen and over HTTP, with nothing changed, then completed once the caries is withdrawn), and four dentist-versus-hygienist races over HTTP on one tooth each, exactly one request winning; a dentist adding a condition from the screen, a hygienist
recording with it but unable to change the catalogue (no controls, 403), retiring it needing a reason, the retired condition refused for new findings (409) and absent from the form while the
old finding still reads as it did, and reactivating it, with the condition's history; a tooth's history listing every finding and change, withdrawn ones included, in order, and still complete after
a finding was withdrawn and entered again; links to a diagnosis, a treatment plan and a procedure shown beside the finding and in the history, repeated links quiet, the finding's version unchanged;
a stale catalogue change refused with the conflict banner naming a condition type, the other screen's change standing, and a reload letting the first person finish; arrow keys moving around the
mixed chart without selecting; front desk and the practice manager denied everywhere and an assistant and hygienist reading but not changing the catalogue; the audit log holding every catalogue
change and link with user and time and no clinical or catalogue content; and no numbering anywhere in what the server holds - then axe scans of the mixed chart, a tooth's history with its links, the
catalogue with its add form, and the 768 px tablet layout (no horizontal scroll), in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.
The original `odontogram-real-backend.spec.ts` (STORY-006) is run alongside it as the parent regression.

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

## STORY-012: periodontal charting walkthrough

`e2e/perio-real-backend.spec.ts` is the un-mocked run for periodontal charting. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above (including
`AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:PERIO_E2E_OUT = "C:\tmp\perio-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.perio.config.ts
```

It signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an assistant, a front-desk user, a billing user and the administrator, creates two patients, and shows:
an empty chart (192 empty boxes, "none taken, not that the gums are healthy", the patient-safety strip in view); depth, recession and bleeding charted on three sites and saved, with the tooth stored
as its FDI key whatever number is shown and attachment loss derived; incorrect data (99 mm, a missing recession) refused on screen with every problem named, the boxes marked, focus on the list and
a link to the box to correct, nothing saved, then corrected and saved as a new chart, and the same mistakes refused by the API for any caller with every problem listed and a missing value reported
rather than defaulted; the same key and chart returning the chart already saved, a different chart under a used key refused (409) and six simultaneous saves making one chart; a dropped connection
(the server stores the chart, the answer is lost) keeping what was typed and the retry making exactly one chart, and a save that cannot be made saying nothing was recorded; starting a new chart from
an earlier one leaving the earlier chart unchanged; the audit log holding one entry per chart with user and time and no clinical content; the hygienist charting, the assistant reading but refused
(403) on save, front desk and billing denied (403) with no tab, anonymous callers 401 and a save without a CSRF token refused - then axe scans of the grid with charts listed, with problems shown,
and the 768 px tablet layout (the page does not scroll sideways), in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.

## ALV-012-C01: step-by-step periodontal charting walkthrough

`e2e/perio-sessions-real-backend.spec.ts` is the un-mocked run for the complete periodontal charting. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above (including
`AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:PERIO_SESSIONS_E2E_OUT = "C:\tmp\perio-sessions-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.perio-sessions.config.ts
```

It signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an assistant, a front-desk user, a billing user and the administrator, creates five patients, and shows: no chart in
progress until one is started, then the first site of the entry order with the depth box focused and the safety strip in view; depth, Enter, recession, Enter saving a site and landing on the next in the
documented order, a tooth's sites saved only when the cursor leaves it, and the six sites reading differently; pus and plaque recorded only when asked for, B toggling bleeding, mobility and furcation set
per tooth (furcation only on a molar) and X marking a tooth not charted; a tooth recorded missing in the odontogram skipped and listed, refused by the server with `tooth_absent` and cleared by marking it not
charted; a wrong entry refused by the real server (a tooth that became missing while the sites were being typed) with every typed entry kept, the cursor on the entry to correct and X as the way out; a draft
changed by someone else shown as the conflict banner with the typed entry kept and the first writer's entry standing, then saved after a reload; **all 192 sites typed from the keyboard with the cursor never
losing its place**, finishing as one immutable chart; a chart compared with the one before it in words (improved, worse, about the same, sites in only one chart counted separately), a diagnosis link added by
reference and repeated quietly, and the chart in progress compared live; the audit log holding every start, save, finalize and link with user and time, one chart entry per chart and no clinical content; the
hygienist charting, the assistant reading but refused (403) on every write, front desk and billing denied with no tab, anonymous callers 401 and a write without a CSRF token refused - then axe scans of the
entry screen, with a problem shown, the comparison with every site listed and the 768 px tablet entry layout (the page does not scroll sideways), in light and dark. Like the other real-backend specs it is
excluded from the default mocked `npm run test:e2e`. `perio-real-backend.spec.ts` (STORY-012) is run alongside it as the parent regression.

## STORY-013: structured diagnoses walkthrough

`e2e/diagnoses-real-backend.spec.ts` is the un-mocked run for structured diagnoses. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above (including
`AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:DIAGNOSES_E2E_OUT = "C:\tmp\diagnoses-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.diagnoses.config.ts
```

It signs in as a dentist and a hygienist (with staff profiles, so attribution shows names), an assistant, a front-desk user, a billing user and the administrator, creates three patients, starts a real
encounter for each through the clinical documentation API, and shows: no form and a reason when a patient has no encounter, then the form and an honest empty list (never "nothing is wrong") with the
patient-safety strip in view; a diagnosis recorded for an encounter, linked to the patient and the encounter, the tooth stored as its FDI key whatever number is shown, and the treatment-plan reference
normalized and shown as unresolved with the statement that it proves nothing; incorrect data refused on screen with every problem listed and linked, focus on the list and nothing sent, and the same
mistakes refused by the API for any caller; an encounter of another patient and one that does not exist refused identically; a correction that keeps the reference unless it is explicitly replaced or removed,
each with its reason and every value in the history; withdrawing with a reason, the diagnosis and its reference kept and shown on request; the same key returning one diagnosis, a different entry under it
refused, eight simultaneous saves making one and a dropped connection keeping what was typed and making exactly one; a diagnosis changed by someone else shown as the conflict banner with the typed entry
kept; the audit log holding every record, correction and withdrawal with user and time and none of what was diagnosed; the hygienist recording, the assistant reading but refused (403), front desk and
billing denied with no tab, anonymous callers 401 and a write without a CSRF token refused - then axe scans of the list, with problems shown, with a correction and the history open and the 768 px
tablet layout (the page does not scroll sideways), in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.

## ALV-013-C01: diagnosis structure walkthrough

`e2e/diagnoses-structure-real-backend.spec.ts` is the un-mocked run for the companion. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above (including
`AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:DIAGNOSES_STRUCTURE_E2E_OUT = "C:\tmp\diagnoses-structure-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.diagnoses-structure.config.ts
```

It creates two patients with real encounters, a real odontogram finding and a real periodontal chart each, and shows: a diagnosis with no coding stored as manual and uncoded; coding, source and region typed on
screen stored, read back, listed, in the history and shown as chips (never as checked); incorrect structure refused on screen with every problem linked and nothing sent, and by the API naming each problem
(including a request to mark the treatment-plan reference resolved); an amendment that asks why, keeps what it replaced with who, when and why and carries the plan reference through unresolved, and one that
changes nothing being quiet; resolve and reactivate with a reason, the diagnosis staying on the list; links offered only from the patient's own findings and charts, a repeat adding nothing, another patient's
record and a missing one refused in identical words; a stale amendment showing the conflict with what was typed kept, and a withdrawn diagnosis refusing every structural change; the audit log holding every new
event with user and time and none of what was diagnosed or coded; the hygienist amending, the assistant reading but refused and shown no controls, front desk and billing denied, anonymous callers 401 and a write
without a CSRF token refused; then axe scans with the forms open and the 768 px tablet layout, in light and dark. Like the other real-backend specs it is excluded from the default mocked `npm run test:e2e`.
STORY-013's own walkthrough (`playwright.diagnoses.config.ts`) is the parent regression and is run separately.

## ALV-N005: procedure and fee catalog walkthrough

`e2e/procedures-real-backend.spec.ts` is the un-mocked run for the procedure catalog. Prepare a fresh database and start the API exactly as for the STORY-003 walkthrough above (including
`AuthAttemptRateLimit__PermitLimit=500`), then from `src/alveara-client`:

```powershell
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
$env:PROCEDURES_E2E_OUT = "C:\tmp\procedures-e2e"   # optional: where the JSON results and screenshots go (use a Windows path)
npx playwright test --config=playwright.procedures.config.ts
```

It signs in a billing user, a dentist and a hygienist and shows: the catalog reached from the navigation; an incomplete entry refused on screen beside each field; a code that looks like CDT refused by
the server with what was typed kept; a procedure added with its fee; a fee change saved as a new version with a required reason, both fees and who changed it readable in the history; inactivate (reason
required) removing it from the planning list and reactivate restoring it; the dentist reading but offered no controls; the hygienist given no link and told permission is needed; the API refusing a
dentist's change, a hygienist's read, a missing CSRF token, a stale version and a duplicate code; and axe scans in light and dark. Like the other real-backend specs it is excluded from the default
mocked `npm run test:e2e` run.
