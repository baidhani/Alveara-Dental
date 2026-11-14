# ALV-N021 A1-R01: client date-site classification

Read-only pass on 2026-11-14 at `a18ce99` (brief section 3 estimated "about 94 client sites"). Search: `toLocaleDateString|toLocaleString|toLocaleTimeString|new Date(` over `src/alveara-client/src`, excluding `*.test.*`, `src/test/` and fake test servers: **148 matches in 93 files**.

The defect class looked for: a **date-only** value (a calendar day with no time: date of birth, target date, due date) parsed or produced as a UTC instant, or a **"today"** taken from the UTC day. An **instant** (a UTC timestamp from the server) shown in the viewer's own zone is correct and is not this class.

| Class | Sites | Verdict |
|---|---|---|
| A. Not a date: number and money formatting with `toLocaleString` (reports: Balances, Cancellations, Collections, Comparison, CompletedProcedures, LedgerProof, Metrics, NumberButton, PatientFlow, Production, RecordsPanel, Schedule, TreatmentPlans; `importFormat.money`, `formatFee`, SignInScreenTab character limit) | ~45 | not applicable |
| B. Instants shown in the viewer's clock: `new Date(isoUtc).toLocaleString()` / `toLocaleDateString()` / `toLocaleTimeString()` for created, changed, issued, recorded, occurred, started, voided, merged, reviewed, last-poll and lock-out times (AdminUserDetail, AuditLog, backup panels, billing documents/ledger/timeline/charge source, AppointmentPanel, clinical, record, diagnosis, external cases, forms, notifications, odontogram, patient history, merge, perio, policy history, procedures `when`, safety `when`/`day`, system status, treatment-plan `when`, `documentText.formatDateTime`/`encounterLabel`, `PrintPreview` prescribed time, `FlowBoardPage` clock, `followUpFormat.formatWhen`, `exportFormat` (explicit practice `timeZone` when known)) | ~70 | correct |
| C. Date-only built safely: `new Date(\`${d}T00:00:00\`)` (local midnight: `treatmentPlanRules.dayOf`, `procedureText.dateText`, `treatmentPlanLayout` target), `new Date(y, m - 1, d)` (`externalCaseFormat`, `reportFormat`, `dateOnly.formatDateOnly`), UTC day numbers on date strings with `getUTCDay` (`calendarLayout`) | ~12 | correct |
| D. "Today" from the local clock parts: `documentText.todayValue`, `externalCaseFormat.todayInput`, `SchedulePage.tomorrow`, `reportFormat.today` and month presets, `TreatmentPlanPatientCopy` printed date | ~8 | correct |
| E. Instant input and output for `datetime-local` boxes: `followUpFormat.toLocalInput/fromLocalInput`, `completionReviewRules`, `TreatmentPlanAdoptCompletion` / `TreatmentPlanReviewComplete` default, `clinicalNotesApi` measured time | ~6 | correct (viewer's clock both ways) |
| F. Session/system instants not displayed as dates: `AuthContext` expiry, `useSystemStatus` confirmation time | 2 | correct |
| **G. Wrong day** | **2** | **fixed in A1** |

**Class G:**
1. `pages/prescription/workflow/PrintPreview.tsx`: the date of birth through `new Date(iso).toLocaleDateString()` (F-107). Fixed with `formatDateOnly`.
2. `components/PatientFieldsForm.tsx:7`: `today = () => new Date().toISOString().slice(0, 10)`, the date-of-birth box's `max`, is the UTC day (tomorrow on a Chicago evening; yesterday on a morning east of UTC, which blocks entering a newborn's date of birth). Not in the finding register; owner approved fixing it within A1. Fixed with `todayDateOnly()`.

**Recorded, not changed:** the statement view (`BillingDocumentView.tsx` `day()`) shows service and ledger dates of instants in the viewer's zone, while version-2 aging counts days in the practice zone. Both agree when the browser runs in the practice zone (the product's documented assumption, see `externalCaseFormat.todayInput`); a browser in another zone could show a service day that differs from the day the aging counted from. Candidate for the date-format/zone work in PKG-14, not an A1 change.

Counts per class are approximate (a line can hold two sites); every line was read.
