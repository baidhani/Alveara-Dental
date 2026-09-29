# ALV-001-C01 R02 — Test Results

All commands run from the repository at implementation commit `f5757719df03eb2e3a7759e0a89eefca71232739`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 158 of 158 passing, 0 failed, 0 skipped.**

10 new tests this attempt, all in `AccountServiceMfaTests.cs`:
- `Starting_re_enrollment_does_not_disable_the_already_established_factor`
- `Replacing_MFA_requires_the_current_password_and_the_wrong_password_is_rejected`
- `Confirming_a_replacement_atomically_swaps_the_factor_and_recovery_codes_and_rotates_the_security_stamp`
- `Two_concurrent_confirmations_of_the_same_pending_enrollment_apply_at_most_once`
- `An_MFA_challenge_issued_before_an_explicit_session_revocation_is_rejected_afterward`
- `An_MFA_challenge_issued_before_a_password_reset_is_rejected_afterward`
- `An_MFA_challenge_issued_before_a_role_change_is_rejected_afterward`
- `Beginning_enrollment_writes_an_MfaEnrollmentStarted_audit_entry`
- `Consuming_a_recovery_code_writes_an_MfaRecoveryCodeUsed_audit_entry_and_the_final_LoginSucceeded_only_fires_after_MFA`
- `A_failed_MFA_challenge_writes_an_MfaChallengeFailed_audit_entry`

Every test from R01 (148) continues to pass, including the full carried-forward STORY-001 regression suite.

## Frontend — `npx vitest run`

```
cd src/alveara-client && npx vitest run
```

**Result: 16 test files, 40 tests, all passing, 0 unhandled errors.**

6 new tests this attempt: `MfaSettingsPage.test.tsx` (2 — shows secret/codes after starting enrollment; asks for current password before replacing an active factor), `PermissionMatrixPage.test.tsx` (2 — renders the full matrix; shows permission-denied), `AdminUserDetailPage.test.tsx` (2 — shows details and updates session timeout; shows not-found for an unknown id).

## Frontend — `npx tsc -b`

Clean, zero errors.

## Frontend — `npx oxlint`

Exit code 0. Same pre-existing warning pattern as R01 (four `react(only-export-components)`/`react(set-state-in-effect)` warnings on files using an already-established pattern in this repo), no new warnings introduced, no errors.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
$env:ConnectionStrings__Alveara = "...AlveraE2E..."; $env:AdminBootstrapSecret = "e2e-real-backend-secret"
dotnet run --no-build

# second terminal, from src/alveara-client
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
npx playwright test --config=playwright.auth.config.ts
```

**Result: 5 of 5 passing** against a genuinely fresh, migrated database and a real running API process (no `page.route()` mocking anywhere in this spec). Raw output: `R02-evidence/artifacts/playwright-real-backend/run-output.txt`. Full procedure: `docs/testing/REAL_BACKEND_E2E.md`.

This run caught and led to the fix of a genuine production bug no mocked/component test had caught: `authApi.ts`'s `request()` threw on the empty-200-body responses `mfa/confirm` and `logout` actually return. See `HANDOFF.md`'s "Corrections made" section for detail.
