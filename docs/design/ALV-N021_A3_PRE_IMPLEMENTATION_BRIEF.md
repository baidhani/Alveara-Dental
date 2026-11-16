# ALV-N021 attempt A3 - pre-implementation briefing (revision 1, for independent review)

- **Story:** ALV-N021 (plan item 58). A1 COMPLETE (A1-R02), A2 COMPLETE (A2-R02, closure `d20e04c`).
- **Authority:** decision 02 (`.alveara/reviews/ALV-N021/A2-DECISION-02.md`, "Authorized follow-on A3 scope - T-1 and T-4") and the execution-plan A3 boundary (item 58, "Required coverage"). The A2 closure review authorizes this briefing only; **no A3 code or test is written before this briefing is approved.**
- **Owner decisions in this briefing (2026-11-16):** D-1 = Option B with server-authoritative presentation rules (section 4); D-2 = keep the legacy `...Utc` input fields temporarily (section 5).
- **Source audit:** `docs/design/ALV-N021_TIME_HANDLING_AUDIT.md` (T-1, T-4), re-inventoried against the current code (`d20e04c`) in Appendix A.

## 1. Intended result

1. **Practice-zone display (T-1, T-4).** Every user-visible instant (histories, audit, notes, diagnoses, perio, forms, billing ledger and documents, notifications, merges, backups, system status, treatment plans, exports, reports) is shown in the practice's time zone: the client renders the server's instant with the server-supplied IANA practice zone through **one shared helper** (section 4.1); it never falls back to the browser zone.
2. **Practice "today".** Every default date, filter preset, relative label and date limit takes the calendar day of the practice zone, not of the browser.
3. **Plain local input.** Every `datetime-local` value is practice-local text. The flows that still convert it to a browser instant (completion performed time, completion adoption performed time, follow-up due time on create and update, vitals measured time) send a new `...Local` field; the server parses it exactly with the invariant culture and converts it only through `IPracticeClock.FromPracticeLocal`; a DST-invalid or ambiguous local time is a field-level 400.
4. **Statement consistency (T-4).** The statement/receipt view and live billing/aging show dates in the practice zone. Issued documents keep their stored, versioned canonical content (A1 contract unchanged).
5. **Guardrails.** A source scan (client) refuses new direct instant formatting and browser-day calculation outside the shared helpers and a reviewed allowlist; real-browser tests from two different browser zones show the same practice-zone result.

## 2. Scope

- **In:** T-1 and T-4 of decision 02; the inventory in Appendix A; the five request contracts in section 5; the design option chosen in section 4 (D-1).
- **Out:** date-format preference (PKG-14/PD-37: formats stay as they are today, only the zone changes); any change to stored data; date-only values (date of birth, service dates, due dates that are `DateOnly`): already built from their parts since A1 and **must not** acquire a zone conversion.

## 3. Current state (verified at `d20e04c`)

| Area | Today | A3 |
|---|---|---|
| Calendar, Flow Board, reports, export history | practice zone (`practiceNow`, `timeZone:` options) | move to the shared helpers (same behaviour) |
| ~76 other display sites | browser zone (`toLocaleString()` etc. without `timeZone`) | shared formatter |
| 12 "today" sites | browser day (`getFullYear/getMonth/getDate`, `todayDateOnly`, `todayInput`, report presets, Schedule default) | practice day |
| 9 `datetime-local` lines (6 inputs, 3 helpers) | appointment/blocked time already practice-local; completion, adoption, follow-up, vitals convert in the browser (`new Date(local).toISOString()`) | `...Local` fields |
| Zone source on the client | practice settings (needs ManagePracticeConfiguration), scheduling snapshot (ViewSchedule), report responses; billing and other roles have none | every signed-in user, from the session response (section 4.1) |

## 4. Design options: who turns an instant into practice time (owner decision D-1: Option B)

The product owner asked that the briefing compare the owner's first approach - **everything time-related comes from the server; the client only displays** - with the approaches first drafted. All options share the same input contract (section 5: typed times are sent as practice-local text and converted only by the server) and the same "no browser zone, ever" rule. They differ in where display text and "today" are produced.

