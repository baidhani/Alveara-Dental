| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |
|---|---|---|---|---|---|---|
| treatment plan identity | TreatmentPlans | `UPDATE [TreatmentPlans] SET [PatientId] = '2cd96d99-dd34-4fda-81a2-29ca893dc79c'` | REJECTED | 51086 | TR_TreatmentPlans_Identity | True |
| treatment plan | TreatmentPlans | `DELETE FROM [TreatmentPlans]` | REJECTED | 51085 | TR_TreatmentPlans_NoDelete | True |
| treatment plan revision | TreatmentPlanRevisions | `UPDATE [TreatmentPlanRevisions] SET [ClinicalRationale] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [ClinicalRationale] IS NOT NULL` | REJECTED | 51101 | TR_TreatmentPlanRevisions_Immutable | True |
| treatment plan revision | TreatmentPlanRevisions | `DELETE FROM [TreatmentPlanRevisions]` | REJECTED | 51101 | TR_TreatmentPlanRevisions_Immutable | True |
| treatment plan revision phases | TreatmentPlanRevisionPhases | `UPDATE [TreatmentPlanRevisionPhases] SET [Name] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Name] IS NOT NULL` | REJECTED | 51102 | TR_TreatmentPlanRevisionPhases_Immutable | True |
| treatment plan revision phases | TreatmentPlanRevisionPhases | `DELETE FROM [TreatmentPlanRevisionPhases]` | REJECTED | 51102 | TR_TreatmentPlanRevisionPhases_Immutable | True |
| treatment plan revision placements | TreatmentPlanRevisionItems | `UPDATE [TreatmentPlanRevisionItems] SET [PhaseNumber] = [PhaseNumber] + 1 WHERE [PhaseNumber] IS NOT NULL` | REJECTED | 51103 | TR_TreatmentPlanRevisionItems_Immutable | True |
| treatment plan revision placements | TreatmentPlanRevisionItems | `DELETE FROM [TreatmentPlanRevisionItems]` | REJECTED | 51103 | TR_TreatmentPlanRevisionItems_Immutable | True |
| treatment plan history | TreatmentPlanEvents | `UPDATE [TreatmentPlanEvents] SET [Title] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Title] IS NOT NULL` | REJECTED | 51091 | TR_TreatmentPlanEvents_Immutable | True |
| treatment plan history | TreatmentPlanEvents | `DELETE FROM [TreatmentPlanEvents]` | REJECTED | 51091 | TR_TreatmentPlanEvents_Immutable | True |
| treatment plan items (the copied fee) | TreatmentPlanItems | `UPDATE [TreatmentPlanItems] SET [Fee] = [Fee] + 1 WHERE [Fee] IS NOT NULL` | REJECTED | 51089 | TR_TreatmentPlanItems_Immutable | True |
| treatment plan items | TreatmentPlanItems | `DELETE FROM [TreatmentPlanItems]` | REJECTED | 51088 | TR_TreatmentPlanItems_NoDelete | True |
| treatment plan state history | TreatmentPlanItemStates | `UPDATE [TreatmentPlanItemStates] SET [Reason] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [Reason] IS NOT NULL` | REJECTED | 51105 | TR_TreatmentPlanItemStates_Immutable | True |
| treatment plan state history | TreatmentPlanItemStates | `DELETE FROM [TreatmentPlanItemStates]` | REJECTED | 51105 | TR_TreatmentPlanItemStates_Immutable | True |
| completed procedure | CompletedProcedures | `UPDATE [CompletedProcedures] SET [ToothKey] = [ToothKey] + 1 WHERE [ToothKey] IS NOT NULL` | REJECTED | 51145 | TR_CompletedProcedures_Immutable | True |
| completed procedure | CompletedProcedures | `DELETE FROM [CompletedProcedures]` | REJECTED | 51145 | TR_CompletedProcedures_Immutable | True |
| completion set | CompletionSets | `UPDATE [CompletionSets] SET [IdempotencyKey] = LEFT(N'overwritten by the Gate C attempt', 33) WHERE [IdempotencyKey] IS NOT NULL` | REJECTED | 51144 | TR_CompletionSets_Immutable | True |
| completion set | CompletionSets | `DELETE FROM [CompletionSets]` | REJECTED | 51144 | TR_CompletionSets_Immutable | True |
| completed plan item (withdraw it) | TreatmentPlanItems | `UPDATE [TreatmentPlanItems] SET [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = (SELECT TOP 1 [Id] FROM [UserAccounts]), [WithdrawnReason] = N'overwritten by the Gate C attempt' WHERE [Id] IN (SELECT [PlanItemId] FROM [CompletedProcedures])` | REJECTED | 51117 | TR_TreatmentPlanItems_CompletedFinal | True |
