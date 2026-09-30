# ALV-N009 R01 — Parent Regression

`ALV-N009` has no course parent (`storyType: new_production`). The relevant regression surfaces are its stated dependencies: `STORY-001`, `ALV-001-C01`, `ALV-N001`, `ALV-N002` — all `COMPLETE`.

- **`STORY-001`/`ALV-001-C01` (authentication/RBAC):** every test originating from these stories (176 backend, carried forward unmodified in behavior into the 178 total) continues to pass. The one production-code change inside `ALV-001-C01`'s own scope (`ConfirmMfaEnrollmentAsync`'s return type, and `mfa/confirm` re-signing-in) is additive/corrective - it fixes a real session-continuity bug without changing any existing passing test's expected behavior, and is itself covered by 2 new dedicated tests. The full real-backend Playwright flow for these stories (tests 1-6) passes unmodified in assertion.
- **`ALV-N001` (application shell):** `App.accessibility.test.tsx`, `App.keyboard.test.tsx`, `App.responsive.test.tsx` pass unmodified. `App.disconnected.test.tsx` and `AppShell.emptyRegistry.test.tsx` were updated (not weakened) for this story's own architectural changes (banner relocation, new `AuthProvider` dependency) and continue to prove the same underlying guarantees.
- **`ALV-N002` (core architecture):** untouched by this attempt - no file under `Architecture/` (background jobs, time/money/storage/backup primitives) was modified.
