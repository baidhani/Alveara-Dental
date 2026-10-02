# ALV-N002 R09 — Test results

Implementation commit `4ba0085393df501923da0602b75c2143387a8a2b`.

| Suite | Result | Artifact |
|---|---|---|
| Frontend `npx vitest run` | **516 / 516** (49 files; +14 `systemStatusContrast`) | `artifacts/test-runs/frontend-vitest.txt` |
| `tsc -b` / `npm run build` / `npm run lint` | exit 0 / 0 / 0 (pre-existing warnings only) | `artifacts/test-runs/tsc-build.txt`, `build.txt`, `lint.txt` |
| Mocked Playwright | **82 / 82** (+4 stale-banner runs) | `artifacts/test-runs/mocked-playwright.txt` |
| Stale-banner contrast against the OLD css | **2 failed, 2 passed** (light fails at 3.46:1; dark passes) | `artifacts/test-runs/stale-contrast-AGAINST-OLD-css-fails.txt` |
| Stale-banner contrast, new css | 4 / 4 | `artifacts/test-runs/stale-contrast-new.txt` |
| Real backend: Gate A route scans (A2, A6) | 3 / 3 (0 critical/serious) | `artifacts/playwright-real-backend/run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Real backend: visit board | 15 / 15 | `run-output-board.txt` |
| Real backend: calendar | 12 / 12 | `run-output-cal.txt` |
| Real backend: forms | 10 / 10 | `run-output-forms.txt` |
| Real backend: patient workspace | 14 / 14 | `run-output-ws.txt` |
| Real backend: STORY-003 registration | 7 / 7 | `run-output-s3.txt` |
| Real backend: scheduling | 10 / 10 | `run-output-sched.txt` |
| Real backend: STORY-011 flow | 10 / 10 | `run-output-s011.txt` |
| Real backend: auth | 12 / 12 (first attempt) | `run-output-auth.txt` |
| Repository checks | 7 / 7 | `artifacts/test-runs/repo-checks.txt` |

Test-first: `systemStatusContrast.test.ts` was written before the fix and failed 1 of 14 (the light stale banner at 3.47:1; the other 13 already passed). Not run: backend suite (no backend file changed).
