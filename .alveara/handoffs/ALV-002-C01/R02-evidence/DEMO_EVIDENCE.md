# ALV-002-C01 R02 — Demo Evidence

## The audit viewer, live

Real-backend/real-browser Playwright test "the audit log page shows real audit entries this session produced, as the admin": navigates to `/admin/audit-log` as the real admin session this spec file's earlier tests authenticated, asserts the `Audit log` heading renders, and asserts two genuinely-produced events are visible in the table - `SessionTimeoutChanged` (from the earlier "security administration" test's own session-timeout update) and `LoginSucceeded` (from this session's own sign-in). No permission-denied text appears for this caller.

The same spec's limited-permission-caller test was extended: a Dentist-role account (holding none of `ManageUsers`/`ViewPermissionMatrix`/`ViewAuditLog`) does not see the "Audit Log" nav link, and a direct visit to `/admin/audit-log` shows permission-denied rather than the page.

## The conflict presentation pattern

`ConcurrencyConflictBanner.test.tsx` demonstrates the component directly (no domain edit flow exists yet to host it in - see "Review-relevant limitations" in `R02.md`): given a `ConcurrencyConflictProblem` for a `StaffProfile` conflict, it renders an alert naming the entity type, states plainly that the caller's changes were not saved, and exposes a single "Reload current version" action that - when clicked - calls the supplied `onReload` callback exactly once. A second test suite proves `authApi.isConcurrencyConflict` correctly identifies a 409 carrying this shape and rejects an unrelated 409, a different error code, a non-409 `ApiError`, and a non-`ApiError` value.

## The genuine audit-persistence-failure policy

`AuditLogImmutabilityTests.A_business_change_and_its_new_required_audit_event_are_both_rolled_back_when_the_audit_INSERT_genuinely_fails`: stages a `SessionTimeoutMinutes` change and a brand-new `SessionTimeoutChanged` audit event (via the real `AuditService.Record` call, not a hand-built entity), then forces that new entry's `Id` to collide with an already-committed row before `SaveChangesAsync` runs. SQL Server rejects the batch with a genuine primary-key violation (`DbUpdateException`, not the in-process `AuditLogImmutableException` preflight guard, since the entry is `Added`, not `Modified`/`Deleted`). A fresh-context query afterward confirms both the business change and the new audit event are absent - proving the "consequential action must not silently succeed without its required audit trail" policy against an actual storage-layer failure.

## Full regression

- Backend: 197/197.
- Frontend: 69/69, `tsc`/build/lint clean.
- Real-backend, real-browser: 9/9, rerun against a freshly dropped/re-migrated `AlveraE2E` LocalDB database and a real `dotnet run` API process built from this attempt's implementation commit. See `artifacts/playwright-real-backend/run-output.txt`.
