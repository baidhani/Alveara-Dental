# ALV-016-C01 R02 - Changed files

Implementation commit `486e44c621caf502def3555425d17d1adaa5fd71` on top of `6b58bd1` (the R01 review record): 8 files. Raw: `git diff --numstat 6b58bd1 486e44c`.

## Backend: the retry policy and the completion service (`src/Alveara.Api/Architecture`)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Adopt.cs` (+5 -1) **edit**
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Deadlock.cs` (+40 -0)
- `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.cs` (+10 -2) **edit**
- `src/Alveara.Api/Architecture/Concurrency/DeadlockRetry.cs` (+76 -0)

## Backend tests (`src/Alveara.Api.Tests`)
- `src/Alveara.Api.Tests/CompletionDeadlockTests.cs` (+353 -0)

## Docs and progress
- `PROGRESS.md` (+7 -0)
- `docs/COMPLETION.md` (+12 -1) **edit**
- `docs/testing/COMPLETION_DEADLOCK_NEGATIVE_CONTROLS.md` (+27 -0)

## Not in the implementation commit (this handoff)
- `.alveara/handoffs/ALV-016-C01/R02.md` and `R02-evidence/` (this attempt's handoff, evidence and artifacts), `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `PROGRESS.md`: the evidence/status commit. R01's handoff, evidence and ZIP are untouched.

## Files deliberately not changed
- Every prescription file (R01's implementation, accepted by the review), STORY-016's code and tests, the migrations (no schema change), the controllers, the screens, `ConcurrencySaveGuard` and `TreatmentPlanService.SaveAsync` (the classifier and the retry sit beside and around them, not inside them), and every existing test.
