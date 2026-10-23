# ALV-N018 R01 - Next-story impact
- The next execution item is 45, `STORY-009` (follow-up tasks and reminders, a course story executed in the portal); it does not use this code. It is not started.
- New permissions in future stories must be added to `PermissionRegistry.json` as Active (a test enforces it). If a new permission is high impact it should be added to `PolicyWriteRules.HighImpact` so it needs a typed confirmation; delegable ones are added to the delegation table by an administrator, not in code.
- A future screen that lists accounts for a non-administrator should reuse the target-filter in `AuthController.ListUsers` rather than re-deriving it.
- The policy `expectedVersion` is policy-wide; a future bulk editor can send one write per cell in sequence, re-reading the version from each result.
- No error codes in the 51204 to 51208 range were added; 51209 onward are free. No migration was added.
