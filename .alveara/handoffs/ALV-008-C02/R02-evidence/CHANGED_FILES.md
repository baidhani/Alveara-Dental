# ALV-008-C02 R02 - Changed files

R02 implementation commit `d03b9e7751c9b3221f22fbfc8656725b92e18782` (15 files, +6789 -36), on top of R01's implementation commit `5ef44f0ebcf63f5a531b73b7b8c357e339380aa3` (75 files, unchanged) and the R01 review record `04c89fd`. R01's evidence and status commits are likewise untouched.

- `PROGRESS.md` (+7 -0)
- `docs/COMPLETION.md` (+1 -1)
- `src/Alveara.Api.Tests/CompletedProcedureSchemaTests.cs` (+9 -7)
- `src/Alveara.Api.Tests/CompletionChartEvidenceTests.cs` (+329 -0)
- `src/Alveara.Api.Tests/CompletionReconciliationTests.cs` (+25 -15)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedure.cs` (+6 -0)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Adopt.cs` (+5 -1)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Chart.cs` (+17 -0)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Reconciliation.cs` (+10 -11)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.cs` (+4 -1)
- `src/Alveara.Api/Architecture/Odontogram/OdontogramService.CompletionSupport.cs` (+23 -0)
- `src/Alveara.Api/Data/CompletionModel.cs` (+2 -0)
- `src/Alveara.Api/Migrations/20261013170209_AddCompletionChartVersion.Designer.cs` (+6280 -0)
- `src/Alveara.Api/Migrations/20261013170209_AddCompletionChartVersion.cs` (+64 -0)
- `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` (+7 -0)

## What the edits to existing files are
- `CompletedProcedure.cs`, `CompletionModel.cs`, `AlveraDbContextModelSnapshot.cs` and the new migration: the `ToothFindingVersionNumber` column, its check, trigger 51173 and the one-time backfill.
- `CompletedProcedureService.cs` (`CompleteAsync`) and `CompletedProcedureService.Adopt.cs` (`AdoptAsync`): save the chart's moves, read the versions, then add the records carrying them. Nothing else in either changed.
- `CompletedProcedureService.Chart.cs`: `ChartVersionsAsync` (maps a changed finding to a `chart_changed` 409). `OdontogramService.CompletionSupport.cs`: `CompletedVersionsAsync` (an internal read; no chart rule changed).
- `CompletedProcedureService.Reconciliation.cs`: `ChartMismatchesAsync` judges the recorded version instead of choosing one by timestamp; the summary comment says so.
- `CompletedProcedureSchemaTests.cs`: two tests now supply valid chart evidence for their finding rows (assertions unchanged). `CompletionReconciliationTests.cs`: the reproducing test added; the old seeded-mismatch test moved into `CompletionChartEvidenceTests.cs` (new).
- `docs/COMPLETION.md`: the reconciliation paragraph describes the exact-version rule and the backfill.
