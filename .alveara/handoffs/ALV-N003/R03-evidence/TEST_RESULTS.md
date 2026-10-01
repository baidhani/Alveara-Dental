# ALV-N003 R03 — Test Results

All commands run from the repository at implementation commit `39350c586cba3ad8af746f3d6a71e9c70894fa6c`.

## Backend — `dotnet test`

```
cd src/Alveara.Api.Tests
dotnet build
dotnet test --no-build
```

**Result: 262 of 262 passing, 0 failed, 0 skipped** (duration 11 m 26 s). No backend source changed in this attempt (the R03 diff touches only frontend files); the full suite was nevertheless re-run on this tree rather than carried forward. Includes the R02 real-database availability races and DST regressions.

## Frontend — `npx vitest run`

**Result: 24 test files, 122 tests, all passing** (113 carried + 9 new):

- `pages/ConfigurationHubPage.test.tsx` (+7) — "Availability editing-session completion protection (ALV-N003 R03)" (6): the reviewer's A-save → B → A → new draft → old-response sequence (fresh draft keeps 10:00, stays dirty, no success notice, and the next save submits `{10:00, revision 2}`); the same sequence ending in a delayed 409 (no banner in the new session); edits typed during an in-flight save are kept with baseline/revision advanced, Save disabled while saving, next save at the new revision; delayed blocked-time add success (new draft kept, no notice), add error (not shown), remove (no notice, fresh list intact). Plus 1 practice-form test: inputs/Save disabled while a save is in flight and the response does not overwrite typed text.
- `components/ConfigEntityPanel.test.tsx` (+2): pending-write policy (form fields, other rows' Edit and Add disabled while saving, re-enabled after); a conflict reload completing after the form was closed does not reopen it.

**Mutation checks (reverted before commit):** with the session check on the save-success path, the draft check, and the blocked-time session guard removed, three of the new availability tests fail (A-save→B→A reproduction, edits-during-save, blocked-time add), proving they exercise the defect. While developing the panel test, a real bug in my own change (the `saving` flag not reset on the success path after the session bump) was caught by the pending-write test and fixed before commit.

All R02 frontend regressions still pass: out-of-order loads, editor hidden while loading, dirty-switch, conflict/retry, navigation guard (5), failed-reload preservation.

## Frontend — `tsc --noEmit`, `npm run build`, `npm run lint`

`tsc --noEmit` clean. Production build clean. Lint exit 0 with 13 informational warnings — identical to R02; no new warning kind.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

Same commands as R01/R02 (fresh `AlveraE2E` database dropped and migrated, real `dotnet run` API on 5072, then Playwright).

**Result: 11 of 11 passing** (all carried; the real-browser configuration flow now exercises the session-bound availability editor against the real API). Raw output: `R03-evidence/artifacts/playwright-real-backend/run-output.txt`.
