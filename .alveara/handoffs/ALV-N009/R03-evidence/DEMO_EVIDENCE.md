# ALV-N009 R03 — Demo Evidence

## Corrected behaviors, demonstrated

### 1. Background polling no longer defeats idle session timeout (ALV-N009-R02-01)

Reproduced the reviewer's runtime-configuration finding as a permanent regression test (`AuthControllerPermissionMatrixTests.cs`, `GET_permissions_does_not_extend_the_session_expiry_on_repeated_calls_SlidingExpiration_is_off`): a signed-in caller calls `GET /api/auth/permissions` four times in a row — exactly the pattern `AuthContext.tsx`'s 30-second poll and focus/visibility handlers produce. Before this attempt: with ASP.NET Core's default `SlidingExpiration = true`, each call after the renewal midpoint would silently push `ExpiresUtc` forward, so continual polling could keep an idle session alive indefinitely. After: `Program.cs` explicitly sets `SlidingExpiration = false`, and the test asserts the reported `sessionExpiresAtUtc` is byte-identical across all four calls — the configured timeout is now an honest, fixed deadline no client-side observation traffic can extend.

### 2. A stale response can no longer resurrect a cleared session (ALV-N009-R02-02)

Reproduced the reviewer's own deterministic ordering probe as two permanent regression tests (`AuthContext.test.tsx`):

- `R03: an older in-flight refresh cannot restore signed-in state after a newer 401 already cleared it` — an older `refresh()` call is left pending while a newer one resolves immediately with a 401 and clears state to `signed-out`; the older call is then allowed to resolve with a successful `signed-in` body. Before this attempt: that later resolution would overwrite `signed-out` with the stale `signed-in` state (exactly the failure the reviewer's probe demonstrated). After: the `requestSeqRef` guard recognizes the older call's sequence number no longer matches the current one and discards its result; state stays `signed-out`.
- `R03: logout() invalidates an older in-flight refresh so it cannot restore state after sign-out` — same guarantee, proven against an explicit `logout()` instead of a reactive 401, since the review specifically required "apply the same protection to explicit logout."

## Real-backend, real-browser regression

8/8 passing, unchanged in assertion from R01/R02, rerun against a freshly dropped/re-migrated `AlveraE2E` LocalDB database and a real `dotnet run` API process built from this attempt's implementation commit. See `artifacts/playwright-real-backend/run-output.txt`. No incidental findings this run — both R02 corrections are backend-configuration and client-concurrency fixes that don't materially change the eight flows' own timing (each completes in well under 2 seconds, far short of the 30-second poll interval or any session timeout), so this run is a confirm-no-regression pass; the corrections themselves are proven deterministically via the backend xunit test and the frontend fake-completion-order tests instead (see `TEST_RESULTS.md`).
