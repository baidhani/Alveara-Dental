# ALV-001-C01 R08 — Test results

Implementation commit `2947e90774e93841a4a9ba96f40ebe5b9fecb685`.

| Suite | Result | Artifact |
|---|---|---|
| Frontend `npx vitest run` | **526 / 526** (50 files; +10 `loginContrast`) | `artifacts/test-runs/frontend-vitest.txt` |
| `tsc -b` / `npm run build` / `npm run lint` | exit 0 / 0 / 0 (pre-existing warnings only) | `artifacts/test-runs/tsc-build.txt`, `build.txt`, `lint.txt` |
| Mocked Playwright | **90 / 90** (+8 login-banner runs) | `artifacts/test-runs/mocked-playwright.txt` |
| Login-banner contrast against the OLD css | **2 failed, 6 passed** (the light expired-session banner at 3.16:1; desktop and tablet) | `artifacts/test-runs/login-contrast-AGAINST-OLD-css-fails.txt` |
| Login-banner contrast, new css | 8 / 8 | `artifacts/test-runs/login-contrast-new.txt` |
| Real backend: Gate A route scans (A2, A6) | 3 / 3 (0 critical/serious) | `artifacts/playwright-real-backend/run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Real backend: visit board | 15 / 15 | `run-output-board.txt` |
| Real backend: calendar | 12 / 12 | `run-output-cal.txt` |
| Real backend: forms | 10 / 10 | `run-output-forms.txt` |
| Real backend: patient workspace | 14 / 14 | `run-output-ws.txt` |
| Real backend: STORY-003 registration | 7 / 7 | `run-output-s3.txt` |
| Real backend: scheduling | 10 / 10 | `run-output-sched.txt` |
| Real backend: STORY-011 flow | 10 / 10 | `run-output-s011.txt` |
| Real backend: auth (sign-in, MFA, recovery) | 12 / 12 (first attempt) | `run-output-auth.txt` |
| Repository checks | 7 / 7 | `artifacts/test-runs/repo-checks.txt` |

Test-first: `loginContrast.test.ts` was written before the fix and failed 4 of 10 (light warning banner at 3.16:1, the success class missing in both themes, the inline colour style). Not run: backend suite (no backend file changed).
