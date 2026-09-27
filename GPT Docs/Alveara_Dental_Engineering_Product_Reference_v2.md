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

## 4. Execution Control, Tracking, Handoff, Review, and Package Specification

**This section contains the operating content formerly held in `Alveara_Dental_Execution_Index_v1.2.md`. The standalone Index is no longer required. Its applicable rules are merged here rather than discarded.**

**Authority split:** the **Execution Plan** tells you what to execute and contains the complete story prompts in execution order. This **Engineering & Product Reference** defines the supporting engineering rules, tracking model, handoff/review lifecycle, ZIP construction, completion contract, revalidation behavior, and operating procedures.

### 1. The Four Story Classes

#### Course stories — `STORY-000` through `STORY-018`

These are owned and verified by the course portal. Their completion authority is the course itself.

A course story is not complete merely because the coding agent says it is complete. It is complete only when:

1.  its exact portal Done Means / acceptance lines are genuinely satisfied;
2.  `.colaberry/progress.json` is updated truthfully;
3.  required course enrichment is written;
4.  the commit/push requirements are satisfied; and
5.  the portal confirms the criteria.

Our own tracker may **mirror** that status for sequencing, but it never overrides the course portal.

#### Companion stories — `ALV-xxx-Cxx`

A companion starts from an original course implementation and hardens it into the production behavior we actually want.

Every companion has a course parent. It must preserve that parent's course completion contract permanently.

#### New production stories — `ALV-Nxxx`

These cover capabilities the course generator omitted, compressed, or could not sequence safely, such as the application shell, architecture, backup/recovery, safety framework, forms/e-signature foundation, reporting, deployment and security-readiness work.

#### Future roadmap stories — `ALV-Fxxx`

These preserve the long-term vision but are **not execution-ready**. They do not enter first-release tracking until deliberately promoted into a full execution prompt.

------------------------------------------------------------------------

### 2. Yes — We Need Our Own Completion and Tracking System

The Execution Plan gives every companion/new production story:

- story-specific **Acceptance / stop condition** items;
- required tests;
- UI/UX deliverables where applicable;
- security/audit/data-integrity requirements;
- failure paths;
- engineering handoff requirements; and
- a stop-and-review rule.

What was missing was a **central production status ledger** comparable to the course portal.

#### Our production tracking file

Create and maintain:

`/.alveara/EXECUTION_STATUS.json`

This is separate from `.colaberry/progress.json`.

The tracking system consists of five complementary records:

1.  **`.alveara/EXECUTION_STATUS.json`** — current status/evidence for every course and ALV story.
2.  **`.alveara/handoffs/<ALV-ID>/<attempt>.md`** — immutable attempt-level engineering handoffs (`R01`, `R02`, ...).
3.  **`.alveara/BUILD_STATE.md`** — concise current truth about what the product actually contains now.
4.  **`.alveara/QUALITY_GATES.md`** — predeclared measurable production-gate thresholds and evidence.
5.  **`Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip`** — the single review package generated for each non-course review attempt.
6.  **`.alveara/reviews/<ALV-ID>/<attempt>.md`** — durable reviewer decision recorded during review closure.

The course portal remains authoritative for course completion. `EXECUTION_STATUS.json` is authoritative for **our internal production execution state**. The ZIP is a portable review snapshot, not a second source of truth: current code/tests remain authoritative if packaged prose ever disagrees with the repository.

------------------------------------------------------------------------

### 3. ALV Story Status Lifecycle

Use only these normal states:

`PLANNED → READY → IN_PROGRESS → AWAITING_REVIEW → COMPLETE`

Exceptional states:

- `BLOCKED` — cannot proceed because a dependency, requirement, credential, architecture decision, or defect blocks truthful completion.
- `CHANGES_REQUIRED` — implementation ran, review found work that must be corrected before completion.
- `DEFERRED` — product owner deliberately postponed the story; impact/reason must be recorded.
- `REOPENED` — a previously complete story later regressed or must be changed.
- `REVALIDATION_REQUIRED` — a completed downstream story may have been affected by a reopened dependency and must be impact-reviewed/regression-verified before its prior completion can be trusted again.

#### Who may declare completion?

The coding agent **does not self-certify `COMPLETE`**.

At the end of an ALV run, the coding agent should move the story to **`AWAITING_REVIEW`** and produce the handoff/evidence.

Then:

1.  you give me the handoff and, when needed, the current project checkpoint;
2.  I review the actual implementation against the Master Plan, dependencies, tests, parent-course contract and next-story impact;
3.  I tell you whether the story is ready for `COMPLETE`, requires `CHANGES_REQUIRED`, or should be `BLOCKED/REOPENED`;
4.  you decide whether to proceed to the next execution item.

That prevents “the agent said done” from becoming our definition of done.

------------------------------------------------------------------------

### 4. The ALV Completion Contract

A companion or new production story is **COMPLETE only when every applicable condition below is true**:

1.  **All story-specific Acceptance / stop condition items pass.**  
    The exact list lives in the Execution Plan under that story.

2.  **All required tests pass.**  
    This includes happy paths and the material failure/security/concurrency/integrity tests required by that prompt.

3.  **Parent course regression passes for companions.**  
    Every original Done Means behavior of the parent `STORY-xxx` must still work. For new production stories, this is `N/A`.

