# Alveara Dental — Master Course + Production Story Plan v3.3

**Status:** Final execution-control hardened master engineering backlog. v3.3 preserves the v3.2 product scope, course contracts, ALV acceptance criteria, story identities, and engineering order while synchronizing with Execution Index v1.2 and closing bootstrap/review/retry/gate/revalidation edge cases.

## 1. Purpose

The course backlog is a **completion scaffold**, not the full production specification. This plan preserves every course completion trigger while adding the work required to turn the generated project into a production-quality dental EHR/PMS.

Two tracks are maintained at all times:

- **Course Track:** original `STORY-xxx` stories. Their portal prompts and exact Done Means/Acceptance text are immutable. Complete them truthfully, update `.colaberry/progress.json`, commit with the course story ID, and push.
- **Production Track:** `ALV-*` stories. These may extend, refactor, or replace implementation details after the course contract is satisfied, but they must never regress an original course acceptance behavior.

### Core invariant

> Production improvement may supersede implementation, but it may never invalidate a course completion contract that has already been truthfully verified.

### v3 Red-Team Revision Summary

Before implementation began, v2 was challenged against the complete Alveara vision, the v1→v4 course-prompt lessons, the authoritative course requirement register, every original course Done Means contract, the production companion strategy, UI-from-day-one requirement, local Windows deployment goal, safety/integrity rules, and the new `.alveara` engineering-handoff workflow.

The challenge did **not** invalidate the dual-track strategy. It did expose execution risks that v3 corrects:

- The financial dependency is corrected: a financial ledger/charge foundation now exists **before** authoritative procedure completion creates billable charges; statement/receipt hardening follows completion.
- The first UI story no longer pretends RBAC exists before authentication. `ALV-N001` establishes the shell/design foundation; `ALV-N009` adds real authorization-aware navigation/session UX after authentication.
- The root course Command Center and the real Alveara product UI now have an explicit coexistence rule so product work cannot accidentally replace the course-required root `index.html`.
- Shared audit/concurrency/finalization infrastructure is reduced to reusable **primitives** early; domain-specific finalization semantics are implemented by the clinical/financial stories that actually own those records.
- Practice configuration is narrowed to the configuration needed for early scheduling/operations; domain-specific templates/defaults are added by the domain stories that need them.
- Clinical safety alerts and medical-clearance state move earlier in the clinical lifecycle rather than being owned by the prescription story.
- Prescription “dosage accuracy” is explicitly bounded: structural/entered-dose validation may be implemented locally; clinical dose-appropriateness decision support must not be claimed without an authoritative medication knowledge source.
- Backup/recovery now covers the full application state, recovery-key/material handling, visible success/failure, and a verified restore drill rather than database-only copying.
- The architecture foundation now defines time, money, identity separation, offline semantics, PHI-safe diagnostics, and a minimal durable background-work mechanism before later stories depend on them.
- Core odontogram scope now includes primary/permanent/mixed dentition; periodontal charting defines efficient full-mouth site entry; diagnosis gets an explicit structured coding/provenance model.
- Forms/consent/e-signature foundations move earlier so the visit workflow does not reach treatment completion before basic signed forms exist.
- Reporting now owns baseline/measurement instrumentation for the project's stated success metrics; Alveara must not claim improvement percentages without a valid baseline.
- Every production story must read `BUILD_STATE` and relevant handoffs **before** editing. The handoff now records the parent implementation actually observed.
- First-release deployment now includes install/upgrade/schema-migration/rollback behavior, not merely a runbook.
- A separate security/privacy engineering-readiness story is added without claiming regulatory certification.
- Future roadmap items are explicitly marked as roadmap implementation briefs, not execution-ready prompts; accounting/business-system integration is restored to the roadmap.
- Intermediate production gates are added so the project can stop and repair a weak subsystem before more work is stacked on top of it.

The real v4 course portal remains course-only unless a concrete future course benefit justifies adding a custom portal story.

### v3.1 Execution-Hardening Summary

A second challenge of v3 found implementation-level gaps rather than a failure of the overall strategy. v3.1 keeps the course/product dual-track model and hardens it in these specific ways:

- Restores explicit course-parent metadata and immutable-parent regression instructions for every course companion, including second companions.
- Starts privacy-safe operational measurement-event capture in the architecture foundation instead of waiting until reporting.
- Makes the integrated patient workspace a cross-cutting product rule so later modules extend one coherent patient context rather than becoming isolated pages.
- Removes premature clinical-safety requirements from the visit-flow story; the later safety story explicitly retrofits minimal permission-appropriate safety indicators where useful.
- Connects required forms/consents to check-in readiness.
- Requires recall/reminder work to use the durable background-work mechanism so restarts do not silently lose due work.
- Splits portability into `ALV-018-C01` versioned export and `ALV-018-C02` validated import/migration work.
- Requires at least one first-release MFA/recovery path that functions without public internet.
- Forbids home-grown cryptography and requires proven authenticated-encryption/platform cryptographic facilities.
- Makes the Windows server the sole database owner; LAN clients use the application/API and never open the database file over a network share.
- Requires consequential audit evidence to be transactionally coupled with authoritative writes or protected by an equivalent durable pattern.
- Strengthens procedure-code identity/provenance, longitudinal medical/dental history semantics, patient-overlap scheduling checks, and treatment-plan estimate truthfulness.
- Gives document categories and recall defaults explicit domain owners rather than returning them to a generic configuration bucket.
- Tightens the security-readiness dependency graph and makes AI review conditional on AI actually being included in the release.
- Adds objective gate-evidence rules so accessibility, performance, security severity, and recovery checks cannot become ceremonial.
- Removes execution/handoff boilerplate from future-roadmap briefs until a future item is deliberately promoted into an execution-ready story.
- Clarifies the boundary between future multi-location/multi-practice domain work and future cloud/SaaS/client deployment evolution.

This is a hardening release of the master plan, not a new product direction.

### v3.2 Execution-Control Synchronization Summary

v3.2 is not a product redesign. It synchronizes the Master Plan with `Alveara_Dental_Execution_Index_v1.1.md` so a coding agent cannot finish an ALV story using the older fragmented handoff process.

- Adds `.alveara/EXECUTION_STATUS.json` as the centralized internal production-story status ledger.
- Adds `.alveara/HANDOFF_SCHEMA.md` as the durable repository contract for handoff-package structure.
- Makes a validated `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>.zip` mandatory after every non-course story.
- Requires each coding agent to finish at `AWAITING_REVIEW`; it may not self-certify `COMPLETE`.
- Keeps `.alveara/handoffs/<ALV-ID>.md` and `.alveara/BUILD_STATE.md` as repository evidence/current truth, while packaging snapshots of the relevant evidence into the ZIP for review.
- Requires acceptance-by-acceptance evidence, exact test results, parent-course regression evidence for companions, demo evidence where applicable, Git state, changed-file summary, and next-story impact in the package.
- Keeps the ZIP compact and free of secrets/real PHI; the full repository is not copied into every package by default.
- Preserves code/tests as the ultimate implementation truth if packaged prose is stale.
- The reviewing ChatGPT returns `APPROVED → COMPLETE`, `CHANGES_REQUIRED`, `BLOCKED`, or `REOPENED`; only approval permits the next ALV story to begin.

The Execution Index controls **when** work runs and what follows it; this Master Plan controls **what** each story must build; `HANDOFF_SCHEMA.md` controls **how** the result is packaged; `EXECUTION_STATUS.json` records **where production execution stands**.

### v3.3 Final Execution-Control Hardening Summary

The final cross-check of Master Plan v3.2 against Execution Index v1.2 and a simulated end-to-end daily workflow validated the product/story design but exposed operational edge cases. v3.3 corrects only execution machinery:

