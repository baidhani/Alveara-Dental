# ALV-N020 R01: acceptance evidence

## A. Plan item 57 acceptance (5 parts)
| # | Part | Met? | Evidence |
|---|---|---|---|
| 1 | Fix and evidence every mapped identity/session finding, including F-037 and F-021 | **Yes** | Table B (F-037, F-007, F-012, F-013, F-014, F-015, F-151, F-155, F-156, F-166, F-021; F-017 out of scope by approval); negative controls NC-1, NC-2, NC-3, NC-4, NC-5 |
| 2 | Preserve genuine 401 behavior while distinguishing server/network failure | **Yes** | AuthContext tests (502, 503, 500, network keep the session; 401 ends it as expired); loginOutcome and LoginPage mappings; NC-2; chain 1.06 (revocation -> expiry notice) and 13.05 |
| 3 | Prove disabled/revoked-session behavior, identity-scoped cache clearing, safe recovery | **Yes** | chain 1.04, 1.05, 1.06, F-021; PatientContext identity tests; two-tab tests (G12) and NC-5; outage recovery tests (AuthContext, chain 13.05) |
| 4 | All R05 stage 1 and minimum stage 2 coverage assigned to PKG-02/03 | **Yes, with one deviation** | chain stage 1 (1.01-1.06, 1.10-1.12, F-021, F-037) and minimal stage 2 (2.01, two patients), run 3 all PASS; deviation: 13.05 is browser-level, not a runner-level API stop/restart |
| 5 | Required real-browser Back, expiry, revocation and form-guard cases | **Partly** | Back: chain 1.10 PASS; expiry: chain 1.12 PASS (warning, then `/login?reason=expired`); revocation: chain 1.06 PASS; form kept during an outage: chain 13.05 PASS. **Not proven: the 1.12 requirement that a form with unsaved content keeps it / warns before the expiry sign-out** (the step fills the form but asserts only the warning and the sign-out) |

Result: **4 of 5 met; part 5 partly met.**

