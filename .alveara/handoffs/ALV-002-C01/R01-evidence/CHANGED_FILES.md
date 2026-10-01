# ALV-002-C01 R01 — Changed Files

`git diff --stat e6ce38f..ef19d25 -- src/` (course-completion-sync commit → R01 implementation commit):

```
src/Alveara.Api.Tests/AuditLogImmutabilityTests.cs                           |  28 ++
src/Alveara.Api.Tests/ConcurrencyGuardTests.cs                               |  94 +++++
src/Alveara.Api.Tests/IdempotencyGuardTests.cs                               |  82 +++++
src/Alveara.Api.Tests/RecordLifecycleGuardTests.cs                           |  45 +++
src/Alveara.Api/Architecture/Auditing/AuditService.cs                        |  45 +++
src/Alveara.Api/Architecture/Concurrency/ConcurrencyConflictException.cs     |  24 ++
src/Alveara.Api/Architecture/Concurrency/ConcurrencySaveGuard.cs             |  25 ++
src/Alveara.Api/Architecture/Idempotency/IdempotencyGuard.cs                 |  33 ++
src/Alveara.Api/Architecture/Idempotency/IdempotencyReceipt.cs               |  17 +
src/Alveara.Api/Architecture/Identity/AccountService.cs                      |  19 +-
src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs                       |  17 +
src/Alveara.Api/Architecture/Identity/IdentityEntities.cs                    |  17 +
src/Alveara.Api/Architecture/Lifecycle/RecordLifecycleGuard.cs               |  42 +++
src/Alveara.Api/Data/AlveraDbContext.cs                                      |  19 +-
.../20260930231312_AddAuditConcurrencyAndIdempotencyPrimitives.Designer.cs   | 402 +++++++++++++++++++++
.../20260930231312_AddAuditConcurrencyAndIdempotencyPrimitives.cs            |  84 +++++
src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs                   |  40 ++
17 files changed, 1020 insertions(+), 13 deletions(-)
```

| File | Change |
|---|---|
| `Architecture/Auditing/AuditService.cs` | New. Shared audit-event write primitive. |
| `Architecture/Concurrency/ConcurrencyConflictException.cs` | New. Shared conflict exception + `ConcurrencyConflictProblem` record. |
| `Architecture/Concurrency/ConcurrencySaveGuard.cs` | New. Wraps `DbUpdateConcurrencyException` into the shared exception. |
| `Architecture/Idempotency/IdempotencyReceipt.cs` / `IdempotencyGuard.cs` | New. Generalizes `BackgroundJob`'s idempotency-key pattern. |
| `Architecture/Lifecycle/RecordLifecycleGuard.cs` | New. `RecordLifecycleAction` enum + transition guard, no domain semantics. |
| `Architecture/Identity/AccountService.cs` | `AddAudit` now delegates to `AuditService.Record` (no call-site change). |
| `Architecture/Identity/AuditLogEntry.cs` | New nullable `EntityType`/`Reason`/`CorrelationId` columns. |
| `Architecture/Identity/IdentityEntities.cs` | `StaffProfile` gained `RowVersion` (concurrency token) - deliberately not `UserAccount` (see `R01.md`). |
| `Data/AlveraDbContext.cs` | New `DbSet<IdempotencyReceipt>`; `StaffProfile.RowVersion` configured `IsRowVersion()`; unique index on `(CommandType, IdempotencyKey)`. |
| `Migrations/20260930231312_AddAuditConcurrencyAndIdempotencyPrimitives.*` | New migration: `StaffProfiles.RowVersion`, `AuditLogEntries.{EntityType,Reason,CorrelationId}`, `IdempotencyReceipts` table + unique index. |
| `Alveara.Api.Tests/ConcurrencyGuardTests.cs`, `IdempotencyGuardTests.cs`, `RecordLifecycleGuardTests.cs` | New test files. |
| `Alveara.Api.Tests/AuditLogImmutabilityTests.cs` | One new test: transactional audit/business-write coupling. |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched. No frontend (`src/alveara-client/`) file was touched.
