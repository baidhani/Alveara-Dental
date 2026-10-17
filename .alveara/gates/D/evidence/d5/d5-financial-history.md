| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |
|---|---|---|---|---|---|---|
| ledger entry | BillingEntries | `UPDATE [BillingEntries] SET [Amount] = [Amount] + 1 WHERE [Amount] IS NOT NULL` | REJECTED | 51120 | TR_BillingEntries_Immutable | True |
| ledger entry | BillingEntries | `DELETE FROM [BillingEntries]` | REJECTED | 51120 | TR_BillingEntries_Immutable | True |
| ledger entry | BillingEntries | `UPDATE [BillingEntries] SET [IdempotencyKey] = N'overwritten' WHERE [IdempotencyKey] IS NOT NULL` | REJECTED | 51120 | TR_BillingEntries_Immutable | True |
| billing account | BillingAccounts | `UPDATE [BillingAccounts] SET [PatientId] = '0779088e-7a73-444e-8b9c-c2115e5b0d35'` | REJECTED | 51119 | TR_BillingAccounts_Identity | True |
| billing account | BillingAccounts | `DELETE FROM [BillingAccounts]` | REJECTED | 51118 | TR_BillingAccounts_NoDelete | True |
| payment allocation | BillingAllocations | `UPDATE [BillingAllocations] SET [Amount] = [Amount] + 1 WHERE [Amount] IS NOT NULL` | REJECTED | 51127 | TR_BillingAllocations_Immutable | True |
| payment allocation | BillingAllocations | `DELETE FROM [BillingAllocations]` | REJECTED | 51127 | TR_BillingAllocations_Immutable | True |
| allocation release (refund) | BillingAllocationReleases | `UPDATE [BillingAllocationReleases] SET [Amount] = [Amount] + 1 WHERE [Amount] IS NOT NULL` | REJECTED | 51164 | TR_BillingAllocationReleases_Immutable | True |
| allocation release (refund) | BillingAllocationReleases | `DELETE FROM [BillingAllocationReleases]` | REJECTED | 51164 | TR_BillingAllocationReleases_Immutable | True |
| issued statement | FinancialDocuments | `UPDATE [FinancialDocuments] SET [DocumentNumber] = [DocumentNumber] + 1 WHERE [DocumentNumber] IS NOT NULL` | REJECTED | 51169 | TR_FinancialDocuments_Immutable | True |
| issued statement | FinancialDocuments | `DELETE FROM [FinancialDocuments]` | REJECTED | 51169 | TR_FinancialDocuments_Immutable | True |

| Duplicate / stale / correction case | Outcome | Exception | Code | State unchanged |
|---|---|---|---|---|
| charge: the same key again | replayed quietly |  |  | True |
| payment: the same key and request again, from the older account version | replayed quietly |  |  | True |
| payment: the same key for a different amount | refused | BillingException | idempotency_key_reused | True |
| payment: a new key from the out-of-date account (stale edit) | refused | ConcurrencyConflictException |  | True |
| payment: two simultaneous submissions from the same account version | one recorded, one conflict | ConcurrencyConflictException |  | True |
| adjustment: the same key again | replayed quietly |  |  | True |
| adjustment: from the out-of-date account (stale edit) | refused | ConcurrencyConflictException |  | True |
| refund: the same key and request again, from the older account version | replayed quietly |  |  | True |
| refund: the same key for a different amount | refused | BillingException | idempotency_key_reused | True |
| refund: from the out-of-date account (stale edit) | refused | ConcurrencyConflictException |  | True |
| reversal: the same key and request again, from the older account version | replayed quietly |  |  | True |
| reversal: the same key for a different entry | refused | BillingException | idempotency_key_reused | True |
| reversal: from the out-of-date account (stale edit) | refused | ConcurrencyConflictException |  | True |
| statement: issued again under the same key | replayed quietly |  |  | True |
