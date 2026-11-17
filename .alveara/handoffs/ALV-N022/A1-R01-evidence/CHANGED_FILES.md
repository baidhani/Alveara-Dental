# ALV-N022 A1-R01: changed files

Base `3ab3c4e` (Addendum 02 approval recorded) .. implementation `6414ffa`.

## Commits
```
6414ffa ALV-N022 A1: contradiction flag per brief 4.2; boundary, concurrency and medication-update tests; Safety-tab browser step
ff68e9d ALV-N022 A1: encounter allergy, medication and medical-history actions write to the one patient chart with immutable encounter provenance
```

## Files (git diff --stat)
```
 PROGRESS.md                                                        |  12 +++
 docs/design/ALV-N022_A1_STEP0_AND_ADDENDUM_01.md                   |   4 +
 src/Alveara.Api.Tests/ClinicalRecordServiceTests.cs                |  17 +++-
 src/Alveara.Api.Tests/EncounterChartApiTests.cs                    | 112 ++++++++++++++++++++++++
 src/Alveara.Api.Tests/EncounterChartRoutingTests.cs                | 405 +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api.Tests/EncounterServiceTests.cs                     | 178 ++++++++++++++++++++++++++-----------
 src/Alveara.Api.Tests/EncountersApiTests.cs                        |  42 ++++++---
 src/Alveara.Api/Architecture/Clinical/ClinicalRecord.cs            |  22 +++++
 src/Alveara.Api/Architecture/Clinical/ClinicalRecordReader.cs      |   5 +-
 src/Alveara.Api/Architecture/Clinical/ClinicalRecordService.cs     |  98 ++++++++++++++++++---
 src/Alveara.Api/Architecture/Clinical/ClinicalRecordViews.cs       |  12 ++-
 src/Alveara.Api/Architecture/Clinical/EncounterChartSections.cs    |  96 ++++++++++++++++++++
 src/Alveara.Api/Architecture/Clinical/EncounterReader.cs           |  21 ++++-
 src/Alveara.Api/Architecture/Clinical/EncounterService.Chart.cs    |  97 +++++++++++++++++++++
 src/Alveara.Api/Architecture/Clinical/EncounterService.cs          |  14 ++-
 src/Alveara.Api/Architecture/Clinical/EncounterViews.cs            |  34 +++++++-
 src/Alveara.Api/Controllers/ClinicalController.cs                  |  29 +++++--
 src/Alveara.Api/Controllers/ClinicalRecordController.cs            |   2 +-
 src/alveara-client/e2e/clinical-real-backend.spec.ts               |  65 +++++++++-----
 src/alveara-client/e2e/encounter-chart-real-backend.spec.ts        | 166 +++++++++++++++++++++++++++++++++++
 src/alveara-client/e2e/safety-real-backend.spec.ts                 |  14 +--
 src/alveara-client/playwright.config.ts                            |   2 +-
 src/alveara-client/playwright.encounter-chart.config.ts            |  21 +++++
 src/alveara-client/src/pages/clinical/ChartSectionPanel.test.tsx   | 103 ++++++++++++++++++++++
 src/alveara-client/src/pages/clinical/ChartSectionPanel.tsx        | 191 ++++++++++++++++++++++++++++++++++++++++
 src/alveara-client/src/pages/clinical/Clinical.css                 |  12 +++
 src/alveara-client/src/pages/clinical/EncounterView.tsx            |  32 +++++--
 src/alveara-client/src/pages/clinical/record/RecordItemRow.tsx     |   2 +-
 src/alveara-client/src/services/clinicalApi.ts                     |  63 +++++++++++---
 src/alveara-client/src/services/clinicalRecordApi.ts               |  13 +--
 src/alveara-client/src/services/safetyApi.ts                       |  10 +++
 src/alveara-client/src/services/safetyApi.withSafetyNotice.test.ts |  34 ++++++++
 tests/safety-notice-guard.test.mjs                                 |  72 +++++++++++++++
 33 files changed, 1851 insertions(+), 149 deletions(-)
```

## By role
- Server product: `Architecture/Clinical/{ClinicalRecord,ClinicalRecordReader,ClinicalRecordService,ClinicalRecordViews,EncounterReader,EncounterService,EncounterViews}.cs`, new `EncounterChartSections.cs`, `EncounterService.Chart.cs`; `Controllers/{ClinicalController,ClinicalRecordController}.cs`.
- Client product: `services/{clinicalApi,clinicalRecordApi,safetyApi}.ts`, `pages/clinical/{EncounterView.tsx,Clinical.css,record/RecordItemRow.tsx}`, new `ChartSectionPanel.tsx`.
- Tests: server `{ClinicalRecordServiceTests,EncounterServiceTests,EncountersApiTests}.cs` (rewritten), new `EncounterChartRoutingTests.cs`, `EncounterChartApiTests.cs`; client new `ChartSectionPanel.test.tsx`, `safetyApi.withSafetyNotice.test.ts`; repo new `tests/safety-notice-guard.test.mjs`; browser `e2e/{clinical,safety}-real-backend.spec.ts` (rewritten), new `e2e/encounter-chart-real-backend.spec.ts` + `playwright.encounter-chart.config.ts`, `playwright.config.ts` (exclusion list).
- Documents: `docs/design/ALV-N022_A1_STEP0_AND_ADDENDUM_01.md` (section 5: M1 not needed), `PROGRESS.md`.
- No migration, no package change.