- pairs this plan with Execution Index v1.2 and Handoff Schema v2;
- requires STEP 0 execution-control bootstrap before STORY-000;
- removes self-referential Git-SHA requirements: committed evidence records the implementation SHA; the generated ZIP records the now-known evidence SHA;
- adds immutable review attempts (`R01`, `R02`, ...) and durable review-decision records;
- adds a review-closure commit that moves an approved story from `AWAITING_REVIEW` to repository-recorded `COMPLETE`;
- defines blocked-attempt packaging when material work/analysis occurred;
- makes Gate A–F explicit stop points between phases;
- makes optional-AI include/defer behavior explicit;
- adds targeted `REVALIDATION_REQUIRED` handling when a completed dependency is reopened;
- corrects `ALV-009-C02` to declare its course parent `STORY-009` explicitly as a dependency.

No original course Done Means text, ALV story acceptance/stop condition, product requirement, or first-release story identity is changed by v3.3.

## 2. Story ID Convention

- `STORY-xxx` — original course story. Never rename or rewrite its completion contract.
- `ALV-xxx-Cyy` — production **companion** to course `STORY-xxx`. Example: `ALV-004-C01` extends `STORY-004`.
- `ALV-Nyyy` — production story with **no course parent** because the generator omitted the capability or because it must be built earlier than the portal unlocks its closest course story.
- `ALV-Fyyy` — future roadmap story after the first production release.

## 2.1 Portal Add-Story Feature Policy

The portal's **Add a story** feature was tested in the disposable v3.1 project with two custom stories. The experiment established that custom acceptance lines are preserved verbatim and can become normal portal Done Means criteria. It also exposed weak autogenerated requirement wrappers, unreliable owner assignment, release/dependency behavior that is not needed for production engineering, and additional progress/enrichment/commit obligations.

**Decision for the real v4 project:** do not use portal-added stories for the Alveara production backlog by default.

The portal is the **course compliance system**. This master plan is the **product engineering system**.

Use the portal Add Story feature later only if there is a concrete course benefit such as graded points, instructor-required visibility, or another requirement that cannot be achieved through the original course stories. Do not add production stories merely for tracking.

### Repository ownership boundaries

- `.colaberry/` is course/platform-controlled except for the exact updates the course instructs us to make.
- `docs/` may be rewritten by the course platform; do not use it as the durable home for Alveara production handoffs.
- `.alveara/` is our production-engineering namespace.
- `.alveara/handoffs/<ALV-ID>/<attempt>.md` stores immutable attempt-level engineering handoffs (`R01`, `R02`, ...).
- `.alveara/reviews/<ALV-ID>/<attempt>.md` stores durable reviewer decisions used by review closure.
- `.alveara/BUILD_STATE.md` stores concise durable facts about the application as it actually exists now.
- `.alveara/EXECUTION_STATUS.json` stores the current internal execution state/evidence summary for course and ALV stories; it does not replace course-portal authority for `STORY-*`.
- `.alveara/QUALITY_GATES.md` stores predeclared measurable production-gate criteria and evidence.
- `.alveara/HANDOFF_SCHEMA.md` stores the canonical non-course handoff-ZIP contract used by every coding-agent run.
- `.alveara/EXECUTION_INDEX.md` may hold the repository copy of Execution Index v1.2 and is the navigation/order reference; it does not replace this Master Plan's story specifications.
- The course Command Center's root `index.html` is protected course surface. The production Alveara application must live in its own application/project path and must not replace, repurpose, or silently break the root Command Center used by STORY-000.
- Production build/deployment may serve Alveara as the local application entry point, but repository-root course artifacts remain intact unless the current course contract explicitly changes.
- Never place secrets, credentials, webhook signing secrets, real PHI, or real patient data in `.alveara/` because the repository is public.

## 2.2 Authoritative Course Requirement Register

This is the final `REQ-001`–`REQ-020` register exposed by STORY-000. It is the best source for what the course itself believes the product requires.

| ID      | Kind       | Requirement                                                                                                                                        |
|---------|------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| REQ-001 | SAFE       | The system must support unique user accounts with secure password storage and configurable session timeout.                                        |
| REQ-002 | SAFE       | The system must provide role-based access control (RBAC) for dentist, hygienist, assistant, front desk, billing, office manager, and admin roles.  |
| REQ-003 | SAFE       | The system must audit account and role changes with user and timestamp.                                                                            |
| REQ-004 | FUNC       | The system must allow patient registration with demographics, contact information, and family/household relationships.                             |
| REQ-005 | FUNC       | The system must support scheduling with multiple providers, operatories, appointment types, and configurable durations.                            |
| REQ-006 | FUNC       | The system must track patient flow states from scheduled to completed, including check-in and treatment.                                           |
| REQ-007 | FUNC       | The system must support structured clinical documentation including medical and dental history, allergies, and medications.                        |
| REQ-008 | FUNC       | The system must provide an interactive odontogram with tooth and surface selection, supporting existing, diagnosed, planned, and completed states. |
| REQ-009 | FUNC       | The system must support periodontal charting with probing depth, recession, and bleeding.                                                          |
| REQ-010 | FUNC       | The system must allow structured diagnosis linked to patient, encounter, and treatment plan.                                                       |
| REQ-011 | FUNC       | The system must manage a procedure/fee catalog with codes, descriptions, and fees.                                                                 |
| REQ-012 | FUNC       | The system must support treatment planning with diagnosis linkage, proposed procedures, and fee estimates.                                         |
| REQ-013 | FUNC       | The system must transition planned care into completed procedures, updating clinical history and billing.                                          |
| REQ-014 | FUNC       | The system must handle billing with charges, payments, and adjustments, preserving financial history.                                              |
| REQ-015 | FUNC       | The system must import and categorize documents and forms, supporting e-signature and metadata linkage.                                            |
| REQ-016 | FUNC       | The system must support prescriptions with medication, dosage, and allergy checks.                                                                 |
| REQ-017 | FUNC       | The system must enable follow-up tasks with recall intervals, reminders, and completion history.                                                   |
| REQ-018 | FUNC       | The system must support duplicate detection and controlled merge of patient records, preserving linked data.                                       |
| REQ-019 | FUNC       | The system must provide data portability with patient export and import capabilities.                                                              |
| REQ-020 | CONSTRAINT | The system must support local Windows server deployment with a responsive web client usable offline.                                               |

### Requirement-register gaps confirmed against the original Alveara vision

The final course register itself dropped or compressed several production-critical items, including MFA, full audit/finalization/concurrency coverage, encrypted backup/restore, practice configuration, complete scheduling operations, complete clinical documentation, complete periodontal data, richer treatment-plan states, complete billing operations, broader safety alerts, referrals/labs, reporting, AI, and most future integrations. These are restored through the `ALV-*` backlog below.

## 2.3 Foundational Engineering Invariants

These are cross-cutting rules, not separate permission to build speculative frameworks.

### Course/product coexistence

- Preserve the root STORY-000 Command Center and its runtime reads from `.colaberry/` files.
- Keep the actual Alveara application in a distinct application/project structure.
- A production story may consume shared repository tooling, but it must not convert the course Command Center into the dental product UI.

### Time and timezone

- Store true event instants in an unambiguous form suitable for comparison/audit and retain the practice timezone needed for local presentation.
- Keep date-only clinical/identity values such as date of birth as date-only values; do not accidentally timezone-shift them.
- Scheduling logic must distinguish practice-local appointment time from audit/system instants.
- Daylight-saving transitions and ambiguous/invalid local appointment times must have explicit tests before production scheduling is considered complete.

### Money

- Never use binary floating-point arithmetic for authoritative financial amounts.
- Establish one documented currency/rounding strategy for first release and preserve amount snapshots on plans, charges, payments, statements, and reversals where history requires it.
- Financial balances must be reconstructable from preserved authoritative transactions rather than from silently overwritten totals.

### Identity separation

