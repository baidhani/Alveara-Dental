# ALV-002-C01 R05 — Acceptance evidence

| Concern | Evidence |
|---|---|
| F2: shared conflict-banner title meets WCAG AA | 3.46:1 -> 5.39:1 light; dark >= 4.83:1; `conflictBannerContrast.test.ts`; real-browser axe passes on three pages with no override |
| Page-level workarounds removed | Calendar.css, PatientWorkspace.css, FlowBoard.css no longer mention the title; the guard test fails if any stylesheet re-colours it |
| Fix is actually what the browsers see | mutation: reverting only the banner colour fails the board, calendar and workspace walkthroughs on axe `color-contrast` |
| Banner behaviour unchanged | `ConcurrencyConflictBanner.test.tsx` unchanged and passing; no markup or API change |
| Reusable-pattern and accessibility acceptance re-asserted | full frontend, mocked browser and 9 real-backend suites pass |
