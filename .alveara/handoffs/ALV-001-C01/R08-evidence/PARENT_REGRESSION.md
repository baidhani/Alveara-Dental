# ALV-001-C01 R08 — Parent and dependency regression

`STORY-001` (secure authentication, portal-verified) is unaffected: no backend file changed, so sign-in, MFA, recovery, lockout and audit behaviour are untouched. The real-backend auth walkthrough (12/12) signs in, enrolls and confirms MFA, uses recovery and the admin flows end to end; the unchanged `LoginPage.test.tsx`, `MfaChallengePage`, `MfaSettingsPage.test.tsx` and `ResetPasswordPage` frontend tests pass.
