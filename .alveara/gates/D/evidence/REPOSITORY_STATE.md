head 851a64b28942cd681379564428bd0de1fa1872a7
start 2026-10-16T19:16:45-05:00
== gate dotnet_build exit=0
== full backend exit=0
A total of 1 test files matched the specified pattern.
Results File: C:\tmp\gated\ev\trx\gated_full.trx

Passed!  - Failed:     0, Passed:  3692, Skipped:     0, Total:  3692, Duration: 1 h 50 m - Alveara.Api.Tests.dll (net10.0)
backend end 2026-10-16T21:07:29-05:00
== gate tsc exit=0
== gate vitest exit=0
   Duration  50.14s (environment 47%, tests 33%, setup 10%, transform 5%, import 5%)

Environment  jsdom was created 93 times · 458.04s total, 47% of tracked time
             create it once per worker with pool: 'vmThreads' (keeps per-file isolation) or isolate: false (shares it across files)
             learn more: https://vitest.dev/guide/improving-performance#test-environments

== gate build exit=0
[33m[plugin builtin:vite-reporter] 
(!) Some chunks are larger than 500 kB after minification. Consider:
- Using dynamic import() to code-split the application
- Use build.rolldownOptions.output.codeSplitting to improve chunking: https://rolldown.rs/reference/OutputOptions.codeSplitting
- Adjust chunk size limit for this warning via build.chunkSizeWarningLimit.[39m
[32m✓ built in 987ms[39m
== gate lint exit=0
src/pages/configuration/PracticeTab.tsx:56:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize React w
src/pages/AuditLogPage.tsx:42:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize React with external 
src/pages/clinical/notes/TemplatesPage.tsx:44:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize Reac
src/pages/AdminUsersPage.tsx:55:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize React with externa
src/pages/configuration/AvailabilityTab.tsx:85:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize Rea
src/pages/PermissionMatrixPage.tsx:37:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize React with e
== gate playwright_mocked exit=0
  ok 131 [tablet] › e2e\system-status-stale-contrast.spec.ts:17:3 › the stale-data banner passes the colour-contrast rule in dark mode (699ms)
  ok 119 [tablet] › e2e\notification-contrast.spec.ts:21:3 › all four notification variants pass the colour-contrast rule in dark mode (1.5s)
  ok 117 [tablet] › e2e\architecture-map.spec.ts:258:5 › Architecture Map tab › has no serious or critical accessibility violations in dark mode, with the dialog open (2.4s)
  ok 136 [tablet] › e2e\command-center.spec.ts:323:5 › Project Management: portal stories and engineering stories › both views have no critical or serious accessibility violations (dark) (2.5s)

  136 passed (18.9s)
== gate repo_checks exit=0
ℹ pass 8
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 1349.548
GATES_DONE
== walkthrough billing
  10 passed (20.1s)
== walkthrough billing_ledger
  11 passed (23.4s)
== walkthrough billing_statements
  9 passed (23.8s)
== walkthrough completion
  11 passed (22.5s)
== walkthrough completion_adoption
  8 passed (15.5s)
== walkthrough prescription_workflow
  15 passed (27.1s)
== walkthrough prescriptions
  10 passed (15.3s)
DONE
