# ALV-N009 R02 — Next Story Impact

## New for future stories to build on

- **A signed-in session's local expiry timestamp is advisory, not authoritative — always reconcile against the server at the boundary.** `AuthContext`'s expiry effect calls `refresh()` rather than assuming signed-out locally, specifically to stay correct under sliding-expiration cookie renewal. Any future client-side timer keyed off a server-issued timestamp should follow the same pattern: treat the timestamp as "check again around here," not "act on this value directly."
- **`AuthContext` now owns a bounded revalidation poll (30s) plus focus/visibility triggers while signed in.** A future authenticated feature does not need to invent its own "did my permissions change" polling — it already gets a fresh `hasPermission()`/`state.role` at least every 30 seconds and on every tab refocus, for free, from this shared context.
- **A multi-step reauthentication flow (login → MFA challenge) must carry its destination through every hop, not just the first one.** `redirectTo` in `location.state` is now the established pattern for this; a future reauthentication step (e.g. a step-up auth challenge for a sensitive action) should follow the same carry-through rather than defaulting to `/`.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 7 (`STORY-002`, a course portal story) begins.
