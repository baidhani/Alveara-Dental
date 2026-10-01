# ALV-N001 R03 - Acceptance Evidence

| Concern | Evidence |
|---|---|
| WCAG AA contrast for Button variants in both themes, default/hover/focus | `src/styles/buttonContrast.test.ts` (18 tests) against the real CSS/tokens; fails on the old CSS; browser scan 0 violations over 48 scans |
| Secondary button hover/focus readable in dark theme | previous ~1.8:1 -> `--color-info` 5.5:1+ on every surface; the previously failing `/admin/users/:id` dark hover/focus scan no longer reports it (see `badge-uses-axe-results.json`) |
| Danger button text in dark theme | 4.15/3.78:1 -> >= 5.1:1 on every surface |
| No behavioural/API change | full frontend suite 176/176 unchanged assertions |

Original acceptance items: unchanged and re-asserted (shell, design tokens, accessibility smoke coverage).
