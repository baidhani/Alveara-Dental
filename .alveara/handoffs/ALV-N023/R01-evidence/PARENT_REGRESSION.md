# ALV-N023 R01: parent and dependency regression

- Parent regression: n/a (new production story in the stabilization programme). Broad regression `DEFERRED_TO_ALV-N028`; not release-certified.
- Dependency (focused, on `f9c0d2a`): every treatment-plan class (groups A and B, 250 tests), the scheduling classes the booking path touches (`AppointmentSchedulerTests`, `AppointmentsApiTests`), completion and adoption (`CompletedProcedureServiceTests`, `CompletedProcedureAdoptionHardeningTests`), `DiagnosisReconciliationTests`, `ClinicalReportsTests` (group C, 118) - 368/368; the client treatment-plan, calendar and schedule tests 238/238 (242/242 on `2ade215`); repo checks 58/58; the new browser spec 7/7.
- Not run (by the route, brief section 9 CR-1): the full backend suite, all `*ApiTests`, the full vitest and mocked Playwright suites, other real-backend walkthroughs (including `treatment-plans-complete-real-backend`, updated in source only) and the chain. These belong to ALV-N028.
