# ALV-N002 R09 — Acceptance evidence

| Concern | Evidence |
|---|---|
| Stale-data banner text meets WCAG AA | 3.46:1 -> 5.39:1 light; dark unchanged (>= 6.35:1); `systemStatusContrast.test.ts`; real-browser axe passes and fails on the old CSS at 3.46:1 |
| Rest of the page checked | muted text >= 4.5:1 and status dots >= 3:1 in both themes (already passing, now pinned) |
| No regression anywhere | 516 frontend, 82 mocked browser, 9 real-backend suites incl. the `/system-status` route scans, all pass |
| No behavioural/API change | no markup, behaviour or backend file changed |
