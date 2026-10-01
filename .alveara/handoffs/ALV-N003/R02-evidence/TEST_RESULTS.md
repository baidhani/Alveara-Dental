# ALV-N003 R02 — Test Results

All commands run from the repository at implementation commit `4d4f3c34994aa8beed63582250cb0722cd633ee9`.

## Backend — `dotnet test`

```
cd src/Alveara.Api.Tests
dotnet build
dotnet test --no-build
```

**Result: 262 of 262 passing, 0 failed, 0 skipped** (duration 11 m 4 s). Baseline 252 (ALV-N003 R01) + 10 new:

| Class | New | Reproduction closed |
|---|---|---|
| `AvailabilityConcurrencyTests` (new) | 5 | **R01-02** — real SQL Server races with a `SaveChanges` interceptor barrier forcing both replacements to finish reading before either commits: initially-empty schedule and populated schedule each give exactly one winner + exactly one `ConcurrencyConflictException`, one valid final schedule (never the overlapping union), matching revision, one audit entry (loser's rolled back); stale editor rejected with schedule/audit unchanged; missing revision refused (`revision_required`); revision advances per replacement and a specialty edit does not disturb it |
| `SchedulingConfigurationTests` | +4 | **R01-04** — the reviewer's exact fall-back reproduction (Sunday 01:30–02:00, 2026-11-01 06:50Z, 30 min) is unavailable (`crosses_dst_transition`) and the same window a week earlier is available; spring-forward-crossing slot refused with wholly-before/after slots available; slot ending exactly at the fold instant available; blocked-time overlap still detected in UTC across the transition date |
| `ConfigurationApiTests` | +1 | **R01-02** — GET returns `revision`; PUT with the current revision succeeds and returns the next; a stale PUT is 409 `concurrency_conflict` (`entityType: ProviderAvailability`); a missing revision is 400 `revision_required` |

All pre-existing availability tests were adapted to pass the revision (through the `ReplaceCurrentAsync` helper) with no assertion weakened.

**Mutation check:** with the revision comparison/bump removed from `ReplaceAvailabilityAsync`, both race tests fail (`Assert.Equal() Failure`), confirming they exercise the real race rather than passing vacuously; the change was reverted before the commit.

## Backend — `dotnet build`

Clean, 0 errors.

## Frontend — `npx vitest run`

**Result: 24 test files, 113 tests, all passing** (101 carried + 12 new):

- `ConfigurationHubPage.test.tsx` (+4, **R01-01 / R01-02 UI**): out-of-order provider responses with hand-completed promises (A pending, B selected and completed, A completes last — still B's 13:00, and a save submits B's rows to `/providers/p2/…` with B's revision); previous provider's editor hidden while the next loads; dirty-switch still prompts; stale schedule save → shared conflict banner → failed reload keeps the 08:00 draft and the banner → retry shows the current 10:00. Existing availability tests updated for the `{ revision, windows }` contract and now assert the revision is sent.
- `App.unsavedNavigation.test.tsx` (5, **R01-03**): shell link declined/accepted; no prompt when clean; browser back declined stays with the draft; sign-out declined; a 401 during a dirty draft redirects with no prompt and no retained draft.
- `ConfigEntityPanel.test.tsx` (+3, **R01-05**): failed reload keeps draft + conflict and recovers on retry; record inactivated elsewhere is found; genuinely missing record closes the form only after a successful read. The existing conflict test now also asserts the stale `update` is called exactly once (banner button no longer submits).

**Mutation checks:** with the superseded-response discard removed, the out-of-order test fails; with `NavigationGuard` removed from the root layout, the shell-link and back tests fail. Both reverted before the commit.

## Frontend — `tsc --noEmit`, `npm run build`, `npm run lint`

`tsc --noEmit` clean. Production build clean. Lint exit 0 with 13 informational warnings — identical to R01 (9 pre-existing + the 4 same-class `set-state-in-effect` from R01); no new warning kind.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

Same commands as R01 (fresh `AlveraE2E` database dropped, migrated through `AddProviderAvailabilityRevision`, real `dotnet run` API on 5072, then Playwright).

**Result: 11 of 11 passing** (10 carried + 1 new: "unsaved configuration edits survive an attempt to leave through the main navigation" — a real browser dialog is dismissed and the page and typed text remain; a second click with the dialog accepted navigates to the Dashboard). The carried end-to-end configuration test exercises the new revision round trip (add window → save) against the real API. Raw output: `R02-evidence/artifacts/playwright-real-backend/run-output.txt`.
