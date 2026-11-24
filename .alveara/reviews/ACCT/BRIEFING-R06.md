# ACCT / ALV-N030 — Briefing R06 Review

**Decision:** APPROVED_FOR_IMPLEMENTATION

**Reviewed:** 2026-11-24

## Scope reviewed

R06 resolves the two changes requested in `BRIEFING-R05.md` for the setup-mode and controlled-restart addendum (section 16 of `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`). The R04 approval remains the governing approval for sections 1–15.

## R06 ruling

R06 adequately resolves both remaining concerns:

1. **CR-7 — client and status availability.** Static client files and the SPA fallback are deliberately available in every startup mode, while mode restrictions apply only to `/api/*`. The anonymous, no-store setup-status response has exact, minimal bodies for `NotInstalled`, `UpgradeRequired`, `Setup`, and `Normal`; S19 and S21 test both API and rendered-browser behavior.
2. **CR-8 — controlled restart.** The completed setup response is sent before a process-level exit code of 75 is set and shutdown begins. The TestHost, dev runner, and future production supervisor each have an explicit responsibility. S20 and S22 prove the code-level contract; ALV-N013 remains responsible for the installed-service supervisor configuration and production proof.

## Implementation boundaries

- ALV-N030 may now implement the approved account/bootstrap scope, including section 16's API-side readiness, setup-mode, client-status, and controlled-restart work.
- It must **not** implement database creation, SQL snapshot settings, migration application, a production service wrapper, or update distribution. Those remain ALV-N013 work.
- The top-of-brief status text should be updated to accurately identify R06 as approved before the first implementation handoff is prepared; this is a documentation consistency correction, not a new design gate.
- Preserve the focused-test policy: routine focused checks are allowed; any run expected to exceed five minutes, broad regression, or chain run still requires the owner’s explicit approval beforehand.

## Required evidence at handoff

Provide the slice-level evidence specified in the brief, including S18–S22 for section 16. A final production-service automatic-restart demonstration is not an ALV-N030 acceptance item; record it as the explicit ALV-N013 dependency proof.

