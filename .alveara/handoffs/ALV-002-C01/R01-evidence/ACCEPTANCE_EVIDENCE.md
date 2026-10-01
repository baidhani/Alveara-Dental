# ALV-002-C01 R01 — Acceptance Evidence

| Acceptance criterion | Evidence |
|---|---|
| Original STORY-002 tests still pass unchanged | Full backend suite 195/195 passing, including every STORY-002-originated test (`AuditLogImmutabilityTests.cs`'s original 4, `AuthControllerPermissionMatrixTests.GET_audit_log_is_allowed_for_a_role_holding_ViewAuditLog_and_denied_for_one_that_does_not`). |
| Existing account/role changes use the shared audit path | `AccountService.AddAudit` delegates to `AuditService.Record` (src/Alveara.Api/Architecture/Identity/AccountService.cs); `AccountServiceRoleChangeTests.cs` (unmodified) still passes, now exercising the shared path end to end. |
| A representative currently-existing mutable record demonstrates optimistic concurrency/stale-edit rejection | `ConcurrencyGuardTests.Saving_a_stale_read_of_the_same_staff_profile_is_rejected_as_a_concurrency_conflict`, against `StaffProfile.RowVersion`. |
| Shared lifecycle/idempotency primitives are unit-tested without pretending nonexistent clinical/financial records are already finalized | `RecordLifecycleGuardTests.cs` (synthetic allowed-sets only, no real domain record); `IdempotencyGuardTests.cs`. |
| Audit failure policy and audit/business-write coupling are explicit and tested | `AuditLogImmutabilityTests.A_business_change_cannot_commit_if_its_coupled_audit_write_is_invalid`. |
| Later domain stories can adopt the primitives without rewriting STORY-002 behavior | `AuditService.Record`/`ConcurrencySaveGuard`/`IdempotencyGuard`/`RecordLifecycleGuard` are static and dependency-free beyond `AlveraDbContext`; none requires any change to `AccountService` or STORY-002 code to adopt. |

## Required test types — where each lives

| Required test | File |
|---|---|
| Original STORY-002 regression | Full backend suite; `AuditLogImmutabilityTests.cs` (original 4), `AuthControllerPermissionMatrixTests.cs` |
| Audit append/read authorization tests | `AuthControllerPermissionMatrixTests.cs` (unchanged from STORY-002) |
| Audit-write failure/rollback-or-durable-outbox test | `AuditLogImmutabilityTests.A_business_change_cannot_commit_if_its_coupled_audit_write_is_invalid` |
| Optimistic-concurrency race test | `ConcurrencyGuardTests.cs` |
| Idempotency primitive tests | `IdempotencyGuardTests.cs` |
| Lifecycle primitive unit tests | `RecordLifecycleGuardTests.cs` |
