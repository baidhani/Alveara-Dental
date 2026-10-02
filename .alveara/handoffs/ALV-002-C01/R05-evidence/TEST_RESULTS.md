# ALV-002-C01 R05 — Test results

Implementation commit `0416b6dd8e1a04fa25ae16cc2dba0f17681cb619`.

| Suite | Result | Artifact |
|---|---|---|
| Frontend `npx vitest run` | **502 / 502** (48 files; +5 `conflictBannerContrast`, -1 obsolete) | `artifacts/test-runs/frontend-vitest.txt` |
| `tsc -b` / `npm run build` / `npm run lint` | exit 0 / 0 / 0 (pre-existing warnings only) | `artifacts/test-runs/tsc-build.txt`, `build.txt`, `lint.txt` |
| Mocked Playwright | **78 / 78** | `artifacts/test-runs/mocked-playwright.txt` |
| Mutation: banner colour reverted, overrides still removed | board **1 failed**, calendar **1 failed**, workspace **1 failed**, each on axe `color-contrast` of `.alv-concurrency-conflict__title` | `artifacts/playwright-real-backend/MUTATION-*.txt` |
| Real backend: Gate A route scans (A2, A6) | 3 / 3 (0 critical/serious) | `run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Real backend: visit board | 15 / 15 | `run-output-board.txt` |
| Real backend: calendar | 12 / 12 | `run-output-cal.txt` |
| Real backend: forms | 10 / 10 | `run-output-forms.txt` |
| Real backend: patient workspace | 14 / 14 | `run-output-ws.txt` |
| Real backend: STORY-003 registration | 7 / 7 | `run-output-s3.txt` |
| Real backend: scheduling | 10 / 10 | `run-output-sched.txt` |
| Real backend: STORY-011 flow | 10 / 10 | `run-output-s011.txt` |
| Real backend: auth | 12 / 12 (first attempt) | `run-output-auth.txt` |
| Repository checks | 7 / 7 | `artifacts/test-runs/repo-checks.txt` |

Test-first: `conflictBannerContrast.test.ts` was written before the fix and failed 2 of 5 (light title at 3.47:1; three page overrides present). Not run: backend suite (no backend file changed).