4.  **The visible workflow is demonstrated when user-facing.**  
    Backend-only implementation is not complete when the story describes a human workflow.

5.  **Security, audit and data-integrity obligations are satisfied.**  
    A story cannot be called complete while knowingly violating its authorization, audit, history, transaction, idempotency, privacy or recovery contract.

6.  **No blocking defect remains inside the story scope.**  
    A known limitation may remain only when it is explicitly out of scope or accepted as non-blocking and recorded in the handoff.

7.  **Implementation commit exists.**

8.  **Attempt handoff exists:** `.alveara/handoffs/<ALV-STORY-ID>/<attempt>.md` and prior attempts are preserved.

9.  **`.alveara/BUILD_STATE.md` is updated** with durable current-state facts.

10. **`.alveara/EXECUTION_STATUS.json` is updated to `AWAITING_REVIEW`** with acceptance/test/demo/regression evidence and commit identifiers.

11. **A complete attempt ZIP is generated and validated.** The coding agent creates `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip` according to Section 7. The user should not collect evidence manually.

12. **Review is completed.** The reviewing ChatGPT inspects the ZIP against the Execution Plan, this Engineering & Product Reference, dependencies, tests, parent contract and next-story impact. Only if the package reveals an uncertainty that cannot be resolved from its evidence should the reviewer request the smallest additional source/project artifact necessary.

13. **Approval is durably closed in the repository.** The coding agent records the review decision under `.alveara/reviews/<ALV-ID>/<attempt>.md`, updates `EXECUTION_STATUS.json` to `COMPLETE`, and makes a small review-closure commit. Only then may the next execution item begin.

Optional first-release AI stories may be deliberately `DEFERRED` without blocking the safe core release, provided the dependency/gate impact is recorded.

------------------------------------------------------------------------

### 5. Canonical `EXECUTION_STATUS.json` Story Record

The repository bootstrap creates all 53 first-release records. A representative ALV record is:

``` json
{
  "schemaVersion": "2",
  "controlSet": {
    "masterPlanVersion": "3.3",
    "executionIndexVersion": "1.2",
    "handoffSchemaVersion": "2"
  },
  "storyId": "ALV-004-C01",
  "storyType": "companion",
  "parentCourseStory": "STORY-004",
  "dependencies": ["STORY-004", "ALV-N003"],
  "status": "AWAITING_REVIEW",
  "attempt": "R01",
  "acceptance": {"total": 7, "passed": 7},
  "tests": {"status": "passed", "summary": "See attempt package"},
  "demo": "verified",
  "parentRegression": "passed",
  "implementationCommit": "<sha>",
  "evidenceCommit": null,
  "handoffPath": ".alveara/handoffs/ALV-004-C01/R01.md",
  "review": {
    "decision": "pending",
    "decisionArtifact": null,
    "approvedAttempt": null,
    "reviewedImplementationCommit": null,
    "reviewedEvidenceCommit": null
  },
  "blockingIssues": [],
  "qualityGateContribution": ["Gate B"],
  "revalidation": {"required": false, "reason": null},
  "completedAt": null
}
```

At `AWAITING_REVIEW`, `evidenceCommit` may still be `null` in the committed repository snapshot because the evidence commit SHA does not exist until after that commit is created. The generated ZIP's `MANIFEST.json` and `evidence/GIT_STATE.md` record the now-known evidence commit SHA. If review is approved, the later review-closure commit writes that evidence SHA and the review decision into `EXECUTION_STATUS.json`.

A committed file is **never required to contain the SHA of the commit that contains that same file**.

For a course story, use the same ledger with `storyType: "course"`, `externalVerification: "course_portal"`, and `COMPLETE` only after portal confirmation.

For a course story, use the same ledger but set:

- `storyType` to `course`;
- `externalVerification` to `course_portal`;
- `status` to `COMPLETE` only after the portal confirms it.

------------------------------------------------------------------------

### 6. What Happens After Every Course Story

1.  Run the **portal's original prompt unchanged**.
2.  Satisfy the portal Done Means criteria truthfully.
3.  Complete the `.colaberry` progress/enrichment/commit/push process.
4.  Wait for portal confirmation.
5.  Mirror the confirmed course state into `.alveara/EXECUTION_STATUS.json`.
6.  Continue in the Execution Plan only after the course result is mirrored into `EXECUTION_STATUS.json`.
7.  If the next item is an ALV story, verify its dependencies are `COMPLETE`/portal-confirmed as applicable. If a dependency is unavailable, stop and review rather than skipping blindly.

A course story may be portal-complete while its **production capability is still incomplete**. That is exactly why companions exist.

------------------------------------------------------------------------

### 7. Non-Course Story Attempt, Review, and Closure Protocol

Generating the handoff ZIP is part of the Definition of Done, but **the ZIP is an attempt package, not proof of final approval**.

#### 7.1 Normal successful attempt

1.  Set the story to `IN_PROGRESS` and use the next attempt number (`R01` for the first review attempt).

2.  Finish implementation, required tests, exact acceptance verification, visible demonstration where applicable, and parent-course regression for companions.

3.  Create the **implementation commit** and capture its full SHA.

4.  Write/update:

    - `.alveara/handoffs/<ALV-ID>/<attempt>.md`
    - `.alveara/BUILD_STATE.md`
    - `.alveara/EXECUTION_STATUS.json` with `AWAITING_REVIEW`
    - affected `.alveara/QUALITY_GATES.md` evidence.

