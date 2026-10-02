# ALV-N001 R04 — Acceptance evidence

| Concern | Evidence |
|---|---|
| F1: success notification text meets WCAG AA | 4.45:1 -> 5.38:1 (light) and 4.42/4.05:1 -> 5.9/5.4:1 (dark) in `statusContrast.test.ts`; real-browser axe `color-contrast` passes (`notification-contrast-new.txt`) and fails on the old CSS at 4.45:1 |
| Same defect in sibling variants and the shell | light warning 3.47 -> 5.39, session-warning banner 3.47 -> 5.39, dark info over raised 4.39 -> 5.46; all pinned by the same test |
| Borders and icons unchanged | `--color-success` / `--color-warning` not modified (non-text contrast already met) |
| No regression anywhere | 498 frontend, 78 mocked browser, 8 real-backend suites with axe scans in both themes, all pass |
| No behavioural/API change | no markup, component API or backend file changed; frontend assertions unchanged |

The story's original acceptance items (shell, design tokens, accessibility smoke coverage) are unchanged and re-asserted.
