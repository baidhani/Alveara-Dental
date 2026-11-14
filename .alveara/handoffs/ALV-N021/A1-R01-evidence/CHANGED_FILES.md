# ALV-N021 A1-R01: changed files

`git diff --stat 76bd422 807014c` (base = A1-DECISION-01 stored; implementation = `807014c`). No migration, no dependency, no permission, route or configuration change.

```
 PROGRESS.md                                                               |  24 ++++++++++++++
 src/Alveara.Api.Tests/BillingDocumentBuilderTests.cs                      |  30 ++++++++---------
 src/Alveara.Api.Tests/BillingDocumentServiceTests.cs                      |   8 +++--
 src/Alveara.Api.Tests/BillingDocumentTemplateVersionServiceTests.cs       | 198 ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api.Tests/BillingTimelineAgingTests.cs                        |  19 +++++++++++
 src/Alveara.Api.Tests/DiagnosisStructureServiceTests.cs                   |  15 +++++++--
 src/Alveara.Api.Tests/FinancialDocumentTemplateVersionTests.cs            | 207 ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api.Tests/FinancialDocumentV1Baseline.cs                      |  97 +++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api.Tests/Fixtures/financial-documents-v1-baseline.json       |  62 ++++++++++++++++++++++++++++++++++++
 src/Alveara.Api.Tests/OriginalFinancialDocumentBuilderV1.cs               | 114 +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api.Tests/PrescriptionServiceTests.cs                         |  14 ++++++++
 src/Alveara.Api.Tests/ProcedureCatalogLifecycleTests.cs                   |  14 ++++++++
 src/Alveara.Api.Tests/TreatmentPlanStateServiceTests.cs                   |  46 +++++++++++++++++++++++++++
 src/Alveara.Api/Architecture/Billing/BillingService.Documents.cs          |  47 ++++++++++++++++++++-------
 src/Alveara.Api/Architecture/Billing/BillingService.Timeline.cs           |   5 +--
 src/Alveara.Api/Architecture/Billing/FinancialDocumentBuilder.cs          |  84 ++++++++++++++++++++++++++++++++++++++++--------
 src/Alveara.Api/Architecture/Billing/FinancialDocumentContracts.cs        |  25 ++++++++++++---
 src/Alveara.Api/Architecture/Clinical/DiagnosisService.Structure.cs       |   2 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Queries.cs    |  10 +++---
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.States.cs     |  15 +++++++++
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanViews.cs              |   9 ++++--
 src/alveara-client/src/components/PatientFieldsForm.tsx                   |   4 ++-
 src/alveara-client/src/components/dateOnly.test.tsx                       |  66 ++++++++++++++++++++++++++++++++++++++
 src/alveara-client/src/components/dateOnly.ts                             |  22 +++++++++++++
 src/alveara-client/src/pages/documents/documentText.ts                    |   6 ++--
 src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx       |   4 ++-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanAnswers.test.tsx  |  19 ++++++++++-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanItemStateLine.tsx |   4 ++-
 src/alveara-client/src/services/treatmentPlansApi.ts                      |   3 ++
 29 files changed, 1109 insertions(+), 64 deletions(-)
```

Production code: server 8 files (billing documents, builder, contracts, timeline; diagnosis structure; treatment-plan queries, states, views); client 6 files (dateOnly, PatientFieldsForm, documentText, PrintPreview, TreatmentPlanItemStateLine, treatmentPlansApi). Tests and evidence: 12 server files (2 new test classes, the baseline capture, its fixture, the original-builder copy), 2 client test files. PROGRESS.md entries for each step.