5.  The committed attempt handoff records the implementation SHA, but **must not claim to know the SHA of the evidence commit that contains it**.

6.  Create the **evidence commit** and capture its full SHA.

7.  Generate one user-facing ZIP:

    `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip`

    Its generated `MANIFEST.json` and `evidence/GIT_STATE.md` record both the implementation SHA and the now-known evidence SHA.

8.  Reopen/list the ZIP and validate every mandatory member.

9.  Return the ZIP path, both SHAs, attempt ID, test summary, regression result and state `AWAITING_REVIEW`.

10. **Stop. Do not begin the next story.**

11. The user uploads only that ZIP to the reviewing ChatGPT.

#### 7.2 Review result

The reviewer returns one of:

- `APPROVED`
- `CHANGES_REQUIRED`
- `BLOCKED`
- `REOPENED`

The reviewer should also produce a compact decision artifact named:

`Alveara_Review_<ALV-ID>_<attempt>.md`

containing the reviewed implementation/evidence SHAs, decision, reasons, required corrections if any, next-story impact, and whether any downstream story/gate needs revalidation.

#### 7.3 Approval closure

When the result is `APPROVED`, give the review decision artifact to the coding/repository agent. That agent performs **closure only**:

1.  Verify the decision artifact refers to the same story, attempt, implementation SHA and evidence SHA.
2.  Store it as `.alveara/reviews/<ALV-ID>/<attempt>.md`.
3.  Update `EXECUTION_STATUS.json` from `AWAITING_REVIEW` to `COMPLETE`, recording the approved attempt, implementation SHA, evidence SHA, completion timestamp and review decision path.
4.  Update `BUILD_STATE.md` only if approval changes a durable current-state fact.
5.  Commit with `ALV-ID: record review approval`.
6.  Return the review-closure commit SHA.
7.  Only now may the next Execution Plan item begin.

The closure commit SHA is **not required inside the files committed by that same closure commit**; Git history itself is the authority for that commit identity.

#### 7.4 Changes required

If review returns `CHANGES_REQUIRED`:

1.  Preserve the rejected attempt and ZIP.
2.  Record the decision under `.alveara/reviews/<ALV-ID>/<attempt>.md`.
3.  Set the story to `CHANGES_REQUIRED`.
4.  Correct the same story; do not begin another story.
5.  Increment the attempt (`R01 → R02`).
6.  Produce a new implementation/evidence pair and a new ZIP.
7.  The next review evaluates the new attempt while preserving prior history.

Never overwrite an earlier attempt handoff or review decision.

#### 7.5 Blocked story

If a material blocker is discovered after work/analysis begins, set `BLOCKED`. Whenever technically possible, produce:

`Alveara_Handoff_<ALV-ID>_<implementation-short-sha-or-NOCOMMIT>_<attempt>_BLOCKED.zip`

It contains the normal evidence that exists plus a prominent blocker description, work completed, work not completed, safety/data implications, and the exact decision/input required to resume. A blocker that prevents packaging must be reported explicitly; never fabricate evidence merely to satisfy the package shape.

#### 7.6 Mandatory attempt ZIP

``` text
Alveara_Handoff_<ALV-ID>_<sha>_<attempt>.zip
├── HANDOFF.md
├── MANIFEST.json
├── BUILD_STATE.md
├── EXECUTION_STATUS.json
├── QUALITY_GATES.md
├── evidence/
│   ├── TEST_RESULTS.md
│   ├── ACCEPTANCE_EVIDENCE.md
│   ├── PARENT_REGRESSION.md
│   ├── DEMO_EVIDENCE.md
│   ├── CHANGED_FILES.md
│   └── GIT_STATE.md
├── artifacts/                       # only when relevant
│   └── ...
└── review/
    └── NEXT_STORY_IMPACT.md
```

`HANDOFF.md` records the story, attempt, parent/baseline, implementation SHA, scope, architecture/schema/API/UI/security/config/measurement changes, decisions, deviations, limitations/debt, unresolved questions and next-story impact. It does **not** need the evidence commit SHA.

`MANIFEST.json` is generated **after** the evidence commit and records control-set versions, story/attempt, parent, implementation SHA, evidence SHA, timestamp/timezone, package inventory, acceptance count, exact test summary, regression/demo result, blockers, gates touched, `containsSecrets:false`, and `containsRealPHI:false`.

`TEST_RESULTS.md` records exact commands/suites and pass/fail/skip counts. `ACCEPTANCE_EVIDENCE.md` maps every exact acceptance item to proving evidence. `PARENT_REGRESSION.md` maps every parent Done Means for companions or states justified N/A. `DEMO_EVIDENCE.md` records the visible workflow/states or justified N/A. `CHANGED_FILES.md` groups material changes. `GIT_STATE.md` records branch, implementation SHA, evidence SHA, clean/dirty state and relevant commits. `NEXT_STORY_IMPACT.md` records assumptions, interfaces, migrations, UI extension points, unresolved decisions and prompt changes relevant to the next scheduled item.

#### 7.7 Package safety

Never package secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, real-data backups, dependency caches, or irrelevant build output. Do not copy the entire repository by default. The package is compact review evidence; current code/tests remain implementation truth.

