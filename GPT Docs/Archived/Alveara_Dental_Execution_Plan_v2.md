# START HERE

# Alveara Dental — Linear Execution Plan

## STEP 0 — EXECUTION-CONTROL BOOTSTRAP

**This is the first action. Execute this before STORY-000.**

Give the coding agent this Execution Plan and the companion Engineering & Product Reference document, then give it the prompt below.

``` text
ALVEARA DENTAL — STEP 0
EXECUTION-CONTROL BOOTSTRAP

You are initializing the repository-side execution controls for Alveara Dental.

This is STEP 0. It is not a course story and not a product story.
DO NOT implement application functionality.
DO NOT begin STORY-000.
DO NOT mark any story COMPLETE.
DO NOT modify course completion truth in .colaberry.

AUTHORITATIVE INPUTS

You have been given:
1. Alveara Dental — Execution Plan
2. Alveara Dental — Engineering & Product Reference
3. The current Alveara repository

The Execution Plan is authoritative for execution order and the exact ALV prompts.
The Engineering & Product Reference is authoritative for cross-cutting engineering/product rules and preserved course contracts.
The course portal is authoritative for current course-story prompts, Done Means, unlock state, and course completion.
Current code/tests are authoritative for what is actually implemented.

CREATE

.alveara/
├── EXECUTION_STATUS.json
├── HANDOFF_SCHEMA.md
├── BUILD_STATE.md
├── QUALITY_GATES.md
├── handoffs/
└── reviews/

1. EXECUTION_STATUS.json
- Create exactly 53 active first-release records matching the numbered first-release items in the Execution Plan.
- Include all 19 STORY-* items and all 34 first-release ALV items exactly once.
- Record story ID, type, parent when applicable, dependencies, status, attempt/review/evidence fields, blockers, revalidation state, and completion metadata needed by the workflow.
- Course records must declare externalVerification = "course_portal".
- Set only genuinely dependency-ready work READY; all other work starts PLANNED.
- Nothing may start COMPLETE or IN_PROGRESS.
- Future ALV-F* roadmap items are not active first-release records.

2. HANDOFF_SCHEMA.md
Create the durable ALV execution/review contract. It must include:
- normal lifecycle: PLANNED → READY → IN_PROGRESS → AWAITING_REVIEW → COMPLETE;
- exceptional states: BLOCKED, CHANGES_REQUIRED, DEFERRED, REOPENED, REVALIDATION_REQUIRED;
- immutable attempts R01, R02, R03...; never overwrite a prior attempt;
- implementation commit first, then evidence/status commit, then validated handoff ZIP;
- coding agent stops at AWAITING_REVIEW and never self-certifies COMPLETE;
- reviewer decision values APPROVED, CHANGES_REQUIRED, BLOCKED, REOPENED;
- approval closure stores the review under .alveara/reviews/<ALV-ID>/<attempt>.md, updates status to COMPLETE, and makes a closure-only commit before the next story;
- CHANGES_REQUIRED preserves the rejected attempt and starts the next attempt of the same story;
- BLOCKED records truthful blocker evidence and never fabricates missing evidence;
- reopened dependencies trigger targeted downstream REVALIDATION_REQUIRED analysis rather than blindly reopening everything;
- canonical ZIP naming: Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip;
- package must contain the canonical manifest/current state/status/gate snapshots, exact test results, acceptance evidence, parent regression evidence or N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact;
- never package secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, real-data backups, dependency caches, or irrelevant build output;
- a committed file is never required to contain the SHA of the commit that contains that same file.

Persist the Standard Production Prompt Rules from the Engineering & Product Reference into this control contract so later ALV prompts can rely on them without the user re-supplying those rules manually.

3. BUILD_STATE.md
- Record current implemented truth only.
- At bootstrap, state honestly that production implementation has not begun unless repository inspection proves otherwise.
- Do not turn this file into a roadmap or claim planned features exist.

4. QUALITY_GATES.md
- Create Gate A through Gate F sections using the gate definitions in the Execution Plan.
- Each criterion must support test/measurement method, environment/dataset, pass/block threshold or rubric, current state, evidence reference, date, and notes/blockers.
- Do not mark any gate passed during bootstrap.
- Do not invent measurements or lower thresholds to manufacture a pass.
- Before later gate evaluation, predeclare the critical workflows and target/rubric for accessibility; measurable response/throughput/resource thresholds and representative data volumes for performance; the scanner/rubric and blocking severity policy for security; the persistent asset classes and representative records/files that must survive recovery drills; and expected source/target counts, totals, and tolerances for reconciliation checks.
- If a threshold legitimately changes later, record the reason and date. Never silently rewrite a failed gate into a pass.

5. DIRECTORIES
- Create .alveara/handoffs/ and .alveara/reviews/ with placeholders only if Git requires them.
- Do not create fake handoffs or fake reviews.

COURSE SEPARATION

.colaberry/ remains course/platform-controlled.
Do not repurpose it for production tracking.
Do not falsely modify course progress to unlock work.

VALIDATE BEFORE COMMITTING

Verify all of the following:
- required .alveara files/directories exist;
- EXECUTION_STATUS.json parses as valid JSON;
- exactly 53 active first-release records exist;
- exactly 19 are course STORY-* records;
- exactly 34 are first-release ALV records;
- every numbered first-release ID in the Execution Plan appears exactly once;
- no duplicate active IDs exist;
- ALV-009-C02 includes STORY-009 as a dependency;
- nothing is COMPLETE or falsely IN_PROGRESS;
- no gate is marked passed;
- no fake evidence exists;
- no product feature was implemented;
- .colaberry was not repurposed or falsely advanced;
- HANDOFF_SCHEMA contains attempts, review closure, BLOCKED, CHANGES_REQUIRED, revalidation, package safety, and the Git self-reference prohibition.

If a material contradiction exists between the supplied documents, repository, and portal truth, STOP and report it instead of guessing.

COMMIT AND BOOTSTRAP HANDOFF PACKAGE

After successful bootstrap implementation and validation:

1. Create one dedicated bootstrap implementation commit:
   `ALV-CONTROL: initialize execution control`

   Capture its full SHA as the bootstrap implementation SHA.
   Do not mix product implementation into this commit.

2. Create the bootstrap review evidence in:
   `.alveara/handoffs/ALV-CONTROL/R01.md`

   This bootstrap handoff must record:
   - purpose and scope of STEP 0;
   - bootstrap attempt `R01`;
   - bootstrap implementation SHA;
   - branch;
   - files/directories created;
   - exact 53/19/34 ledger counts;
   - initial READY item(s);
   - every validation check and result;
   - `.colaberry` impact;
   - Git working-tree state;
   - discrepancies/blockers;
   - confirmation that no product feature was implemented;
   - confirmation that no story or gate was falsely completed;
   - next authorized execution item;
   - any review-relevant limitation or unresolved question.

3. Create bootstrap evidence files under:
   `.alveara/handoffs/ALV-CONTROL/R01-evidence/`

   At minimum create:
   - `VALIDATION_RESULTS.md` — every STEP 0 validation check, method, and PASS/FAIL result;
   - `CHANGED_FILES.md` — all bootstrap-created/changed files and why;
   - `GIT_STATE.md` — branch, bootstrap implementation SHA, current clean/dirty state, and relevant commits;
   - `CONTROL_INVENTORY.md` — required `.alveara` files/directories, 53/19/34 counts, story-ID uniqueness/dependency checks, and initial READY state;
   - `COURSE_SEPARATION.md` — evidence that `.colaberry` was not repurposed or falsely advanced.

4. Update `.alveara/EXECUTION_STATUS.json`, `.alveara/BUILD_STATE.md`, and `.alveara/QUALITY_GATES.md` only as required to truthfully record bootstrap/control state. Do not mark a product story `AWAITING_REVIEW` merely because STEP 0 itself is under review; STEP 0 is not one of the 53 product stories.

5. Create one dedicated bootstrap evidence commit:
   `ALV-CONTROL: prepare bootstrap review evidence`

   Capture its full SHA as the bootstrap evidence SHA.

   The repository handoff/evidence files committed by this evidence commit are not required to contain that evidence commit's own SHA. The generated ZIP can record it after the commit exists.

6. Generate exactly one user-facing bootstrap handoff ZIP:

   `Alveara_Handoff_ALV-CONTROL_<implementation-short-sha>_R01.zip`

   The ZIP must contain:

   ```text
   Alveara_Handoff_ALV-CONTROL_<implementation-short-sha>_R01.zip
   ├── HANDOFF.md
   ├── MANIFEST.json
   ├── BUILD_STATE.md
   ├── EXECUTION_STATUS.json
   ├── HANDOFF_SCHEMA.md
   ├── QUALITY_GATES.md
   ├── evidence/
   │   ├── VALIDATION_RESULTS.md
   │   ├── CHANGED_FILES.md
   │   ├── GIT_STATE.md
   │   ├── CONTROL_INVENTORY.md
   │   └── COURSE_SEPARATION.md
   └── review/
       └── NEXT_ACTION.md
