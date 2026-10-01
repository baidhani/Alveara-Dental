# ALV-002-C01 R01 — Demo Evidence

This story is backend-architecture-level infrastructure (shared primitives), not a user-facing workflow — there is no new screen or visible interaction to walk through beyond what `STORY-002` already demonstrates (the audit-log viewer, already built and unchanged). Each primitive's behavior is demonstrated via a focused integration test against a real LocalDB database, which is the appropriate form of "demo evidence" for infrastructure a future domain story will build visible workflows on top of.

## 1. Shared audit path (no visible change, by design)

`AccountServiceRoleChangeTests.Changing_a_user_s_role_updates_it_and_writes_an_audit_entry_naming_the_actor` (unmodified from STORY-002) still passes, now exercising `AuditService.Record` internally. The audit entry it asserts on is byte-identical in shape to before this story - proving the migration to the shared path is genuinely invisible to every existing caller.

## 2. Optimistic concurrency - stale-edit rejection

`ConcurrencyGuardTests.Saving_a_stale_read_of_the_same_staff_profile_is_rejected_as_a_concurrency_conflict`: two contexts read the same `StaffProfile` row; the first saves a change successfully; the second (holding the now-stale version token) attempts its own save and receives a `ConcurrencyConflictException` naming the correct entity type and id, while the first caller's change is what's actually persisted.

## 3. A genuine regression caught and demonstrated by the regression suite itself

The intermediate attempt (RowVersion on `UserAccount`) is itself a form of demonstration: the regression suite caught three real `ALV-001-C01` test failures the moment the (incorrect) design was run, proving the suite's own sensitivity to exactly this class of bug. After moving to `StaffProfile`, the same three tests pass again, and the new concurrency behavior is demonstrated cleanly on a record with no competing write path.

## 4. Idempotency - duplicate command rejection

`IdempotencyGuardTests.A_duplicate_command_key_is_rejected_by_the_database_s_unique_constraint`: two separate contexts attempt to mark the exact same `(CommandType, IdempotencyKey)` pair processed; the second raises a `DbUpdateException` from the real database unique index, not an application-level race.

## 5. Lifecycle guard - unknown transition rejection

`RecordLifecycleGuardTests.A_requested_transition_outside_the_allowed_set_throws_UnknownLifecycleTransitionException`: a requested `RecordLifecycleAction.Void` against an allowed-set containing only `Finalize` throws, naming the rejected action.

## 6. Audit/business-write transactional coupling

`AuditLogImmutabilityTests.A_business_change_cannot_commit_if_its_coupled_audit_write_is_invalid`: a `StaffProfile`-unrelated business change (a `SessionTimeoutMinutes` update on a `UserAccount`) and an invalid audit mutation are staged on the same context; the single `SaveChangesAsync` call rejects both, and a fresh query proves the business change never persisted.

## Real-backend, real-browser regression

8/8 passing, unchanged in assertion, rerun against a freshly dropped/re-migrated `AlveraE2E` LocalDB database and a real `dotnet run` API process built from this attempt's implementation commit. See `artifacts/playwright-real-backend/run-output.txt`. No incidental findings this run - the shared primitives are backend-only and don't touch any of the eight flows' own behavior.
