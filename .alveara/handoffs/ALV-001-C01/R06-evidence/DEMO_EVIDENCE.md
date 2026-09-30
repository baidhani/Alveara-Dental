# ALV-001-C01 R06 — Demo Evidence

Primary demo evidence is again a real Chromium browser, running the actual built React application, against a real `Alveara.Api` process and a real (freshly migrated) LocalDB database. This attempt is test-infrastructure-only (three internal, no-op-in-production coordination seams plus rewritten unit tests), with no user-visible flow change, so the same 6-test suite was rerun unchanged.

## Result

```
Running 6 tests using 1 worker

  ok 1 … an unauthenticated caller cannot select a role at registration (256ms)
  ok 2 … bootstraps the first admin via the real API and signs in (946ms)
  ok 3 … enrolls a real TOTP factor and completes an MFA challenge end to end (1.5s)
  ok 4 … replaying the exact same successful MFA challenge submission is rejected, not accepted twice (83ms)
  ok 5 … security administration: list users, view detail, change role, set session timeout (437ms)
  ok 6 … permission matrix is visible to the admin and shows every role (138ms)

  6 passed (5.6s)
```

Full raw output: `artifacts/playwright-real-backend/run-output.txt`.

## Why no new browser test was added

Both R05 findings were evidence gaps in service-level unit tests - the underlying production logic (the step-up row-count check, the recovery-code rollback) was already correct per the reviewer's own non-blocking observation. This attempt adds deterministic *proof* at the level the bugs exist (in-process concurrency/transaction coordination), which a black-box browser test cannot meaningfully add to: the public HTTP behavior is identical before and after this attempt.

## Disposable artifacts

The `AlveraE2E` LocalDB database, its throwaway admin account, and the local API/Vite dev-server processes used to generate this evidence are disposable session-local artifacts — not committed, contain no real data. The database was dropped and the API process stopped immediately after this run.