```

Package construction rules:

- `HANDOFF.md` is the bootstrap attempt handoff from `.alveara/handoffs/ALV-CONTROL/R01.md`.
- `BUILD_STATE.md`, `EXECUTION_STATUS.json`, `HANDOFF_SCHEMA.md`, and `QUALITY_GATES.md` are snapshots from the evidence commit.
- `MANIFEST.json` is generated after the evidence commit and must record:
  - package schema/version;
  - package type `bootstrap_control_handoff`;
  - bootstrap ID `ALV-CONTROL`;
  - attempt `R01`;
  - branch;
  - full bootstrap implementation SHA;
  - full bootstrap evidence SHA;
  - timestamp and timezone;
  - complete package inventory;
  - 53/19/34 record counts;
  - validation summary;
  - blocker/discrepancy summary;
  - `containsSecrets: false`;
  - `containsRealPHI: false`.
- `evidence/GIT_STATE.md` in the generated ZIP must record both the implementation SHA and evidence SHA plus clean/dirty state.
- `review/NEXT_ACTION.md` must state that the reviewer should return `APPROVED` or `CHANGES_REQUIRED`; `APPROVED` authorizes item 1 — STORY-000, while `CHANGES_REQUIRED` requires STEP 0 correction and a new immutable attempt (`R02`, then `R03`, etc.).
- Never package secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, real-data backups, dependency caches, or irrelevant build output.
- Do not package the entire repository.

7.  Reopen/list the generated ZIP and validate:

    - every mandatory member exists exactly where required;
    - `MANIFEST.json` parses;
    - packaged `EXECUTION_STATUS.json` parses and still contains exactly 53 active first-release records: 19 course + 34 ALV;
    - manifest SHAs match the actual implementation/evidence commits;
    - packaged control files match the evidence-commit snapshots;
    - no forbidden/sensitive content is included;
    - the ZIP is readable and non-empty.

    If package validation fails, correct the evidence/package and regenerate it before returning success.

8.  STEP 0 review attempts are immutable. If a reviewer later returns `CHANGES_REQUIRED`, preserve `R01` and its ZIP, correct STEP 0 only, increment to `R02`, create new implementation/evidence commits as applicable, and generate: `Alveara_Handoff_ALV-CONTROL_<implementation-short-sha>_R02.zip` Repeat similarly for later attempts. Never overwrite a prior bootstrap handoff, evidence, review decision, or ZIP.

FINAL RESPONSE

Return:

- bootstrap result: SUCCESS or BLOCKED;
- bootstrap attempt ID;
- full bootstrap implementation SHA;
- full bootstrap evidence SHA;
- branch;
- files created;
- 53/19/34 record counts;
- initial READY item(s);
- validation summary;
- `.colaberry` impact;
- working-tree state;
- discrepancies/blockers;
- exact bootstrap handoff ZIP filename/path;
- ZIP validation result;
- next authorized execution item after review approval.

The expected next authorized item after successful bootstrap **and reviewer approval** is STORY-000.

STOP AFTER GENERATING AND VALIDATING THE BOOTSTRAP HANDOFF ZIP. DO NOT EXECUTE STORY-000. DO NOT REQUIRE THE USER TO MANUALLY COLLECT INDIVIDUAL `.alveara` FILES FOR REVIEW. THE USER SHOULD NEED TO UPLOAD ONLY THE GENERATED BOOTSTRAP HANDOFF ZIP.


    **STOP HERE after STEP 0. Do not begin item 1 yet.**

    **STEP 0 REVIEW ACTION:** Upload **only the generated `Alveara_Handoff_ALV-CONTROL_<implementation-short-sha>_<attempt>.zip`** to the reviewing ChatGPT. Do not manually collect or attach individual `.alveara` files. The ZIP is the complete portable STEP 0 review package.

    Ask the reviewer to validate the bootstrap against the STEP 0 prompt, the Execution Plan, and the Engineering & Product Reference and return a decision artifact `Alveara_Review_ALV-CONTROL_<attempt>.md` with one of two decisions:

    - `APPROVED` → give that review artifact back to the coding/repository agent. The agent must store it under `.alveara/reviews/ALV-CONTROL/<attempt>.md`, make a closure-only commit `ALV-CONTROL: record bootstrap review approval`, report the closure SHA, and stop. **Only after that closure commit may you continue to item 1 — STORY-000.**
    - `CHANGES_REQUIRED` → give the review artifact back to the coding/repository agent. Preserve the rejected attempt/ZIP/review, repair STEP 0 only, increment the immutable attempt (`R01 → R02 → R03...`), generate and validate a new complete bootstrap handoff ZIP, and repeat review. Do not begin STORY-000 until STEP 0 is approved and approval is durably closed in the repository.

    # 1 — STORY-000 — Build your Command Center

    **ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

    Open `STORY-000` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

    When the portal has genuinely verified `STORY-000` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-000` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-000: record course completion`. Then continue directly to **item 2 — ALV-N001** below.

    # 2 — ALV-N001 — Alveara Application Shell and Design Foundation

    **Type:** New Production  
    **Dependencies:** COURSE STORY-000

    ### Full Coding-Agent Prompt

    **Story:** ALV-N001 — Alveara Application Shell and Design Foundation

    No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

    **Dependencies:** COURSE STORY-000

    **User story:** As any Alveara user, I want a coherent responsive application frame and visual language so that every later workflow can grow inside one consistent dental product.

    **Why this story exists:** The course Command Center is not the product UI, but authentication/RBAC does not exist yet. This story creates only the UI foundation that can be built honestly before authentication and protects the course root surface.

    **Implementation scope**

    - Create the production web application in its own project/application path; do not replace the repository-root STORY-000 Command Center
    - Responsive shell frame, route/layout structure, primary workspace region, patient-context placeholder region, and module-registry/navigation structure that does **not** pretend current-user permissions already exist
    - Design tokens for typography, spacing, radius, elevation, icon sizing, density, focus, validation and semantic statuses
    - Build only the reusable controls immediately needed by the shell and the next authentication/foundation stories; do not pre-build an unused giant component library
    - Establish page-header, action, form-field, validation, loading, empty, error, disconnected and notification patterns
    - Keyboard navigation/focus treatment/accessibility baseline
    - Theme/token architecture that can accept later branding without rewriting components
    - Small component/pattern showcase for the controls that actually exist

    **UI/UX deliverables**

    - Working production shell, not a mockup
    - Desktop-first clinical/office density with responsive tablet behavior
    - High-frequency actions have a predictable location and avoid unnecessary modal chains
    - The course Command Center remains reachable/working in its original repository surface

    **Security, audit, and data-integrity requirements**

    - Do not hard-code fake roles, fake permissions, fake signed-in identity or fake patient data
    - Permission-aware navigation is explicitly deferred until authentication/RBAC exists
    - UI controls must be designed so later server authorization can drive them without structural rewrite

    **Failure paths to handle**

    - Unknown route
    - Empty module registry
    - Disconnected/local-server-unavailable shell state
    - Component validation/focus errors
    - Long labels and constrained tablet widths

    **Acceptance / stop condition**

    - The root STORY-000 Command Center still satisfies its course contract
    - The production Alveara app is structurally separate from that root course surface
    - Shell is usable at agreed desktop and tablet widths
    - Tokens and the initial reusable controls are demonstrated without pre-building unrelated feature components
    - At least one keyboard-only path and accessibility smoke check pass
    - No acceptance criterion depends on authentication/RBAC that has not been built yet

    **Required tests**

    - STORY-000 regression/smoke check
    - Shell/component tests
    - Keyboard/accessibility smoke test
    - Responsive layout smoke test
    - Course-root coexistence test/check

    **Out of scope / future extension** Real authorization-aware navigation/session UX is added by `ALV-N009`. Expand the component library only when real stories require new patterns.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-N001` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-N001/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N001: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N001_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 3 — ALV-N002** below.

    # 3 — ALV-N002 — Core Architecture, Local Deployment, Data Invariants, and Durable Background Work

    **Type:** New Production  
    **Dependencies:** ALV-N001

    ### Full Coding-Agent Prompt

    **Story:** ALV-N002 — Core Architecture, Local Deployment, Data Invariants, and Durable Background Work

    No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

    **Dependencies:** ALV-N001

    **User story:** As the product team, we want a modular local-first architecture with explicit data invariants so that the first practice can run reliably on a Windows LAN and future clients/integrations do not require rewriting the domain.

    **Why this story exists:** REQ-020 has no dedicated course story. Architecture mistakes in time, money, identity, offline semantics, persistence, or background work would contaminate every later feature.

    **Implementation scope**

    - Define client-independent domain/application/API boundaries
    - Local Windows-server runtime/deployment model and environment configuration
    - Persistent database, schema migrations, transaction boundaries and migration failure policy
    - Define first-release offline semantics: core workflows require no public internet while the local server/LAN is available; disconnected browser/server editing is not silently invented
    - Establish time model: practice timezone, unambiguous event instants, date-only values, appointment-local time conversion rules and daylight-saving test strategy
    - Establish authoritative money type/rounding/currency policy; never use binary floating-point for financial values
    - Establish identity boundaries between login account, staff profile and clinical provider profile
    - Establish storage-root/blob abstraction and integrity metadata seam for later documents without building the document module yet
    - Establish PHI-safe diagnostic/logging conventions
    - Establish one versioned, privacy-minimized operational measurement-event convention and helper/store for later success-metric instrumentation; events are observational, not authoritative clinical/financial data
    - Enforce server-owned database topology: LAN/browser clients use the application/API and never open the database file over a network share
    - Define a database-consistent snapshot/backup integration seam for the backup story rather than raw live-file copying
    - Implement a minimal durable local background-work mechanism with persisted job state, restart recovery, observable failures, bounded operation-specific retries and idempotency hooks; no distributed broker
    - Health/status endpoint and admin-visible service/database/job-runner status
    - Extension seams for future FHIR/HL7, imaging, eRx, payments, claims, AI and cloud hosting without building those integrations

    **UI/UX deliverables**

    - Small admin System Status page showing truthful app/server/database/background-runner/version/connectivity state
    - Distinguish “internet unavailable” from “local server unavailable” in the UI
    - No secrets or PHI payloads in status/diagnostics

    **Security, audit, and data-integrity requirements**

    - Secrets outside source control
    - Least-privilege service configuration
    - Secure LAN transport strategy documented and exercised where supported by the chosen stack
    - Database/storage permissions restricted to the server/service identity and approved maintenance path; clients have no direct database-file access
    - No unnecessary PHI in ordinary logs or operational measurement events
    - Background jobs cannot execute consequential work twice merely because the process restarts

    **Failure paths to handle**

    - Database unavailable/corrupt
    - Migration failure
    - Disk full/read-only
    - LAN client loses server connectivity
    - Public internet unavailable
    - Background worker restarts mid-job
    - Invalid/ambiguous local appointment time around timezone/DST transition

    **Acceptance / stop condition**

    - Application runs on the local Windows-server model and a LAN client can use the production shell with no public internet
    - Schema migration initializes and upgrades a test database with safe failure behavior
    - Time/timezone and money invariants are documented in code/tests and exercised
    - Disconnected-server behavior is explicit and does not pretend unsynchronized edits succeeded
    - A persisted test background job survives restart without duplicate side effects
    - A versioned sample measurement event can be recorded without duplicating the underlying clinical/financial record or requiring unnecessary PHI
    - LAN-client integration proves application/API access rather than direct database-file access
    - System Status reports truthful local service/database/background-runner state
    - Failure paths do not silently corrupt authoritative data

    **Required tests**

    - Migration tests
    - No-public-internet smoke test
    - Server-disconnect UI/API test
    - Transaction rollback test
    - Timezone/DST conversion tests
    - Money/rounding tests
    - Durable background-job restart/idempotency test
    - Measurement-event version/privacy-minimization test
    - Direct-database-access topology check/integration test
    - Health/status tests

    **Out of scope / future extension** Cloud hosting, true disconnected-client editing, desktop/mobile shells, multi-practice tenancy, and distributed job infrastructure build on these boundaries later.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-N002` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-N002/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N002: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N002_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 4 — STORY-001** below.

    # 4 — STORY-001 — Implement secure user authentication and RBAC

    **ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

    Open `STORY-001` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

    When the portal has genuinely verified `STORY-001` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-001` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-001: record course completion`. Then continue directly to **item 5 — ALV-001-C01** below.

    # 5 — ALV-001-C01 — Complete Authentication Security, MFA, Recovery, Session Controls, and Authorization Administration

    **Type:** Companion  
    **Parent:** STORY-001  
    **Dependencies:** STORY-001, ALV-N001, ALV-N002

    ### Full Coding-Agent Prompt

    **Story:** ALV-001-C01 — Complete Authentication Security, MFA, Recovery, Session Controls, and Authorization Administration

    Parent course story: STORY-001 — its completion contract is immutable and must keep passing.

    **Dependencies:** STORY-001, ALV-N001, ALV-N002

    **User story:** As an administrator, I want production-grade identity controls so that accounts, MFA, sessions and role permissions are safely managed.

    **Why this story exists:** Completes the security atoms dropped by the generated REQ while permanently preserving STORY-001 completion behavior.

    **Implementation scope**

    - MFA enrollment/challenge/recovery path
    - At least one supported first-release MFA factor and recovery path must function while the local server has no public-internet access (for example standards-based local-capable OTP/recovery-code mechanisms); email/SMS cannot be the only path
    - Account disable/enable
    - Password reset/recovery without storing recoverable passwords
    - Configurable session timeout and explicit sign-out/revocation
    - Granular permission matrix mapped to dentist, hygienist, assistant, front desk, billing, office manager, admin
    - Security-administration UI for users, roles and permission visibility
    - Audit security-sensitive account, MFA, recovery and role changes

    **UI/UX deliverables**

    - Login, lockout, MFA challenge, recovery and expired-session screens use the design system
    - Admin user list/detail with status, roles and MFA state
    - Clear permission-denied presentation

    **Security, audit, and data-integrity requirements**

    - Server/API authorization is authoritative
    - Rate-limit or throttle repeated authentication attempts as appropriate
    - Recovery artifacts/secrets are never logged
    - Recovery codes/secrets are stored and displayed using one-time/hashed-or-protected semantics appropriate to the chosen mechanism

    **Failure paths to handle**

    - MFA unavailable/invalid
    - Recovery token invalid/expired
    - Disabled account attempts login
    - Session expires during work
    - Unauthorized role/permission change

    **Acceptance / stop condition**

    - Original STORY-001 tests still pass unchanged
    - A configured MFA path can be enrolled and challenged
    - At least one MFA/recovery path is demonstrated successfully with public internet unavailable
    - Disabled user cannot authenticate
    - At least one role-allowed and role-denied action is proven at API/service level
    - Session timeout is enforced
    - Security administration changes are audited

    **Required tests**

    - Original STORY-001 tests as regression
    - MFA tests including no-public-internet challenge/recovery
    - Permission matrix tests
    - Session expiry tests
    - Account disable/recovery tests

    **Out of scope / future extension** Enterprise Entra/AD/SSO/passkeys can plug into the identity boundary later.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-001-C01` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-001-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-001-C01: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-001-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 6 — ALV-N009** below.

    # 6 — ALV-N009 — Authorization-Aware Navigation, Session UX, and Identity Context

    **Type:** New Production  
    **Dependencies:** STORY-001, ALV-001-C01, ALV-N001, ALV-N002

    ### Full Coding-Agent Prompt

    **Story:** ALV-N009 — Authorization-Aware Navigation, Session UX, and Identity Context

    No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

    **Dependencies:** STORY-001, ALV-001-C01, ALV-N001, ALV-N002

    **User story:** As an authenticated Alveara user, I want navigation and session behavior driven by my real permissions so that the shell exposes only appropriate modules while the server remains the security authority.

    **Why this story exists:** ALV-N001 deliberately avoids pretending RBAC exists before authentication. This story binds the production shell to the real auth/RBAC/session model.

    **Implementation scope**

    - Bind shell/navigation module registry to server-authoritative permissions/capabilities
    - Signed-in account identity/context presentation
    - Session timeout/expiry UX and re-authentication path consistent with ALV-001-C01
    - Permission-denied route/action states
    - Direct URL/deep-link authorization behavior
    - Global sign-out/account menu
    - Establish navigation extension contract for later modules without hard-coded role-name branching where capability checks are more appropriate

    **UI/UX deliverables**

    - Role/capability-aware navigation
    - Clear session-expiry warning/state without leaking data across users
    - Permission-denied page/action state
    - Keyboard-accessible account/session menu

    **Security, audit, and data-integrity requirements**

    - Server/API enforcement remains authoritative
    - Hidden navigation is not treated as authorization
    - Session transition clears protected UI state appropriately

    **Failure paths to handle**

    - Permission changes while user is signed in
    - Session expires during edit
    - Direct URL to unauthorized module
    - Server denies action the cached UI thought was allowed

    **Acceptance / stop condition**

    - Navigation is driven by real authorization data/capabilities rather than fake users or hard-coded display assumptions
    - Unauthorized direct routes/actions are denied server-side
    - Session expiry/sign-out removes protected application state
    - ALV-N001 shell/layout regression tests remain green
    - STORY-001/ALV-001-C01 authentication/RBAC behaviors remain green

    **Required tests**

    - Permission-matrix navigation tests
    - Direct-route/API denial tests
    - Session expiry/sign-out tests
    - UI-state clearing test
    - Upstream auth/shell regression

    **Out of scope / future extension** Staff/provider-specific context appears after ALV-N003 links accounts to personnel/provider profiles.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-N009` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-N009/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N009: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N009_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 7 — STORY-002** below.

    # 7 — STORY-002 — Establish audit logging for critical actions

    **ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

    Open `STORY-002` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

    When the portal has genuinely verified `STORY-002` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-002` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-002: record course completion`. Then continue directly to **item 8 — ALV-002-C01** below.

    # 8 — ALV-002-C01 — Shared Audit, Concurrency, and Record-Lifecycle Primitives

    **Type:** Companion  
    **Parent:** STORY-002  
    **Dependencies:** STORY-002, ALV-001-C01, ALV-N002

    ### Full Coding-Agent Prompt

    **Story:** ALV-002-C01 — Shared Audit, Concurrency, and Record-Lifecycle Primitives

    Parent course story: STORY-002 — its completion contract is immutable and must keep passing.

    **Dependencies:** STORY-002, ALV-001-C01, ALV-N002

    **User story:** As a compliance-minded practice, I want reusable traceability and conflict-detection primitives so that each later clinical and financial domain can apply safe history rules without inventing a new trust mechanism.

    **Why this story exists:** The course story proves role-change auditing. v2 overreached by assigning finalization semantics to clinical/financial records that did not exist yet. v3 establishes reusable primitives and lets each domain story own its lifecycle semantics.

    **Implementation scope**

    - Reusable audit service/event model with actor, timestamp, action, target/entity identity, reason/context and correlation metadata
    - Append-only/immutable-to-ordinary-users audit semantics
    - Optimistic concurrency/version-token primitives for mutable authoritative records
    - Shared conflict result/problem representation usable by APIs and UI
    - Reusable record-lifecycle interfaces/patterns for finalize/amend/addendum/void/reversal/inactivate **without declaring which pattern applies to a domain record that has not been built yet**
    - Idempotency/correlation primitives for consequential commands
    - Define policy for what happens when required audit persistence fails: consequential action must not silently succeed without its required audit trail
    - Provide transactional coupling between authoritative local writes and required audit evidence where feasible; otherwise provide an equivalent durable outbox/reconciliation pattern so a successful action cannot become permanently unaudited

    **UI/UX deliverables**

    - Basic permission-aware audit/history viewer for records/actions that already exist
    - Reusable stale-edit/conflict presentation pattern
    - Domain-specific finalization dialogs are deferred to the stories that own those records

    **Security, audit, and data-integrity requirements**

    - Audit read permissions separated from business write paths
    - Ordinary users cannot modify/delete audit events
    - Do not log secrets or unnecessary PHI
    - Do not invent clinical/financial lifecycle states merely to exercise the infrastructure

    **Failure paths to handle**

    - Required audit write fails
    - Stale concurrent update
    - Duplicate command/correlation key
    - Unauthorized audit access
    - Unknown lifecycle transition requested by a domain

    **Acceptance / stop condition**

    - Original STORY-002 tests still pass unchanged
    - Existing account/role changes use the shared audit path
    - A representative currently-existing mutable record demonstrates optimistic concurrency/stale-edit rejection
    - Shared lifecycle/idempotency primitives are unit-tested without pretending nonexistent clinical/financial records are already finalized
    - Audit failure policy and audit/business-write coupling are explicit and tested
    - Later domain stories can adopt the primitives without rewriting STORY-002 behavior

    **Required tests**

    - Original STORY-002 regression
    - Audit append/read authorization tests
    - Audit-write failure/rollback-or-durable-outbox test
    - Optimistic-concurrency race test
    - Idempotency primitive tests
    - Lifecycle primitive unit tests

    **Out of scope / future extension** Clinical-note signing, completed-procedure finalization, signed-form immutability and financial reversal semantics are implemented by their owning domain stories.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-002-C01` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-002-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-002-C01: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-002-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 9 — ALV-N003** below.

    # 9 — ALV-N003 — Practice, Staff, Provider, Operatory, and Scheduling Configuration

    **Type:** New Production  
    **Dependencies:** ALV-001-C01, ALV-002-C01, ALV-N002

    ### Full Coding-Agent Prompt

    **Story:** ALV-N003 — Practice, Staff, Provider, Operatory, and Scheduling Configuration

    No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

    **Dependencies:** ALV-001-C01, ALV-002-C01, ALV-N002

    **User story:** As a practice manager, I want the scheduling/operational structure of the practice configurable without code so that the first real patient and appointment workflows reflect how the office operates.

    **Why this story exists:** Practice configuration was dropped from v4. v2 also pulled future clinical/forms/payment/recall settings too early. v3 configures only what the next operational stories genuinely need.

    **Implementation scope**

    - Practice information and practice timezone/currency settings consistent with ALV-N002 invariants
    - Staff profiles and active/inactive state
    - Clinical provider profiles separate from login accounts, with explicit optional account↔staff/provider linkage
    - Location-aware model with one active location initially
    - Operatories/chairs
    - Appointment types and default durations
    - Provider availability and blocked time
    - Safe inactivation/history for referenced configuration
    - Reusable configuration service/UI pattern that later domain stories can extend with their own settings

    **UI/UX deliverables**

    - Admin Configuration hub for practice/staff/providers/location/operatories/appointment types/availability
    - Search/filter appropriate to the small v1 scope
    - Inline validation and unsaved-change protection
    - Preview scheduling-relevant configuration where useful

    **Security, audit, and data-integrity requirements**

    - Practice-manager/admin permissions
    - Material configuration changes audited through shared audit primitives
    - Historical references survive inactivation

    **Failure paths to handle**

    - Duplicate/conflicting staff/provider/operatory identifiers where prohibited
    - Invalid appointment duration/availability ranges
    - Attempt to destructively delete referenced configuration
    - User-account link points to inactive/missing account

    **Acceptance / stop condition**

    - Provider, operatory and appointment type/duration can be configured and immediately consumed by scheduling
    - Provider availability and blocked time are persisted in practice-local time semantics
    - User accounts, staff profiles and provider profiles remain distinct but linkable
    - Inactive referenced configuration remains historically resolvable
    - Material changes are audited

    **Required tests**

    - Configuration CRUD/validation tests
    - Account↔staff/provider linkage tests
    - Referenced-record inactivation tests
    - Timezone/availability tests
    - Scheduling-consumer integration smoke test

    **Out of scope / future extension** Note templates, form templates, recall defaults, payment methods and document categories are added by the domain stories that actually need them. Multiple active locations/location-specific overrides remain future work.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-N003` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-N003/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N003: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N003_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 10 — ALV-N004** below.

    # 10 — ALV-N004 — Encrypted Full-State Backup, Verification, Restore, and Recovery Operations

    **Type:** New Production  
    **Dependencies:** ALV-N002, ALV-002-C01

    ### Full Coding-Agent Prompt

    **Story:** ALV-N004 — Encrypted Full-State Backup, Verification, Restore, and Recovery Operations

    No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

    **Dependencies:** ALV-N002, ALV-002-C01

    **User story:** As a system administrator, I want verified encrypted backups and a proven full restore path so that the local practice can recover the application after data, disk, or server failure.

    **Why this story exists:** Backup/recovery disappeared from v4 but is a production safety requirement. A database-only copy is insufficient once documents/configuration exist, and encrypted backups are useless without a tested recovery-material strategy.

    **Implementation scope**

    - Scheduled encrypted backups using the durable background-work mechanism
    - Use maintained platform/library cryptography with authenticated encryption; do not implement custom encryption, key derivation or custom cryptographic formats
    - Use a database-consistent backup/snapshot mechanism supplied by the chosen database/application stack; never rely on blindly copying a live database file
    - Manual backup
    - Backup set/manifest capable of covering database plus application-managed document/blob storage and critical configuration/state as those assets exist
    - Retention policy
    - Integrity metadata and verification
    - Visible last successful and failed backup plus backup history
    - Success/failure notification mechanism appropriate to local deployment
    - Explicit encryption-key/recovery-material strategy; required recovery material must not exist only inside the encrypted backup
    - Configurable confidence/probation workflow allowing manual verification/test-restore cadence before fully trusting unattended scheduled backup
    - Restore to an isolated/safe target first
    - Corrupt/incompatible/wrong-key backup rejection
    - Post-restore validation
    - Full restore drill procedure and evidence
    - Backup/restore audit

    **UI/UX deliverables**

    - Backup & Recovery admin page with history, storage target, schedule/retention, last success/failure and verification state
    - Restore wizard with explicit target, compatibility and recovery-material checks
    - Prominent backup failure state without exposing encryption material
    - Clear indication of whether a backup set includes all currently managed persistent asset classes

    **Security, audit, and data-integrity requirements**

    - Backup/recovery administration strongly authorized and audited
    - Keys/recovery secrets never committed/logged or embedded only inside the backup they unlock
    - Encryption/key-handling implementation uses reviewed platform/library facilities rather than home-grown cryptography
    - Restore does not overwrite the active production data set without an explicit protected workflow

    **Failure paths to handle**

    - Backup destination unavailable/full
    - Backup interrupted mid-run
    - Verification/hash mismatch
    - Missing/wrong recovery material
    - Incompatible schema/application version
    - Document/blob missing from backup set
    - Restore validation fails
    - Notification delivery fails without marking backup itself failed if backup actually succeeded

    **Acceptance / stop condition**

    - Manual and scheduled encrypted backups work
    - Backup history truthfully distinguishes success, failure and verification state
    - A test backup/restore covers every persistent asset class that exists at that point in the project
    - Missing/wrong recovery material is handled safely and visibly
    - A verified restore drill can start the recovered application and validate representative records/assets
    - Backup success/failure is visible and notifications are testable
    - No required restore secret exists only inside the encrypted backup

    **Required tests**

    - Scheduled/manual backup tests
    - Background-job restart/idempotency test
    - Encryption/authentication/recovery-material tests
    - Database-consistent snapshot/restore test
    - Corrupt/incompatible backup tests
    - Full-state restore drill
    - Missing-document/blob recovery test when blob storage exists
    - Authorization/audit tests

    **Out of scope / future extension** Off-site backup targets and cloud disaster-recovery orchestration are future extensions; the local full-state backup/restore contract must work first.

    **When you finish — package this story for review**

    - Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
    - After implementation and all required tests/regressions pass, commit the implementation using `ALV-N004` in the message and capture the full implementation SHA.
    - Create/update `.alveara/handoffs/ALV-N004/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
    - Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N004: record engineering handoff` and capture the handoff/evidence SHA.
    - Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N004_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
    - Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
    - Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
    - In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

    **NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **GATE A** below.

    # GATE A — FOUNDATION READY

    **MANDATORY STOP. Do not begin the next numbered item until this gate passes.**

    ## Gate execution prompt

    ``` text
    ALVEARA DENTAL — GATE A — Foundation Ready

    Evaluate Gate A against the current repository and the exact gate evidence requirements below.

    Read .alveara/BUILD_STATE.md, .alveara/EXECUTION_STATUS.json, .alveara/QUALITY_GATES.md, all handoffs/reviews created since the prior gate, and the current code/tests. Do not rely on planning claims when implementation evidence disagrees.

    For every criterion:
    1. identify the exact test/check/measurement and representative environment/data;
    2. use the predeclared pass/block threshold or rubric;
    3. execute or inspect the evidence needed to judge it truthfully;
    4. record PASS, FAIL, or BLOCKED with evidence references in .alveara/QUALITY_GATES.md;
    5. do not lower a threshold after seeing a failure merely to make the gate pass.

    If every blocking criterion passes, record Gate A PASS with the evidence/commit information and report that the next execution item is authorized.

    If any blocking criterion fails or cannot be truthfully verified, record Gate A FAIL/BLOCKED, identify the responsible story/stories or missing evidence, and STOP. Do not begin the next phase. Repair/reopen/revalidate the responsible work first, then rerun this same gate.

    When the integrated verification is substantial, create a reproducible gate evidence package named Alveara_Gate_A_<evidence-short-sha>.zip. Never include secrets or real PHI.

    GATE A EVIDENCE REQUIREMENTS

    Required evidence before broad patient/clinical expansion:

    - Root STORY-000 Command Center still passes and coexists with the separate production app.
    - Production shell/design foundation is coherent and accessible.
    - Local server/database/migration/time/money/offline/background-job/database-topology invariants are tested.
    - Privacy-safe versioned measurement-event convention is available before later metric-producing stories rely on it.
    - Authentication/RBAC/MFA/session controls pass.
    - Authorization-aware navigation/session UX passes.
    - Shared audit/concurrency/lifecycle primitives pass without pretending later domain semantics already exist.
    - Practice/staff/provider/operatory/scheduling configuration works.
    - Encrypted full-state backup can be created, verified and restored for every persistent asset class that exists at this point.

    Return the gate decision, criterion-by-criterion results, evidence references, repository state/commit information, blockers if any, and the next authorized action.

