# ALV-N004 R06 — Test results

Implementation commit `3c6ac1dccb271966d7bca4ee0cbd9e78316ee01a`.

| Suite | Result | Artifact |
|---|---|---|
| Frontend `npx vitest run` | **547 / 547** (51 files; +20 `backupContrast`, +1 deterministic test) | `artifacts/test-runs/frontend-vitest.txt` |
| `tsc -b` / `npm run build` / `npm run lint` | exit 0 / 0 / 0 (pre-existing warnings only) | `artifacts/test-runs/tsc-build.txt`, `build.txt`, `lint.txt` |
| Mocked Playwright | **94 / 94** (+4 backup-contrast runs) | `artifacts/test-runs/mocked-playwright.txt` |
| Backup contrast against the OLD css | **2 failed, 2 passed** (light warning check line at 3.51:1; dark passes) | `artifacts/test-runs/backup-contrast-AGAINST-OLD-css-fails.txt` |
| Backup contrast, new css | 4 / 4 | `artifacts/test-runs/backup-contrast-new.txt` |
| Backup test file under load: before / timeout-only attempt / fixed | 1 of 16 failed / 2 of 16 failed (at 10 s) / **16 of 16 passed** | `artifacts/test-runs/stress-summary.txt`, `before-failing-run-example.txt`, `after-timeout-attempt-failing-run-example.txt` |
| Real backend: Gate A route scans (A2, A6) | 3 / 3 (0 critical/serious) | `artifacts/playwright-real-backend/run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Real backend: visit board | 15 / 15 | `run-output-board.txt` |
| Real backend: calendar | 12 / 12 | `run-output-cal.txt` |
| Real backend: forms | 10 / 10 | `run-output-forms.txt` |
| Real backend: patient workspace | 14 / 14 | `run-output-ws.txt` |
| Real backend: STORY-003 registration | 7 / 7 | `run-output-s3.txt` |
| Real backend: scheduling | 10 / 10 | `run-output-sched.txt` |
| Real backend: STORY-011 flow | 10 / 10 | `run-output-s011.txt` |
| Real backend: auth (incl. backup and recovery through the page) | run 1: 9 passed, 1 failed (recovery-key 5 s wait), 2 not run; run 2: same; **run 3: 12 / 12** | `run-output-auth-first-attempt-recovery-key-timeout.txt`, `run-output-auth-second-attempt-recovery-key-timeout.txt`, `run-output-auth-passing-rerun.txt` |
| Repository checks | 7 / 7 | `artifacts/test-runs/repo-checks.txt` |

Test-first: `backupContrast.test.ts` failed 1 of 20 before the fix (the light warning check line); the edit-preservation test was written before the page change and fails 3 of 3 against the old page. Not run: backend suite (no backend file changed).
