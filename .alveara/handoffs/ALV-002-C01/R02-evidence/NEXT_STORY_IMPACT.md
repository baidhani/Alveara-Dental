# ALV-002-C01 R02 — Next Story Impact

## New for future stories to build on

- **`ConcurrencyConflictBanner` + `authApi.isConcurrencyConflict` are the established UI pattern for presenting a stale-edit conflict.** The first future domain story that gives a mutable record (with `RowVersion`-based optimistic concurrency) a real edit flow adopts this component rather than inventing its own conflict UI - the pattern is already tested and ready.
- **`AuditLogPage` is the established pattern for a permission-aware read-only admin viewer.** A future story needing filtering/pagination extends both `GET /api/auth/audit-log` and this page together, rather than building a separate screen.
- **`IdempotencyGuard.IsDuplicateReceiptViolation` is the required check before treating a caught `DbUpdateException` as "already processed."** Any future consumer of `IdempotencyGuard.MarkProcessed` must call this before assuming deduplication succeeded - a broad catch on exception type alone is not safe.
- **A persistence-failure test for a shared primitive should force a genuine storage-layer failure (a real constraint violation), not just the in-process guard that happens to run first.** This attempt's genuine-PK-violation technique (colliding a new entity's Id with an already-committed row) is a reusable technique for proving "X cannot persist if Y fails" claims at the actual transaction boundary.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 9 (`ALV-N003`) begins.