Footprint measured at `d20e04c` (read-only):
- **Server:** 127 response records carry 191 instant fields, in 24 areas (largest: Clinical 16 records/27 fields, Documents 11/13, Reporting 9/12, Billing 8/9, Notifications 7/11); plus 9 view classes and 9 anonymous controller responses (Auth, Backup, System status). Every instant is serialized today as UTC (`...Z`).
- **Client:** 33 API type files with 206 instant fields; 76 display sites; 12 "today" sites; 5 sites that use instants for logic (sorting, elapsed minutes, session expiry), which need the real instant whichever option is chosen.
- **Tests:** 74 test files assert a literal `...Z` instant string.

| | **A. Server sends display text and today's date** | **B. Client renders with the server-supplied zone (decided)** | **D. Server sends instants with the practice offset** |
|---|---|---|---|
| What the server sends | for each visible instant, an extra practice-local text field next to the instant (e.g. `signedAtUtc` + `signedAtLocal: "2030-01-14T09:30"`), as the calendar already does with `startLocal`; **and** the practice "today" date (a version without the server "today" is incomplete: 12 client sites would still compute a browser day) | instants unchanged; `practiceTimeZoneId` (IANA) once, in the session response | every instant written with the practice offset instead of `Z` (`2030-01-14T09:30:00-06:00`) through one serializer setting, and the practice "today" date |
| What the client does | displays the text; never converts | renders each instant with one shared helper (`Intl.DateTimeFormat` + the supplied zone) and derives "today" with one shared helper | intended to show the wall-clock part as written |
| Client uses a zone? | no | only the server-supplied one, only for rendering and "today" | no (intended) |
| Server change | largest: ~127 records / ~191 new fields, the anonymous responses, plus "today" | one session field (+ IANA normalization, section 4.1) | one global JSON converter + "today" |
| Client change | 206 type fields reviewed, 76 display sites and 12 "today" sites switched to server values | 76 display sites + 12 "today" sites to two helpers | 76 display + 12 "today" sites |
| Tests touched | a server test per new field; client display tests | helper and display tests; API test for the field | the 74 files asserting `Z` strings |
| Main risk | a missed field anywhere in a large contract change | the helper itself (one tested place) | see "why not D" |

(The earlier draft listed a separate option C, "A plus a server today"; it is the complete form of A and is presented as A.)

**Decision (owner, D-1): Option B, with server-authoritative presentation rules (section 4.1).** B keeps the API contract unchanged except for one additive field, puts all rendering in one tested place, and leaves policy on the server: the server owns the configured zone, typed-time conversion, DST gap/overlap refusal and every persisted value; the client only renders a server-supplied instant with the server-supplied zone.

**Why not D.** Changing serialized offsets globally does not make the browser display correct: as soon as JavaScript parses `2030-01-14T09:30:00-06:00` into a `Date`, it is an instant again and any default formatting renders it in the browser zone, so the client would still need a rule and a guard. At the same time D changes the textual form of every instant in the API (74 test files, and any caller that compares or stores instant strings), a large compatibility risk for no gain over B.

**Why not A.** A satisfies the principle but grows ~127 response shapes by ~191 fields (plus "today"), where any missed field silently falls back to a client-formatted value; B reaches the same displayed result with one additive field and one helper.

### 4.1 Option B contract (required)

