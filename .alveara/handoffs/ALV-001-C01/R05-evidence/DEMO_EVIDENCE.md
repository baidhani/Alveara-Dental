# ALV-001-C01 R05 — Demo Evidence

Primary demo evidence is again a real Chromium browser, running the actual built React application, against a real `Alveara.Api` process and a real (freshly migrated) LocalDB database. Both R05 findings are internal (a test-only threshold-math fix, and a service-layer transaction-rollback fix) with no new user-visible flow, so the same 6-test suite from R04 was rerun unchanged.

## Result

```
Running 6 tests using 1 worker

  ok 1 … an unauthenticated caller cannot select a role at registration (266ms)
  ok 2 … bootstraps the first admin via the real API and signs in (926ms)
  ok 3 … enrolls a real TOTP factor and completes an MFA challenge end to end (1.5s)
  ok 4 … replaying the exact same successful MFA challenge submission is rejected, not accepted twice (92ms)
  ok 5 … security administration: list users, view detail, change role, set session timeout (422ms)
  ok 6 … permission matrix is visible to the admin and shows every role (168ms)

  6 passed (5.6s)
```

Full raw output: `artifacts/playwright-real-backend/run-output.txt`.

## Why no new browser test was added

Finding R04-01 was purely a test-arithmetic defect in a service-level unit test, with no production-code or HTTP-observable change at all. Finding R04-02 is an internal transactional-atomicity property (whether a losing challenge's row is committed-consumed or rolled-back-unconsumed) with no distinct observable HTTP-response shape from the outside - the public API's response to the losing request is `InvalidMfaCodeException` before and after the fix; what changed is what remains true in the database afterward, which the new service-level tests assert directly.

## Disposable artifacts

The `AlveraE2E` LocalDB database, its throwaway admin account, and the local API/Vite dev-server processes used to generate this evidence are disposable session-local artifacts — not committed, contain no real data. The database was dropped and the API process stopped immediately after this run.