**PASS → continue directly to item 11 — STORY-003. FAIL/BLOCKED → stop; do not move forward.**

# 11 — STORY-003 — Implement patient registration workflow

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-003` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-003` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-003` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-003: record course completion`. Then continue directly to **item 12 — ALV-003-C01** below.

# 12 — ALV-003-C01 — Complete Patient Identity, Household, Guarantor, and Registration Workspace

**Type:** Companion  
**Parent:** STORY-003  
**Dependencies:** STORY-003, ALV-N003

### Full Coding-Agent Prompt

**Story:** ALV-003-C01 — Complete Patient Identity, Household, Guarantor, and Registration Workspace

Parent course story: STORY-003 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-003, ALV-N003

**User story:** As front desk staff, I want complete patient identity and household management so that registration is safe, fast and financially usable.

**Why this story exists:** Completes the patient-registration atoms not proven by the course acceptance contract.

**Implementation scope**

- Family/household relationships
- Separate guarantor/responsible party
- Active/inactive patient state
- Duplicate candidate warning before create
- Safe edits with history/concurrency
- Patient search and identity summary
- Establish the persistent patient-context workspace/header/navigation extension point that later clinical, treatment, document, billing and task stories extend
- Patient switching/navigation must clear or reload patient-scoped state so information from the previous patient cannot remain visible
- Validation appropriate to configurable practice requirements

**UI/UX deliverables**

- Fast registration workspace with clear required fields and progressive sections
- Household/guarantor relationship editor
- Duplicate warning comparison panel
- Patient header identity card reused throughout app
- Shared patient-workspace navigation/status extension points expose only modules/statuses that actually exist at this point; no fake balances, diagnoses, alerts or unsigned-work counts

**Security, audit, and data-integrity requirements**

- Registration/edit permission checks
- Audit create/update/relationship changes

**Failure paths to handle**

- Likely duplicate
- Conflicting concurrent edit
- Invalid relationship
- Missing required data

**Acceptance / stop condition**

- Original STORY-003 tests still pass
- Household and guarantor can be represented independently
- Likely duplicate is warned without silent merge
- Active/inactive state is preserved
- Concurrent edit cannot silently overwrite another user's change
- Registration UI is usable keyboard-first
- Switching between two patients cannot leave stale identity or patient-scoped data visible in the shared workspace

**Required tests**

- Original regression
- Household/guarantor tests
- Duplicate warning tests
- Concurrency test
- Patient-context switch/isolation test

**Out of scope / future extension** Portal self-registration can use the same patient model later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-003-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-003-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-003-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-003-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 13 — ALV-N010** below.

# 13 — ALV-N010 — Versioned Forms, Consents, and E-Signature Foundation

**Type:** New Production  
**Dependencies:** ALV-003-C01, ALV-002-C01, ALV-N009

### Full Coding-Agent Prompt

**Story:** ALV-N010 — Versioned Forms, Consents, and E-Signature Foundation

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-003-C01, ALV-002-C01, ALV-N009

**User story:** As practice staff, I want versioned patient forms and signed consent snapshots early in the visit lifecycle so that privacy, financial and treatment-related forms do not arrive only after clinical work is already complete.

**Why this story exists:** The original vision includes forms/consents/e-signature. v2 placed the entire area late with document imports. v3 establishes the form/signature trust model earlier and lets STORY-010 later add the full document library.

**Implementation scope**

- Versioned form/template definition model
- Initial categories for privacy, financial, general consent and treatment-related forms without claiming legal completeness of template wording
- Patient/staff-assisted form completion
- Signer identity/relationship, timestamp and signature capture metadata
- Immutable signed snapshot bound to the exact template version and entered values shown at signing
- Template edits create a new version and never alter prior signed snapshots
- Form status/history and controlled void/inactivation/correction semantics
- Extension seam for later storage in full document library

**UI/UX deliverables**

- Form/template administration appropriate to first-release scope
- Patient form completion/signature view
- Pre-sign review screen
- Signed snapshot/history view
- Clear unsigned/draft/signed/void status

**Security, audit, and data-integrity requirements**

- Permissions for template administration and viewing signed forms
- Signature/snapshot actions audited
- Do not claim that generic e-signature capture alone establishes legal sufficiency for every consent scenario
- Signed snapshots are immutable

**Failure paths to handle**

- Template changes during active completion
- Signature interrupted
- Signer identity/relationship missing
- Duplicate submit
- Attempt to alter signed snapshot

**Acceptance / stop condition**

- A versioned form can be completed and signed
- Signed snapshot preserves exactly the form/template version and captured responses shown at signing
- Later template edits do not change prior signed snapshots
- Duplicate submission does not create duplicate signed artifacts
- Audit/history identifies signer/actor/time and template version
- The form subsystem can later be indexed/viewed by ALV-010-C01 without changing signature semantics

**Required tests**

- Template-version tests
- Signed snapshot immutability test
- Duplicate-submit/idempotency test
- Authorization/audit tests
- Interrupted-signature test

**Out of scope / future extension** Full imported-document/image management and richer document metadata arrive in `ALV-010-C01`. Legal/compliance review of actual form wording is external to this engineering story.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N010` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N010/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N010: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N010_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 14 — STORY-004** below.

# 14 — STORY-004 — Enable appointment scheduling with conflict prevention

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-004` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-004` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-004` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-004: record course completion`. Then continue directly to **item 15 — ALV-004-C01** below.

# 15 — ALV-004-C01 — Complete Production Scheduler and Conflict-Aware Calendar UX

**Type:** Companion  
**Parent:** STORY-004  
**Dependencies:** STORY-004, ALV-N003, ALV-003-C01

### Full Coding-Agent Prompt

**Story:** ALV-004-C01 — Complete Production Scheduler and Conflict-Aware Calendar UX

Parent course story: STORY-004 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-004, ALV-N003, ALV-003-C01

**User story:** As front desk staff, I want a fast visual scheduler that respects practice availability and visit states so that the day can be managed without double booking.

**Why this story exists:** Expands the narrow provider-conflict story into the actual scheduling workflow.

**Implementation scope**

- Multiple providers/hygienists/operatories
- Appointment type and configurable duration
- Provider availability and blocked time
- Provider, operatory and patient-overlap conflict detection
- Reschedule
- Cancel with reason
- No-show
- Appointment notes/status metadata
- Concurrency-safe schedule edits

**UI/UX deliverables**

- Day/week calendar views with provider/operatory filters
- Create/edit appointment drawer with conflict explanation
- Drag/drop or equivalent reschedule only if safely validated
- Distinct cancelled/no-show presentation
- Fast patient lookup and appointment-type defaults

**Security, audit, and data-integrity requirements**

- Front-desk/scheduler authorization
- Audit create, reschedule, cancel, no-show and manual overrides

**Failure paths to handle**

- Provider/operatory/patient-overlap conflict
- Unavailable/blocked time
- Stale schedule edit
- Invalid duration
- Concurrent reschedule

**Acceptance / stop condition**

- Original STORY-004 tests still pass
- Operatory conflicts are rejected
- Patient-overlap conflicts are rejected unless an explicitly authorized future policy says otherwise
- Blocked/unavailable time is respected
- Reschedule, cancel and no-show work and remain historically visible
- Calendar accurately reflects provider/operatory assignments
- Concurrent edits do not silently double-book

**Required tests**

- Original regression
- Provider+operatory+patient-overlap conflict tests
- Availability tests
- Reschedule/cancel/no-show tests
- Concurrency race test

**Out of scope / future extension** Online booking and external calendar integrations later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-004-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-004-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-004-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-004-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 16 — STORY-011** below.

# 16 — STORY-011 — Track patient flow states from scheduled to completed

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-011` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-011` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-011` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-011: record course completion`. Then continue directly to **item 17 — ALV-011-C01** below.

# 17 — ALV-011-C01 — Live Patient Flow Board and Complete Visit-State Workflow

**Type:** Companion  
**Parent:** STORY-011  
**Dependencies:** STORY-011, ALV-004-C01, ALV-N010

### Full Coding-Agent Prompt

**Story:** ALV-011-C01 — Live Patient Flow Board and Complete Visit-State Workflow

Parent course story: STORY-011 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-011, ALV-004-C01, ALV-N010

**User story:** As the front office and clinical team, we want a shared live visit board so that everyone knows where each patient is and what happens next.

**Why this story exists:** Restores the full patient-flow state machine and operational UI.

**Implementation scope**

- Scheduled, confirmed, arrived/check-in, ready, seated/in operatory, in treatment, checked out, completed, cancelled, no-show
- Provider assignment
- Operatory assignment
- Valid transition rules
- Timestamped state history
- Current-day live flow
- Check-in readiness surfaces incomplete/complete required forms/consents from ALV-N010; viewing a form does not silently mark it complete/signed

**UI/UX deliverables**

- Live flow board with columns/statuses, patient/provider/operatory cues and elapsed-time indicators
- Fast status transition actions with confirmation only where needed
- Visible required-form/consent readiness cues at check-in
- Do not invent clinical safety flags yet; ALV-N011 will add a minimal permission-appropriate safety indicator after the actual safety model exists

**Security, audit, and data-integrity requirements**

- Role-based transition permissions where appropriate
- Audit every state/assignment change

**Failure paths to handle**

- Invalid transition
- Operatory already occupied where enforced
- Concurrent status change
- Missing appointment

**Acceptance / stop condition**

- Original STORY-011 tests still pass
- A visit can traverse the major production state chain
- Provider and operatory assignment are visible
- Cancelled/no-show remain distinct from completed
- Two users cannot silently overwrite current state
- Live board updates to persisted truth
- Check-in shows required-form/consent readiness accurately and does not fabricate completion

**Required tests**

- State-machine tests
- Original regression
- Concurrent transition test
- Flow-board integration test
- Required-form/check-in readiness integration test

**Out of scope / future extension** Queue optimization and patient-facing arrival status later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-011-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-011-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-011-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-011-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **GATE B** below.

# GATE B — PATIENT, SCHEDULING, AND VISIT ENTRY READY

**MANDATORY STOP. Do not begin the next numbered item until this gate passes.**

## Gate execution prompt

``` text
ALVEARA DENTAL — GATE B — Patient, Scheduling, and Visit Entry Ready

Evaluate Gate B against the current repository and the exact gate evidence requirements below.

Read .alveara/BUILD_STATE.md, .alveara/EXECUTION_STATUS.json, .alveara/QUALITY_GATES.md, all handoffs/reviews created since the prior gate, and the current code/tests. Do not rely on planning claims when implementation evidence disagrees.

For every criterion:
1. identify the exact test/check/measurement and representative environment/data;
2. use the predeclared pass/block threshold or rubric;
3. execute or inspect the evidence needed to judge it truthfully;
4. record PASS, FAIL, or BLOCKED with evidence references in .alveara/QUALITY_GATES.md;
5. do not lower a threshold after seeing a failure merely to make the gate pass.

If every blocking criterion passes, record Gate B PASS with the evidence/commit information and report that the next execution item is authorized.

If any blocking criterion fails or cannot be truthfully verified, record Gate B FAIL/BLOCKED, identify the responsible story/stories or missing evidence, and STOP. Do not begin the next phase. Repair/reopen/revalidate the responsible work first, then rerun this same gate.

When the integrated verification is substantial, create a reproducible gate evidence package named Alveara_Gate_B_<evidence-short-sha>.zip. Never include secrets or real PHI.

GATE B EVIDENCE REQUIREMENTS

Required evidence:

- Registration/family/guarantor workflow is production-usable.
- Versioned forms/consents/e-signature foundation works and check-in surfaces required-form readiness.
- Patient registration has established one persistent patient-context workspace that later modules extend without stale cross-patient state.
- Scheduler enforces provider/operatory/blocked-time rules and supports reschedule/cancel/no-show.
- Patient flow/status board works through the visit lifecycle states implemented so far.
- Critical front-desk workflows are keyboard/responsive and preserve course contracts.
- No known high-severity identity/scheduling/history defect remains.

