# ALV-002-C01 R03 — Next Story Impact

## New for future stories to build on

- **`GET /api/auth/audit-log` returns the shared `entityType`, `reason`, `correlationId` metadata** (nullable for legacy or metadata-less rows). Gate A's eventual audit evidence and any future filtering/pagination story should use this corrected reader/viewer contract, and extend endpoint and page together.
- **`AUDIT_LOG_WINDOW` (`authApi.ts`) is the single source for the viewer's window**: the request and the description both derive from it. A client that wants more than the server default of 100 rows must pass `take` explicitly; the server clamps at 500.
- **A new field a viewer renders must be asserted end to end.** A mocked component test is not evidence that the real endpoint produces the field; pair it with an API round-trip test and a real-browser assertion on the visible value.
- Carried forward from R02: `ConcurrencyConflictBanner` + `isConcurrencyConflict` for stale-edit UI; `IdempotencyGuard.IsDuplicateReceiptViolation` before treating a `DbUpdateException` as "already processed"; force genuine storage-layer failures when proving "X cannot persist if Y fails".

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 9 (`ALV-N003`) begins.
