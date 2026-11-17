# ALV-N022 A2-R01: changed files

Base `05460a1` (Addendum 03 approval) .. implementation `6348276`.

```
6348276 ALV-N022 A2: a finalized encounter accepts no new diagnosis; completion attribution to Draft or Finalized proven unchanged

 PROGRESS.md                                                                      |   6 +++
 src/Alveara.Api.Tests/CompletedProcedureAdoptionServiceTests.cs                  |  27 +++++++++++++
 src/Alveara.Api.Tests/CompletedProcedureAmendmentTests.cs                        |  19 +++++++++
 src/Alveara.Api.Tests/DiagnosisServiceTests.cs                                   |  38 +++++++++++++++++-
 src/Alveara.Api/Architecture/Clinical/DiagnosisService.cs                        |  12 ++++--
 src/alveara-client/e2e/finalized-encounter-real-backend.spec.ts                  | 153 +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/alveara-client/playwright.config.ts                                          |   2 +-
 src/alveara-client/playwright.finalized-encounter.config.ts                      |  21 ++++++++++
 src/alveara-client/src/pages/diagnosis/DiagnosisForm.tsx                         |   7 +++-
 src/alveara-client/src/pages/diagnosis/DiagnosisFormEncounters.test.tsx          |  40 +++++++++++++++++++
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanAdoptCompletion.test.tsx |  12 ++++++
 11 files changed, 330 insertions(+), 7 deletions(-)
```

Production: `DiagnosisService.cs` (server), `DiagnosisForm.tsx` (client). Tests: `DiagnosisServiceTests.cs`, `CompletedProcedureAdoptionServiceTests.cs`, `CompletedProcedureAmendmentTests.cs`, `DiagnosisFormEncounters.test.tsx` (new), `TreatmentPlanAdoptCompletion.test.tsx`, `e2e/finalized-encounter-real-backend.spec.ts` (new), `playwright.finalized-encounter.config.ts` (new), `playwright.config.ts` (exclusion list). No migration.