Return the gate decision, criterion-by-criterion results, evidence references, repository state/commit information, blockers if any, and the next authorized action.
```

**PASS → continue directly to item 18 — STORY-005. FAIL/BLOCKED → stop; do not move forward.**

# 18 — STORY-005 — Develop clinical documentation module

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-005` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-005` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-005` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-005: record course completion`. Then continue directly to **item 19 — ALV-005-C01** below.

# 19 — ALV-005-C01 — Complete Clinical Documentation, Templates, Signing, and Amendments

**Type:** Companion  
**Parent:** STORY-005  
**Dependencies:** STORY-005, ALV-011-C01, ALV-002-C01

### Full Coding-Agent Prompt

**Story:** ALV-005-C01 — Complete Clinical Documentation, Templates, Signing, and Amendments

Parent course story: STORY-005 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-005, ALV-011-C01, ALV-002-C01

**User story:** As a clinician, I want efficient structured documentation with safe finalization so that the chart is clinically useful and legally traceable.

**Why this story exists:** Completes the documentation workflow beyond history forms.

**Implementation scope**

- Structured medical and dental history with longitudinal change history and reviewed/verified-by/on attribution
- Allergies with active/inactive/resolved status, reaction/severity fields where known, and history-preserving correction
- Medications with active/inactive/discontinued state and history
- Explicit `unknown` / `not reviewed` / `none known` semantics where clinically appropriate so required fields never force invented facts
- Vitals with encounter/date attribution
- SOAP/progress notes
- Treatment notes
- Configurable note templates owned by the clinical-documentation domain rather than generic practice configuration
- Free text where appropriate
- Encounter linkage
- Draft/signed/finalized states
- Addendum/amendment preserving original
- Documentation completeness indicators

**UI/UX deliverables**

- Patient clinical workspace with history summary and encounter note editor
- Template picker and structured sections
- Sticky save/status indicator
- Signing/finalization dialog showing unresolved items
- Timeline of amendments

**Security, audit, and data-integrity requirements**

- Clinical role permissions
- Finalized content immutable except governed addendum/amendment
- Audit all saves/sign/finalize/amend

**Failure paths to handle**

- Autosave/save failure
- Stale edit
- Signing with unresolved required data
- Amendment failure

**Acceptance / stop condition**

- Original STORY-005 tests still pass
- Longitudinal medical/dental history, allergies, medications and vitals can be captured with attribution
- Unknown/not-reviewed/none-known states remain distinguishable and do not force fabricated clinical facts
- SOAP/progress and treatment notes are encounter-linked
- Template use is demonstrated
- Finalized note cannot be overwritten
- Amendment preserves original and attribution

**Required tests**

- Original regression
- History/allergy/medication lifecycle and reviewed-attribution tests
- Unknown/not-reviewed/none-known semantic tests
- Template/note tests
- Sign/amend tests
- Concurrency test

**Out of scope / future extension** AI-assisted draft generation is added in ALV-N007.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-005-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-005-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-005-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-005-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 20 — ALV-N011** below.

# 20 — ALV-N011 — Patient Safety Alerts, Medical Risk Context, and Clearance Tracking

**Type:** New Production  
**Dependencies:** ALV-005-C01, ALV-002-C01, ALV-011-C01

### Full Coding-Agent Prompt

**Story:** ALV-N011 — Patient Safety Alerts, Medical Risk Context, and Clearance Tracking

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-005-C01, ALV-002-C01, ALV-011-C01

**User story:** As a clinician, I want important patient safety information visible before diagnosis, planning, treatment and prescribing so that known risks and unresolved clearances are not discovered only at the prescription step.

**Why this story exists:** The original vision treats safety alerts as a cross-clinical capability. v2 incorrectly made the prescription story own the broader safety framework.

**Implementation scope**

- Safety alert model linked to patient and source/provenance
- Surface allergies, current medications, significant conditions, relevant pregnancy flags, anticoagulant status, adverse reactions, unresolved medical/dental clearance and authorized custom alerts
- Severity/priority and active/resolved state appropriate to the implementation
- Explicit acknowledgment/viewed state must never silently equal clinical resolution
- Clearance tracking with requested/received/resolved state, actor/time/reason and linked supporting document when available later
- Patient-header/clinical-context projection usable by odontogram, perio, diagnosis, treatment planning, procedure completion and prescriptions
- Minimal permission-appropriate safety indicator projection for the live patient-flow board; detailed clinical safety information remains inside authorized patient context
- Do not infer or invent missing clinical risks from unrelated data

**UI/UX deliverables**

- Persistent high-priority patient safety area in the clinical workspace/header
- Safety detail panel showing source, status and last update
- Live-flow board integration shows only a minimal safety/clearance indicator to authorized roles, not detailed diagnoses/medications in the shared board
- Clearance workflow/status
- Distinguish acknowledged/viewed from actually resolved

**Security, audit, and data-integrity requirements**

- Clinical permissions and audit
- Alert/clearance resolution requires attribution and reason/evidence as appropriate
- No silent dismissal/deletion of historically consequential safety information

**Failure paths to handle**

- Conflicting/stale safety data
- Alert source missing
- Attempt to resolve without required reason/evidence
- Permission denied
- Supporting clearance document not yet available

**Acceptance / stop condition**

- Safety alerts are visible in clinical context before treatment planning/completion
- Allergy/medication/significant-condition/custom/clearance categories can be represented
- Acknowledging an alert does not silently resolve it
- Resolved/changed alerts preserve history, actor and reason
- Downstream clinical screens can query the shared safety context
- Live patient-flow board can consume the minimal safety indicator without exposing detailed PHI
- No alert is invented merely to fill an empty field

**Required tests**

- Alert lifecycle/history tests
- Acknowledged-vs-resolved test
- Clearance workflow tests
- Permission/audit tests
- Downstream safety-context service test
- Flow-board safety-indicator authorization/minimization test

**Out of scope / future extension** Drug interaction/dose decision support and external medical-information integrations require validated future sources; this story is the trustworthy patient-safety context, not an autonomous clinical decision engine.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N011` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N011/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N011: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N011_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 21 — STORY-006** below.

# 21 — STORY-006 — Implement interactive odontogram

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-006` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-006` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-006` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-006: record course completion`. Then continue directly to **item 22 — ALV-006-C01** below.

# 22 — ALV-006-C01 — Full Odontogram, Mixed Dentition, Conditions, Surfaces, Lifecycle, and Longitudinal History

**Type:** Companion  
**Parent:** STORY-006  
**Dependencies:** STORY-006, ALV-005-C01, ALV-N011

### Full Coding-Agent Prompt

**Story:** ALV-006-C01 — Full Odontogram, Mixed Dentition, Conditions, Surfaces, Lifecycle, and Longitudinal History

Parent course story: STORY-006 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-006, ALV-005-C01, ALV-N011

**User story:** As a dentist, I want a clinically expressive odontogram for primary, permanent, and mixed dentition so that current conditions, planned care and completed work are visible over time.

**Why this story exists:** The course skeleton is too shallow for a production dental chart, and deferring primary/mixed dentition would unnecessarily exclude ordinary general-practice pediatric patients.

**Implementation scope**

- Anatomical tooth identity independent of display numbering
- Permanent, primary and mixed dentition support
- Universal numbering/lettering default with an abstraction that can later render FDI/ISO without changing tooth identity
- Surface selection and tooth-state validation
- Structured caries, restorations, crowns, missing, implants, root canals and extensible condition types
- Existing, diagnosed, planned and completed lifecycle
- Longitudinal event/history model
- Link findings to diagnosis/treatment/completed procedure where applicable
- Preserve historical events when teeth/conditions change rather than rewriting prior chart history

**UI/UX deliverables**

- Interactive permanent/primary/mixed dentition chart with clear legend and lifecycle distinctions
- Tooth detail panel with surfaces, findings and longitudinal history
- Efficient condition entry and keyboard support where practical
- Safety alerts remain visible in patient context without obscuring the chart

**Security, audit, and data-integrity requirements**

- Clinical permissions and audit
- Concurrency-safe tooth updates
- Historical events cannot be silently deleted by a later chart edit

**Failure paths to handle**

- Invalid tooth/surface combination
- Condition invalid for selected dentition/tooth state
- Conflicting/stale update
- Unknown/inactive condition type
- Switch between mixed/permanent representation without losing recorded history

**Acceptance / stop condition**

- Original STORY-006 tests still pass
- Permanent, primary and mixed dentition can be represented
- Surface-specific findings save/render correctly
- Lifecycle states are visually distinguishable
- Longitudinal history remains accessible after subsequent changes
- Numbering/lettering is presentation metadata, not the tooth's database identity

**Required tests**

- Original regression
- Permanent/primary/mixed tooth-identity tests
- Tooth/surface validation
- Lifecycle/history tests
- Concurrency test

**Out of scope / future extension** Specialty-specific odontogram visualization can extend the shared anatomical/history model later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-006-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-006-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-006-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-006-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 23 — STORY-012** below.

# 23 — STORY-012 — Support periodontal charting with probing depth, recession, and bleeding

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-012` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-012` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-012` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-012: record course completion`. Then continue directly to **item 24 — ALV-012-C01** below.

# 24 — ALV-012-C01 — Complete Six-Site Periodontal Charting and Longitudinal Comparison

**Type:** Companion  
**Parent:** STORY-012  
**Dependencies:** STORY-012, ALV-005-C01, ALV-N011

### Full Coding-Agent Prompt

**Story:** ALV-012-C01 — Complete Six-Site Periodontal Charting and Longitudinal Comparison

Parent course story: STORY-012 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-012, ALV-005-C01, ALV-N011

**User story:** As a dentist or hygienist, I want fast full-mouth periodontal charting with precise site semantics so that periodontal status can be evaluated and compared over time.

**Why this story exists:** The course requirement captures only three measurements. Production needs a complete, efficient and unambiguous site model.

**Implementation scope**

- Explicit six-site per-tooth measurement model (MB, B, DB, ML, L, DL; palatal presentation where anatomically appropriate)
- Probing depth
- Gingival recession
- CAL derived/stored according to one documented rule
- Bleeding on probing
- Suppuration
- Mobility
- Furcation
- Plaque
- Missing/excluded tooth handling without corrupting entry order
- Periodontal diagnosis/history linkage
- Finalized chart-session history and longitudinal comparison

**UI/UX deliverables**

- Full-mouth sequential keyboard entry optimized for hygienist/dentist workflow
- Clear current tooth/site focus and rapid next-site movement
- Visual abnormal-value cues that do not substitute for diagnosis
- Previous-vs-current comparison and trend summary
- Safety alerts remain visible in patient context

**Security, audit, and data-integrity requirements**

- Clinical permissions and audit
- Finalized chart-session history preserved
- Derived CAL calculation rule is transparent/tested

**Failure paths to handle**

- Out-of-range values
- Invalid site/tooth state
- Partial save failure
- Stale session edit
- Missing tooth encountered during sequential entry

**Acceptance / stop condition**

- Original STORY-012 tests still pass
- All six periodontal sites are distinguishable per tooth
- CAL, mobility, furcation, suppuration and plaque can be recorded
- Full-mouth sequential entry works without losing site context
- Prior finalized chart can be compared with current chart
- Invalid values are rejected without discarding unrelated valid entry state

**Required tests**

- Original regression
- Six-site mapping tests
- Measurement/CAL validation tests
- Full-mouth keyboard-flow test
- Comparison test
- Session rollback/concurrency test

**Out of scope / future extension** Specialty periodontal analytics and predictive risk scoring can build on the longitudinal model later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-012-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-012-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-012-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-012-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 25 — STORY-013** below.

# 25 — STORY-013 — Allow structured diagnosis linked to patient, encounter, and treatment plan

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-013` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-013` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-013` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-013: record course completion`. Then continue directly to **item 26 — ALV-013-C01** below.

# 26 — ALV-013-C01 — Structured Diagnosis Lifecycle, Coding Provenance, Context Linkage, and Amendments

**Type:** Companion  
**Parent:** STORY-013  
**Dependencies:** STORY-013, ALV-006-C01, ALV-012-C01, ALV-N011

### Full Coding-Agent Prompt

**Story:** ALV-013-C01 — Structured Diagnosis Lifecycle, Coding Provenance, Context Linkage, and Amendments

Parent course story: STORY-013 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-013, ALV-006-C01, ALV-012-C01, ALV-N011

**User story:** As a clinician, I want diagnoses represented as structured, attributable clinical statements tied to the correct context so that treatment planning remains explainable and traceable.

**Why this story exists:** The course story proves basic linkage but does not define what makes a diagnosis structured or how external/local coding provenance survives over time.

**Implementation scope**

- Stable internal diagnosis identity
- Structured fields for coding system (optional), code (optional), display/label, clinician explanatory text, status and provenance/source
- Patient and encounter linkage
- Tooth and oral-region linkage when applicable
- Finding/odontogram/perio linkage
- Treatment-plan linkage
- Active/resolved/amended status model appropriate to the implementation
- Amendment preserving prior authoritative value, actor, time and reason
- Do not bundle or invent licensed terminology content merely to populate the model

**UI/UX deliverables**

- Diagnosis editor with structured coding/provenance fields and context chips for tooth/region/finding
- Patient diagnosis list/timeline with status/history
- Amendment/history view
- Safety/clearance context remains visible while documenting diagnosis

**Security, audit, and data-integrity requirements**

- Clinical permissions and audit
- No silent destructive edit of finalized/used diagnosis
- Coding/provenance source retained when data is imported or mapped later

**Failure paths to handle**

- Invalid context link
- Unknown/unsupported coding-system value
- Diagnosis referenced by plan cannot be destructively deleted
- Stale edit
- Attempt to silently replace an authoritative diagnosis used downstream

**Acceptance / stop condition**

- Original STORY-013 tests still pass
- A diagnosis can be stored structurally even when no external code system is configured
- Optional code/system/display/provenance survive round trip when present
- Tooth/region/finding linkage works where applicable
- Diagnosis is traceable to encounter and treatment plan
- Amendment preserves prior value and attribution

**Required tests**

- Original regression
- Structured diagnosis serialization/validation tests
- Context-link validation
- Amendment/history tests
- Coding/provenance round-trip tests

**Out of scope / future extension** Terminology/code-set integrations can be added later without changing internal diagnosis identity or history semantics.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-013-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-013-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-013-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-013-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 27 — ALV-N005** below.

# 27 — ALV-N005 — Production Procedure and Fee Catalog Foundation

**Type:** New Production  
**Dependencies:** ALV-N003, ALV-002-C01

### Full Coding-Agent Prompt

**Story:** ALV-N005 — Production Procedure and Fee Catalog Foundation

No course parent. This is production work omitted by the generated course backlog.

**Dependencies:** ALV-N003, ALV-002-C01

**User story:** As a billing manager and clinician, I want a reusable procedure catalog before treatment planning so that proposed and completed care share one authoritative procedure definition.

**Why this story exists:** The portal gates COURSE STORY-014 after STORY-015, which is backward for a production dependency. Build the production catalog first; later COURSE STORY-014 simply verifies its minimum criteria.

**Implementation scope**

- Stable internal procedure identity
- Procedure code plus explicit code system/source (for example local vs externally sourced), optional source/version metadata, and effective period
- Description
- Fee
- Category
- Tooth/surface applicability
- Active/inactive and effective status/date strategy
- Historical fee preservation for already-created plans/charges
- CDT-ready structural fields without bundling licensed CDT descriptions or implying that a local code is CDT
- Preserve source/version/provenance so future licensed code-set imports do not require changing internal procedure identity
- Reusable service/API for planning, completion and billing

**UI/UX deliverables**

- Procedure catalog list/search/filter
- Add/edit/inactivate workflow
- Fee/effective-state presentation
- Usage warning before inactivation

**Security, audit, and data-integrity requirements**

- Billing-manager/admin permissions
- Audit all catalog changes

**Failure paths to handle**

- Duplicate/invalid code
- Negative/invalid fee
- Destructive edit of referenced historical procedure

**Acceptance / stop condition**

- Procedure with stable internal identity, code/system/source, description, fee, category and applicability can be created
- Inactivation preserves historical references
- Fee changes do not silently rewrite existing plan/charge history
- Treatment-planning API can query active procedures
- Later STORY-014 acceptance can pass without architectural rework

**Required tests**

- Catalog validation
- Code-system/source/version/provenance test
- Historical fee/version test
- Reference/inactivation test

**Out of scope / future extension** Licensed code-set import can be added under appropriate licensing.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N005` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N005/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N005: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N005_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 28 — STORY-015** below.

# 28 — STORY-015 — Support treatment planning with diagnosis linkage, proposed procedures, and fee estimates

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-015` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-015` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-015` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-015: record course completion`. Then continue directly to **item 29 — ALV-015-C01** below.

# 29 — ALV-015-C01 — Complete Treatment Planning, Acceptance, Phasing, and Revision History

**Type:** Companion  
**Parent:** STORY-015  
**Dependencies:** STORY-015, ALV-N005, ALV-013-C01

### Full Coding-Agent Prompt

**Story:** ALV-015-C01 — Complete Treatment Planning, Acceptance, Phasing, and Revision History

Parent course story: STORY-015 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-015, ALV-N005, ALV-013-C01

**User story:** As a dentist, I want complete treatment plans that patients can understand and accept so that proposed care is organized, priced and traceable.

**Why this story exists:** Completes the planning atoms omitted by the generated prompt.

**Implementation scope**

- Finding/diagnosis linkage
- Catalog procedure
- Tooth/surface
- Phase/planned visit
- Fee and practice-estimated patient cost clearly labeled as an Alveara/practice estimate; without a future insurance-benefit engine it must not be presented as insurer-adjudicated coverage or guaranteed patient responsibility
- Accept/decline per plan/item where appropriate
- Scheduling state
- Completion state
- Revision/version history
- Clinical rationale/patient-facing explanation fields

**UI/UX deliverables**

- Treatment-plan builder grouped by phase/visit
- Clear fee/estimate summary with explicit estimate source/limitations and no implication of insurance adjudication when insurance functionality is absent
- Accept/decline presentation
- Revision comparison/history
- Print-friendly/patient-friendly view

**Security, audit, and data-integrity requirements**

- Clinical authoring permissions; acceptance attribution
- Audit create/revise/accept/decline

**Failure paths to handle**

- Missing diagnosis/procedure
- Referenced procedure inactive
- Stale plan edit
- Partial acceptance conflict

**Acceptance / stop condition**

- Original STORY-015 tests still pass
- Tooth/surface and phases are supported
- Fee/estimate is visible and truthfully labeled as a practice estimate when no insurance-benefit engine exists
- Accept/decline is recorded with attribution
- Revision preserves prior version
- Completion state can later be updated without rewriting history

**Required tests**

- Original regression
- Phase/tooth tests
- Estimate-label/source truthfulness test
- Acceptance tests
- Revision history test
- Concurrency test

**Out of scope / future extension** Insurance estimates and patient portal acceptance later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-015-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-015-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-015-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-015-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 30 — STORY-014** below.

# 30 — STORY-014 — Manage procedure/fee catalog with codes, descriptions, and fees

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-014` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-014` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-014` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-014: record course completion`. Then continue directly to **GATE C** below.

# GATE C — CLINICAL CORE READY

**MANDATORY STOP. Do not begin the next numbered item until this gate passes.**

## Gate execution prompt

``` text
ALVEARA DENTAL — GATE C — Clinical Core Ready

