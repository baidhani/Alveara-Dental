# ALV-N021 decision request 02 - application-wide time handling (T-1 to T-4)

Date: 2026-11-14. Session CC-20261109-8w3d. Prepared for the independent reviewer at the product owner's request.

**Owner's request:** address all four findings within ALV-N021 if possible.

**Evidence:** `docs/design/ALV-N021_TIME_HANDLING_AUDIT.md` (commit `055c506`, read-only audit of every server and client time-handling site). No code has changed and no test has run for these findings.

**State of the story:**
- A1-R01 was CHANGES_REQUIRED (documentation only).
- A1-R02 is awaiting review (`Alveara_Handoff_ALV-N021_807014c_A1-R02.zip`).
- A2 is approved but not started; it may begin only after A1 closes (BRIEFING-R04).

## 1. The findings in short

| # | Finding | Severity |
|---|---|---|
| T-1 | The client mixes two zones. Calendar, Flow Board, reports and exports show and take "today" in the practice zone. About 70 other sites show instants in the browser's zone. The "today" defaults use the browser's day. The `datetime-local` inputs (completion performed time, follow-up due, vitals measured time) are read in the browser's zone. Correct only while every browser is in the practice's zone, which is assumed in one code comment and not enforced or shown. | Low today; high for remote or multi-site use |
| T-2 | 21 server date/time `ToString` calls with a `yyyy`/`HH` format, and two `DateOnly.ParseExact` calls, use the machine culture; the process culture is not pinned. Under th-TH a 1985 date of birth is sent as 2528. Under a culture whose time separator is not `:`, appointment local times change form. | Medium (latent; Windows installer targets) |
| T-3 | Calendar (`AppointmentsController.List`) and Flow Board (`VisitBoardService.BoardAsync`) convert the day's midnight with the reject policy. In a zone whose clocks change at midnight, that day answers 400. Reports already use the safe `ReportFilterResolver.StartOfDayUtc`. | Low (not Chicago) |
| T-4 | The statement view's dates follow the browser zone while version-2 aging counts practice days (a case of T-1). | Low |

## 2. Proposed placement (all inside ALV-N021)

A1 is not reopened: its package is under review and its scope is closed by A1-DECISION-01. The four findings go into the remaining attempts.

| Finding | Attempt | Why |
|---|---|---|
| T-3 | **A2** | A2 already rewrites the Calendar and Board day logic (check-in day rule F-047, completion day F-071). The day-start rule should be fixed once, where those rules live. |
| T-2 | **A2**, first commit | Small and mechanical. It also covers the appointment local-time strings A2 changes. |
| T-1, T-4 | **New attempt A3** (after A2 closes) | About 80 client sites plus three input contracts. Folding it into A2, which already holds the M1-M3 migrations and the checkout gate, would make one package too large to review well. |

**Alternatives for the reviewer:**
- (i) T-1/T-4 inside A2;
- (ii) T-1/T-4 as a separate story after ALV-N021 (for example within PKG-14);
- (iii) T-1 accepted as a documented limitation, with only the warning in option B below.

## 3. Proposed contracts

### T-2: culture-independent machine formats (A2)

1. Every server date/time formatted for an API, payload, key, log text or file uses `CultureInfo.InvariantCulture` (the 21 sites), and the two `ParseExact` calls parse with it.
2. The API process sets `CultureInfo.DefaultThreadCurrentCulture` and `DefaultThreadCurrentUICulture` to invariant at startup, as a second guard. Human-facing wording is English-only today, so nothing visible changes.
3. A guard test scans `src/Alveara.Api` for date/time `ToString`/`ParseExact` calls with a format string and no culture argument (same pattern as the existing source-scanning tests).
4. A behaviour test runs the patient DOB response, the appointment local time, a report day row and a recall job payload with the thread culture set to th-TH and to a culture with `.` as time separator. All values must be unchanged.
5. Negative control: remove the invariant argument at one site and show the guard and behaviour tests fail.

### T-3: one practice-day-start rule (A2)

1. Move `StartOfDayUtc` into the clock area as one shared helper: midnight that does not exist starts the day at the first instant that does; an ambiguous midnight takes the earlier instant.
2. Use the helper in `AppointmentsController.List`, `VisitBoardService.BoardAsync` and the reports.
3. Tests with a zone whose clocks change at midnight: the day shows the appointments of that day, with no 400. An existing Chicago day is unchanged.
4. Negative control: restore the reject conversion and show the new test fails.

### T-1 and T-4: one practice-zone time model on the client (A3)

**Decision needed (owner and reviewer):**
- **Option A (recommended, complete fix):** every instant is shown in the practice zone, every "today" is the practice's day, and every typed time means practice wall-clock time.
- **Option B:** keep the browser zone, but show a persistent banner when the browser's zone differs from the practice's.
- **Option C:** accept the limitation in writing.

**Option A contract:**
1. **Display.** One shared formatter, `formatInstant(iso, style)` in `components/`, uses the practice `timeZoneId` the client already loads (configuration snapshot). It replaces every class-B site of the classification (about 70) and the statement view (T-4). Reports and exports already do this and keep their behaviour. Every time shown carries the zone abbreviation where it could be misread, matching reports.
2. **"Today".** One `practiceToday()` replaces the browser-day defaults: document date, external-case date, scheduling default, report presets, DOB limit, patient copy. It uses the same rule Calendar uses (`practiceNow`).
3. **Typed times.** The three `datetime-local` inputs (completion performed time, follow-up due, vitals measured time) send the practice wall-clock text ("yyyy-MM-ddTHH:mm"), and the server converts it with `clock.FromPracticeLocal`, exactly as appointments do. DST gap and overlap are refused with a clear message. The API change is additive: the new `...Local` field is accepted, the existing `...Utc` field keeps working, and the client stops sending it. No migration is needed (storage stays UTC).
4. **Guard.** A client test fails on any `toLocaleString`/`toLocaleDateString`/`toLocaleTimeString` of an instant outside the shared formatter (source scan, with an allowlist for number and money formatting).
5. **Tests.** Each rule runs with the test process in zones behind, equal to and ahead of the practice zone (`process.env.TZ`, as in A1). The three inputs are tested at a DST gap, at an overlap and in the evening across the UTC day. Server tests cover the new `...Local` fields, including refusal of a nonexistent time.
6. **Negative controls:** one display site back on the browser zone; one input back on browser conversion.
7. **Visible change, disclosed:** a user whose computer is not in the practice's zone will see practice times everywhere (today they see their own zone on most screens). For the single-site practice with PCs in its zone nothing changes on screen.
8. **Out of scope:** the user's choice of date format (PKG-14/PD-37); multi-location zones (one practice zone today).

## 4. Test policy

This request authorizes no run. Each attempt follows the standing rules:
- code is shown to the owner before any run;
- focused runs of five minutes or less are allowed; longer runs need the owner's explicit permission;
- each attempt has its own Stage 1 handoff;
- broad regression is deferred to ALV-N028.

## 5. Decisions requested

1. Placement: T-2 and T-3 in A2, and T-1/T-4 as attempt A3 of ALV-N021 (recommended); or alternative (i), (ii) or (iii).
2. T-1 option: A (recommended), B or C.
3. Approval of the contracts in section 3, or corrections.
4. Confirmation that A1 closes on the A1-R02 package unchanged, with these findings carried into A2/A3.
