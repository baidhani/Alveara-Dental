# ALV-001-C01 R01 — Parent (STORY-001) Regression

`STORY-001`'s three original acceptance criteria (per `docs/stories/STORY-001.md` and the course portal's own verification) remain true word for word. This story's own prompt explicitly permits revising only the narrow set of STORY-001 tests that encoded the insecure caller-selected-role-assignment behavior; every other STORY-001 test/behavior is preserved unmodified in intent.

| STORY-001 criterion (paraphrased) | Still true? | Regression coverage |
|---|---|---|
| Users can register and authenticate with securely hashed passwords (PBKDF2, no plaintext/reversible storage) | Yes | `AccountServiceRegistrationTests`, `AccountServiceLoginTests` — `Pbkdf2PasswordHasher` unchanged (210,000 iterations, random salt); registration/login still function end-to-end, now via the admin-provisioning path instead of role-at-registration |
| Role-based access control restricts admin-only actions to the Admin role | Yes | `AuthControllerRbacTests`, `AuthControllerPermissionMatrixTests` — RBAC now expressed via the permission matrix rather than a single hardcoded `Roles=Admin` string, but the observable behavior (non-admin denied, admin allowed) is identical or stricter, never weaker |
| Failed logins are rate-limited/locked out after repeated attempts | Yes, and corrected | `AccountServiceLoginTests` lockout suite — the *original* STORY-001 lockout (5-attempt threshold, 15-minute duration) still triggers; this story additionally fixes the rearm-after-expiry defect the reviewer found, which is a bug fix to the existing contract, not new/different behavior a caller would observe as a regression |

## Tests revised (not removed) per the story's explicit instruction

- `AccountServiceRegistrationTests` — the original test asserting a caller could register directly into an arbitrary role (including `Admin`) is replaced by `An_unauthenticated_registration_request_cannot_select_a_role_or_produce_an_enabled_account`, which asserts the corrected, secure behavior.
- `AccountServiceRoleChangeTests`, `AccountServiceLoginTests`, `AuthControllerTests`, `AuthControllerRbacTests` — every test that previously obtained a privileged/working-role account via `RegisterAsync(username, password, role)` (a 3-argument overload that no longer exists) now obtains it via `IdentityTestHelpers.GetOrBootstrapAdminAsync` + `ChangeRoleAsync`/`SetAccountEnabledAsync`, or via the real HTTP `bootstrap-admin` + `PUT .../role` + `PUT .../enabled` endpoints. The *assertions* these tests make about RBAC/lockout/login behavior are unchanged; only the *setup* mechanism changed, because the old setup mechanism was itself the insecure behavior this story removes.

## Verification

`dotnet test` — 148 of 148 passing, including every STORY-001-originated test file listed above. See `TEST_RESULTS.md`.