Evaluate Gate C against the current repository and the exact gate evidence requirements below.

Read .alveara/BUILD_STATE.md, .alveara/EXECUTION_STATUS.json, .alveara/QUALITY_GATES.md, all handoffs/reviews created since the prior gate, and the current code/tests. Do not rely on planning claims when implementation evidence disagrees.

For every criterion:
1. identify the exact test/check/measurement and representative environment/data;
2. use the predeclared pass/block threshold or rubric;
3. execute or inspect the evidence needed to judge it truthfully;
4. record PASS, FAIL, or BLOCKED with evidence references in .alveara/QUALITY_GATES.md;
5. do not lower a threshold after seeing a failure merely to make the gate pass.

If every blocking criterion passes, record Gate C PASS with the evidence/commit information and report that the next execution item is authorized.

If any blocking criterion fails or cannot be truthfully verified, record Gate C FAIL/BLOCKED, identify the responsible story/stories or missing evidence, and STOP. Do not begin the next phase. Repair/reopen/revalidate the responsible work first, then rerun this same gate.

When the integrated verification is substantial, create a reproducible gate evidence package named Alveara_Gate_C_<evidence-short-sha>.zip. Never include secrets or real PHI.

GATE C EVIDENCE REQUIREMENTS

Required evidence:

- Structured history, allergies, medications, vitals and encounter notes work with safe signing/amendment.
- Safety alerts/medical clearance are visible before planning/completion/prescribing.
- Odontogram supports primary/permanent/mixed dentition, surfaces, lifecycle and longitudinal history.
- Perio supports explicit six-site full-mouth charting and comparison.
- Structured diagnosis/provenance/context linkage works.
- Procedure/fee catalog precedes and feeds treatment planning.
- Treatment plans preserve revisions, fees/estimates, acceptance/decline and phasing.
- No finalized clinical history can be silently overwritten.

Return the gate decision, criterion-by-criterion results, evidence references, repository state/commit information, blockers if any, and the next authorized action.
```

**PASS → continue directly to item 31 — STORY-007. FAIL/BLOCKED → stop; do not move forward.**

# 31 — STORY-007 — Enable billing workflow with financial history preservation

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-007` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-007` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-007` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-007: record course completion`. Then continue directly to **item 32 — ALV-007-C01** below.

# 32 — ALV-007-C01 — Financial Ledger, Charges, Payments, Adjustments, and Guarantor Foundation

**Type:** Companion  
**Parent:** STORY-007  
**Dependencies:** STORY-007, ALV-003-C01, ALV-N005, ALV-002-C01

### Full Coding-Agent Prompt

**Story:** ALV-007-C01 — Financial Ledger, Charges, Payments, Adjustments, and Guarantor Foundation

Parent course story: STORY-007 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-007, ALV-003-C01, ALV-N005, ALV-002-C01

**User story:** As billing staff, I want an authoritative ledger and charge/payment foundation so that later procedure completion can create financial effects without inventing billing infrastructure inside the clinical story.

**Why this story exists:** v2 incorrectly placed billing after procedure completion even though completion must create a billable charge. This companion now establishes the financial transaction model first.

**Implementation scope**

- Authoritative account/ledger transaction model using the ALV-N002 money/rounding invariant
- Charges with source linkage seam for completed procedures
- Payments and payment allocation
- Adjustments/corrections via preserving entries rather than destructive rewrite
- Reversal/void primitives appropriate to existing transaction types
- Patient and guarantor responsibility/account relationship
- Common recorded payment-method configuration owned by the billing domain (not generic practice configuration)
- Running/reconstructed balances derived from preserved financial history
- Idempotency keys/correlation for charge and payment creation
- Service/API contract that ALV-008-C01 can call to create exactly one charge per billable completed procedure
- Financial audit integration

**UI/UX deliverables**

- Account ledger with running/reconstructed balance
- Basic payment entry/allocation workflow
- Adjustment/reversal workflow with reason
- Patient/guarantor responsibility summary
- Clear pending/posted/reversed presentation if those states are used

**Security, audit, and data-integrity requirements**

- Billing permissions
- Financial audit
- No destructive deletion of authoritative transactions
- No binary floating-point arithmetic for authoritative money
- Do not let UI-calculated totals override ledger truth

**Failure paths to handle**

- Duplicate charge/payment submission
- Payment over/under-allocation according to documented rules
- Stale account balance/view
- Adjustment/reversal conflict
- Source procedure/plan reference invalid
- Audit write failure

**Acceptance / stop condition**

- Original STORY-007 tests still pass
- Ledger balance reconstructs from preserved authoritative transactions
- Payments/adjustments cannot silently rewrite original financial history
- Duplicate command cannot create duplicate charge/payment
- Guarantor responsibility is represented independently from patient identity
- A tested API/service contract exists for ALV-008-C01 to create one linked billable charge per completed procedure
- Money/rounding rules are consistent with ALV-N002

**Required tests**

- Original regression
- Ledger reconstruction/math tests
- Payment allocation tests
- Adjustment/reversal tests
- Charge/payment idempotency tests
- Guarantor linkage tests
- Procedure-charge service contract test

**Out of scope / future extension** Statements, receipt presentation, richer refunds/account UX and financial operational hardening are completed in `ALV-007-C02`; integrated processors and insurance accounting remain future.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-007-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-007-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-007-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-007-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 33 — STORY-008** below.

# 33 — STORY-008 — Complete treatment plan and update clinical history

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-008` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-008` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-008` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-008: record course completion`. Then continue directly to **item 34 — ALV-008-C01** below.

# 34 — ALV-008-C01 — Atomic Procedure Completion, Clinical History, Odontogram, and Charge Generation

**Type:** Companion  
**Parent:** STORY-008  
**Dependencies:** STORY-008, ALV-015-C01, ALV-N005, ALV-006-C01, ALV-007-C01

### Full Coding-Agent Prompt

**Story:** ALV-008-C01 — Atomic Procedure Completion, Clinical History, Odontogram, and Charge Generation

Parent course story: STORY-008 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-008, ALV-015-C01, ALV-N005, ALV-006-C01, ALV-007-C01

**User story:** As a clinician, I want one or more selected planned procedures completed as one trustworthy clinical operation so that clinical history, chart state, treatment-plan history and billing stay consistent.

**Why this story exists:** The course acceptance does not prove odontogram and procedure-linked billing transitions. The financial foundation must already exist before this story creates charges.

**Implementation scope**

- Complete one or multiple selected accepted/planned procedures in a visit/encounter
- Create authoritative completed-procedure records linked to source plan/version
- Preserve treatment-plan/version history
- Update relevant odontogram lifecycle
- Create exactly one linked billable charge for each billable completed procedure through ALV-007-C01
- Finalization/amendment semantics owned by the completed-procedure domain
- Idempotent duplicate-submit protection
- Define one transaction boundary for the selected completion set so a failed coupled effect cannot leave the submitted set half-authoritative
- Preserve provider, completion date/time, tooth/surface and encounter attribution

**UI/UX deliverables**

- Completion review screen listing every selected procedure, tooth/surface, provider/date and resulting charge effect
- Explicit confirmation of the submitted completion set
- Clear failure state; never show procedures as completed if the authoritative transaction rolled back
- Post-completion links to clinical history, odontogram and account ledger

**Security, audit, and data-integrity requirements**

- Clinical completion permission
- Financial effect audit
- Finalized completed procedure cannot be silently deleted/rewritten
- Charge creation uses billing service/idempotency contract rather than direct ad-hoc ledger writes

**Failure paths to handle**

- One charge creation fails in a multi-procedure completion
- Odontogram update fails
- Duplicate completion submission
- Stale treatment-plan version
- Selected procedure already completed/cancelled
- Audit/finalization persistence failure

**Acceptance / stop condition**

- Original STORY-008 tests still pass
- One or multiple selected planned procedures can complete in one authoritative operation
- Completed procedures remain linked to the exact source plan/version
- Odontogram updates when applicable
- Exactly one linked charge is created per billable completed procedure
- Duplicate/retry does not double-complete or double-charge
- A failed submitted completion set leaves no mixed authoritative clinical/financial state

**Required tests**

- Original regression
- Single- and multi-procedure transactional completion tests
- Procedure→charge cardinality test
- Idempotency/duplicate-submit test
- Failure rollback tests
- Stale-plan concurrency test

**Out of scope / future extension** Insurance claim generation can later subscribe to completed/billable state.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-008-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-008-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-008-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-008-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 35 — ALV-007-C02** below.

# 35 — ALV-007-C02 — Statements, Receipts, Refunds, Financial Corrections, and Account UX Hardening

**Type:** Companion  
**Parent:** STORY-007  
**Dependencies:** STORY-007, ALV-007-C01, ALV-008-C01

### Full Coding-Agent Prompt

**Story:** ALV-007-C02 — Statements, Receipts, Refunds, Financial Corrections, and Account UX Hardening

Parent course story: STORY-007 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-007, ALV-007-C01, ALV-008-C01

**User story:** As billing staff, I want complete account operations and customer-facing financial documents so that balances, corrections, receipts and statements remain understandable after real completed treatment creates charges.

**Why this story exists:** The financial foundation must precede procedure completion, but statement/receipt/refund UX is safer to harden after real procedure-linked charges exist.

**Implementation scope**

- Statement/invoice generation from preserved ledger history
- Receipt generation for recorded payments
- Refund workflow represented through preserving financial transactions/reversals
- Void/reversal/correction reasons and attribution
- Allocation correction without erasing original financial events
- Financial timeline joining charges, payments, adjustments, reversals and procedure sources
- Account aging/current balance presentation as derivable views where implemented
- Print/export-safe statement and receipt representations
- Guarantor responsibility presentation across account documents

**UI/UX deliverables**

- Statement/invoice preview and print
- Payment receipt preview/print
- Refund/reversal/correction workflow with reason and confirmation
- Account timeline with links back to completed procedure/charge sources
- Clear distinction between original, reversed and replacement transactions

**Security, audit, and data-integrity requirements**

- Billing permissions
- Financial documents reflect ledger truth; generated documents do not become an alternate source of balance
- Refund/reversal/correction actions audited
- Original financial transactions remain preserved

**Failure paths to handle**

- Statement generated while source changes
- Refund exceeds eligible payment according to documented policy
- Duplicate refund/reversal submission
- Allocation correction conflict
- Print/render/export failure

**Acceptance / stop condition**

- Statements and receipts can be generated from authoritative ledger history
- Refund/reversal preserves original transactions and reconstructable balance
- Duplicate submission cannot duplicate a refund/reversal
- Account timeline links financial effects back to source completed care where applicable
- Guarantor responsibility is reflected consistently
- ALV-007-C01 and ALV-008-C01 regression suites remain green

**Required tests**

- Statement/receipt snapshot tests
- Refund/reversal tests
- Allocation-correction tests
- Duplicate-submit/idempotency tests
- Ledger reconciliation tests
- Upstream financial/procedure regression suite

**Out of scope / future extension** Integrated/online payment processing, dental insurance and clearinghouse workflows remain future roadmap items.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-007-C02` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-007-C02/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-007-C02: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-007-C02_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 36 — STORY-016** below.

# 36 — STORY-016 — Support prescriptions with medication, dosage, and allergy checks

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-016` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-016` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-016` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-016: record course completion`. Then continue directly to **item 37 — ALV-016-C01** below.

# 37 — ALV-016-C01 — Complete Prescription Workflow Using the Shared Clinical Safety Framework

**Type:** Companion  
**Parent:** STORY-016  
**Dependencies:** STORY-016, ALV-005-C01, ALV-N011, ALV-002-C01

### Full Coding-Agent Prompt

**Story:** ALV-016-C01 — Complete Prescription Workflow Using the Shared Clinical Safety Framework

Parent course story: STORY-016 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-016, ALV-005-C01, ALV-N011, ALV-002-C01

**User story:** As an authorized clinician, I want a structured prescription workflow that surfaces known patient risks so that I can review and finalize prescriptions without the software pretending to provide unsupported medication decision support.

**Why this story exists:** The broad patient-safety framework is now built earlier in ALV-N011. This story completes prescription records and consumes that safety context. The course phrase “dosage accuracy” must not be misrepresented as medical dose appropriateness without an authoritative drug-knowledge source.

**Implementation scope**

- Medication, entered dose value/unit, instructions, quantity, refills, prescriber, status and relevant dates
- Structural validation of required fields, numeric/range/syntax rules that are explicitly defined by the application
- Allergy/current-medication/safety-alert context from ALV-N011
- Printable prescription output where appropriate
- Draft/finalized/cancelled-or-discontinued lifecycle as supported by the implementation
- Finalization/amendment/correction rules owned by the prescription domain
- If no validated drug-knowledge source exists, do **not** claim therapeutic dosing appropriateness, interaction checking or medical dose validation; surface that limitation explicitly

**UI/UX deliverables**

- Prescription composer with persistent patient safety panel
- Clear distinction between structural validation and clinical warnings/data the application actually knows
- Finalization confirmation and print preview
- Visible indication when advanced medication decision-support data is not configured

**Security, audit, and data-integrity requirements**

- Prescribing permissions
- Prescription finalization/audit
- No autonomous diagnosis/prescription or silent clinical override
- Known allergy/current-medication data is surfaced but the system does not invent missing facts

**Failure paths to handle**

- Known allergy/safety conflict requiring clinician review according to configured policy
- Missing/structurally invalid prescription data
- Stale allergy/medication list update
- Print/render failure
- Unsupported request for clinical dose appropriateness when no authoritative source is configured

**Acceptance / stop condition**

- Original STORY-016 tests still pass exactly
- Structured quantity/refills/instructions/prescriber/status are stored
- Entered dose fields are structurally validated according to documented local rules
- Current medications and ALV-N011 safety alerts are visible during prescribing
- The UI does not claim medical dosage appropriateness or interaction checking unless a validated source actually provides it
- Prescription actions are audited and finalized safely

**Required tests**

- Original regression
- Structural dose/input validation tests
- Safety-context tests
- Prescription finalization/correction tests
- Permission tests
- Explicit no-drug-knowledge-source behavior test

**Out of scope / future extension** eRx/pharmacy connectivity and authoritative drug knowledge/interaction services are added later under validated integrations.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-016-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-016-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-016-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-016-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **GATE D** below.

# GATE D — TREATMENT COMPLETION AND FINANCIAL CORE READY

**MANDATORY STOP. Do not begin the next numbered item until this gate passes.**

## Gate execution prompt

``` text
ALVEARA DENTAL — GATE D — Treatment Completion and Financial Core Ready

Evaluate Gate D against the current repository and the exact gate evidence requirements below.

Read .alveara/BUILD_STATE.md, .alveara/EXECUTION_STATUS.json, .alveara/QUALITY_GATES.md, all handoffs/reviews created since the prior gate, and the current code/tests. Do not rely on planning claims when implementation evidence disagrees.

For every criterion:
1. identify the exact test/check/measurement and representative environment/data;
2. use the predeclared pass/block threshold or rubric;
3. execute or inspect the evidence needed to judge it truthfully;
4. record PASS, FAIL, or BLOCKED with evidence references in .alveara/QUALITY_GATES.md;
5. do not lower a threshold after seeing a failure merely to make the gate pass.

If every blocking criterion passes, record Gate D PASS with the evidence/commit information and report that the next execution item is authorized.

If any blocking criterion fails or cannot be truthfully verified, record Gate D FAIL/BLOCKED, identify the responsible story/stories or missing evidence, and STOP. Do not begin the next phase. Repair/reopen/revalidate the responsible work first, then rerun this same gate.

When the integrated verification is substantial, create a reproducible gate evidence package named Alveara_Gate_D_<evidence-short-sha>.zip. Never include secrets or real PHI.

GATE D EVIDENCE REQUIREMENTS

Required evidence:

- Financial ledger/charge/payment foundation reconciles from preserved transactions.
- One or many planned procedures can complete atomically with exact source linkage, odontogram effects and one billable charge per billable completed procedure.
- Statement/receipt/refund/reversal/account UX hardening passes.
- Prescription workflow consumes the shared safety context and does not claim unsupported medical dose decision support.
- Financial and completed-procedure histories survive duplicate submissions, corrections and stale edits without destructive overwrite.

Return the gate decision, criterion-by-criterion results, evidence references, repository state/commit information, blockers if any, and the next authorized action.
```

**PASS → continue directly to item 38 — STORY-010. FAIL/BLOCKED → stop; do not move forward.**

# 38 — STORY-010 — Implement document import and categorization

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-010` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-010` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-010` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-010: record course completion`. Then continue directly to **item 39 — ALV-010-C01** below.

# 39 — ALV-010-C01 — Complete Document and Imaging Imports, Metadata, Storage Integrity, and Advanced Form Management

**Type:** Companion  
**Parent:** STORY-010  
**Dependencies:** STORY-010, ALV-N010, ALV-003-C01, ALV-002-C01, ALV-N004

### Full Coding-Agent Prompt

**Story:** ALV-010-C01 — Complete Document and Imaging Imports, Metadata, Storage Integrity, and Advanced Form Management

Parent course story: STORY-010 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-010, ALV-N010, ALV-003-C01, ALV-002-C01, ALV-N004

**User story:** As staff, I want patient documents, imported images and signed forms organized in clinical context so that the longitudinal record is complete, safe to store and recoverable.

