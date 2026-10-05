# Periodontal charting (STORY-012)

**Requirement:** REQ-009 - the system must support periodontal charting with probing depth, recession, and bleeding.
**Where it lives:** the patient workspace's **Periodontal** tab (`/patients/:id/periodontal`), inside the clinical documentation module beside the odontogram.
**Who can use it:** anyone who may read clinical documentation (`ViewClinicalDocumentation`: dentist, hygienist, assistant, admin) can see the charts on record; saving a chart needs `ManageClinicalNotes` (dentist, hygienist, admin). Front desk, billing and the office manager cannot see the tab and are refused (403) by the API.

## What it does

A chart is one periodontal examination of one patient at one moment: for each probed **site** the **probing depth** and the **recession** (whole millimetres, 0 to 15) and whether the site **bled on probing** (yes or no).

- **Sites.** Six per tooth - distal, middle and mesial on the cheek side (`DB`, `B`, `MB`) and the same on the tongue side (`DL`, `L`, `ML`) - on the 32 permanent teeth, so a chart holds at most 192 readings.
- **The grid.** One row per tooth, upper then lower arch, in the numbering system in use (Universal by default, as in the odontogram). A tooth's stored identity is its **FDI key** (`"16"`), exactly as in the odontogram; the display number never travels to the server. Every box is labelled with its tooth, site and measure. A site left completely empty is not part of the chart. Enter moves to the next box, Shift+Enter back.
- **Derived, not stored.** Clinical attachment loss is probing depth plus recession. It is computed when a chart is read and shown, never entered and never stored, so it cannot disagree with its parts.
- **Charts on record.** Newest first, the latest open: who took it and when, the number of sites and teeth, the share of sites that bled, the number of sites 4 mm or deeper, the deepest site, and a table of every reading. No chart on record says "none has been taken, not that the gums are healthy"; a failed load says it could not load, never "none".
- **Corrections.** A saved chart is **never edited or deleted**. Changing a value and saving again records a new chart; "Start a new chart from these values" copies an earlier chart into the grid to do that (asking first if unsaved entries would be replaced). The earlier chart stays as the record of what was found then.

## How the promises are kept

| Promise | How |
|---|---|
| Records depth, recession and bleeding | `PerioService.RecordAsync` stores exactly what was sent per site; the read returns each reading with its derived attachment loss. |
| Incorrect data is rejected and the person is told what to correct | `PerioRules.Validate` judges the whole chart before anything is written and returns **every** problem (tooth, site, field, stable code, message); one problem means nothing is saved (`validation_failed`, 400). The screen applies the same rules first, marks each box, lists the problems in one place with links to the boxes and moves focus to the list. A value left out is reported (`required`), never defaulted to 0 mm or "no". |
| The database refuses bad data too | Check constraints on the tooth (32 permanent FDI keys, case-sensitive), the site (the six codes, case-sensitive), depth and recession (0 to 15) and the reading count (1 to 192); a unique key on (chart, tooth, site). A writer that bypasses the service still cannot store bad data. |
| Every chart is logged with user and time | The chart, its readings and its audit entry are saved **once**: if the audit write fails nothing is stored. The audit text never holds a tooth or a measurement. |
| A failed save says so and leaves nothing | A database failure becomes `save_failed` (503, "nothing was recorded, try again"), logged with its error class only; the screen keeps everything typed. |
| Retries are safe | Each save carries an idempotency key, unique per patient. The same key with the same readings (in any order) returns the chart already saved - no second chart, no second audit entry; the same key with different readings is refused (`idempotency_key_reused`, 409) rather than silently ignored; simultaneous saves with one key end with one chart. The screen keeps the key across retries of an unchanged chart and renews it when anything changes. |
| Charts are immutable | Triggers refuse any edit or delete of a chart or its readings. |
| Nothing is invented | An empty chart is not "healthy"; unentered sites are not zeros; attachment loss and the figures are arithmetic on what was recorded. |

## API

- `GET /api/patients/{patientId}/periodontal/charts?take=N` - the patient's charts, newest first (20 by default, at most 100). `ViewClinicalDocumentation`.
- `POST /api/patients/{patientId}/periodontal/charts` - body `{ idempotencyKey, readings: [{ toothKey, site, probingDepthMm, recessionMm, bleeding }] }`; returns the chart. `ManageClinicalNotes` plus a CSRF token.
- Refusals carry `error`, `message` and `problems`: `validation_failed` (400), `patient_not_found` (404), `idempotency_key_reused` (409), `save_failed` (503). A body of the wrong shape (a string where a number belongs) is the framework's ordinary 400.

## Schema (migration `AddPeriodontalCharting`)

`PerioExams` (patient, idempotency key, recorded by and when, reading count) and `PerioReadings` (chart, tooth, site, depth, recession, bleeding). Triggers 51064 (a chart is never edited or deleted) and 51065 (its readings are never edited or deleted); the next free number is 51066.

## Decisions

1. **A chart is immutable; a correction is a new chart.** A periodontal chart is a clinical record of a moment; editing it would erase what was found. The history keeps every chart.
2. **Permanent teeth only.** Primary teeth are not routinely probed; they are refused with their own message. Widening this later is a change to the key list and the database check.
3. **No negative recession.** Gingival overgrowth (the margin coronal to the cemento-enamel junction) is not modelled; recession is 0 or more. Recording it needs a different measure.
4. **One key per save, kept for an unchanged retry.** Reusing a key with different readings is refused so the second set is never lost without anyone knowing.
5. **Bleeding is a yes/no checkbox per site; unchecked means no.** A reading needs a depth and a recession, so bleeding alone is not a reading.
6. **The server's messages name teeth by FDI number**, whatever the grid shows; the screen says so when it shows them.

## Limits (recorded, not hidden)

- **Permanent teeth only; no negative recession; whole millimetres only** (decisions 2, 3 and the range 0 to 15).
- **Not linked to an encounter, diagnosis or treatment plan yet.** `STORY-013` (structured diagnosis linked to patient, encounter and treatment plan) is where that link belongs; a chart is per patient and moment.
- **No missing-tooth awareness.** The chart does not read the odontogram, so a tooth recorded there as missing can still be charted here; a clinician leaves it empty.
- **No mobility, furcation, plaque or suppuration measures**, and no automatic periodontal staging or grading.
- **No printed or exported chart**, and no comparison view between two charts beyond reading them side by side.
- **Only the latest 20 charts are listed by default** (up to 100 by the API); the screen does not page further.
- **A chart is saved whole.** There is no draft saved on the server; what is typed and not saved is lost if the page is closed.
- **A dropped connection after the server stored the chart** is recovered by saving again with the same key; a person who edits a value between the two attempts creates a second chart.

## Verification

Backend, against real SQL Server: `PerioRulesTests` (rules and boundaries), `PerioSchemaTests` (the database's own refusals, triggers), `PerioServiceTests` (behaviour, audit and save failure, idempotency and simultaneous saves), `PerioApiTests` (roles, CSRF, refusal shape, workflow). Frontend: `Perio.test.tsx` (rules, grid, problems, retries, history, start-from-earlier, read-only, axe). Real browser, un-mocked: `e2e/perio-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`).
