# ALV-N020 R01: demo evidence

The demonstration is the accumulated chain in a real browser against a real API on a throw-away database (G5 run 3, 2026-11-12), plus the three real-backend walkthroughs (G4). Raw evidence: `artifacts/focused-proof/g5/run3-evidence/` (`evidence.json`, `playwright-report.json`, `runner-steps.json`, `meta.json`, `clock-mode.json`, `playwright-artifacts/`) and `artifacts/focused-proof/g4/`.

| Step | What a person sees | Result |
|---|---|---|
| 1.01 | A first visit to `/admin/users` shows the plain sign-in page, no "session expired" | PASS |
| 1.02 | A wrong password and an unknown name get the same refusal | PASS |
| 1.03 | The administrator signs in with spaces around the name and sees the admin menu | PASS |
| 1.04 / 1.05 | A disabled account is refused; re-enabled, it signs in | PASS |
| 1.06 | After the administrator revokes a user's sessions, that user's next click shows the expiry notice | PASS |
| 2.01 | The front desk registers two synthetic patients | PASS |
| F-037 | The front desk signs out on Brian Baker's page; the administrator signs in on the same browser and lands on the dashboard with no patient shown | PASS |
| 1.10 | After sign-out, Back shows the sign-in page and no patient name or date of birth | PASS |
| F-021 | Inactivating the dentist's staff profile ends the dentist's open session and refuses sign-in; reactivating the profile does not re-enable the login | PASS |
| 1.11 | Starting MFA enrolment without finishing leaves password sign-in unchanged | PASS |
| 13.05 | With every API call answered 502 inside the browser, the signed-in screen and the typed first name stay; when calls succeed again there is no sign-in and no expiry notice | PASS (browser-level) |
| 1.12 | With a 5-minute per-user timeout, the warning appears about 3 minutes in and the expiry sign-out at 5 minutes, at `/login?reason=expired` | PASS (unsaved-form part not asserted) |

Walkthroughs (G4): auth 12 of 12, workspace 14 of 14, permission-policy-admin 8 of 8, each on a fresh database. Screenshots: `g4/*-e2e-out/`.

Not demonstrated in a browser: the staff-inactivation failure alert (the in-app Urgent notification) is proven by server tests and the notification-list component test, not by a browser step.