- A login/user account, a staff/personnel profile, and a clinical provider profile are related concepts but are not the same entity.
- Link them explicitly where appropriate rather than making later scheduling/clinical attribution depend on authentication-table identity.

### Offline semantics

- “Works offline” for first release means **no public-internet dependency for core workflows while the local Windows server/LAN is available**.
- Loss of LAN/server connectivity is a separate failure mode. Do not silently accept authoritative edits into an unsynchronized browser cache unless a later story explicitly designs and verifies disconnected editing.

### Durable background work

- Scheduled backups, recalls, and later automation must use a small durable local background-work mechanism with persisted job state, restart recovery, observable failure, and operation-specific retry/idempotency rules.
- Do not introduce distributed brokers/microservices merely to satisfy this rule.

### Files/documents

- Stored file identity must not depend on user-supplied filenames.
- Preserve metadata and integrity information needed to detect missing/corrupt blobs.
- Later document imports must validate type/content/size, prevent path traversal/executable treatment, and participate in backup/restore.

### PHI and diagnostics

- Do not write unnecessary PHI, secrets, tokens, passwords, recovery material, or full clinical payloads into ordinary diagnostic logs.
- Support correlation/troubleshooting without turning logs into a shadow patient record.

### Encryption/recovery material

- Encryption keys or recovery material must not live only inside the encrypted content they are required to restore.
- Backup encryption design must include a tested administrative recovery path and explicit handling of missing/wrong recovery material.

### Integrated patient workspace

- Once a patient exists, user-facing patient features should extend one persistent patient-context workspace rather than create unrelated application islands.
- The patient context must make identity unambiguous and prevent stale data from a previously viewed patient remaining visible after patient switch/navigation.
- Add only the modules/status summaries that actually exist at that point in the build; do not show placeholder balances, diagnoses, alerts, unsigned notes or treatment state that have not been implemented.
- Later stories should contribute role-appropriate patient navigation/status indicators through shared extension points instead of duplicating patient headers.

### Measurement events

- Establish one small, versioned, privacy-minimized operational measurement-event convention early.
- Feature stories that affect stated success metrics should emit only the events/counts needed to measure them; do not create a shadow analytics copy of the patient chart.
- Metric definitions must be versioned so later reports can distinguish a real trend from a changed definition.
- Measurement instrumentation is observational and must never become the authoritative source for clinical or financial state.

### Local database topology

- The Windows server process owns database access. Browser/LAN clients communicate through the application/API and must never open or mutate the database file through a network share.
- If an embedded database is used, backup must use a database-consistent backup/snapshot mechanism rather than blindly copying a live file.
- Database/storage permissions should be restricted to the server/service identity and authorized maintenance operations.

### Cryptography

- Do not design custom cryptographic algorithms, custom authenticated-encryption formats, or ad-hoc key derivation.
- Use maintained, well-reviewed platform/library cryptographic facilities with authenticated encryption where confidentiality and integrity are required.
- Recovery/key-management behavior must be tested independently from the encrypted payload it protects.

### Audit coupling

- For consequential clinical, financial, security and merge/configuration operations that require audit evidence, the business write and required audit evidence must commit atomically where feasible.
- If one local transaction cannot cover both, use an equivalent durable pattern such as a transactional outbox that prevents the authoritative action from becoming permanently unaudited.
- A best-effort log call after a successful authoritative write is not sufficient for required audit events.

### Domain finalization

- Shared infrastructure may provide audit/concurrency/finalization primitives, but each clinical/financial domain story defines what “finalized,” “amended,” “voided,” “reversed,” or “inactive” actually means for its records.
- Avoid a generic trust framework that invents lifecycle semantics for domain objects that do not exist yet.

## 3. Course Completion Protocol

For every course story:

1.  Execute the portal-generated prompt unchanged unless it would violate an explicit project guardrail.
2.  Preserve each Acceptance / Done Means line **word for word**.
3.  Implement and test the behavior before claiming it.
4.  Change only the corresponding `passed` value in `.colaberry/progress.json` from `false` to `true` after verification.
5.  Create the required `.colaberry/enrichment/STORY-xxx.json` using evidence actually produced by that story.
6.  Commit with `STORY-xxx: <what you did>` (or the equivalent accepted Story line) and push.
7.  Confirm the portal sees the push. Never mark criteria true merely to unlock the next story.
8.  Production companion work may follow immediately, but its commits should use the `ALV-*` ID and must retain regression tests for the parent course contract.
9.  Do not add the companion to the real course portal simply to make it visible there; execute it from this master plan/repository workflow.
10. Do not begin the next production story automatically after a companion finishes. The companion must generate its validated handoff ZIP, enter `AWAITING_REVIEW`, and stop. Review that package first and revise the next prompt if implementation reality changed.

### Portal Done Means precedence

The sampled portal pages (`STORY-001`, `STORY-002`, `STORY-004`) showed that **Done Means is identical to the prompt's Acceptance / stop condition**, and STORY-000 explicitly states that the lines are text-matched. This plan therefore preserves the Acceptance text as the course completion contract. If any future portal page ever differs from the stored prompt, the **portal's current Done Means text wins** and must be copied word for word before implementation continues.

## 4. Engineering Execution Cycle, Status Tracking, and Handoff Package Protocol

The production backlog is a living engineering plan. Execution order is governed by Execution Index v1.2 plus real course unlock state.

### STEP 0 — bootstrap before STORY-000

Before any story work, initialize and commit `.alveara/EXECUTION_INDEX.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/BUILD_STATE.md`, `.alveara/QUALITY_GATES.md`, `.alveara/handoffs/`, and `.alveara/reviews/` exactly as specified by Execution Index v1.2. The status ledger is prepopulated with all 53 first-release items and control-set versions Master Plan `3.3`, Index `1.2`, Handoff Schema `2`.

### Production tracking stack

- `.colaberry/` + course portal — authoritative for original `STORY-*` completion.
- `.alveara/EXECUTION_STATUS.json` — authoritative internal production execution state.
- `.alveara/handoffs/<ALV-ID>/<attempt>.md` — immutable attempt-level handoff history.
- `.alveara/reviews/<ALV-ID>/<attempt>.md` — durable review decisions.
- `.alveara/BUILD_STATE.md` — concise current product truth.
- `.alveara/QUALITY_GATES.md` — measurable integrated/release gate evidence.
- `.alveara/HANDOFF_SCHEMA.md` — canonical Handoff Schema v2.
- `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip` — portable attempt package.

### Status lifecycle

Normal: `PLANNED → READY → IN_PROGRESS → AWAITING_REVIEW → COMPLETE`

Exceptional: `BLOCKED`, `CHANGES_REQUIRED`, `DEFERRED`, `REOPENED`, `REVALIDATION_REQUIRED`.

The implementation agent never self-certifies `COMPLETE`.

### Normal ALV attempt

1.  Read current execution/build/gate state, Handoff Schema, dependency handoffs/reviews and current code/tests.
2.  Use the next attempt ID (`R01`, then `R02`, ... if review requires correction).
3.  Implement/test/demonstrate exact acceptance; companions also reverify the immutable parent course contract.
4.  Commit implementation and capture its SHA.
5.  Write the attempt handoff, BUILD_STATE/status/gate evidence and set `AWAITING_REVIEW`. The committed handoff records the implementation SHA but does **not** claim the SHA of the evidence commit that contains itself.
6.  Commit evidence and capture its SHA.
7.  Generate/validate the attempt ZIP. Generated `MANIFEST.json` and `GIT_STATE.md` record both known SHAs.
8.  Stop for review.

### Review and closure

Review returns `APPROVED`, `CHANGES_REQUIRED`, `BLOCKED`, or `REOPENED` in `Alveara_Review_<ALV-ID>_<attempt>.md`.

If approved, the coding/repository agent verifies the decision matches the story/attempt/SHAs, stores it under `.alveara/reviews/`, updates status to `COMPLETE`, records the approved attempt/evidence SHA, and makes `ALV-ID: record review approval`. Only then may the next execution item begin. The closure commit is not required to contain its own SHA.

