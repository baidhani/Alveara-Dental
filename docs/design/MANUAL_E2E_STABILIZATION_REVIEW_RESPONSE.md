# Manual E2E Stabilization Review — Direction to the Coding Agent

**Date:** 2026-11-09  
**Source:** `C:\.alvera-data\Manual-E2E\alveara-manual-e2e-reviewer-package.zip`  
**Status:** Approved for stabilization planning. Deployment work remains paused until the resulting correction work is planned, implemented, reviewed, and closed.

## Purpose

The first manual end-to-end pass found important workflow, integrity, and operational-readiness gaps despite completed planned feature stories. Treat this as a stabilization phase before release engineering. Do not begin `ALV-N013`, `ALV-N014`, or `ALV-N008` while release-blocking stabilization work is outstanding.

The raw manual log remains evidence. It contains duplicates, withdrawn observations, and later corrections; do not use the README table as the sole source of truth. Preserve the raw finding identifiers in tests, issue notes, and handoffs.

## Required working method

1. Read the package, the current repository, execution-control files, and the authoritative execution plan/reference before editing production code.
2. Produce a concise stabilization implementation plan grouped by shared root cause. For each group, identify the exact finding IDs, affected architecture, permission/audit/data-migration impact, targeted tests, walkthrough impact, and whether it needs a new engineering control story.
3. Resolve factual uncertainty in code and controlled synthetic data before changing behavior. Record a finding as `CONFIRMED`, `NOT_REPRODUCED`, `DUPLICATE`, `WITHDRAWN`, or `DESIGN_DECISION_REQUIRED`; do not silently drop it.
4. Do not make a broad UI redesign or unrelated cleanup while correcting integrity/security behavior.
5. Use the two-stage review policy: package focused implementation evidence at Stage 1; wait for independent approval; run the frozen broader regression only after that approval. Do not start the next correction group until the current one is reviewed and closed.
6. Do not change portal course completion, existing completed-story evidence, or historical audit records merely to make a correction easier.

## Priority 0 — establish the stabilization backlog

Normalize the manual log before implementation:

- Give every design note and finding a unique canonical key. `D-01` and `D-02` are reused for different topics in the supplied materials.
- Link duplicates to one canonical item. Examples: F-099/F-104 are the same numbering-display family; F-116/F-175 are the aging-versus-balance reconciliation family; F-185/F-188 are the treatment-scheduling/follow-up family.
- Keep F-091 as a scoped design check, not a broken missing-tooth invariant: the later evidence proves Existing/Completed missing teeth refuse conflicting findings. Decide and document the meaning of Diagnosed/Planned missing teeth and surface a warning if coexistence is intentional.
- Keep F-104 as a display defect only: Universal tooth 14 and FDI tooth 26 refer to the same tooth. Never alter stored tooth identity to "fix" this.

## Priority 1 — release-blocking integrity, identity, and authorization corrections

### F-021 — staff deactivation and login access

Implement the owner decision:

- Deactivating a staff profile disables its linked login by default and revokes active sessions.
- Reactivating the profile never re-enables the login.
- Only an administrator can explicitly re-enable the login later.
- Profile deactivation, account disablement, session invalidation, and audit outcome must avoid a partial state. Use one transaction where the data shares a transaction boundary.
- Account-link changes and concurrent updates must be safe and versioned.
- A failure must produce a clear operation result and durable operational evidence. The proposed highest-urgency notification may be added, but it cannot be the sole indication of a database/notification failure.

Required tests include deactivation, existing-session invalidation, profile reactivation without login reactivation, administrator-only re-enable, concurrent change behavior, and failure handling.

### F-037 — patient selection survives sign-out or a user change

Treat as release-blocking privacy and wrong-patient risk.

- Clear selected-patient state, cached patient data, and post-login return destination on sign-out, expiry, and identity change.
- A successful new sign-in lands on the dashboard, not the previous user's patient page.
- Re-fetch data under the new identity; do not trust browser-stored patient context.
- Verify two tabs and browser refresh behavior. Refresh may retain same-user context only if it is safely keyed to the active identity.