**Why this story exists:** ALV-N010 already establishes versioned forms/consent/signature semantics. This companion adds the production document/image library and hardens storage/import behavior.

**Implementation scope**

- Import/view approved X-rays, photos, PDFs, referral letters, medical clearances, lab reports, insurance cards, IDs, correspondence and other document types
- Configurable document categories owned by the document domain, with safe inactivation/history
- Metadata: patient, date, category, encounter and tooth/region where applicable
- Content/type/size validation; safe generated storage identity; user filename treated as metadata only
- Prevent path traversal and never execute uploaded content as application code
- Integrity hash/metadata sufficient to detect missing/corrupt stored blobs
- Transactional file+metadata persistence or explicit recovery from partial storage failure
- Document status/inactivation rather than destructive historical loss
- Extend/configure form/privacy/financial/treatment templates on top of ALV-N010
- Signed snapshot remains immutable after template edits
- Ensure application-managed documents participate in ALV-N004 backup/restore coverage

**UI/UX deliverables**

- Document library with list/thumbnail/filters/preview
- Document-category administration appropriate to document permissions
- Upload/import progress and clear validation failures
- Document metadata editor with encounter/tooth-region context
- Form/template administration that reuses ALV-N010 signature/versioning semantics
- Signed snapshot/history view

**Security, audit, and data-integrity requirements**

- Document/form permissions
- Signed snapshot immutable
- Audit import, metadata edits, sign and inactivate
- Storage paths/identifiers are not trusted from user input
- Diagnostic logs do not become a copy of document/clinical content

**Failure paths to handle**

- Unsupported/mismatched/oversize file
- Path-traversal/malicious filename
- Metadata invalid
- Storage write succeeds but metadata fails, or vice versa
- Corrupt/missing blob
- Signature interrupted
- Template changed after signing
- Backup/restore omits document asset

**Acceptance / stop condition**

- Original STORY-010 tests still pass
- Representative image/PDF/document types can be safely categorized and viewed
- Encounter/tooth-region metadata works
- Unsafe filenames/content types are rejected or safely normalized according to policy
- Template edit after signing does not alter signed content
- Failed import does not leave orphaned authoritative metadata/blob state
- A backup/restore drill proves representative stored documents survive recovery

**Required tests**

- Original regression
- File type/size/path-safety tests
- Blob-integrity/missing-file test
- Signed snapshot regression
- Transactional import/recovery test
- Backup/restore document round-trip

**Out of scope / future extension** Direct imaging acquisition/DICOM/device integration remains `ALV-F004`; malware-scanning integrations can be added as deployment requirements mature.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-010-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-010-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-010-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-010-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 40 — STORY-009** below.

# 40 — STORY-009 — Manage follow-up tasks and reminders

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-009` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-009` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-009` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-009: record course completion`. Then continue directly to **item 41 — ALV-009-C01** below.

# 41 — ALV-009-C01 — Recall, Unscheduled Treatment, Work Queues, and Follow-Up Task Operations

**Type:** Companion  
**Parent:** STORY-009  
**Dependencies:** STORY-009, ALV-015-C01, ALV-004-C01, ALV-N002

### Full Coding-Agent Prompt

**Story:** ALV-009-C01 — Recall, Unscheduled Treatment, Work Queues, and Follow-Up Task Operations

Parent course story: STORY-009 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-009, ALV-015-C01, ALV-004-C01, ALV-N002

**User story:** As staff, I want actionable recall and follow-up queues so that patients and unfinished care do not disappear from daily operations.

**Why this story exists:** Restores recall intervals and operational task detail compressed out of the course prompt.

**Implementation scope**

- Recall interval defaults/configuration owned by this domain story rather than generic practice configuration
- Patient-specific recall intervals/next-due calculation where supported
- Unscheduled-treatment tracking
- Durable internal reminder records/scheduled work using ALV-N002 background-work infrastructure so process/server restart does not silently lose due reminders
- Work queues
- Owner
- Due date
- Priority
- Status
- Patient and optional appointment link
- Notes
- Completion history
- Filters for overdue/due soon/unassigned

**UI/UX deliverables**

- Work-queue dashboard
- Patient follow-up timeline
- Fast assign/reassign/complete actions
- Overdue and priority visual treatment

**Security, audit, and data-integrity requirements**

- Role-based queue access
- Audit task status/ownership changes

**Failure paths to handle**

- Missed/duplicate reminder creation
- Server/background-worker restart while reminders are due
- Concurrent completion/reassignment
- Invalid owner/due date

**Acceptance / stop condition**

- Original STORY-009 tests still pass
- Recall interval creates actionable follow-up state
- Due internal reminders survive background-worker/server restart and are not duplicated by retry
- Unscheduled accepted treatment can appear in a queue
- Task has owner/due/priority/status
- Completion history remains visible
- Concurrent update does not silently lose changes

**Required tests**

- Original regression
- Recall-default/patient-specific scheduling tests
- Durable reminder restart/idempotency tests
- Queue filter tests
- Concurrency tests

**Out of scope / future extension** SMS/email campaign automation later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-009-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-009-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-009-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-009-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 42 — ALV-009-C02** below.

# 42 — ALV-009-C02 — Referral and Dental Laboratory Case Tracking

**Type:** Companion  
**Parent:** STORY-009  
**Dependencies:** STORY-009, ALV-009-C01, ALV-010-C01

### Full Coding-Agent Prompt

**Story:** ALV-009-C02 — Referral and Dental Laboratory Case Tracking

Parent course story: STORY-009 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-009, ALV-009-C01, ALV-010-C01

**User story:** As clinical staff, I want referrals and lab cases tracked with due dates and attachments so that outside work has accountable follow-up.

**Why this story exists:** Referrals and labs were present in the original product vision but disappeared from the course backlog.

**Implementation scope**

- Referral provider, reason, status, sent/received dates, follow-up and attachments
- Lab vendor, status, dates, tooth/procedure/treatment-plan linkage, material and shade where useful
- Task/follow-up integration
- Document linkage

**UI/UX deliverables**

- Referral and lab case lists with status filters
- Patient-level referral/lab timeline
- Case detail with linked documents and follow-up

**Security, audit, and data-integrity requirements**

- Clinical/admin permissions
- Audit status and attachment changes

**Failure paths to handle**

- Missing external party
- Overdue case
- Broken document link
- Concurrent status change

**Acceptance / stop condition**

- Referral can be created, tracked and completed with follow-up
- Lab case can link to treatment/tooth and track status/dates
- Attachments are linked rather than duplicated
- Overdue items surface in work queues

**Required tests**

- Referral lifecycle tests
- Lab lifecycle tests
- Task integration tests

**Out of scope / future extension** Electronic referral/lab integrations later.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-009-C02` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-009-C02/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-009-C02: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-009-C02_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 43 — STORY-017** below.

# 43 — STORY-017 — Support duplicate detection and controlled merge of patient records

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-017` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-017` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-017` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-017: record course completion`. Then continue directly to **item 44 — ALV-017-C01** below.

# 44 — ALV-017-C01 — Safe Duplicate Review, Conflict Resolution, Merge Preservation, and Recovery

**Type:** Companion  
**Parent:** STORY-017  
**Dependencies:** STORY-017, ALV-003-C01, ALV-007-C01, ALV-010-C01

### Full Coding-Agent Prompt

**Story:** ALV-017-C01 — Safe Duplicate Review, Conflict Resolution, Merge Preservation, and Recovery

Parent course story: STORY-017 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-017, ALV-003-C01, ALV-007-C01, ALV-010-C01

**User story:** As an administrator, I want human-controlled duplicate merging so that identity cleanup never loses clinical or financial history.

**Why this story exists:** Makes 'preserving linked data' concrete and testable across the actual domain.

**Implementation scope**

- Duplicate candidate scoring/flags
- Human review required; no silent auto-merge
- Side-by-side demographic conflict resolution
- Merge appointments, notes, odontogram, perio, diagnoses, plans, completed procedures, prescriptions, documents, billing, household/guarantor, referrals/tasks and audit references
- Source/target lineage
- Unmerge/recovery where practical and safe

**UI/UX deliverables**

- Duplicate-review queue
- Side-by-side comparison with explicit source/target
- Conflict-resolution controls
- Merge impact preview and post-merge history

**Security, audit, and data-integrity requirements**

- Restricted merge permission
- Merge itself audited with source/target and resolutions

**Failure paths to handle**

- Conflicting identities
- Referenced data cannot move
- Partial merge failure
- Attempt to merge already-merged record

**Acceptance / stop condition**

- Original STORY-017 tests still pass
- No merge occurs without explicit human confirmation
- Representative linked clinical/financial/document/task data survives
- Conflicting values require resolution
- Merge is transactional or safely recoverable
- Lineage remains visible

**Required tests**

- Original regression
- Cross-domain preservation test
- Rollback/recovery test
- Permission test

**Out of scope / future extension** More sophisticated probabilistic matching can evolve without auto-merging.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-017-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-017-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-017-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-017-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 45 — STORY-018** below.

# 45 — STORY-018 — Provide data portability with patient export and import capabilities

**ORIGINAL COURSE STORY — EXECUTE IN THE COURSE PORTAL**

Open `STORY-018` in the course portal and execute its **current portal-generated coding prompt**. The portal's current Done Means, unlock state, and verification are authoritative. Do not substitute an ALV prompt for this course story and do not mark it complete merely to preserve this plan's order.

When the portal has genuinely verified `STORY-018` complete, update `.alveara/EXECUTION_STATUS.json` for `STORY-018` to `COMPLETE`, record `externalVerification = "course_portal"` plus the verification timestamp/evidence reference, and commit that control-state synchronization using `STORY-018: record course completion`. Then continue directly to **item 46 — ALV-018-C01** below.

# 46 — ALV-018-C01 — Versioned Data Export and Portable Record Package

**Type:** Companion  
**Parent:** STORY-018  
**Dependencies:** STORY-018, ALV-017-C01, ALV-010-C01, ALV-007-C02

### Full Coding-Agent Prompt

**Story:** ALV-018-C01 — Versioned Data Export and Portable Record Package

Parent course story: STORY-018 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-018, ALV-017-C01, ALV-010-C01, ALV-007-C02

**User story:** As an authorized data manager, I want transparent human-readable and structured exports so that a practice can retain and move its records without relying on Alveara as the only readable copy.

**Why this story exists:** The course story proves that export exists. Production portability requires a versioned package contract that explains what was exported, from which schema version, and whether associated files are complete.

**Implementation scope**

- Human-readable patient export
- Structured, versioned export package with manifest/schema version and generated-at/application-version metadata
- Export the applicable first-release patient record domains: demographics/household/guarantor, appointments/flow history, structured histories/notes metadata as permitted, odontogram/perio, diagnoses, treatment plans, completed procedures, prescriptions, financial history, forms/documents metadata, follow-up/referral/lab data, and other implemented patient-linked domains
- Include authorized application-managed document/file payloads or an explicit manifest that identifies them, with integrity hashes where exported
- Explicit included/excluded-domain manifest so an export never silently looks “complete” when a domain was intentionally omitted
- Stable identifiers/provenance sufficient to understand relationships without exposing internal secrets
- Bulk/structured export path in addition to per-patient human-readable export
- Export history/audit without logging exported PHI content

**UI/UX deliverables**

- Export center with scope selector, format/package explanation and estimated included domains
- Clear warning for excluded/unavailable domains
- Export history with actor/time/scope/result
- Downloaded package contains a human-readable manifest/readme describing format version and included assets

**Security, audit, and data-integrity requirements**

- Restricted export permission
- Server-side scope enforcement
- Audit actor, scope and result without logging sensitive file contents
- Exported package must not contain authentication secrets, password hashes, encryption keys or unrelated system secrets

**Failure paths to handle**

- Export interrupted
- Document/blob missing or hash mismatch
- Unsupported/unavailable domain
- User loses permission during export
- Large export exceeds configured resource limits

**Acceptance / stop condition**

- Original STORY-018 export behavior still passes
- Human-readable patient export is available
- Structured export has an explicit schema/package version and included/excluded-domain manifest
- Representative relationships across clinical, scheduling, treatment and financial records survive export
- Representative document/file payloads export with integrity evidence when included
- Export history is auditable and contains no unnecessary PHI payload logging

**Required tests**

- Original STORY-018 export regression
- Export schema/manifest version test
- Cross-domain relationship export test
- Document/file hash round-trip test
- Permission/scope test
- Interrupted/partial export cleanup test

**Out of scope / future extension** Vendor-specific adapters remain future work. Import/migration is intentionally separated into `ALV-018-C02`.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-018-C01` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-018-C01/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-018-C01: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-018-C01_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 47 — ALV-018-C02** below.

# 47 — ALV-018-C02 — Validated Bulk Import and Migration Workbench

**Type:** Companion  
**Parent:** STORY-018  
**Dependencies:** STORY-018, ALV-018-C01, ALV-017-C01, ALV-004-C01, ALV-007-C01

### Full Coding-Agent Prompt

**Story:** ALV-018-C02 — Validated Bulk Import and Migration Workbench

Parent course story: STORY-018 — its completion contract is immutable and must keep passing.

**Dependencies:** STORY-018, ALV-018-C01, ALV-017-C01, ALV-004-C01, ALV-007-C01

**User story:** As an authorized data manager, I want dry-run-first import/migration tooling so that patients, appointments and opening financial balances can be loaded without silently corrupting identity, scheduling or ledger truth.

**Why this story exists:** Import has different risks from export. It changes authoritative records, must cooperate with duplicate detection/scheduling rules, and must never bypass the financial ledger by directly overwriting a balance.

**Implementation scope**

- CSV/tabular import for patients, appointments and opening balances as required by the original vision
- Mapping, preview and dry-run before commit
- Versioned import-batch/source metadata and row provenance
- Validation and row-level errors
- Duplicate-patient candidate handling through ALV-017-C01 review logic; never silent auto-merge
- Appointment import validates patient/provider/operatory/time data and uses the scheduling conflict service; conflicts are rejected/reported or deliberately resolved by an authorized workflow, never silently double-booked
- Opening balances create explicit migration-origin ledger transactions/adjustments with source batch/provenance; never directly set a mutable “balance” total
- Defined batch atomicity/partial-success policy visible to the operator
- Safe restart/retry/idempotency for interrupted imports
- Batch history, reconciliation counts and audit

**UI/UX deliverables**

- Import wizard with source/type selection, mapping, preview, validation summary and explicit dry-run
- Row-level downloadable error report
- Duplicate/conflict resolution queues
- Batch reconciliation screen: attempted, accepted, rejected, skipped, unresolved
- Clear confirmation before authoritative commit

**Security, audit, and data-integrity requirements**

- Restricted import/migration permission
- Server-side validation and authorization
- Import files treated as untrusted input
- Audit actor/source batch/result without logging full sensitive file contents
- Migration-origin financial entries use the authoritative ledger and remain historically distinguishable

**Failure paths to handle**

- Malformed/oversize file
- Unknown or duplicate columns
- Invalid references
- Duplicate-patient candidate
- Appointment conflict
- Invalid opening balance/currency
- Partial batch errors
- Process/server interruption during commit
- Retry after an uncertain previous result

**Acceptance / stop condition**

- Original STORY-018 malformed-import rejection behavior still passes
- Dry-run performs all material validation without authoritative writes
- Invalid rows are reported precisely
- Duplicate candidates are routed through controlled review
- Imported appointments cannot silently bypass provider/operatory/patient conflict rules
- Opening balances are represented by traceable migration-origin ledger transactions, not direct total overwrite
- Interrupted/retried import cannot duplicate committed rows or leave an unexplained partial state
- Batch reconciliation and audit explain exactly what happened

**Required tests**

- Original STORY-018 import regression
- Dry-run/no-write test
- Malformed/partial batch tests
- Duplicate-patient integration
- Scheduling-conflict import integration
- Opening-balance ledger/provenance test
- Restart/retry/idempotency test
- Batch reconciliation test

**Out of scope / future extension** Vendor-specific adapters and automated external migrations remain future work.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-018-C02` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-018-C02/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-018-C02: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-018-C02_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 48 — ALV-N006** below.

# 48 — ALV-N006 — Operational, Clinical, Financial Reporting, and Outcome Measurement

**Type:** New Production  
**Dependencies:** ALV-007-C02, ALV-009-C01, ALV-011-C01, ALV-017-C01

### Full Coding-Agent Prompt

**Story:** ALV-N006 — Operational, Clinical, Financial Reporting, and Outcome Measurement

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-007-C02, ALV-009-C01, ALV-011-C01, ALV-017-C01

**User story:** As an office manager and product owner, I want trustworthy reports plus measured workflow baselines so that the practice can understand operations and Alveara can evaluate improvement without inventing success claims.

**Why this story exists:** Reporting disappeared from v4. The project also defined success targets such as reducing measurable nonclinical overhead and lowering conflicts/corrections, but v2 did not define how a baseline would be captured.

**Implementation scope**

- Schedule report
- Patient-flow report
- Production/charges
- Collections/payments
- Balances
- Treatment-plan status
- Completed procedures
- Provider activity
- Recall/follow-up
- Cancellations/no-shows
- Date/provider/location filters
- Define measurable baseline events/metrics for scheduling conflicts, billing corrections/reversals, duplicate-patient detection/merge, incomplete/unsigned notes, missed/overdue follow-ups and other metrics that the product can truthfully observe
- Capture baseline periods before claiming improvement where historical data exists
- Support comparison periods without claiming causation
- Track duplicate-entry/administrative-time metrics only where the application can measure them validly; otherwise mark them unknown/not yet measurable
- Treat the ~20–30% nonclinical-overhead reduction as a product target, not an automatically achieved result

**UI/UX deliverables**

- Reporting hub with filter bar, summary cards and drill-down
- Every important number links to supporting records where practical
- Measurement/baseline panel clearly labels baseline, comparison period, sample size/record count and unavailable metrics
- Export/print where authorized and safe

**Security, audit, and data-integrity requirements**

- Role-based access to financial vs clinical reports
- Report/export access audited where appropriate
- Aggregate views must not bypass row/scope authorization
- Never display a claimed improvement percentage when no valid baseline/comparison exists

**Failure paths to handle**

- No data
- Large date range
- Stale/changed source during generation
- Unauthorized filter/scope
- Baseline unavailable
- Metric definition changes between periods

**Acceptance / stop condition**

- Each first-release report category is represented
- Financial totals reconcile to ledger source data
- Patient-flow counts reconcile to visit states
- Baseline metrics show supporting counts/records and never fabricate missing history
- Improvement percentages appear only when a valid comparable baseline exists
- No-data and large-range states are honest and usable
- Restricted users cannot access unauthorized financial/clinical detail

**Required tests**

- Reconciliation tests
- Filter/authorization tests
- Baseline/comparison calculation tests
- Missing-baseline truthfulness test
- Metric-definition version test
- Performance smoke test

**Out of scope / future extension** Forecasting, benchmarking and predictive analytics remain later work. Do not convert operational correlation into clinical/business causation claims.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N006` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N006/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N006: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N006_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **GATE E** below.

