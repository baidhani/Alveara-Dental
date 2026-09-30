# ALV-001-C01 R03 — Demo Evidence

Primary demo evidence is again a real Chromium browser, running the actual built React application, against a real `Alveara.Api` process and a real (freshly migrated) LocalDB database — extended this attempt with a test that directly reproduces the R02 reviewer's own live finding.

## Result

```
Running 6 tests using 1 worker

  ok 1 … an unauthenticated caller cannot select a role at registration (282ms)
  ok 2 … bootstraps the first admin via the real API and signs in (1.8s)
  ok 3 … enrolls a real TOTP factor and completes an MFA challenge end to end (924ms)
  ok 4 … replaying the exact same successful MFA challenge submission is rejected, not accepted twice (54ms)
  ok 5 … security administration: list users, view detail, change role, set session timeout (322ms)
  ok 6 … permission matrix is visible to the admin and shows every role (109ms)

  6 passed (5.5s)
```

Full raw output: `artifacts/playwright-real-backend/run-output.txt`.

## The replay-rejection test in detail

The R02 reviewer's own reproduction: "the reviewer bootstrapped an account, enrolled and confirmed TOTP, obtained one MFA challenge through password login, computed one current standards-compatible TOTP, and submitted the identical challenge-token/code body twice. The first response was HTTP 200 and the second response was also HTTP 200."

This attempt's new test (`e2e/auth-real-backend.spec.ts`, "replaying the exact same successful MFA challenge submission...") reproduces that exact shape against the real API:

```
POST /api/auth/login {username, password}          → 202, {challengeToken}
POST /api/auth/mfa/challenge {challengeToken, code} → 200  (first submission)
POST /api/auth/mfa/challenge {challengeToken, code} → NOT 200  (identical second submission)
```

The second submission is now rejected — the fix genuinely closes the exact gap the reviewer demonstrated, not a narrower or differently-shaped version of it.

## Disposable artifacts

The `AlveraE2E` LocalDB database, its throwaway admin account, and the local API/Vite dev-server processes used to generate this evidence are disposable session-local artifacts — not committed, contain no real data.
