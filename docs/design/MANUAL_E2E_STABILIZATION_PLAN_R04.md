# Manual E2E Stabilization Plan - R04

**Date:** 2026-11-09
**Prepared by:** the coding agent, for independent review
**Revision:** R04, responding to `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R03_REVIEW.md` (decision `CHANGES_REQUIRED` on R03, a traceability correction only). R03 is retained unchanged for comparison.
**Source evidence:** the raw manual log (`manual-e2e-findings-2026-10-30.md`, outside git), `01-test-cases.md`, `00-STEP-BY-STEP.md`, the reviewer package
**Status:** PROPOSAL for independent review. Nothing here is implemented. No production code, execution plan, execution status or database has been changed. This plan stays a proposal until the authoritative execution plan and execution status are deliberately updated, after independent approval, to add the approved packages. Deployment work stays paused.

## 0. Release gate and control state

1. **Release engineering stays paused until the owner explicitly says so.** `ALV-N013`, `ALV-N014`, `ALV-N008` and release items 56 to 58 do not start on any other signal.
2. **Sequence (preserved exactly):**
   1. the selected stabilization packages close through two-stage review;
   2. the complete automated accumulated-state chain passes with no release-blocking exception (PKG-16);
   3. selected additional features, if any, are implemented, reviewed and added to automated coverage (PKG-17);
   4. the owner uses the finished product and chooses the timing of one final manual E2E;
   5. the owner completes that manual E2E and gives an explicit clear-to-go; then
   6. release engineering begins.
3. **The final manual E2E is a required, owner-timed, one-time release gate**, not a recurring regression tier and not run after each package. Without an owner-run manual E2E there is no release. The automated chain replaces repeated manual regression during stabilization and routine readiness checks.
4. **Owner features:** the list is not yet supplied and is not guessed. PKG-17 is a future placeholder and does not block stabilization until the owner supplies a concrete, approved list. Known mentions kept out of scope: E-01 (tooth drawings, licence caveat) and a future drug-interaction design (the RxNav interaction path is not viable).
5. **Actual execution-control state (corrected from R02):** `ALV-N007` and `ALV-N012` are already recorded `DEFERRED` in `.alveara/EXECUTION_STATUS.json` under the product-owner decision (commit `0a14b66`, "Control: defer optional AI release branch"). Nothing is pending on them. R02's statement that recording them as deferred still waited was stale and is withdrawn. Only release items 56 to 58 and `ALV-N013`, `ALV-N014`, `ALV-N008` wait for the owner's signal.
6. **Anything this plan leaves out of the package needs the owner's approval.** Every finding, question and observation in the raw log therefore remains listed in Appendices A to C, and every manual step stays in the coverage map in section 6.

## 1. Response to review R02

| R03 review requirement | What R04 does |
|---|---|
| 1. Review every section 6.4 row against the raw log; do not use keyword matching as the final method | All 142 segments of the raw log were re-read and classified by hand (stage, asserting package, test type, what is asserted). The three large segments covering steps 113 to 161 were split into 48 sub-rows, one per manual step. Section 6.4 now has 190 rows. The keyword rule is gone. |
| 2. Reconcile 6.4 with 6.1 to 6.3 and correct every package reference that conflicts with the package table or the stage-to-package table | Section 6.4 rows cite the 6.1 case ids and 6.3 step ids they cover. Wrong package references found in R03 are corrected (section 12 lists each one). The 'Package' column names the package whose acceptance criteria the assertion proves; the stage table names the package that builds the stage. |
| 3. For each manual source step, the concrete automated assertion(s) that prove its outcome | Section 6.1 gives a concrete assertion for all 124 test cases; 6.3 and 6.4 give one for each other step and segment. 'Same business sequence' is no longer used as evidence. |
| 4. Preserve raw source identifiers and the justification where the automated equivalent differs from the click sequence | Every row keeps its source id; the 'Why the mechanism differs' column states each deliberate difference (for example concurrency at API level instead of two browser windows, clock control instead of editing rows in SQL). |
| 5. Keep manual-only work narrow | Manual-only remains: subjective visual/readability judgment (section 6.5) and the owner's single final manual E2E. |
| 6. Keep package scope, order, owner-decision ownership, deferred-AI state and release sequence unchanged unless a corrected entry exposes a real dependency conflict | Nothing in sections 0, 3, 4 or 5 changed. No dependency conflict was exposed (section 12.5). |
| Resubmit as a new immutable revision with a concise change log of every corrected mapping and reference | This is R04; R03 is untouched. Section 12 is the change log. |

## 2. How to read this plan

- Findings keep raw ids (F-nnn, D-nn, Q-5x, O-x). D-01 and D-02 were reused for different topics and are keyed D-01a/b and D-02a/b; F-044b is new; F-123 and F-206 were never assigned.
- Dispositions: CONFIRMED (observed in the manual pass; the first task of each package is to re-verify in code and synthetic data), NOT_REPRODUCED, DUPLICATE, WITHDRAWN, DESIGN_DECISION_REQUIRED. Every finding has exactly one home package.
- A package holds several findings only where they share a service or invariant, migration impact and chain stage. Inside a package the work is ordered in labelled groups; release-blocking groups come first and carry their own acceptance criteria and tests, so nothing is lost in consolidation.
- Product decisions are PD-nn (section 5). Ordinary UX and implementation choices are made in the package design.
- The two-stage review policy applies to every package. No broad redesign is bundled with integrity work. Portal course completion, completed-story evidence and historical audit records are never touched.

## 3. Packages in dependency order

| Package | Pri | Title | Findings | Depends on | Open owner decision before coding |
|---|---|---|---|---|---|
| PKG-01 | P0 | Foundation: backlog normalization, tracked-exception register and chain runner | 1 | none | none |
| PKG-15 | P1 | Dependency advisories and developer-environment hygiene (read-only investigation first) | 9 | none; does not block any other package | PD-26 |
| PKG-02 | P1 | Identity and session integrity: patient-context isolation (first), then network-failure/session/outage behavior | 11 | PKG-01 | PD-01, PD-36 |
| PKG-03 | P1 | Staff/login lifecycle security (F-021) | 1 | PKG-02 | none |
| PKG-04 | P1 | Date-only and time correctness (F-107, F-118) | 7 | PKG-01 | none |
| PKG-05 | P1 | Visit transitions, role enforcement and scheduling integrity | 35 | PKG-03, PKG-04 | PD-05, PD-06 |
| PKG-06 | P2 | Safety and clinical-record integrity, including the allergy-class check | 28 | PKG-02 | PD-09, PD-12 |
| PKG-07 | P2 | Finalized encounters and the addendum route (F-103) | 1 | PKG-06 | PD-10 |
| PKG-08 | P1 | Treatment-plan scheduling, follow-up integrity, state and revisions | 23 | PKG-04, PKG-05 (a real linked appointment relies on its integrity rules), PKG-07 (completion against finalized encounters) | PD-14, PD-15 |
| PKG-09 | P3 | Financial issuance and reconciliation | 23 | PKG-04, PKG-08 (charges come from completion) | PD-17, PD-18 |
| PKG-10 | P3 | Audit readability, observability and patient-access audit design | 18 | PKG-03 | PD-20 |
| PKG-11 | P2 | Patient identity: duplicate detection, import, merge, value lists and patient number | 13 | PKG-10 (override and merge audit) | PD-21 |
| PKG-12 | P3 | Documents, prescription printing and backup/restore operations | 14 | PKG-04, PKG-05, PKG-06, PKG-10 | PD-11, PD-24, PD-25 |
| PKG-13 | P3 | Messaging, follow-ups, notifications and reports | 15 | PKG-08 (follow-up closing rule), PKG-09 (report reconciliation), PKG-10 | none |
| PKG-14 | P4 | Navigation, visual consistency, practice configuration, onboarding, menu visibility and date-format preference | 36 | all P1 to P3 packages; the date-format preference also needs PKG-04 | PD-04, PD-31, PD-34, PD-37 |
| PKG-16 | P1 | Complete chain run and exit evidence | 0 | all selected packages | none |
| PKG-17 | P4 | Owner features (placeholder; list to be supplied) | 0 | none; does not delay any other package | PD-02 |

**Dependency review notes.** PKG-04 (date correctness) and PKG-03 (login lifecycle) come before PKG-05 because scheduling and visit rules use real roles and dates. PKG-08 does not certify a scheduling or follow-up invariant until PKG-05 and PKG-07 have closed. PKG-09 depends on PKG-08 because charges come from completion, and PKG-13's follow-up and report checks depend on PKG-08 and PKG-09. A package may use current behavior as test data, but it must not certify an invariant that an earlier open correction can still violate. PKG-15 is read-only first and blocks nothing; PKG-17 delays nothing. F-107 (printed date of birth one day early, not named in either reviewer response) is PKG-04 at P1.

## 4. Package detail

### PKG-01 - Foundation: backlog normalization, tracked-exception register and chain runner (P0)

- **Findings (raw ids):** F-201 [WITHDRAWN]
- **Depends on:** none
- **Chain stages built here (same review cycle):** Stage 0 (runner and environment).
- **Governance / escalation:** Shared test infrastructure and any test-only helper need the owner's approval first.
- **Decisions needed:** none.
- **Existing isolated specs affected:** adds a tier beside the isolated walkthroughs.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Backlog normalization, tracked-exception register and chain runner skeleton** (from R02 story 00; findings: F-201 [WITHDRAWN])
  - Root cause: The raw log has reused keys, duplicates, withdrawn items and corrections; the chain has no runner yet.
  - Verify first: Appendices A to C of this plan are the first version; the reviewer confirms them.
  - Every design note and finding has a unique canonical key (D-01a/b, D-02a/b, F-044b assigned); duplicates link to one canonical item and keep raw ids; withdrawn and not-reproduced items are retained.
  - A script fails if any raw id in the log has no disposition row.
  - A tracked-exception register exists (finding id, story, owner, exit condition) and the runner reads it.
  - The runner (stage 0) owns the throw-away database, document storage, configuration, test-only injected clock, communications stubs, evidence directory, run id, schema version and implementation SHA for the whole run, using the safety rules of scripts/Invoke-RealBackendWalkthrough.ps1; it never touches AlveraDev or Dev-Data.
  - Tests: Disposition-completeness script; runner self-test (creates and drops its own database, refuses a dev database name, stamps SHA and clock).
  - Permission / audit / migration impact: Permission/audit/migration: none (test infrastructure only).

### PKG-15 - Dependency advisories and developer-environment hygiene (read-only investigation first) (P1)

- **Findings (raw ids):** F-001, F-002, F-003, F-005, F-004, F-006 [NOT_REPRODUCED], F-009, F-011, F-129
- **Depends on:** none; does not block any other package
- **Chain stages built here:** none.
- **Governance / escalation:** External dependency changes cross a governance boundary: escalate before upgrading. Optional npm tooling work must not delay any release-blocking story.
- **Decisions needed:** PD-26 (approval to upgrade).
- **Existing isolated specs affected:** bulk-import-real-backend, patient-merge-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Dependency advisory and nullable/raw-SQL investigation (read-only first)** (from R02 story 01; findings: F-001, F-002, F-003, F-005)
  - Root cause: Known-vulnerability warnings, nullable warnings in import code and raw-SQL interpolation were logged and never dispositioned.
  - Verify first: Identify the exact advisories (NU1902/NU1903 for System.Security.Cryptography.Pkcs 9.0.18; five npm advisories), dependency paths, reachability and safe fixed versions. Inspect empty and malformed import rows for the CS8604/CS8714/CS8621 spots. Read all four EF1002 call sites in PatientMergeService and PatientUnmergeService. No blind bulk upgrades.
  - Each advisory is dispositioned (not reachable, dev-only, or fixed) with evidence; any upgrade is a deliberate add after the owner's approval.
  - Raw SQL is parameterized, or a safe server-generated-value boundary is documented; any user-controlled interpolation is a stop-and-fix defect handled at once.
  - Nullable cases are guarded or documented, with tests using empty and malformed import rows.
  - Tests: Stored dependency-audit output as evidence; unit tests for import rows; negative SQL-injection tests on merge/unmerge.
  - Permission / audit / migration impact: Permission/audit/migration: none.

  **Group B: Developer-environment hygiene and guides** (from R02 story 29; findings: F-004, F-006 [NOT_REPRODUCED], F-009, F-011, F-129)
  - Root cause: Developer scripts and docs carry stale details.
  - Verify first: F-006 did not repeat (close). F-129 is development configuration; production HTTPS behavior is separate release-engineering verification.
  - RUNNING_LOCALLY no longer states counts that go stale (F-009); script refusals print the message without a stack block and exit non-zero (F-011).
  - The npm major-version update is planned, not drive-by (F-004); the stale lines in the manual guides are corrected.
  - Tests: Script exit-code test.
  - Permission / audit / migration impact: none.

### PKG-02 - Identity and session integrity: patient-context isolation (first), then network-failure/session/outage behavior (P1)

- **Findings (raw ids):** F-037, F-007, F-012 [NOT_REPRODUCED], F-013 [DESIGN_DECISION_REQUIRED], F-014, F-015 [NOT_REPRODUCED], F-017 [DESIGN_DECISION_REQUIRED], F-151, F-155, F-156, F-166 [DUPLICATE]
- **Depends on:** PKG-01
- **Chain stages built here (same review cycle):** Stage 1 (setup and sign-in) and the minimal part of stage 2 (two patients) are built here.; Stage 1 sign-in checks and the 'stop API, restart' runner step (case 13.05) are built here.
- **Governance / escalation:** Privacy and wrong-patient risk: release-blocking.
- **Decisions needed:** PD-01 (offline meaning). PD-36: optional, separate owner policy choice about session extension; not required by this plan.
- **Existing isolated specs affected:** auth-real-backend, patient-workspace-real-backend.; auth-real-backend, shell/route-guard specs.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Patient context isolation on sign-out, expiry and identity change (F-037)** (from R02 story 02; findings: F-037)
  - Root cause: Selected-patient state, cached patient data and the post-login return destination live in the browser outside the identity that owns them.
  - Verify first: Reproduce with two accounts and two tabs on synthetic data.
  - Selected-patient state, cached patient data and the post-login return destination are cleared on sign-out, expiry and identity change.
  - A successful new sign-in lands on the dashboard, never on the previous user's patient page; data is re-fetched under the new identity.
  - Refresh keeps same-user context only when keyed to the active identity; two tabs behave the same.
  - Tests: Vitest for the context store; Playwright with two accounts and two tabs; API assertion that a wrong-identity patient fetch is not served from cache.
  - Permission / audit / migration impact: Permission: none new. Audit: none here (sign-out events belong to PKG-10). Migration: none.

  **Group B: Network failure, session and outage behavior** (from R02 story 03; findings: F-007, F-012 [NOT_REPRODUCED], F-013 [DESIGN_DECISION_REQUIRED], F-014, F-015 [NOT_REPRODUCED], F-017 [DESIGN_DECISION_REQUIRED], F-151, F-155, F-156, F-166 [DUPLICATE])
  - Root cause: The client treats every failed call, a missing session and a real expiry the same way.
  - Verify first: Re-time F-012 with the API stopped. Confirm in AuthContext (lines ~77-83) and LoginPage (lines ~60-73) that 502/network failures reach the 401 branch. F-015 is NOT_REPRODUCED / INVESTIGATE: it happened around a mid-run runtime update; reproduce with preserved evidence before treating any part of it as a defect. Fixed expiry is an intentional security control and stays as it is; session extension (F-017) is NOT part of this story.
  - A 502/503 or network failure enters a reconnecting state; the authenticated UI and unsaved form content are preserved where safe; only a genuine 401 signs the user out (F-155).
  - With the API down, sign-in says the server is unavailable, never 'Invalid username or password'; a 500 from a live API gets a server-problem message; 429, disabled and locked are unchanged (F-014).
  - A first, never-signed-in visit shows neutral sign-in wording; the expiry notice appears only after a real expiry or a 401 after being signed in, and is hidden while the server is unreachable (F-007, F-012).
  - A signed-in user who opens /login goes to the authenticated landing page.
  - Sign-in normalizes surrounding spaces in the username and the audit stores the normalized name (F-151, F-166); the theme preference applies to the sign-in page after reload (F-156).
  - Wording of the disconnected banner follows PD-01 (F-013).
  - Tests: Vitest for LoginPage and AuthContext (401, 429, disabled, locked, 500, 502, 503, network); route-guard tests updated; Playwright: stop API, reload, sign in, restart, recover.
  - Permission / audit / migration impact: Permission/audit/migration: none.

### PKG-03 - Staff/login lifecycle security (F-021) (P1)

- **Findings (raw ids):** F-021
- **Depends on:** PKG-02
- **Chain stages built here (same review cycle):** Stage 1 staff-profile steps extended here.
- **Governance / escalation:** Security rule: escalation already given by the owner decision; implementation still goes through review.
- **Decisions needed:** Notification level: map 'highest level' to an existing level or add one (story design).
- **Existing isolated specs affected:** auth-real-backend, permission-policy-admin-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Staff deactivation and login lifecycle (F-021)** (from R02 story 04; findings: F-021)
  - Root cause: A staff profile and its linked login are separate records with no rule tying their lifecycles together.
  - Verify first: Reproduce with dentist1/dentist2 on synthetic data; read StaffProviderService (lines ~79-84) and PeopleTabs (lines ~43-51).
  - Deactivating a profile disables its linked login by default and revokes active sessions (owner decision).
  - Reactivating the profile never re-enables the login; only an administrator can re-enable it.
  - Profile deactivation, account disablement, session invalidation and the audit outcome happen without a partial state, in one transaction where they share a boundary; account-link changes and concurrent updates are versioned and safe.
  - A failure returns a clear result and a durable operational record; the highest-urgency notification goes to the administrator and the office manager but is never the only evidence of a failure.
  - An administrator can unlink a login from an inactive profile with a reason; the account list shows the linked profile.
  - Tests: Deactivation, existing-session invalidation, profile reactivation without login reactivation, admin-only re-enable, concurrent change, forced failure of the account step with rollback, notification-failure path.
  - Permission / audit / migration impact: Permission: re-enable stays ManageUsers. Audit: new events for the disable and for the failure. Migration: a version column on the link only if none exists.

### PKG-04 - Date-only and time correctness (F-107, F-118) (P1)

- **Findings (raw ids):** F-030, F-100, F-107, F-118, F-133 [WITHDRAWN], F-184, F-218 [DUPLICATE]
- **Depends on:** PKG-01
- **Chain stages built here (same review cycle):** Stage 0 clock check; date assertions in stages 8 and 9.
- **Governance / escalation:** Patient-identification defect: release-blocking.
- **Decisions needed:** none.
- **Existing isolated specs affected:** prescriptions, billing-statements, treatment-plans and reports specs where dates are asserted.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Date-only and time correctness (F-107, F-118)** (from R02 story 06; findings: F-030, F-100, F-107, F-118, F-133 [WITHDRAWN], F-184, F-218 [DUPLICATE])
  - Root cause: Date-only values travel through UTC conversion, and several recorded times are replaced or defaulted instead of kept.
  - Verify first: Trace every place a date-only value is formatted: print, statements, lists, exports, appointment lists. Reproduce F-107 (printed DOB one day early) and F-118 (performed date one day late). Check where the 8:00:00 times of F-100 come from.
  - A date-only value (DOB, performed date) is never converted through a time zone; regression tests with a zone behind UTC and across the date line.
  - Recorded times of answers, schedules and actions are kept and not replaced by later actions (F-184, F-218); defaulted times are not shown as real click times (F-100).
  - Audit and history keep UTC with the practice-zone display as before.
  - The user-selectable date-format preference is NOT part of this story (see PKG-14).
  - Tests: Unit tests on the shared date formatter (zones, DST, year boundary); API round-trip of DOB and performed date; print test comparing the header DOB; test clock injected through the runner only.
  - Permission / audit / migration impact: Permission/audit/migration: none.

### PKG-05 - Visit transitions, role enforcement and scheduling integrity (P1)

