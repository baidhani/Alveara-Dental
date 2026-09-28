# ALV-N001 R02 — Acceptance Evidence

Updates R01's mapping with genuine (not jsdom-only) evidence where the reviewer found it missing.

| # | Acceptance item | R02 evidence |
|---|---|---|
| 1 | Root STORY-000 Command Center still satisfies its course contract | Unchanged from R01: `tests/story-000-coexistence.test.mjs` (4 checks) + zero `git diff` on course-managed paths across this attempt too. |
| 2 | Production Alveara app is structurally separate | Unchanged from R01. |
| 3 | Shell is usable at agreed desktop and tablet widths | **Now genuinely established**: `e2e/shell.spec.ts`'s "layout has no unintended horizontal overflow at this viewport" test runs in real Chromium at both declared viewports (1280×800 desktop, 768×1024 tablet), asserting `document.documentElement.scrollWidth <= clientWidth` and a concrete structural difference (narrow sidebar column at desktop vs. ≥90%-width collapsed top bar at tablet). The showcase-navigation test's long-text-wrap assertion checks the description text's bounding box never exceeds the viewport width, at both viewports. |
| 4 | Tokens and initial controls demonstrated without unrelated sprawl | Unchanged component set from R01; now also verified rendering correctly in a real browser via `e2e/shell.spec.ts`'s showcase test (buttons, form field, all visible) and confirmed accessible via `e2e/accessibility.spec.ts` in both themes. |
| 5 | At least one keyboard-only path and accessibility smoke check pass | **Strengthened**: the keyboard-only path is now also verified in real Chromium (`e2e/shell.spec.ts`), not only jsdom. Accessibility smoke coverage now spans Dashboard, Showcase, and Not-Found, in both light and dark theme, via real-browser axe (`e2e/accessibility.spec.ts`, 12 tests, 0 critical/serious violations) — addressing the reviewer's specific note that the dark-theme contrast defect would have been caught by broader coverage. |
| 6 | No dependence on unbuilt authentication/RBAC | Unchanged; additionally, `AppShell.emptyRegistry.test.tsx` now proves the shell tolerates an empty registry without inventing fake permissions or patient data to fill the gap. |

## Defects found by the R01 review, and their resolution evidence

| Finding | Resolution evidence |
|---|---|
| N001-R01-01 (connectivity false positive) | `useConnectionStatus.test.ts` (6 tests) + real dev-server/API reproduction in `TEST_RESULTS.md` (502 when down through the proxy, real 200 JSON when up) |
| N001-R01-02 (no genuine browser evidence) | `e2e/shell.spec.ts` (20 tests) + `e2e/accessibility.spec.ts` (12 tests), all passing, real Chromium, both declared viewports |
| N001-R01-03 (dark contrast) | `contrast.test.ts` (4 tests, computed WCAG ratios) + real-browser axe color-contrast pass in dark mode |
