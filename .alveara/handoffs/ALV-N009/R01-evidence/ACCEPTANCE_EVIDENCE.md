# ALV-N009 R01 — Acceptance Evidence

5 of 5 acceptance criteria from the authoritative prompt (`GPT Docs/Alveara_Dental_Execution_Plan_v4.md`, item 6), each mapped to specific passing tests.

## 1. Navigation is driven by real authorization data/capabilities rather than fake users or hard-coded display assumptions

- `AppShell.permissionAwareNav.test.tsx` — "hides a nav entry whose required permission the caller does not hold" / "shows a nav entry once the caller holds its required permission": both derive the nav's visible links purely from a stubbed `GET /api/auth/permissions` response, never a hard-coded role name.
- Real-backend Playwright test 7 ("a caller without ManageUsers/ViewPermissionMatrix never sees those nav links...") — a genuinely-created Dentist account, assigned its role through the real API, signs in through the real UI and the real nav omits both admin links. No mocking anywhere in this test.

## 2. Unauthorized direct routes/actions are denied server-side

- `App.routeGuards.test.tsx` — "denies a direct URL to a protected route when signed out" and "shows permission-denied... for a signed-in caller lacking the required permission" both push a `window.history` URL directly (simulating a bookmark/typed URL) and assert denial before the protected page's own content or data fetch ever appears.
- Real-backend Playwright test 7 — the same Dentist account, navigating directly to `/admin/users` and `/admin/permissions` by URL, sees `PermissionDenied`, against the real server, which independently enforces `[RequirePermission]` on the underlying API regardless of what the client renders.

## 3. Session expiry/sign-out removes protected application state

- `AuthContext.test.tsx` — "transitions to signed-out as soon as any authenticated call receives a 401" (reactive session-loss detection) and "logout() clears signed-in state" (`hasPermission` flips to false immediately).
- Real-backend Playwright test 8 — clicking the real "Sign out" button, then navigating directly to `/admin/users`, lands back on the session-expired login screen against the real API; the admin table never renders.

## 4. ALV-N001 shell/layout regression tests remain green

- `App.accessibility.test.tsx`, `App.keyboard.test.tsx`, `App.responsive.test.tsx` — unmodified, pass unchanged (nav filtering and the new account-menu controls sit after the existing nav links in DOM/tab order, so the keyboard-path test's element sequence is unaffected).
- `App.disconnected.test.tsx` — updated (not weakened) for the connectivity banner's relocation from inside the authenticated shell to a global position; still proves the disconnected banner is shown.
- `AppShell.emptyRegistry.test.tsx` — updated to wrap `AuthProvider` (AppShell now depends on `useAuth()`); still proves an empty module registry renders 0 nav links without crashing.

## 5. STORY-001/ALV-001-C01 authentication/RBAC behaviors remain green

- Full backend suite: 178/178 passing, including every `ALV-001-C01`-originated test carried forward unmodified in behavior (176 of them; the 2 new backend tests are additive, proving the MFA-confirm session-continuity fix).
- Full real-backend Playwright flow: tests 1–6 (bootstrap, login, MFA enrollment/challenge, replay rejection, security administration, permission matrix) all pass unmodified in assertion, now additionally proving the caller's own session survives confirming MFA (previously silently broken - see R01.md's "genuine bug" section).

## Summary

**5 of 5 acceptance criteria met**, each with dedicated unit/integration coverage and live real-backend/real-browser proof. No criterion was satisfied only narratively.
