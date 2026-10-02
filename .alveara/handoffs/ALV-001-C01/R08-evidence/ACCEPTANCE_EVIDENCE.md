# ALV-001-C01 R08 — Acceptance evidence

| Concern | Evidence |
|---|---|
| Expired-session banner text meets WCAG AA | 3.16:1 -> 4.92:1 or better (light); dark unchanged (>= 5.17:1); `loginContrast.test.ts`; real-browser axe passes and fails on the old CSS at 3.16:1 |
| MFA-confirmed message meets WCAG AA | 4.10 / 4.41 / 4.40:1 -> 4.95:1 or better in both themes; inline style replaced by a class the test reads; the test forbids inline colour styles on the auth pages |
| Danger banner unchanged and passing | 4.76:1 or better in both themes (pinned) |
| No regression anywhere | 526 frontend, 90 mocked browser, 9 real-backend suites incl. the auth walkthrough, all pass |
| No behavioural/API change | no behaviour or backend file changed; `LoginPage.test.tsx` and `MfaSettingsPage.test.tsx` unchanged and passing |
