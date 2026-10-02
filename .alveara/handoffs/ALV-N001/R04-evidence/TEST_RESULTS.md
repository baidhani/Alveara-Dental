# ALV-N001 R04 — Test results

Implementation commit `7dbcc1640e08a5ed7490139d27b11079bd2c02f5`.

| Suite | Result | Artifact |
|---|---|---|
| Frontend `npx vitest run` | **498 / 498** (47 files; +14 `statusContrast.test.ts`) | `artifacts/test-runs/frontend-vitest.txt` |
| `tsc -b` / `npm run build` / `npm run lint` | exit 0 / 0 / 0 (pre-existing warnings only) | `artifacts/test-runs/tsc-build.txt`, `build.txt`, `lint.txt` |
| Mocked Playwright | **78 / 78** (+4 notification-contrast runs) | `artifacts/test-runs/mocked-playwright.txt` |
| Notification contrast against the OLD css | **2 failed, 2 passed** (light fails at 4.45:1 and 3.46:1; dark passes in the browser, see the limit) | `artifacts/test-runs/notification-contrast-AGAINST-OLD-css-fails.txt` |
| Notification contrast, new css | 4 / 4 | `artifacts/test-runs/notification-contrast-new.txt` |
| Real backend: Gate A route scans (A2, A6) | 3 / 3 (0 critical/serious) | `artifacts/playwright-real-backend/run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Real backend: visit board | 15 / 15 | `run-output-board.txt` |
| Real backend: calendar | 12 / 12 | `run-output-cal.txt` |
| Real backend: forms | 10 / 10 | `run-output-forms.txt` |
| Real backend: patient workspace | 14 / 14 | `run-output-ws.txt` |
| Real backend: STORY-003 registration | 7 / 7 | `run-output-s3.txt` |
| Real backend: scheduling | 10 / 10 | `run-output-sched.txt` |
| Real backend: STORY-011 flow | 10 / 10 | `run-output-s011.txt` |
| Repository checks `node --test tests/*.test.mjs` | 7 / 7 | `artifacts/test-runs/repo-checks.txt` |

Test-first evidence: `statusContrast.test.ts` was written before the fix and failed 9 of 14 (success/warning notifications and session-warning banner in light, success and info in dark, and the two missing tokens). Not run: backend suite and the auth walkthrough (no backend or auth file changed). The real-browser notification test cannot see the dark-theme failures because axe measures against the page behind the toast; the unit test pins those.
