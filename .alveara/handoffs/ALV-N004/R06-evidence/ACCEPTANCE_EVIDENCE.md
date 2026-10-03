# ALV-N004 R06 — Acceptance evidence

| Concern | Evidence |
|---|---|
| Backup warning text meets WCAG AA | 3.51:1 -> 5.2:1 or better (light); dark unchanged; `backupContrast.test.ts` (20 tests); real-browser axe passes and fails on the old CSS at 3.51:1 |
| Rest of the page checked | danger/success lines, muted label, alert body text on all three tints, alert borders: all >= 4.5:1 (text) / 3:1 (non-text) in both themes |
| Intermittent stale-settings test made deterministic at its cause | root cause: reload-on-permission-change unmounted the form and discarded the edit; deterministic regression test fails 3/3 on the old page, passes on the new; loaded runs: 1/16 failed before, 16/16 pass after |
| No regression anywhere | 547 frontend, 94 mocked browser, 9 real-backend suites incl. the auth walkthrough through the backup page (12/12 on the third run), all pass |
| No backend/API change | no backend file changed; the page's 34 pre-existing tests unchanged and passing |