If changes are required, preserve the attempt and review, set `CHANGES_REQUIRED`, increment the attempt, correct the same story, and produce a new package. Never overwrite earlier attempts.

If materially blocked after work begins, produce a BLOCKED package whenever technically possible; never fabricate missing evidence.

### Reopen and revalidation

A reopened completed story triggers dependency-impact analysis. Mark only potentially affected completed descendants `REVALIDATION_REQUIRED`, correct/review-close the reopened story, rerun targeted regression/integration checks, and reevaluate affected phase gates. Do not blindly reopen every later story.

### Phase gates

Execution Index v1.2 inserts mandatory Gate A–F stops after atomic items 10, 17, 30, 37, 48 and 53 respectively. A failed gate blocks progression into the next phase until repaired/revalidated.

### Optional AI branch

After Gate E, either execute/review-close `ALV-N007` and `ALV-N012`, or deliberately mark them `DEFERRED` with reason/impact and verify the safe core has no AI dependency before proceeding to release engineering.

### ALV completion contract

An ALV story becomes `COMPLETE` only after exact acceptance, required tests, parent regression where applicable, truthful UI demonstration where applicable, security/integrity obligations, no blocking in-scope defect, implementation/evidence commits, attempt handoff, current BUILD_STATE/status/gate evidence, validated ZIP, reviewer approval, and review-closure commit.

### Git-SHA rule

A committed file is never required to contain the SHA of the commit that contains that same file. Repository attempt evidence records the implementation SHA. The ZIP generated after the evidence commit records the evidence SHA. The later review-closure commit records the approved evidence SHA and decision; Git history identifies the closure commit.

### Package safety

Never package secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, real-data backups, dependency caches, or irrelevant build output. Do not copy the entire repository by default. The ZIP is review evidence; current code/tests remain implementation truth.

### BUILD_STATE rule

`BUILD_STATE.md` is current truth, not a diary. Keep durable architecture/capability/schema/UI/security/limitation facts and amend stale facts when implementation changes.

## 5. UI/UX Rule From Day One

UI presentation is part of the definition of done for production work, not a cleanup phase.

Every production feature story must deliver:

- a usable visible workflow using the shared design system;
- loading, empty, validation, permission, error, and offline/partial-failure states where relevant;
- keyboard-efficient interaction for high-frequency office/clinical tasks;
- responsive desktop/tablet behavior appropriate to a dental operatory/front desk;
- role-aware actions consistent with server authorization;
- no misleading sample, status, connection, health, clinical, or financial information.

Infrastructure-only stories must provide an admin-visible status/configuration surface when an operator needs to understand the feature.

## 6. Course Unlock Order vs. Production Dependency Order

**Portal/course unlock order is authoritative for course completion.**  
**Production dependency order is authoritative for engineering quality.**

Known portal gates from the generated plan:

- Start: `STORY-000`, `STORY-001`, `STORY-002`.
- After `STORY-002`: `STORY-003`, `STORY-004`, `STORY-011`.
- After `STORY-011`: `STORY-005`, `STORY-006`, `STORY-012`, `STORY-013`, `STORY-015`.
- After `STORY-015`: `STORY-007`, `STORY-008`, `STORY-014`, `STORY-016`.
- After `STORY-016`: `STORY-009`, `STORY-010`, `STORY-017`, `STORY-018`.

The generated prompts contain some contradictory “Where this sits” references; use the portal unlock state for course gating.

### Recommended production sequence

This sequence is the engineering recommendation, not permission to bypass portal gates. Course stories can only be truthfully completed when their portal requirements and dependencies are available.

**Foundation**

1.  `STORY-000`
2.  `ALV-N001` — product shell/design foundation while preserving the root course Command Center
3.  `ALV-N002` — local architecture/data/time/money/background-work foundation
4.  `STORY-001` → `ALV-001-C01`
5.  `ALV-N009` — real authorization-aware navigation/session UX
6.  `STORY-002` → `ALV-002-C01`
7.  `ALV-N003` — scheduling/operational practice configuration only
8.  `ALV-N004` — full-state encrypted backup/recovery

**STOP — Gate A must pass before the next phase.**

**Patient, scheduling, and visit entry** 9. `STORY-003` → `ALV-003-C01` 10. `ALV-N010` — form/consent/e-signature foundation 11. `STORY-004` → `ALV-004-C01` 12. `STORY-011` → `ALV-011-C01`

**STOP — Gate B must pass before the next phase.**

**Clinical core** 13. `STORY-005` → `ALV-005-C01` 14. `ALV-N011` — patient safety alerts and medical-clearance framework 15. `STORY-006` → `ALV-006-C01` 16. `STORY-012` → `ALV-012-C01` 17. `STORY-013` → `ALV-013-C01` 18. `ALV-N005` — production procedure/fee catalog before treatment planning 19. `STORY-015` → `ALV-015-C01` 20. When the portal unlocks it, run `STORY-014` against `ALV-N005` and satisfy its immutable course contract.

**STOP — Gate C must pass before the next phase.**

**Treatment completion and financial core** 21. `STORY-007` → `ALV-007-C01` — ledger/charge/payment foundation first 22. `STORY-008` → `ALV-008-C01` — authoritative one-or-many procedure completion using the financial foundation 23. `ALV-007-C02` — statements, receipts, refunds/reversals, account UX, and financial hardening 24. `STORY-016` → `ALV-016-C01` — prescriptions consume the already-existing safety framework

**STOP — Gate D must pass before the next phase.**

**Operations, records, and data** 25. `STORY-010` → `ALV-010-C01` 26. `STORY-009` → `ALV-009-C01` 27. `ALV-009-C02` 28. `STORY-017` → `ALV-017-C01` 29. `STORY-018` → `ALV-018-C01` — versioned export/package 30. `ALV-018-C02` — validated import/migration 31. `ALV-N006` — reporting plus baseline/outcome measurement

**STOP — Gate E must pass before optional AI/release engineering.**

**Optional assistive AI and release engineering** 32. Optional branch: execute `ALV-N007` — AI-assisted clinical documentation — **or mark it `DEFERRED` with reason/impact** 33. Optional branch: execute `ALV-N012` — authorized AI search/summaries/explanations/admin assistance — **or mark it `DEFERRED` with reason/impact** 34. `ALV-N013` — Windows install/upgrade/schema-migration/rollback 35. `ALV-N014` — security/privacy engineering-readiness review 36. `ALV-N008` — final production-candidate gate

**STOP — Gate F must pass before declaring the first-production baseline.**

At every arrow or production-story boundary, review the latest `.alveara` handoff before continuing. This sequence can be interleaved with portal unlocks as needed; never falsely mark a course story complete to preserve engineering order.

# Part I — Original Course Stories, Immutable Completion Contracts, and Gap Analysis

## STORY-000 — Build your Command Center

**Type:** Original course story  
**Requirement:** Course infrastructure / project control  
**Original user story:** Build the course Command Center that reflects the project plan and verified progress.

### Immutable Course Completion Contract

1.  Given the Command Center, when it is opened, then every tab is reachable and every card drills down one level.
2.  Given sample mode, when any tab is shown, then the sample data is visibly labelled as sample.
3.  Given the Command Center, when any tab renders, then .colaberry/plan.json and .colaberry/progress.json are both committed in this repo and every tab reads its content from them at runtime rather than from hard-coded values.
4.  Given the Command Center, when any tab is shown, then .colaberry/manifest.json is committed in this repo and every tab shows how old that data is and warns when the age exceeds a week.
5.  Trust — no tab shows a number, a connection or a result the project has not actually produced.

### Production Gap Analysis

This is a course/project-management UI, not the Alveara Dental product shell. It must remain separate from the production application.

