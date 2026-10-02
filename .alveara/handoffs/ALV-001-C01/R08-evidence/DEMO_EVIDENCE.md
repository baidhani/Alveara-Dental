# ALV-001-C01 R08 — Demo evidence

Visible behaviour: the "Your session expired. Please sign in again." banner on `/login?reason=expired`, the sign-in-failure banner, and the "MFA is now active on your account." message. `e2e/login-banner-contrast.spec.ts` renders the first two in a real browser in both themes and runs axe `color-contrast` on them (old CSS: the warning banner fails at 3.16:1; new CSS: passes); the third is pinned by the unit test and passed through by the auth walkthrough. The change is a text colour; the measurements are the evidence.
