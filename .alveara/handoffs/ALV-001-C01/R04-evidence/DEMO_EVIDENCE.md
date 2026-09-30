# ALV-001-C01 R04 — Demo Evidence

Primary demo evidence is again a real Chromium browser, running the actual built React application, against a real `Alveara.Api` process and a real (freshly migrated) LocalDB database. R04's two findings are service-layer concurrency/transaction fixes with no new user-visible flow, so the same 6-test suite from R03 was rerun unchanged (including the R03 replay-rejection test, which continues to pass — its underlying rejection path is now additionally audited, which this browser suite does not itself assert; the audit assertions are covered at the service-test level, see `TEST_RESULTS.md`).

## Result

```
Running 6 tests using 1 worker

  ok 1 … an unauthenticated caller cannot select a role at registration (239ms)
  ok 2 … bootstraps the first admin via the real API and signs in (560ms)
  ok 3 … enrolls a real TOTP factor and completes an MFA challenge end to end (1.4s)
  ok 4 … replaying the exact same successful MFA challenge submission is rejected, not accepted twice (72ms)
  ok 5 … security administration: list users, view detail, change role, set session timeout (369ms)
  ok 6 … permission matrix is visible to the admin and shows every role (108ms)

  6 passed (4.9s)
```

Full raw output: `artifacts/playwright-real-backend/run-output.txt`.

## Why no new browser test was added

Both R03 findings are internal atomicity/concurrency properties of `AccountService.cs` (a transaction boundary and an affected-row check) that have no distinct observable HTTP-response shape from the outside — the public API's success/failure responses are identical before and after the fix; what changed is what's true in the database afterward (whether an audit row exists, whether a pending secret was created). That is exactly what the new service-level tests in `AccountServiceMfaTests.cs` assert directly against the database, which a browser-level black-box test cannot do without reaching into the database itself (at which point it stops being a meaningfully different test from the service-level one). The existing real-backend replay-rejection browser test remains the correct browser-level proof that a replay is still rejected end-to-end.

## Disposable artifacts

The `AlveraE2E` LocalDB database, its throwaway admin account, and the local API/Vite dev-server processes used to generate this evidence are disposable session-local artifacts — not committed, contain no real data. The database was dropped and the API process stopped immediately after this run.
