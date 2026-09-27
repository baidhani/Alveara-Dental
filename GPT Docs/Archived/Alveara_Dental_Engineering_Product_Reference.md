# Alveara Dental — Engineering & Product Reference

**REFERENCE ONLY — DO NOT USE THIS DOCUMENT TO DETERMINE WHAT TO EXECUTE NEXT.**

The Execution Plan is the sole user-facing execution sequence. This document contains the supporting engineering/product material split out of Master Production Story Plan v3.3 so the execution document stays linear and usable.

## Source Baseline: Master Production Story Plan v3.3

**Status:** Supporting engineering/product reference split from Master Production Story Plan v3.3. It does not determine execution order; the Alveara Dental Execution Plan does.

## 1. Purpose

The course backlog is a **completion scaffold**, not the full production specification. This reference preserves the course completion contracts and the engineering/product rules required to turn the generated project into a production-quality dental EHR/PMS.

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

This was a hardening release of the source master plan, not a new product direction.

### v3.2 Execution-Control Synchronization Summary

Historically, v3.2 was not a product redesign. It synchronized the source Master Plan with the then-current execution-control design so a coding agent could not finish an ALV story using the older fragmented handoff process.

- Adds `.alveara/EXECUTION_STATUS.json` as the centralized internal production-story status ledger.
- Adds `.alveara/HANDOFF_SCHEMA.md` as the durable repository contract for handoff-package structure.
- Makes a validated `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>.zip` mandatory after every non-course story.
- Requires each coding agent to finish at `AWAITING_REVIEW`; it may not self-certify `COMPLETE`.
- Keeps `.alveara/handoffs/<ALV-ID>.md` and `.alveara/BUILD_STATE.md` as repository evidence/current truth, while packaging snapshots of the relevant evidence into the ZIP for review.
- Requires acceptance-by-acceptance evidence, exact test results, parent-course regression evidence for companions, demo evidence where applicable, Git state, changed-file summary, and next-story impact in the package.
- Keeps the ZIP compact and free of secrets/real PHI; the full repository is not copied into every package by default.
- Preserves code/tests as the ultimate implementation truth if packaged prose is stale.
- The reviewing ChatGPT returns `APPROVED → COMPLETE`, `CHANGES_REQUIRED`, `BLOCKED`, or `REOPENED`; only approval permits the next ALV story to begin.

In the current two-document split, the Execution Plan controls **when** work runs and contains the executable story prompts; this Engineering & Product Reference preserves cross-cutting engineering/product rules and immutable course contracts; `HANDOFF_SCHEMA.md` controls **how** ALV results are packaged; `EXECUTION_STATUS.json` records **where production execution stands**.

### v3.3 Source Execution-Control Hardening Summary

The v3.3 source revision hardened execution machinery, but a later cold-start usability audit found that its packaging was not sufficiently direct for real execution. The current two-document split supersedes that packaging while preserving the approved product/story content. v3.3 introduced these execution-control changes:

- historically paired the source master plan with its execution-control companion and Handoff Schema v2;
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

The portal is the **course compliance system**. The Execution Plan plus this Engineering & Product Reference form the **product engineering system**.

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
9.  Do not add the companion to the real course portal simply to make it visible there; execute it from the Execution Plan/repository workflow.
10. Do not begin the next production story automatically after a companion finishes. The companion must generate its validated handoff ZIP, enter `AWAITING_REVIEW`, and stop. Review that package first and revise the next prompt if implementation reality changed.

### Portal Done Means precedence

The sampled portal pages (`STORY-001`, `STORY-002`, `STORY-004`) showed that **Done Means is identical to the prompt's Acceptance / stop condition**, and STORY-000 explicitly states that the lines are text-matched. This plan therefore preserves the Acceptance text as the course completion contract. If any future portal page ever differs from the stored prompt, the **portal's current Done Means text wins** and must be copied word for word before implementation continues.