# GATE E — OPERATIONS, DOCUMENTS, AND DATA READY

**MANDATORY STOP. Do not begin the next numbered item until this gate passes.**

## Gate execution prompt

``` text
ALVEARA DENTAL — GATE E — Operations, Documents, and Data Ready

Evaluate Gate E against the current repository and the exact gate evidence requirements below.

Read .alveara/BUILD_STATE.md, .alveara/EXECUTION_STATUS.json, .alveara/QUALITY_GATES.md, all handoffs/reviews created since the prior gate, and the current code/tests. Do not rely on planning claims when implementation evidence disagrees.

For every criterion:
1. identify the exact test/check/measurement and representative environment/data;
2. use the predeclared pass/block threshold or rubric;
3. execute or inspect the evidence needed to judge it truthfully;
4. record PASS, FAIL, or BLOCKED with evidence references in .alveara/QUALITY_GATES.md;
5. do not lower a threshold after seeing a failure merely to make the gate pass.

If every blocking criterion passes, record Gate E PASS with the evidence/commit information and report that the next execution item is authorized.

If any blocking criterion fails or cannot be truthfully verified, record Gate E FAIL/BLOCKED, identify the responsible story/stories or missing evidence, and STOP. Do not begin the next phase. Repair/reopen/revalidate the responsible work first, then rerun this same gate.

When the integrated verification is substantial, create a reproducible gate evidence package named Alveara_Gate_E_<evidence-short-sha>.zip. Never include secrets or real PHI.

GATE E EVIDENCE REQUIREMENTS

Required evidence:

- Document/image imports are safe, integrity-checked, backed up and recoverable.
- Follow-up/recall work queues and due internal reminders operate through durable background work and survive restart without duplication.
- Referral/lab tracking works with document links.
- Duplicate detection/merge preserves linked data with recovery strategy as implemented.
- Versioned export package identifies included/excluded domains and preserves representative file integrity.
- Import/migration dry-runs first, respects duplicate/scheduling/ledger rules, and reports errors without silent loss.
- Reporting reconciles to source records.
- Baseline/outcome metrics never claim improvement without valid comparable data.

Return the gate decision, criterion-by-criterion results, evidence references, repository state/commit information, blockers if any, and the next authorized action.
```

**PASS → continue directly to item 49 — ALV-N007. FAIL/BLOCKED → stop; do not move forward.**

# 49 — ALV-N007 — AI-Assisted Clinical Documentation and Review

**OPTIONAL AI BRANCH — DECIDE HERE:**

- **Include AI in this release:** execute `ALV-N007` now. After it is reviewed and closed `COMPLETE`, continue to item 50 — `ALV-N012`.
- **Defer AI from this release:** set **both** `ALV-N007` and `ALV-N012` to `DEFERRED` in `.alveara/EXECUTION_STATUS.json`, record the product-owner reason and release impact for each, verify the required non-AI core has no dependency on either story, and jump directly to **item 51 — ALV-N013**. Do not execute item 50 when `ALV-N007` is deferred.

**Type:** New Production  
**Dependencies:** ALV-005-C01, ALV-006-C01, ALV-013-C01, ALV-N011

### Full Coding-Agent Prompt

**Story:** ALV-N007 — AI-Assisted Clinical Documentation and Review

No course parent. This is production work omitted by the generated course backlog.

**Dependencies:** ALV-005-C01, ALV-006-C01, ALV-013-C01, ALV-N011

**User story:** As a clinician, I want AI-assisted documentation drafts that remain under my control so that documentation is faster without making AI authoritative.

**Why this story exists:** This is the prioritized standout capability from the sharpening questions and was dropped from the generated backlog.

**Implementation scope**

- Optional provider-agnostic AI boundary
- Voice/dictation transcription input path
- Draft SOAP/progress/treatment note
- Extract referenced teeth/procedures where appropriate
- Relevant history/safety summary
- Documentation completeness flags
- Clear AI provenance and review state
- AI disabled mode leaves app fully functional

**UI/UX deliverables**

- AI draft panel beside normal note editor
- Highlight extracted/uncertain items
- Accept/reject/edit controls at field/section level where practical
- Clear 'AI-generated draft — not finalized' labeling

**Security, audit, and data-integrity requirements**

- PHI sent only to explicitly approved deployment/configuration
- No plaintext secrets in AI payload/logs
- AI cannot silently diagnose, prescribe, finalize, alter authoritative records or execute financial actions

**Failure paths to handle**

- Provider unavailable/timeout
- Low-confidence extraction
- Partial transcript
- AI response malformed
- AI disabled

**Acceptance / stop condition**

- Core documentation works with AI off
- AI output is always non-authoritative until clinician acceptance
- Uncertainty is surfaced rather than guessed
- No AI action can finalize a consequential record
- Provider failure degrades to normal manual workflow

**Required tests**

- AI-off tests
- Mock provider timeout/malformed-response tests
- Human-acceptance boundary tests
- Permission/privacy payload tests

**Out of scope / future extension** Private/local models can extend this boundary. Authorized NL chart search, summaries, patient-friendly explanations and administrative assistance are separated into `ALV-N012` so this clinical-documentation story stays focused.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N007` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N007/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N007: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N007_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 50 — ALV-N012** below.

# 50 — ALV-N012 — AI-Assisted Authorized Search, Summaries, Explanations, and Administrative Follow-Up

**OPTIONAL AI BRANCH — ITEM 50 IS REACHABLE ONLY IF ITEM 49 WAS EXECUTED:**

`ALV-N012` depends on `ALV-N007`. Do **not** execute this story if `ALV-N007` was deferred.

- If `ALV-N007` is `COMPLETE`, you may execute `ALV-N012` now.
- If you completed `ALV-N007` but choose to defer only this second AI enhancement, set `ALV-N012` to `DEFERRED`, record the product-owner reason and release impact, verify no required core workflow depends on it, and continue directly to **item 51 — ALV-N013**.
- If `ALV-N007` was deferred, `ALV-N012` must already have been marked `DEFERRED`; jump to item 51.

**Type:** New Production — Optional first-release enhancement  
**Dependencies:** ALV-N007, ALV-N006, ALV-015-C01, ALV-009-C01

### Full Coding-Agent Prompt

**Story:** ALV-N012 — AI-Assisted Authorized Search, Summaries, Explanations, and Administrative Follow-Up

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-N007, ALV-N006, ALV-015-C01, ALV-009-C01

**User story:** As an authorized user, I want assistive AI to summarize permitted records, answer chart questions with source evidence, explain treatment plans in patient-friendly language, and draft low-risk follow-up content so that information is easier to use without giving AI authority over the record.

**Why this story exists:** The original product vision included several lower-risk AI opportunities beyond documentation. v2 deferred them all while jumping from note drafting to much more advanced future AI.

**Implementation scope**

- Authorized natural-language search/question interface over data the signed-in user may access
- Source-linked history/record summaries with explicit provenance
- Patient-friendly treatment-plan explanation drafts derived from the actual plan, clearly labeled as draft/explanation rather than clinical consent or diagnosis
- Administrative follow-up/reminder draft assistance that never sends or changes authoritative state without human confirmation
- Reuse provider-agnostic AI boundary and AI-off behavior from ALV-N007
- Refuse/flag questions when evidence is insufficient rather than inventing answers
- Record AI provenance/model/provider/configuration metadata appropriate to the implementation

**UI/UX deliverables**

- Search/ask panel with cited/source-linked results
- Summary view with “source records used” affordance
- Patient-explanation draft with edit/approve/copy workflow
- Administrative-draft review screen
- Clear AI-generated/not-authoritative labeling

**Security, audit, and data-integrity requirements**

- Server authorization filters retrieval before AI generation and before results are shown
- PHI only leaves the system through explicitly approved AI configuration
- AI cannot alter diagnoses, prescriptions, treatment completion, billing, permissions, finalized notes or send communications without an authorized human action
- AI actions/output review state audited where consequential

**Failure paths to handle**

- No supporting records
- Conflicting records
- Provider timeout/malformed response
- Unauthorized question scope
- AI returns an unsupported statement
- AI feature disabled

**Acceptance / stop condition**

- Authorized chart questions return source-linked evidence or explicitly say evidence is insufficient
- A restricted user cannot retrieve data outside their authorization scope through AI
- Patient-friendly explanation is a reviewable draft tied to the actual treatment plan
- Administrative AI does not send/complete work without human confirmation
- AI-off mode leaves the underlying product workflows fully functional
- Unsupported/hallucinated claims are not silently accepted into authoritative records

**Required tests**

- Authorization-filter tests
- Source/provenance tests
- Insufficient-evidence test
- Provider timeout/malformed-response tests
- Human-confirmation boundary tests
- AI-off regression

**Out of scope / future extension** Advanced imaging AI, predictive analytics and autonomous/decision-support claims remain `ALV-F010` and require separate validation/governance.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N012` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N012/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N012: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N012_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 51 — ALV-N013** below.

# 51 — ALV-N013 — Windows Installation, Upgrade, Schema Migration, and Rollback

**Type:** New Production  
**Dependencies:** ALV-N004, ALV-018-C01, ALV-N002

### Full Coding-Agent Prompt

**Story:** ALV-N013 — Windows Installation, Upgrade, Schema Migration, and Rollback

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-N004, ALV-018-C01, ALV-N002

**User story:** As the practice administrator, I want a repeatable install and upgrade path with safe migration/rollback so that a local Alveara server can be maintained without risking clinical or financial history.

**Why this story exists:** A production local Windows product needs more than deployment instructions. Upgrades will eventually include schema and configuration changes that must be recoverable.

**Implementation scope**

- Repeatable first-time Windows-server installation/deployment path for the chosen stack
- Service/application startup and restart behavior
- Versioned application/configuration/schema migration workflow
- Pre-upgrade compatibility check and verified backup requirement
- Upgrade health verification
- Failure rollback strategy for application binaries/configuration and database migration according to what the chosen database safely supports
- Explicit handling when automatic rollback of a schema migration is unsafe: stop, preserve evidence and restore from verified backup rather than inventing reversal
- Configuration migration/versioning
- Upgrade log/status that avoids secrets/PHI
- Version reporting in admin System Status

**UI/UX deliverables**

- Admin-visible install/upgrade status/checklist appropriate to deployment approach
- Current version, schema/config version and last upgrade result
- Clear recovery instructions/state when an upgrade fails

**Security, audit, and data-integrity requirements**

- Administrative authorization for upgrade operations where initiated from the app
- Pre-upgrade backup verification
- No secrets/PHI in upgrade logs
- Never claim rollback succeeded unless the old version/data actually passes health checks

**Failure paths to handle**

- Installer/deployment interrupted
- Service fails to start
- Database migration fails halfway
- New application incompatible with current schema/config
- Backup missing/unverified
- Rollback cannot safely reverse schema change

**Acceptance / stop condition**

- Clean install starts a healthy local server/application
- A test upgrade from a previous fixture version migrates schema/config and preserves representative data
- Failed migration does not get reported as successful
- Recovery path is demonstrated: safe rollback where supported or verified-backup restore where rollback is unsafe
- System Status reports application/schema/config versions truthfully
- Course/root Command Center artifacts remain intact in the repository

**Required tests**

- Clean-install smoke test
- Upgrade migration test
- Failed-migration/recovery test
- Version/config migration test
- Service startup/restart test
- Backup prerequisite integration test

**Out of scope / future extension** Code-signing/commercial distribution processes can be added as commercialization matures, but first-release maintenance must already be safe and reproducible.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N013` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N013/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N013: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N013_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 52 — ALV-N014** below.

# 52 — ALV-N014 — Security and Privacy Engineering Readiness

**Type:** New Production  
**Dependencies:** ALV-001-C01, ALV-002-C01, ALV-N004, ALV-010-C01, ALV-018-C01, ALV-018-C02, ALV-N013

### Full Coding-Agent Prompt

**Story:** ALV-N014 — Security and Privacy Engineering Readiness

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-001-C01, ALV-002-C01, ALV-N004, ALV-010-C01, ALV-018-C01, ALV-018-C02, ALV-N013

**User story:** As the product owner, I want a focused security/privacy engineering review before production candidacy so that local deployment, sensitive records, exports, logs and recovery are hardened without falsely claiming regulatory certification.

**Why this story exists:** v2 placed a few security checks only inside the final release gate. A U.S.-first dental EHR/PMS benefits from an explicit engineering readiness pass before the final end-to-end gate.

**Implementation scope**

- Threat-oriented review of authentication, authorization, session controls, audit, exports/imports, backups/recovery, documents and admin operations
- Review AI configuration/security only if ALV-N007 and/or ALV-N012 is actually included in the release; absence of optional AI must not block the safe core product
- Encryption-at-rest strategy review for database/files/backups and protection of recovery material; document what is and is not encrypted by the chosen stack/deployment
- LAN transport/TLS configuration review
- Secret storage/configuration review
- Dependency/package vulnerability scanning process and remediation threshold
- Security headers/cookie/session configuration appropriate to the web stack
- PHI/logging review
- Least-privilege service/file/database access review
- Export/download access review
- Configurable retention/inactivation policy seams where applicable; do not automatically purge clinical/financial history without an approved policy
- Audit-review/admin visibility
- Security/privacy operational checklist and documented residual risks

**UI/UX deliverables**

- Admin Security/Privacy Readiness page or checklist surface where operational settings/status must be visible
- No fake “HIPAA compliant” or legal-certification badge
- Clear residual-risk/configuration warnings

**Security, audit, and data-integrity requirements**

- All findings must distinguish engineering control from legal/regulatory certification
- High-severity security/data-exposure defects block production candidacy according to the documented severity/triage rubric in `.alveara/QUALITY_GATES.md`; severity criteria must be chosen before evaluating the release and not weakened afterward merely to pass
- Sensitive diagnostics/evidence must not leak secrets/PHI into public repo artifacts

**Failure paths to handle**

- Weak/missing TLS configuration
- Excessive service/file permissions
- Export accessible to wrong role
- Dependency scanner reports high-severity issue
- Logs contain prohibited sensitive payload
- Encryption/recovery configuration incomplete

**Acceptance / stop condition**

- Security/privacy checklist is completed with evidence and residual risks
- Critical authorization/export/logging/secret-storage tests pass
- Findings at or above the pre-declared blocking severity threshold are fixed or production is explicitly blocked
- Encryption/transport/recovery protections are documented truthfully
- No legal/regulatory certification is claimed solely from this engineering review

**Required tests**

- Authorization/export regression tests
- Secret/logging inspection tests
- Security-header/session configuration tests
- Dependency scan with documented result
- Least-privilege deployment check
- Backup/recovery protection review

**Out of scope / future extension** Formal HIPAA/legal/compliance assessment, penetration testing by independent specialists, certification/attestation and organizational policies remain external activities where required.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N014` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N014/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N014: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N014_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **item 53 — ALV-N008** below.

# 53 — ALV-N008 — Production-Candidate Gate: End-to-End Recovery, Accessibility, Performance, and Release Verification

**Type:** New Production  
**Dependencies:** ALV-N004, ALV-N006, ALV-N013, ALV-N014, ALV-018-C01, ALV-018-C02, ALV-007-C02

### Full Coding-Agent Prompt

**Story:** ALV-N008 — Production-Candidate Gate: End-to-End Recovery, Accessibility, Performance, and Release Verification

No course parent. This is production work omitted by the generated course backlog or deliberately separated from course completion.

**Dependencies:** ALV-N004, ALV-N006, ALV-N013, ALV-N014, ALV-018-C01, ALV-018-C02, ALV-007-C02

**User story:** As the product owner, I want a formal production-candidate gate so that Alveara is not considered ready merely because individual stories pass.

**Why this story exists:** A dental EHR/PMS needs system-level proof across workflow, integrity, recovery, deployment, security, UX and performance. Optional AI must not be required for the safe core product.

**Implementation scope**

