# Gate E Stage 1 - repository state of the evidence run

- Run commit (product code and tests under evidence): `173051da01b1eababfb9bf10773283cfa3a6f74d`. It holds the test-only repair `2be1d73` (billing-ledger walkthrough locator) and the predeclared Gate E text `173051d` (QUALITY_GATES row E9, report skeleton); no other change to `src/` since the ALV-N006 closure `22f66e9` (`git diff --name-only 22f66e9 173051d -- src` lists only `src/alveara-client/e2e/billing-ledger-real-backend.spec.ts`).
- Working tree: clean before and after the run (0 changed paths each time).
- Machine: Windows 11, SQL Server Express `.\SQLEXPRESS`, the owner's workstation; walkthroughs each on a fresh database created and dropped by `scripts/Invoke-RealBackendWalkthrough.ps1`.
- Not run in this stage: the full serial backend suite, the all-`ApiTests` run, any walkthrough outside the twelve listed. Walkthrough screenshots are not committed (the run outputs and evidence JSON are).

## Sweep log (`summary.txt`)

```
run commit: 173051da01b1eababfb9bf10773283cfa3a6f74d
working tree before: 0 changed paths (0 = clean)
start: 2026-10-29T19:54:42-05:00
== leak counts before taken
== backend build exit=0
== backend focused (E1-E9 classes) exit=0 elapsed=1561s: Passed!  - Failed:     0, Passed:  1546, Skipped:     0, Total:  1546, Duration: 25 m 52 s - Alveara.Api.Tests.dll (net10.0)
== gate tsc exit=0 elapsed=7s: 
== gate vitest exit=0 elapsed=200s:  Test Files  120 passed (120)       Tests  1879 passed (1879) 
== gate build exit=0 elapsed=9s: [32m✓ built in 1.12s[39m 
== gate lint exit=0 elapsed=3s: 
== gate playwright_mocked exit=0 elapsed=25s:   138 passed (22.9s) 
== gate repo_checks exit=0 elapsed=12s: 
== walkthrough documents exit=0 elapsed=32s:   14 passed (18.2s) 
== walkthrough documents-library exit=0 elapsed=37s:   12 passed (20.8s) 
== walkthrough follow-ups exit=0 elapsed=42s:   8 passed (28.4s) 
== walkthrough work-queue exit=0 elapsed=61s:   8 passed (45.8s) 
== walkthrough external-cases exit=0 elapsed=65s:   9 passed (50.0s) 
== walkthrough patient-merge exit=0 elapsed=38s:   9 passed (21.8s) 
== walkthrough patient-export-package exit=0 elapsed=51s:   10 passed (30.8s) 
== walkthrough patient-portability exit=0 elapsed=42s:   7 passed (15.3s) 
== walkthrough bulk-import exit=0 elapsed=93s:   13 passed (1.3m) 
== walkthrough reports exit=0 elapsed=35s:   7 passed (18.7s) 
== walkthrough notifications exit=0 elapsed=57s:   10 passed (40.0s) 
== walkthrough billing-ledger exit=0 elapsed=44s:   11 passed (26.9s) 
== leak counts after taken
end: 2026-10-29T20:35:41-05:00
working tree after: 0 changed paths
== SWEEP DONE
```
