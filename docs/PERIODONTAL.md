# Periodontal charting (STORY-012 and ALV-012-C01)

**Requirement:** REQ-009 - the system must support periodontal charting with probing depth, recession, and bleeding. `STORY-012` built the course charting (a whole chart saved at once); `ALV-012-C01` completes it for production: the six-site model spelled out, suppuration, plaque, mobility and furcation, missing and excluded teeth, a chart entered as a draft with full-mouth keyboard entry, links to diagnoses and history, and comparison with an earlier chart. Everything `STORY-012` promised still holds and its tests are unchanged.
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
- **The grid (whole-chart) save does not read the odontogram**, so a tooth recorded there as missing can still be charted through the grid; step-by-step entry does read it and refuses (see ALV-012-C01 below). The grid is `STORY-012`'s contract and was left as it was.
- **No automatic periodontal staging, grading or diagnosis**, and no printed or exported chart.
- **Only the latest 20 charts are listed by default** (up to 100 by the API); the screen does not page further.
- **The grid saves a chart whole.** It keeps no draft on the server; what is typed there and not saved is lost if the page is closed (step-by-step entry saves as it goes).
- **A dropped connection after the server stored the chart** is recovered by saving again with the same key; a person who edits a value between the two attempts creates a second chart.

---

# ALV-012-C01: complete six-site charting and longitudinal comparison

**Where it lives:** the same **Periodontal** tab. An "Entry method" switch chooses between the **Grid** (`STORY-012`, all sites at once, the default) and **Step by step (keyboard)**. Charts on record, links and comparison are below both.
**Who can use it:** unchanged - reading needs `ViewClinicalDocumentation` (dentist, hygienist, assistant, admin), writing needs `ManageClinicalNotes` (dentist, hygienist, admin), plus a CSRF token on every write.

## What it adds