1. **Canonical zone in the session.** `GET /api/auth/permissions` (every signed-in user, `[Authorize]` only) gains `practiceTimeZoneId`: an **IANA** identifier (`America/Chicago`) that browser `Intl` supports. The server derives it from the configured `PracticeTimeZone`; if the configuration holds a Windows identifier (`Central Standard Time`, accepted by `FindSystemTimeZoneById` on Windows), the server converts it with `TimeZoneInfo.TryConvertWindowsIdToIanaId`; if no IANA form exists the API **does not start** with a misleading value (start-up check, tested). A platform-specific identifier is never published.
2. **Client-neutral contract; future WPF client.** The field is the same for every client. The server owns a tested mapping `PracticeZoneIds` (IANA canonical id and its Windows id via `TimeZoneInfo.TryConvertIanaIdToWindowsId`) so that a future Windows/WPF client whose native APIs need a Windows id gets it from the server, not by mapping it itself. A3 adds and tests the mapping; it publishes only the IANA field now (a Windows-id field is added when that client exists). Neither client may select, infer, persist or independently map the practice zone.
3. **Two shared helpers only.** `src/alveara-client/src/time/practiceTime.ts`: `formatInstant(utcIso, zone, style)` (`Intl.DateTimeFormat` with `timeZone: zone`; the styles the screens use today) and `practiceToday(zone, now?)` / `practiceNow(zone, now?)`. No other code formats an instant or derives a calendar day from the browser clock (guard, section 8).
4. **No silent fallback.** If the field is missing (older server) or `Intl` rejects it, the helpers return `null`; components show **"Time unavailable"** and one banner "Times cannot be shown: the practice time zone is not available." Nothing is rendered in the browser zone. Tested.
5. **Zone change during a session.** The zone is a deployment setting: changing it needs an API restart, and the existing deployment-settings guard (ALV-N004) refuses domain calls while the running settings and the recorded ones disagree. The client re-reads `practiceTimeZoneId` with every session refresh (`permissions` call: sign-in, reload, session renewal and when the tab becomes visible again); if the value differs from the one in use, every rendered time re-renders with the new zone and a notice says "The practice time zone changed to <id>; times are now shown in it." Tested with a fake session response.
6. **True instants preserved.** API instants stay UTC (`...Z`) and unchanged; sorting, elapsed-time counters, session expiry, stale checks and every API contract keep using the instant, never the rendered text.
7. **Plain practice-local input.** `datetime-local` values are sent as practice-local text (`...Local`, section 5) and converted only by the server through `IPracticeClock.FromPracticeLocal`; the client never converts typed text to an instant.
8. **Date-only values unchanged.** `DateOnly` values (date of birth, service dates) keep `formatDateOnly` and are never zone-converted.

## 5. API compatibility map

| Flow | Route | Today | A3 |
|---|---|---|---|
| Complete procedures | `POST api/treatment-plans/{planId}/complete-procedures` | `performedAtUtc` (browser-converted from `datetime-local`) | new `performedAtLocal` ("yyyy-MM-ddTHH:mm" or with ":ss", invariant, exact); `performedAtUtc` kept for compatibility (D-2) |
| Adopt a completion | `POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` | `performedAtUtc` | `performedAtLocal`, as above |
| Follow-up create | `POST api/patients/{patientId}/follow-ups` | `dueAtUtc` | `dueAtLocal` |
| Follow-up update | `PUT api/follow-ups/{id}` | `dueAtUtc` | `dueAtLocal` |
| Vitals | `POST api/encounters/{encounterId}/vitals` | `measuredAtUtc` | `measuredAtLocal` |
| Amend a completed procedure | `POST api/completed-procedures/{id}/amend` | `performedAtUtc` | **unchanged**: no screen sends it (no browser conversion exists) |
| Appointments, blocked time | (unchanged) | already `...Local` | unchanged |

Rules (the five changed rows): exactly one of `...Local` / `...Utc` may be sent; **both** = 400 `ambiguous_time` naming both fields; local text parsed with `DateTime.ParseExact(..., InvariantCulture)` then `IPracticeClock.FromPracticeLocal`; a DST gap = 400 `local_time_does_not_exist`, an overlap = 400 `local_time_ambiguous`, each on the local field; the stored value stays a UTC `DateTimeOffset` (no schema change). The client sends only `...Local`.

**Owner decision D-2 (2026-11-16): the legacy `...Utc` fields are retained temporarily.** Each is still accepted when supplied alone, for the existing chain, walkthrough and external callers; the product UI sends only the matching `...Local` field; a request with both forms is refused as ambiguous. The `...Utc` forms are documented as legacy-compatible (API comments and this map), and their retirement is deferred until the dependent callers are intentionally inventoried and migrated (recorded in `PROGRAM_REGRESSION_LEDGER.md` / next-story impact, not done in A3).

## 6. Shared client helpers (one module, `src/alveara-client/src/time/practiceTime.ts`)

- `formatInstant(utcIso, zone, style)` with the styles the screens use today (date, date-time, time), `Intl.DateTimeFormat` with `timeZone: zone`; returns `null` without a valid zone.
- `practiceToday(zone, now?)` and `practiceNow(zone, now?)` (moved from `calendarLayout.ts`).
- `toLocalInput(utcIso, zone)` for prefilling a `datetime-local` from an instant (rendering only; typed text is never converted by the client).
- `formatDateOnly` stays in `components/dateOnly.ts` (date-only values are never zone-converted).

