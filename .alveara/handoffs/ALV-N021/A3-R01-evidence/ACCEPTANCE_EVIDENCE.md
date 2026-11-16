# ALV-N021 A3-R01: acceptance evidence

Approved briefing: `docs/design/ALV-N021_A3_PRE_IMPLEMENTATION_BRIEF.md` revision 2 (`c5b7c7b`, review `.alveara/reviews/ALV-N021/A3-BRIEFING-R02.md` APPROVED). Inventory baseline: `docs/design/ALV-N021_A3_SITE_INVENTORY.md` revision 2 (comparison: `INVENTORY_COMPARISON.md`).

## Briefing section 4.1 (Option B contract)

| Item | Implementation | Proof |
|---|---|---|
| 1 canonical IANA zone in every authenticated session response | `PracticeZoneIds.CanonicalIanaId` (Windows id converted, none -> `PracticeZoneConfigurationException`), start-up check in `Program.cs`, `IPracticeClock.PracticeTimeZoneIanaId`, `practiceTimeZoneId` in `AuthController.MyPermissions` (authentication only) | `PracticeTimeZoneSessionTests`: all eight roles incl. Unassigned get `America/Chicago`, signed out 401; `Central Standard Time` configuration published as `America/Chicago`; custom zone refused |
| 2 client-neutral contract; server-owned Windows mapping for a future WPF client | `PracticeZoneIds.TryWindowsId`; only the IANA id is published | `PracticeTimeZoneSessionTests` round trip (Chicago, Tokyo, Havana) |
| 3 shared helpers only | `src/alveara-client/src/time/practiceTime.ts` (`formatInstant`, `toPracticeLocalInput`, `practiceToday`, `practiceNow`, `showInstant`, `sessionToday`, `sessionLocalInput`, `localInputText`) | `practiceTime.test.ts`; guard (section 8) |
| 4 no silent fallback | every helper `null` without a usable zone; screens show "Time unavailable"; shell banner "Times cannot be shown: the practice time zone is not available." | `practiceTime.test.ts`, `AppShell.practiceZone.test.tsx`; negative controls (slices 3, 4, 5) |
| 5 deployment-zone lifecycle | every authenticated `permissions` response replaces the zone; a different usable zone re-renders and shows the neutral notice once ("Your session was refreshed; times are shown in <zone>."), dismissable; zone forgotten at session end | `AuthContext.practiceZone.test.tsx`, `AppShell.practiceZone.test.tsx` (Chicago -> Denver re-renders 9:30 -> 8:30 PM) |
| 6 true instants preserved | API instants unchanged (UTC); sorting, Flow Board elapsed time, session expiry untouched (RL rows) | full client suite 2045/2045; full mocked Playwright |
| 7 plain practice-local input | follow-up `dueAtLocal`, completion `performedAtLocal` (complete and adopt), vitals `measuredAtLocal`; prefills by `toPracticeLocalInput` in the practice zone; no `new Date(local).toISOString()` remains | `FollowUpScreens.test.tsx` (sends exactly `2026-12-01T09:30`, no `dueAtUtc`), `TreatmentPlanReviewComplete.test.tsx`, `vitalsPayload.test.ts`, two-zone walkthrough |
| 8 date-only values unchanged | `formatDateOnly` and the RO rows untouched | `dateOnly.test.tsx` (four process zones), guard allowlist |

## Briefing section 5 (API map, D-2) and BR-2

| Route | Local field | Proof |
|---|---|---|
| `POST api/treatment-plans/{planId}/complete-procedures` | `performedAtLocal` (seconds) | `PracticeLocalInputApiTests` refusal matrix; accepted path through the shared helper (`PracticeLocalInputTests`) |
| `POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` | `performedAtLocal` (seconds) | as above |
| `POST api/patients/{patientId}/follow-ups` | `dueAtLocal` | refusal matrix; stored instant over HTTP (CST) |
| `PUT api/follow-ups/{id}` | `dueAtLocal` | refusal matrix; stored instant over HTTP (CDT) |
| `POST api/encounters/{encounterId}/vitals` | `measuredAtLocal` | refusal matrix; stored instant over HTTP |

One parser, `PracticeLocalInput` (exact invariant ASCII, `Unspecified` kind, `FromPracticeLocal` only). Codes: `invalid_local_time`, `local_time_does_not_exist`, `local_time_ambiguous`, `ambiguous_time` (both fields named); legacy `...Utc` alone accepted (existing route tests). Owner decision (2026-11-16, option 3): when the local form was sent, a later field error the unchanged service reports under the legacy UTC name is also given under the local name (`PracticeLocalInputApiTests` vitals case; unit test of the alias); follow-up errors carry no field names, so nothing is copied there.

## Briefing section 8 (guard) and BR-4

`tests/practice-time-guard.test.mjs`: two rules, six allowlist entries (file, exact expression, reason; stale entries fail; each entry must really be caught), negative controls in-test and on a real file.

## Briefing section 9 (focused proof plan)

| Row | Where |
|---|---|
| shared formatter, no browser fallback, practice today | `practiceTime.test.ts`, `dateOnly.test.tsx`, `AppShell.practiceZone.test.tsx` |
| server local input (all five routes), cultures | `PracticeLocalInputTests` (th-TH, dotted time, ar-SA), `PracticeLocalInputApiTests` |
| zone source, canonical id, WPF mapping | `PracticeTimeZoneSessionTests` |
| date-only unchanged | `dateOnly.test.tsx` |
| statement view | `BillingDocumentView.tsx` migrated (rows 12); covered by the billing component tests in the full client run (no dedicated new test; disclosed) |
| real browser, two zones | `practice-time-zones-real-backend.spec.ts` 2/2 |
| guard | 4/4 + controls |
| existing Chicago behaviour | full client suite, full mocked Playwright |
| exact input prefill (BR-1) | `practiceTime.test.ts` (Latin digits, Gregorian, h23, seconds, midnight 00) |
| session lifecycle (BR-3), contract (BR-5) | `AuthContext.practiceZone.test.tsx`, `AppShell.practiceZone.test.tsx`, `PracticeTimeZoneSessionTests`; 25 fakes on one builder |

## Story acceptance

With A1, A2 and A3 complete the story's time-zone scope (T-1..T-4 of decision 02) is delivered; the story's acceptance is claimed only after A3 is approved and closed.
