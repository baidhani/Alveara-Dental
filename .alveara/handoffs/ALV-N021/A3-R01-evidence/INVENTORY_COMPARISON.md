# ALV-N021 A3-R01: implementation compared with the approved inventory baseline

Baseline: `docs/design/ALV-N021_A3_SITE_INVENTORY.md` revision 2 (85 rows). Implementation `b8756f1` (diff from `3845e31`).

## Rows

| Rows | Disposition | Outcome |
|---|---|---|
| 1-54 (instant display, wrappers) | MI | every file changed (checked by script: no MI row's file is unchanged); 63 replacements by `showInstant`; callers of the exported wrappers unchanged, as planned |
| 3 (login lockout) | MI-pre-session | shown as "Try again in N minutes" (`tryAgainIn` in `pages/loginOutcome.ts`, the page's existing helper module); needed no allowlist entry |
| 53 (`safetyText.day`, no callers) | removed | removed |
| 55 (printed plan date) | MD | done in slice 5 with the other "today" rows |
| 56 (`calendarLayout.practiceNow`) | RS | **difference:** not moved; the Calendar and Flow Board keep the existing helper, which already uses the scheduling snapshot's zone. `time/practiceTime.ts` has its own `practiceNow`. The briefing's "zone of the snapshot equals the session zone" assertion was not added. Behaviour unchanged. |
| 57 (`formatMoment`) | RS | rebuilt on `formatInstant`; browser fallback removed |
| 58-62 ("today") | MD | done; without a zone the defaults are empty (the date-of-birth limit is not shown) |
| 63-70 (inputs) | MP / MS | done; vitals error mapped from `measuredAtLocal` |
| 71 (blocked time) | retain | unchanged |
| 72-77 (date-only) | RO | unchanged; rows 72-76 are the guard's allowlist; **difference:** row 77 (`fromDayNumber`, `toISOString` arithmetic) is not matched by the guard rules and needs no entry |
| 78-85 (logic, keys, numbers) | RL / X | unchanged; not matched by the guard |

## Session contract (section 6)

| Planned | Outcome |
|---|---|
| server field, start-up, mapping | done (`AuthController.cs`, `Program.cs`, `PracticeZoneIds.cs`, `PracticeClock.cs`) |
| `authApi.ts` session type, `AuthContext.tsx` | done; also `contexts/authContextObject.ts` (where the state type and the new `usePracticeZone` hook live) |
| 34 fakes/fixtures to one builder | **difference:** 25 changed (18 client, 7 mocked specs); 9 need no change: `AuthContext.tsx` itself, the two permission-policy real-backend specs, `chain/stage-1.spec.ts` and `gate-a/gate-a-browser.spec.ts` (real server), and the four `login-*` specs (they fake only a signed-out answer) |

## Product files changed that a row names only as a caller or a briefing section names

| File | Why |
|---|---|
| `components/PatientFieldsForm.tsx` | row 58's caller (date-of-birth limit) |
| `pages/treatmentplan/TreatmentPlanReviewComplete.tsx` | row 68 (performed-time form) |
| `services/followUpsApi.ts`, `services/completionApi.ts` | the request types of rows 64 and 67 (`dueAtLocal`, `performedAtLocal`) |
| `services/authApi.ts`, `contexts/authContextObject.ts` | section 6 session type and state |
| `pages/loginOutcome.ts` | row 3's duration helper |
| `app/AppShell.tsx`, `app/AppShell.css` | briefing 4.1-4 banner and 4.1-5 notice |

## New files

`time/practiceTime.ts` (+ test), `test/signedInSession.ts`, `e2e/support/signedInSession.ts`, `contexts/AuthContext.practiceZone.test.tsx`, `app/AppShell.practiceZone.test.tsx`, `services/vitalsPayload.test.ts`, `e2e/practice-time-zones-real-backend.spec.ts`, `playwright.practice-time.config.ts`, `tests/practice-time-guard.test.mjs`; server `PracticeZoneIds.cs`, `PracticeLocalInput.cs` and their three test classes.

## Implementation choice not described in the inventory

The session zone the screens render in is held inside `time/practiceTime.ts` (`setRenderZone`, called only by the AuthProvider and by the test setup), so the ~25 plain formatting wrappers need no zone parameter; `formatInstant` still takes an explicit zone as the briefing defines.