## 4. Engineering Execution Cycle, Status Tracking, and Handoff Package Protocol

The production backlog is a living engineering plan. Execution order is governed by Execution Plan plus real course unlock state.

### STEP 0 — bootstrap before STORY-000

Before any story work, initialize and commit `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, `.alveara/BUILD_STATE.md`, `.alveara/QUALITY_GATES.md`, `.alveara/handoffs/`, and `.alveara/reviews/` exactly as specified by Execution Plan. The status ledger is prepopulated with all 53 first-release items and the execution-control schema and Handoff Schema `2`.

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

Execution Plan inserts mandatory Gate A–F stops after atomic items 10, 17, 30, 37, 48 and 53 respectively. A failed gate blocks progression into the next phase until repaired/revalidated.

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

**Optional assistive AI and release engineering** 32. Decide the AI branch at `ALV-N007`: either execute/review-close `ALV-N007` and then optionally execute/review-close `ALV-N012`, or defer **both** AI stories and jump to release engineering. `ALV-N012` must never execute when `ALV-N007` is deferred because it depends on `ALV-N007`. If `ALV-N007` is complete, `ALV-N012` may still be individually deferred with documented reason/impact. 33. `ALV-N013` — Windows install/upgrade/schema-migration/rollback 34. `ALV-N014` — security/privacy engineering-readiness review 35. `ALV-N008` — final production-candidate gate

**STOP — Gate F must pass before declaring the first-production baseline.**

At every arrow or production-story boundary, review the latest `.alveara` handoff before continuing. This sequence can be interleaved with portal unlocks as needed; never falsely mark a course story complete to preserve engineering order.

# Original Course Stories, Immutable Completion Contracts, and Gap Analysis

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

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

Execute the portal-generated prompt for this story **unchanged**. The exact completion contract above is the portion that must remain word-for-word compatible with `.colaberry/progress.json`. The full portal prompt remains the authoritative course execution brief; the Execution Plan adds production work around it rather than rewriting it.

# Production Engineering Rules

## Gate Evidence Rules

Before any gate is evaluated, maintain `.alveara/QUALITY_GATES.md` with the measurable criteria relevant to that gate. The file must identify the metric/check, test method/tool, representative dataset or environment, and pass/block threshold or rubric.

- Accessibility: define the critical workflows and target/rubric before the release check; do not retroactively lower it to pass.
- Performance: define measurable response/throughput/resource thresholds and representative data volumes before final measurement.
- Security severity: define the scanner/rubric and blocking severity policy before reviewing release findings.
- Recovery: define what persistent asset classes and representative records/files must survive each restore drill.
- Data reconciliation: define expected source/target counts/totals and tolerances before import/export/financial checks.
- If a threshold legitimately changes, record the reason and date; never silently rewrite a failed gate into a pass.

## Standard Production Prompt Rules

Every `ALV-*` coding-agent prompt in the Execution Plan inherits these rules. STEP 0 persists them into `.alveara/HANDOFF_SCHEMA.md` so they remain available during repository execution:

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

## Global Definition of Production Complete

Alveara Dental v1 is not considered production-complete until:

- Required course stories are truthfully verified by the portal.
- Every required first-production `ALV-N*` and `ALV-*-C*` story is complete. Only stories explicitly marked optional by the Execution Plan may be deliberately deferred with documented reason/impact; the optional AI branch may be deferred without weakening the safe core.
- Gates A through F pass with evidence.
- No known high-severity data-integrity, security, backup/restore, installation/upgrade, privacy-exposure, or critical workflow defect remains open.
- Financial history can be reconstructed from preserved authoritative transactions.
- Finalized clinical records cannot be silently overwritten.
- Duplicate merge/import/export do not silently lose linked data.
- Backup recovery includes every persistent asset class actually used by the release.
- The product does **not** claim legal/regulatory certification solely because these engineering controls exist; external assessment remains separate.

# Architecture and Product Principles

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