### F-059/F-063/F-065 and related permission questions

Create a definitive visit-transition and permission matrix before changing buttons:

- Name the allowed state transitions, their required role/permission, whether intermediate states are optional, and who performs the final completion/check-out actions.
- Enforce the matrix in the service/API, then make Calendar, Visit Board, and any other caller show the same permitted actions.
- Record actor and transition in history.
- Re-evaluate hygienist authority to add, retire, or mark allergies/medications in error under the same permission review.

### F-155/F-007/F-014 — outage/session semantics

- A 502/network failure must enter a reconnecting state and preserve the authenticated UI state and unsaved form content where safe.
- Only an actual authentication denial signs the user out.
- A first unauthenticated visit receives neutral sign-in wording; an expiry message is only shown for a real expiry.
- When the API is down, state that the server is unavailable rather than claiming invalid credentials.
- A signed-in user visiting `/login` should return to the appropriate authenticated landing page.

Required tests cover network failure, genuine 401, server restart/recovery, first visit, and sign-in while the API is unavailable.

## Priority 2 — clinical, scheduling, and follow-up integrity

### F-078/F-086/F-084/F-085/F-111

Use one authoritative safety-summary projection and invalidate/recompute it on every relevant clinical-record, medication, allergy, alert, clearance, and review-state change. Confirm and correct duplicate-allergy behavior and medication visibility from finalized encounter workflows.

### F-103 — finalized encounters accepting completed procedures

Finalized encounters must not accept new consequential clinical care through a normal edit path. Define the valid route as a documented addendum/amendment workflow, preserving the original finalized record and audit history. Test direct API and UI paths.

### F-105 — penicillin allergy warning

Implement only the bounded correction first: clinically owned allergy-class matching for supported, normalized entries, with a visible unknown/not-checkable state. Do not ship a broader drug-interaction or prescribing-recommendation engine under this finding.

The proposed RxNav interaction path is not viable: NLM discontinued RxNav drug-drug interaction features on 2024-01-02. A separate future design is required for interaction data, governance, update rights, offline behavior, clinical ownership, and failure semantics. No patient data may leave the product as part of an early lookup.

### F-053/F-061/F-071

Enforce overlap and state rules server-side for blocked time, provider/operatory changes, and future appointments. Return the conflicting records in a privacy-appropriate form. A future appointment cannot become completed.

### F-185/F-188/F-221

Treat this as release-blocking:

- A blank date cannot create a scheduled state.
- A planned date and a booked appointment are separate concepts.
- Only a real linked appointment can close an unscheduled-treatment follow-up.
- Removing a planned/scheduled state must restore the correct prior state, create history, and be auditable.
- A past planned date needs at least an explicit confirmation and clear overdue wording; prefer refusal when it has no legitimate clinical use.

## Priority 3 — billing, documents, audit, and operational readiness

### F-114/F-116/F-175/F-209

- Receipt and statement issuance must be idempotent. Reissue must be an explicit replacement operation with a reason and a link to the original.
- Preserve an accurate balance while making statement and aging reconciliation understandable. Do not force account-level adjustments into a charge-age bucket; show them as a separately explained amount.
- Payer is independent of responsible party. Allow the registered guarantor and eligible household members as payer choices where policy permits.

### F-038/F-039 and F-029/F-149/F-172/F-192/F-214

Assess patient-chart access event logging as its own compliance/audit story. Do not add indiscriminate page-view logging without defining purpose, event shape, retention, reader permissions, and noise controls.

Correct audit readability: resolve actor names safely, retain immutable identifiers, show meaningful history, and distinguish handled business refusals from genuine faults. For SQL failures, record method/path and SQL number, never unsafe detail or clinical data.

### F-110/F-164

Prescription output must meet explicit product/regulatory requirements before release. Determine the jurisdictional requirements separately; do not guess an NPI or licence obligation from generic examples. A cancelled/discontinued prescription must never print as an apparently active dispense instruction. If historical printing remains needed, clearly watermark its status.

### F-147/F-152/F-153/F-154

