# ALV-001-C01 R01 — Test Results

All commands run from the repository at implementation commit `01afb667cb18d37643ae48ca3341477b05b04ed`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 148 of 148 passing, 0 failed, 0 skipped.**

This includes:
- The full pre-existing test suite from `STORY-001`/`ALV-N002` (migration, transaction-rollback, storage-isolation, least-privilege-access, PHI-safe-logging, background-job, measurement-event, system-status tests, etc.) — none regressed.
- `AccountServiceRegistrationTests` (5) — self-service registration produces `Unassigned`+disabled, never a caller-selected role.
- `AccountServiceBootstrapTests` (4) — correct-secret success, wrong-secret rejection, second-use rejection, concurrent-race-leaves-exactly-one-admin.
- `AccountServiceRoleChangeTests` (3) — role change, no-op same-role, unknown-account error.
- `AccountServiceLoginTests` (10) — correct/incorrect/unknown-username login, timing-parity (structural KDF call-count proof), disabled-account rejection, 5-failure lockout, counter-reset-on-success, lockout rearm-after-expiry, concurrent-failed-login race (no lost increments), correct-password-racing-the-triggering-failure (branches on which legitimate ordering occurred and asserts the matching safety invariant).
- `AccountServiceMfaTests` (7) — enrollment+confirm (correct/wrong code), login pausing for a challenge, TOTP challenge completion, recovery-code challenge completion (one-time use proven), tampered-token rejection.
- `AccountServicePasswordResetTests` (4) — issue+complete+audit, one-time-use, garbage-token rejection, concurrent-use-leaves-exactly-one-change.
- `AccountServiceAuditTests` (4) — account-disabled/enabled/sessions-revoked audit entries name the correct actor; no-op toggle writes no extra entry.
- `AuthControllerTests` (9, HTTP-level via `WebApplicationFactory`) — register/bootstrap/login/CSRF at the real API boundary.
- `AuthControllerRbacTests` (10, HTTP-level) — whoami/logout/session-timeout/role-change/user-list/enable-disable/revoke-sessions, each proven allowed and denied as appropriate.
- `AuthControllerPermissionMatrixTests` (3, HTTP-level) — `GET /api/auth/permissions`, an allowed vs. denied `PUT .../role`, and the permission-matrix visibility endpoint's own gating.
- `PermissionMatrixTests` (7, pure unit) — every role's grant set matches the story's role list; every `Role` enum value has an explicit matrix entry.
- `AuthRateLimitTests` (3, HTTP-level) — over-limit login/registration return 429; the limit is shared across different usernames from the same caller.
- `AuthCookieAttributesTests` (1, HTTP-level) — a real login's `Set-Cookie` header carries `HttpOnly`/`SameSite=Strict`.

## Frontend — `npx vitest run`

```
cd src/alveara-client && npx vitest run
```

**Result: 13 test files, 34 tests, all passing, 0 unhandled errors.**

New in this attempt: `LoginPage.test.tsx` (3 — invalid-credentials, lockout, session-expired-banner messaging) and `AdminUsersPage.test.tsx` (3 — permission-denied, loaded list, empty state). All 28 pre-existing frontend tests (shell/accessibility/responsive/keyboard/routing/component/hook/contrast) still pass unmodified in behavior (one shared test-infrastructure fix in `test/setup.ts`, described in `HANDOFF.md`).

## Frontend — `npx tsc -b`

Clean, zero errors.

## Frontend — `npx oxlint`

Exit code 0. Four pre-existing-pattern warnings only (`react(only-export-components)` on files that intentionally export a hook alongside a provider component — the same pattern already used by `Notification.tsx`; `react(set-state-in-effect)` on the two new fetch-on-mount effects, the same pattern already used by `useSystemStatus.ts`). No errors.