### Production Linkage

- ALV-N001

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-001 — Implement secure user authentication and RBAC

**Type:** Original course story  
**Requirement:** REQ-001, REQ-002  
**Original user story:** As an admin, I want secure user authentication and role-based access control, so that user access is appropriately managed.

### Immutable Course Completion Contract

1.  Given a new user, when they register, then their account is created with secure password storage.
2.  Given a user, when they attempt to log in with incorrect credentials multiple times, then their account is locked.
3.  Trust: All account and role changes are audited with user and timestamp.

### Production Gap Analysis

Good password/RBAC skeleton, but the generated requirement dropped MFA entirely. Acceptance does not prove role allow/deny behavior, account disablement, recovery, session administration, or a usable security-administration UI.

### Production Linkage

- ALV-001-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-002 — Establish audit logging for critical actions

**Type:** Original course story  
**Requirement:** REQ-003  
**Original user story:** As a system auditor, I want audit logging for critical actions, so that changes are traceable.

### Immutable Course Completion Contract

1.  Given a user changes their role, when the change is saved, then the action is logged with a timestamp.
2.  Given a user attempts to modify an audit log, when they do not have permission, then the action is denied.
3.  Trust: Audit logs are immutable and protected from unauthorized modification.

### Production Gap Analysis

Course scope is account/role auditing only. Production needs application-wide clinical, financial, administrative, security, merge, configuration and backup audit; finalized-record semantics; stale-write detection; and explicit concurrency behavior.

### Production Linkage

- ALV-002-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-003 — Implement patient registration workflow

**Type:** Original course story  
**Requirement:** REQ-004  
**Original user story:** As a front desk staff, I want to register patients, so that their information is accurately captured in the system.

### Immutable Course Completion Contract

1.  Given a new patient, when they provide their information, then the system captures demographics and contact details.
2.  Given a patient with incomplete information, when they attempt to register, then the system prompts for required fields.
3.  Trust: All registration actions are logged with user and timestamp.

### Production Gap Analysis

The REQ mentions family/household relationships but the acceptance contract does not. Production also needs guarantor/responsible party, active/inactive status, duplicate warning, safe updates, patient search, concurrency handling, and polished intake UI.

### Production Linkage

- ALV-003-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-004 — Enable appointment scheduling with conflict prevention

**Type:** Original course story  
**Requirement:** REQ-005  
**Original user story:** As a scheduler, I want to schedule appointments without conflicts, so that provider and operatory availability is respected.

### Immutable Course Completion Contract

1.  Given a provider is available, when an appointment is scheduled, then it is confirmed without conflicts.
2.  Given a provider is double-booked, when a new appointment is attempted, then the system rejects it.
3.  Trust: All scheduling actions are logged with user and timestamp.

### Production Gap Analysis

REQ keeps providers/operatories/types/durations, but acceptance tests only provider conflict. Production needs operatory conflict, availability, blocked time, reschedule, cancel, no-show, scheduler views, conflict UX and concurrency protection.

### Production Linkage

- ALV-004-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-005 — Develop clinical documentation module

**Type:** Original course story  
**Requirement:** REQ-007  
**Original user story:** As a clinician, I want to document patient encounters, so that clinical history is accurately recorded.

### Immutable Course Completion Contract

1.  Given a patient encounter, when documented, then the system records medical and dental history.
2.  Given an encounter note is finalized, when an amendment is needed, then the original is preserved with an addendum.
3.  Trust: All documentation changes are logged with user and timestamp.

### Production Gap Analysis

Production documentation also requires allergies, medications, vitals, SOAP/progress/treatment notes, templates, free text, encounter linkage, signing/finalization UX, amendment history, concurrency and completeness states.

### Production Linkage

- ALV-005-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-006 — Implement interactive odontogram

**Type:** Original course story  
**Requirement:** REQ-008  
**Original user story:** As a dentist, I want an interactive odontogram, so that I can accurately record dental conditions and treatments.

### Immutable Course Completion Contract

1.  Given a tooth is selected, when a condition is recorded, then it is saved with the correct lifecycle state.
2.  Given a completed treatment, when recorded, then the odontogram updates to reflect the change.
3.  Trust: All odontogram updates are logged with user and timestamp.

### Production Gap Analysis

Lifecycle states survived well, but production still needs surface-specific findings, a comprehensive condition/restoration vocabulary, longitudinal history, anatomical tooth identity independent of numbering and future FDI/ISO support.

### Production Linkage

- ALV-006-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-007 — Enable billing workflow with financial history preservation

**Type:** Original course story  
**Requirement:** REQ-014  
**Original user story:** As a billing specialist, I want to manage billing, so that financial transactions are accurately recorded and preserved.

### Immutable Course Completion Contract

1.  Given a procedure is completed, when billed, then the charge is recorded with the correct amount.
2.  Given a payment is made, when recorded, then the balance updates and history is preserved.
3.  Trust: All billing actions are logged with user and timestamp.

### Production Gap Analysis

Course REQ includes adjustments, but acceptance does not test them. Production also needs voids/refunds, statements/invoices, receipts, payment methods, guarantor responsibility, correction history, transaction UI and concurrency.

### Production Linkage

- ALV-007-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-008 — Complete treatment plan and update clinical history

**Type:** Original course story  
**Requirement:** REQ-013  
**Original user story:** As a clinician, I want to complete treatment plans, so that patient care is accurately reflected in their clinical history.

### Immutable Course Completion Contract

1.  Given a treatment plan is accepted, when completed, then the clinical history updates accordingly.
2.  Given a treatment plan is revised, when updated, then the original plan is preserved.
3.  Trust: All treatment plan changes are logged with user and timestamp.

### Production Gap Analysis

The REQ says completion also updates billing, but the acceptance contract does not verify charge creation. Production needs an atomic planned→completed transition, odontogram update, preserved plan history, procedure-linked charge, finalization/amendment and duplicate-completion protection.

### Production Linkage

- ALV-008-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-009 — Manage follow-up tasks and reminders

**Type:** Original course story  
**Requirement:** REQ-017  
**Original user story:** As a clinician, I want to manage follow-up tasks, so that patient care is continuous and timely.

### Immutable Course Completion Contract

1.  Given a follow-up task is created, when due, then a reminder is sent to the responsible party.
2.  Given a task is completed, when marked, then the completion history is updated.
3.  Trust: All follow-up actions are logged with user and timestamp.

### Production Gap Analysis

Recall interval exists in REQ but is not tested. Production needs unscheduled-treatment queues, owner/due/priority/status, appointment links, notes, work queues and separate referral/lab tracking.

### Production Linkage

- ALV-009-C01
- ALV-009-C02

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-010 — Implement document import and categorization

**Type:** Original course story  
**Requirement:** REQ-015  
**Original user story:** As an admin, I want to import and categorize documents, so that patient records are comprehensive and organized.

### Immutable Course Completion Contract

1.  Given a document is imported, when categorized, then it is linked with the correct metadata.
2.  Given a form is e-signed, when completed, then the signed version is preserved.
3.  Trust: All document actions are logged with user and timestamp.

### Production Gap Analysis

This is stronger than many course stories, but production still needs explicit imaging/document categories, encounter/tooth-region metadata, configurable form templates, exact signed-version immutability after template edits, preview/download UX and file validation.

### Production Linkage

- ALV-010-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-011 — Track patient flow states from scheduled to completed

**Type:** Original course story  
**Requirement:** REQ-006  
**Original user story:** As a receptionist, I want to track patient flow states from scheduled to completed, so that I can manage patient appointments efficiently.

### Immutable Course Completion Contract

1.  Given a patient is scheduled, when they check in, then the system updates their status to 'checked-in'.
2.  Given a patient is checked-in, when treatment is completed, then the system updates their status to 'completed'.
3.  Trust: All status changes are logged with timestamps and user IDs.

### Production Gap Analysis

