# ALV-N010 R01 — Parent / dependency regression

**Parent regression: not applicable** — `ALV-N010` is a New Production story with no course parent (`parentRegression: "n/a"` in `EXECUTION_STATUS.json`).

**Dependency regression: verified.** The stories this one depends on and the shared code it extends all still pass, with their tests unchanged except as noted:

| Dependency | Evidence on the final code |
|---|---|
| `ALV-003-C01` (patient workspace, tab extension point) | Its backend classes (inside 602/602), its frontend tests (inside 371/371) and its un-mocked walkthrough **14 of 14** re-run from a fresh database. The only edit to one of its tests: `App.patientWorkspaceRoutes.test.tsx` pinned the workspace tab list to exactly three tabs; it now expects the Forms tab (gated by `ViewSignedForms`) — the extension point working as designed. `test/fakePatientServer.ts` gained one protected hook for the forms fake; behaviour unchanged. |
| `STORY-003` (via `ALV-003-C01`) | Its six test files are untouched and pass; its original real-backend walkthrough **7 of 7**. |
| `ALV-002-C01` (audit, idempotency, concurrency primitives) | Used unchanged: `AuditService.Record`, `IdempotencyGuard`, `ConcurrencySaveGuard`/`ConcurrencyConflictException` and the audit-immutability guard (the new immutability guard is added beside it, not in place of it). Its tests pass inside 602/602. |
| `ALV-N009` (permission-aware navigation) | The new nav entry and routes are permission-gated through the same registry and `RequirePermission`; `auth-real-backend` **12 of 12**; Gate A A6 seven-role navigation matrix (3 of 3): a link is rendered exactly when the route is reachable. |
| `ALV-N001`, `ALV-N002`, `ALV-N003`, `ALV-N004` (shell, measurement, configuration, backup) | Their suites pass inside 602/602 and 371/371; the permission-matrix, migration, measurement-event and least-privilege test classes were also run in isolation (55/55) before the full run. |

Files of completed stories touched by the implementation commit are listed in `CHANGED_FILES.md` ("Reading guide"); every change there is additive.
