head 45abf514a2e26aa7330ad8e8900330602f92afb1
== gate dotnet_build exit=0
== gate c8 exit=0
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 21 s - Alveara.Api.Tests.dll (net10.0)
== gate tsc exit=0
== gate vitest exit=0
   Start at  11:16:51
   Duration  25.86s (tests 65%, environment 18%, transform 6%, setup 6%, import 5%)

    Isolate  93 workers spawned · ~1.10s startup each (spawn + environment, per file)
             at least ~3.35s faster with isolate: false — reuses workers across files instead of one per file

== gate build exit=0
[33m[plugin builtin:vite-reporter] 
(!) Some chunks are larger than 500 kB after minification. Consider:
- Using dynamic import() to code-split the application
- Use build.rolldownOptions.output.codeSplitting to improve chunking: https://rolldown.rs/reference/OutputOptions.codeSplitting
- Adjust chunk size limit for this warning via build.chunkSizeWarningLimit.[39m
[32m✓ built in 269ms[39m
== gate lint exit=0
src/pages/PermissionMatrixPage.tsx:37:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize React with e
src/pages/BackupRecoveryPage.tsx:75:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize React with ext
src/pages/clinical/notes/TemplatesPage.tsx:44:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize Reac
src/pages/prescription/PrescriptionForm.tsx:23:17: warning react(only-export-components): Fast refresh only works when a file only exports components. Use a new file to share constants or functions be
src/pages/treatmentplan/TreatmentPlanAdoptCompletion.tsx:23:17: warning react(only-export-components): Fast refresh only works when a file only exports components. Use a new file to share constants or
src/pages/configuration/AvailabilityTab.tsx:85:5: warning react(set-state-in-effect): Calling setState synchronously within an effect can trigger cascading renders help: Effects should synchronize Rea
== gate playwright_mocked exit=0
  ok 131 [tablet] › e2e\system-status-stale-contrast.spec.ts:17:3 › the stale-data banner passes the colour-contrast rule in dark mode (727ms)
  ok 104 [tablet] › e2e\command-center.spec.ts:323:5 › Project Management: portal stories and engineering stories › both views have no critical or serious accessibility violations (light) (2.9s)
  ok 112 [tablet] › e2e\architecture-map.spec.ts:258:5 › Architecture Map tab › has no serious or critical accessibility violations in dark mode, with the dialog open (2.5s)
  ok 136 [tablet] › e2e\command-center.spec.ts:323:5 › Project Management: portal stories and engineering stories › both views have no critical or serious accessibility violations (dark) (2.2s)

  136 passed (18.3s)
== gate repo_checks exit=0
ℹ pass 8
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 1253.0251
GATES_DONE
== walkthrough clinical
  9 passed (25.7s)
== walkthrough clinical_companion
  9 passed (34.2s)
== walkthrough safety
  11 passed (27.2s)
== walkthrough c2_points
  6 passed (6.6s)
== walkthrough odontogram
  11 passed (18.6s)
== walkthrough odontogram_longitudinal
  11 passed (23.2s)
== walkthrough perio
  10 passed (25.8s)
== walkthrough perio_sessions
  12 passed (34.9s)
== walkthrough diagnoses
  12 passed (20.5s)
== walkthrough diagnoses_structure
  12 passed (25.6s)
== walkthrough procedures
  8 passed (17.1s)
== walkthrough treatment_plans
  11 passed (14.9s)
== walkthrough treatment_plans_complete
  11 passed (30.6s)
DONE
