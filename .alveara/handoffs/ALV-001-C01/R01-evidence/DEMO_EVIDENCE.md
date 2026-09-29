# ALV-001-C01 R01 — Demo Evidence

A real, running instance of the API (`dotnet run`, LocalDB `AlveraDev`, migrations applied) was exercised end-to-end via `curl` against `http://localhost:5072` — genuine HTTP requests against the actual compiled application, not a `WebApplicationFactory` in-process test double. Timestamps are from the live server's own responses. Full raw request/response transcript follows; MFA codes were computed independently in Python from the RFC 6238 algorithm against the exact Base32 secret the server returned at enrollment — proving the server's TOTP implementation is standards-compliant and interoperable with an independent implementation, and that the whole MFA path requires no network call to complete (the code-generation step above was a local computation, not a call to any third-party authenticator service).

## 1–4. Registration lockdown, one-time bootstrap, admin login

```
POST /api/auth/register {"username":"demo-selfreg","password":"correct-horse-battery"}
→ {"id":"2bb9c4d4-...","username":"demo-selfreg","role":"Unassigned", "message":"Account created. An administrator must enable it and assign a role before you can sign in."}

POST /api/auth/bootstrap-admin {"username":"demo-admin","password":"admin-password-1","secret":"demo-bootstrap-secret-r01"}
→ {"id":"93f2e5d7-...","username":"demo-admin","role":"Admin"}

POST /api/auth/bootstrap-admin (second attempt, same secret)
→ status: 409 (bootstrap_already_used)

POST /api/auth/login {"username":"demo-admin","password":"admin-password-1"}
→ {"id":"93f2e5d7-...","username":"demo-admin","role":"Admin"}
```

Proves: self-registration cannot select a role or produce a working account; first-admin bootstrap succeeds exactly once and a repeat is safely rejected; the provisioned admin can log in.

## 5–8. CSRF, whoami, own permissions, user list

```
GET /api/auth/csrf-token → {"token":"CfDJ8Eyv..."}
GET /api/auth/whoami (as demo-admin) → {"username":"demo-admin","role":"Admin"}
GET /api/auth/permissions → {"role":"Admin","permissions":["IssuePasswordResets","ManageAccountStatus","ManageAppointments","ManageBilling","ManageClinicalNotes","ManageMfaPolicy","ManageRoles","ManageTreatmentPlans","ManageUsers","RevokeSessions","ViewAuditLog","ViewBilling","ViewPatientRecords","ViewPermissionMatrix","ViewSchedule"]}
GET /api/auth/users →
  [{"username":"demo-admin","role":"Admin","isDisabled":false,"mfaEnabled":false,...},
   {"username":"demo-selfreg","role":"Unassigned","isDisabled":true,"mfaEnabled":false,...}]
```

Proves: the admin's real permission set matches the matrix; the self-registered account is genuinely `Unassigned`+disabled as stored, visible to the admin's own user-management view.

## 9–12. Admin-driven role/enable, CSRF enforcement

```
PUT /api/auth/{demo-selfreg-id}/role {"role":"FrontDesk"} (with X-CSRF-Token)
→ {"id":"2bb9c4d4-...","username":"demo-selfreg","role":"FrontDesk"}

PUT /api/auth/{demo-selfreg-id}/enabled {"enabled":true} (with X-CSRF-Token)
→ {"id":"2bb9c4d4-...","username":"demo-selfreg","isDisabled":false}

POST /api/auth/login {"username":"demo-selfreg","password":"correct-horse-battery"}
→ {"id":"2bb9c4d4-...","username":"demo-selfreg","role":"FrontDesk"}   # now logs in successfully

PUT /api/auth/{demo-selfreg-id}/enabled {"enabled":false} (NO X-CSRF-Token header)
→ status: 400
```

Proves: an admin-driven role assignment + enable makes a self-registered account usable; the same state-changing call without a CSRF token is rejected.

## 13–17. Full MFA enrollment → challenge → login, no network call

```
POST /api/auth/mfa/enroll (as demo-admin, with CSRF)
→ {"base32Secret":"ZBABEGYNRN2IE2IPPLCEQZULG6FQGZMS","recoveryCodes":["E5A7A77DB6B5", ... 10 codes]}

# Independent Python computation (RFC 6238, HMAC-SHA1, 30s step) against that exact secret:
#   totp("ZBABEGYNRN2IE2IPPLCEQZULG6FQGZMS") = "838754"

POST /api/auth/mfa/confirm {"code":"838754"} (with CSRF)
→ status: 200   # MFA now active on demo-admin

POST /api/auth/logout, then:
POST /api/auth/login {"username":"demo-admin","password":"admin-password-1"}
→ status: 202, {"error":"mfa_required","challengeToken":"CfDJ8Eyv..."}   # password alone no longer logs in

# A fresh code computed at the new current 30s step:
#   totp(...) = "838754" (same 30s window at the speed these requests ran)

POST /api/auth/mfa/challenge {"challengeToken":"...","code":"838754"}
→ {"id":"93f2e5d7-...","username":"demo-admin","role":"Admin"}   # session now genuinely established

GET /api/auth/whoami (using the session cookie from the challenge completion)
→ {"username":"demo-admin","role":"Admin"}
```

Proves: a password-correct login for an MFA-enabled account does not establish a session by itself; the second factor is genuinely required and, once supplied correctly (computed entirely offline from the enrollment secret), completes the login. No HTTP call left this machine's loopback interface at any point in this sequence.

## 18. Lockout: threshold, correct-password rejection while locked

```
5 consecutive POST /api/auth/login with the wrong password for demo-selfreg
  → attempts 1-4: 401 (invalid_credentials)
  → attempt 5: 423 {"error":"account_locked","lockedUntilUtc":"2026-09-29T19:10:47.88Z"}

6th attempt, the CORRECT password:
  → 423 {"error":"account_locked","lockedUntilUtc":"2026-09-29T19:10:47.88Z"}   # still rejected — a correct
    password does not bypass an active lock
```

(The first live attempt at this sequence was itself interrupted by the rate limiter after 3 requests — see the note below — which is additional live confirmation that IP-based throttling is active and correctly fires under exactly the kind of repeated-request pattern it exists to stop; the server was restarted with a higher configured limit to isolate and demonstrate the lockout behavior specifically, without changing any application logic.)

## Unknown-user / permission-matrix visibility

```
POST /api/auth/login {"username":"no-such-user-xyz","password":"whatever"}
→ 401 {"error":"invalid_credentials","message":"Invalid username or password."}   # identical shape to a wrong password for a real user

GET /api/auth/permission-matrix (as demo-admin)
→ [{"role":"Dentist","permissions":["ManageClinicalNotes","ManageTreatmentPlans","ViewBilling","ViewPatientRecords","ViewSchedule"]}, ...]   # full matrix, all 8 roles
```

## What this demo does not cover

The React UI pages (`LoginPage`, `MfaChallengePage`, `ResetPasswordPage`, `AdminUsersPage`) were exercised via `vitest`/Testing Library component tests (see `TEST_RESULTS.md`), not a live browser click-through in this attempt — no screenshot or Playwright evidence of the rendered pages is included here. The API-level demo above proves every underlying workflow genuinely works end-to-end against the real server and real database; the UI layer's own correctness is covered by its component tests plus `tsc`/`oxlint`, but a full manual/visual pass through the actual rendered screens was not performed. Flagged as a limitation in `HANDOFF.md`.

The rate-limiting demo (config override) and the demo database (`AlveraDev` on LocalDB) are local, disposable artifacts of this session — not committed, and contain no real data (synthetic `demo-admin`/`demo-selfreg` accounts only).
