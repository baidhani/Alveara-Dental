# ALV-N021 - application-wide time-handling audit (read-only)

Date: 2026-11-14. Session CC-20261109-8w3d. Tree: `4253887` (A1-R02 package). **Read-only: no code changed, no test run.** Requested by the owner after the A1-R02 package, to find every time handling in the application, not only the mapped findings.

## 1. Method

Every source line (tests and test fakes excluded) matching any of these was read in context:

| Side | Patterns |
|---|---|
| Server (`src/Alveara.Api`, migrations included, designer/snapshot files excluded) | `DateOnly.FromDateTime`, `.UtcDateTime`, `UtcNow.Date`, `DateTime.Now/Today`, `DateTimeOffset.Now`, `ToLocalTime`, `.LocalDateTime`, `TimeZoneInfo.Local`, any `yyyy`/`HH` format, `.DayOfWeek`, `.Date`, `FromPracticeLocal`, `StartOfDayUtc`, `ParseExact`, SQL `GETDATE/SYSDATETIME/SYSDATETIMEOFFSET/GETUTCDATE/AT TIME ZONE/DATEADD/DATEDIFF/CAST AS date/CONVERT(date` |
| Client (`src/alveara-client/src`) | `toLocaleDateString/String/TimeString`, `new Date(`, `Date.now`, `Date.parse`, `getHours/getDate/getDay/setHours/setDate`, `getTimezoneOffset`, `Intl.DateTimeFormat`, `timeZone`, `toISOString` (148 + 72 matches; the first set is classified line by line in `.alveara/handoffs/ALV-N021/A1-R01-evidence/DATE_SITE_CLASSIFICATION.md`) |

## 2. What is correct (verified by reading)

- **Storage:** every instant is a `DateTimeOffset` stored as UTC or with its own offset; every calendar day (date of birth, effective dates, due dates, document dates, planned dates) is a `DateOnly`. No `DateTime.Now`, `DateTime.Today`, `ToLocalTime` or `TimeZoneInfo.Local` anywhere on the server.
- **"Today" on the server:** every one (billing responsibility, procedures, treatment plans, patients' age and future-DOB check, external cases and their jobs, documents, imports, reports baseline, visit board) is `DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow))`: the practice's day.
- **Instant to practice day on the server:** recall due dates, recall lapsed check, document library dates, completed-procedure and schedule report grouping, the export package's practice-time text, the billing timeline (F-118, fixed in A1), the perio label (fixed in A1), version-2 aging (A1).
- **Practice day to instants on the server:** appointment and blocked-time input go through `clock.FromPracticeLocal` with DST gap/overlap rejected and caught (400); recall and external-case due times use a fixed local hour with the earlier instant on an overlap; report day ranges use `StartOfDayUtc`, which also handles a day whose midnight does not exist.
- **SQL:** `SYSDATETIMEOFFSET()` in three places (policy version stamp, two data migrations) records an instant with its offset, which is correct; no date truncation in SQL, triggers or migrations.
- **Machine-format instants:** hashes, document canonical text, export timestamps, CSV "made at (UTC)" are written in UTC with `InvariantCulture`.
- **Client practice-zone screens:** Calendar and Flow Board take "today" and "now" from the practice zone (`practiceNow(timeZoneId)`); reports, report drill-downs, baselines and the export history show instants in the practice zone; the Flow Board's minute counters use the server's clock plus elapsed time.
- **Client date-only values:** built from their parts (`formatDateOnly`, `T00:00:00` local parse, `new Date(y, m-1, d)`) everywhere after A1.

## 3. Findings (new; none fixed)

| # | Finding | Where | Effect | Severity |
|---|---|---|---|---|
| **T-1** | **The client uses two different zones.** Calendar, Flow Board, reports and exports use the **practice** zone. Every other screen (about 70 sites: audit, histories, notes, diagnosis, perio, forms, billing documents and ledger, notifications, merge, backup, system status, treatment plans) shows instants in the **browser's** zone, and the "today" defaults (document date, external-case status date, scheduling "tomorrow", report presets, date-of-birth limit) use the browser's day. The `datetime-local` inputs (completion performed time, follow-up due, vitals measured time) are read in the browser's zone, while appointment and blocked-time inputs are read in the practice zone. | client-wide | Correct only while every browser runs in the practice's zone, an assumption written in one code comment (`externalCaseFormat.todayInput`) but not enforced or shown. A browser in another zone (a laptop on travel, a misconfigured PC, a remote biller) sees times shifted, gets a "today" the server may refuse as future, and records performed times shifted. | **High for a multi-site or remote future; Low today** (single practice, PCs in its zone) |
| **T-2** | **Server formatting uses the machine culture.** 21 server sites format dates with `ToString("yyyy-MM-dd")` / `"HH:mm"` / `"yyyy-MM-ddTHH:mm"` without `InvariantCulture` (patients' DOB in API responses, duplicate/merge views, edit-history values, appointment local times, blocked-time local times, provider windows, report day rows and filters, recall and external-case job payloads), and two `DateOnly.ParseExact` calls (recall, external-case jobs) parse with it. Nothing pins the process culture. | server | On a Windows server whose culture uses a non-Gregorian calendar (th-TH Buddhist, ar-SA Hijri) the year is wrong (a DOB of 1985 is sent as 2528); on a culture whose time separator is not ":" the `HH:mm` strings change and appointment times no longer parse. On en-US (this machine) nothing is wrong. | **Medium** (latent; depends on the Windows installer's target machines, ALV-N013) |
| **T-3** | **Day start that does not exist.** The Calendar list (`AppointmentsController`) and the Flow Board (`VisitBoardService`) convert the day's midnight with `FromPracticeLocal` and the reject policy; reports use the safe `StartOfDayUtc`. In a zone whose clocks change at midnight, that day's calendar and board answer 400 instead of showing the day. | server | Not possible in America/Chicago (changes at 02:00); possible in zones that change at midnight. Two implementations of one rule. | **Low** |
| **T-4** | **The statement view's dates follow the browser zone** while version-2 aging counts practice days (already disclosed in A1-R01). A case of T-1. | `BillingDocumentView.tsx` | Same as T-1. | Low |

Already handled elsewhere, not repeated as findings: the check-in day rule and "cannot complete before its day" (A2: F-047, F-071); date-format preference (PKG-14/PD-37).

## 4. Recommendation (needs the owner's and the reviewer's decision; nothing started)

- **T-1:** a design decision, not an A1 fix. Options: (a) show and enter every time in the practice zone (one shared formatter and one shared input parser using the practice `timeZoneId` the client already loads, as Calendar and reports do); (b) keep the browser zone but detect a browser zone different from the practice zone and show a persistent warning; (c) record the assumption as an accepted limitation. (a) is the complete fix; it touches about 80 client sites and fits PKG-14 (date/time presentation) or a new story.
- **T-2:** small and mechanical: pin `CultureInfo.DefaultThreadCurrentCulture`/`UICulture` to invariant for machine formats at startup, or add `CultureInfo.InvariantCulture` at the 23 sites, plus a test that runs a formatter under th-TH. Could be an A1 addition or part of ALV-N013 (Windows installer).
- **T-3:** replace the two midnight conversions with the existing `StartOfDayUtc` rule (one shared helper) plus a test with a midnight-transition zone. Fits A2, which already changes the Calendar and Board day logic.
- **T-4:** with T-1.

Nothing here changes the A1 implementation's correctness for the mapped findings. A1-R02 is with the reviewer as packaged.
