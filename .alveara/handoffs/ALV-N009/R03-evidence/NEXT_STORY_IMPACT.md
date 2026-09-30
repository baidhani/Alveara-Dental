# ALV-N009 R03 — Next Story Impact

## New for future stories to build on

- **Cookie-authenticated sessions in this system now use a fixed, non-sliding expiry (`SlidingExpiration = false`, `Program.cs`).** A future feature must not assume that making authenticated calls keeps a session alive indefinitely. Session length is now a hard ceiling from sign-in time (`SessionTimeoutMinutes`), not an idle timer — any future UX that wants a *genuinely* activity-extended session would need to explicitly re-sign-in (the same `SignInAsync` pattern `AuthController` already uses for login/MFA-confirm), not rely on ordinary API traffic.
- **Any concurrent-refresh/shared-async-state pattern should use a monotonic sequence guard by default, not just when a race is found.** `AuthContext.tsx`'s `requestSeqRef` is the established pattern for "multiple async triggers can update the same piece of state, and only the most recent decision should ever win" — a future feature with a similar shape (e.g. a live-updating dashboard polling several endpoints) should copy this pattern proactively rather than waiting for a reviewer to construct the out-of-order probe that finds the gap.
- **Packaging discipline:** the documented two-commit `HANDOFF_SCHEMA.md` sequence (implementation, then evidence) should be followed literally, with SHA backfilling done only in generated, non-committed package files (`MANIFEST.json`, the packaged `evidence/GIT_STATE.md`) — not via an extra repository commit, which creates its own traceability gap between the manifest's declared evidence SHA and the actual packaged snapshot's commit.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 7 (`STORY-002`, a course portal story) begins.