- Correct Import UI wording: the current export package is documented as non-importable. Do not imply a round trip that does not exist.
- Backup failure/restore status needs an operator-visible, authorized channel and clear statement of which channel is tested. File-only alerts are insufficient operational evidence.
- Include generated recovery-key extensions in the restore picker.
- Give slow backup/restore actions a shared busy, disabled, idempotency-safe state and durable result feedback.

### F-001/F-005/F-002/F-003

Investigate before changing:

- Identify the exact dependency advisories, affected dependency paths, reachability, and safe fixed versions. Do not run blind bulk upgrades.
- Inspect nullable warnings with empty and malformed import-row tests.
- Inspect all raw SQL interpolations; parameterize or document a safe server-generated-value boundary. Any user-controlled SQL interpolation is a stop-and-fix security defect.

## Priority 4 — planned usability and product improvements

These are worthwhile but must follow the integrity work or be planned as independent, bounded stories:

- One staff-account creation/onboarding route for administrators (F-016).
- A real dashboard, replacing story-shell wording (F-008).
- Context-aware patient selection bar, with no unrelated patient safety panel on report/admin pages and no selected-patient context over registration (F-034/F-035/F-045).
- A clear patient-workspace appointments view and linked rebook/reschedule flow (F-083/F-051/F-073).
- Consistent tab, theme, form, modal/side-panel, feedback, and responsive-table behavior (D-02/D-07/D-08 and listed visual findings).
- One complete prescription workflow instead of the legacy quick-entry/composer split (D-04). Preserve portal-story contracts and legacy record behavior through a deliberate engineering story.
- Patient number/MRN design (F-146): stable, unique, non-reused, searchable, separate from the GUID, with migration/import/merge rules defined first.
- Menu/data-exchange organization (D-05), billing subtabs (D-09), and guarantor account summaries (D-10) with least-privilege access.

## Closure decisions

- F-006 may close as a one-off Vite optimization message, based on the later non-repetition evidence.
- F-044 may close only if its evidence confirms the duration was in fact prefilled.
- F-199 is a duplicate of F-067; F-201 is withdrawn.
- F-170 is an intentional delegated-permission model. Verify boundaries and auditing; do not remove it solely because an office manager successfully used a delegated grant.
- F-129 is development configuration cleanup. Production HTTPS behavior still requires separate release-engineering verification.
- F-152, F-001, F-005, F-002, and F-003 remain open pending the investigations above.

## Automated accumulated-state E2E proposal

This is the intended repeatable replacement for rerunning the week-long manual E2E pass after every stabilization cycle. Build the ordered automated chain from the supplied manual test plan and step-by-step guide, preserving their business sequence, role changes, data handoffs, and expected outcomes.

Create a coverage map from every manual test step to one of:

- an automated chain stage/test and its evidence location;
- a focused automated regression test when that is the more appropriate durable check; or
- an explicitly justified manual-only check, limited to work that cannot be objectively automated, such as visual judgment or subjective workflow assessment.

Do not silently omit a manual step. A changed test step must retain its source-step identifier and explain why its automated equivalent is more reliable than reproducing the original UI click sequence verbatim.

The chain remains a separate tier beside isolated real-backend walkthroughs. Start by implementing the early stages in the same order as the manual pass, but design the full chain so that its completed form covers the entire manual journey. Use one new, throw-away database and matching throw-away document/storage configuration for the complete run; do not reset between stages. Use run-specific immutable identifiers rather than names alone. Preserve database, document storage, configuration, evidence, schema version, implementation SHA, and clock configuration together for any diagnostic resume.

Do not treat a resumed run as evidence of an uninterrupted full run. Known defects may be recorded as explicit tracked exceptions during construction, but they must not turn a release-blocking defect into a green result. Build each stage in order and retain focused API/service tests for each corrected root cause.

## Next deliverable

Return a stabilization plan that proposes discrete correction stories in dependency order, maps every included finding to acceptance criteria and tests, includes the complete manual-step-to-automated-coverage map above, and identifies the small set of product decisions still required before implementation. Do not begin release engineering until that plan is independently reviewed and the execution plan has been updated to authorize the selected stabilization stories.
