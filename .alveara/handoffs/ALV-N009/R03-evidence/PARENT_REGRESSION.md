# ALV-N009 R03 — Parent Regression

`ALV-N009` has no course parent (`storyType: new_production`). The relevant regression surfaces are its stated dependencies: `STORY-001`, `ALV-001-C01`, `ALV-N001`, `ALV-N002` — all `COMPLETE`.

- **`STORY-001`/`ALV-001-C01` (authentication/RBAC):** the only backend file touched this attempt is `Program.cs`, adding one line (`options.SlidingExpiration = false`) to the existing cookie-auth configuration those stories established. This is a policy tightening (a fixed deadline instead of a renewable one), not a contract change — no existing test in either story's own suite asserted sliding-renewal behavior (confirmed by search: no test references `SlidingExpiration`, `renew`, or asserts a changing `ExpiresUtc`). The full 179/179 backend suite passes unmodified.
- **`ALV-N001` (application shell):** untouched by this attempt.
- **`ALV-N002` (core architecture):** untouched by this attempt — no file under `Architecture/` was modified.

This attempt's four touched files are all within `ALV-N009`'s own R01/R02 scope (its `AuthContext.tsx`, its dedicated test file, its backend cookie config, and a permissions-endpoint test file it already extended in R01); none is shared production code any other story's own passing tests depend on beyond what's already verified above.