- **The six sites, spelled out.** `DB`, `B`, `MB` on the cheek side and `DL`, `L`, `ML` on the tongue side are the stored identity and never change. What a site is *called* depends on the tooth and is presentation only: "mesial palatal" on an upper tooth, "mesial lingual" on a lower one, "mid facial" on a front tooth, "mid buccal" on a back tooth. All six read differently on all 32 teeth.
- **Measures.** Per site: probing depth, recession, bleeding (as before), plus **suppuration** and **plaque**, which may be true, false or **not assessed** (null; a chart saved without them is unchanged). Per tooth: **mobility** (grade 0 to 3) and **furcation** (grade 0 to 3, multi-rooted teeth only: molars and the first upper premolars), each with not assessed as a value of its own.
- **One attachment-loss rule.** CAL = probing depth + recession, in millimetres, at the same site. It is **derived, never stored**, so it cannot disagree with its parts; because recession is never negative, CAL is never less than the depth. The rule is a tested function and a test asserts there is no field to store it in.
- **Missing and excluded teeth.** A tooth can be marked **not charted** (excluded) in a chart; it carries no readings or grades (the database and the rules both refuse the contradiction) and is skipped by the entry order. A tooth the **odontogram records as missing** (an active Absent-effect finding in Existing or Completed) is also skipped and listed, and a reading on it is refused (`tooth_absent`) with the way out named (mark it not charted, or correct the odontogram). Only the teeth a save touches are judged, so a change in the odontogram never blocks an unrelated save; it does stop the final save of a chart that still charts a now-missing tooth.
- **A chart entered as a draft.** A **session** is a draft saved into as the person goes, then **finalized** into the immutable chart of `STORY-012` (same table, same history, same audit entry). One open draft per patient (a unique index): starting returns the open one. Every save echoes the draft's **row version**; a stale one is the shared 409 conflict and nothing is merged.
- **Full-mouth keyboard entry.** One documented order, shared by the server and the screen: along the cheek side of the upper arch (patient's right to left), back along the tongue side, then the lower arch the same way. Within a tooth the sites run in the direction of travel. The cursor shows "Tooth 3, mesial palatal · site 14 of 192"; type the depth, Enter, the recession (0 if none), Enter. B toggles bleeding, S pus, P plaque (when those measures are being recorded), X marks the tooth not charted, Shift+Enter goes back. The order, the site names and the cue thresholds exist in both C# and TypeScript, and a SHA-256 of the whole sweep is asserted in both languages so the screen's order cannot drift from the server's.
- **Entry never loses what was already valid.** What is typed waits in a buffer that is saved into the draft when the cursor leaves a tooth (or on demand). A save is a batch applied together or not at all: if any entry in it is wrong, the whole batch is refused, every problem is named, the draft is exactly as it was (not even its version moves) and the screen keeps everything typed, marking the entry to correct. A stale draft shows the conflict banner and a reload that keeps what was typed; a dropped connection keeps it too.
- **Links.** A finalized chart can be linked, by reference, to a **diagnosis, treatment plan, encounter or history entry** (the records may not exist yet, so the reference is plain text). Append-only; the same link again is quiet; linking never changes the chart. Who linked and when is shown.
- **Comparison and trend.** A saved chart (or the draft being entered) is compared with an earlier finalized chart (by default the latest one before it). Whole-chart figures then and now (sites, mean depth, mean attachment loss, % bleeding, sites 4 mm or deeper, sites 6 mm or deeper, sites with pus, % plaque among those assessed) with the plain difference, and site by site: depth change, attachment-loss change, bleeding then and now, and a result in words. **Improved or worse only when the depth moved by 2 mm or more** (a smaller move is within the error of measuring); sites charted in only one chart are listed and counted separately, never dropped or averaged in. Teeth whose state or grades changed are listed.
- **Visual cues are not a diagnosis.** Depth of 4 mm or more ("deeper"), 6 mm or more ("very deep"), recession, bleeding and pus are marked in words as well as marks, with a line on screen saying they only draw the eye. A reading with no cue is not thereby healthy.

## How the promises are kept (ALV-012-C01)

| Promise | How |
|---|---|
| All six sites are distinguishable on every tooth | `PerioSiteModel.Describe` and its TypeScript twin; a test asserts six different readings on each of the 32 teeth, and the reference sites match in both languages. |
| CAL, mobility, furcation, suppuration and plaque can be recorded | Site columns and tooth records (migration `AddPerioSessionsAndMeasures`), accepted in sessions and, for the site measures, in the `STORY-012` save; the chart view returns them; derived CAL on every reading. |
| Full-mouth entry without losing site context | The cursor is held as a tooth and site, not a position, so skipping a tooth, going back or resuming a draft never shifts it; a test types all 192 sites from the keyboard and checks the position along the way. |
| Invalid values are rejected without discarding unrelated valid entry | A batch is judged as the draft would be after it; a refusal changes nothing. The screen keeps its buffer. |
| Prior finalized chart can be compared with the current one | `PerioComparison` (pure) and `PerioComparisonService` (picks the charts; both must belong to the patient); proved with a hand-worked fixture in C# and again on screen. |
| Partial save failure | A failed audit write or a failed insert halfway through a finalize leaves no half-chart and the draft open (both tested against real SQL Server); a failed batch save applies none of the batch. |
| Stale session edit | The draft's row version; two people saving the same version at once give one success and one 409. |
| Finalized history is preserved | A closed session and its entries can never change and a session is never deleted (triggers 51067 to 51069); charts and tooth records are immutable (51064 to 51066); links are append-only (51070). |
| Clinical permissions and audit | Same permissions as `STORY-012`; start, each save, finalize, abandon and link write a PHI-free audit entry (never a tooth or a measurement) in the same save; a chart has exactly one `PerioExamRecorded` entry. |

## API (ALV-012-C01)

- `GET /api/patients/{id}/periodontal/session` - the open draft as `{ session }`, or `{ session: null }`.
- `POST /api/patients/{id}/periodontal/sessions` - start a draft (the open one if there is one; 200 either way).
- `GET /api/periodontal/sessions/{sessionId}` - a session with its readings, tooth records, missing teeth and the next site.
- `POST /api/periodontal/sessions/{sessionId}/entries` - body `{ rowVersion, readings, teeth, clearSites, clearTeeth }`; applied together or not at all.
- `POST /api/periodontal/sessions/{sessionId}/finalize` and `/abandon` - body `{ rowVersion }`; finalizing a finalized session returns its chart.
- `GET /api/periodontal/charts/{examId}` - a chart with readings, tooth records and links; `POST .../links` with `{ linkType, reference }`.
- `GET /api/patients/{id}/periodontal/comparison?currentExamId=|currentSessionId=&previousExamId=` - the comparison.
- Refusals add `session_not_found` and `exam_not_found` (404), `session_closed` (409), `row_version_required` and `row_version_invalid` (400), `no_previous_chart` (404); a stale edit is the shared `concurrency_conflict` (409); `validation_failed` lists every problem, now including `tooth_absent`, `excluded_tooth`, `furcation_not_applicable`, `duplicate_tooth` and `out_of_range` grades.

## Schema (migration `AddPerioSessionsAndMeasures`)

Nullable `Suppuration` and `Plaque` on `PerioReadings`; `PerioToothRecords` (chart, tooth, mobility, furcation, excluded); `PerioSessions` (status Draft, Finalized or Abandoned, who and when, the chart it became, a row version, one Draft per patient), `PerioSessionReadings` and `PerioSessionTeeth` (the draft's entries); `PerioExamLinks`. Check constraints refuse grades outside 0 to 3, a furcation on a single-rooted tooth, grades on an excluded tooth, a session whose closing facts do not match its status, and an out-of-range or malformed site, tooth or link type. Triggers 51066 (tooth records immutable), 51067 (a session is never deleted), 51068 (a closed session never changes), 51069 (a closed session's entries never change) and 51070 (links append-only); the next free number is 51071.

## Decisions (ALV-012-C01)

1. **The attachment-loss rule is derivation, not storage** (depth + recession), so it can never disagree with its parts.
2. **The immutable chart stays the finalized form; a draft is a separate editable record.** `STORY-012`'s save and tests are untouched, and a finalized session becomes an ordinary chart.
3. **New measures are optional and tri-state** (true, false, not assessed), so a measure not taken is never recorded as none. On screen they are recorded only when ticked for the visit.
4. **A refusal never changes the draft.** A batch is judged whole, so valid entries are neither lost nor half-applied.
5. **One documented entry order**, a snake along each arch, held identical in C# and TypeScript by a checksum test.
6. **Only the teeth a save touches are judged**, so unrelated odontogram changes cannot block entry; the final save re-checks everything.
7. **A change counts as better or worse at 2 mm**, a plain stated threshold; the trend is by probing depth, with attachment-loss change shown beside it rather than combined.
8. **The grid save was left as `STORY-012` had it** (it does not read the odontogram), rather than changing an approved contract.
9. **A draft is discarded by being abandoned, never deleted**, so what was entered stays in the record.

## Limits (ALV-012-C01)

- **Furcation is one grade per tooth**, not per furcation site (buccal, mesial, distal), and mobility is Miller grades 0 to 3 only.
- **Permanent teeth only; no negative recession; whole millimetres 0 to 15** (as `STORY-012`).
- **One open draft per patient**; a second person starting gets the first draft. There is no merge of two people's entries (a stale save is refused).
- **What is typed since the last save is lost if the page is closed**, at most the sites of the current tooth; the screen warns before leaving while anything is unsaved.
- **The comparison is by probing depth at a 2 mm threshold**; there is no staging, grading, risk score or charted trend over more than two charts (the model supports adding them later).
- **The step-by-step order is fixed** (cheek side across, tongue side back, upper then lower); other orders are not offered.
- **A link points at a record by plain reference**; nothing checks that a diagnosis or plan with that reference exists (those stories are not built).
- **Teeth recorded missing are read at save time only**; a tooth that becomes missing later does not change a chart already finalized.

## Verification

Backend, against real SQL Server: `PerioRulesTests` (rules and boundaries), `PerioSchemaTests` (the database's own refusals, triggers), `PerioServiceTests` (behaviour, audit and save failure, idempotency and simultaneous saves), `PerioApiTests` (roles, CSRF, refusal shape, workflow). Frontend: `Perio.test.tsx` (rules, grid, problems, retries, history, start-from-earlier, read-only, axe). Real browser, un-mocked: `e2e/perio-real-backend.spec.ts` and, for ALV-012-C01, `e2e/perio-sessions-real-backend.spec.ts` (see `docs/testing/REAL_BACKEND_E2E.md`). ALV-012-C01 adds `PerioSiteModelTests`, `PerioChartValidatorTests`, `PerioSessionSchemaTests`, `PerioMigrationTests`, `PerioSessionServiceTests`, `PerioComparisonTests`, `PerioComparisonServiceTests` and `PerioSessionApiTests` (backend) and `perioSiteModel.test.ts`, `PerioSessionApi.test.ts`, `PerioSteps.test.tsx` and `PerioCompare.test.tsx` (frontend).