- **Findings (raw ids):** F-056 [DESIGN_DECISION_REQUIRED], F-058, F-059, F-060, F-063, F-064, F-065, F-066, F-170 [WITHDRAWN], D-01a [WITHDRAWN], F-047, F-053, F-061, F-071, F-044 [WITHDRAWN], F-044b [DESIGN_DECISION_REQUIRED], F-045, F-046, F-048, F-049, F-050 [DESIGN_DECISION_REQUIRED], F-051, F-052, F-054, F-055, F-057 [DESIGN_DECISION_REQUIRED], F-062, F-068 [NOT_REPRODUCED], F-069, F-070, F-072, F-073 [DESIGN_DECISION_REQUIRED], F-074, F-076, F-077
- **Depends on:** PKG-03, PKG-04
- **Chain stages built here (same review cycle):** Stage 3 (scheduling).; Stage 3.; Stage 4 (visit flow).
- **Governance / escalation:** Cancel reasons change report inputs: reviewer to confirm. Permission model change: escalate.
- **Decisions needed:** PD-05 (matrix and authority), PD-06 (hygienist allergy authority).; Story-design choices; F-044b is a design decision recorded in the story.
- **Existing isolated specs affected:** calendar-real-backend, schedule-real-backend, visit-board-real-backend.; calendar-real-backend, schedule-real-backend.; visit-board-real-backend, patient-flow-real-backend, calendar-real-backend, permission-policy-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Visit-transition matrix and role enforcement** (from R02 story 05; findings: F-056 [DESIGN_DECISION_REQUIRED], F-058, F-059, F-060, F-063, F-064, F-065, F-066, F-170 [WITHDRAWN], D-01a [WITHDRAWN])
  - Root cause: Visit actions are decided separately in the Calendar panel and on the Visit board, and role rules were never written down as one matrix.
  - Verify first: List the real transitions each caller offers per role; reproduce Q-065a (a hygienist completing another provider's visit). F-170 is an intentional delegated-permission model: verify its boundaries and auditing, do not remove it.
  - A written matrix names every allowed state transition, its required role/permission, whether intermediate states are optional, and who performs final completion and check-out.
  - The service/API enforces the matrix; Calendar, Visit board and any other caller show exactly the permitted actions.
  - Every transition records actor and transition in history; no role has a dead end in the chain.
  - Hygienist authority to add, retire or mark allergies/medications in error is re-evaluated under the same review (PD-06).
  - A room or provider change on the board is decided against the booking (F-060).
  - Tests: API tests for every allowed and forbidden transition per role (403 matrix); UI parity test between Calendar and Board; Playwright visit chain per role; delegated-grant boundary and audit test (F-170).
  - Permission / audit / migration impact: Permission: possible new grants; Permission Matrix defaults and policy-health updated. Audit: actor on every transition. Migration: none expected.

  **Group B: Scheduling overlap and state integrity** (from R02 story 14; findings: F-047, F-053, F-061, F-071)
  - Root cause: Rules are enforced when booking but not on blocked time, room/provider change and completion.
  - Verify first: Reproduce F-053, F-061 and F-071 against synthetic data.
  - Overlap and state rules are enforced server-side for blocked time, provider/operatory changes and future appointments; the response lists the conflicting records in a privacy-appropriate form.
  - A future appointment cannot become completed; a completed appointment does not keep holding a slot.
  - Check-in is offered only on the appointment day or asks for confirmation (F-047).
  - Tests: API tests for each refusal with the conflicting-record payload; concurrency test for the booking race (case 3.10).
  - Permission / audit / migration impact: Permission: none. Audit: unchanged. Migration: none expected.

  **Group C: Appointment history, drawing and usability** (from R02 story 15; findings: F-044 [WITHDRAWN], F-044b [DESIGN_DECISION_REQUIRED], F-045, F-046, F-048, F-049, F-050 [DESIGN_DECISION_REQUIRED], F-051, F-052, F-054, F-055, F-057 [DESIGN_DECISION_REQUIRED], F-062, F-068 [NOT_REPRODUCED], F-069, F-070, F-072, F-073 [DESIGN_DECISION_REQUIRED], F-074, F-076, F-077)
  - Root cause: History rows lack actor and the new value, and several views omit non-working or blocked time.
  - Verify first: Re-check F-068 in Brave/Chrome. F-044 stays withdrawn as a prefill defect; the silent 60-minute default is the separate key F-044b.
  - Appointment history shows who acted and from-to values (F-046, F-049, F-062, F-074).
  - Blocked time and non-working hours are drawn on Week as well as Day view; a closed day says so (F-048, F-052, F-054).
  - Cancel reason has categories; cancelled and no-show appointments offer Rebook/Reschedule and an Open patient link; undo of check-in and no-show correction are decided (F-050, F-051, F-057, F-073).
  - The silent 60-minute default is decided and its wording clear (F-044b); status and wording items F-045, F-055, F-068-F-070, F-072, F-076, F-077 are corrected.
  - Tests: UI and API tests per item.
  - Permission / audit / migration impact: Permission: none. Audit: history content. Migration: cancel-reason column if approved.

### PKG-06 - Safety and clinical-record integrity, including the allergy-class check (P2)

- **Findings (raw ids):** F-078, F-080, F-081, F-084, F-085, F-086, F-111, F-112, F-067, F-079, F-082, F-088 [DESIGN_DECISION_REQUIRED], F-089, F-090, F-091 [DESIGN_DECISION_REQUIRED], F-092, F-093, F-094, F-095, F-097, F-098, F-099, F-104 [DUPLICATE], F-124 [DUPLICATE], F-137, F-199 [DUPLICATE], F-105, F-106
- **Depends on:** PKG-02
- **Chain stages built here (same review cycle):** Stage 5 (clinical).; Stage 6 (charting).; Stage 8 (prescriptions).
- **Governance / escalation:** Clinical policy decision for F-091 split out as its own decision. Clinical rule needs sign-off (PD-09). The RxNav interaction path is not viable (discontinued 2024-01-02); any interaction feature is a separate design.
- **Decisions needed:** PD-09.; PD-09.; PD-12.
- **Existing isolated specs affected:** clinical-real-backend, clinical-companion-real-backend, safety-real-backend.; odontogram-real-backend, odontogram-longitudinal-real-backend, perio-real-backend, perio-sessions-real-backend, diagnoses-real-backend, diagnoses-structure-real-backend, clinical-real-backend.; prescriptions-real-backend, prescription-workflow-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Safety-summary freshness and duplicate/medication integrity** (from R02 story 07; findings: F-078, F-080, F-081, F-084, F-085, F-086, F-111, F-112)
  - Root cause: The safety banner is computed separately from the data the pages change.
  - Verify first: Confirm F-084/F-085/F-111 in code and data (duplicate allergy accepted; medication from a finalized encounter missing on the Safety tab).
  - One authoritative safety-summary projection is recomputed or invalidated on every clinical-record, medication, allergy, alert, clearance and review-state change; the banner never contradicts the page body or the chart (F-078, F-086, F-081).
  - Duplicate-allergy behavior is decided and tested; medications from any encounter state reach the Safety tab (F-084, F-085, F-111).
  - A 'reviewed - none reported' entry cannot contradict an active allergy, or the contradiction is shown (F-080, F-112; PD-09).
  - Tests: Projection unit tests for every mutation path; UI tests for banner refresh after each save without reload; API tests for refusals.
  - Permission / audit / migration impact: Permission: none. Audit: unchanged. Migration: none expected.

  **Group B: Clinical record references, chart display and clinical-policy items** (from R02 story 08; findings: F-067, F-079, F-082, F-088 [DESIGN_DECISION_REQUIRED], F-089, F-090, F-091 [DESIGN_DECISION_REQUIRED], F-092, F-093, F-094, F-095, F-097, F-098, F-099, F-104 [DUPLICATE], F-124 [DUPLICATE], F-137, F-199 [DUPLICATE])
  - Root cause: Free-text fields stand in for references, and the chart mixes numbering systems and labels.
  - Verify first: F-091: the reviewer notes Existing/Completed missing teeth do refuse conflicting findings; only Diagnosed/Planned coexistence needs a defined meaning. F-104 is display numbering only; never change stored tooth identity.
  - The meaning of a Diagnosed/Planned missing tooth is documented; if coexistence is intended a warning is shown (F-091; its own clinical-policy decision).
  - Alert source is required or shown as 'not recorded' (F-089); clearance document reference becomes a picker from the patient's Documents (F-090); the periodontal linked-record reference becomes a picker or is validated (F-098).
  - Numbering systems are labelled and consistent on one screen (F-099, F-104); sex is displayed consistently (F-067, F-124); grammar fixed (F-199).
  - Empty drafts can be discarded (F-088); the odontogram Plan/Complete behavior is explained or connected (F-095); small items F-079, F-082, F-092, F-093, F-094, F-097, F-137 are corrected.
  - Tests: API and UI tests per item; Playwright charting journeys.
  - Permission / audit / migration impact: Permission: none. Audit: unchanged. Migration: none.

  **Group C: Allergy-class check (F-105, F-106)** (from R02 story 10; findings: F-105, F-106)
  - Root cause: Allergy matching is a name comparison only, and the prescription list does not show status.
  - Verify first: Read the allergy-check code; list the classes and normalized entries it can support.
  - Clinically owned allergy-class matching for supported, normalized entries, with a visible unknown/not-checkable state; no drug-interaction or recommendation engine under this finding.
  - No patient data leaves the product for any lookup.
  - The Prescriptions tab shows status, instructions and quantity; a superseded version is visibly superseded (F-106).
  - Tests: Unit tests for class matching including unknown; API test that not-checkable is returned; UI status test.
  - Permission / audit / migration impact: Permission: none. Audit: unchanged. Migration: allergy-class seed (clinically owned).

### PKG-07 - Finalized encounters and the addendum route (F-103) (P2)

- **Findings (raw ids):** F-103
- **Depends on:** PKG-06
- **Chain stages built here (same review cycle):** Stage 5/7 steps.
- **Governance / escalation:** New clinical workflow: design approval needed.
- **Decisions needed:** PD-10.
- **Existing isolated specs affected:** completion-real-backend, completion-adoption-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Finalized encounters and the addendum route (F-103)** (from R02 story 09; findings: F-103)
  - Root cause: The completion form lists finalized encounters as valid targets and records new care against a signed note.
  - Verify first: Reproduce on both finalized encounters through the UI and the direct API.
  - A finalized encounter does not accept new consequential clinical care through a normal edit path, UI or API.
  - The valid route is a documented addendum/amendment workflow that preserves the original finalized record and audit history.
  - The completion form lists only encounters that can accept care.
  - Tests: API tests refusing completion on a finalized encounter; UI picker test; addendum happy path and audit test.
  - Permission / audit / migration impact: Permission: addendum permission to be named. Audit: addendum events. Migration: depends on the design.

### PKG-08 - Treatment-plan scheduling, follow-up integrity, state and revisions (P1)

- **Findings (raw ids):** F-185, F-188 [DUPLICATE], F-221, F-101 [DESIGN_DECISION_REQUIRED], F-102, F-177, F-178, F-179, F-180, F-181, F-182, F-183, F-186, F-187, F-189, F-190, F-191, F-192, F-219, F-220 [DUPLICATE], F-222, D-03 [DESIGN_DECISION_REQUIRED], D-11 [DESIGN_DECISION_REQUIRED]
- **Depends on:** PKG-04, PKG-05 (a real linked appointment relies on its integrity rules), PKG-07 (completion against finalized encounters)
- **Chain stages built here (same review cycle):** Stage 7 (treatment plans), built here.; Stage 7.
- **Governance / escalation:** State-model change: escalate if schema is touched.
- **Decisions needed:** PD-15 (past planned dates) - required before this package's coding starts.; PD-14.
- **Existing isolated specs affected:** treatment-plans-real-backend, treatment-plans-complete-real-backend, follow-ups-real-backend.; treatment-plans-real-backend, treatment-plans-complete-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Treatment-plan scheduling and follow-up integrity (release-blocking block)** (from R02 story 12; findings: F-185, F-188 [DUPLICATE], F-221)
  - Root cause: A planned date, a booked appointment and a plan state are not separate concepts.
  - Verify first: Reproduce the blank-date Schedule, take-off-the-schedule restoring state, and a follow-up closing without a real appointment.
  - A blank date cannot create a scheduled state; a planned date and a booked appointment are separate concepts.
  - Only a real linked appointment can close an unscheduled-treatment follow-up.
  - Removing a planned/scheduled state restores the correct prior state, creates history, and is audited.
  - A past planned date follows the owner's decision PD-15 (refuse, allow with confirmation and overdue wording, or another rule). Until the owner decides, current behavior is not changed.
  - Tests: API tests per transition and restore; follow-up closing with and without a real appointment; Playwright plan journey.
  - Permission / audit / migration impact: Permission: none. Audit: richer plan events. Migration: state columns only if required.

  **Group B: Treatment-plan state, revisions and display** (from R02 story 13; findings: F-101 [DESIGN_DECISION_REQUIRED], F-102, F-177, F-178, F-179, F-180, F-181, F-182, F-183, F-186, F-187, F-189, F-190, F-191, F-192, F-219, F-220 [DUPLICATE], F-222, D-03 [DESIGN_DECISION_REQUIRED], D-11 [DESIGN_DECISION_REQUIRED])
  - Root cause: Plan-level state does not follow items; some changes create no revision or history.
  - Verify first: Check whether answer changes and the code change are or are not revisions (F-181, F-190, F-219).
  - Plan-level state follows its items (in progress/completed) or the label changes; a completed plan's actions are decided (F-101, F-177, D-11; PD-14).
  - Empty-plan actions are hidden (F-178, F-187, F-189, F-222, F-180).
  - Revision history consistently shows answers, code change and rename (F-181, F-190, F-219); audit rows name patient, plan and code (F-192).
  - A completed procedure links back to its account entry (F-102); stale banners removed (F-179, F-220); display items F-182, F-183, F-186, F-191, D-03.
  - Tests: API and UI tests per item; Playwright plan journeys.
  - Permission / audit / migration impact: Permission: none. Audit: richer plan events. Migration: possible.

### PKG-09 - Financial issuance and reconciliation (P3)

- **Findings (raw ids):** F-113, F-114, F-115, F-117, F-119, F-120, F-121, F-122, F-207, F-116, F-175 [DUPLICATE], F-193, F-195, F-196, F-197, F-198, F-203, F-204, F-205, F-208, F-209, D-09 [DESIGN_DECISION_REQUIRED], D-10 [DESIGN_DECISION_REQUIRED]
- **Depends on:** PKG-04, PKG-08 (charges come from completion)
- **Chain stages built here (same review cycle):** Stage 9 (billing), built here.; Stage 9.
- **Governance / escalation:** Financial. Financial: unit + integration + E2E required.
- **Decisions needed:** PD-17 (receipt/statement reissue) - required before this package's coding starts.; PD-18 (payer eligibility) - required before this part of the package starts.
- **Existing isolated specs affected:** billing-real-backend, billing-ledger-real-backend.; billing-statements-real-backend, billing-ledger-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Receipt and statement idempotency** (from R02 story 16; findings: F-113, F-114, F-115, F-117, F-119, F-120, F-121, F-122, F-207)
  - Root cause: Receipts and statements are not idempotent.
  - Verify first: Reproduce F-114: a second preview issues a second receipt for one payment.
  - Receipt and statement issuance is idempotent: the same payment cannot silently produce a second receipt. Whether a reissue is allowed, the reason required, replacement numbering and the original/replacement presentation follow PD-17.
  - A reversed payment's receipt is marked; statement and CSV carry patient, practice, statement number and as-of time; reversed entries are marked (F-115, F-117, F-119).
  - Preview placement, stale banners and message wording issues F-113, F-120, F-121, F-122, F-207 are corrected.
  - Tests: API idempotency tests (double issue, replacement with reason); statement content tests; Playwright billing and receipt journeys.
  - Permission / audit / migration impact: Permission: unchanged. Audit: replacement audited. Migration: replacement link.

  **Group B: Aging/balance reconciliation and payer selection** (from R02 story 17; findings: F-116, F-175 [DUPLICATE], F-193, F-195, F-196, F-197, F-198, F-203, F-204, F-205, F-208, F-209, D-09 [DESIGN_DECISION_REQUIRED], D-10 [DESIGN_DECISION_REQUIRED])
  - Root cause: The aging view omits account-level adjustments, and the payer is tied to the responsible party.
  - Verify first: Reproduce F-116/F-175: the gap equals the account-level adjustments.
  - The balance stays exact; adjustments are shown as a separately explained amount, not forced into a charge-age bucket.
  - Payer is independent of the responsible party; which household/guarantor relationships may be selected as payer, and under which permissions, follows PD-18; the default follows the responsible party (F-209, F-205).
  - Recording the same responsibility again is refused or explained (F-197); messages name the people (F-204); money format and wording are consistent (F-198, F-208, F-196); small items F-193, F-195, F-203 placed.
  - Billing sub-tabs and the guarantor account view (D-09, D-10) are delivered later in PKG-14 with least-privilege access.
  - Tests: Reconciliation property test (sum of parts equals balance); payer-choice tests; Playwright billing journeys.
  - Permission / audit / migration impact: Permission: unchanged. Audit: unchanged. Migration: none expected.

### PKG-10 - Audit readability, observability and patient-access audit design (P3)

- **Findings (raw ids):** F-010 [DUPLICATE], F-029, F-036, F-040, F-041, F-087, F-126, F-127 [NOT_REPRODUCED], F-128, F-138, F-149, F-150, F-172, F-174, F-212, F-214, F-038 [DESIGN_DECISION_REQUIRED], F-039 [DESIGN_DECISION_REQUIRED]
- **Depends on:** PKG-03
- **Chain stages built here (same review cycle):** Stage 13 (permissions and audit), built here.; Stage 13 once approved.
- **Governance / escalation:** Audit design. Compliance design: escalate.
- **Decisions needed:** PD-20.
- **Existing isolated specs affected:** all specs that read the audit log.; n/a until approved.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Audit readability and observability** (from R02 story 18; findings: F-010 [DUPLICATE], F-029, F-036, F-040, F-041, F-087, F-126, F-127 [NOT_REPRODUCED], F-128, F-138, F-149, F-150, F-172, F-174, F-212, F-214)
  - Root cause: Audit rows store actor ids and thin details, and the exception middleware logs only the exception type.
  - Verify first: Read SafeExceptionHandlingMiddleware; classify the TaskCanceled and SqlException cases.
  - Actor names resolved safely with immutable ids retained; meaningful history with before/after values for demographic edits (F-029, F-041, F-149, F-172, F-214, F-087).
  - Handled business refusals are distinguished from faults; abandoned requests are not logged as errors; for SQL failures record method, path and SQL number, never message or clinical data (F-010, F-126, F-127).
  - An override of a duplicate warning records the matched patient and a reason (F-036); audit page text matches what it holds (F-150, F-174); measurement events with an invalid category no longer fail silently (F-128); log lines carry a timestamp (F-212); document history names the person (F-138).
  - Tests: Middleware classification unit tests; audit-content tests per event; contract test that audit rows hold no patient content.
  - Permission / audit / migration impact: Permission: none. Audit: richer. Migration: none.

  **Group B: Patient-access audit design (F-038, F-039)** (from R02 story 19; findings: F-038 [DESIGN_DECISION_REQUIRED], F-039 [DESIGN_DECISION_REQUIRED])
  - Root cause: Patient-chart reads, sign-out and expiry are not recorded; whether and how to record them is a compliance design.
  - Verify first: Define purpose, event shape, readers, retention and noise controls before any logging is added.
  - A design document is approved first (purpose, readers, retention, noise controls); sign-out/expiry events follow only after.
  - No indiscriminate page-view logging is added.
  - Tests: Defined by the approved design.
  - Permission / audit / migration impact: Reader permission and retention are new policy.

### PKG-11 - Patient identity: duplicate detection, import, merge, value lists and patient number (P2)

- **Findings (raw ids):** F-145, F-148, F-210, F-211 [DUPLICATE], D-02a [DESIGN_DECISION_REQUIRED], F-032, F-033, F-042, F-043, F-144 [DESIGN_DECISION_REQUIRED], F-146 [DESIGN_DECISION_REQUIRED], F-147, F-202
- **Depends on:** PKG-10 (override and merge audit)
- **Chain stages built here (same review cycle):** Stage 2 (registration and import), built here.; Stage 2.
- **Governance / escalation:** MRN is a data-model decision: escalate.
- **Decisions needed:** Thresholds are decided in story design.; PD-21.
- **Existing isolated specs affected:** patient-portability-real-backend, patient-export-package-real-backend.; patient-registration-real-backend, bulk-import-real-backend, patient-merge-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Duplicate detection and override rules** (from R02 story 20; findings: F-145, F-148, F-210, F-211 [DUPLICATE], D-02a [DESIGN_DECISION_REQUIRED])
  - Root cause: Duplicate detection uses exact date of birth only.
  - Verify first: Reproduce F-145/F-210 with the manual fixtures.
  - A lower-confidence rule catches same normalized name with a matching phone, email or address and a different DOB, in registration, import and the Duplicate patients page.
  - Not-a-duplicate asks for a reason like merge (F-148); the guarantor picker shows enough to tell two patients apart (F-211); the override path for a near match is settled (D-02a).
  - Tests: Matcher unit tests with fixtures from the manual data; import idempotency; Playwright registration journey.
  - Permission / audit / migration impact: Permission: unchanged. Audit: override reasons. Migration: none expected.

  **Group B: Import wording, merge, value lists and patient number** (from R02 story 21; findings: F-032, F-033, F-042, F-043, F-144 [DESIGN_DECISION_REQUIRED], F-146 [DESIGN_DECISION_REQUIRED], F-147, F-202)
  - Root cause: Import and merge screens mix wording and controls; the patient has no human-readable number.
  - Verify first: Confirm what the package manifest handling does today (F-147).
  - Import wording no longer implies a round trip that does not exist (F-147); the Show filter allows multi-select (F-144).
  - Sex is a list with an unspecified value; State is a list for USA with the 2-letter code stored (F-032, F-033).
  - Patient number/MRN design is approved first (stable, unique, non-reused, searchable, not the GUID, with migration/import/merge rules) before any implementation (F-146).
  - Household-of-one and inactivate-patient dialog behavior are reviewed (F-202, F-042, F-043).
  - Tests: Import tests; value-list tests; MRN tests after design.
  - Permission / audit / migration impact: Migration: MRN column and backfill after approval.

### PKG-12 - Documents, prescription printing and backup/restore operations (P3)

- **Findings (raw ids):** F-125, F-130, F-131, F-171, F-173, F-108, F-109, F-110 [DESIGN_DECISION_REQUIRED], F-164 [DESIGN_DECISION_REQUIRED], F-165, F-152, F-153, F-154, D-06 [DESIGN_DECISION_REQUIRED]
- **Depends on:** PKG-04, PKG-05, PKG-06, PKG-10
- **Chain stages built here (same review cycle):** Stage 10 (documents and storage), built here.; Stage 14 (backup and restore), built here.; Stage 8.
- **Governance / escalation:** Escalate only if a new channel is added. Permission model change: escalate with PKG-05. Regulatory content.
- **Decisions needed:** PD-24.; PD-11.; PD-25 (backup alert delivery) - required before this part of the package starts.
- **Existing isolated specs affected:** backup-related specs.; documents-real-backend, documents-library-real-backend.; prescription-workflow-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Documents, storage and clinical-document access** (from R02 story 22; findings: F-125, F-130, F-131, F-171, F-173)
  - Root cause: Document actions are shown regardless of file state or the viewer's clinical permission.
  - Verify first: Confirm Q-5cn and F-173 in code.
  - A missing file offers no Open/Download and a friendly failure page (F-130, F-131).
  - Edit/Inactivate rights follow the viewer's rights, not a single import grant (F-173); clinical documents follow the clinical-documentation permission (Q-5cn).
  - Empty filter results say so (F-125); the actions column is tidy (F-171); the orphan-file report counts are explained (Q-5cx).
  - Tests: API tests per role; UI missing-file test; storage report test.
  - Permission / audit / migration impact: Permission: possible rule change.

  **Group B: Prescription output requirements** (from R02 story 11; findings: F-108, F-109, F-110 [DESIGN_DECISION_REQUIRED], F-164 [DESIGN_DECISION_REQUIRED], F-165)
  - Root cause: Printing has no print stylesheet, status handling or researched content requirements.
  - Verify first: Jurisdictional prescription requirements (F-110) are researched separately; do not guess NPI or licence obligations.
  - A cancelled or discontinued prescription never prints as an apparently active dispense instruction; historical printing, if kept, carries a clear status mark with date and reason (F-164).
  - Printing produces one sheet without browser header/footer content that exposes the URL or internal id (F-108, F-109); quantity wording is correct (F-165).
  - Signature, prescriber identification and patient address follow the researched requirements (F-110, PD-11).
  - Tests: Print-stylesheet and status-mark tests; Playwright prescription workflow.
  - Permission / audit / migration impact: Permission/audit: unchanged. Migration: none.

  **Group C: Backup, restore and operational status** (from R02 story 23; findings: F-152, F-153, F-154, D-06 [DESIGN_DECISION_REQUIRED])
  - Root cause: Backup alerts are written only to a folder; the restore picker hides the generated key; slow actions give no feedback.
  - Verify first: Confirm the alert channel in code.
  - Backup failure and restore status reach an operator-visible channel chosen under PD-25, with defined failure behavior (what happens when delivery itself fails); the page states which channel was tested; file-only alerts are insufficient evidence (F-152).
  - The restore picker includes the generated recovery-key extensions (F-153); slow actions have a shared busy, disabled, idempotency-safe state and durable result feedback (F-154).
  - System Status shows backup/recovery, storage and background-job health for administrators (D-06).
  - Tests: Status unit/API tests; UI busy-state test.
  - Permission / audit / migration impact: Permission: backup alert reader.

### PKG-13 - Messaging, follow-ups, notifications and reports (P3)

- **Findings (raw ids):** F-132, F-134, F-135, F-136, F-139, F-140, F-141, F-142 [DESIGN_DECISION_REQUIRED], F-143, F-167, F-168, F-169 [DESIGN_DECISION_REQUIRED], F-157 [NOT_REPRODUCED], F-216, F-217
- **Depends on:** PKG-08 (follow-up closing rule), PKG-09 (report reconciliation), PKG-10
- **Chain stages built here (same review cycle):** Stage 11 (follow-ups, referrals, notifications), built here.; Stage 12 (reports and measurement), built here.
- **Governance / escalation:** none.
- **Decisions needed:** Story-design choices.
- **Existing isolated specs affected:** notifications-real-backend, follow-ups-real-backend, work-queue-real-backend, external-cases-real-backend.; reports-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Follow-ups, messaging, notifications and referrals** (from R02 story 24; findings: F-132, F-134, F-135, F-136, F-139, F-140, F-141, F-142 [DESIGN_DECISION_REQUIRED], F-143, F-167, F-168, F-169 [DESIGN_DECISION_REQUIRED])
  - Root cause: Lists and forms were built for correctness but not for the conversation, history or timing the user expects.
  - Verify first: Confirm the polling interval behind F-134 and the follow-up re-notify period (Q-5bx).
  - Own actions refresh counts and banners immediately (F-134); recall changes appear in the Follow-ups timeline and clear stale reasons (F-135, F-136).
  - Reply pre-fills the sender, the thread reads as a conversation, the Sent list shows who read it (F-140, F-141, F-142); upload batches name the uploader (F-143).
  - Hide has a Show-hidden view or a clear explanation (F-169); notice styling follows the theme (F-139, F-167, F-168); system follow-ups are findable (F-132).
  - Tests: UI refresh-after-action tests; read-receipt API test; Playwright messaging journey.
  - Permission / audit / migration impact: Permission: none. Audit: recall changes audited.

  **Group B: Reports and measurement** (from R02 story 25; findings: F-157 [NOT_REPRODUCED], F-216, F-217)
  - Root cause: Baselines can be duplicated and not removed; a timing observation was not re-checked.
  - Verify first: Re-measure F-157; check what the denominators count (Q-5ce).
  - Saving the same baseline twice is refused or idempotent (F-216); the overlap message is readable (F-217).
  - Report totals reconcile with the transactions that produced them (case 12.02); permission-call latency has a budget (F-157).
  - Tests: Baseline idempotency test; reconciliation tests; latency budget.
  - Permission / audit / migration impact: none.

### PKG-14 - Navigation, visual consistency, practice configuration, onboarding, menu visibility and date-format preference (P4)

- **Findings (raw ids):** F-016 [DESIGN_DECISION_REQUIRED], F-018, F-022, F-031, F-158, F-159, F-008, F-034 [DESIGN_DECISION_REQUIRED], F-035, F-075, F-083, F-096, F-160, F-161, F-162 [NOT_REPRODUCED], F-163 [NOT_REPRODUCED], F-176, F-194, F-200, F-213, F-215, D-01b [DESIGN_DECISION_REQUIRED], D-02b [DESIGN_DECISION_REQUIRED], D-04 [DESIGN_DECISION_REQUIRED], D-05 [DESIGN_DECISION_REQUIRED], D-07 [DESIGN_DECISION_REQUIRED], D-08 [DESIGN_DECISION_REQUIRED], E-01 [DESIGN_DECISION_REQUIRED], F-019, F-020, F-023 [DESIGN_DECISION_REQUIRED], F-024 [DESIGN_DECISION_REQUIRED], F-025, F-026 [DESIGN_DECISION_REQUIRED], F-027, F-028
- **Depends on:** all P1 to P3 packages; the date-format preference also needs PKG-04
- **Chain stages built here (same review cycle):** Assertions in stages 8 and 9.; Assertions only.; Stage 1 extension.; Stage 13.
- **Governance / escalation:** Large redesign: one pattern at a time. Security posture: escalate. Touches every screen: reviewer to confirm scope.
- **Decisions needed:** PD-04.; PD-34; dashboard, tab structure and panel choice are decided inside the story design.; PD-31.; PD-37 (options and default).
- **Existing isolated specs affected:** all specs asserting dates.; all specs that click tab names; shell.spec.ts (asserts the old dashboard text).; auth-real-backend.; practice configuration specs.; reports-real-backend.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Administrator account onboarding and user-details usability** (from R02 story 26; findings: F-016 [DESIGN_DECISION_REQUIRED], F-018)
  - Root cause: Accounts can only be created through an undocumented API call; the user details page has no way back.
  - Verify first: Read the register endpoint and its rate limit.
  - An administrator can add a staff account in-app (name, temporary password, role, enabled, forced change at first sign-in); the open register endpoint keeps its rate limit and gets an off switch, per the approved onboarding model.
  - The user details page has a way back to the list and content that earns the page.
  - Tests: API and UI tests; Playwright admin journey.
  - Permission / audit / migration impact: Permission: onboarding permission. Audit: new events.

  **Group B: Menu visibility and report filter usability** (from R02 story 28; findings: F-022, F-031, F-158, F-159)
  - Root cause: Developer pages are shown to every role, and the report filter fails quietly for a permitted role.
  - Verify first: Check what System Status shows a front-desk user.
  - Developer pages (Component Showcase) are hidden outside development; System Status content is reduced for non-admins (F-022, F-159, O-G1b).
  - The schedule report filter loads providers or says why not (F-158); the menu shown to each role matches the matrix (F-031).
  - Tests: Menu-per-role test; report filter test.
  - Permission / audit / migration impact: Permission: menu follows matrix.

  **Group C: Navigation, layout, wording and visual consistency** (from R02 story 30; findings: F-008, F-034 [DESIGN_DECISION_REQUIRED], F-035, F-075, F-083, F-096, F-160, F-161, F-162 [NOT_REPRODUCED], F-163 [NOT_REPRODUCED], F-176, F-194, F-200, F-213, F-215, D-01b [DESIGN_DECISION_REQUIRED], D-02b [DESIGN_DECISION_REQUIRED], D-04 [DESIGN_DECISION_REQUIRED], D-05 [DESIGN_DECISION_REQUIRED], D-07 [DESIGN_DECISION_REQUIRED], D-08 [DESIGN_DECISION_REQUIRED], E-01 [DESIGN_DECISION_REQUIRED])
  - Root cause: Layout patterns were built per page; the dashboard is still the first-story shell.
  - Verify first: Re-check F-162 and F-163 at real widths and heights.
  - A real dashboard replaces the story-shell wording (F-008).
  - A context-aware patient bar: no unrelated safety panel on report/admin pages and no selected-patient context over registration (F-034, F-035).
  - One tab, theme, form, modal/side-panel, feedback and responsive-table pattern (D-02b, D-07, D-08, F-096, F-176, F-194, F-161 to F-163, F-213, F-215); progress indication (F-075); next-step links (F-200); workspace appointments view with rebook link (F-083).
  - One complete prescription workflow replacing the quick-entry/composer split, preserving portal-story contracts and legacy records (D-04).
  - Menu and data-exchange organization (D-05), billing sub-tabs and guarantor account view (D-09, D-10) with least-privilege access; 'Deactivate' wording and 'Inactive' status label (PD-34); E-01 tooth drawings only after a licence check.
  - Tests: axe and viewport tests at declared widths; component tests for the shared panel; Playwright updated for renamed tabs.
  - Permission / audit / migration impact: Permission: menus follow permissions.

  **Group D: Practice configuration usability and rules** (from R02 story 31; findings: F-019, F-020, F-023 [DESIGN_DECISION_REQUIRED], F-024 [DESIGN_DECISION_REQUIRED], F-025, F-026 [DESIGN_DECISION_REQUIRED], F-027, F-028)
  - Root cause: Configuration forms open out of sight; who may be a provider and the specialty values were never decided.
  - Verify first: Read ConfigEntityPanel and the other list-plus-form screens.
  - Read-only time zone and currency look read-only (F-019); the add/edit form is visible and focused when opened (F-020).
  - Provider eligibility and specialty values are decided and enforced (F-023, F-024; PD-31); scheduling preview shows blocked time or is renamed (F-026); F-025, F-027, F-028 corrected.
  - Tests: Component tests for the shared panel; setup journey.
  - Permission / audit / migration impact: none.

  **Group E: User-selectable date-format preference (follow-on to PKG-04)** (from R02 story 27; findings: none)
  - Root cause: Owner request: a system setting that controls how the system shows and prints dates.
  - Verify first: Starts only after the date-only correctness fix (PKG-04) is closed.
  - A setting selects the date format used on screen and in print; the default and the options are approved before building; printed clinical documents offer an unambiguous option.
  - Setting change is audited and applies everywhere through the one shared formatter.
  - Tests: Formatter tests for every format; setting persistence; print test.
  - Permission / audit / migration impact: Permission: admin. Audit: setting change. Migration: setting row.

### PKG-16 - Complete chain run and exit evidence (P1)

- **Findings (raw ids):** (none)
- **Depends on:** all selected packages
- **Chain stages built here (same review cycle):** All stages 0 to 14.
- **Governance / escalation:** Closes the stabilization set.
- **Decisions needed:** none.
- **Existing isolated specs affected:** none.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Complete chain run and exit evidence** (from R02 story 32; findings: none)
  - Root cause: The chain stages are built inside their stories; the complete run proves them together.
  - Verify first: Run the whole chain from an empty database with no resume.
  - The complete automated accumulated-state chain passes with no release-blocking tracked exception.
  - Required focused regressions, real-backend walkthroughs, quality-gate evidence and release-engineering checks pass.
  - Evidence preserves together: database, document storage, configuration, evidence, schema version, implementation SHA and clock configuration. A resumed run is never accepted as evidence of a full uninterrupted run.
  - Tests: The chain itself plus the register-agreement self-test.
  - Permission / audit / migration impact: none.

### PKG-17 - Owner features (placeholder; list to be supplied) (P4)

- **Findings (raw ids):** (none)
- **Depends on:** none; does not delay any other package
- **Chain stages built here (same review cycle):** Per feature.
- **Governance / escalation:** Each feature crosses the escalation list as applicable.
- **Decisions needed:** PD-02.
- **Existing isolated specs affected:** Per feature.
- **Acceptance groups (each names the raw findings it covers; verify each first and record it as CONFIRMED / NOT_REPRODUCED / DUPLICATE / WITHDRAWN / DESIGN_DECISION_REQUIRED):**

  **Group A: Owner features (list to be supplied; placeholder)** (from R02 story 33; findings: none)
  - Root cause: The owner will add features after stabilization and before the owner's final manual E2E.
  - Verify first: The owner supplies and approves a concrete list.
  - Each approved feature becomes its own story with acceptance criteria and its own chain steps before the owner reviews the finished product.
  - Tests: Per feature.
  - Permission / audit / migration impact: Per feature.

## 5. Decision register (real product, clinical, jurisdiction, policy and data-model decisions)

Reviewer recommendations are recommendations, not owner decisions. Each decision below is required before the coding of the package that needs it.

| Id | Decision | Package | Decider |
|---|---|---|---|
| PD-01 | What 'offline' means for this product (F-013; requirement lines 3 and 51). | PKG-02 | owner |
| PD-02 | The owner's additional features: list, order, scope before the owner's final manual E2E. | PKG-17 | owner |
| PD-04 | Staff onboarding model: in-app Add user, temporary password and forced change; whether the open register endpoint stays behind an off switch (F-016). | PKG-14 | owner |
| PD-05 | Visit-transition matrix: allowed transitions, authority per role, owner of final completion and check-out (F-056, F-059, F-063, F-065, Q-065a). | PKG-05 | owner with clinical lead |
| PD-06 | Hygienist authority to add, retire or mark allergies and medications in error. | PKG-05 | owner with clinical lead |
| PD-09 | Clinical meaning of Diagnosed/Planned missing-tooth coexistence (F-091), duplicate allergy (F-084), and 'none reported' with an active allergy (F-080, F-112). | PKG-06 | clinical sign-off |
| PD-10 | Addendum/amendment workflow for finalized encounters (F-103). | PKG-07 | owner with clinical lead |
| PD-11 | Jurisdiction and content of a prescription: signature, prescriber identification, patient address (F-110). | PKG-12 | researched, then owner |
| PD-12 | Ownership and update rights of allergy-class data (F-105). | PKG-06 | owner |
| PD-14 | Plan-level state after items are completed, and the actions a completed plan offers (F-101, D-11). | PKG-08 | owner |
| PD-15 | **Restored.** Past planned dates (F-221): refuse by default, allow with confirmation and overdue wording, or another rule. The reviewer preferred refusal where no legitimate use exists; the agent's recommendation is confirmation plus an overdue label, because refusing would block recording earlier care. The owner has not selected the rule. | PKG-08 | owner |
| PD-17 | **Restored.** Receipt/statement reissue (F-114): whether a reissue is allowed, the required reason, replacement numbering, and original/replacement presentation. Agent recommendation: allowed as an explicit replacement with a reason, a new number, and the original marked superseded. | PKG-09 | owner |
| PD-18 | **Restored.** Payer eligibility (F-209): which household/guarantor relationships may be selected as payer, and under which permissions. Agent recommendation: the registered guarantor and household members, billing permission required, recorded in the ledger. | PKG-09 | owner |
| PD-20 | Patient-chart access logging: purpose, event shape, readers, retention and noise controls (F-038, F-039). | PKG-10 | owner with compliance |
| PD-21 | Patient number / MRN design (F-146). | PKG-11 | owner |
| PD-24 | Who may see clinical documents (Q-5cn, F-173). | PKG-12 | owner |
| PD-25 | **Restored.** Backup alert delivery (F-152): the operator-visible channel or channels and failure behavior when delivery itself fails. Agent recommendation: in-app inbox plus a banner on administrator screens, with a durable record when delivery fails. | PKG-12 | owner |
| PD-26 | Approval to upgrade vulnerable packages after the advisory review (F-001, F-005). | PKG-15 | owner (governance) |
| PD-31 | Provider eligibility and specialty values (F-023, F-024). | PKG-14 | owner |
| PD-34 | Wording: 'Deactivate' on buttons and messages and 'Inactive' as the status label, replacing the build's 'Inactivate' (not in the requirements; first seen 2026-09-30 in ALV-002-C01). | PKG-14 | owner |
| PD-36 | Optional, separate owner policy choice: any change to the fixed session expiry (extend-session). NOT required by this plan. | none | owner |
| PD-37 | Date-format preference: options, default and print default (follow-on after PKG-04). | PKG-14 | owner |

**Decided by someone else and therefore not in the register:** PD-32 (the chain clock is test-only and explicitly injected, never a production setting and never controlling authentication expiry or retry: reviewer, binding as a test-infrastructure rule). PD-03, PD-07, PD-08, PD-13, PD-16, PD-19, PD-22, PD-23, PD-27, PD-28, PD-29, PD-30 and PD-33 stay retired as in R02: ordinary design choices made inside the package design, or superseded by PD-36/PD-37. PD-16 (how account-level adjustments appear in aging) is treated as a reviewer-stated invariant that the balance stays exact and adjustments are shown as a separately explained amount; the presentation is a package design choice.

## 6. Manual-step to automated coverage map

**Rules**
- Every automated test keeps its manual source-step id (for example `3.10`, `Step 148`) in its name and writes machine-readable pass/fail evidence (run id, stage, step id, outcome, evidence path, implementation SHA, schema version).
- The runner owns the database, document storage, configuration, clock (test-only, explicitly injected), communications stubs, evidence directory, schema version and implementation SHA for the whole run. Run-specific immutable identifiers are used; names alone are not enough.
- A resumed run is diagnostic evidence only and is labelled RESUMED; it is never evidence of a full uninterrupted run.
- A release-blocking known defect cannot appear as an expected green result. It fails, or appears as a blocking tracked exception (`TRACKED_EXCEPTION`), and the run stays red until the register has no release-blocking entry. Playwright's `test.fail()` is not used because it reports a failing test as a pass.
- Manual-only checks are narrow (visual or subjective judgment); they do not recreate the manual test week.
- A changed step keeps its source id and states why the automated equivalent is more reliable than reproducing the original click sequence.
- Chain stages are built inside the package that introduces or repairs them. They do not get their own review turn.

| Stage | Name | Built in |
|---|---|---|
| 0 | Runner and environment | PKG-01 |
| 1 | Setup and sign-in | PKG-02 (extended by PKG-03 and PKG-14) |
| 2 | Registration and import | PKG-02 (minimal), PKG-11 (full) |
| 3 | Scheduling | PKG-05 |
| 4 | Visit flow | PKG-05 |
| 5 | Clinical | PKG-06 (addendum steps in PKG-07) |
| 6 | Charting | PKG-06 |
| 7 | Treatment plans | PKG-08 |
| 8 | Prescriptions | PKG-06 (allergy class), PKG-12 (printing) |
| 9 | Billing | PKG-09 |
| 10 | Documents and storage | PKG-12 |
| 11 | Follow-ups, referrals, notifications | PKG-13 |
| 12 | Reports and measurement | PKG-13 |
| 13 | Permissions and audit | PKG-10 (extended by PKG-14) |
| 14 | Backup and restore | PKG-12 |

The complete chain is run once from an empty database with no resume in PKG-16.

### 6.1 Test cases from 01-test-cases.md

| Source step | What the manual step did | Stage | Package asserting | Type | Concrete automated assertion | Why the mechanism differs (or same) | Raw-log evidence line |
|---|---|---|---|---|---|---|---|
| 1.01 | Open the app address while signed out | 1 Setup and sign-in | PKG-02 | CHAIN | GET of a protected route with no session returns the login page; no patient data is requested | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.02 | Sign in with a wrong password | 1 Setup and sign-in | PKG-02 | CHAIN | wrong password: 401 invalid_credentials, same message for an existing and a non-existing username | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.03 | Sign in correctly | 1 Setup and sign-in | PKG-02 | CHAIN | correct sign-in: whoami returns the username and role; the admin menu entries are present | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.04 | Sign in with an account that is still disabled | 1 Setup and sign-in | PKG-03 | CHAIN | a freshly registered (disabled, role-less) account cannot sign in: response is account_disabled | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.05 | Security Administration: disable billing1, then try to sign in as billing1 | 1 Setup and sign-in | PKG-03 | CHAIN | disable billing1 then sign in: refused; re-enable: sign-in succeeds | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.06 | Security Administration: use "reset password" and "revoke sessions" on a test user while t... | 1 Setup and sign-in | PKG-03 | CHAIN | reset password and revoke sessions: the other session's next API call returns 401 and its UI is signed out | API-level session check is more reliable than waiting in two browsers | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.07 | Look at the left menu | 13 Permissions and audit | PKG-10 | CHAIN + REGRESSION | for each of the 7 roles the set of menu entries equals the expected set from the Permission Matrix fixture; Component Showcase is absent outside development (PKG-14) | table-driven per role instead of eyeballing seven windows | 77 |
| 1.08 | Type /admin/audit-log and /admin/users directly in the address bar | 13 Permissions and audit | PKG-10 | CHAIN | frontdesk1 GET /admin/audit-log and /admin/users render the permission-denied page naming the permission; API returns 403 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.09 | Permission Matrix: read it and compare with 1.07 | 13 Permissions and audit | PKG-10 | CHAIN | for every matrix cell, the real API response for that role equals the matrix (200 vs 403) | script compares the full matrix instead of sampling | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.10 | Sign out; press the browser Back button | 1 Setup and sign-in | PKG-02 | REGRESSION | after sign-out the Back button shows the login page and no cached patient data (PKG-02) | component test of history and cache clearing | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.11 | Multi-Factor Authentication: start enrolling, but do not finish | 1 Setup and sign-in | PKG-02 | CHAIN + MANUAL-ONLY | starting MFA enrolment without finishing leaves password sign-in unchanged (automated); how the flow reads is a subjective judgment (manual-only) | split: objective part automated | executed in the 6.4 rows of this stage; no single log line carries this id |
| 1.12 | Leave the app idle past the session timeout (or use the per-user timeout if you can set a ... | 1 Setup and sign-in | PKG-02 | REGRESSION + CHAIN | with a short per-user timeout set by the test: the warning appears, a form with unsaved content keeps it, expiry signs out with the expiry message; fixed expiry itself is unchanged | short timeout replaces waiting 30 minutes; no session extension is added | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.01 | Register Patient: leave everything empty, submit | 2 Registration and import | PKG-11 | CHAIN | submitting an empty registration returns 400 naming each required field; zero patients created | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.02 | Register Alice Anderson (1985-03-14) with full details | 2 Registration and import | PKG-11 | CHAIN | registering Alice returns 201 and the record opens with the same fields | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.03 | Register Alice Anderson again, same details | 2 Registration and import | PKG-11 | CHAIN | registering the identical person is refused as an exact duplicate and returns the existing record id | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.04 | Register "Alicia Anderson", same date of birth, same address | 2 Registration and import | PKG-11 | CHAIN | near match (Alicia, same DOB and address) returns a possible-duplicate response with a compare payload; 'register anyway' requires a reason (F-036, D-02a) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.05 | Phone 5550 (fewer than 7 digits) | 2 Registration and import | PKG-11 | CHAIN | phone '5550' returns a field error on phone | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.06 | Date of birth in the future, and 31/02/1990 | 2 Registration and import | PKG-11 | CHAIN | future DOB and '31/02/1990' each return a field error on date of birth | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.07 | Name with accents and an apostrophe (Zoë O'Neil) | 2 Registration and import | PKG-11 | CHAIN | 'Zoe O'Neil' with accents round-trips unchanged through API, list and header | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.08 | Register Brian Baker, Carla Chen, Daniel Diaz (same address as Carla) | 2 Registration and import | PKG-11 | CHAIN | Brian, Carla and Daniel (same address as Carla) are created; counts equal 3 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.09 | Double-click the Register button quickly | 2 Registration and import | PKG-11 | REGRESSION + CHAIN | two identical concurrent create calls with one idempotency key create exactly one patient | API-level concurrency is more reliable than a fast double-click | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.10 | Patients: search by last name, by part of a name, by date of birth, by phone | 2 Registration and import | PKG-11 | CHAIN | search by last name, name fragment, DOB and phone each returns the expected patient; an empty result shows the empty state | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.11 | Open a patient, edit the phone, save, reload | 2 Registration and import | PKG-11 | CHAIN | editing the phone and reloading returns the new phone | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.12 | Open the same patient in two tabs; change different fields in each; save both | 2 Registration and import | PKG-11 | REGRESSION | two updates carrying the same row version: the second returns a version-conflict response and nothing is overwritten | API-level version check instead of two browser tabs | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.13 | Patient > Household & guarantor: make Carla Daniel's guarantor | 2 Registration and import | PKG-11 | CHAIN | setting Carla as Daniel's guarantor shows on both records | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.14 | Edit a field, then click another menu item without saving | 2 Registration and import | PKG-02 | REGRESSION | editing then navigating away triggers the unsaved-changes guard (component test) | component test instead of a manual click path | executed in the 6.4 rows of this stage; no single log line carries this id |
| 2.15 | Open a patient | 13 Permissions and audit | PKG-10 | CHAIN | billing1 and assistant1 open a patient and see exactly the tabs the matrix allows | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.01 | Calendar: book Alice, New patient exam, Dr. Dana, Operatory 1, next weekday 09:00 | 3 Scheduling | PKG-05 | CHAIN | booking Alice with Dr. Dana in Operatory 1 returns 201 and the appointment appears in the calendar query | same mechanism | 286 |
| 3.02 | Book Brian with Dr. Dana at 09:30 (overlaps 3.01) | 3 Scheduling | PKG-05 | CHAIN | an overlapping booking for the same provider is refused: provider double-booked, nothing created | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.03 | Book Brian with Harper in Operatory 1 at 09:00 | 3 Scheduling | PKG-05 | CHAIN | an overlapping booking in the same operatory is refused: operatory in use | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.04 | Book Alice again at 09:15 with Harper in Hygiene 1 | 3 Scheduling | PKG-05 | CHAIN | a second booking for the same patient at an overlapping time is refused: patient double-booked | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.05 | Book at 07:00 (before hours) and at 12:30 (blocked lunch) | 3 Scheduling | PKG-05 | CHAIN | 07:00 (before hours) and a blocked-lunch time are each refused with the reason | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.06 | Book in the past | 3 Scheduling | PKG-05 | CHAIN | booking in the past is refused (injected clock) | clock control instead of real time | 267 |
| 3.07 | Book exactly back-to-back (09:00 to 10:00, then 10:00) | 3 Scheduling | PKG-05 | CHAIN | back-to-back bookings (end equals next start) are allowed | same mechanism | 273 |
| 3.08 | Reschedule 3.01 to another time | 3 Scheduling | PKG-05 | CHAIN | rescheduling moves the appointment and history records the old and new slot and the actor (F-049) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.09 | Cancel an appointment with a reason; mark another no-show | 3 Scheduling | PKG-05 | CHAIN | cancel with a reason frees the slot; no-show is allowed only after the start time passes (injected clock replaces the SQL time shift) | clock control instead of editing rows in SQL | 265 |
| 3.10 | Book the same slot from two windows at once | 3 Scheduling | PKG-05 | REGRESSION + CHAIN | two concurrent bookings for the same slot: exactly one 201, one refusal | API concurrency instead of two browser windows | 274 |
| 3.11 | Visit board: check Alice in | 4 Visit flow | PKG-05 | CHAIN | check-in moves the appointment through the matrix transitions with actor history | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 3.12 | Visit board: move the patient to a chair and assign provider/operatory | 4 Visit flow | PKG-05 | CHAIN | per role, the move to chair and the provider/operatory assignment are allowed or refused as the visit-transition matrix says; the booking itself is unchanged | same mechanism, driven by the PD-05 matrix | 286 |
| 3.13 | Navigate day/week, change provider and operatory filters, Today button | 3 Scheduling | PKG-05 | CHAIN | day/week, provider/operatory filters and Today change the query result; non-working hours are marked | same mechanism | 286 |
| 3.14 | Schedule page (menu "Schedule") | 3 Scheduling | PKG-05 | CHAIN | the Schedule page list equals the calendar query for the same day | same mechanism | 286 |
| 4.01 | Alice > Clinical: add medical history, an allergy (penicillin), a medication | 5 Clinical | PKG-06 | CHAIN | medical history, a penicillin allergy and a medication are saved; the allergy appears in the patient header and Safety tab without a reload (F-078) | same mechanism | 301 |
| 4.02 | Start a clinical note (encounter), type text, save a draft | 5 Clinical | PKG-06 | CHAIN | a draft note autosaves and reloads with the typed text | same mechanism | 314 |
| 4.03 | Sign/complete the note | 5 Clinical | PKG-07 | CHAIN | finalizing makes the note read-only; changes only by addendum | same mechanism | 340 |
| 4.04 | Try to edit a signed note | 5 Clinical | PKG-07 | CHAIN | editing a signed note is refused | same mechanism | 348 |
| 4.05 | Open the clinical templates; apply one | 5 Clinical | PKG-06 | CHAIN | applying a template fills the section and stays editable before signing; a required section blocks finalizing until filled | same mechanism | 350 |
| 4.06 | Safety tab: add an alert and a clearance | 5 Clinical | PKG-06 | CHAIN | an alert and a clearance are saved, shown on the patient, and acknowledge/resolve require their reasons | same mechanism | 366 |
| 4.07 | frontdesk1 and billing1 open Alice's Clinical/Safety URL directly | 13 Permissions and audit | PKG-10 | CHAIN + REGRESSION | frontdesk1 and billing1 receive 403 on Clinical and Safety endpoints and the permission-denied page on those URLs | same mechanism, plus direct API 403 check | 388 |
| 4.08 | hygienist1 adds a note | 5 Clinical | PKG-05 | CHAIN | hygienist1 adds a note as the matrix allows | same mechanism | 396 |
| 5.01 | Odontogram: mark tooth 14 as having a filling on a surface | 6 Charting | PKG-06 | CHAIN | a finding on tooth 14 appears on the chart with a history entry | same mechanism | 412 |
| 5.02 | Change the same tooth later | 6 Charting | PKG-06 | CHAIN | changing the same tooth later keeps the old state in history | same mechanism | 413 |
| 5.03 | Mark an impossible combination (a surface on a missing tooth) | 6 Charting | PKG-06 | CHAIN | Existing/Completed missing tooth refuses another finding; Diagnosed/Planned coexistence follows PD-09 (warning if kept); stored tooth identity is never altered (F-091, F-104) | assertion for the open part waits for PD-09 | 414 |
| 5.04 | Periodontal: new session; enter probing depths for several teeth | 6 Charting | PKG-06 | CHAIN | a periodontal session with probing depths saves; bleeding percentage is computed | same mechanism | 448 |
| 5.05 | Enter a depth outside range (e.g. 25 mm) | 6 Charting | PKG-06 | CHAIN | a depth of 25 mm is rejected | same mechanism | 452 |
| 5.06 | Create a second session; compare with the first | 6 Charting | PKG-06 | CHAIN | a second session compares with the first and shows the differences | same mechanism | 448 |
| 5.07 | Diagnoses: add a diagnosis linked to tooth 14 and the encounter | 6 Charting | PKG-06 | CHAIN | a diagnosis linked to tooth 14 and the encounter is listed and can be resolved with a reason | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 5.08 | hygienist1: try to add a diagnosis | 6 Charting | PKG-05 | CHAIN | hygienist1 can or cannot add a diagnosis exactly as the matrix says | same mechanism | 472 |
| 6.01 | dentist1: Treatment plan for Alice: add L3000 composite filling on tooth 14 | 7 Treatment plans | PKG-08 | CHAIN | a plan item L3000 on tooth 14 takes the catalog fee 180.00 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.02 | Add L4000 crown; present the plan | 7 Treatment plans | PKG-08 | CHAIN | adding L4000 and presenting the plan gives the correct total | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.03 | Change a fee in Procedures & fees (as billing1) and look at the existing plan | 7 Treatment plans | PKG-08 | CHAIN | a catalog fee change does not alter an existing plan's fee but applies to a new plan | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.04 | dentist1: complete a plan item (the filling) | 7 Treatment plans | PKG-08 | CHAIN | completing the filling marks it completed and creates one charge of 180.00; completion against a finalized encounter is refused (F-103) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.05 | Complete the same item again (double click / second tab) | 7 Treatment plans | PKG-08 | REGRESSION + CHAIN | completing the same item twice with the same key creates one charge | repeating the API call is more reliable than a double click | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.06 | Withdraw an item | 7 Treatment plans | PKG-08 | CHAIN | withdrawing an item marks it withdrawn and keeps it | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.07 | Prescriptions: prescribe penicillin to Alice (allergy recorded in 4.01) | 8 Prescriptions | PKG-06 | CHAIN | prescribing penicillin to a penicillin-allergic patient shows the allergy warning and needs an override reason; the check returns not-checkable for unknown entries (F-105) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.08 | Prescription composer: draft, finalize, print, then amend and cancel another | 8 Prescriptions | PKG-12 | CHAIN | draft, finalize, print, amend, cancel and discontinue each record who and when; a cancelled/discontinued prescription never prints as active (F-164) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 6.09 | frontdesk1 tries to prescribe | 8 Prescriptions | PKG-05 | CHAIN + REGRESSION | frontdesk1 and hygienist1 prescribe attempts return 403 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.01 | Alice > Billing: view the account after 6.04 | 9 Billing | PKG-09 | CHAIN | after completion the account shows one charge of 180.00 and balance owed 180.00 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.02 | Record a payment of $50.00 (cash) | 9 Billing | PKG-09 | CHAIN | a 50.00 cash payment leaves balance 130.00 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.03 | Record a payment larger than the balance | 9 Billing | PKG-09 | CHAIN | a payment larger than the balance follows the documented rule (credit or refusal) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.04 | Reverse the payment | 9 Billing | PKG-09 | CHAIN | reversing a payment adds a reversing entry; the original stays; balance returns to 180.00 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.05 | Add an adjustment with a reason | 9 Billing | PKG-09 | CHAIN | an adjustment requires a reason and appears in the ledger | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.06 | Try to delete or edit a ledger entry | 9 Billing | PKG-09 | CHAIN | no API or UI route edits or deletes a ledger entry | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.07 | Open the same account in two windows; record a payment in each | 9 Billing | PKG-09 | REGRESSION + CHAIN | two concurrent payment calls on one account: the second is told the account changed | API concurrency instead of two windows | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.08 | Statement: generate one for Alice | 9 Billing | PKG-09 | CHAIN | a statement's totals equal the ledger; a receipt is issued once per payment and its CSV/print carry patient, practice and as-of time (F-114, F-117, F-119) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.09 | Guarantor: Daniel's charge, view from Carla's account (2.13) | 9 Billing | PKG-09 | CHAIN | Daniel's charge viewed from Carla's account shows the responsible party correctly; payer choices follow PD-18 | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.10 | Procedures & fees: add, revise (with reason), inactivate a procedure | 9 Billing | PKG-09 | CHAIN | a procedure can be added, revised with a reason and inactivated; history is kept | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.11 | Procedures: add a duplicate code | 9 Billing | PKG-09 | CHAIN | a duplicate procedure code is refused | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 7.12 | frontdesk1 changes a fee | 13 Permissions and audit | PKG-10 | CHAIN | frontdesk1 fee change returns 403 and the screen is read-only | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 8.01 | Alice > Documents: import xray-sample.png with a category and date | 10 Documents and storage | PKG-12 | CHAIN | importing xray-sample.png with category and date lists it and opens inline | same mechanism | 762 |
| 8.02 | Import photo-sample.png (identical bytes, different name) | 10 Documents and storage | PKG-12 | CHAIN | an identical-bytes file is flagged as a duplicate and added only on confirmation | same mechanism | 763 |
| 8.03 | Import referral-letter.pdf, lab-report.pdf, intraoral-sample.jpg | 10 Documents and storage | PKG-12 | CHAIN | the three files are listed with type and size | same mechanism | 766 |
| 8.04 | Import each REFUSE-* file | 10 Documents and storage | PKG-12 | CHAIN | each REFUSE-* file is refused with a reason and nothing is stored | same mechanism | 768 |
| 8.05 | Import large-5MB.bin | 10 Documents and storage | PKG-12 | CHAIN | large-5MB.bin is accepted below the limit | same mechanism | 766 |
| 8.06 | Edit a document's category/date/description (manager1) | 10 Documents and storage | PKG-12 | CHAIN | editing category/date/description is saved and recorded in history | same mechanism | 776 |
| 8.07 | Inactivate then reactivate a document | 10 Documents and storage | PKG-12 | CHAIN | inactivate then reactivate records both; nothing is deleted | same mechanism | 777 |
| 8.08 | Thumbnails / filters by category, date, text | 10 Documents and storage | PKG-12 | CHAIN | thumbnails and category/date/text filters combine correctly | same mechanism | 779 |
| 8.09 | Document settings (admin): add a blocked type bin, retry 8.05; remove it | 10 Documents and storage | PKG-12 | CHAIN | a blocked type refuses the import, removing it allows it; built-in blocked types cannot be removed | same mechanism | 786 |
| 8.10 | Document storage (admin): run a check | 10 Documents and storage | PKG-12 | CHAIN | the storage check reports all Ok | same mechanism | 788 |
| 8.11 | Stop the API; rename one stored file on disk under Dev-Data\Storage; restart; run the chec... | 10 Documents and storage | PKG-12 | CHAIN | the runner renames a stored file on disk, restarts the API, then the check reports Missing and open is never served (friendly failure, F-130/F-131); the runner restores the file | scripted by the runner instead of manual file surgery | 789 |
| 8.12 | Patient > Forms: use a form template (admin: Form Templates), have a patient sign | 10 Documents and storage | PKG-12 | CHAIN | a signed form is listed as Signed form; editing the template later does not change the signed copy | same mechanism | 789 |
| 9.01 | Recall settings (admin): set a recall interval | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | a recall interval is saved and shown | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 9.02 | Alice > Follow-ups: add a task with a due date and an owner | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | a follow-up with due date and owner appears in My follow-ups for that owner | same mechanism | 819 |
| 9.03 | Complete it; reopen the page | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | completing a follow-up keeps its history across reload | same mechanism | 819 |
| 9.04 | Work queue: filter by owner, status, due date | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | work-queue filters by owner, status and due date match the tasks | same mechanism | 819 |
| 9.05 | Outside parties (admin): add a lab and a specialist | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | a lab and a specialist can be added and listed; duplicate names are refused | same mechanism | 847 |
| 9.06 | Alice > Referrals & labs: create a referral and a lab case; link a document from 8.03 | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | a referral and a lab case link a document and the timeline shows each step | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 9.07 | Move a case through its statuses; try an invalid jump | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | valid status moves are accepted and an invalid jump is refused or not offered | same mechanism | 863 |
| 10.01 | Bell icon: create an event that notifies (a due follow-up) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | a due follow-up creates exactly one notification | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 10.02 | Mark read; reload | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | mark read persists across reload and counts refresh immediately (F-134) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 10.03 | Messages: open the list and a message | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | a message is readable and holds no more patient detail than the recipient's role may see | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 10.04 | Notification history (admin) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | notification history lists deliveries without patient content | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 10.05 | Break-glass: access a record you normally cannot, as per the docs (docs\NOTIFICATIONS.md /... | 13 Permissions and audit | PKG-10 | CHAIN | break-glass access needs a reason and is recorded in the audit log | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.01 | Data import: send patients.csv as kind Patients | 2 Registration and import | PKG-11 | CHAIN | sending patients.csv as kind Patients is accepted and the batch is listed | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.02 | Confirm the column mapping; run the dry-run | 2 Registration and import | PKG-11 | CHAIN | dry-run result counts: valid, skipped (existing and repeated rows), rejected (bad phone, bad date, missing address) match the fixture | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.03 | Decide any duplicate queue rows ("different person" / "same person") | 2 Registration and import | PKG-11 | CHAIN | a duplicate-queue decision ('different person' or 'same person') is recorded with a reason | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.04 | Commit with "commit the valid rows only" and tick the confirmation | 2 Registration and import | PKG-11 | CHAIN | commit of valid rows finishes with counts equal to the dry-run | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.05 | Reconcile; download the error report | 2 Registration and import | PKG-11 | CHAIN | reconcile reports no problems; the error report lists rejected rows and holds no values | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.06 | Send the same file again | 2 Registration and import | PKG-11 | CHAIN | re-sending the same file returns the existing batch; no duplicate patients | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.07 | Send REFUSE-import-empty.csv and REFUSE-import-ragged.csv | 2 Registration and import | PKG-11 | CHAIN | the empty and ragged files are refused naming row and column | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.08 | Try to commit twice quickly | 2 Registration and import | PKG-11 | REGRESSION | a second concurrent commit does nothing | API concurrency instead of fast clicking | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.09 | Export patient data (menu "Export patient data") for Alice | 2 Registration and import | PKG-11 | CHAIN | exporting Alice downloads a package whose manifest and data match her record | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.10 | Import a patient (menu "Import a patient") using that package into the same system | 2 Registration and import | PKG-11 | CHAIN | importing that package into the same system reports the patient exists and creates nothing; the wording does not imply an unsupported round trip (F-147) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.11 | Duplicate patients (menu): merge two near-duplicates (use "Alicia Anderson" if she exists) | 2 Registration and import | PKG-11 | CHAIN | merge preview, confirmation and merged record work and the merge can be undone; not-a-duplicate needs a reason (F-148) | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 11.12 | Open the merge/import/export screens by URL | 13 Permissions and audit | PKG-10 | CHAIN | frontdesk1 gets 403/permission-denied on merge, import and export routes | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 12.01 | Reports: open each report you are allowed to see | 12 Reports and measurement | PKG-13 | CHAIN | each report the role may see loads and shows a clear empty state | same mechanism | 967 |
| 12.02 | Compare a report total to what you did (payments, charges, appointments) | 12 Reports and measurement | PKG-13 | CHAIN | report totals equal the sums of the transactions the earlier stages created | same mechanism | 968 |
| 12.03 | Change the date range to one with no data | 12 Reports and measurement | PKG-13 | CHAIN | a range with no data shows zero, not an error; a reversed range is refused | same mechanism | 973 |
| 12.04 | A role without report permission opens Reports by URL | 13 Permissions and audit | PKG-10 | CHAIN + REGRESSION | a role without report permission gets 403 on the report URL (frontdesk1 may see the Schedule report per the matrix, F-158) | expectation corrected to the matrix | 973 |
| 13.01 | Audit Log (admin): find your own sign-in, a patient registration and a document import | 13 Permissions and audit | PKG-10 | CHAIN | the audit log holds the sign-in, a registration and a document import with actor name and time, and no patient names or file names in the text (F-149, F-214) | same mechanism | 992 |
| 13.02 | Audit Log: filter and page | 13 Permissions and audit | PKG-10 | CHAIN | audit filters and paging return the expected rows | same mechanism | executed in the 6.4 rows of this stage; no single log line carries this id |
| 13.03 | Backup & Recovery (admin): read the status | 14 Backup and restore | PKG-12 | CHAIN | Backup & Recovery shows the last backup or a plain statement that none exists | same mechanism | 999 |
| 13.04 | Take a backup if the screen offers one | 14 Backup and restore | PKG-12 | CHAIN | taking a backup completes and appears in the status; failures reach the operator-visible channel (PD-25, F-152) | same mechanism | 1006 |
| 13.05 | Shows healthy; stop the API and watch the client | 14 Backup and restore | PKG-12 | CHAIN | the runner stops the API: the disconnected banner appears, typed work is kept (outage and preservation behavior are asserted under PKG-02), and the app recovers on restart; System Status is truthful | runner stops and starts the API instead of Ctrl+C by hand | 1033 |
| 13.06 | Restore the SQL checkpoint from E10 and sign in | 14 Backup and restore | PKG-12 | CHAIN | restoring the checkpoint returns the data as at the checkpoint, snapshot options stay on, and the orphan-file report lists the files nothing points to | same mechanism | 1333 |

### 6.2 Cross-cutting checks (section 14 of the test cases)

| Source step | Manual check | Type | Automated equivalent |
|---|---|---|---|
| 14.a | Keyboard-only reach of every button and field | REGRESSION | axe plus a scripted Tab traversal per page |
| 14.b | Phone-width layout | REGRESSION | viewport tests at the declared widths |
| 14.c | 200% zoom readable | REGRESSION | zoom-emulation layout test |
| 14.d | Refresh in the middle of a form loses nothing without warning | REGRESSION | unsaved-content guard test (PKG-02) |
| 14.e | No stack trace, SQL error, patient name in a URL or raw error code | CHAIN | automated scan of every response and page in every stage |
| 14.f | Anything slower than 3 seconds | CHAIN | per-step timing budget recorded and asserted |
| 14.g | Whether it reads or looks right | MANUAL-ONLY | cannot be objectively automated |

### 6.3 Steps run outside the test-case document

| Source step | Manual step | Stage | Package asserting | Type | Concrete automated assertion | Why the mechanism differs |
|---|---|---|---|---|---|---|
| S-01 to S-08 | Startup-script experiments: busy port, no database, client without API | 0 Runner and environment | PKG-01 | CHAIN + REGRESSION | Scripts refuse with a one-line message and a non-zero exit; the runner creates and drops its own database | script exit-code test replaces reading console output |
| S-09 | Admin creation and first sign-in on a fresh database | 0 Runner and environment | PKG-01 | CHAIN | admin created once; second run reports it exists and changes nothing; sign-in succeeds (F-015 INVESTIGATE) | same mechanism |
| D1 to D6 | Start API/client, create the administrator, create and enable six staff logins | 1 Setup and sign-in | PKG-02, PKG-03 | CHAIN | logins exist, are enabled and carry the right role | logins created by API in the chain; the in-app route is PKG-14 (F-016) |
| E1 to E9 | Practice, staff, providers, operatories, appointment types, availability, scheduling summary, procedure catalog, registration settings | 1 Setup and sign-in | PKG-14, PKG-03 | CHAIN | each configuration saves, lists and enforces its rule (see 6.4 rows for lines 123 to 139) | same mechanism |
| E10 | Checkpoint backup (COPY_ONLY) and restore | 0 Runner and environment | PKG-01 | CHAIN | runner takes a safety-net checkpoint after each stage and never restores automatically in a full run | checkpoints are safety nets, never resets |
| Part G section 1 | Role menus, direct URLs, permission-denied pages, Procedures & fees read-only for FrontDesk | 13 Permissions and audit | PKG-10 | CHAIN + REGRESSION | menu set per role equals the matrix; denied routes name the permission | table-driven |
| Part G section 2 | Registration, duplicates, double-click, conflict, search, guarantor | 2 Registration and import | PKG-11, PKG-02 | CHAIN + REGRESSION | see cases 2.01 to 2.15; F-037 context clearing asserted in PKG-02 | same mechanism |
| Part G section 3 | Household, guarantor, child patient, inactivate/reactivate, audit rows | 2 Registration and import | PKG-11, PKG-10 | CHAIN | relationships appear on both records; audit rows carry actor and values (F-041) | same mechanism |
| Step 113 | Hygienist resolves an alert | 5 Clinical | PKG-05 | CHAIN | resolved alert disappears from Active; banner no longer lists it | same mechanism |
| Step 114 | Resolved-alert detail; open message from an urgent notice | 5 Clinical; 11 Follow-ups, referrals, notifications | PKG-06, PKG-13 | CHAIN | resolved alert shows who/why; the unread list empties after opening | same mechanism |
| Step 115 | hygienist1 reopens and resolves the dentist's alert | 5 Clinical | PKG-05 | CHAIN | reopen and resolve are allowed per the matrix and both appear in history | same mechanism |
| Step 116 | billing1 tab and notice views | 13 Permissions and audit | PKG-10 | CHAIN | tabs shown equal the matrix for billing1 | same mechanism |
| Step 117 | manager1 Permission Matrix and delegation table | 13 Permissions and audit | PKG-10 | CHAIN | delegation rules are listed; non-delegable permissions are not editable | same mechanism |
| Step 118-119 | manager1 grants and stops ImportDocuments; effect for assistant1; manager1 admin URLs | 13 Permissions and audit; 10 Documents and storage | PKG-10 (stage 13); PKG-12 (stage 10) | CHAIN + REGRESSION | a delegated grant needs reason and confirmation, changes only what the delegate holds, and is audited; boundary and audit verification belongs to PKG-05 (F-170 verification task) | same mechanism |
| Step 120 | Billing tab as manager1 (Alice) | 9 Billing | PKG-09 | CHAIN | read-only billing view matches the ledger | same mechanism |
| Step 121 | Treatment plan tab look; Review old diagnosis references | 7 Treatment plans | PKG-08 | CHAIN | four plans and their actions render; no old reference to tie (tying is second pass) | same mechanism |
| Step 122 | Adopt earlier completions on four plans | 7 Treatment plans | PKG-08 | CHAIN | the box explains there is nothing to adopt, or the button is hidden (F-180); the submit path is second pass | same mechanism |
| Step 123-124 | Rename plan, reference code, compare revisions | 7 Treatment plans | PKG-08 | CHAIN | a rename creates a named revision (F-219); compare shows Before/After | same mechanism |
| Step 125-127 | Accept all pending, Schedule L1000, change schedule, withdraw item/plan | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | a blank date cannot schedule (F-188); withdraw keeps the record | blank-date schedule becomes a regression |
| Step 128-131 | Revise test plan, single accept, decline all pending, changed answers, plan audit rows | 7 Treatment plans; 13 Permissions and audit | PKG-08; PKG-10 | CHAIN | answers, revisions and audit rows agree (F-190, F-192) | same mechanism |
| Step 132 | Household & guarantor tab as billing1 | 9 Billing | PKG-09 | CHAIN | tab is read-only for billing1 | same mechanism |
| Step 133-135 | Alice Billing tab, record responsibility, Daniel's Billing tab | 9 Billing | PKG-09 | CHAIN | responsibility options and displays follow PD-18 | same mechanism |
| Step 136-138 | Daniel treatment plan / diagnoses / clinical tabs | 5 Clinical; 7 Treatment plans | PKG-06, PKG-08 | CHAIN | with no diagnosis or encounter the forms are unavailable with an explanation | same mechanism |
| Step 139-146 | Second party for Alice, self-responsible, who paid, receipt 3, Carla's household and billing | 9 Billing | PKG-09 | CHAIN + REGRESSION | payer choices and responsibility records follow PD-18; receipt numbering follows PD-17 | payer/responsible independence becomes a regression |
| Step 147 | API window review for F-127 | 13 Permissions and audit | PKG-10 | REGRESSION | handled refusals and real SqlExceptions are logged under different event names with method, path and SQL number | unit test of the middleware |
| Step 148-150 | Permission Matrix as admin; assistant1 and manager1 views; composer read-only for assistant1 | 13 Permissions and audit; 8 Prescriptions | PKG-10; PKG-05 | CHAIN | matrix cells equal real API responses | same mechanism |
| Step 152-154 | Measurement and baselines | 12 Reports and measurement | PKG-13 | CHAIN + REGRESSION | refusals, save, honest comparison, duplicate baseline refused (F-216) | same mechanism |
| Step 155a-155b | Take off the schedule; revise (phase validation); rename | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | taking off the schedule restores the prior state with history; revise needs a reason | same mechanism |
| Step 156-157 | Revision history and phase save | 7 Treatment plans | PKG-08 | CHAIN | history lists each revision with reason | same mechanism |
| Step 158 | Compare revisions 4 vs 5; blank answer | 7 Treatment plans | PKG-08 | CHAIN | comparison text and blank 'who gave this answer' accepted | same mechanism |
| Step 159 | Patient copy and past planned date | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | patient copy content correct; past planned date follows PD-15 (F-221) | print look is manual-only |
| Step 160 | Adopt earlier completions box on a plan with nothing adoptable | 7 Treatment plans | PKG-08 | CHAIN | same explanation or hidden button (F-222) | same mechanism |
| Step 161 / 13.06 | Restore the SQL checkpoint; snapshot options; patients list; orphan-file report | 14 Backup and restore | PKG-12 | CHAIN | restored data equals the checkpoint; snapshot options on; orphan report lists unreferenced files (Q-5cw, Q-5cx) | same mechanism |
| Q-5cl (skipped) | Skipped by the owner for the second pass | not placed | PKG-16 | pending | to be decided with the second pass | n/a |

### 6.4 Every session segment of the raw log (hand-classified in R04)

Every segment of the raw log (142 headings and inline markers) was re-read and classified by hand; the three long segments covering steps 113 to 161 are split into one row per manual step (190 rows in all). 'Covers' cites the section 6.1 case ids and 6.3 step ids the segment exercised. 'Package asserting' is the package whose acceptance criteria the assertion proves; the stage-to-package table names the package that builds the stage, and a later package may extend it. Rows marked N/A are not executed steps (analysis notes, ideas, release input).

| Log line | Segment | Stage | Package asserting | Type | Covers | What is asserted |
|---|---|---|---|---|---|---|
| 35 | Recommendations for triage (not applied) | n/a | - | N/A (analysis notes, not an executed step) | F-001 to F-021 recommendations | No test: these are recommendations, each finding is covered by the package that owns it (Appendix A). |
| 83 | Startup-script experiments (owner's plan; expectations come from reading the scripts,... | 0 Runner and environment | PKG-01 | CHAIN + REGRESSION | S-01 to S-08 | Start scripts refuse a busy port or a missing database with a one-line message and a non-zero exit (F-011); the runner creates and drops its own database. |
| 97 | Input for Release Engineering (stories 56-58), from F-015 (only if the mid-run runtim... | n/a | - | N/A (release-engineering input, owner-gated) | F-015 | No test: input for release engineering, which is paused. |
| 105 | Vite console during S-07 (owner paste) | 1 Setup and sign-in | PKG-02 | REGRESSION | S-07 | With the API down the UI shows 'server unavailable' within the 4 s timeout, never 'session expired' or 'invalid credentials' (F-012, F-014). |
| 109 | S-09 progress | 0 Runner and environment | PKG-01 | CHAIN | S-09 | Admin creation is idempotent (second run reports it exists and changes nothing); first sign-in on a fresh database succeeds; F-015 stays INVESTIGATE. |
| 113 | D5 (staff accounts) | 1 Setup and sign-in | PKG-02 | CHAIN | D5 | Six staff logins register disabled with no role (done by API in the chain). |
| 117 | D6 (roles and enabling) | 1 Setup and sign-in | PKG-03 | CHAIN | D6 | Enabling a login and assigning its role is reflected in the user list (Enabled, role). |
| 121 | Part E progress (practice setup) | n/a | - | N/A (heading; content in the rows below) | - | - |
| 123 | E1 Practice tab: name, phone, address entered and saved (owner's practice name "Baghd... | 1 Setup and sign-in | PKG-14 | CHAIN | E1 | Practice name, phone, address save; time zone and currency are shown read-only (F-019). |
| 125 | E2 Staff tab: six active profiles (Alex Assistant/assistant1, Bill Billing/billing1, ... | 1 Setup and sign-in | PKG-03 | CHAIN + REGRESSION | E2 | Six staff profiles exist; deactivating a profile also disables its linked login (F-021). |
| 126 | E3 Providers tab: Dr. Dana (General dentistry) and Harper (Hygiene) added, both Activ... | 1 Setup and sign-in | PKG-14 | CHAIN | E3 | Providers can be created from eligible staff (rule per PD-31, F-023, F-024). |
| 128 | E4 Operatories tab: Hygiene 1, Operatory 1 and Operatory 2 added, all Active, listed ... | 1 Setup and sign-in | PKG-14 | CHAIN | E4 | Operatories save and list alphabetically; duplicate name is refused. |
| 129 | E5 Appointment types tab: Cleaning 45 min, Emergency 30 min, Filling 60 min, New pati... | 1 Setup and sign-in | PKG-14 | CHAIN | E5 | Appointment types save with a duration in 5-minute steps. |
| 130 | E6 Availability tab (Dr. Dana): weekly hours Monday to Friday 09:00 AM to 05:00 PM sa... | 1 Setup and sign-in | PKG-14 | CHAIN | E6 | Weekly hours and a blocked time save; the provider cannot be booked outside them. |
| 131 | E6 follow-up: with the end before the start, clicking "Save weekly hours" did not sav... | 1 Setup and sign-in | PKG-14 | CHAIN | E6 | End-before-start is refused; the stale row error clears once rows return to the saved state (F-025). |
| 132 | E6 Harper (Hygiene): switching the provider cleared the page to an empty schedule (PA... | 1 Setup and sign-in | PKG-14 | CHAIN | E6 | Switching provider shows that provider's own schedule with nothing left over. |
| 133 | E7 Scheduling preview tab: NOT a preview. It is a read-only summary: "Active location... | 1 Setup and sign-in | PKG-14 | CHAIN | E7 | The scheduling summary shows effective hours including blocked time (F-026). |
| 134 | E8 Procedures & fees (form inspection, nothing saved yet): empty catalog shows "No pr... | 1 Setup and sign-in | PKG-14 | CHAIN | E8 | Empty catalog shows its empty state; the Add form is reachable (F-020, F-027). |
| 135 | E8 result: all five procedures added (L1000 Exam $75 Diagnostic Whole mouth; L2000 Cl... | 9 Billing | PKG-09 | CHAIN | E8, 7.10 | L1000 to L5000 save with their fees and applicability. |
| 136 | E8 bad-entry results (all refused, nothing saved): (a) duplicate L1000: inline "That ... | 9 Billing | PKG-09 | CHAIN | E8, 7.11 | Duplicate code, bad fee and bad entries are refused with nothing saved; one code-system name is used everywhere (F-028). |
| 139 | E9 Patient Registration Settings: page title "Patient registration requirements" (the... | 1 Setup and sign-in | PKG-14 | CHAIN | E9 | Registration requirements page shows the required fields. |
| 140 | E10 checkpoint backup taken with the API and client running (COPY_ONLY works online):... | 0 Runner and environment | PKG-01 | CHAIN | E10 | The runner takes COPY_ONLY safety-net checkpoints after each stage; the clock/time-zone observation (F-030) is covered by PKG-04. |
| 141 | E8 extra: T9999 "Time test" ($55, owner's value) inactivated: the button asks for a r... | 9 Billing | PKG-09 | CHAIN | E8 extra | A used procedure is inactivated with a reason and stays in the list and history. |
| 142 | Part G section 1, frontdesk1 (private window, role shown "FrontDesk"): sign-in works.... | 13 Permissions and audit | PKG-10 | CHAIN | 1.07, 1.08 | frontdesk1 menu equals the matrix; admin-only entries are absent. |
| 143 | Part G section 1, frontdesk1 follow-up (2026-10-31, owner resumed next morning): Proc... | 13 Permissions and audit | PKG-10 | CHAIN | 1.08 | frontdesk1 sees Procedures & fees read-only; direct admin URLs return permission-denied naming the permission. |
| 145 | Part G section 2, Register patient as frontdesk1: form with Demographics (First name*... | 2 Registration and import | PKG-11 | CHAIN | 2.01, 2.02 | Registration form requires its fields; a clean patient saves (sex and state lists, F-032, F-033). |
| 148 | Part G section 2, near-duplicates (2026-10-31): Alicia Anderson (same DOB, different ... | 2 Registration and import | PKG-11 | CHAIN | 2.03, 2.04 | Near-duplicates are warned with a compare view; override needs a reason (F-036, D-02a). |
| 153 | Part G section 2, results of the follow-up tests (2026-10-31): (a) "Register anyway -... | 2 Registration and import | PKG-02 | CHAIN + REGRESSION | 2.04 | F-037: after sign-out and a different sign-in no previous patient context remains; the override of a duplicate is audited with the matched patient (F-036, PKG-10). |
| 155 | Part G section 2 (admin, 2026-10-31), household removal and full Audit Log review: "R... | 13 Permissions and audit | PKG-10 | CHAIN | 13.01 | Household removal and setup actions appear in the audit log with actor and time; the audit page text matches its content (F-150). |
| 163 | Part G section 3 (admin, 2026-10-31, owner screenshots): (a) Set guarantor: Alicia An... | 2 Registration and import | PKG-11 | CHAIN | 2.13 | Guarantor and household changes show on both records; the patient bar is context-aware (F-034, PKG-14). |
| 167 | Part G section 3, inactivate and reactivate (admin, owner screenshots): "Inactivate p... | 2 Registration and import | PKG-11 | CHAIN | 2.13 | Inactivate and reactivate a patient keep record and history; the dialog wording is app-owned (F-042). |
| 168 | Part G section 3, child patient (admin, owner screenshots): Carla Chen and Daniel Dia... | 2 Registration and import | PKG-11 | CHAIN | 2.08, 2.13 | Child patient with a guarantor and household shows both sides; checkpoint 2 is a runner safety net. |
| 171 | Section 4 - Scheduling (frontdesk1), first booking | 3 Scheduling | PKG-05 | CHAIN | 3.01 | First booking, blank duration takes the default (F-044b) and appears on the calendar; the panel links to the patient (F-045). |
| 184 | Section 4 - Scheduling rules (frontdesk1) | 3 Scheduling | PKG-05 | CHAIN | 3.02, 3.03, 3.05 | Provider, operatory and availability refusals; a blocked time cannot be saved over an existing appointment without listing it (F-053). |
| 223 | Section 4 - Visit board, change room, other roles (2026-10-31 late / 2026-11-01) | 4 Visit flow | PKG-05 | CHAIN | 3.11, 3.12 | Board room/provider change is checked against availability (F-060, F-061); actions per role equal the matrix (F-058, F-059). |
| 233 | Section 4 - Visit board role walk (hygienist1 / frontdesk1), 2026-11-01 00:40-00:55 | 4 Visit flow | PKG-05 | CHAIN | 3.11, 3.12 | Complete cannot skip the matrix transitions; hygienist and frontdesk chains have no dead end (F-063, F-065); stale banners clear (F-066). |
| 244 | Section 4 - Schedule page, double-booking tests (frontdesk1), 2026-11-01 | 3 Scheduling | PKG-05 | CHAIN | 3.02, 3.04, 3.14 | Double-booking refusals on the Schedule page; the list shows status (F-069, F-070, F-071). |
| 255 | Section 4 - Same-patient and board conflict re-test (frontdesk1), 2026-11-01 09:15-09... | 3 Scheduling | PKG-05 | CHAIN + REGRESSION | 3.04, 3.12 | Same-patient overlap is refused and names the conflicting appointment (F-072); board conflict re-test (F-061). |
| 263 | Section 4 - No-show and past-date (frontdesk1), 2026-11-01 ~09:30 | 3 Scheduling | PKG-05 | CHAIN | 3.06, 3.09 | Booking in the past is refused; no-show is available only after the start time (injected clock). |
| 271 | Section 4 - Back-to-back and two-window booking (frontdesk1), 2026-11-01 ~09:50 | 3 Scheduling | PKG-05 | CHAIN + REGRESSION | 3.07, 3.10 | Back-to-back allowed; concurrent same-slot bookings leave exactly one. |
| 279 | Section 4 - No-show via database time shift (frontdesk1), 2026-11-01 10:26 | 3 Scheduling | PKG-05 | CHAIN | 3.01, 3.09, 3.13, 3.14 | No-show through the clock control replaces the manual SQL time shift; history shows actor and slot (F-046, F-062, F-074). |
| 288 | Section 4 - 3.13 calendar filters (frontdesk1), 2026-11-01 | 3 Scheduling | PKG-05 | CHAIN | 3.13 | Calendar filters and Today change the grid; filtered empty grid says so (F-076). |
| 296 | Section 5 - Clinical tab, first pass (dentist1, patient Alice Anderson), 2026-11-01 1... | 5 Clinical | PKG-06 | CHAIN | 4.01 | Clinical tab first use: records appear and the safety banner agrees (F-078). |
| 308 | Section 5 - Safety banner refresh, Safety tab, encounter start (dentist1, Alice), 202... | 5 Clinical | PKG-06 | CHAIN | 4.01, 4.02 | Safety banner refreshes after allergy/medication saves without reload (F-078, F-081). |
| 320 | Section 5 - Encounter note, finalize gate, allergy trap (dentist1, Alice), 2026-11-01... | 5 Clinical | PKG-06 | CHAIN | 4.02, 4.03 | Note autosave and the finalize gate; 'none reported' cannot contradict an active allergy (F-080). |
| 329 | Cross-cutting - patient workspace has no appointments tab (owner question, 2026-11-01... | 3 Scheduling | PKG-14 | CHAIN once the workspace appointments view exists | F-083 | Recorded gap: the patient workspace has no appointments view; the assertion is added with PKG-14. |
| 334 | Section 5b - Encounter steps 1-6 and finalize (dentist1, Alice), 2026-11-01 evening | 5 Clinical | PKG-06 | CHAIN | 4.02, 4.03 | Encounter steps 1 to 6 and finalize; duplicate allergy is decided (F-084). |
| 343 | Section 5c - Safety tab recheck, signed note edit attempt, addendum, new draft (denti... | 5 Clinical | PKG-07 | CHAIN | 4.04, 4.05 | Editing a signed note is refused; the addendum route works; Safety tab agrees with entries (F-085, F-086). |
| 352 | Section 5d - Note sections, encounter history, chart vs encounter, drafts (dentist1, ... | 5 Clinical | PKG-06 | CHAIN | 4.02 | Note sections, encounter history with actor, drafts can be discarded (F-087, F-088). |
| 361 | Section 5e - Confirm list, note templates, safety alert, clearance (dentist1, Alice),... | 5 Clinical | PKG-06 | CHAIN | 4.05, 4.06 | Confirm-list-is-current, templates, alert source required (F-089). |
| 370 | Section 5f - Template in encounter, clearance received, alert change/resolve, confirm... | 5 Clinical | PKG-06 | CHAIN | 4.05, 4.06 | Template in an encounter; clearance received; alert change/resolve; document reference is a picker (F-090). |
| 379 | Section 5g - Clearance resolved, template limits (dentist1, Alice), 2026-11-01 7:05-7... | 5 Clinical | PKG-06 | CHAIN | 4.06 | Clearance resolve asks for a reason and keeps who resolved it. |
| 385 | Section 5h - Required template section, permission checks (dentist1, frontdesk1, bill... | 5 Clinical | PKG-06 | CHAIN + REGRESSION | 4.05, 4.07 | Required template section blocks signing until filled; frontdesk1 and billing1 are refused the Clinical and Safety URLs (stage 13 assertion owned by PKG-10). |
| 392 | Section 5i - Hygienist on the clinical pages (hygienist1 = Harper, Alice), 2026-11-01... | 5 Clinical | PKG-05 | CHAIN | 4.05, 4.08 | Hygienist works on the clinical pages as the matrix allows; required section clears. |
| 401 | Section 5j - Hygienist signs and finalizes, hygienist alert (hygienist1 = Harper, Ali... | 5 Clinical | PKG-06 | CHAIN | 4.08 | Hygienist signs and finalizes; hygienist alert (F-080, F-087, F-089). |
| 410 | Section 5k - Odontogram (dentist1 = Dr. Dana, Alice), 2026-11-01 about 11:19-11:25 PM | 6 Charting | PKG-06 | CHAIN | 5.01, 5.02, 5.03 | Record a finding; empty chart wording; stacked states. |
| 418 | Section 5l - Odontogram states, withdraw, missing-tooth rules (dentist1, Alice), 2026... | 6 Charting | PKG-06 | CHAIN | 5.02, 5.03 | States and withdraw; missing-tooth rules (F-091, F-092, F-093). |
| 428 | Section 5m - Implants, restorations, withdraw reason, treatment-plan link (dentist1, ... | 6 Charting | PKG-06 | CHAIN | 5.03 | Implants and restorations; withdraw reason; plan link (F-094, F-095). |
| 438 | Section 5n - Duplicate filling re-test (dentist1, Alice, tooth 5), 2026-11-02 | 6 Charting | PKG-06 | CHAIN | 5.01 | Repeating an identical restoration gives feedback and no duplicate (F-094). |
| 443 | Enhancement idea E-01 (owner, 2026-11-02): tooth drawings instead of tiles | n/a | - | N/A (product idea E-01, not an executed step) | E-01 | No test: owner idea for tooth drawings; PKG-14 includes a licence check first. |
| 448 | 5o. Periodontal 5.04 to 5.06 and design note (2026-11-02, evening) | 6 Charting | PKG-06 | CHAIN | 5.04, 5.05, 5.06 | Periodontal grid entry, range rejection, session comparison; labels (F-097). |
| 458 | 5p. Diagnosis and chart link (2026-11-02, evening) | 6 Charting | PKG-06 | CHAIN | 5.07 | Diagnosis recorded and linked to a chart; linked-record reference is a picker (F-098). |
| 465 | 5q. Diagnosis history and resolve (2026-11-02, 10:07 PM) | 6 Charting | PKG-06 | CHAIN | 5.07 | Diagnosis history and resolve with a reason; numbering systems labelled (F-099). |
| 472 | 5r. Hygienist diagnosis, 5.08 (2026-11-03, 7:03 AM) | 6 Charting | PKG-05, PKG-06 | CHAIN + REGRESSION | 5.08 | Hygienist can record a diagnosis exactly as the permission matrix allows (permission: PKG-05; the diagnosis record itself: PKG-06); the role refusal on other clinical actions is asserted in stage 13 (PKG-10). |
| 478 | 5s. Treatment plan creation (2026-11-03, 7:55 AM) | 7 Treatment plans | PKG-08 | CHAIN | 6.01, 6.02 | Plan creation needs a current diagnosis and tooth where required. |
| 485 | 5t. Accepting a procedure (2026-11-03, ~8:00 AM) | 7 Treatment plans | PKG-08 | CHAIN | 6.01 | Accepting a procedure records the answer; entered-time is the real time (F-100). |
| 491 | 5u. Completing a planned procedure (2026-11-03, 8:30 AM) | 7 Treatment plans | PKG-08 | CHAIN | 6.04 | Completing needs provider and encounter; link back to the account entry (F-101, F-102). |
| 500 | 5v. Idempotency and the charge (2026-11-03) | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | 6.05, 7.01 | Completing twice creates one charge. |
| 508 | 5w. Completion against a finalized encounter (2026-11-03, ~12:15 PM) | 7 Treatment plans | PKG-07 | CHAIN | 6.04 | Completion against a finalized encounter is refused (F-103). |
| 517 | 5x. Tooth numbering explained, plan form needs tooth (2026-11-03) | 6 Charting | PKG-06 | CHAIN | 6.01 | Universal 14 and FDI 26 shown consistently (F-099, F-104). |
| 526 | 5y. Second completion, other finalized encounter (2026-11-03, ~2:00 PM) | 7 Treatment plans | PKG-07 | CHAIN | 6.04 | Second completion against the other finalized encounter is refused (F-103). |
| 537 | 5z. Odontogram after completions; prescription screens (2026-11-03, ~2:10 PM) | 6 Charting | PKG-06 | CHAIN | 5.01, 6.07 | Chart after completions is accurate (Q-5aj); prescription screens open. |
| 545 | 5aa. Prescription checks, no save yet (2026-11-03, ~2:20 PM) | 8 Prescriptions | PKG-06 | CHAIN | 6.07 | Prescription checks with empty entries list each missing field; nothing saved. |
| 554 | 5ab. Saving a prescription with an allergy override (2026-11-03) | 8 Prescriptions | PKG-06 | CHAIN | 6.07 | Saving against an allergy needs an override reason (F-105). |
| 562 | 5ac. Prescription reload and amendment draft (2026-11-03, ~9:15 PM) | 8 Prescriptions | PKG-06 | CHAIN | 6.08 | Prescription saved once; an amendment starts as a draft. |
| 568 | 5ad. Finalizing an amendment: refusals (2026-11-03, ~9:20 PM) | 8 Prescriptions | PKG-06 | CHAIN | 6.08 | Finalizing an incomplete amendment is refused with the missing fields. |
| 575 | 5ae. Finalizing the amendment (2026-11-03, 9:25 PM) | 8 Prescriptions | PKG-06 | CHAIN | 6.08 | Confirm screen and finalize create a new version and supersede the old (F-106). |
| 581 | 5af. Prescription history and print (2026-11-03, 9:32 PM) | 8 Prescriptions | PKG-12 | CHAIN | 6.08 | History records finalize/supersede; the printed copy has the correct DOB, one sheet, no browser header/footer (F-107, F-108, F-109, F-110). |
| 590 | 5ag. Discontinue and print (2026-11-03, 9:44 PM) | 8 Prescriptions | PKG-12 | CHAIN | 6.08 | Discontinue needs a reason; discontinued prescriptions print with a status mark (F-164). |
| 598 | 5ah. Prescriptions tab after discontinue; checkpoint (2026-11-03, ~9:50 PM) | 8 Prescriptions | PKG-06 | CHAIN | 6.08 | Prescriptions tab shows status of every version (F-106). |
| 603 | 5ai. Hygienist view of treatment and prescriptions (2026-11-03, ~10:00 PM) | 8 Prescriptions | PKG-05 | CHAIN | 6.09 | Hygienist reads but cannot prescribe: no form, 403 from the API. |
| 612 | 5aj. Safety tab as hygienist (2026-11-03, 10:02 PM) | 5 Clinical | PKG-05 | CHAIN | 4.08 | Hygienist Safety tab: alerts and clearances per the matrix (F-058, F-059, F-111). |
| 620 | 5ak. Chart-level record vs encounter entries (2026-11-03, ~10:14 PM) | 5 Clinical | PKG-06 | CHAIN | 4.01 | Chart-level record and encounter entries agree (F-111, F-112). |
| 630 | 5al. Open clearance (2026-11-03, ~10:40 PM) | 5 Clinical | PKG-06 | CHAIN | 4.06 | Open clearance form fields and refusals. |
| 640 | 5am. Completion with an open clearance (2026-11-03, 10:42 PM) | 7 Treatment plans | PKG-08 | CHAIN | 6.04 | Completion with an open clearance follows the decided rule (F-104). |
| 649 | 5an. Billing role view (2026-11-03, ~10:50 PM) | 9 Billing | PKG-09 | CHAIN | 7.01, 2.15 | Billing role sees only its tabs and a read-only Details tab. |
| 656 | 5ao. Recording a payment (2026-11-03, ~11 PM) | 9 Billing | PKG-09 | CHAIN | 7.02, 7.03 | Recording a payment: empty and invalid refusals; valid payment updates the balance. |
| 664 | 5ap. Payment idempotency and receipt preview (2026-11-03, ~11 PM) | 9 Billing | PKG-09 | CHAIN | 7.02, 7.08 | Payment idempotency after reload; receipt preview placement (F-113). |
| 671 | 5aq. Issuing and printing a receipt (2026-11-03, 11:07 PM) | 9 Billing | PKG-09 | CHAIN | 7.08 | Issuing a receipt creates Receipt 1 once; print has one sheet (F-108, F-109). |
| 679 | 5ar. Billing as billing1: second receipt click, reversal (2026-11-04) | 9 Billing | PKG-09 | CHAIN | 7.04, 7.08 | A second receipt click must not create a second receipt for the same payment (F-114). |
| 687 | 5as. Billing as billing1: duplicate receipt, payment reversal (2026-11-04) | 9 Billing | PKG-09 | CHAIN | 7.04 | Duplicate receipt prevented; reversed payment's receipt is marked (F-114, F-115). |
| 695 | 5at. Billing as billing1: payment with chosen charges, refund (2026-11-04) | 9 Billing | PKG-09 | CHAIN | 7.02 | Payment applied to chosen charges; refund records and restricts reverse/correct. |
| 705 | 5au. Billing as billing1: adjustments (2026-11-04) | 9 Billing | PKG-09 | CHAIN | 7.05 | Adjustments need amount and reason; balance and aging reconcile (F-116). |
| 717 | 5av. Billing as billing1: reverse and correct adjustments, statement (2026-11-04) | 9 Billing | PKG-09 | CHAIN | 7.05, 7.06, 7.08 | Reverse/correct adjustments need a reason; statement marks reversed entries (F-117). |
| 726 | 5aw. Billing as billing1: statement print, CSV, timeline, methods (2026-11-04) | 9 Billing | PKG-09 | CHAIN | 7.08 | Statement print, CSV content, timeline and methods (F-118, F-119). |
| 735 | 5ax. Step 53: payment methods, Zelle payment, CSV formula test (billing1) | 9 Billing | PKG-09 | CHAIN | 7.08 | Payment methods: adding a method twice is idempotent and says so; CSV formula text is neutralized (F-120). |
| 744 | 5ay. Step 54: Move payment 8; Household tab (frontdesk1) | 9 Billing | PKG-09 | CHAIN | 7.09 | Move a payment between charges; household tab consistent (F-121, F-122). |
| 753 | 5az. Step 55: timeline after move; Alicia responsibility; frontdesk1 on Billing | 9 Billing | PKG-09 | CHAIN | 7.09, 7.12 | Timeline shows the move; responsibility before first charge; frontdesk billing view. |
| 760 | 6a. Step 56: frontdesk1 receipt/statement preview; Section 8 start (manager1) | 9 Billing | PKG-09 | CHAIN + REGRESSION | 7.12, 8.01 | frontdesk1 can only preview receipts and statements (no Issue); manager1 starts the document stage. |
| 766 | 6b. Step 57: documents 8.03 to 8.05 (manager1) | 10 Documents and storage | PKG-12 | CHAIN | 8.03, 8.04, 8.05 | Documents import, refused files, large file. |
| 774 | 6c. Step 58: notification, edit, inactivate, thumbnails, filters (manager1) | 10 Documents and storage | PKG-12 | CHAIN | 8.06, 8.07, 8.08 | Edit, inactivate, thumbnails, filters; empty-filter wording (F-125). |
| 783 | 6d. Step 59: manager1 admin pages, document settings, storage check | 13 Permissions and audit | PKG-10 | CHAIN | 8.06, 8.09, 8.10, 8.11, 8.12 | manager1 admin pages open as the matrix says; document settings; storage check (Q-5bh). |
| 791 | 6e. Step 60: form templates and signing (8.12) | 10 Documents and storage | PKG-12 | CHAIN | 8.12 | Form template creation, versioning and signing. |
| 799 | 6f. Step 61: signed form in Documents, void, API log review | 10 Documents and storage | PKG-12 | CHAIN | 8.12 | Signed form listed read-only; void needs a reason; API log classification (F-126, F-127, F-128, F-129 in PKG-10). |
| 809 | 6g. Step 62: 8.11 stored file missing | 10 Documents and storage | PKG-12 | CHAIN | 8.11 | Stored file missing: reported Missing, open is never served and fails politely (F-130, F-131). |
| 816 | 7a. Step 63: Section 9 start (manager1) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.02, 9.03, 9.04 | Work queue and My follow-ups start empty; counters equal 0. |
| 821 | 7b. Step 64: follow-ups and work queue | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.02, 9.03, 9.04 | Follow-ups: system tasks are findable (F-132); due times match input (F-133 withdrawn). |
| 831 | 7d. Notifications and Recall settings (2026-11-04) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.01, 10.01, 10.02 | Mark read updates counts at once (F-134); recall settings. |
| 839 | 7e. Arrange this patient's recall (admin, Alice) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.01 | Recall interval bounds; recall changes appear in the timeline and clear stale reasons (F-135, F-136). |
| 847 | 7f. Outside parties (manager1, 9.05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.05 | Outside parties form and required fields. |
| 851 | 7g. Outside parties results (manager1) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.05 | Add lab/specialist; duplicate name refused; make inactive. |
| 857 | 7h. Referral and lab case (manager1, Alice, 2026-11-05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.06 | New referral and lab case; inactive party not offered; tooth validation (F-137). |
| 865 | 7i. Referral status moves and document links (manager1, 2026-11-05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.07 | Status moves and document links; invalid jump not offered. |
| 874 | 7j. Completing referral and lab case (manager1, 2026-11-05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.07 | Completing a referral and a lab case; document history names the person (F-138). |
| 881 | 7k. Remake, unlink, cancel, All cases (manager1, 2026-11-05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.07 | Remake, unlink, cancel and All cases views. |
| 887 | 7l. Step 73: referral overdue path (PASS) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 9.07 | Overdue referral shows an Overdue badge and counters. |
| 893 | 7m. Notifications and Messages, first pass (manager1, 2026-11-05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 10.01, 10.02, 10.03 | Notifications and Messages first pass; layout items (F-139). |
| 901 | 7n. Messages: refusals, urgent, receiving and replying (manager1 and dentist1, 2026-1... | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 10.03 | Message refusals, urgent, receive, reply pre-fills the sender (F-140, F-141, F-142). |
| 912 | 7o. Notifications: mark read, hide, clear history (manager1, 2026-11-05) | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 10.01, 10.02 | Notification mark read, hide, clear history (F-169). |
| 918 | 7p. Data import, patients.csv, up to dry-run (admin, 2026-11-06) | 2 Registration and import | PKG-11 | CHAIN | 11.01, 11.02 | Bulk import upload, column match and dry-run counts; uploader named (F-143). |
| 927 | 7q. Data import commit and checks (admin, 2026-11-06) | 2 Registration and import | PKG-11 | CHAIN | 11.03, 11.04, 11.05 | Duplicate decisions, commit, reconcile, error report (F-144, F-145, F-146). |
| 938 | 7r. Duplicates page and re-upload (admin, 2026-11-06) | 2 Registration and import | PKG-11 | CHAIN | 11.06, 11.11 | Duplicates page and re-upload idempotency; a typo DOB is still caught (F-145). |
| 946 | 7s. Export patient data and Import a patient (admin, Frank Foster, 2026-11-06) | 2 Registration and import | PKG-11 | CHAIN | 11.09, 11.10 | Export package and Import a patient; wording does not imply a round trip (F-147). |
| 955 | 7t. Merge preview and Not a duplicate (admin, 2026-11-06) | 2 Registration and import | PKG-11 | CHAIN | 11.11 | Merge preview shows every field choice (F-148). |
| 961 | 7u. Not a duplicate, result (admin, 2026-11-06) | 2 Registration and import | PKG-11 | CHAIN | 11.11 | Not-a-duplicate requires a reason; suggest again undoes it. |
| 965 | 7v. Reports, admin, range 2026-10-08 to 2026-11-06 (2026-11-06) | 12 Reports and measurement | PKG-13 | CHAIN | 12.01, 12.02, 12.04 | All reports open; frontdesk view of the Schedule report; totals reconcile. |
| 974 | 7w. Reports, empty range and CSV (2026-11-06) | 12 Reports and measurement | PKG-13 | CHAIN | 12.03 | Empty range shows zero; default range. |
| 977 | 7x. Reports, reversed range, Production CSV, Print (2026-11-06) | 12 Reports and measurement | PKG-13 | CHAIN | 12.01 | Reversed range refused; Production CSV and Print. |
| 982 | 7y. Production CSV with data, Audit Log first look (2026-11-06) | 12 Reports and measurement | PKG-13 | CHAIN | 12.02, 13.01 | Production CSV totals equal the ledger; first Audit Log look. |
| 985 | 7z. Audit Log (admin, 2026-11-06) | 13 Permissions and audit | PKG-10 | CHAIN | 13.01, 13.02 | Audit log lists, filters and pages; names shown, not ids (F-149, F-150, F-151). |
| 999 | 7aa. Backup & recovery, first look (13.03, 2026-11-07) | 14 Backup and restore | PKG-12 | CHAIN | 13.03 | Backup page without a key explains the state. |
| 1008 | 7ab. Backup & recovery: key, first backup (13.04, 2026-11-07) | 14 Backup and restore | PKG-12 | CHAIN | 13.04 | Recovery key, first backup, test notification channel stated (F-152, F-153, F-154). |
| 1033 | 7ac. System Status (13.05, first look, 2026-11-07) | 14 Backup and restore | PKG-12 | CHAIN | 13.05 | System Status is truthful (F-149, F-150). |
| 1041 | 7ad. API stopped and restarted (13.05, 2026-11-07) | 14 Backup and restore | PKG-12 | CHAIN | 13.05 | API stopped and restarted: banner, preserved work, recovery (F-155, F-156, F-157). |
| 1059 | 7ae. frontdesk1 and Reports (12.04, 2026-11-07) | 13 Permissions and audit | PKG-10 | CHAIN + REGRESSION | 12.04 | frontdesk1 and Reports follow the matrix (F-158, F-159 in PKG-14). |
| 1071 | 7af. Section 14 usability checks as frontdesk1 (2026-11-07) | n/a | PKG-14 | REGRESSION (axe, viewport, zoom, keyboard, timing) + MANUAL-ONLY (readability) | 14.a to 14.g | Section 14 usability checks: see 6.2. |
| 1086 | 7ag. Prescription finalize and cancel as dentist1, 2026-11-07 | 8 Prescriptions | PKG-12 | CHAIN | 6.08 | Prescription finalize and cancel; cancel print rule (F-164, F-165, F-166). |
| 1098 | 7ah. Hygienist and alert resolution, 2026-11-07 (step 113, partial) | 5 Clinical | PKG-05 | CHAIN | 4.06, 4.08 | Step 113: hygienist1 resolves an alert; dentist1 sees the same state; the banner no longer lists it; hygienist sees Add an alert and Request a clearance but no Billing tab (matrix). |
| 1107 | Step 114 result: Resolved alerts detail PASS. "Hygiene test alert" (Low, custom) was ... | 5 Clinical | PKG-06 | CHAIN | 4.06 | Step 114: resolved-alert detail shows creator, change (Moderate to High), resolver and reasons with a two-line history; a resolved High alert is not drawn as urgent (F-167, F-168 styling in PKG-14). |
| 1108 | Notifications as hygienist1 PASS. Two unread from Maya (11/5 9:02 PM, and 9:06 PM mar... | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 10.01, 10.02 | Step 114 notifications (hygienist1): mark read lowers the badge and the urgent box stays while an urgent notice remains. |
| 1112 | Step 115 results (hygienist1): | 11 Follow-ups, referrals, notifications | PKG-13 | CHAIN | 10.02, 10.03 | Step 115 (hygienist1): opening a message marks its notice read; the badge and urgent box clear after leaving; Hide is one-way and says so or can be undone (F-169). |
| 1118 | Step 116 results: | 5 Clinical | PKG-05 | CHAIN | 4.06, 4.08 | Step 116: hygienist1 reopens and resolves the dentist's alert as the matrix allows; history lines record actor and reason; the earlier lines stay untouched. |
| 1120 | manager1 on Alice: Details, Household & guarantor, History, Forms, Documents, Follow-... | 13 Permissions and audit | PKG-10 | CHAIN | 2.15, 1.07 | Step 116 (manager1 on Alice): tabs are Details, Household, History, Forms, Documents, Follow-ups, Referrals and Billing only; no Clinical, Safety or chart tabs and no safety banner; menu entries match the matrix. |
| 1122 | Step 117 results (manager1): | 13 Permissions and audit | PKG-10 | CHAIN | 1.09 | Step 117 (manager1): the Permission Matrix and the delegation table show only what the viewer may do; matrix cells equal real access; Q-5cn clinical-document visibility is asserted in PKG-12. |
| 1128 | Step 118.1 result, F-170 RESOLVED as by design (delegation): manager1 clicked Allow o... | 13 Permissions and audit | PKG-10 | CHAIN + REGRESSION | 1.09 | Step 118.1: manager1's delegated grant of ImportDocuments needs a reason and confirmation and changes only what the delegate holds; Change history names the actor (F-172); boundary and audit verification of the delegation model is owned by PKG-05 (F-170). |
| 1132 | Step 119 results (grant effect and revert): | 10 Documents and storage | PKG-12 | CHAIN + REGRESSION | 8.01, 13.01 | Step 119: with the grant assistant1 can import; Edit/Inactivate follow the viewer's own rights, not the import grant (F-173); Stop removes the form; audit records PermissionGranted, DocumentImported, PermissionReverted. |
| 1136 | Step 119.4, manager1 URLs: /admin/audit-log opens (matrix says ViewAuditLog default f... | 13 Permissions and audit | PKG-10 | CHAIN | 1.08 | Step 119.4: manager1's admin URLs follow the matrix (Audit Log opens, Backup is read-only) and the menu agrees (F-163, F-174). |
| 1140 | Step 120 results: | 9 Billing | PKG-09 | CHAIN | 7.01, 12.02 | Step 120: manager1's Billing view equals the ledger; balance and aging reconcile with adjustments shown separately (F-175, F-176); Schedule report counts equal the appointments (9 = 7 + 1 + 1). |
| 1149 | 7ai. Treatment plan tab as dentist1 (Alice), 2026-11-07 (step 121, look only) | 7 Treatment plans | PKG-08 | CHAIN | 6.01 | Step 121: Treatment plan tab lists the four plans with their actions; plan label follows its items (F-177); empty-plan actions hidden (F-178); stale banner removed (F-179). |
| 1157 | Step 122 PASS: "Adopt earlier completions..." on all four plans opens a panel saying ... | 7 Treatment plans | PKG-08 | CHAIN | Step 122 | Step 122: Adopt earlier completions opens the explanation 'nothing to adopt' or is hidden (F-180); the submit path is second pass. |
| 1158 | Step 123 (Rename plan, Set reference code on Gingivitis care plan): | 7 Treatment plans | PKG-08 | CHAIN | Step 123 | Step 123: Rename plan (empty title refused, revision created) and Set reference code (revision history shows it, F-181); F5 keeps both. |
| 1165 | Step 124 results: | 7 Treatment plans | PKG-08 | CHAIN | Step 124 | Step 124: Review old diagnosis references reports nothing to tie; Compare revisions shows Before/After on separate lines (F-182); the patient copy shows the plan to the patient (F-183). |
| 1169 | Step 125 (new plan "Accept test", diagnosis Gingivitis, L1000 Exam $75.00; Accept all... | 7 Treatment plans | PKG-08 | CHAIN | Step 125 | Step 125: new plan 'Accept test' is created; Accept all pending records the answer with an entered-by time that is kept (F-184); answers appear in history (F-190). |
| 1175 | Step 126 (Schedule L1000 on "Accept test"): my step was wrong. The form says "This re... | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | Step 126 | Step 126: Schedule L1000 records a planned date, does not book an appointment, and does not close the unscheduled-treatment follow-up (F-185). |
| 1181 | Step 127 (Change schedule, Withdraw item, Withdraw plan on "Accept test"): | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | Step 127 | Step 127: Change schedule, a blank date cannot schedule (F-188), Withdraw item and Withdraw plan with reasons; Show withdrawn plans lists them; empty plan hides accept/decline (F-186, F-187). |
| 1191 | Step 128 (new plan "Revise test", Gingivitis, L1000 Exam): | 7 Treatment plans | PKG-08 | CHAIN | Step 128 | Step 128: new plan 'Revise test'; Add procedure creates a revision; revision history; Revise plan form fields. |
| 1198 | Step 129 (plan "Revise test": single accept, Decline all pending on a partly accepted... | 7 Treatment plans | PKG-08 | CHAIN | Step 129 | Step 129: single accept; Decline all pending on a partly answered plan is refused with its message (F-189). |
| 1203 | Step 130 (single-item Decline and changing answers, plan "Revise test"): | 7 Treatment plans | PKG-08 | CHAIN | Step 130 | Step 130: decline and change answers both ways; the history keeps every answer with its time (F-190, F-191). |
| 1211 | Step 131 (audit log as admin, 11/8/2026, plan events). Present and correctly timed: T... | 13 Permissions and audit | PKG-10 | CHAIN | Step 131 | Step 131: plan events in the audit log correctly timed; rows name patient, plan and code (F-192); schedule/unschedule events exist; the follow-up closing rule is asserted in PKG-08. |
| 1221 | 7aj. Billing tab as billing1 (Alicia Anderson), 2026-11-08 | 9 Billing | PKG-09 | CHAIN | Step 132 | Step 132 (billing1, Alicia): household tab is read-only; Billing sections (responsible, ready to bill, history, statements, timeline); payment methods placement and button styling (F-193, F-194, F-195). |
| 1232 | Step 133 (Alice, Billing tab as billing1). Alice has the 16-entry history from the 11... | 9 Billing | PKG-09 | CHAIN | Step 133 | Step 133: Alice Billing tab with her 16-entry history. |
| 1238 | Step 134 (Alice, Record responsibility). The dropdown offers only "The patient" (Alic... | 9 Billing | PKG-09 | CHAIN | Step 134 | Step 134: Record responsibility offers only the patient when none other exists (F-197). |
| 1242 | Step 135: Daniel's Billing tab: balance $0.00, "Who is responsible" names Carla Chen ... | 9 Billing | PKG-09 | CHAIN | Step 135 | Step 135: Daniel's Billing tab shows Carla as responsible. |
| 1243 | Step 136 (dentist1, Daniel, Treatment plan tab): the plan cannot be created: "This pa... | 5 Clinical | PKG-06 | CHAIN | Step 136 | Step 136: Daniel has no diagnosis, so no plan can be created. |
| 1245 | Step 137 (dentist1, Daniel, Diagnoses tab): no form. "This patient has no encounter y... | 5 Clinical | PKG-06 | CHAIN | Step 137 | Step 137: Daniel has no encounter, so no diagnosis form. |
| 1247 | Step 138 (dentist1, Daniel, Clinical tab): four cards (Medical history, Dental histor... | 5 Clinical | PKG-06 | CHAIN | Step 138 | Step 138: Daniel's Clinical tab cards. |
| 1251 | Step 139: shortcut chosen: give Alice (who has the billed ledger) a second party inst... | 9 Billing | PKG-09 | CHAIN | Step 139 | Step 139: second party for Alice (shortcut for the guarantor test). |
| 1255 | Step 140 (frontdesk1): Alicia "Make self-responsible": green message "Patient is now ... | 9 Billing | PKG-09 | CHAIN | Step 140 | Step 140: make Alicia self-responsible; message names the people (F-204). |
| 1257 | Step 141 (billing1, Alice Billing after guarantor change): "Who is responsible" STILL... | 9 Billing | PKG-09 | CHAIN | Step 141 | Step 141: responsible party text after the guarantor change. |
| 1258 | Step 142 (billing1, Alice): chose "Carla Chen (registered guarantor)", Why "Carla agr... | 9 Billing | PKG-09 | CHAIN | Step 142 | Step 142: responsibility to Carla with a reason. |
| 1260 | Step 143 (billing1, Alice): "Who paid" list offers "The patient" and "Carla Chen (res... | 9 Billing | PKG-09 | CHAIN | Step 143 | Step 143: Who paid offers the patient and the responsible guarantor per PD-18 (F-209, F-205). |
| 1265 | Step 144 (billing1, Alice, receipts): Issue this receipt on payment 17 gave "Receipt ... | 9 Billing | PKG-09 | CHAIN | Step 144 | Step 144: receipts on payment 17; stale status line (F-207, F-208). |
| 1270 | Step 145 (billing1, Alice): chose "The patient", Why "Patient will pay herself", Reco... | 9 Billing | PKG-09 | CHAIN | Step 145 | Step 145: record the patient again as responsible is refused or explained (F-197). |
| 1272 | Step 146 (billing1, Carla Chen, DOB 1992-07-21): Household & guarantor: "This patient... | 2 Registration and import | PKG-11 | CHAIN | Step 146 | Step 146 (billing1, Carla Chen): two Carla Chen records with the same phone and different DOB exist; duplicate detection and the guarantor picker tell them apart (F-210, F-211, F-145). |
| 1280 | Step 147 (F-127, API window pasted by owner, 2026-11-08): this API window is a LATER ... | 13 Permissions and audit | PKG-10 | REGRESSION | Step 147 | Step 147: API log content classification test for SqlException and refused writes (F-127). |
| 1285 | Step 148 (Permission Matrix as admin, 3 screenshots, 2026-11-08): the table is wider ... | 13 Permissions and audit | PKG-10 | CHAIN | Step 148 | Step 148: Permission Matrix as admin (63 rows by 8 roles); sticky header and non-default tags are PKG-14 (F-213, F-215). |
| 1294 | Step 149 (assistant1 and manager1 on Alice Anderson, 2026-11-08): PASS, matches the m... | 13 Permissions and audit | PKG-10 | CHAIN | Step 149 | Step 149: assistant1 and manager1 views match the matrix. |
| 1300 | Step 150 (assistant1, Prescription composer, 2026-11-08): PASS, Q-5cv ANSWERED. The t... | 8 Prescriptions | PKG-05 | CHAIN | Step 150 | Step 150: assistant1 sees the Prescription composer read-only. |
| 1302 | Step 152 (admin, Reports > Measurement and baselines, before any baseline, 2026-11-08... | 12 Reports and measurement | PKG-13 | CHAIN | Step 152 | Step 152: Measurement empty state. |
| 1303 | Step 153 (admin, Set a baseline, 2026-11-08): PASS x3. (1) 11/01 to 11/08 (today): re... | 12 Reports and measurement | PKG-13 | CHAIN + REGRESSION | Step 153 | Step 153: baseline refusals and save; duplicate baseline refused (F-216). |
| 1304 | Step 154 (admin, Measurement, 2026-11-08): | 12 Reports and measurement | PKG-13 | CHAIN | Step 154 | Step 154: comparison only when allowed; repeat save and reload. |
| 1309 | Step 155a (dentist1, Revise test, L1000, 2026-11-08): Take off the schedule PASS. Sta... | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | Step 155a | Step 155a: Take off the schedule restores the prior state with history. |
| 1312 | Step 155b (dentist1, plan "Revise test", 2026-11-08): | 7 Treatment plans | PKG-08 | CHAIN | Step 155b | Step 155b: revise needs a reason; phase validation; rename (F-219). |
| 1319 | Step 156/157 (dentist1, 2026-11-08): | 7 Treatment plans | PKG-08 | CHAIN | Step 156, 157 | Steps 156-157: revision history and phase save. |
| 1324 | Step 158 (dentist1, Revise test v3, 2026-11-08): | 7 Treatment plans | PKG-08 | CHAIN | Step 158 | Step 158: compare revisions; blank 'who gave this answer' is accepted. |
| 1328 | Step 159 (dentist1, 2026-11-08): | 7 Treatment plans | PKG-08 | CHAIN + REGRESSION | Step 159 | Step 159: patient copy; past planned date follows PD-15 (F-221). |
| 1332 | Step 160 (dentist1, Clearance test, Adopt earlier completions…, 2026-11-08): the butt... | 7 Treatment plans | PKG-08 | CHAIN | Step 160 | Step 160: Adopt earlier completions box on a plan with nothing adoptable (F-222). |
| 1333 | Step 161 / case 13.06 (restore, 2026-11-08): PASS. | 14 Backup and restore | PKG-12 | CHAIN | Step 161, 13.06 | Step 161: restore the checkpoint; snapshot options; orphan-file report (Q-5cw, Q-5cx). |
| 1344 | Session timeline | n/a | - | N/A (session timeline, not a test step) | A1 | Startup observations; covered by the S-xx rows above. |
| 1350 | Not yet reviewed | n/a | - | N/A (empty placeholder) | - | - |

### 6.5 Manual-only checks (justified and narrow)

- Dark-theme and contrast judgment, print-preview appearance, layout feel, thumbnails look (F-096, F-108, F-109, F-139, F-161-F-163, F-176, F-194): automated checks (axe, viewport, zoom) cover what is objective; judging whether it looks right stays manual.
- The mid-run .NET runtime update behind F-015: cannot be scripted; covered by a release-engineering note and by PKG-15's after-login failure message.
- Case 1.11 'note how the flow reads' (Multi-Factor enrolment wording).
- The owner's final manual E2E before release (this is by design a human gate).
- Anything the owner designates as 'subjective workflow assessment'.

### 6.6 Tracked-exception register

- Entry: finding id, package, owner, exit condition, blocking yes/no.
- Release-blocking set at the time of writing: F-021, F-037, F-107, F-118, F-103, F-185/F-188/F-221, F-114, F-155/F-014, F-053/F-061/F-071, F-059/F-063/F-065, F-078/F-086, F-084/F-085/F-111.
- PKG-01 builds the register and the runner reads it; PKG-16 requires no blocking entry.

## 7. My notes on what was missed, left over or needs the reviewer's attention (updated for R04)

**A. Errors in my own earlier work**
1. F-037 was missing from my first triage; the reviewer caught it. It is the first part of PKG-02.
2. I listed F-199 as a separate fix although it was withdrawn as a duplicate of F-067, and F-127 appeared in both my fix and close lists. Corrected in the dispositions.
3. In R01 I turned a log recommendation (extend session) into an acceptance criterion; removed. In R01 I treated F-015 as an auth/health defect; it is NOT_REPRODUCED / INVESTIGATE. I buried the silent 60-minute default under F-044; it is F-044b.
4. R01 had broad stories and a long decision list; R02 overcorrected to 34 stories and treated four reviewer recommendations as settled. R03 consolidates to 16 packages and restores PD-15, PD-17, PD-18 and PD-25 as owner decisions.
5. R02 carried a stale statement that recording ALV-N007/N012 as deferred still waited. It was already recorded in `.alveara/EXECUTION_STATUS.json` (commit 0a14b66). Withdrawn; my own notes had the same stale item and are corrected.
6. R02's treatment-plan work depended only on date correctness; a real linked appointment relies on PKG-05. Corrected.

**B. Items the reviewer responses still do not mention individually**
- F-107 and F-118 (PKG-04); F-145/F-210/F-211 (PKG-11); F-098/F-090 (PKG-06); F-216/F-217 (PKG-13); F-032/F-033 (PKG-11); F-087 (PKG-10); F-100 (PKG-04); F-130/F-131 and F-173 (PKG-12); F-134 to F-143 (PKG-13); all Q-5x questions and O-x observations (Appendices B and C).

**C. Points where I read the evidence differently, or want a ruling**
- F-044/F-044b: the log says the duration box was not pre-filled and a blank silently meant 60 minutes; the decision on the silent default is F-044b inside PKG-05.
- F-164: the reviewer prefers a clear status mark for historical printing; the owner had asked about blocking. PKG-12 follows the reviewer, with the final wording decided there.
- F-170: kept as intentional delegation; a verification task remains in PKG-05.
- PKG-02 combines patient-context isolation with session/outage behavior as two ordered groups because both change client identity and session state. If the reviewer wants F-037 closed completely alone, PKG-02 splits into two and the count becomes 17.
- Four decisions (PD-15, PD-17, PD-18, PD-25) carry an agent recommendation; they remain the owner's.

**D. Left over / not done**
1. Not tested in the first pass (second pass): Q-5cl, Adopt earlier completions (submit), tying old diagnosis references, a successful Add procedure, Q-5cx (10 files on disk, 8 listed in Document Storage).
2. Stale lines in the manual guides (61 migrations and 130 tables, tab list, 'Practice code', sqlcmd -I, COPY_ONLY does not need the API stopped) are corrected in PKG-15; this coverage map replaces the guides as the test-source of record.
3. Screenshots: the screens folder was empty; evidence is the text in the log and the owner's screenshots in the working conversation.
4. The database checkpoints (`checkpoint-*.bak`) are not in the package.
5. I have not checked which existing automated tests already cover these findings; that is part of each package's verify-first step.
6. The execution plan and `.alveara/EXECUTION_STATUS.json` have not been changed by this work; they are updated only after this plan is independently approved.
7. The ALV-N013 requirement to enable ALLOW_SNAPSHOT_ISOLATION on Windows installs and upgrades stays with release engineering.
8. The segment mapping in 6.4 is keyword-based and 74 test cases have no single log line carrying their id; both are stated in the tables.
9. Nothing was committed, pushed or uploaded; the plan files are untracked.

## 8. Appendix A: disposition of every finding and design note (raw ids kept)

| Raw id | Severity (as logged) | Disposition | Home package | Raw-log line | Excerpt from the log | Note |
|---|---|---|---|---|---|---|
| F-001 | Q | CONFIRMED | PKG-15 | 10 | Build prints NU1902 (moderate) and NU1903 (high): package System.Security.Cryptography.Pkcs 9.0.18 has known vulnerabilities (GHSA-2fmv-rv2c... | observed in the manual pass; verify in code first |
| F-002 | Q | CONFIRMED | PKG-15 | 11 | Nullable warnings CS8604/CS8714/CS8621 in Architecture/Imports/ImportPatientRows.cs(63) and ImportCommitEngine.cs(129). Bulk import code (te... | observed in the manual pass; verify in code first |
| F-003 | Q | CONFIRMED | PKG-15 | 12 | EF1002 raw-SQL-with-interpolation warnings in PatientMergeService.cs (163, 174, 176) and PatientUnmergeService.cs (120). Need reading to say... | observed in the manual pass; verify in code first |
| F-004 | S3 | CONFIRMED | PKG-15 | 13 | npm reports a new major version: npm 11.17.0 -> 12.2.1 (npm install -g npm@12.2.1). Tooling update only; not applied. To be planned (check t... | observed in the manual pass; verify in code first |
| F-005 | Q | CONFIRMED | PKG-15 | 14 | npm audit reports 5 vulnerabilities in the client dependencies (2 moderate, 3 high). Not yet looked at: which packages, whether dev-only or ... | observed in the manual pass; verify in code first |
| F-006 | S3 | NOT_REPRODUCED | PKG-15 | 33 | Vite printed "Re-optimizing dependencies because vite config has changed" on start. Probably normal after a config change; noted only to con... | did not repeat; close as a one-off |
| F-007 | S3 | CONFIRMED | PKG-02 | 15 | A visitor with no session sees "Your session expired. Please sign in again." (URL /login?reason=expired) even though they never signed in. B... | observed in the manual pass; verify in code first |
| F-008 | S2 | CONFIRMED | PKG-14 | 16 | The Dashboard is still the ALV-N001 shell placeholder: header text "The primary workspace region. Real content arrives with later feature st... | observed in the manual pass; verify in code first |
| F-009 | S3 | CONFIRMED | PKG-15 | 17 | docs/RUNNING_LOCALLY.md (step 4) says the initialize script applies "93 tables, 42 migrations at the time of writing". The database actually... | observed in the manual pass; verify in code first |
| F-010 | S3 | DUPLICATE | PKG-10 | 18 | API console showed fail: Alveara.Api.Architecture.Logging.SafeExceptionHandlingMiddleware[0] Unhandled request exception: TaskCanceledExcept... | of F-126 |
| F-011 | S3 | CONFIRMED | PKG-15 | 19 | The refusal is clear, but the scripts report it with throw, so the plain message is followed by a PowerShell stack block (At ...Start-DevApi... | observed in the manual pass; verify in code first |
| F-012 | S3 | NOT_REPRODUCED | PKG-02 | 20 | For about 10-15 s after the page loads, the login page gives no sign that the server is unreachable and, with F-007, even says "Your session... | delay not explained by the code; re-time before calling it a defect |
| F-013 | Q | DESIGN_DECISION_REQUIRED | PKG-02 | 21 | The banner says "Changes cannot be saved until the connection is restored. This is not an offline mode - reconnect to continue." (Disconnect... | meaning of 'offline' |
| F-014 | S2 | CONFIRMED | PKG-02 | 22 | With the API down, typing admin and the password and clicking Sign in shows the red message "Invalid username or password." (screenshot: it ... | observed in the manual pass; verify in code first |
| F-015 | S1 | NOT_REPRODUCED | PKG-02 | 23 | On the freshly created database (61 migrations, admin created by New-DevAdmin.ps1) signing in with the correct password does not work: the l... | INVESTIGATE: occurred around a mid-run runtime update; not a confirmed production defect (review R01); reproduce with preserved evidence |
| F-016 | Q | DESIGN_DECISION_REQUIRED | PKG-14 | 24 | There is no screen to create a staff account. The login page has no "register" link and Security Administration has no "add user"; accounts ... | onboarding model |
| F-017 | S2 | DESIGN_DECISION_REQUIRED | PKG-02 | 25 | The shell showed the amber notice "Your session will expire soon. Save your work and sign in again to continue." There is no way to extend t... | session extension: NOT authorized by this plan; fixed expiry is an intentional control; the owner may choose separately |
| F-018 | S3 | CONFIRMED | PKG-14 | 26 | The "User details" page (/admin/users/<id>, AdminUserDetailPage.tsx) has no way back to the list: no "Back to users" link, no breadcrumb, an... | observed in the manual pass; verify in code first |
| F-019 | S3 | CONFIRMED | PKG-14 | 27 | Time zone and Currency cannot be changed and look like ordinary editable boxes (same styling as the editable fields; only small grey hint te... | observed in the manual pass; verify in code first |
| F-020 | S2 | CONFIRMED | PKG-14 | 28 | Clicking "Add location" (top right of the Location section) appears to do nothing: the Add form opens BELOW the list, off the visible screen... | observed in the manual pass; verify in code first |
| F-021 | Q | CONFIRMED | PKG-03 | 29 | Owner linked login dentist1 to a staff profile, then deactivated the profile. Observed: the account stays linked to the inactive profile and... | observed in the manual pass; verify in code first |
| F-022 | S3 | CONFIRMED | PKG-14 | 30 | The Dentist's left menu contains "Component Showcase" (/showcase), a developer page for UI components, and "System Status" and "Messages", w... | observed in the manual pass; verify in code first |
| F-023 | Q | DESIGN_DECISION_REQUIRED | PKG-14 | 31 | The "Staff member" box offers every active staff member who is not yet a provider, including non-clinical staff (Alex Assistant, Bill Billin... | provider eligibility |
| F-024 | Q | DESIGN_DECISION_REQUIRED | PKG-14 | 32 | Specialty is a required free-text box (up to 80 characters), so "General dentistry", "general dentist", "Hygiene" and "Hygienist" are all di... | specialty values |
| F-025 |  | CONFIRMED | PKG-14 | 131 | , low severity). Recommendation: clear the row error whenever the rows change or Save succeeds/ reverts to the saved state. Observation O-E6... | observed in the manual pass; verify in code first |
| F-026 | design | DESIGN_DECISION_REQUIRED | PKG-14 | 133 | (1) the name promises a preview but shows only configuration; (2) blocked time (2026-11-02 12:00-13:00) is not shown, so effective hours are... | name and content of the tab |
| F-027 |  | CONFIRMED | PKG-14 | 147 | addendum (owner): the "Add a procedure" button sits at the END of the list, so the owner had to scroll to the bottom every time, and the for... | observed in the manual pass; verify in code first |
| F-028 | low | CONFIRMED | PKG-14 | 137 | the code system is called "Practice code" in the form, list and filter, but the duplicate-code banner calls it "the Local code system". Reco... | observed in the manual pass; verify in code first |
| F-029 | low | CONFIRMED | PKG-10 | 138 | "Fee and change history" for L1000 shows "Version 1: $75.00 from Oct 30, 2026 - Exam 10/30/2026, 3:38:45 PM by Staff member" and "Created 10... | observed in the manual pass; verify in code first |
| F-030 |  | CONFIRMED | PKG-04 | 140 | candidate, high importance for scheduling). RESULT: test procedure T9999 "Time test" added at PC clock 8:54:10 PM; its history shows 10/30/2... | observed in the manual pass; verify in code first |
| F-031 | medium | CONFIRMED | PKG-14 | 142 | "Component Showcase" (a developer page) and the Dashboard placeholder "No feature modules have been built on top of this shell yet. This is ... | observed in the manual pass; verify in code first |
| F-032 | low | CONFIRMED | PKG-11 | 146 | the Sex field on Register patient is a free-text box. It should be a drop-down list (a fixed list of values, plus "unspecified"), so reports... | observed in the manual pass; verify in code first |
| F-033 | low | CONFIRMED | PKG-11 | 150 | State is a free-text box. When the country is USA (the practice default), State should be a drop-down of the 50 states plus DC and territori... | observed in the manual pass; verify in code first |
| F-034 | medium | DESIGN_DECISION_REQUIRED | PKG-14 | 151 | the always-visible patient bar at the top (patient name, DOB and age, sex, phone, plus links "Open workspace" and "Close patient") is not co... | patient bar behavior |
| F-035 | medium | CONFIRMED | PKG-14 | 152 | on the Register patient page the bar still shows "Alice Anderson DOB 1985-03-14 (41 y) Female 555-010-0101" while the user is typing a DIFFE... | observed in the manual pass; verify in code first |
| F-036 |  | CONFIRMED | PKG-10 | 160 | detail: the override entry does not say WHICH patient it was compared against, and carries no reason; add the matched patient ID(s) and a re... | observed in the manual pass; verify in code first |
| F-037 | HIGH | CONFIRMED | PKG-02 | 154 | the selected patient and the open patient page survive a change of user. The next user lands on the previous user's patient record, which ex... | observed in the manual pass; verify in code first |
| F-038 | HIGH | DESIGN_DECISION_REQUIRED | PKG-10 | 157 | NO patient-record access events. frontdesk1 and admin both opened Brian's workspace and Alice's record, and nothing was logged; only registr... | separate audit/compliance design; no logging assumed |
| F-039 | medium | DESIGN_DECISION_REQUIRED | PKG-10 | 158 | sign-out and session expiry are not recorded (only LoginSucceeded/LoginFailed exist). A session cannot be shown to have ended, and with F-03... | separate audit/compliance design |
| F-040 | medium | CONFIRMED | PKG-10 | 163 | the patient History has no "changed by" column (who changed it) and no reason, and the household rows say only "Set"/"None" without naming t... | observed in the manual pass; verify in code first |
| F-041 | medium | CONFIRMED | PKG-10 | 166 | the PatientUpdated entry names the field ("phone") but not the old and new values; for a demographic change the audit should hold before and... | observed in the manual pass; verify in code first |
| F-042 | low | CONFIRMED | PKG-11 | 167 | inactivating a PATIENT asks no reason, while inactivating a procedure does; a reason (list plus free text, e.g. moved away, deceased, duplic... | observed in the manual pass; verify in code first |
| F-043 | medium | CONFIRMED | PKG-11 | 168 | nothing marks Daniel as a minor (no "Minor" badge, no consent or guardian-signature prompt, no rule that a minor must have a guarantor/guard... | observed in the manual pass; verify in code first |
| F-044 |  | WITHDRAWN | PKG-05 | 173 | WITHDRAWN: owner typed 30 in the duration box; it was not pre-filled. Blank duration correctly gave 60 min (Alicia's booking). Not a defect. | original prefill defect withdrawn; the silent 60-minute default is the separate key F-044b |
| F-045 | S3 | CONFIRMED | PKG-05 | 174 | Appointment panel has no "Open patient" link, and the top bar still says "No patient selected" while the panel shows Brian Baker. Ties to F-... | observed in the manual pass; verify in code first |
| F-046 | S3 | CONFIRMED | PKG-05 | 175 | Appointment History says "Scheduled for 30 minutes." but not who booked it. Same gap as F-040/F-041. Recommendation: add "by <user>" to each... | observed in the manual pass; verify in code first |
| F-047 | Q | CONFIRMED | PKG-05 | 176 | "Check in" is offered on an appointment 4 days in the future. Recommendation: offer it only on the appointment day, or ask for confirmation. | check-in timing; decided in story design |
| F-048 | S3 | CONFIRMED | PKG-05 | 177 | Calendar opened on a Saturday (today) is fully shaded with no message. Recommendation: show "No working hours for any provider on this day" ... | observed in the manual pass; verify in code first |
| F-049 | S3 | CONFIRMED | PKG-05 | 193 | Reschedule History row records only the OLD slot ("Was ... 09:00 ... Operatory 1"), not the new one or who changed it. Recommendation: "From... | observed in the manual pass; verify in code first |
| F-050 | S3 | DESIGN_DECISION_REQUIRED | PKG-05 | 194 | Cancel reason is free text only. Reports cannot separate patient-cancelled from office-cancelled or short-notice. Recommendation: add a reas... | cancel reason categories |
| F-051 | S3 | CONFIRMED | PKG-05 | 195 | A cancelled appointment panel has no "Rebook" or "Reschedule" action and no "Open patient" link. | observed in the manual pass; verify in code first |
| F-052 | S3 | CONFIRMED | PKG-05 | 196 | Week view shows no shading for non-working hours or blocked time (Day view does shade non-working hours). Recommendation: shade in Week view... | observed in the manual pass; verify in code first |
| F-053 |  | CONFIRMED | PKG-05 | 197 | CONFIRMED (S2): Admin added a "Training" block 2026-11-04 10:00-10:30 directly over Brian's existing checked-in 10:00-10:30 appointment. No ... | observed in the manual pass; verify in code first |
| F-054 | S3 | CONFIRMED | PKG-05 | 200 | In Week view, two overlapping-in-time appointments (different providers) are drawn side by side and truncated to "10:00-10:" with no name, s... | observed in the manual pass; verify in code first |
| F-055 | S3 | CONFIRMED | PKG-05 | 201 | After check-in the panel shows both "Scheduled" and "Checked in" status chips; History shows the raw value "Scheduled -> CheckedIn"; label "... | observed in the manual pass; verify in code first |
| F-056 | S2 | DESIGN_DECISION_REQUIRED | PKG-05 | 202 | After check-in both "Start treatment" and "Complete treatment" are enabled. Does "Complete" skip the in-progress step by design? To test. | complete without start: decided in the visit matrix |
| F-057 | S3 | DESIGN_DECISION_REQUIRED | PKG-05 | 203 | There is no way to undo a check-in (owner searched the panel). Recommendation: allow "Undo check-in" before treatment starts, recorded in Hi... | undo check-in |
| F-058 | S3 | CONFIRMED | PKG-05 | 208 | Calendar panel and Visit board give different actions for the same appointment. Calendar: Check in, Start treatment, Complete treatment. Boa... | observed in the manual pass; verify in code first |
| F-059 | S2 | CONFIRMED | PKG-05 | 209 | FrontDesk user (frontdesk1) can start and complete treatment, a clinical status. Check against the Permission Matrix whether FrontDesk is me... | observed in the manual pass; verify in code first |
| F-060 | S2 | CONFIRMED | PKG-05 | 217 | "Change room or provider" on the board opens an inline Provider/Operatory form with the note "This changes where the patient is seen today. ... | observed in the manual pass; verify in code first |
| F-061 | S2 | CONFIRMED | PKG-05 | 225 | Board "Change room or provider" as frontdesk1 SAVED Carla (Cleaning, 10:00-10:45) onto Dr. Dana + Operatory 1 with no warning ("Carla Chen w... | observed in the manual pass; verify in code first |
| F-062 | S3 | CONFIRMED | PKG-05 | 226 | Move to Harper + Operatory 2 also saved. Calendar panel then shows "Provider Harper / Operatory Hygiene 1" (booking) and a new row "Seen by,... | observed in the manual pass; verify in code first |
| F-063 | S2 | CONFIRMED | PKG-05 | 227 | assistant1 AND hygienist1 see identical board buttons on a Checked in card: Mark ready, Start treatment, Complete visit, Change room or prov... | observed in the manual pass; verify in code first |
| F-064 | S3 | CONFIRMED | PKG-05 | 229 | assistant1 and hygienist1 have exactly the same menu: Dashboard, Patients, Calendar, Visit board, Schedule, Work queue, My follow-ups, Refer... | observed in the manual pass; verify in code first |
| F-065 | S2 | CONFIRMED | PKG-05 | 236 | role split makes a dead end at the end of the chain. hygienist1 cannot Confirm or Check in (Scheduled card only offers Change room or provid... | observed in the manual pass; verify in code first |
| F-066 | S3 | CONFIRMED | PKG-05 | 239 | the stale banner text "Brian Baker is now in treatment." stayed on the screen of the other session after frontdesk1 had checked him out, unt... | observed in the manual pass; verify in code first |
| F-067 | S3 | CONFIRMED | PKG-06 | 241 | Patient bar shows sex as lowercase "male" for Brian but "Female" for Carla. Also the Patient safety banner headline says "No alerts, active ... | observed in the manual pass; verify in code first |
| F-068 | S4 | NOT_REPRODUCED | PKG-05 | 249 | Schedule page Start time field has no clock/picker button while the Date field has a calendar button. Seen in Firefox private window; to re-... | seen in a Firefox private window only; re-check in Brave/Chrome |
| F-069 | S3 | CONFIRMED | PKG-05 | 250 | "Appointments on <day>" list (Time, Patient, Provider, Operatory, Type) has no Status column and rows are not links. Completed appointments ... | observed in the manual pass; verify in code first |
| F-070 | S3 | CONFIRMED | PKG-05 | 251 | booking success message says "Appointment confirmed." but the appointment status is Scheduled; Confirm is a separate later step on the Visit... | observed in the manual pass; verify in code first |
| F-071 | S2 | CONFIRMED | PKG-05 | 252 | Brian's 11/04 14:00 Filling was Checked out and Completed on 11/01 (3 days BEFORE it takes place). The Completed appointment still holds Dr.... | observed in the manual pass; verify in code first |
| F-072 | S4 | CONFIRMED | PKG-05 | 258 | the same-patient refusal does not say which appointment or time (the provider refusal does: "Dr. Dana already has an appointment from 14:00 ... | observed in the manual pass; verify in code first |
| F-073 | S3 | DESIGN_DECISION_REQUIRED | PKG-05 | 283 | a no-show is final. No undo (patient arrives late, wrong click), no reason or note captured, no Rebook/Reschedule offer, same gap as cancell... | no-show correction |
| F-074 | S4 | CONFIRMED | PKG-05 | 284 | History detail repeats the event name ("Marked as a no-show - Marked as a no-show."), shows no user (same as F-046/F-062). | observed in the manual pass; verify in code first |
| F-075 | S3 | CONFIRMED | PKG-14 | 291 | changing a filter (and, per the tester, many other actions across the app) gives NO busy or progress indicator. There is a short delay, the ... | observed in the manual pass; verify in code first |
| F-076 | S4 | CONFIRMED | PKG-05 | 292 | the filtered week grid with no results shows an empty grid with no "No appointments for this selection" message (the Schedule page has one: ... | observed in the manual pass; verify in code first |
| F-077 | S4 | CONFIRMED | PKG-05 | 294 | in the week header, "Friday 2026-10-30" sits on one line and is vertically centred, while the other days wrap onto two lines (day name, date... | observed in the manual pass; verify in code first |
| F-078 | S1 | CONFIRMED | PKG-06 | 302 | AFTER Penicillin (Active) and Lisinopril (Active) were saved, the patient bar still shows "Patient safety: No alerts, active allergies or cu... | observed in the manual pass; verify in code first |
| F-079 | S4 | CONFIRMED | PKG-06 | 303 | each item's History is expanded by default; with many entries the Clinical tab becomes long. Collapse by default. | observed in the manual pass; verify in code first |
| F-080 | S2 | CONFIRMED | PKG-06 | 315 | inside the encounter the Allergies card says "Not yet addressed" and offers "Reviewed - none reported" even though an ACTIVE Penicillin alle... | observed in the manual pass; verify in code first |
| F-081 | S4 | CONFIRMED | PKG-06 | 316 | the safety banner is shown twice on the encounter page (page top and again inside the encounter). | observed in the manual pass; verify in code first |
| F-082 | S4 | CONFIRMED | PKG-06 | 317 | the encounter list row says "Some sections not yet addressed - 0 entries" although vital signs were recorded; unclear what "entries" counts. | observed in the manual pass; verify in code first |
| F-083 | S2 | CONFIRMED | PKG-14 | 331 | the patient workspace (tabs Details, Household & guarantor, History, Forms, Documents, Clinical, Safety, Odontogram, Periodontal, Diagnoses,... | observed in the manual pass; verify in code first |
| F-084 | S2 | CONFIRMED | PKG-06 | 337 | "Add allergy" Penicillin in the encounter was accepted ("Allergies entry saved.") although an ACTIVE Penicillin allergy is already on the ch... | observed in the manual pass; verify in code first |
| F-085 | S2 | CONFIRMED | PKG-06 | 346 | the medication added inside the finalized encounter (Ibuprofen 400 mg, as needed) is NOT on the Safety tab: it still says "1 current medicat... | observed in the manual pass; verify in code first |
| F-086 | S2 | CONFIRMED | PKG-06 | 347 | the Safety tab "Not established" box still lists "Allergies / Medications / Medical history have changed or were never confirmed as current"... | observed in the manual pass; verify in code first |
| F-087 | S3 | CONFIRMED | PKG-10 | 355 | "History of this encounter (20)" lists events with a time but NO person: 1 Encounter started 11:21:56, 2 Vital signs recorded, 3-11 nine "No... | observed in the manual pass; verify in code first |
| F-088 | S3 | DESIGN_DECISION_REQUIRED | PKG-06 | 359 | each click on "Start an encounter" opens a new empty draft and navigates away, and there is no way to cancel, delete or discard an empty dra... | discard empty draft |
| F-089 | S3 | CONFIRMED | PKG-06 | 367 | the Source box was left empty, yet the saved alert says "Source: patient". The app invented the provenance (the page says alerts are "what a... | observed in the manual pass; verify in code first |
| F-090 | S3 | CONFIRMED | PKG-06 | 374 | the clearance "Document reference" is free text (owner typed "ER Doctor", a name, not a document); it is not a picker from the patient's Doc... | observed in the manual pass; verify in code first |
| F-091 | S2 | DESIGN_DECISION_REQUIRED | PKG-06 | 414 | 5.03 FAIL (impossible combination accepted). Tooth 15 was recorded "Missing tooth (whole tooth) ... Diagnosed" at 11:22:26 PM. Then "Caries ... | meaning of Diagnosed/Planned missing-tooth coexistence (Existing/Completed already refuse) |
| F-092 | S3 | CONFIRMED | PKG-06 | 423 | the Withdraw form says "Use this for an entry that was wrong. It leaves the chart but stays in the history, with who withdrew it and this re... | observed in the manual pass; verify in code first |
| F-093 | S4 | CONFIRMED | PKG-06 | 425 | stale message. After selecting tooth 3 the banner still shows the tooth-15 text "Not saved: This tooth is recorded as missing, so no other f... | observed in the manual pass; verify in code first |
| F-094 | S3 | CONFIRMED | PKG-06 | 433 | repeating the identical record (tooth 5, Restoration, Mesial, Existing) produced NO message. The finding was not duplicated (Mesial still "1... | observed in the manual pass; verify in code first |
| F-095 | S2 | CONFIRMED | PKG-06 | 435 | the odontogram Plan and Complete are only labels. Treatment plan tab (Alice): "This patient has no current diagnosis. A treatment plan propo... | observed in the manual pass; verify in code first |
| F-096 | S4 | CONFIRMED | PKG-14 | 436 | dark theme: on the Treatment plan tab the "Plan title" box and the "Review old diagnosis references" button are bright white inside the dark... | observed in the manual pass; verify in code first |
| F-097 | S3 | CONFIRMED | PKG-06 | 453 | the two boxes per site have no visible labels; only the paragraph above says depth then recession. | observed in the manual pass; verify in code first |
| F-098 | S2 | CONFIRMED | PKG-06 | 461 | periodontal chart "Linked records": the Reference box is free text with no picker, no search, no check. Owner typed "chronic" and then paste... | observed in the manual pass; verify in code first |
| F-099 | S3 | CONFIRMED | PKG-06 | 468 | numbering mix: the card says "Tooth 14 (upper left first molar)" (Universal) but the History table says "(tooth 26 FDI)". Same tooth, two sy... | observed in the manual pass; verify in code first |
| F-100 | S3 | CONFIRMED | PKG-04 | 488 | "Answer given by Patient. Entered by Dr. Dana on 11/3/2026, 8:00:00 AM": the time is exactly 8:00:00, not a real click time (plan was create... | observed in the manual pass; verify in code first |
| F-101 | S3 | DESIGN_DECISION_REQUIRED | PKG-08 | 497 | the plan badge still says "Proposed" with its only procedure Accepted and Completed ("last changed by Dr. Dana 8:30:21 AM"). Plan-level stat... | plan state after completion |
| F-102 | S3 | CONFIRMED | PKG-08 | 503 | the link to the account ledger exists only on the one-time result box (closed by Done). "account entry 1" in Clinical history is plain text,... | observed in the manual pass; verify in code first |
| F-103 | S2 | CONFIRMED | PKG-07 | 512 | completion accepted when the encounter chosen was meant to be Finalized (11/1 6:05:48 PM). If confirmed, a finalized (signed-off) encounter ... | observed in the manual pass; verify in code first |
| F-104 | S2 | DUPLICATE | PKG-06 | 513 | the plan row shows "Tooth 14, surface M" but the Clinical history row shows "tooth 26 · surface M". Same procedure, two different teeth. Pen... | of F-099 (display numbering only; stored tooth identity must not change) |
| F-105 | S2 | CONFIRMED | PKG-06 | 551 | Amoxicillin 500 mg Every 6 hours, Check entry: "No allergy recorded for this patient matches this medication's name. This is a name comparis... | observed in the manual pass; verify in code first |
| F-106 | S2 | CONFIRMED | PKG-06 | 579 | the Prescriptions tab lists both versions identically (500 mg · Every 6 hours, allergy text), with no status, no instructions and no quantit... | observed in the manual pass; verify in code first |
| F-107 | S1 | CONFIRMED | PKG-04 | 584 | the printed prescription says "born 3/13/1985" but the patient header says DOB 1985-03-14 (and the import CSV had 1985-03-14). The date of b... | observed in the manual pass; verify in code first |
| F-108 | S3 | CONFIRMED | PKG-12 | 585 | the browser print preview shows "2 sheets of paper"; page 1 is the prescription, page 2 is blank. Needs a print stylesheet that prints only ... | observed in the manual pass; verify in code first |
| F-109 | S3 | CONFIRMED | PKG-12 | 586 | browser headers and footers print on the sheet: the page title (Alveara Dental) top left, the page URL top right (it contains the patient's ... | observed in the manual pass; verify in code first |
| F-110 | S2 | DESIGN_DECISION_REQUIRED | PKG-12 | 587 | the printed prescription has no prescriber signature line, no prescriber licence/NPI number, and no patient address. | jurisdictional requirements to be researched |
| F-111 | S2 | CONFIRMED | PKG-06 | 617 | the 11/1 11:21:56 AM encounter recorded "Ibuprofen 400 mg as needed" (medication entry added 5:42:23 PM), later than Lisinopril (11:07:44 AM... | observed in the manual pass; verify in code first |
| F-112 | S3 | CONFIRMED | PKG-06 | 626 | encounter 6:05:48 PM (finalized by Harper 8:45:57 PM) says "Reviewed - nothing to report here" for Medical history and Medications while the... | observed in the manual pass; verify in code first |
| F-113 | S3 | CONFIRMED | PKG-09 | 667 | "Receipt for payment 5" opens the receipt preview inside "Statements and receipts" at the bottom of a very long page, with no scroll, focus ... | observed in the manual pass; verify in code first |
| F-114 | S2 | CONFIRMED | PKG-09 | 689 | issuing from a second preview of "Receipt for payment 5" succeeded and created Receipt 2 (11/4/2026 6:41:20 AM) for the same payment 5, same... | observed in the manual pass; verify in code first |
| F-115 | S3 | CONFIRMED | PKG-09 | 692 | after the payment is reversed, Receipt 1 and Receipt 2 still sit in the list with no mark that the payment was reversed. Receipt for payment... | observed in the manual pass; verify in code first |
| F-116 | S3 | CONFIRMED | PKG-09 | 715 | the aging table ("What is open, by age") and the line "Still to pay on charges" showed $320.00 while Balance owed showed $295.00 and $305.00... | observed in the manual pass; verify in code first |
| F-117 |  | CONFIRMED | PKG-09 | 722 | , S3: no strike-through or "reversed" mark as the history table has). Practice name and address shown; "Up to: 11/4/2026, 8:53:00 AM". | observed in the manual pass; verify in code first |
| F-118 | S3 | CONFIRMED | PKG-04 | 731 | Charge 4 says "Completed procedure performed 2026-11-04" but its charge time is 11/3/2026 10:42 PM local (history row also dated 11/3). The ... | observed in the manual pass; verify in code first |
| F-119 |  | CONFIRMED | PKG-09 | 729 | , S3): no patient name, practice, statement number or "up to" time in the file; reversed entries are plain rows with no "reversed by" column... | observed in the manual pass; verify in code first |
| F-120 | S4 | CONFIRMED | PKG-09 | 737 | the message does not say it already existed. Empty name and lowercase "check" not yet reported (Q-5bb). | observed in the manual pass; verify in code first |
| F-121 | S4 | CONFIRMED | PKG-09 | 747 | the inline errors stay visible after a field is chosen until the next Move click. F-122 (S4): "Nothing was sent" appears on some failures an... | observed in the manual pass; verify in code first |
| F-122 | S4 | CONFIRMED | PKG-09 | 747 | "Nothing was sent" appears on some failures and not on others (second banner omitted it); a move sends nothing anyway. | observed in the manual pass; verify in code first |
| F-124 | S4 | DUPLICATE | PKG-06 | 757 | Alicia's header shows sex as lowercase "female"; Alice shows "Female". Same family as F-032 (Sex handling on import). | of F-067 |
| F-125 | S4 | CONFIRMED | PKG-12 | 778 | the Inactive filter with nothing in it says "No documents yet. Imported documents and signed forms for this patient appear here." (misleadin... | observed in the manual pass; verify in code first |
| F-126 | S3 | CONFIRMED | PKG-10 | 802 | ~50 "fail ... Unhandled request exception: TaskCanceledException" from SafeExceptionHandlingMiddleware. These are almost certainly requests ... | observed in the manual pass; verify in code first |
| F-127 | S2 | NOT_REPRODUCED | PKG-10 | 803 | one "Unhandled request exception: SqlException" (correlation 00-e9e8e1661d25ed47ffba6ef4a4bf3d12-d502e4e4e76c4838-00). The log gives no SQL ... | SqlException seen once, not reproduced; the observability fix remains |
| F-128 | S3 | CONFIRMED | PKG-10 | 804 | 10 warnings "appointment.flow measurement event was not recorded ... Property 'category' has an unacceptable value for its type/bounded-valu... | observed in the manual pass; verify in code first |
| F-129 | S4 | CONFIRMED | PKG-15 | 805 | "Failed to determine the https port for redirect" warning at startup (already known harmless). | observed in the manual pass; verify in code first |
| F-130 | S3 | CONFIRMED | PKG-12 | 811 | the Open/Download links of a file known to be missing should be removed or disabled. | observed in the manual pass; verify in code first |
| F-131 | S3 | CONFIRMED | PKG-12 | 812 | the user lands on a raw JSON page in a new tab instead of a message in the app. Message itself is clear (S4: "Contact support" should say wh... | observed in the manual pass; verify in code first |
| F-132 | S4 | CONFIRMED | PKG-13 | 822 | the empty-state text "No open follow-ups" appears with nothing under Closed for these system tasks (they are only in the timeline). | observed in the manual pass; verify in code first |
| F-133 |  | WITHDRAWN | PKG-04 | 824 | WITHDRAWN: owner confirms a time was entered on each task and the two differ, so the display matches the input. (My earlier "date-only defau... | owner confirmed a time was entered on each task |
| F-134 |  | CONFIRMED | PKG-13 | 833 | after Mark all read the sidebar count and the yellow "important notification" banner stayed for about a minute, then cleared (owner believes... | observed in the manual pass; verify in code first |
| F-135 | S3 | CONFIRMED | PKG-13 | 843 | after unticking opt-out and saving, the Recall section says "No completed visit yet, so there is no recall date." but still shows "Reason: T... | observed in the manual pass; verify in code first |
| F-136 | S3 | CONFIRMED | PKG-13 | 844 | none of the three recall changes (interval, opt-out, un-opt-out) appear in the Follow-ups Timeline (still 13 entries), although the page say... | observed in the manual pass; verify in code first |
| F-137 | S3 | CONFIRMED | PKG-06 | 861 | Tooth 99 was NOT flagged (only "Choose the laboratory." showed with the generic "Fix the marked fields and save again."), while Tooth 3 give... | observed in the manual pass; verify in code first |
| F-138 |  | CONFIRMED | PKG-10 | 877 |  | observed in the manual pass; verify in code first |
| F-139 | S3 | CONFIRMED | PKG-13 | 895 | Notifications page design. The Unread / History tabs and the notice buttons (Open patient, Mark read, Hide) touch each other with no spacing... | observed in the manual pass; verify in code first |
| F-140 | S3 | CONFIRMED | PKG-13 | 906 | Reply form does not pre-fill To with the original sender. The checkbox list is empty and offers every staff member; the replier must tick th... | observed in the manual pass; verify in code first |
| F-141 | S3 | CONFIRMED | PKG-13 | 907 | the thread view does not read as a conversation. Messages are stacked as plain text with a bold name and time; no bubbles, indent, side or r... | observed in the manual pass; verify in code first |
| F-142 | S3 | DESIGN_DECISION_REQUIRED | PKG-13 | 908 | Sent list shows "to 1, read by 0" (numbers only). Code confirms this is the implementation (MessagesPage.tsx line 53: recipientCount and rea... | sent-list detail |
| F-143 | S3 | CONFIRMED | PKG-13 | 920 | Step 1 says "Sent ... by Unknown staff member" while admin is signed in. The batch should name the uploader (audit). | observed in the manual pass; verify in code first |
| F-144 | S3 | DESIGN_DECISION_REQUIRED | PKG-11 | 929 | Show filter on the dry-run Row results takes one value at a time (All rows / Rejected / Skipped ...). Owner wants multi-select (for example ... | multi-select filter |
| F-145 | S2 | CONFIRMED | PKG-11 | 932 | after commit the search for Chen shows TWO active Carla Chens: DOB 1997-07-21 (existing) and 1992-07-21 (imported). Same name, phone 555-010... | observed in the manual pass; verify in code first |
| F-146 | S3 | DESIGN_DECISION_REQUIRED | PKG-11 | 933 | Details tab shows no MRN / patient number. Code: Patient.Id is a Guid (Patient.cs line 10); no MRN or chart-number field anywhere in the sou... | MRN design |
| F-147 | S2 | CONFIRMED | PKG-11 | 951 | Import a patient refuses manifest.json from the package. Package root holds manifest.json and a data folder with 19 .json files. The importe... | observed in the manual pass; verify in code first |
| F-148 | S3 | CONFIRMED | PKG-11 | 959 | Not a duplicate on the pair opens an inline form with an UNLABELLED text box, Confirm and Cancel buttons and no instruction. The merge form ... | observed in the manual pass; verify in code first |
| F-149 | S3 | CONFIRMED | PKG-10 | 987 | "Performed by" shows raw user GUIDs (8658f317... is admin, 32d03dcb... manager1) so an auditor cannot read who acted; Details sometimes name... | observed in the manual pass; verify in code first |
| F-150 | S3 | CONFIRMED | PKG-10 | 988 | the page description says "account, role, and session events" but the log holds clinical, billing, document, report, import, messaging and r... | observed in the manual pass; verify in code first |
| F-151 | S4 | CONFIRMED | PKG-02 | 990 | login rows record the typed username untrimmed ('manager1 ', 'dentist1 ', 'billing1 ', 'hygienist1 ' with a trailing space) and those logins... | observed in the manual pass; verify in code first |
| F-152 |  | CONFIRMED | PKG-12 | 1014 | explained: disk check shows the backup alerts are written as text files in C:\.alvera-data\Dev-Data\Backup\notifications (TestNotification 1... | observed in the manual pass; verify in code first |
| F-153 | S3 | CONFIRMED | PKG-12 | 1019 | the wizard's Browse... file picker is filtered to "All Supported Types (*.pem;*.ke...)" and did NOT list the recovery key file the app itsel... | observed in the manual pass; verify in code first |
| F-154 | S3 | CONFIRMED | PKG-12 | 1030 | clicking "Remove the restored copy" gives NO busy feedback: the button stays enabled, no spinner or "Removing..." text, nothing changes for ... | observed in the manual pass; verify in code first |
| F-155 | S3 | CONFIRMED | PKG-02 | 1043 | a short server outage (restart, deploy, power blip) signs EVERY open user out and throws away any form in progress. Cause found in code: src... | observed in the manual pass; verify in code first |
| F-156 | S4 | CONFIRMED | PKG-02 | 1048 | the third screenshot (after F5) is in the LIGHT theme; the first two were Dark. The theme preference is not applied to the sign-in page afte... | observed in the manual pass; verify in code first |
| F-157 | S3 | NOT_REPRODUCED | PKG-13 | 1057 | the first two permissions calls took 19 ms and 23 ms, but the following ones show about 4,000 ms each (4062, 3783, 2041, 4063, 4083, 4096, 4... | needs re-measurement |
| F-158 | S3 | CONFIRMED | PKG-14 | 1064 | on the Schedule report for frontdesk1 the filter area says "The list of providers could not be loaded, so this report cannot be narrowed by ... | observed in the manual pass; verify in code first |
| F-159 | S3 | CONFIRMED | PKG-14 | 1065 | frontdesk1 sees System Status and Component Showcase in the menu. Check what System Status shows a front-desk user (it should be admin-only ... | observed in the manual pass; verify in code first |
| F-160 | S4 | CONFIRMED | PKG-14 | 1075 | says "could not load", not "not found". Retry is pointless for an id that does not exist. Two messages say the same thing. | observed in the manual pass; verify in code first |
| F-161 | S3 | CONFIRMED | PKG-14 | 1079 | on phones and at 200% zoom the full menu (14 entries, 4 to 5 rows), the sign-out row, the theme button and the patient banner sit above the ... | observed in the manual pass; verify in code first |
| F-162 | S4 | NOT_REPRODUCED | PKG-14 | 1080 | on Patients at 390 the page appears about 444 px wide (the menu panel stops short of the right edge of the full-page capture), so the table ... | recheck at real width |
| F-163 | S4 | NOT_REPRODUCED | PKG-14 | 1081 | at desktop height the side menu scrolls inside itself. In the Register screen the first entry (Dashboard) was out of view, and in the error ... | unconfirmed |
| F-164 | S3 | DESIGN_DECISION_REQUIRED | PKG-12 | 1092 | Print stays available on a Cancelled (and a Discontinued) prescription. A cancelled prescription "should not be dispensed", yet a clean prin... | print of cancelled/discontinued prescription (status mark vs block) |
| F-165 | S4 | CONFIRMED | PKG-12 | 1093 | quantity wording "28 tablet" (should be "28 tablets"). "1 tablet" with "Twice daily" and 3 refills is test data, not a defect. | observed in the manual pass; verify in code first |
| F-166 | S4 | DUPLICATE | PKG-02 | 1094 | the login audit line reads "Successful login for 'dentist1 '." with a trailing space inside the name. Either the sign-in accepted a name typ... | of F-151 |
| F-167 | S4 | CONFIRMED | PKG-13 | 1109 | the Unread and History toggles (and "New prescription draft" on the composer) render as plain light-grey default buttons on the dark page, a... | observed in the manual pass; verify in code first |
| F-168 | S4 | CONFIRMED | PKG-13 | 1110 | the urgent notice has sat unread since 11/5, and the pink box, light on a dark page, is the loudest thing on every screen. Readable (dark re... | observed in the manual pass; verify in code first |
| F-169 | S3 | DESIGN_DECISION_REQUIRED | PKG-13 | 1116 | "Hide" on a notification (History) is one-way from the user's side. There is no "Show hidden" view and no undo, and "Clear history" at the t... | show hidden / undo |
| F-170 | S2 | WITHDRAWN | PKG-05 | 1123 | the Permission Matrix page shows manager1 a "Who may import documents" table with an Allow button for Dentist, Hygienist, Assistant, FrontDe... | intentional delegated-permission model; boundary/audit verification kept in PKG-05 |
| F-171 | S4 | CONFIRMED | PKG-12 | 1127 | the Documents Actions column wraps to two lines with up to six buttons per row; the layout is uneven row to row. | observed in the manual pass; verify in code first |
| F-172 | S3 | CONFIRMED | PKG-10 | 1129 | the Change history "By" column shows 32d03dcb, a truncated id, not the manager's name, although the page says each change is recorded with y... | observed in the manual pass; verify in code first |
| F-173 | S3 | CONFIRMED | PKG-12 | 1134 | with ImportDocuments granted, assistant1 also gets Edit and Inactivate on every document in the list, including other people's and clinical ... | observed in the manual pass; verify in code first |
| F-174 | S4 | CONFIRMED | PKG-10 | 1138 | the Audit log heading says "the most recent 500 account, role, and session events", but it also lists SafetyAlertResolved, SafetyAlertReopen... | observed in the manual pass; verify in code first |
| F-175 | S3 | DUPLICATE | PKG-09 | 1142 | the page shows "Balance owed: $269.00", but the line under it says "Still to pay on charges: $300.00" and the age table puts $300.00 in 0 to... | of F-116 |
| F-176 | S3 | CONFIRMED | PKG-14 | 1143 | the Billing tab uses default light form controls (white Amount, Note, Reason and Why boxes, grey method select, grey Reverse, Correct, Refun... | observed in the manual pass; verify in code first |
| F-177 |  | CONFIRMED | PKG-08 | 1152 | after the only item is accepted and completed the plan label does not change. Rename or add a derived state (Completed). | observed in the manual pass; verify in code first |
| F-178 |  | CONFIRMED | PKG-08 | 1153 | In the code the button only hides when the plan is already Declined. | observed in the manual pass; verify in code first |
| F-179 |  | CONFIRMED | PKG-08 | 1155 | that message stays on screen after plans exist and reads as a warning; show it only when no plan exists. | observed in the manual pass; verify in code first |
| F-180 |  | CONFIRMED | PKG-08 | 1157 | the button is offered on plans where nothing can ever be adopted; it could be hidden unless an adoptable completion exists. The adopt form i... | observed in the manual pass; verify in code first |
| F-181 | S4 | CONFIRMED | PKG-08 | 1162 | setting the code changed "last changed" but created no revision, so the revision history still shows only Revisions 1 and 2. The code change... | observed in the manual pass; verify in code first |
| F-182 |  | CONFIRMED | PKG-08 | 1167 | "Before" and "After" run together on one line with no separator. | observed in the manual pass; verify in code first |
| F-183 |  | CONFIRMED | PKG-08 | 1168 | no practice name, address or phone on a page meant to be printed and handed over; "done" is terse and lower case; the intro "Your dentist ha... | observed in the manual pass; verify in code first |
| F-184 | S3 | CONFIRMED | PKG-04 | 1178 | the item's line "Answer given by patient. Entered by Dr. Dana on ..." changed from 11/8/2026 12:08:35 AM (before scheduling) to 12:12:35 AM ... | observed in the manual pass; verify in code first |
| F-185 | S4 | CONFIRMED | PKG-08 | 1179 | a "Scheduled for" label on a plan item reads like a booked visit but nothing links it to the calendar; no "Book appointment" link from the i... | release-blocking per the reviewer |
| F-186 |  | CONFIRMED | PKG-08 | 1184 | rather than refused with a message. PASS. | observed in the manual pass; verify in code first |
| F-187 | S4 | CONFIRMED | PKG-08 | 1186 | with no active procedure left, the plan still says "Proposed" and "Patient's answer: Not yet answered", and offers Accept all pending and De... | observed in the manual pass; verify in code first |
| F-188 | S3 | DUPLICATE | PKG-08 | 1190 | the item "schedule" function does not do anything useful. Saving an empty date is accepted and leaves the item labelled "Scheduled" with no ... | family with F-185 (separate acceptance criteria kept) |
| F-189 | S4 | CONFIRMED | PKG-08 | 1201 | the button name says "pending" (unanswered) but it works only when no item has the opposite answer, so on a partly answered plan it can neve... | observed in the manual pass; verify in code first |
| F-190 | S3 | CONFIRMED | PKG-08 | 1208 | revision history after these changes still shows only Revision 1 and Revision 2 (Procedure added). The answer changes (Accepted 8:40:12, Dec... | observed in the manual pass; verify in code first |
| F-191 |  | CONFIRMED | PKG-08 | 1205 | Plan buttons: Accept all pending stays although nothing is pending (all declined), Decline all pending and Review and complete are gone. PAS... | observed in the manual pass; verify in code first |
| F-192 | S3 | CONFIRMED | PKG-08 | 1213 | the plan audit rows are thin. Entity is "TreatmentPlan <guid>", "Performed by" is a guid, and Details say only "Item 1 declined in a treatme... | observed in the manual pass; verify in code first |
| F-193 | S4 | CONFIRMED | PKG-09 | 1226 | Payment methods (Zelle, Stop using Zelle, Add method) are practice-wide but sit at the bottom of every patient's Billing tab. Belongs in Pra... | observed in the manual pass; verify in code first |
| F-194 | S4 | CONFIRMED | PKG-14 | 1227 | "Preview a statement", "Show the timeline", "Add method" and "Stop using Zelle" are light grey default-looking buttons in the dark theme, an... | observed in the manual pass; verify in code first |
| F-195 | S4 | CONFIRMED | PKG-09 | 1228 | Account history and Timeline are two separate sections; it is not clear how they differ. Clarify at review. | observed in the manual pass; verify in code first |
| F-196 | S4 | CONFIRMED | PKG-09 | 1236 | in Record a payment the "Choose which charges this pays" checkbox is centred with its label under it, unlike every other field. | observed in the manual pass; verify in code first |
| F-197 | S4 | CONFIRMED | PKG-09 | 1239 | recording the same party that already applies (the default, the patient) is accepted and adds a row; no "nothing would change" message. Eith... | observed in the manual pass; verify in code first |
| F-198 | S4 | CONFIRMED | PKG-09 | 1240 | wording differs on the same screen: "recorded by billing" (role) vs "Bill" (person) in the table. "From" is a date only, no time. | observed in the manual pass; verify in code first |
| F-199 | S4 | DUPLICATE | PKG-06 | 1244 | the safety banner says "Medical history have not been reviewed" (should be "has"). | of F-067 |
| F-200 | S4 | CONFIRMED | PKG-14 | 1246 | each tab only tells the user to go to another tab; no link or button ("Go to Clinical"). Add a link in each message. The chain is easy to mi... | observed in the manual pass; verify in code first |
| F-201 |  | WITHDRAWN | PKG-01 | 1248 | WITHDRAWN: duplicate of Q-5b (line ~304), already logged on Alice. Also, "Start an encounter" opens a draft directly with no form (see 4.05)... | duplicate of Q-5b; assistant error |
| F-202 | S4 | CONFIRMED | PKG-11 | 1252 | Alice's household table lists only "Alice Anderson (this patient)" with relationship "Spouse" and nothing else. A one-person household with ... | observed in the manual pass; verify in code first |
| F-203 | S4 | CONFIRMED | PKG-09 | 1254 | Set guarantor stays enabled, and the refusal only appears after the click. The "This patient is the guarantor for: Alicia" line is already o... | observed in the manual pass; verify in code first |
| F-204 | S4 | CONFIRMED | PKG-09 | 1256 | the saved messages are generic and inconsistent ("Guarantor set." does not name who; "Patient is now responsible for themselves." does not n... | observed in the manual pass; verify in code first |
| F-205 | S4 | CONFIRMED | PKG-09 | 1259 | the default is the patient although Carla is the responsible party now; default to the responsible party. | observed in the manual pass; verify in code first |
| F-207 | S4 | CONFIRMED | PKG-09 | 1267 | the "Payment of $5.00 recorded. Balance owed is now $264.00." banner is still shown at the top after issuing Receipt 3 and viewing Receipts ... | observed in the manual pass; verify in code first |
| F-208 | S4 | CONFIRMED | PKG-09 | 1268 | money format differs inside the same notice: "264.00 USD" there, "$264.00" everywhere else. | observed in the manual pass; verify in code first |
| F-209 | S3 | CONFIRMED | PKG-09 | 1271 | the "Who paid" field disappeared from Record a payment again as soon as the patient became the responsible party, although Carla is still th... | observed in the manual pass; verify in code first |
| F-210 | S3 | CONFIRMED | PKG-11 | 1276 | two patients with the same name, phone (and per the CSV, address) and a birth date that differs by five years exist side by side. Most likel... | observed in the manual pass; verify in code first |
| F-211 | S4 | DUPLICATE | PKG-11 | 1277 | a guarantor picker that shows only "Carla Chen (born 1992-07-21)" is the only clue that two exist; show phone or address too. | family with F-145/F-210 |
| F-212 | S4 | CONFIRMED | PKG-10 | 1283 | console log lines carry no timestamp, so a log line cannot be matched to an action or to the audit log. | observed in the manual pass; verify in code first |
| F-213 | S4 | CONFIRMED | PKG-14 | 1288 | the matrix needs sideways scrolling and the header row and caption scroll away or are cut off (caption reads "Permission matrix: which permi... | observed in the manual pass; verify in code first |
| F-214 | S4 | CONFIRMED | PKG-10 | 1289 | the "By" column in Change history shows a raw id fragment ("32d03dcb"), not the account name, so a reviewer cannot tell who changed a permis... | observed in the manual pass; verify in code first |
| F-215 | S4 | CONFIRMED | PKG-14 | 1290 | every cell repeats the "default" tag and a Change button, 63 rows x 8 columns of identical controls; hard to see what is non-default. Recomm... | observed in the manual pass; verify in code first |
| F-216 | S3 | CONFIRMED | PKG-13 | 1306 | saving the same baseline twice is accepted: two identical "First week" rows (09:35 and 09:30). Baselines cannot be changed or removed, so a ... | observed in the manual pass; verify in code first |
| F-217 | S4 | CONFIRMED | PKG-13 | 1307 | the overlap message ("do not end before this period starts, so the two overlap or are the wrong way round") is hard to read; say "Your basel... | observed in the manual pass; verify in code first |
| F-218 | S4 | DUPLICATE | PKG-04 | 1310 | after taking it off, the item line reads "Answer given by patient. Entered by Dr. Dana on 11/8/2026, 9:41:17 PM", the time of the unschedule... | of F-184 |
| F-219 | S3 | CONFIRMED | PKG-08 | 1317 | renaming bumped the revision from 3 to 4 although Rename plan asks for no reason, while Revise plan refuses without one. Either the name is ... | observed in the manual pass; verify in code first |
| F-220 | S4 | DUPLICATE | PKG-08 | 1323 | a stale banner "Nothing is saved until you create a plan." stays at the top of the Treatment plan tab while the owner works on other plans (... | of F-179 |
| F-221 | S3 | CONFIRMED | PKG-08 | 1330 | a past planned date is accepted without warning. L2000 was scheduled for 11/01/2026 while today is 11/08/2026: banner "L2000 scheduled.", ta... | observed in the manual pass; verify in code first |
| F-222 | S4 | CONFIRMED | PKG-08 | 1332 | the button is offered on every plan with a completed item although only an old-style completion can be adopted; show it only when the server... | observed in the manual pass; verify in code first |
| D-01a | design | WITHDRAWN | PKG-05 | 144 | WITHDRAWN (owner, 2026-10-31): read-only access to Procedures & fees is good for FrontDesk (they need to see prices); no change wanted. /adm... | owner: read-only Procedures & fees is good for FrontDesk |
| D-01b | design | DESIGN_DECISION_REQUIRED | PKG-14 | 456 | owner, S3/S4): dark theme: ALL text boxes, drop-downs, textareas and number boxes on the clinical pages are bright white (periodontal grid, ... | design note from the owner |
| D-02a | design | DESIGN_DECISION_REQUIRED | PKG-11 | 145 | for triage: the duplicate is a hard block with no "this is a different person, register anyway" path; with real data, two people can share n... | design note from the owner |
| D-02b | design | DESIGN_DECISION_REQUIRED | PKG-14 | 463 | owner): workspace tabs (Details ... Billing, two rows) look like plain text: no tab shape, weak active-tab marker, no hover highlight. Needs... | design note from the owner |
| D-03 | S4 | DESIGN_DECISION_REQUIRED | PKG-08 | 489 | the treatment plan card is crowded: action buttons (Accept / Decline / Withdraw L2000, and the plan-level row) sit on rows with almost no ve... | design note from the owner |
| D-04 | two prescrip | DESIGN_DECISION_REQUIRED | PKG-14 | 610 | docs/PRESCRIPTION_WORKFLOW.md states the composer sits "beside STORY-016's Prescriptions tab (the quick entry form, kept so that STORY-016's... | design note from the owner |
| D-05 | design | DESIGN_DECISION_REQUIRED | PKG-14 | 945 | menu review. Import a patient (JSON file exported from Alveara, up to 256 KB, one patient), Data import (CSV from another system: patients, ... | design note from the owner |
| D-06 | owner idea,  | DESIGN_DECISION_REQUIRED | PKG-12 | 1036 | add to System Status (a) backup and recovery status (last good backup, last restore drill, key set up yes/no, overdue flag), (b) storage sta... | design note from the owner |
| D-07 | owner, 2026- | DESIGN_DECISION_REQUIRED | PKG-14 | 1197 | group the patient workspace tabs. (a) Under Clinical, make Diagnoses and Treatment plan sub-tabs; other clinical tabs may move there at the ... | design note from the owner |
| D-08 | owner, 2026- | DESIGN_DECISION_REQUIRED | PKG-14 | 1218 | stop showing blank create forms all the time (e.g. the "Create treatment plan" form is always on the page); show an "Add treatment plan" but... | design note from the owner |
| D-09 | design | DESIGN_DECISION_REQUIRED | PKG-09 | 1229 | Account history can grow long; move it to its own sub tab inside Billing (owner prefers a sub tab over a new page). Assistant view: agree. S... | design note from the owner |
| D-10 | design | DESIGN_DECISION_REQUIRED | PKG-09 | 1274 | a guarantor cannot see the accounts she is responsible for (balances, her own payments for them). Suggest a section on the guarantor's Billi... | design note from the owner |
| D-11 | design | DESIGN_DECISION_REQUIRED | PKG-08 | 1318 | the owner revised "Finalized test 2" by mistake. Its only procedure is Completed (charged, account entry 3) yet it still reads "Proposed" an... | design note from the owner |
| E-01 | idea | DESIGN_DECISION_REQUIRED | PKG-14 | 443 | tooth drawings instead of tiles | design note from the owner |
| F-044b | design | DESIGN_DECISION_REQUIRED | PKG-05 | 173 | the duration box was left blank and the booking silently took the 60-minute default (split from F-044 per review R01) | silent 60-minute default when the duration is left blank (split from F-044 per review R01) |

Numbers never assigned in the log: F-123, F-206.

Disposition counts: CONFIRMED 176, DESIGN_DECISION_REQUIRED 36, DUPLICATE 10, NOT_REPRODUCED 8, WITHDRAWN 5.

## 9. Appendix B: open questions (Q-5x and Q-065a)

| Question | Status | Home package | Log line | Excerpt |
|---|---|---|---|---|
| Q-5a | OPEN | PKG-05 | 299 | dentist sees Billing and Forms tabs; check against the Permission Matrix (which roles should see Billing). |
| Q-5b | ANSWERED or carried in a finding (see log) |  | 304 | the Encounters text says "Start an encounter to record medical and dental history, allergies and medications", but items were adde... |
| Q-5e | ANSWERED in the pass |  | 349 | ANSWERED: the owner clicked Sign note, then Finalize encounter (two steps). The finalized screen does not show a separate signatur... |
| Q-5f | OPEN | PKG-06 | 324 | Medical history shows "Needs attention" in the encounter although Hypertension is already on the chart; every encounter must re-ad... |
| Q-5g | OPEN | PKG-06 | 338 | "Diabetes type 2" added through "Add medical history item" (saved, "Medical history entry saved."). The encounter card lists only ... |
| Q-5h | ANSWERED in the pass |  | 345 | then what did the encounter's "Add allergy" actually save? A note-level copy? Unclear to the clinician; there was still no duplica... |
| Q-5i | OPEN | PKG-06 | 357 | , and nothing in the encounter says the chart still needs confirming. TO TEST: does the banner clear after "Confirm list is curren... |
| Q-5j | OPEN | PKG-06 | 365 | the page sits under one patient's URL (/patients/<id>/clinical/templates) although templates look practice-wide; who may create th... |
| Q-5k | OPEN | PKG-06 | 367 | the author must also acknowledge their own alert ("1 alert you have not acknowledged yet" counted the creator). |
| Q-5l | ANSWERED in the pass |  | 635 | ANSWERED (informational only): as dentist1 with the clearance open, Check entry for Ibuprofen 400 mg As needed: "The entry has no ... |
| Q-5m | ANSWERED in the pass |  | 372 | the starter text is saved as real note content at once; if a section is "required before signing" does unedited starter text count... |
| Q-5n | ANSWERED in the pass |  | 381 | a resolved clearance still offers "Replace document"; is a replacement after resolution audited and does it reopen anything? |
| Q-5o | OPEN | PKG-06 | 382 | / design limit: after the first template is applied (and the note has text) the "Start from a template" chooser disappears, header... |
| Q-5p | ANSWERED in the pass |  | 383 | "Review before finalizing" for that draft lists only the four clinical sections (Medical history, Dental history, Allergies, Medic... |
| Q-5q | OPEN | PKG-05 | 388 | the front desk sees no patient-safety banner, so no allergy/alert warning when booking or checking in a patient; confirm that is i... |
| Q-5r | ANSWERED in the pass |  | 397 | S2 for the Permission Matrix): hygienist1's chart page shows the full edit set on every existing item: "Correct details", "Change ... |
| Q-5s | ANSWERED in the pass |  | 406 | a hygienist can sign and finalize a note whose medical-history and allergy entries are a dentist's clinical area; same person sign... |
| Q-5t | ANSWERED in the pass |  | 415 | the dental chart has no "Filling" (restoration) condition among the types seen. How is an existing filling recorded? Ask the owner... |
| Q-5u | ANSWERED in the pass |  | 435 | ANSWERED / F-095 (S2): the odontogram Plan and Complete are only labels. Treatment plan tab (Alice): "This patient has no current ... |
| Q-5v | OPEN | PKG-06 | 430 | should the Missing finding be superseded or retired once an implant is in place? Today the chart says the tooth is both missing an... |
| Q-5w | OPEN | PKG-06 | 431 | should an Implant require a recorded missing tooth, at least when Existing?). |
| Q-5x | OPEN | PKG-06 | 451 | design): typing 0 on all six sites per tooth is slow; deliberate to avoid empty = 0? |
| Q-5y | ANSWERED in the pass |  | 454 | S4): MB went 4 to 3 and stopped bleeding but is labelled "About the same" with no mark; consider noting resolved bleeding. |
| Q-5z | ANSWERED in the pass |  | 460 | S4): recorded against the FINALIZED 11/1 encounter; is adding a diagnosis to a finalized encounter intended? Nothing on the page s... |
| Q-5aa | OPEN | PKG-06 | 469 | S4): the diagnosis card does not show that a periodontal chart is linked to it (the link is only visible on the chart). Expected b... |
| Q-5ab | ANSWERED in the pass |  | 480 | ANSWERED: the plan form's diagnosis list offers the RESOLVED diagnosis "Chronic localized periodontitis (tooth 14)" as well as Gin... |
| Q-5ac | ANSWERED in the pass |  | 475 | S3, policy): the hygienist also sees Correct, Amend, Mark resolved, Link and Withdraw on the DENTIST's diagnosis (Chronic localize... |
| Q-5ad | OPEN | PKG-07 | 476 | the hygienist's diagnosis was also attached to the finalized 11/1 encounter (only encounter offered). Same question as Q-5z. |
| Q-5ae | OPEN | PKG-08 | 481 | S4): the empty Procedure box is NOT in the list, so is a plan without a procedure allowed, or is the check missing? Test later. |
| Q-5af | OPEN | PKG-08 | 498 | "Performed on" 11/3 against a Draft encounter dated 11/1 (two days earlier). No warning. Is a performed date after the encounter d... |
| Q-5ag | OPEN | PKG-09 | 505 | the charge says "Recorded by Dr. Dana" but Harper performed the cleaning. The ledger does not show the performing provider. Intend... |
| Q-5ah | OPEN | PKG-08 | 506 | when is "Completed procedures ready to bill" ever populated, if completion always charges? (Maybe only for "Adopt earlier completi... |
| Q-5ai | OPEN | PKG-06 | 521 | the Tooth box does not pre-fill from the diagnosis ("Chronic localized periodontitis (tooth 14)"); it must be chosen again. |
| Q-5aj | OPEN | PKG-06 | 522 | after completing L3000 on tooth 14 surface M, the odontogram shows tooth 14 Mesial "none recorded" (only Crown Planned and Caries ... |
| Q-5ak | ANSWERED in the pass |  | 542 | the Prescriptions page lists Current medications "Lisinopril 40 mg daily", while the finalized encounter 11/1 11:21:56 AM records ... |
| Q-5al | ANSWERED in the pass |  | 558 | ANSWERED: the Prescriptions tab and the Prescription composer show the same record. Composer, Finalized prescriptions: Penicillin ... |
| Q-5am | OPEN | PKG-12 | 548 | "How often" has both "Once" and "Once daily". Difference? |
| Q-5an | OPEN | PKG-04 | 559 | "Prescribed ... 9:11:00 PM" has zero seconds (same pattern as F-100 8:00:00 AM). Owner to confirm the actual clock time at the sav... |
| Q-5ao | OPEN | PKG-12 | 566 | clicking Amend creates a stored draft before the owner types anything; does a second click on Amend create a second draft? (test l... |
| Q-5ap | ANSWERED in the pass |  | 571 | that refusal did not list the allergy reason although the reason box was empty on screen; the original already carried a reason. D... |
| Q-5aq | OPEN | PKG-12 | 595 | the printed discontinued prescription does not show the discontinue date or reason. Intended? |
| Q-5ar | OPEN | PKG-05 | 609 | a hygienist can print a prescription from the composer. Intended? |
| Q-5as | ANSWERED in the pass |  | 616 | hygienist1 sees Reopen on the dentist's resolved alert and Replace document on the dentist's clearance. Check against the Permissi... |
| Q-5at | ANSWERED in the pass |  | 636 | should an open clearance warn before related treatment (for example an extraction waiting on a cardiology clearance)? Currently no... |
| Q-5au | OPEN | PKG-05 | 652 | billing1 can open History, Forms and Documents (may hold medical content). Check what they show. |
| Q-5av | OPEN | PKG-09 | 676 | the printed receipt does not show its number ("Receipt 1"); the on-screen one does. |
| Q-5aw | ANSWERED in the pass |  | 677 | untested: a second click on "Receipt for payment 5": does it offer the issued receipt or start another preview that could issue a ... |
| Q-5ax | OPEN | PKG-09 | 702 | is a partly refunded payment meant to be impossible to reverse or correct? |
| Q-5ay | OPEN | PKG-09 | 715 | should adjustments appear in aging? |
| Q-5az | ANSWERED or carried in a finding (see log) |  | 724 | confirm that a second issue would make Statement 2). |
| Q-5ba | OPEN | PKG-09 | 729 | a Reason beginning with "=" or "+" could run as an Excel formula; test with Reason =1+1 on an adjustment and see whether the CSV n... |
| Q-5bb | OPEN | PKG-09 | 737 |  |
| Q-5bc | ANSWERED or carried in a finding (see log) |  | 749 | where is the move recorded, the timeline?). F5 not reported. |
| Q-5bd | OPEN | PKG-11 | 751 | The Billing tab is visible to frontdesk1 (Q-5be: intended?). |
| Q-5be | OPEN | PKG-05 | 751 | intended?). |
| Q-5bf | OPEN | PKG-09 | 755 | is it right that responsibility cannot be set before the first charge? Needs a completed procedure for Alicia to test. |
| Q-5bg | ANSWERED or carried in a finding (see log) |  | 758 | can frontdesk issue a receipt, and a statement preview?). |
| Q-5bh | OPEN | PKG-05 | 764 | manager1's menu includes Recall settings, Outside parties, Import a patient, Reports and Permission Matrix. Is Permission Matrix m... |
| Q-5bi | ANSWERED in the pass |  | 775 | answered: the notification is "Important: A duplicate document was added" (11/4/2026, 12:40:07 PM), caused by the Add anyway impor... |
| Q-5bj | OPEN | PKG-12 | 767 | by design?). |
| Q-5bk | OPEN | PKG-12 | 776 | The Details history for the edit was not shown (still to check). |
| Q-5bl | ANSWERED in the pass |  | 794 |  |
| Q-5bm | ANSWERED in the pass |  | 817 | earlier API log showed "treatment_enqueued=1 ... treatment_closing=1" passes (the accepted-treatment task was created and closed a... |
| Q-5bn | ANSWERED in the pass |  | 828 | the second task (11/20) is not "Due soon" (0). What is the due-soon window (recall settings)? |
| Q-5bo | OPEN | PKG-13 | 825 | ; the follow-up-due notification went to manager1 only and was not marked read after completing the task (Q-5bp). |
| Q-5bp | ANSWERED in the pass |  | 834 | answered: a notice stays unread until the user reads it; completing the task does not clear it. Treat as expected unless the requi... |
| Q-5bq | ANSWERED in the pass |  | 861 | which tooth numbering does this field use (FDI two-digit vs the odontogram's)? |
| Q-5br | OPEN | PKG-13 | 0 |  |
| Q-5bs | OPEN | PKG-12 | 877 | Its Details history shows only Imported/Opened entries, one "Opened by Staff member" (the role name instead of the person; compare... |
| Q-5bt | OPEN | PKG-13 | 884 |  |
| Q-5bu | OPEN | PKG-13 | 885 | is the session lifetime fixed rather than sliding?). |
| Q-5bv | OPEN | PKG-13 | 897 | in the History screenshot the sidebar badge shows 1 again after "Nothing unread" showed no badge. Screenshot order, or a lagging c... |
| Q-5bw | OPEN | PKG-13 | 902 | intended? |
| Q-5bx | OPEN | PKG-13 | 910 | does a due follow-up re-notify every few minutes? confirm period). |
| Q-5by | OPEN | PKG-13 | 914 | does a message to dentist1 create a notice? |
| Q-5bz | ANSWERED in the pass |  | 922 | which of these were really in the database already (from the earlier registration tests)? If Alicia was not, then a near-duplicate... |
| Q-5ca | ANSWERED in the pass |  | 939 | answered (owner): the 1997 Carla Chen was entered by hand with a typo, not deliberate. This is exactly the realistic case: a typo ... |
| Q-5cb | OPEN | PKG-11 | 957 | Name differs (Alice vs Alicia) and Email differs (None vs alice.anderson@...) but only Phone is listed under "Fields that differ".... |
| Q-5cc | ANSWERED in the pass |  | 969 | S3 if real): Completed procedures puts all 4 procedures on 2026-11-03, but every appointment in range is on 2026-10-30 or 2026-11-... |
| Q-5cd | ANSWERED in the pass |  | 970 | Patient flow "Moves recorded" shows Checked in 3, Ready 1, Seated 1, Checked out 1 but Completed 3 (visits can skip states?). And ... |
| Q-5ce | OPEN | PKG-13 | 971 | Measurement "Clinical notes not finalized 0 of 0 / Not available" although visits and notes exist in range; check what the denomin... |
| Q-5cf | OPEN | PKG-10 | 991 | compliance, triage): opening a patient chart does not appear to leave an audit row (DocumentOpened and ReportViewed exist, no pati... |
| Q-5cg | ANSWERED in the pass |  | 1015 | ANSWERED: the unread notification was "Important: A duplicate document was added 11/4/2026, 12:40:07 PM" (a real document-duplicat... |
| Q-5ch | OPEN | PKG-12 | 1027 | owner question): can old backups be deleted? UI has no delete button for a backup. Retention does it (Backups to keep = 7; the new... |
| Q-5ci | ANSWERED in the pass |  | 1049 | evidence (Firefox devtools): the two cookies (.AspNetCore.Cookies 665 bytes, Alveara-CSRF 167 bytes; HttpOnly true, SameSite Stric... |
| Q-5cj | ANSWERED in the pass |  | 1064 | does admin see a provider box on the same report? |
| Q-5ck | ANSWERED in the pass |  | 1102 | should a hygienist be able to resolve an alert (here one raised for hygiene)? Check the Resolved alerts detail for who resolved it... |
| Q-5cl | SKIPPED for the second pass | PKG-16 | 1147 | does manager1 get a read-only or an editable Permission Matrix? And what does the manager's Documents tab list (clinical documents... |
| Q-5cm | ANSWERED in the pass |  | 1124 | should the manager's menu show them, or should the matrix not grant them? URL test pending. |
| Q-5cn | OPEN | PKG-12 | 1126 | compliance candidate): manager1 has no ViewClinicalDocumentation, yet the Documents tab lists and offers Open, Download, Preview, ... |
| Q-5co | ANSWERED or carried in a finding (see log) |  | 1154 | WITHDRAWN: the 14 vs 26 difference is Universal vs FDI numbering for the same tooth, already logged as F-099 / F-104 (display only... |
| Q-5cp | ANSWERED in the pass |  | 1174 | what is recorded when "Who gave this answer?" is left empty (does the item say "Answer given by ..." with a blank, or by the patie... |
| Q-5cq | ANSWERED in the pass |  | 1207 | both reasons start with the word "patient" ("patient planing for later time", "patient changed mind"). Confirm whether the owner t... |
| Q-5cr | ANSWERED in the pass |  | 1262 | the documents list shows Receipt 1 (payment 5) and Receipt 2 (payment 5), but payment 5 was reversed (entry 7). Is it right to hav... |
| Q-5cs | OPEN | PKG-09 | 1269 | payment 5 has two receipts (Receipt 1 on 11/3 and Receipt 2 on 11/4 6:41) but payment 17 got one. Why was a second receipt issued ... |
| Q-5ct | ANSWERED in the pass |  | 1273 | check first): Daniel Diaz's page showed "Guarantor: Carla Chen" and a household with Carla (Parent), yet Carla's "guarantor for" l... |
| Q-5cu | ANSWERED in the pass |  | 1291 | Dentist shows "2 enabled" but only dentist1 was created in this run. Who is the second enabled Dentist account (check Security Adm... |
| Q-5cv | ANSWERED in the pass |  | 1299 | S4): assistant1 sees "Prescription composer" in the tab bar. The matrix grants Assistant ViewPrescriptions; confirm whether the co... |
| Q-5cw | ANSWERED in the pass |  | 1340 | ANSWERED (admin, Document Storage after the restore): status Documents 0, Intact 0, Missing 0, Damaged 0, Not checked yet 0, "No f... |
| Q-5cx | OPEN | PKG-12 | 1340 | disk count was 10 but 8 are listed; the other 2 may be non-document files or inside a subfolder (check at triage). The "That patie... |
| Q-065a | OPEN | PKG-05 | 237 | hygienist1 can start and complete a visit that belongs to Dr. Dana (Filling) - the board does not tie the action to the booked pro... |

## 10. Appendix C: observations (O-x)

| Observation | Home package | Log line | Excerpt |
|---|---|---|---|
| O-E6a | PKG-14 | 130 | in the third screenshot Monday is back to 09:00 AM to 05:00 PM and Save is greyed, but the red "A window must end after it starts.... |
| O-E6b | PKG-14 | 131 | Save is enabled for an invalid row (harmless, since the click is refused with a message; optional to disable it). |
| O-E7a | PKG-14 | 133 | three names for the practice (sidebar "Alveara Dental", Practice tab "Baghdad Test Dental", location "Mansor White Dental"); proba... |
| O-E8a | PKG-14 | 134 | the help text under Code ("A practice code must not look like a CDT code (D followed by four digits)") stays visible when CDT code... |
| O-E8b | PKG-14 | 134 | the session-expiry notice appeared again during this step (expected at the 30-minute deadline; see the earlier session finding). |
| O-E8c | PKG-14 | 136 | , low). (c) D1110 as Practice code: "That looks like a CDT code. Choose the CDT code system for it, or give the local code a diffe... |
| O-E9a | PKG-14 | 139 | , low). Name, date of birth, phone and address are always required; the "Also require" box offers two optional boxes, "An email ad... |
| O-E10a | PKG-14 | 140 | to verify): the fee history earlier showed the L1000 creation time as 3:38:45 PM (practice time, America/Chicago) while the PC clo... |
| O-G1a | PKG-14 | 142 | low): the message names the internal permission code. Finding F-031 (medium, release polish): "Component Showcase" (a developer pa... |
| O-G1b | PKG-14 | 142 | "System Status" is visible to every role; confirm that is intended and what it shows. Questions for triage against the Permission ... |
| O-G2a | PKG-11 | 145 | low): the confirmation shows only a raw UUID; a short human patient number would be easier for staff to quote on the phone. Second... |
| O-G2b | PKG-11 | 145 | low): the same message appears twice (banner and panel). Question D-02 for triage: the duplicate is a hard block with no "this is ... |
| O-G2c | PKG-11 | 148 | low): the panel's "Why flagged" for the entered patient shows a dash; fine. Not yet tested: "Register anyway" itself (does the aud... |
| O-G2d | PKG-11 | 153 | low) the relationship list is chosen as "Their relationship to this household", wording is hard to follow; Alice shows as Spouse a... |
| O-G2e | PKG-11 | 153 | low) the "Remove Brian from household" button sits beside "Add to household", easy to hit by mistake (does it ask to confirm?). To... |
| O-G2f | PKG-11 | 154 | , low, a rendering glitch, maybe a status placeholder), and a blue Patient safety panel: "No alerts, active allergies or current m... |
| O-G2g | PKG-11 | 154 | low): grammar "Medical history have not been reviewed". |
| O-G2h | PKG-11 | 155 | low): it is the browser's own dialog (title "localhost:5173"), not the app's styled dialog, and OK is the highlighted default; use... |
| O-G2i | PKG-11 | 161 | low): the blocked exact-duplicate attempts (Alice twice, "alice anderson") are not logged; consider logging blocked attempts (usef... |
| O-G2j | PKG-11 | 161 | low): household entries lack the relationship (Spouse) and the other party; "Household membership set." on Alice's record at 2:08:... |
| O-G2k | PKG-11 | 161 | low): the page says "account, role, and session events" but also lists configuration, procedures, patients and reports; retitle "A... |
| O-G3a | PKG-11 | 163 | low): the guarantor has the same birth date as the patient (test data); no warning is given, acceptable. Audit entry for the guara... |
| O-G3b | PKG-11 | 163 | low): the list has no Sex, no patient number and no guarantor/household column; with two Andersons of the same birth date, a patie... |
| O-G3c | PKG-11 | 163 | low): no paging or count total beyond "N patients shown" (test with many patients later). (c) Edit: Brian's phone changed to 555-0... |
| O-G3d | PKG-11 | 165 | low/medium): the left menu now has about 25 entries in one flat list (clinical, scheduling, admin, reports mixed) and needs scroll... |
| O-G3e | PKG-11 | 167 | low): native browser dialog again (O-G2h); O-G3f (low): the Inactivate button is at the very bottom of the long Details page. Ques... |
| O-G3f | PKG-11 | 167 | low): the Inactivate button is at the very bottom of the long Details page. Question to check: what happens to Alicia's future app... |
| O-G3g | PKG-11 | 167 | to verify): Alicia's Sex is stored and shown as "female" in lowercase on Details and in the bar, while the form had "Female" at th... |
| O-G3h | PKG-11 | 168 | low): "Parent" is stored as Carla's relationship; the screen shows Carla as "Parent" and Daniel as "Self", so the direction is rea... |
| O-G3i | PKG-11 | 168 | low): the guarantor is a separate concept from the household; after adding Carla to the household the guarantor was already Carla ... |
| O-G4a | PKG-06 | 178 | On 2026-11-04 the shading runs 07:00-09:00 for Dr. Dana. To confirm with owner whether weekly hours were set to 09:00 (guide sugge... |
| O-G4b | PKG-06 | 179 | The booking form's date is independent of the calendar date, and the calendar jumps to the booked day after saving. Good. |
| O-G4c | PKG-06 | 180 | Confirmation line "Appointment booked: Brian Baker with Dr. Dana, 2026-11-04 09:00-09:30." appears under the calendar heading. No-... |
| O-G4d | PKG-06 | 181 | Date formats differ across the screen (10/31/2026 pickers, 2026-11-04 headings, 10/31/2026 7:18:52 PM history). Cosmetic. |
| O-G4e | PKG-06 | 182 | Calendar block shows only time and patient name (no type). |
| O-G4i | PKG-06 | 210 | Visit board is 8 columns wide; the last column(s) are cut off at normal width and need a horizontal scrollbar. Board shows useful ... |
| O-G4j | PKG-06 | 212 | Calendar behind the booking panel stays on today (Saturday 10/31) while the form targets 11/04; no jump to the target date until a... |
| O-G4k | PKG-06 | 218 | Board announces each action in text ("Carla Chen is now confirmed.", "...checked in."), shows "Updated <time>", has a Refresh now ... |
| O-G4f | PKG-06 | 219 | Error messages never name the other patient (good privacy), but also do not say which operatory/patient; acceptable. |
| O-G4g | PKG-06 | 220 | Top bar still says "No patient selected" throughout all appointment work (F-034/F-045 confirmed again). |
| O-G4h | PKG-06 | 221 | Week view truncates names ("Brian ...") and has no visible tooltip in the screenshots. |
| O-G4l | PKG-06 | 230 | "In this step" timer counts from check-in, not from the room change (23 min, then 27 min after the changes; 2 h 53 min at 00:30 ne... |
| O-G4m | PKG-06 | 231 | Orange "Your session will expire soon" banner appeared for frontdesk1 after about 28 min (works as designed, F-0xx session notes). |
| O-G4n | PKG-06 | 238 | positive): Complete visit asks for confirmation with "cannot be undone" wording and a "Keep it open" choice. Check in (F-057) has ... |
| O-G4o | PKG-06 | 240 | Schedule page as hygienist1: "Your role can view the schedule but not book appointments." Shows "Appointments on 2026-11-02" (defa... |
| O-G4p | PKG-06 | 266 | positive, and a consistency point): no-show is correctly gated by time, whereas Check in, Start treatment, Complete visit and Chec... |
| O-5c | PKG-02 | 306 | session-expiry banner "Your session will expire soon. Save your work and sign in again to continue." appeared for dentist1 after a... |
| O-5d | PKG-06 | 323 | positive): "Review and finalize" opens "Review before finalizing" with: "Once finalized, this note cannot be changed. If something... |
| O-5e | PKG-06 | 345 | the "severity is not assumed" wording is good. |
| O-5f | PKG-06 | 355 | Sign and Finalize are separate recorded events 18 s apart, the false "none reported" and its clearing are both kept. Gaps: no acto... |
| O-5g | PKG-06 | 375 | a change forces a fresh acknowledge). "Resolve" asks "Why is 'Before invasive work' resolved?" (reason box, "The alert stays in th... |

## 11. What approval of R04 authorizes

1. The package list and order (section 3), starting with PKG-01 and the read-only part of PKG-15, once the execution plan and execution status are deliberately updated to authorize them.
2. The home package and disposition of every item in Appendices A to C, subject to the reviewer's corrections.
3. The release gate in section 0 and the coverage-map rules in section 6.
4. That each owner decision in section 5 is answered by the owner before the package that needs it starts. PD-15, PD-17, PD-18 and PD-25 are explicitly the owner's.
No code is written and the execution plan and execution status are not changed until this plan is independently approved.


## 12. Change log R03 to R04 (every corrected mapping and reference)

No package scope, package order, owner-decision ownership, deferred-AI state or release sequence changed.

### 12.1 Corrected package references
| Where in R03 | R03 said | R04 says | Why |
|---|---|---|---|
| 6.1 case 3.12 | transition matrix owned by PKG-02 | PKG-05 | the visit-transition matrix is PKG-05 |
| 6.1 case 13.05 | unsaved-form preservation asserted in PKG-15 | PKG-02 (outage behavior); stage owned by PKG-12 | PKG-15 is dependency hygiene |
| 6.3 D1 to D6 | in-app account route follows PKG-02 | PKG-14 | onboarding (F-016) is in PKG-14 |
| 6.3 steps 118-119 | boundary/audit verification under PKG-02 | PKG-05 | F-170 verification task is in PKG-05; stages 13 (PKG-10) and 10 (PKG-12) assert the behavior |
| 6.3 steps 125-127 | blank-date regression in PKG-07 | PKG-08 | treatment-plan scheduling is PKG-08 |
| 6.3 steps 139-146 | payer regression in PKG-06 | PKG-09 | billing is PKG-09 |

The cause was that these strings carried an earlier numbering and were renumbered mechanically. All package references in section 6 are now written directly and checked against the package table and the stage-to-package table.

### 12.2 The four examples in the review
| Raw-log line | R03 | R04 |
|---|---|---|
| 255 same-patient / board conflict re-test | Clinical | 3 Scheduling, PKG-05 (cases 3.04, 3.12; F-061, F-072) |
| 385 clinical template and permission segment | Billing | 5 Clinical, PKG-06 (template/required section) with the frontdesk/billing refusal asserted by PKG-10 (cases 4.05, 4.07) |
| 472 hygienist diagnosis check | Charting only | 6 Charting; permission asserted by PKG-05 and the diagnosis record by PKG-06, with role refusals in stage 13 (PKG-10) (case 5.08) |
| Steps 118-119 row | note pointed to PKG-02 | PKG-05 (see 12.1) |

### 12.3 Every segment whose stage changed (R03 stage to R04 stage)
| Log line | Segment | R03 stage | R04 stage | Package asserting |
|---|---|---|---|---|
| 97 | Input for Release Engineering (stories 56-58), from F-015 (only if the mid-run runtim... | 0 Runner and environment | n/a | - |
| 105 | Vite console during S-07 (owner paste) | 0 Runner and environment | 1 Setup and sign-in | PKG-02 |
| 121 | Part E progress (practice setup) | 1 Setup and sign-in | n/a | - |
| 255 | Section 4 - Same-patient and board conflict re-test (frontdesk1), 2026-11-01 09:15-09... | 5 Clinical | 3 Scheduling | PKG-05 |
| 385 | Section 5h - Required template section, permission checks (dentist1, frontdesk1, bill... | 9 Billing | 5 Clinical | PKG-06 |
| 428 | Section 5m - Implants, restorations, withdraw reason, treatment-plan link (dentist1, ... | 5 Clinical | 6 Charting | PKG-06 |
| 438 | Section 5n - Duplicate filling re-test (dentist1, Alice, tooth 5), 2026-11-02 | 5 Clinical | 6 Charting | PKG-06 |
| 443 | Enhancement idea E-01 (owner, 2026-11-02): tooth drawings instead of tiles | 6 Charting | n/a | - |
| 517 | 5x. Tooth numbering explained, plan form needs tooth (2026-11-03) | 7 Treatment plans | 6 Charting | PKG-06 |
| 783 | 6d. Step 59: manager1 admin pages, document settings, storage check | 10 Documents and storage | 13 Permissions and audit | PKG-10 |
| 791 | 6e. Step 60: form templates and signing (8.12) | 5 Clinical | 10 Documents and storage | PKG-12 |
| 1059 | 7ae. frontdesk1 and Reports (12.04, 2026-11-07) | 12 Reports and measurement | 13 Permissions and audit | PKG-10 |
| 1071 | 7af. Section 14 usability checks as frontdesk1 (2026-11-07) | all stages | n/a | PKG-14 |

### 12.4 Rows added and cases reclassified
- **New sub-rows (one per manual step, from the three long segments):** lines 35, 123, 125, 126, 128, 129, 130, 131, 132, 133, 134, 135, 136, 139, 140, 141, 142, 143, 145, 148, 153, 155, 163, 167, 168, 1107, 1108, 1112, 1118, 1120, 1122, 1128, 1132, 1136, 1140, 1157, 1158, 1165, 1169, 1175, 1181, 1191, 1198, 1203, 1211, 1232, 1238, 1242, 1243, 1245, 1247, 1251, 1255, 1257, 1258, 1260, 1265, 1270, 1272, 1280, 1285, 1294, 1300, 1302, 1303, 1304, 1309, 1312, 1319, 1324, 1328, 1332, 1333, 1344, 1350.
- **Test cases whose stage or type changed from R03:**
  - 1.08: 1 Setup and sign-in / CHAIN -> 13 Permissions and audit / CHAIN (PKG-10)
  - 4.01: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-06)
  - 4.02: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-06)
  - 4.03: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-07)
  - 4.04: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-07)
  - 4.05: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-06)
  - 4.06: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-06)
  - 4.08: 4 Visit flow / CHAIN -> 5 Clinical / CHAIN (PKG-05)
  - 5.01: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.02: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.03: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.04: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.05: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.06: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.07: 5 Clinical / CHAIN -> 6 Charting / CHAIN (PKG-06)
  - 5.08: 13 Permissions and audit / CHAIN -> 6 Charting / CHAIN (PKG-05)
  - 7.12: 9 Billing / CHAIN -> 13 Permissions and audit / CHAIN (PKG-10)

### 12.5 Dependency conflicts exposed by the corrections
None. The corrected rows keep every assertion inside the package that already owns the finding; where an assertion lives in a stage built by another package (for example permission refusals in stage 13, built by PKG-10) the row names both.

### 12.6 Statement of method and its limits
All 142 raw-log segments were read in the digest form (heading, first lines, users, case ids, finding ids); the long segments (steps 113 to 161) and every segment I was unsure of were read in full. Assertions are specifications of what the automated test proves, not test code. The reviewer should still spot-check any row; wrong rows are traceability defects to be fixed in R05 without reopening scope.
