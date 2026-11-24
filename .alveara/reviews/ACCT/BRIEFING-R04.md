# ACCT — Pre-Implementation Brief R04 Review

**Decision:** APPROVED_FOR_IMPLEMENTATION (was APPROVED_PENDING_EXECUTION_RECORD; transitioned 2026-11-24 per the review's own instruction, see below)  
**Reviewed document:** `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md` (R04)

## Architecture and implementation approval

The R04 brief satisfies all six prior review corrections and is approved in substance:

- normal human staff creation is an atomic, linked staff-profile and interactive-account operation;
- Deployment Administrator remains the sole new account-only exception, retains Administrator-equivalent access, and is guided rather than blocked from manual practice configuration;
- Practice Owner is a protected designation for a linked human staff member, not a new broad role;
- anonymous sign-up and the older bootstrap-admin endpoint are removed;
- the setup-token, forced-password-change, reset recovery, audit, migration, upgrade-transition, and authorization rules are adequately specified;
- test provisioning is structurally absent from the product host and product publish output, runs only in the separately built TestHost, and is constrained to loopback addresses; and
- the focused security/test matrix covers the required happy, failure, race, recovery, upgrade, production-boundary, and browser cases.

R04 also correctly limits `DeploymentAdministratorAssigned` to deterministic migration conversion of exactly one legacy profile-less Administrator and covers all TestHost guards G1 through G8.

## Required execution-control action before implementation

`ACCT` is only a working name. It is not yet a record in the authoritative execution plan, execution-status ledger, story catalog, or Architecture Map. No code may begin under a temporary identifier.

The owner must assign a permanent ALV story identifier and authorize the corresponding execution-plan insertion before this approval becomes `APPROVED_FOR_IMPLEMENTATION`. The approved placement is immediately after ALV-N026 and before ALV-N027. The new record must preserve the post-Gate-E focused-proof policy: `fullRegression: DEFERRED_TO_ALV-N028`, `releaseCertified: false`.

Once that documents-only control action is complete, update this review decision to `APPROVED_FOR_IMPLEMENTATION`; no further architecture review or test run is required solely for that transition.

## Transition record (agent, 2026-11-24, session CC-20261123-q4vn)

The owner assigned the permanent id `ALV-N030` and authorized the plan insertion (item 63a, after ALV-N026, before ALV-N027). Records added: `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` item 63a; `.alveara/EXECUTION_STATUS.json` (num 63.1, PLANNED, `fullRegression: DEFERRED_TO_ALV-N028`, `releaseCertified: false`; ALV-N027 and ALV-N028 dependencies updated); `.alveara/story_catalog.json`; `HANDOFF_SCHEMA.md` and `PROGRAM_REGRESSION_LEDGER.md` deferred-regression scope; `BUILD_STATE.md`. Documents only; no code or test run.
