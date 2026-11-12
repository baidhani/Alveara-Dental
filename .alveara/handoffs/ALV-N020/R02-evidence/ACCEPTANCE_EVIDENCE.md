# ALV-N020 R02: acceptance evidence

R01's tables (`../R01-evidence/ACCEPTANCE_EVIDENCE.md`) stand for everything R02 did not change. This file gives the changed rows and the R01 review's four items.

## A. Plan item 57 acceptance (5 parts)
| # | Part | Met? | Evidence |
|---|---|---|---|
| 1 | Fix and evidence every mapped identity/session finding (F-037, F-021 included) | **Yes** | R01 table B; unchanged |
| 2 | Preserve genuine 401 behavior while distinguishing server/network failure | **Yes** | R01; plus R02 13.05 with the API really stopped: no sign-out, then `GET /api/auth/permissions` 200 with the same cookie after the restart |
| 3 | Disabled/revoked sessions, identity-scoped cache clearing, safe recovery | **Yes** | R01; recovery now proven against a real API stop and restart |
| 4 | All R05 stage 1 and minimum stage 2 coverage for PKG-02/03 | **Yes** | chain re-run 20 of 20; 13.05 is now the owned-runner case the brief described |
| 5 | Required real-browser Back, expiry, revocation and form-guard cases | **Yes** | Back 1.10, revocation 1.06, expiry 1.12, form guard: 1.12 (the unsaved-changes prompt on a really dirty form, the text kept through the expiry warning, cleared at expiry) and 13.05 (the dirty form kept through a real outage); all PASS in the chain re-run |

Result: **5 of 5.**

## B. R01 review items
| # | Item | Test | Result |
|---|---|---|---|
| 1 | 1.12: actual dirty form, real short expiry, warning/preservation before `/login?reason=expired` | chain `1.12 a short per-user timeout: a really dirty form is guarded and kept through the warning, then expiry signs out with the expiry message and clears it (fixed expiry unchanged)`: guard prompt text exactly "You have unsaved changes. Discard them?", dismissed -> still `/patients/register` with "Unsaved"; the alert "Your session will expire soon. Save your work and sign in again to continue." visible with "Unsaved" still in First name; then "your session expired" at `/login?reason=expired`, exactly one prompt in the step, no First name field and no "Unsaved" text | PASS |
| 2 | 13.05: owned-runner stop/restart, reconnecting state, retained dirty form, recovery without sign-in | chain `13.05 when the runner really stops its own API ...` + runner steps `API-STOP`, `API-START` (owned process 44012 stopped, port 5072 free; owned process 23468 started with the same settings, healthy) | PASS |
| 3 | Database refusal with either document link | `StaffInactivationNotificationSchemaTests.The_new_kind_is_refused_with_a_document_link(RelatedDocumentId)` and `(RelatedOtherDocumentId)`: real document, `CK_Notifications_Shape` (547) | PASS (8 of 8 in the class) |
| 4 | No new lint warning | `npm run lint` 41 warnings (pre-story count); `useIdentityEpoch` now in `authContextObject.ts` | PASS |