## 7. Database and migrations

None. No stored value changes; issued financial documents keep their canonical content.

## 8. Source-scan guard (client) and allowlist

A node test in `tests/` (with the existing repo checks) scans `src/alveara-client/src` (tests, fakes excluded) and fails on: `toLocaleString/toLocaleDateString/toLocaleTimeString` on a date value, `new Intl.DateTimeFormat(` without `timeZone`, and `getFullYear()/getMonth()/getDate()/getHours()` on a `new Date()` outside `time/practiceTime.ts`. Allowlist (each with its reason, reviewed): number formatting (`count.toLocaleString()`, 33 sites in Appendix A class Z), `components/dateOnly.ts` (date-only), keys built from `Date.now()` (idempotency keys, timers: class E rows that are not dates). A negative control adds a direct `toLocaleString()` of an instant and shows the guard fails.

## 9. Focused proof plan (each run expected <= 5 minutes unless asked)

| Requirement | Proof |
|---|---|
| shared formatter | unit tests: Chicago normal day; a zone ahead of UTC (Pacific/Kiritimati) where the practice date differs from the browser's; DST gap and overlap instants; invalid zone -> null |
| no browser fallback | unit + component test: missing/invalid zone shows "Time unavailable" and the banner, no time text |
| practice today | unit tests with a fixed `now` where browser day != practice day (both directions, year end) |
| server local input | per flow: local accepted and stored as the right UTC; both fields -> 400 `ambiguous_time`; gap/overlap -> 400 on the local field; legacy UTC alone still accepted (D-2); malformed text 400; parse under th-TH and the dotted-time culture |
| zone source | API test: every role's `permissions` response carries `practiceTimeZoneId`; unauthenticated 401 |
| date-only unchanged | existing `dateOnly` tests + a test that DOB/service dates render the same in two browser zones |
| statement view | component test: the view shows practice-zone dates; the canonical document content is unchanged |
| real browser, two zones | one focused real-backend spec run twice with Playwright `timezoneId` (America/Chicago and Asia/Tokyo) against one Chicago practice: the same history time, today, and a performed-time entry stored as the same instant (asks the owner first if > 5 minutes) |
| guard | the scan passes; negative control fails it |
| existing Chicago behaviour | the A3-touched component tests (calendar, board, reports, billing, history) |
| canonical zone id (4.1-1) | server tests: an IANA configuration is published as is; a Windows configuration (`Central Standard Time`) is published as `America/Chicago`; an id without an IANA form stops start-up with a clear error |
| WPF mapping (4.1-2) | server test: `PracticeZoneIds` maps the IANA id to its Windows id and back (Chicago, Tokyo, a zone with a midnight transition) |
| zone change in a session (4.1-5) | client test: a session refresh with a different `practiceTimeZoneId` re-renders the times in the new zone and shows the notice |
| true instants preserved (4.1-6) | existing sorting, Flow Board elapsed-minute and session-expiry tests unchanged and passing; API instants still end in `Z` |

Negative controls: the helper falls back to the browser zone; the session publishes a Windows id; server converts local text with the process culture; the guard allowlist widened.

## 10. Risks

- ~90 client lines change; mitigated by one helper module, mechanical replacement, the guard, and component tests per screen.
- A walkthrough or chain spec that asserts a browser-zone string; runs only in the deferred ALV-N028 regression unless the owner asks.
- Session response grows by one field: additive.

## 11. Requested from the reviewer

Approve the inventory (Appendix A), the API map (section 5) with the owner's D-2, the owner's D-1 (Option B) and its contract (section 4.1), the guard and allowlist (section 8), the focused proof plan (section 9) and the no-fallback behaviour. No broad regression is requested (`DEFERRED_TO_ALV-N028`).

## Appendix A

`docs/design/ALV-N021_A3_SITE_INVENTORY.md`: every client line matching the display, today, input and wire patterns at `d20e04c` (157 lines, classified A-F/Z by pattern; class A and B rows to be confirmed line by line during implementation, any misclassification reported in the handoff), plus the five server request fields of section 5.
