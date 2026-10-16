# ALV-016-C01 R02 negative controls (the deadlock-victim handling of completion and adoption)

**Purpose:** prove the guards added for review finding ALV-016-C01-R01-01 are held by tests, not just present. Each control breaks one guard on purpose and runs the story's own tests; a control is **killed** when at least one test fails. Risk declaration: HIGH (a shared completion subsystem, a transaction and ledger path).

**Command.** `dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~CompletionDeadlockTests"` (10 tests; the clean baseline passed before the controls, and three further runs in a row on the final code). Each mutant changed exactly one place, the tests ran, and the file was restored byte for byte.

| # | The guard that was broken | Result |
|---|---|---|
| D01 | a deadlock wrapped by EF (`InvalidOperationException` > `DbUpdateException` > `SqlException`) is not recognised | killed |
| D02 | a deadlock inside an `AggregateException` is not recognised | killed |
| D03 | any SQL error is treated as a deadlock | killed |
| D04 | the policy never retries | killed |
| D05 | the policy makes one attempt too many | killed |
| D06 | the policy retries failures that are not deadlocks | killed |
| D07 | an exhausted retry is not turned into `save_failed` | killed |
| D08 | the change tracker is not cleared between attempts | **survived, equivalent** (see below) |
| D09 | a completion is not retried | killed |
| D10 | an adoption is not retried | killed |
| D11 | a retry is not logged | killed |
| D12 | the pause does not grow with each failure | killed |
| D13 | the failure message names the deadlock | killed |

13 controls; 12 killed, 1 equivalent.

**D08, equivalent.** The clear in the retry wrapper is defense in depth. The failed attempt's own handler (`RecordAsync` and `RecordAdoptionAsync`) already calls `db.ChangeTracker.Clear()` before rethrowing, and every read made before the transaction starts is untracked (`AsNoTracking`), so there is nothing left in the tracker for the wrapper to clear: with the line removed the observable behaviour (every test outcome, the data written, the messages) is identical. It is kept because a future change that tracks an entity before the transaction would otherwise carry it into the next attempt.

**Method note.** Restoring a mutated file leaves an older timestamp, so an incremental build can keep a mutant's binary; after the controls the sources were touched and rebuilt before the full regression, and no mutant text was left in any source (checked by search).