Acceptance compresses the workflow to checked-in→completed. Production needs scheduled, confirmed, arrived, ready, seated, in treatment, checked out, completed, cancelled/no-show, provider/operatory assignment and a live flow board.

### Production Linkage

- ALV-011-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-012 — Support periodontal charting with probing depth, recession, and bleeding

**Type:** Original course story  
**Requirement:** REQ-009  
**Original user story:** As a dentist, I want to perform periodontal charting with probing depth, recession, and bleeding, so that I can accurately assess patient periodontal health.

### Immutable Course Completion Contract

1.  Given a patient record, when periodontal charting is performed, then the system records probing depth, recession, and bleeding.
2.  Given incorrect charting data, when saved, then the system rejects the input and prompts for correction.
3.  Trust: All charting entries are logged with user ID and timestamp.

### Production Gap Analysis

Production requires CAL, suppuration, mobility, furcation, plaque, periodontal diagnosis/history, efficient keyboard entry and longitudinal comparisons.

### Production Linkage

- ALV-012-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-013 — Allow structured diagnosis linked to patient, encounter, and treatment plan

**Type:** Original course story  
**Requirement:** REQ-010  
**Original user story:** As a clinician, I want to record structured diagnoses linked to patient, encounter, and treatment plan, so that I can maintain comprehensive patient records.

### Immutable Course Completion Contract

1.  Given a patient encounter, when a diagnosis is recorded, then it is linked to the patient and treatment plan.
2.  Given a diagnosis entry error, when saved, then the system prompts for correction.
3.  Trust: All diagnosis entries are logged with user ID and timestamp.

### Production Gap Analysis

Production also needs tooth/oral-region linkage where relevant, finding linkage, explanatory text, diagnosis status and amendment/history preservation.

### Production Linkage

- ALV-013-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-014 — Manage procedure/fee catalog with codes, descriptions, and fees

**Type:** Original course story  
**Requirement:** REQ-011  
**Original user story:** As a billing manager, I want to manage a procedure/fee catalog with codes, descriptions, and fees, so that I can ensure accurate billing.

### Immutable Course Completion Contract

1.  Given a new procedure, when added to the catalog, then it includes a code, description, and fee.
2.  Given an incorrect procedure entry, when saved, then the system prompts for correction.
3.  Trust: All catalog changes are logged with user ID and timestamp.

### Production Gap Analysis

Production needs category, tooth/surface applicability, active/effective status, version-safe fee changes and CDT-ready structure. Because the portal gates STORY-014 after STORY-015 while treatment planning should consume the catalog, production builds the catalog earlier in ALV-N005, then later verifies this course story against that implementation.

### Production Linkage

- ALV-N005

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-015 — Support treatment planning with diagnosis linkage, proposed procedures, and fee estimates

**Type:** Original course story  
**Requirement:** REQ-012  
**Original user story:** As a dentist, I want to create treatment plans with diagnosis linkage, proposed procedures, and fee estimates, so that I can provide patients with comprehensive care plans.

### Immutable Course Completion Contract

1.  Given a patient diagnosis, when creating a treatment plan, then it links to proposed procedures and fee estimates.
2.  Given an error in treatment plan entry, when saved, then the system prompts for correction.
3.  Trust: All treatment plan entries are logged with user ID and timestamp.

### Production Gap Analysis

Production needs tooth/surface, phases/visits, full fees and patient estimates, accept/decline, scheduling/completion states and revision history with a patient-friendly presentation.

### Production Linkage

- ALV-015-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-016 — Support prescriptions with medication, dosage, and allergy checks

**Type:** Original course story  
**Requirement:** REQ-016  
**Original user story:** As a clinician, I want to prescribe medications with dosage and allergy checks, so that I can ensure patient safety.

### Immutable Course Completion Contract

1.  Given a patient record, when prescribing medication, then the system checks for allergies and dosage accuracy.
2.  Given an incorrect prescription entry, when saved, then the system prompts for correction.
3.  Trust: All prescription entries are logged with user ID and timestamp.

### Production Gap Analysis

Production needs instructions, quantity, refills, prescriber, status, printable output, current-medication awareness and a broader clinical-alert model including significant conditions, pregnancy/relevant flags, anticoagulants, adverse reactions, custom alerts and clearance needs.

### Production Linkage

- ALV-016-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-017 — Support duplicate detection and controlled merge of patient records

**Type:** Original course story  
**Requirement:** REQ-018  
**Original user story:** As a system administrator, I want to detect and merge duplicate patient records, so that I can maintain accurate patient data.

### Immutable Course Completion Contract

1.  Given duplicate patient records, when detected, then the system suggests a controlled merge.
2.  Given a merge conflict, when resolved, then the system preserves linked data.
3.  Trust: All merge actions are logged with user ID and timestamp.

### Production Gap Analysis

Production needs explicit human review, no silent auto-merge, side-by-side conflict resolution, preservation of every linked domain, source/target traceability and an unmerge/recovery strategy where practical.

### Production Linkage

- ALV-017-C01

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

## STORY-018 — Provide data portability with patient export and import capabilities

**Type:** Original course story  
**Requirement:** REQ-019  
**Original user story:** As a data manager, I want to export and import patient data, so that I can ensure data portability and compliance.

### Immutable Course Completion Contract

1.  Given a patient record, when exported, then the system provides a portable data format.
2.  Given an incorrect import file, when processed, then the system rejects it and prompts for correction.
3.  Trust: All export and import actions are logged with user ID and timestamp.

### Production Gap Analysis

Production needs human-readable patient export, structured/bulk export, generic CSV/tabular imports for patients/appointments/balances, preview/dry-run, validation/error reports and safe partial-failure handling.

### Production Linkage

- ALV-018-C01
- ALV-018-C02

### Original Course Prompt Handling

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; this master plan adds production work around it rather than rewriting it.

# Part II — Production Companion and New Stories

## Standard Production Prompt Rules

Every `ALV-*` coding-agent prompt below inherits these rules:

1.  **Read execution state before editing.** Read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and dependency handoffs when they exist, then inspect current code/tests. Current code/tests are authoritative when they disagree with stale packaged/handoff prose.
2.  **Inspect the parent implementation.** If the story has a parent `STORY-xxx`, identify what that course implementation actually built before changing it. Record this later as **Parent baseline observed**.
3.  **Preserve course truth.** Run the parent's exact course acceptance behavior before and after companion work. Never remove, weaken, or fake those behaviors.
4.  **Protect the course Command Center.** Do not replace or repurpose the root STORY-000 `index.html`/Command Center while building the real product UI.
5.  **UI and function together.** Do not finish with backend-only behavior when a user operates the feature. Build the visible workflow using the shared design system.
6.  **Authorization is server-side.** UI visibility is convenience; service/API enforcement is the security boundary.
7.  **Use shared primitives, own domain semantics.** Reuse audit/concurrency/finalization infrastructure, but let the domain story define its own finalized/amended/reversed lifecycle.
8.  **No destructive history.** Finalized clinical and authoritative financial records are corrected by amendment/addendum/void/reversal/inactivation as appropriate, never silent overwrite/delete.
9.  **Transactions and idempotency.** Multi-record consequential operations must be atomic where the domain requires it, safely recoverable otherwise, and protected against duplicate submission.
10. **No invented integrations, clinical facts, or unsupported medical decision support.** Surface uncertainty and missing data. Do not claim clinical dose appropriateness, diagnosis, insurance benefit adjudication, or other decision support unless the required authoritative source/validation exists.
11. **Respect foundational data rules.** Use documented time/timezone, money, identity-separation, offline, PHI-safe logging, document-integrity, database-topology, cryptography, audit-coupling, measurement-event, and background-work conventions.
12. **Extend the integrated patient workspace.** Patient-facing/patient-context features should add to the shared patient workspace/header/navigation/status model rather than invent a parallel patient shell.
13. **Own domain configuration where it belongs.** Add configuration such as note templates, document categories, recall defaults or payment methods in the domain story that consumes it rather than growing one speculative global settings story.
14. **Instrument measurable outcomes minimally.** When a story affects an agreed success metric, use the shared privacy-safe/versioned measurement-event convention.
15. **Tests are part of the story.** Include happy path, material failure paths, authorization where applicable, data-integrity/concurrency where applicable, and regression tests for the parent course contract.
16. **Demonstrate truthful UI states.** Include loading/empty/error/permission/offline-or-disconnected states where the workflow can encounter them; never show invented health/status/clinical/financial facts.
17. **Implementation stop condition.** Implementation is ready for packaging only when production acceptance criteria pass, required tests pass, the visible workflow is demonstrated where applicable, and no parent course behavior regressed.
18. **Attempt evidence is mandatory.** Create `.alveara/handoffs/<ALV-STORY-ID>/<attempt>.md`, update `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and affected quality-gate evidence; preserve prior attempts.
19. **Implementation + evidence commits.** Commit implementation first and capture its SHA; commit attempt handoff/status/build-state evidence separately. Do not require that committed evidence to contain its own evidence-commit SHA.
20. **Attempt ZIP + review closure are mandatory.** Follow `.alveara/HANDOFF_SCHEMA.md`, generate and validate `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip`, leave the story `AWAITING_REVIEW`, and stop. After reviewer approval, record the review and make the closure commit that moves the repository status to `COMPLETE` before another story begins.

## ALV-N001 — Alveara Application Shell and Design Foundation

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

## ALV-N002 — Core Architecture, Local Deployment, Data Invariants, and Durable Background Work

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

## ALV-001-C01 — Complete Authentication Security, MFA, Recovery, Session Controls, and Authorization Administration

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

## ALV-N009 — Authorization-Aware Navigation, Session UX, and Identity Context

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

## ALV-002-C01 — Shared Audit, Concurrency, and Record-Lifecycle Primitives

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

## ALV-N003 — Practice, Staff, Provider, Operatory, and Scheduling Configuration

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

## ALV-N004 — Encrypted Full-State Backup, Verification, Restore, and Recovery Operations

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

## ALV-003-C01 — Complete Patient Identity, Household, Guarantor, and Registration Workspace

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

## ALV-N010 — Versioned Forms, Consents, and E-Signature Foundation

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

## ALV-004-C01 — Complete Production Scheduler and Conflict-Aware Calendar UX

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

## ALV-011-C01 — Live Patient Flow Board and Complete Visit-State Workflow

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

## ALV-005-C01 — Complete Clinical Documentation, Templates, Signing, and Amendments

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

## ALV-N011 — Patient Safety Alerts, Medical Risk Context, and Clearance Tracking

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

## ALV-006-C01 — Full Odontogram, Mixed Dentition, Conditions, Surfaces, Lifecycle, and Longitudinal History

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

## ALV-012-C01 — Complete Six-Site Periodontal Charting and Longitudinal Comparison

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

## ALV-013-C01 — Structured Diagnosis Lifecycle, Coding Provenance, Context Linkage, and Amendments

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

## ALV-N005 — Production Procedure and Fee Catalog Foundation

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

## ALV-015-C01 — Complete Treatment Planning, Acceptance, Phasing, and Revision History

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

## ALV-007-C01 — Financial Ledger, Charges, Payments, Adjustments, and Guarantor Foundation

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

## ALV-008-C01 — Atomic Procedure Completion, Clinical History, Odontogram, and Charge Generation

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

## ALV-007-C02 — Statements, Receipts, Refunds, Financial Corrections, and Account UX Hardening

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

## ALV-016-C01 — Complete Prescription Workflow Using the Shared Clinical Safety Framework

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

## ALV-010-C01 — Complete Document and Imaging Imports, Metadata, Storage Integrity, and Advanced Form Management

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

## ALV-009-C01 — Recall, Unscheduled Treatment, Work Queues, and Follow-Up Task Operations

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

## ALV-009-C02 — Referral and Dental Laboratory Case Tracking

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

## ALV-017-C01 — Safe Duplicate Review, Conflict Resolution, Merge Preservation, and Recovery

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

## ALV-018-C01 — Versioned Data Export and Portable Record Package

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

## ALV-018-C02 — Validated Bulk Import and Migration Workbench

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

## ALV-N006 — Operational, Clinical, Financial Reporting, and Outcome Measurement

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

## ALV-N007 — AI-Assisted Clinical Documentation and Review

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

## ALV-N012 — AI-Assisted Authorized Search, Summaries, Explanations, and Administrative Follow-Up

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

## ALV-N013 — Windows Installation, Upgrade, Schema Migration, and Rollback

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

## ALV-N014 — Security and Privacy Engineering Readiness

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

## ALV-N008 — Production-Candidate Gate: End-to-End Recovery, Accessibility, Performance, and Release Verification

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

# Part III — Future Roadmap Stories

These are **roadmap definitions, not execution-ready prompts**. They intentionally do not carry engineering-handoff boilerplate. Before executing any `ALV-F*` item, promote it into a new execution-ready prompt: review `.alveara/BUILD_STATE.md`, relevant handoffs, current external standards/vendor constraints, define dependencies/acceptance/tests/security/failure paths, then add the normal `.alveara` handoff protocol. Do not execute a generic future brief merely because it appears below.

These stories are intentionally outside the first production release. They preserve the long-term product direction without forcing speculative integration frameworks into the core build. Each must reuse the modular boundaries created earlier.

## ALV-F001 — True Multi-Location and Multi-Practice Operations

**Type:** Future roadmap  
**Purpose:** Extend the **domain/operational model** into independent locations, cross-location scheduling/reporting, provider sharing, data/permission partitioning and tenant-aware business rules. This story owns multi-location/multi-practice semantics, not cloud hosting/client deployment.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F002 — Dental Insurance, Benefits, Estimates, Claims, and Clearinghouse Integration

**Type:** Future roadmap  
**Purpose:** Add plan/benefit data, eligibility, estimates, claim lifecycle, attachments, remittance and payer/clearinghouse integrations.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F003 — Patient Portal and Communications

**Type:** Future roadmap  
**Purpose:** Add secure patient access for forms, balances, treatment plans, appointments, messages and configurable email/SMS communications.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F004 — Direct Dental Imaging and DICOM/Device Integration

**Type:** Future roadmap  
**Purpose:** Demonstrate real-time image acquisition from a supported device, safe patient/tooth linkage, failure recovery and replaceable vendor adapters.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F005 — Electronic Prescribing and Pharmacy Connectivity

**Type:** Future roadmap  
**Purpose:** Add eRx workflows behind the prescription boundary with provider identity, transmission status and external-service failure handling.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F006 — Integrated and Online Payments

**Type:** Future roadmap  
**Purpose:** Add processor/terminal adapters, tokenized payment methods, idempotent transactions, reconciliation and online payment links.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F007 — Inventory and Supply Management

**Type:** Future roadmap  
**Purpose:** Track stock, reorder levels, lots/expiry where needed and procedure-linked consumption without coupling inventory to clinical core.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F008 — FHIR/HL7 and External Clinical Interoperability

**Type:** Future roadmap  
**Purpose:** Expose/import approved clinical data through standards-based adapters with mapping, provenance and failure queues.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F009 — Cloud SaaS, Desktop/Mobile Clients, Enterprise Identity, and Off-Site Operations

**Type:** Future roadmap  
**Purpose:** Evolve **hosting and client deployment** to cloud/SaaS, desktop/mobile clients, enterprise SSO and off-site operations after ALV-F001 has defined any required multi-location/multi-practice domain/tenant semantics.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F010 — Advanced AI, Imaging Analysis, Predictive Analytics, and Benchmarking

**Type:** Future roadmap  
**Purpose:** Add higher-risk AI only after governance, validation and human-control requirements are defined; retain explicit provenance and non-autonomous clinical decision boundaries.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F011 — Specialty Workflow Expansion

**Type:** Future roadmap  
**Purpose:** Add specialty-specific workflows for periodontics, endodontics, oral surgery, orthodontics, pediatric dentistry, prosthodontics and implant care without fragmenting the shared patient, chart, diagnosis, procedure and billing model.

### Roadmap Implementation Brief — expand before execution

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

## ALV-F012 — Accounting and Business-System Integration

**Type:** Future roadmap  
**Purpose:** Integrate approved financial/operational data with accounting or business systems without making the external system the authoritative clinical ledger.

### Roadmap Implementation Brief — expand before execution

**Story:** ALV-F012 — Accounting and Business-System Integration

This is a future Alveara roadmap story. Do not execute this generic brief directly; first expand it against the current build state and the specific target system/API.

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

# Part IV — Production Gates and Global Definition of Production Complete

Before declaring any gate complete, review `.alveara/BUILD_STATE.md` plus all handoffs created since the prior gate. A planned story is not automatically required merely because it appears in this document: if earlier work safely absorbed it, document that decision and revise the plan instead of duplicating implementation.

The real course portal remains limited to its original course-generated stories unless the product owner deliberately changes that policy for a concrete course benefit.

## Gate Evidence Rules

Before any gate is evaluated, maintain `.alveara/QUALITY_GATES.md` with the measurable criteria relevant to that gate. The file must identify the metric/check, test method/tool, representative dataset or environment, and pass/block threshold or rubric.

- Accessibility: define the critical workflows and target/rubric before the release check; do not retroactively lower it to pass.
- Performance: define measurable response/throughput/resource thresholds and representative data volumes before final measurement.
- Security severity: define the scanner/rubric and blocking severity policy before reviewing release findings.
- Recovery: define what persistent asset classes and representative records/files must survive each restore drill.
- Data reconciliation: define expected source/target counts/totals and tolerances before import/export/financial checks.
- If a threshold legitimately changes, record the reason and date; never silently rewrite a failed gate into a pass.

## Gate A — Foundation Ready

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

## Gate B — Patient, Scheduling, and Visit Entry Ready

Required evidence:

- Registration/family/guarantor workflow is production-usable.
- Versioned forms/consents/e-signature foundation works and check-in surfaces required-form readiness.
- Patient registration has established one persistent patient-context workspace that later modules extend without stale cross-patient state.
- Scheduler enforces provider/operatory/blocked-time rules and supports reschedule/cancel/no-show.
- Patient flow/status board works through the visit lifecycle states implemented so far.
- Critical front-desk workflows are keyboard/responsive and preserve course contracts.
- No known high-severity identity/scheduling/history defect remains.

## Gate C — Clinical Core Ready

Required evidence:

- Structured history, allergies, medications, vitals and encounter notes work with safe signing/amendment.
- Safety alerts/medical clearance are visible before planning/completion/prescribing.
- Odontogram supports primary/permanent/mixed dentition, surfaces, lifecycle and longitudinal history.
- Perio supports explicit six-site full-mouth charting and comparison.
- Structured diagnosis/provenance/context linkage works.
- Procedure/fee catalog precedes and feeds treatment planning.
- Treatment plans preserve revisions, fees/estimates, acceptance/decline and phasing.
- No finalized clinical history can be silently overwritten.

## Gate D — Treatment Completion and Financial Core Ready

Required evidence:

- Financial ledger/charge/payment foundation reconciles from preserved transactions.
- One or many planned procedures can complete atomically with exact source linkage, odontogram effects and one billable charge per billable completed procedure.
- Statement/receipt/refund/reversal/account UX hardening passes.
- Prescription workflow consumes the shared safety context and does not claim unsupported medical dose decision support.
- Financial and completed-procedure histories survive duplicate submissions, corrections and stale edits without destructive overwrite.

## Gate E — Operations, Documents, and Data Ready

Required evidence:

- Document/image imports are safe, integrity-checked, backed up and recoverable.
- Follow-up/recall work queues and due internal reminders operate through durable background work and survive restart without duplication.
- Referral/lab tracking works with document links.
- Duplicate detection/merge preserves linked data with recovery strategy as implemented.
- Versioned export package identifies included/excluded domains and preserves representative file integrity.
- Import/migration dry-runs first, respects duplicate/scheduling/ledger rules, and reports errors without silent loss.
- Reporting reconciles to source records.
- Baseline/outcome metrics never claim improvement without valid comparable data.

## Gate F — Production Candidate

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

## Global Definition of Production Complete

Alveara Dental v1 is not considered production-complete until:

- Required course stories are truthfully verified by the portal.
- Required first-production `ALV-N*` and `ALV-*-C*` stories are complete or deliberately deferred by the product owner with documented impact; optional AI stories may be deferred without weakening the safe core.
- Gates A through F pass with evidence.
- No known high-severity data-integrity, security, backup/restore, installation/upgrade, privacy-exposure, or critical workflow defect remains open.
- Financial history can be reconstructed from preserved authoritative transactions.
- Finalized clinical records cannot be silently overwritten.
- Duplicate merge/import/export do not silently lose linked data.
- Backup recovery includes every persistent asset class actually used by the release.
- The product does **not** claim legal/regulatory certification solely because these engineering controls exist; external assessment remains separate.

# Part V — Architecture and Product Principles to Keep Visible

- **Prepare the foundation, not the feature.** Preserve extension points for known future needs without premature microservices, brokers or plugin frameworks.
- **Protect course/product coexistence.** The root course Command Center is not the dental application and must not be silently replaced.
- **Read build truth before coding.** `BUILD_STATE`, dependency handoffs, then current code/tests; code/tests win if prose is stale.
- **Time and money are architecture.** Practice-local scheduling/timezone semantics and authoritative decimal/integer financial arithmetic are fixed early and tested.
- **Offline means no public-internet dependency, not imaginary disconnected editing.** A lost local server connection is a visible failure unless a later story explicitly implements synchronized offline edits.
- **Background work is durable but simple.** Persist scheduled work/recovery state without introducing distributed infrastructure the first release does not need.
- **Clinical safety precedes prescribing.** Known risks/clearances are patient-context capabilities, not prescription-only data.
- **Do not overclaim medication intelligence.** Structural dose-entry validation is not the same as validated clinical dose appropriateness/interaction decision support.
- **Signed means version-bound.** A signed form/consent preserves the exact content/version/data shown at signing.
- **Country-neutral clinical core, U.S.-first workflows.**
- **One practice / one active location initially, location-aware model from day one.**
- **Local Windows server + LAN responsive web client first; no internet required for core workflows.**
- **Client-independent domain/application/API layer for future desktop/mobile/cloud.**
- **Human control for consequential clinical, financial, security and irreversible actions.**
- **Never silently guess.** Preserve source/provenance and surface uncertainty.
- **No silent last-write-wins.**
- **No silent destructive correction of finalized or authoritative history.**
- **UI is part of functionality.** A capability is not complete merely because a database row can be created.
- **Implementation evidence feeds planning.** Every production story ends with a durable `.alveara` handoff, and the next story is reviewed against that evidence before execution.
- **One patient workspace, many modules.** Registration establishes patient context; later clinical, treatment, document, billing and task modules extend it instead of creating parallel patient shells.
- **Measure from the beginning, report later.** Minimal versioned operational events are emitted by the stories that create the behavior; reporting later analyzes them without becoming the source of truth.
- **Server owns the database.** LAN clients use application/API boundaries; no direct network-share database access.
- **Use proven cryptography.** Never build custom crypto merely because the product must encrypt backups or sensitive local data.
- **Portability is versioned and explainable.** Export says exactly what it contains; import dry-runs and obeys the same identity/scheduling/ledger rules as normal entry.
