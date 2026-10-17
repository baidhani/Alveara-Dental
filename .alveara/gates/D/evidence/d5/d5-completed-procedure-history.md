| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |
|---|---|---|---|---|---|---|
| completed procedure | CompletedProcedures | `UPDATE [CompletedProcedures] SET [ToothKey] = [ToothKey] + 1 WHERE [ToothKey] IS NOT NULL` | REJECTED | 51145 | TR_CompletedProcedures_Immutable | True |
| completed procedure | CompletedProcedures | `DELETE FROM [CompletedProcedures]` | REJECTED | 51145 | TR_CompletedProcedures_Immutable | True |
| completion set | CompletionSets | `UPDATE [CompletionSets] SET [IdempotencyKey] = LEFT(N'overwritten by the Gate D attempt', 33) WHERE [IdempotencyKey] IS NOT NULL` | REJECTED | 51144 | TR_CompletionSets_Immutable | True |
| completion set | CompletionSets | `DELETE FROM [CompletionSets]` | REJECTED | 51144 | TR_CompletionSets_Immutable | True |
| completed procedure amendment | CompletedProcedureAmendments | `UPDATE [CompletedProcedureAmendments] SET [Reason] = LEFT(N'overwritten by the Gate D attempt', 33) WHERE [Reason] IS NOT NULL` | REJECTED | 51146 | TR_CompletedProcedureAmendments_Immutable | True |
| completed procedure amendment | CompletedProcedureAmendments | `DELETE FROM [CompletedProcedureAmendments]` | REJECTED | 51146 | TR_CompletedProcedureAmendments_Immutable | True |
| completion charge (ledger entry) | BillingEntries | `UPDATE [BillingEntries] SET [Amount] = [Amount] + 1 WHERE [Amount] IS NOT NULL` | REJECTED | 51120 | TR_BillingEntries_Immutable | True |
| completion charge (ledger entry) | BillingEntries | `DELETE FROM [BillingEntries]` | REJECTED | 51120 | TR_BillingEntries_Immutable | True |

| Duplicate / stale / correction case | Outcome | Exception | Code | State unchanged |
|---|---|---|---|---|
| completion: the same set again (same key, same request, older plan version) | replayed quietly |  |  | True |
| completion: the same key for a different set | refused | TreatmentPlanException |  | True |
| completion: a new key from the out-of-date plan (stale edit) | refused | ConcurrencyConflictException |  | True |
| amendment: the same key and request again | replayed quietly |  |  | True |
| amendment: a new key from the out-of-date amendment number (stale edit) | refused | ConcurrencyConflictException |  | True |
| reconciliation after all of it | clean |  |  | True |