### **The user should normally upload one handoff ZIP for review, then return one review-decision file to the coding agent for closure.**

### 8. Phase Map — What the Product Becomes

| Phase                  | Expected product state at the end                                                                                                                                                                     |
|------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Foundation             | Course Command Center remains intact; real Alveara shell exists; local-server/database/API/time/money/background-work foundations are real.                                                           |
| Security & Foundation  | Authentication, MFA/RBAC, authorization-aware navigation, audit/concurrency primitives, practice/provider/operatories configuration, and recoverable encrypted backups exist.                         |
| Patient & Front Office | A patient can be registered into a persistent patient workspace, complete/sign required forms, be scheduled without conflicts, check in, and move through a live visit-flow board.                    |
| Clinical Core          | The chart supports longitudinal history/notes, safety alerts, mixed-dentition odontogram, six-site perio, structured diagnoses, procedure catalog, and treatment planning.                            |
| Treatment & Finance    | Completed treatment becomes authoritative clinical history/odontogram state and exactly-linked financial charges; billing, statements, corrections, and prescription workflow are safe and traceable. |
| Operations & Data      | Documents/images, recall/tasks, referrals/labs, duplicate merge, versioned export/import migration, reporting, and outcome measurement are production-grade.                                          |
| Optional AI            | AI assistance can draft/summarize/search/explain under authorization while remaining optional, reviewable, source-aware, and non-authoritative.                                                       |
| Release Engineering    | Windows install/upgrade/recovery, security/privacy readiness, and final end-to-end production-candidate gates are verified.                                                                           |

This is intentionally **not backend-first followed by frontend-later**. The application shell starts almost immediately, and each user-facing capability is built as a vertical slice: domain/data/service behavior + security/integrity + UI + tests + demonstration.

------------------------------------------------------------------------

### 9. Atomic First-Release Execution Index

**Important:** This is the engineering order. The course portal remains authoritative for whether a course story is unlocked. Never falsely complete a course story just to preserve this order.