## B. Briefing section 11 table (row by row)
| Item | Criterion | Test | Result |
|---|---|---|---|
| F-037 | New user never lands on the previous user's patient | chain `F-037` (frontdesk on Brian's page signs out, admin signs in -> `/`, no "Brian Baker"); `App.routeGuards` different-user-after-expiry | PASS (G5 run 3; re-run B) |
| F-037 | Patient context cleared on identity change | `PatientContext.test` identity cases (clears on a new identity, keeps it for the same person) | PASS (re-run B) |
| F-037 | Two tabs | `App.routeGuards` "two tabs: a DIFFERENT person ... leaves the page for the dashboard" and "the SAME person ... stays"; `AuthContext.test` "a different person found by revalidation advances the identity counter" | PASS (G12; NC-5 effective). Unit/component level, not a two-tab browser test |
| F-037 | No cached patient after Back (1.10) | chain `1.10` | PASS (G5 run 3) |
| F-007 | First visit neutral | `App.routeGuards` first test; `AuthContext.test` "a first visit with no session is neutral"; chain `1.01` | PASS |
| F-007 | Expiry notice after real expiry | `AuthContext.test` "a 401 on revalidation ends the session as expired"; `App.routeGuards` same/different user after expiry; chain 1.06, 1.12 | PASS |
| F-155 | Outage keeps the session UI | `AuthContext.test` it.each 502, 503, 500, network; first-load outage recovery; NC-2 | PASS |
| F-155 | Unsaved form kept during outage | chain `13.05` (typed value kept, no sign-in, recovers) | PASS, browser-level (deviation) |
| F-012 | Expiry text hidden while unreachable | `LoginPage.test` "F-012: hides the expiry notice while the server cannot be reached" | PASS |
| F-012 | Banner timing | G7 re-time 15.4-15.7 s -> 0.3-0.6 s; `useConnectionStatus.test` StrictMode case | REPRODUCED and FIXED |
| F-014 | Error mapping | `loginOutcome.test` (401, 429, 403, 423, 502, 503, 504, 500 proxy, 500 API, network, abort); `LoginPage.test` F-014 cases; NC-2 | PASS |
| F-014 | Login timeout | `authApi.loginTimeout.test` (aborts at 15 s, not before; maps to unreachable; no timer left) | PASS (G11) |
| (R05) | Signed-in visitor on /login | `LoginPage.test` "a signed-in person who opens the sign-in page goes to the dashboard" | PASS |
| F-151/F-166 | Username trimmed | `AccountServiceLoginTests` (+3: login with spaces audited trimmed, failed login audited trimmed, registration trimmed, all-space refused); `LoginPage.test` F-151; chain 1.03 (spaces around the name) | PASS |
| F-156 | Sign-in page theme | `useAppTheme.test` "the sign-in page uses the saved dark theme on a fresh load" | PASS |
| F-013 | Sign-in variant of the banner | `useAppTheme.test` sign-in variant / signed-in wording unchanged | PASS |
| F-015 | Fresh-database sign-in | chain stage-0 `S-09d`; auth walkthrough | NOT REPRODUCED (passes) |
| F-021 | Deactivation disables login and revokes sessions | `StaffInactivationServiceTests` first test (disabled, stamp rotated, both audits); chain `F-021` (open session ends on its next action, sign-in refused) | PASS |
| F-021 | Reactivation does not re-enable | `StaffInactivationServiceTests`; chain `F-021` | PASS |
| F-021 | Atomic failure | `StaffInactivationServiceTests` last-admin (rolled back, failure recorded, alert attempted with the record id) and failing alert; `StaffInactivationAlertTests` unexpected error (rolled back, stamp unchanged, class only, alert, original error surfaced), record cannot be written (no alert, correlation id in refusal and log), Urgent to each enabled Admin/OfficeManager once, idempotent re-send; NC-3, NC-4 | PASS |
| F-021 | Concurrency | `StaffInactivationServiceTests` stale version (409, nothing changed, not a failure) | PASS. The inactivation-versus-re-enable race is not forced by a test |
| F-021 | Unlink with reason, admin only | `ConfigurationApiTests` (practice manager 403 on inactivate and unlink, nothing changed; empty reason 400 `reason_required`; admin with reason unlinks; reactivation needs only configuration authority); `StaffInactivationServiceTests` unlink cases; `StaffLoginDialogs.test` | PASS |
| F-021 | Account list shows linked profile | `AdminUsersPage.test` "shows the staff profile linked to each account, marking an inactive one" | PASS |
| 1.04/1.05 | Disabled account cannot sign in | chain 1.04, 1.05 | PASS |
| 1.06 | Revoked session | chain 1.06 | PASS |
| 1.11 | MFA start without finish | chain 1.11 | PASS |
| 1.12 | Short timeout expiry | chain 1.12: warning, then `/login?reason=expired` | PASS for warning and expiry; **unsaved-form part NOT asserted** |

## C. Migration decision: required proof
| Requirement | Test | Result |
|---|---|---|
| Failure record succeeds, alert send fails: refusal and record remain | `StaffInactivationServiceTests` "A failing alert does not change the outcome or hide the failure record" | PASS |
| Failure-record write fails: no alert; correlation id returned and logged | `StaffInactivationAlertTests` "When the failure record cannot be written ..." | PASS |
| New kind accepted only with its shape; unknown/invalid refused | `StaffInactivationNotificationSchemaTests` (accepted with only a key; refused with a patient, with a message, without a key; unknown kind refused) | PASS; document-link case not separately tested |
| Upgrade and empty-table down as declared; refusal over an alert | `StaffInactivationNotificationSchemaTests` migration test (down restores three kinds, up accepts, down refused 51275 with the row kept) | PASS |
| Notification page label, no patient link | `Notifications.test` new case | PASS |
| Idempotency per recipient | `StaffInactivationAlertTests` re-send twice | PASS |