- End-to-end visit workflow validation
- Role/permission matrix regression
- Audit/finalization/concurrency regression across implemented domains
- Full-state backup/restore drill
- Clean-install and upgrade/recovery drill using ALV-N013
- Accessibility review on critical workflows against the pre-declared accessibility target/rubric in `.alveara/QUALITY_GATES.md`
- Performance smoke/baseline tests for high-frequency screens and representative large datasets against pre-declared metrics/thresholds in `.alveara/QUALITY_GATES.md`; do not choose thresholds after seeing the results
- Error/empty/disconnected states review
- Data import/export recovery/round-trip check
- Security/privacy readiness evidence from ALV-N014
- Operational runbook and support diagnostics
- Versioned release artifact/deployment instructions
- Verify optional AI can be disabled without breaking core workflows

**UI/UX deliverables**

- Final consistency sweep across critical screens
- No dead-end navigation, placeholder actions or misleading sample/live indicators
- Responsive/keyboard checks on registration, scheduling, clinical charting, treatment completion and billing
- Admin status clearly shows version, backup health and critical configuration without exposing secrets

**Security, audit, and data-integrity requirements**

- Threat-oriented regression of auth/permissions/audit/exports/backups/logs
- No unresolved high-severity security or data-integrity finding
- No claim of legal/regulatory certification unless independently established

**Failure paths to handle**

- Restore drill fails
- Upgrade/recovery drill fails
- Permission leak
- Critical workflow partial failure
- Migration/import/export round-trip problem
- Performance regression
- AI-disabled mode breaks a core feature

**Acceptance / stop condition**

- Full registration→forms/consent→scheduling→flow→history/safety→odontogram/perio→diagnosis→documentation→treatment plan→procedure completion→billing/payment→follow-up demo passes
- Full-state backup/restore drill passes
- Clean install and representative upgrade/recovery drill pass
- Critical role-denial cases pass
- No unresolved high-severity data-integrity/security/recovery defect remains
- Critical screens meet the measurable accessibility/performance thresholds committed in `.alveara/QUALITY_GATES.md` before final evaluation
- Core product works with AI disabled
- Release documentation/runbook is complete and truthful

**Required tests**

- End-to-end suite
- Security/authorization suite
- Full-state backup restore drill
- Install/upgrade/recovery drill
- Accessibility smoke
- Performance smoke
- Migration/import/export round-trip
- AI-disabled core regression

**Out of scope / future extension** Independent security/compliance assessments, commercial distribution certification/code-signing and future roadmap capabilities are separate gates.

**When you finish — package this story for review**

- Do not start the next story. A successful coding run ends at `AWAITING_REVIEW`, not `COMPLETE`.
- After implementation and all required tests/regressions pass, commit the implementation using `ALV-N008` in the message and capture the full implementation SHA.
- Create/update `.alveara/handoffs/ALV-N008/<attempt>.md`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and any quality-gate evidence affected by this story. Set this story to `AWAITING_REVIEW`.
- Commit those evidence/status changes separately (the committed handoff need not contain this evidence commit's own SHA) using `ALV-N008: record engineering handoff` and capture the handoff/evidence SHA.
- Follow `.alveara/HANDOFF_SCHEMA.md` and generate `Alveara_Handoff_ALV-N008_<implementation-short-sha>_<attempt>.zip`. The ZIP must contain the canonical manifest, current state/status/gate snapshots, exact test results, acceptance-by-acceptance evidence, parent-regression evidence or explicit N/A, demo evidence or justified N/A, changed-files summary, Git state, and next-story impact.
- Validate the ZIP by reopening/listing it and confirming all mandatory members exist and are non-empty where applicable.
- Never include secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, or real-data backups in the package. Do not copy the entire repository by default.
- In the final response, return the ZIP path, attempt ID, implementation SHA, handoff/evidence SHA, concise implementation/test summary, parent-regression result when applicable, blockers/limitations, and state `AWAITING_REVIEW`. Tell the user to upload the single ZIP to the reviewing ChatGPT conversation. Do not mark `COMPLETE`; after approval, perform only the review-closure protocol before any next story. **Working method** Work in small reversible steps. Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and the handoffs named by this story's dependencies when they exist, then inspect current code/tests. Code/tests are authoritative if they conflict with planning or packaged prose. Reuse existing services/components rather than rebuilding adjacent stories. If this story affects a defined success metric, use the shared privacy-safe measurement-event convention. If a required architectural change would invalidate a verified course criterion, stop and explain the conflict. When implementation is ready, demonstrate the visible workflow where applicable, map evidence to every acceptance item, package the validated attempt ZIP, and stop for review. Preserve prior attempts; after approval, record the review-closure commit before any next story.

**NEXT:** Do not continue until this story has been reviewed and, when approved, the review-closure commit has recorded it `COMPLETE`. Then continue directly to **GATE F** below.

# GATE F — PRODUCTION CANDIDATE

**MANDATORY STOP. Do not begin the next numbered item until this gate passes.**

## Gate execution prompt

``` text
ALVEARA DENTAL — GATE F — Production Candidate

Evaluate Gate F against the current repository and the exact gate evidence requirements below.

Read .alveara/BUILD_STATE.md, .alveara/EXECUTION_STATUS.json, .alveara/QUALITY_GATES.md, all handoffs/reviews created since the prior gate, and the current code/tests. Do not rely on planning claims when implementation evidence disagrees.

For every criterion:
1. identify the exact test/check/measurement and representative environment/data;
2. use the predeclared pass/block threshold or rubric;
3. execute or inspect the evidence needed to judge it truthfully;
4. record PASS, FAIL, or BLOCKED with evidence references in .alveara/QUALITY_GATES.md;
5. do not lower a threshold after seeing a failure merely to make the gate pass.

If every blocking criterion passes, record Gate F PASS with the evidence/commit information and report that the next execution item is authorized.

If any blocking criterion fails or cannot be truthfully verified, record Gate F FAIL/BLOCKED, identify the responsible story/stories or missing evidence, and STOP. Do not begin the next phase. Repair/reopen/revalidate the responsible work first, then rerun this same gate.

When the integrated verification is substantial, create a reproducible gate evidence package named Alveara_Gate_F_<evidence-short-sha>.zip. Never include secrets or real PHI.

GATE F EVIDENCE REQUIREMENTS

Required evidence:

- Windows clean install, upgrade, schema/config migration and recovery/rollback drill pass.
- Security/privacy engineering-readiness review has no unresolved high-severity blocker.
- Full-state backup/restore drill passes on the release candidate.
- End-to-end visit lifecycle passes: registration → forms/consent → scheduling → check-in/flow → history/safety → odontogram/perio → diagnosis/documentation → treatment plan → procedure completion → billing/payment → follow-up.
- Critical screens meet the agreed accessibility/performance baseline.
- Core product works with public internet unavailable and with AI disabled.
- Optional AI, if included, remains reviewable, source-aware and non-authoritative.
- No dead-end placeholder action or misleading sample/live/health/clinical/financial state remains in critical workflows.
- Operational runbook and release evidence are complete.

Return the gate decision, criterion-by-criterion results, evidence references, repository state/commit information, blockers if any, and the next authorized action.
```

**PASS → continue directly to FIRST-PRODUCTION BASELINE / FUTURE ROADMAP. FAIL/BLOCKED → stop; do not move forward.**

# FIRST-RELEASE EXECUTION COMPLETE

Do not declare the first-production baseline until Gate F has passed.

# FUTURE ROADMAP — NOT PART OF THE FIRST RELEASE

The following `ALV-F*` items are **future executable coding-agent prompts**. They are not part of the first release and are not an automatic sequence. After Gate F, execute a future story only when the product owner deliberately selects it. Give the coding agent the selected story's complete prompt below together with the current Execution Plan and Engineering & Product Reference.

## ALV-F001 — True Multi-Location and Multi-Practice Operations

**Type:** Future roadmap  
**Purpose:** Extend the **domain/operational model** into independent locations, cross-location scheduling/reporting, provider sharing, data/permission partitioning and tenant-aware business rules. This story owns multi-location/multi-practice semantics, not cloud hosting/client deployment.

### Full Future Coding-Agent Prompt

**Story:** ALV-F001 — True Multi-Location and Multi-Practice Operations

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Evolve the domain, authorization and operational model for true multi-location and later multi-practice tenancy while remaining deployable in the existing hosting model. Cloud hosting/client evolution is owned by ALV-F009.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F002 — Dental Insurance, Benefits, Estimates, Claims, and Clearinghouse Integration

**Type:** Future roadmap  
**Purpose:** Add plan/benefit data, eligibility, estimates, claim lifecycle, attachments, remittance and payer/clearinghouse integrations.

### Full Future Coding-Agent Prompt

**Story:** ALV-F002 — Dental Insurance, Benefits, Estimates, Claims, and Clearinghouse Integration

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Add plan/benefit data, eligibility, estimates, claim lifecycle, attachments, remittance and payer/clearinghouse integrations.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F003 — Patient Portal and Communications

**Type:** Future roadmap  
**Purpose:** Add secure patient access for forms, balances, treatment plans, appointments, messages and configurable email/SMS communications.

### Full Future Coding-Agent Prompt

**Story:** ALV-F003 — Patient Portal and Communications

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Add secure patient access for forms, balances, treatment plans, appointments, messages and configurable email/SMS communications.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F004 — Direct Dental Imaging and DICOM/Device Integration

**Type:** Future roadmap  
**Purpose:** Demonstrate real-time image acquisition from a supported device, safe patient/tooth linkage, failure recovery and replaceable vendor adapters.

### Full Future Coding-Agent Prompt

**Story:** ALV-F004 — Direct Dental Imaging and DICOM/Device Integration

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Demonstrate real-time image acquisition from a supported device, safe patient/tooth linkage, failure recovery and replaceable vendor adapters.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F005 — Electronic Prescribing and Pharmacy Connectivity

**Type:** Future roadmap  
**Purpose:** Add eRx workflows behind the prescription boundary with provider identity, transmission status and external-service failure handling.

### Full Future Coding-Agent Prompt

**Story:** ALV-F005 — Electronic Prescribing and Pharmacy Connectivity

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Add eRx workflows behind the prescription boundary with provider identity, transmission status and external-service failure handling.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F006 — Integrated and Online Payments

**Type:** Future roadmap  
**Purpose:** Add processor/terminal adapters, tokenized payment methods, idempotent transactions, reconciliation and online payment links.

### Full Future Coding-Agent Prompt

**Story:** ALV-F006 — Integrated and Online Payments

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Add processor/terminal adapters, tokenized payment methods, idempotent transactions, reconciliation and online payment links.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F007 — Inventory and Supply Management

**Type:** Future roadmap  
**Purpose:** Track stock, reorder levels, lots/expiry where needed and procedure-linked consumption without coupling inventory to clinical core.

### Full Future Coding-Agent Prompt

**Story:** ALV-F007 — Inventory and Supply Management

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Track stock, reorder levels, lots/expiry where needed and procedure-linked consumption without coupling inventory to clinical core.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F008 — FHIR/HL7 and External Clinical Interoperability

**Type:** Future roadmap  
**Purpose:** Expose/import approved clinical data through standards-based adapters with mapping, provenance and failure queues.

### Full Future Coding-Agent Prompt

**Story:** ALV-F008 — FHIR/HL7 and External Clinical Interoperability

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Expose/import approved clinical data through standards-based adapters with mapping, provenance and failure queues.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F009 — Cloud SaaS, Desktop/Mobile Clients, Enterprise Identity, and Off-Site Operations

**Type:** Future roadmap  
**Purpose:** Evolve **hosting and client deployment** to cloud/SaaS, desktop/mobile clients, enterprise SSO and off-site operations after ALV-F001 has defined any required multi-location/multi-practice domain/tenant semantics.

### Full Future Coding-Agent Prompt

**Story:** ALV-F009 — Cloud SaaS, Desktop/Mobile Clients, Enterprise Identity, and Off-Site Operations

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Evolve deployment/hosting/client surfaces using the established domain and tenant boundaries rather than redefining multi-location/multi-practice business semantics here.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F010 — Advanced AI, Imaging Analysis, Predictive Analytics, and Benchmarking

**Type:** Future roadmap  
**Purpose:** Add higher-risk AI only after governance, validation and human-control requirements are defined; retain explicit provenance and non-autonomous clinical decision boundaries.

### Full Future Coding-Agent Prompt

**Story:** ALV-F010 — Advanced AI, Imaging Analysis, Predictive Analytics, and Benchmarking

This is a future Alveara roadmap story. Do not begin until the first production release and its integrity/security/recovery gates are stable.

**Goal:** Add higher-risk AI only after governance, validation and human-control requirements are defined; retain explicit provenance and non-autonomous clinical decision boundaries.

**Requirements**

- Reuse the existing domain/API/security/audit/concurrency boundaries; do not bypass them.
- Preserve all prior clinical and financial history semantics.
- Deliver a visible UI workflow together with the integration/functionality.
- Treat every external call as fallible: explicit timeout, bounded retry only where semantically safe, idempotency where applicable, and a visible failure/recovery state.
- Preserve provenance of imported/external data.
- Add authorization and audit coverage appropriate to the capability.
- Add contract/integration tests with fakes or sandbox systems before any production credential is used.

**Stop condition**

- The feature works end to end in a controlled test environment.
- Failure and recovery paths are demonstrated.
- Existing production regression suites pass.
- No existing course completion contract or production integrity rule regresses.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F011 — Specialty Workflow Expansion

**Type:** Future roadmap  
**Purpose:** Add specialty-specific workflows for periodontics, endodontics, oral surgery, orthodontics, pediatric dentistry, prosthodontics and implant care without fragmenting the shared patient, chart, diagnosis, procedure and billing model.

### Full Future Coding-Agent Prompt

**Story:** ALV-F011 — Specialty Workflow Expansion

This is a future Alveara roadmap story. Do not begin until the general-dentistry core is stable.

**Goal:** Add specialty workflows as modular extensions of the shared clinical core rather than separate incompatible mini-applications.

**Requirements**

- Reuse patient identity, encounter, diagnosis, treatment plan, completed procedure, documents, audit, permissions and billing foundations.
- Add specialty-specific data only where the shared model cannot accurately represent it.
- Preserve longitudinal history and cross-specialty visibility.
- Deliver role-appropriate UI workspaces without forking the entire navigation/design system.
- Add specialty-specific validation and tests.
- Do not claim clinical completeness for a specialty until its workflows have been reviewed by appropriate domain experts.

**Stop condition**

- At least one selected specialty works end to end on top of the shared core.
- Shared general-dentistry workflows do not regress.
- Specialty data remains visible in the patient longitudinal record.
- Authorization, audit and finalization behavior remains consistent.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.

## ALV-F012 — Accounting and Business-System Integration

**Type:** Future roadmap  
**Purpose:** Integrate approved financial/operational data with accounting or business systems without making the external system the authoritative clinical ledger.

### Full Future Coding-Agent Prompt

**Story:** ALV-F012 — Accounting and Business-System Integration

This is a future Alveara execution story. Begin only after Gate F and only when the product owner has selected a concrete target system/API. If no concrete target is supplied, stop `BLOCKED` and request that decision rather than inventing one.

**Goal:** Export/synchronize authorized accounting-ready transactions, payments, adjustments or summary data to selected business systems while preserving Alveara's authoritative clinical/guarantor/ledger history and explicit reconciliation.

**Requirements**

- Select a concrete target/integration contract before implementation; examples may include accounting platforms or enterprise systems, but do not assume any vendor API exists or behaves a certain way without current documentation.
- Define authoritative ownership of each field/transaction and one-way vs two-way boundaries before coding.
- Preserve immutable source links, external IDs, sync status and reconciliation evidence.
- Use explicit timeout, bounded retry only when semantically safe, idempotency and failure queues.
- Never duplicate a financial transaction because a sync is retried.
- Expose reconciliation/error UI and audit integration.
- Do not send clinical PHI that the accounting purpose does not require.
- Contract/integration tests must use fakes/sandboxes before real credentials.

**Stop condition**

- The selected integration works end to end in a controlled environment.
- Duplicate/retry behavior cannot duplicate financial effects.
- Reconciliation can explain what was sent, accepted, rejected or changed.
- Alveara ledger truth remains reconstructable independently of the external system.
- Authorization/audit/privacy boundaries pass.

**Future-story execution and review protocol**

- Begin only after Gate F has passed and the product owner has deliberately selected this future story.
- Before editing, read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/QUALITY_GATES.md`, the Engineering & Product Reference, relevant completed handoffs/reviews, and current code/tests.
- Treat current code/tests as implementation truth. Revalidate external standards, vendor/device/API constraints, security requirements, and integration assumptions that may have changed since this plan was written. If a material assumption is obsolete or a required product/vendor decision is missing, stop `BLOCKED` and state the exact decision/update required; do not guess.
- Identify and record the concrete current dependencies before implementation. Do not bypass existing domain/API/security/audit/concurrency boundaries.
- Implement the requirements above with visible workflow, authorization, audit, failure/recovery handling, data-integrity protection, and tests appropriate to the selected capability.
- Preserve all first-release course contracts and production integrity rules. Do not silently rewrite finalized clinical/financial history.
- Use the normal immutable attempt lifecycle from `.alveara/HANDOFF_SCHEMA.md`: implementation commit → evidence/status commit → validated `Alveara_Handoff_<ALV-F-ID>_<implementation-short-sha>_<attempt>.zip` → `AWAITING_REVIEW`.
- The coding agent must not self-certify `COMPLETE`. Stop after producing the validated handoff ZIP and final response. After reviewer approval, perform the normal review-closure commit before any other future story begins.
- Never package secrets, credentials, tokens, encryption/recovery keys, real PHI/patient data, or real-data backups.
