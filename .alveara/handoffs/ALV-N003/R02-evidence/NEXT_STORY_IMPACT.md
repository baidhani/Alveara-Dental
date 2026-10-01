# ALV-N003 R02 — Next Story Impact

## Changed contracts later stories must use

- **A weekly schedule is a versioned aggregate.** `GET api/config/providers/{id}/availability` returns `{ revision, windows }`; replacing requires `{ windows, revision }` and a stale revision is the shared 409 (`entityType: "ProviderAvailability"`). Any future writer (e.g. a scheduling-template or import story) must read the revision first and handle the conflict — never write windows without it. Backup/restore (`ALV-N004`) should treat `ProviderProfiles.AvailabilityRevision` as part of the schedule state.
- **`CheckProviderAvailabilityAsync` refuses slots that span a UTC-offset change** (`crosses_dst_transition`) rather than evaluating them. A booking flow should surface that reason to the user instead of treating it as "outside hours". Slots with a constant offset behave as before; blocked time is a UTC overlap.
- **Unsaved-change protection is app-wide and router-level.** Any editor that can hold unsaved input should call `useUnsavedChangesWarning(dirty)`; shell links, browser back/forward and sign-out will then prompt automatically via `UnsavedChangesProvider`/`NavigationGuard`. The guard only blocks a *signed-in* session, so authorization loss always redirects immediately — do not add a second blocker that could hold a revoked session's UI.
- **The app now runs on a data router** (`createBrowserRouter` in `App.tsx`), so `useBlocker`, loaders and actions are available to later stories. Route definitions and `RequireAuth`/`RequirePermission` behave as before.
- **Conflict recovery pattern** (reused by `ConfigEntityPanel` and the availability tab): replace the draft only after a *successful* fresh read; keep draft, banner and a retryable error on failure; look for an inactivated record before concluding it is gone. `ConcurrencyConflictBanner`'s button is now non-submitting.
- **Provider-bound async UI pattern:** number requests, discard superseded responses, clear state bound to the previous selection on change, and make writes target the identity the data was loaded for (see `AvailabilityTab`). Use it for any editor whose subject can change while requests are in flight.

## Assumptions / migrations / unresolved decisions

- New migration `AddProviderAvailabilityRevision` (adds a non-null int, default 0) on top of R01's `AddPracticeConfiguration`; no data transformation.
- The DST rule is conservative by design; a future scheduling story that needs transition-hour bookings can evaluate such slots piecewise.
- Blocked-time add/remove are not revision-checked (independent audited rows; cannot form an invalid union).
- One active location remains the first-release limit.

## Gate A

Row A8 can be evaluated once this story is approved; it is not evaluated or marked here. Gate A as a whole still waits for `ALV-N004`.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 10 (`ALV-N004`) begins.