|  \# | Phase                  | ID            | Class          | Capability                                                                                           | Completion authority                                                            | After completion                                  |
|----:|------------------------|---------------|----------------|------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|---------------------------------------------------|
|   1 | Foundation             | `STORY-000`   | Course         | Build your Command Center                                                                            | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-N001`    |
|   2 | Foundation             | `ALV-N001`    | New production | Alveara Application Shell and Design Foundation                                                      | 6 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N002`                   |
|   3 | Foundation             | `ALV-N002`    | New production | Core Architecture, Local Deployment, Data Invariants, and Durable Background Work                    | 9 story acceptance items + ALV Completion Contract                              | Review handoff, then `STORY-001`                  |
|   4 | Security & Foundation  | `STORY-001`   | Course         | Implement secure user authentication and RBAC                                                        | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-001-C01` |
|   5 | Security & Foundation  | `ALV-001-C01` | Companion      | Complete Authentication Security, MFA, Recovery, Session Controls, and Authorization Administration  | 7 story acceptance items + ALV Completion Contract; parent regression STORY-001 | Review handoff, then `ALV-N009`                   |
|   6 | Security & Foundation  | `ALV-N009`    | New production | Authorization-Aware Navigation, Session UX, and Identity Context                                     | 5 story acceptance items + ALV Completion Contract                              | Review handoff, then `STORY-002`                  |
|   7 | Security & Foundation  | `STORY-002`   | Course         | Establish audit logging for critical actions                                                         | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-002-C01` |
|   8 | Security & Foundation  | `ALV-002-C01` | Companion      | Shared Audit, Concurrency, and Record-Lifecycle Primitives                                           | 6 story acceptance items + ALV Completion Contract; parent regression STORY-002 | Review handoff, then `ALV-N003`                   |
|   9 | Security & Foundation  | `ALV-N003`    | New production | Practice, Staff, Provider, Operatory, and Scheduling Configuration                                   | 5 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N004`                   |
|  10 | Security & Foundation  | `ALV-N004`    | New production | Encrypted Full-State Backup, Verification, Restore, and Recovery Operations                          | 7 story acceptance items + ALV Completion Contract                              | Review handoff, then `STORY-003`                  |
|  11 | Patient & Front Office | `STORY-003`   | Course         | Implement patient registration workflow                                                              | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-003-C01` |
|  12 | Patient & Front Office | `ALV-003-C01` | Companion      | Complete Patient Identity, Household, Guarantor, and Registration Workspace                          | 7 story acceptance items + ALV Completion Contract; parent regression STORY-003 | Review handoff, then `ALV-N010`                   |
|  13 | Patient & Front Office | `ALV-N010`    | New production | Versioned Forms, Consents, and E-Signature Foundation                                                | 6 story acceptance items + ALV Completion Contract                              | Review handoff, then `STORY-004`                  |
|  14 | Patient & Front Office | `STORY-004`   | Course         | Enable appointment scheduling with conflict prevention                                               | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-004-C01` |
|  15 | Patient & Front Office | `ALV-004-C01` | Companion      | Complete Production Scheduler and Conflict-Aware Calendar UX                                         | 7 story acceptance items + ALV Completion Contract; parent regression STORY-004 | Review handoff, then `STORY-011`                  |
|  16 | Patient & Front Office | `STORY-011`   | Course         | Track patient flow states from scheduled to completed                                                | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-011-C01` |
|  17 | Patient & Front Office | `ALV-011-C01` | Companion      | Live Patient Flow Board and Complete Visit-State Workflow                                            | 7 story acceptance items + ALV Completion Contract; parent regression STORY-011 | Review handoff, then `STORY-005`                  |
|  18 | Clinical Core          | `STORY-005`   | Course         | Develop clinical documentation module                                                                | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-005-C01` |
|  19 | Clinical Core          | `ALV-005-C01` | Companion      | Complete Clinical Documentation, Templates, Signing, and Amendments                                  | 7 story acceptance items + ALV Completion Contract; parent regression STORY-005 | Review handoff, then `ALV-N011`                   |
|  20 | Clinical Core          | `ALV-N011`    | New production | Patient Safety Alerts, Medical Risk Context, and Clearance Tracking                                  | 7 story acceptance items + ALV Completion Contract                              | Review handoff, then `STORY-006`                  |
|  21 | Clinical Core          | `STORY-006`   | Course         | Implement interactive odontogram                                                                     | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-006-C01` |
|  22 | Clinical Core          | `ALV-006-C01` | Companion      | Full Odontogram, Mixed Dentition, Conditions, Surfaces, Lifecycle, and Longitudinal History          | 6 story acceptance items + ALV Completion Contract; parent regression STORY-006 | Review handoff, then `STORY-012`                  |
|  23 | Clinical Core          | `STORY-012`   | Course         | Support periodontal charting with probing depth, recession, and bleeding                             | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-012-C01` |
|  24 | Clinical Core          | `ALV-012-C01` | Companion      | Complete Six-Site Periodontal Charting and Longitudinal Comparison                                   | 6 story acceptance items + ALV Completion Contract; parent regression STORY-012 | Review handoff, then `STORY-013`                  |
|  25 | Clinical Core          | `STORY-013`   | Course         | Allow structured diagnosis linked to patient, encounter, and treatment plan                          | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-013-C01` |
|  26 | Clinical Core          | `ALV-013-C01` | Companion      | Structured Diagnosis Lifecycle, Coding Provenance, Context Linkage, and Amendments                   | 6 story acceptance items + ALV Completion Contract; parent regression STORY-013 | Review handoff, then `ALV-N005`                   |
|  27 | Clinical Core          | `ALV-N005`    | New production | Production Procedure and Fee Catalog Foundation                                                      | 5 story acceptance items + ALV Completion Contract                              | Review handoff, then `STORY-015`                  |
|  28 | Clinical Core          | `STORY-015`   | Course         | Support treatment planning with diagnosis linkage, proposed procedures, and fee estimates            | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-015-C01` |
|  29 | Clinical Core          | `ALV-015-C01` | Companion      | Complete Treatment Planning, Acceptance, Phasing, and Revision History                               | 6 story acceptance items + ALV Completion Contract; parent regression STORY-015 | Review handoff, then `STORY-014`                  |
|  30 | Clinical Core          | `STORY-014`   | Course         | Manage procedure/fee catalog with codes, descriptions, and fees                                      | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `STORY-007`   |
|  31 | Treatment & Finance    | `STORY-007`   | Course         | Enable billing workflow with financial history preservation                                          | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-007-C01` |
|  32 | Treatment & Finance    | `ALV-007-C01` | Companion      | Financial Ledger, Charges, Payments, Adjustments, and Guarantor Foundation                           | 7 story acceptance items + ALV Completion Contract; parent regression STORY-007 | Review handoff, then `STORY-008`                  |
|  33 | Treatment & Finance    | `STORY-008`   | Course         | Complete treatment plan and update clinical history                                                  | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-008-C01` |
|  34 | Treatment & Finance    | `ALV-008-C01` | Companion      | Atomic Procedure Completion, Clinical History, Odontogram, and Charge Generation                     | 7 story acceptance items + ALV Completion Contract; parent regression STORY-008 | Review handoff, then `ALV-007-C02`                |
|  35 | Treatment & Finance    | `ALV-007-C02` | Companion      | Statements, Receipts, Refunds, Financial Corrections, and Account UX Hardening                       | 6 story acceptance items + ALV Completion Contract; parent regression STORY-007 | Review handoff, then `STORY-016`                  |
|  36 | Treatment & Finance    | `STORY-016`   | Course         | Support prescriptions with medication, dosage, and allergy checks                                    | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-016-C01` |
|  37 | Treatment & Finance    | `ALV-016-C01` | Companion      | Complete Prescription Workflow Using the Shared Clinical Safety Framework                            | 6 story acceptance items + ALV Completion Contract; parent regression STORY-016 | Review handoff, then `STORY-010`                  |
|  38 | Operations & Data      | `STORY-010`   | Course         | Implement document import and categorization                                                         | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-010-C01` |
|  39 | Operations & Data      | `ALV-010-C01` | Companion      | Complete Document and Imaging Imports, Metadata, Storage Integrity, and Advanced Form Management     | 7 story acceptance items + ALV Completion Contract; parent regression STORY-010 | Review handoff, then `STORY-009`                  |
|  40 | Operations & Data      | `STORY-009`   | Course         | Manage follow-up tasks and reminders                                                                 | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-009-C01` |
|  41 | Operations & Data      | `ALV-009-C01` | Companion      | Recall, Unscheduled Treatment, Work Queues, and Follow-Up Task Operations                            | 7 story acceptance items + ALV Completion Contract; parent regression STORY-009 | Review handoff, then `ALV-009-C02`                |
|  42 | Operations & Data      | `ALV-009-C02` | Companion      | Referral and Dental Laboratory Case Tracking                                                         | 4 story acceptance items + ALV Completion Contract; parent regression STORY-009 | Review handoff, then `STORY-017`                  |
|  43 | Operations & Data      | `STORY-017`   | Course         | Support duplicate detection and controlled merge of patient records                                  | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-017-C01` |
|  44 | Operations & Data      | `ALV-017-C01` | Companion      | Safe Duplicate Review, Conflict Resolution, Merge Preservation, and Recovery                         | 6 story acceptance items + ALV Completion Contract; parent regression STORY-017 | Review handoff, then `STORY-018`                  |
|  45 | Operations & Data      | `STORY-018`   | Course         | Provide data portability with patient export and import capabilities                                 | Portal Done Means + course repo verification                                    | Confirm portal, mirror status, then `ALV-018-C01` |
|  46 | Operations & Data      | `ALV-018-C01` | Companion      | Versioned Data Export and Portable Record Package                                                    | 6 story acceptance items + ALV Completion Contract; parent regression STORY-018 | Review handoff, then `ALV-018-C02`                |
|  47 | Operations & Data      | `ALV-018-C02` | Companion      | Validated Bulk Import and Migration Workbench                                                        | 8 story acceptance items + ALV Completion Contract; parent regression STORY-018 | Review handoff, then `ALV-N006`                   |
|  48 | Operations & Data      | `ALV-N006`    | New production | Operational, Clinical, Financial Reporting, and Outcome Measurement                                  | 7 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N007`                   |
|  49 | Optional AI            | `ALV-N007`    | New production | AI-Assisted Clinical Documentation and Review                                                        | 5 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N012`                   |
|  50 | Optional AI            | `ALV-N012`    | New / optional | AI-Assisted Authorized Search, Summaries, Explanations, and Administrative Follow-Up                 | 6 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N013`                   |
|  51 | Release Engineering    | `ALV-N013`    | New production | Windows Installation, Upgrade, Schema Migration, and Rollback                                        | 6 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N014`                   |
|  52 | Release Engineering    | `ALV-N014`    | New production | Security and Privacy Engineering Readiness                                                           | 5 story acceptance items + ALV Completion Contract                              | Review handoff, then `ALV-N008`                   |
|  53 | Release Engineering    | `ALV-N008`    | New production | Production-Candidate Gate: End-to-End Recovery, Accessibility, Performance, and Release Verification | 8 story acceptance items + ALV Completion Contract                              | Release candidate / first-production baseline     |

#### Mandatory Phase Gate Checkpoints

The 53 story items remain unchanged, but daily execution has six **non-story stop points**:

| After item | Gate                                                       | Rule                                                             |
|-----------:|------------------------------------------------------------|------------------------------------------------------------------|
|         10 | **Gate A — Foundation Ready**                              | Do not start patient/front-office expansion until Gate A passes. |
|         17 | **Gate B — Patient, Scheduling, and Visit Entry Ready**    | Do not start Clinical Core until Gate B passes.                  |
|         30 | **Gate C — Clinical Core Ready**                           | Do not start Treatment & Finance until Gate C passes.            |
|         37 | **Gate D — Treatment Completion and Financial Core Ready** | Do not start Operations & Data until Gate D passes.              |
|         48 | **Gate E — Operations, Documents, and Data Ready**         | Resolve Gate E before optional AI/release engineering.           |
|         53 | **Gate F — Production Candidate**                          | Required before first-production baseline is declared.           |

At a gate, run the gate checks defined at that point in the Execution Plan and update `.alveara/QUALITY_GATES.md`. A failed gate stops forward phase progression. Repair/reopen the responsible story or stories, run impact revalidation, then reevaluate the gate. Do not lower a predeclared threshold merely to pass.

Gate evaluation is evidence-producing work. When a gate requires a substantial integrated verification run, package its evidence as `Alveara_Gate_<A-F>_<evidence-short-sha>.zip` so the review can be reproduced without turning the gate into another product story.

#### Optional AI branch

After item 48 / Gate E:

- **Include AI:** execute and review-close `ALV-N007`. Then either execute/review-close `ALV-N012`, or deliberately defer only `ALV-N012` with product-owner reason and release impact.
- **Defer AI entirely:** record **both** `ALV-N007` and `ALV-N012` as `DEFERRED` with product-owner reason and release impact, verify no required core workflow depends on AI, then continue directly to `ALV-N013`.
- `ALV-N012` must never execute when `ALV-N007` is deferred because `ALV-N012` depends on `ALV-N007`.

Deferral is a recorded release decision, not silent skipping.

#### Special sequencing notes

- `ALV-N005` intentionally builds the production procedure/fee catalog foundation **before** `STORY-014`; when STORY-014 becomes available, satisfy its immutable course contract against that already-production-grade implementation.
- `ALV-007-C01` establishes the financial ledger/charge foundation **before** `ALV-008-C01` procedure completion creates billable charges.
- `ALV-007-C02` hardens statements/receipts/refunds/corrections after real procedure-linked charges exist.
- `ALV-018-C01` and `ALV-018-C02` intentionally split export from import/migration because their risk profiles are different.
- `ALV-N007` and `ALV-N012` are optional first-release AI enhancements. Deferring them does not excuse any weakness in the core non-AI product.

------------------------------------------------------------------------

### 10. Completion Conditions Are Story-Specific, Not One Generic Checkbox

The `Completion authority` column above tells you how each execution item is judged.

For every ALV story, the **exact behavioral conditions are not duplicated here**. They remain in the Master Plan under:

`Acceptance / stop condition`

The story prompt tells us how many exact story-level acceptance items must pass before the common ALV Completion Contract can even be considered.

That gives us two layers:

**Layer A — Story-specific proof**  
“Did this particular scheduler / odontogram / backup / billing / import story do exactly what its prompt requires?”

**Layer B — Project completion proof**  
“Did it pass tests, preserve its parent, create evidence, update build truth, survive review, and leave no blocking defect?”

A story needs **both** layers to become `COMPLETE`.

------------------------------------------------------------------------

### 11. Quality Gates Are Above Story Completion

Individual story completion does not automatically mean the release is ready.

The Execution Plan defines Gate A through Gate F. `.alveara/QUALITY_GATES.md` records the measurable criteria/evidence for those gates.

A story can be `COMPLETE` while a later gate still fails because:

- several individually correct modules do not integrate correctly;
- end-to-end performance is insufficient;
- recovery fails with the full data set;
- a security review finds a cross-cutting defect;
- install/upgrade behavior fails;
- accessibility breaks in an integrated workflow.

So tracking has three levels:

**Course completion** → portal  
**ALV story completion** → `EXECUTION_STATUS.json` + handoff/review  
**Production readiness** → Quality Gates A–F

### 11.1 Reopening and Downstream Revalidation

When a previously `COMPLETE` story is reopened:

1.  Set that story to `REOPENED` and record why.
2.  Traverse its declared dependency descendants and identify only those whose assumptions/contracts could be affected.
3.  Mark affected completed descendants `REVALIDATION_REQUIRED`; do **not** automatically reopen every later story.
4.  Correct and review-close the reopened story using the normal attempt protocol.
5.  Rerun the targeted regression/integration checks for each affected descendant.
6.  Restore an unaffected/revalidated descendant to `COMPLETE` with evidence, or `REOPENED` if correction is actually required.
7.  Reevaluate every Gate A–F whose evidence depended on changed behavior.

This preserves trustworthy completion without causing unnecessary reimplementation.

------------------------------------------------------------------------

### 12. Future Roadmap Reference

The twelve `ALV-F*` stories are outside the first-release 53-item sequence. Their full future coding-agent prompts now live in the Execution Plan after Gate F.

| ID         | Future capability                                                                | Current tracking rule                                       |
|------------|----------------------------------------------------------------------------------|-------------------------------------------------------------|
| `ALV-F001` | True Multi-Location and Multi-Practice Operations                                | Future executable prompt — select deliberately after Gate F |
| `ALV-F002` | Dental Insurance, Benefits, Estimates, Claims, and Clearinghouse Integration     | Future executable prompt — select deliberately after Gate F |
| `ALV-F003` | Patient Portal and Communications                                                | Future executable prompt — select deliberately after Gate F |
| `ALV-F004` | Direct Dental Imaging and DICOM/Device Integration                               | Future executable prompt — select deliberately after Gate F |
| `ALV-F005` | Electronic Prescribing and Pharmacy Connectivity                                 | Future executable prompt — select deliberately after Gate F |
| `ALV-F006` | Integrated and Online Payments                                                   | Future executable prompt — select deliberately after Gate F |
| `ALV-F007` | Inventory and Supply Management                                                  | Future executable prompt — select deliberately after Gate F |
| `ALV-F008` | FHIR/HL7 and External Clinical Interoperability                                  | Future executable prompt — select deliberately after Gate F |
| `ALV-F009` | Cloud SaaS, Desktop/Mobile Clients, Enterprise Identity, and Off-Site Operations | Future executable prompt — select deliberately after Gate F |
| `ALV-F010` | Advanced AI, Imaging Analysis, Predictive Analytics, and Benchmarking            | Future executable prompt — select deliberately after Gate F |
| `ALV-F011` | Specialty Workflow Expansion                                                     | Future executable prompt — select deliberately after Gate F |
| `ALV-F012` | Accounting and Business-System Integration                                       | Future executable prompt — select deliberately after Gate F |

A future story is not an active first-release record. When the product owner deliberately selects one after Gate F, add it to `EXECUTION_STATUS.json` as a new tracked execution item and execute the complete prompt from the Execution Plan against the then-current repository and external constraints.

**Historical note:** former Execution Index v1.2 described these as roadmap-only briefs requiring later promotion. That rule is superseded because the current Execution Plan now contains complete future coding-agent prompts.

### 13. Daily User Interaction

#### After a course story

Tell me the course story ID and portal result. There is no ALV handoff ZIP for a course-only run unless the course implementation exposes a material production issue requiring review.

#### After a non-course ALV attempt

Upload exactly the generated `Alveara_Handoff_<ALV-ID>_<sha>_<attempt>.zip`.

I review it and return `Alveara_Review_<ALV-ID>_<attempt>.md`.

- If `APPROVED`, give that one review file back to the coding/repository agent for the closure-only commit.
- If `CHANGES_REQUIRED`, give the review file back to the agent; it records the decision and begins the next attempt of the **same** story.
- If `BLOCKED`, resolve the named decision/input before resuming.
- Do not manually collect handoff/test/Git/state files from different locations.

------------------------------------------------------------------------

### 14. STEP 0 — Execution-Control Bootstrap Reference

STEP 0 is executed from the **START HERE** section of the Execution Plan before `STORY-000`. It is not a course story and not one of the 53 product execution items.

The current two-document model intentionally does **not** require a separate user-facing Execution Index and does **not** create `.alveara/EXECUTION_INDEX.md`. The coding agent receives:

1.  the current Alveara Dental Execution Plan;
2.  this Engineering & Product Reference; and
3.  the current repository.

STEP 0 creates and commits:

``` text
.alveara/
├── EXECUTION_STATUS.json
├── HANDOFF_SCHEMA.md
├── BUILD_STATE.md
├── QUALITY_GATES.md
├── handoffs/
└── reviews/
```

Bootstrap requirements:

1.  Prepopulate `EXECUTION_STATUS.json` with exactly the 53 numbered first-release items from the Execution Plan: 19 course records and 34 first-release ALV records.
2.  Course records declare `externalVerification: "course_portal"`.
3.  Record the lifecycle, immutable Rxx attempt model, review decisions, approval closure, BLOCKED/CHANGES_REQUIRED/revalidation behavior, canonical ZIP construction, package safety, and Git-SHA rule in `HANDOFF_SCHEMA.md`.
4.  Create `BUILD_STATE.md` as honest current implemented truth, not a roadmap.
5.  Create `QUALITY_GATES.md` with Gate A–F structure and measurable evidence fields. Do not mark a gate passed or invent measurements during bootstrap.
6.  Create `handoffs/` and `reviews/` directories/placeholders as needed.
7.  Set only genuinely dependency-ready work to `READY`; other items start `PLANNED`. Nothing starts `COMPLETE` or falsely `IN_PROGRESS`.
8.  Validate all IDs, parents, dependencies, counts, and course/product separation against the Execution Plan and this Reference.
9.  Commit bootstrap separately as `ALV-CONTROL: initialize execution control`.
10. Stop after bootstrap and complete the explicit STEP 0 review action in the Execution Plan before beginning `STORY-000`.

**Historical note:** former Execution Index v1.2 required storing the Index itself as `.alveara/EXECUTION_INDEX.md`. That requirement is superseded by the current two-document design; the Execution Plan is the human execution authority and this Reference contains the merged operating rules.

### 15. One-Line Operating Rule

> **Never start the next ALV story because an implementation agent or reviewer merely said “done.” Advance only after the current attempt is approved, the approval is durably recorded by the review-closure commit, any required phase gate passes, and `EXECUTION_STATUS.json` truthfully shows the prior item `COMPLETE` (or deliberately `DEFERRED` only where the Execution Plan explicitly allows it).**

### 4.A Historical provenance from the former standalone Index

The following notes are retained for provenance only. They explain why the handoff/package system evolved; they do not override the current Execution Plan or the current rules above.

### Historical Index evolution — v1 → v1.1

I challenged v1 as if several stories had already been completed and the project had to continue months later in another chat.

The execution sequence and three-level completion model were sound, but five practical weaknesses were found:

1.  **The handoff workflow was too manual.** Asking the user to gather several files creates friction and makes incomplete evidence likely.
2.  **“Handoff exists” was underspecified.** A prose handoff alone does not prove acceptance coverage, exact tests, parent regression, Git state, or next-story impact.
3.  **Review portability was weak.** A future reviewing conversation should be able to understand a completed story from one artifact.
4.  **The evidence package needed a schema.** Without a manifest and fixed filenames, different coding-agent runs could produce incompatible handoffs.
5.  **Evidence versus source-of-truth needed clarification.** The ZIP is a review snapshot; current code/tests remain authoritative. Full repositories should not be copied into every handoff by default.

v1.1 fixes these weaknesses without changing the execution sequence. The central improvement is a mandatory, validated **single handoff ZIP after every non-course story**.

### Historical Index evolution — v1.2

A full daily-use simulation of Master Plan v3.2 + Index v1.1 found no product-story redesign requirement, but it exposed execution-control edge cases. v1.2 corrects them:

- historically paired the standalone Index with Master Plan v3.3;
- adds **STEP 0 — Execution-Control Bootstrap** before any story work;
- removes the impossible requirement for a committed handoff file to contain the SHA of the commit that contains itself;
- adds a durable **review-closure commit** after approval so the repository, not only the chat, records `COMPLETE`;
- preserves rejected/corrected review attempts as `R01`, `R02`, ... rather than overwriting history;
- defines a useful `BLOCKED` package path when material work or analysis occurred;
- inserts explicit Gate A–F stop points into daily execution;
- makes the optional-AI include/defer branch operationally explicit;
- adds `REVALIDATION_REQUIRED` for downstream impact when a completed story is reopened;
- fixes course-row wording so course stories do not imply an ALV ZIP handoff;
- aligns the production status schema to Master Plan v3.3 / Index v1.2.

No course Done Means, ALV acceptance criterion, product requirement, or first-release story identity is changed by this revision.

------------------------------------------------------------------------

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
