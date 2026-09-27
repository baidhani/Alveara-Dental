# Alveara Dental — Execution Index v1.2

**Companion document to:** `Alveara_Dental_Master_Production_Story_Plan_v3.3.md`  
**Purpose:** This is the human-readable navigation and execution-control document. The Master Plan contains the full prompts and exact acceptance criteria. This Index tells us **what to run, in what order, what “complete” means, what evidence must exist, what to review next, and how our own production-story tracking works**. v1.2 is the final execution-control hardening pass. It preserves the story sequence while adding bootstrap, review closure, attempt/retry history, blocked handoffs, explicit Gate A–F stops, optional-AI branching, and reopen/revalidation handling.

## v1 → v1.1 Challenge Outcome

I challenged v1 as if several stories had already been completed and the project had to continue months later in another chat.

The execution sequence and three-level completion model were sound, but five practical weaknesses were found:

1.  **The handoff workflow was too manual.** Asking the user to gather several files creates friction and makes incomplete evidence likely.
2.  **“Handoff exists” was underspecified.** A prose handoff alone does not prove acceptance coverage, exact tests, parent regression, Git state, or next-story impact.
3.  **Review portability was weak.** A future reviewing conversation should be able to understand a completed story from one artifact.
4.  **The evidence package needed a schema.** Without a manifest and fixed filenames, different coding-agent runs could produce incompatible handoffs.
5.  **Evidence versus source-of-truth needed clarification.** The ZIP is a review snapshot; current code/tests remain authoritative. Full repositories should not be copied into every handoff by default.

v1.1 fixes these weaknesses without changing the execution sequence. The central improvement is a mandatory, validated **single handoff ZIP after every non-course story**.

## v1.2 Final Cross-Check Corrections

A full daily-use simulation of Master Plan v3.2 + Index v1.1 found no product-story redesign requirement, but it exposed execution-control edge cases. v1.2 corrects them:

- pairs this Index explicitly with Master Plan v3.3;
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

## 1. The Four Story Classes

### Course stories — `STORY-000` through `STORY-018`

These are owned and verified by the course portal. Their completion authority is the course itself.

A course story is not complete merely because the coding agent says it is complete. It is complete only when:

1.  its exact portal Done Means / acceptance lines are genuinely satisfied;
2.  `.colaberry/progress.json` is updated truthfully;
3.  required course enrichment is written;
4.  the commit/push requirements are satisfied; and
5.  the portal confirms the criteria.

Our own tracker may **mirror** that status for sequencing, but it never overrides the course portal.

### Companion stories — `ALV-xxx-Cxx`

A companion starts from an original course implementation and hardens it into the production behavior we actually want.

Every companion has a course parent. It must preserve that parent's course completion contract permanently.

### New production stories — `ALV-Nxxx`

These cover capabilities the course generator omitted, compressed, or could not sequence safely, such as the application shell, architecture, backup/recovery, safety framework, forms/e-signature foundation, reporting, deployment and security-readiness work.

### Future roadmap stories — `ALV-Fxxx`

These preserve the long-term vision but are **not execution-ready**. They do not enter first-release tracking until deliberately promoted into a full execution prompt.

------------------------------------------------------------------------

## 2. Yes — We Need Our Own Completion and Tracking System

The Master Plan already gives every companion/new production story:

- story-specific **Acceptance / stop condition** items;
- required tests;
- UI/UX deliverables where applicable;
- security/audit/data-integrity requirements;
- failure paths;
- engineering handoff requirements; and
- a stop-and-review rule.

What was missing was a **central production status ledger** comparable to the course portal.

### Our production tracking file

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

## 3. ALV Story Status Lifecycle

Use only these normal states:

`PLANNED → READY → IN_PROGRESS → AWAITING_REVIEW → COMPLETE`

Exceptional states:

- `BLOCKED` — cannot proceed because a dependency, requirement, credential, architecture decision, or defect blocks truthful completion.
- `CHANGES_REQUIRED` — implementation ran, review found work that must be corrected before completion.
- `DEFERRED` — product owner deliberately postponed the story; impact/reason must be recorded.
- `REOPENED` — a previously complete story later regressed or must be changed.
- `REVALIDATION_REQUIRED` — a completed downstream story may have been affected by a reopened dependency and must be impact-reviewed/regression-verified before its prior completion can be trusted again.

### Who may declare completion?

The coding agent **does not self-certify `COMPLETE`**.

At the end of an ALV run, the coding agent should move the story to **`AWAITING_REVIEW`** and produce the handoff/evidence.

Then:

1.  you give me the handoff and, when needed, the current project checkpoint;
2.  I review the actual implementation against the Master Plan, dependencies, tests, parent-course contract and next-story impact;
3.  I tell you whether the story is ready for `COMPLETE`, requires `CHANGES_REQUIRED`, or should be `BLOCKED/REOPENED`;
4.  you decide whether to proceed to the next execution item.

That prevents “the agent said done” from becoming our definition of done.

------------------------------------------------------------------------

## 4. The ALV Completion Contract

A companion or new production story is **COMPLETE only when every applicable condition below is true**:

1.  **All story-specific Acceptance / stop condition items pass.**  
    The exact list lives in the Master Plan under that story.

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

12. **Review is completed.** The reviewing ChatGPT inspects the ZIP against the Master Plan, dependencies, tests, parent contract and next-story impact. Only if the package reveals an uncertainty that cannot be resolved from its evidence should the reviewer request the smallest additional source/project artifact necessary.

13. **Approval is durably closed in the repository.** The coding agent records the review decision under `.alveara/reviews/<ALV-ID>/<attempt>.md`, updates `EXECUTION_STATUS.json` to `COMPLETE`, and makes a small review-closure commit. Only then may the next execution item begin.

Optional first-release AI stories may be deliberately `DEFERRED` without blocking the safe core release, provided the dependency/gate impact is recorded.

------------------------------------------------------------------------

## 5. Canonical `EXECUTION_STATUS.json` Story Record

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

## 6. What Happens After Every Course Story

1.  Run the **portal's original prompt unchanged**.
2.  Satisfy the portal Done Means criteria truthfully.
3.  Complete the `.colaberry` progress/enrichment/commit/push process.
4.  Wait for portal confirmation.
5.  Mirror the confirmed course state into `.alveara/EXECUTION_STATUS.json`.
6.  Follow this Index only after the course result is mirrored into `EXECUTION_STATUS.json`.
7.  If the next item is an ALV story, verify its dependencies are `COMPLETE`/portal-confirmed as applicable. If a dependency is unavailable, stop and review rather than skipping blindly.

A course story may be portal-complete while its **production capability is still incomplete**. That is exactly why companions exist.

------------------------------------------------------------------------

## 7. Non-Course Story Attempt, Review, and Closure Protocol

Generating the handoff ZIP is part of the Definition of Done, but **the ZIP is an attempt package, not proof of final approval**.

### 7.1 Normal successful attempt

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

### 7.2 Review result

The reviewer returns one of:

- `APPROVED`
- `CHANGES_REQUIRED`
- `BLOCKED`
- `REOPENED`

The reviewer should also produce a compact decision artifact named:

`Alveara_Review_<ALV-ID>_<attempt>.md`

containing the reviewed implementation/evidence SHAs, decision, reasons, required corrections if any, next-story impact, and whether any downstream story/gate needs revalidation.

### 7.3 Approval closure

When the result is `APPROVED`, give the review decision artifact to the coding/repository agent. That agent performs **closure only**:

1.  Verify the decision artifact refers to the same story, attempt, implementation SHA and evidence SHA.
2.  Store it as `.alveara/reviews/<ALV-ID>/<attempt>.md`.
3.  Update `EXECUTION_STATUS.json` from `AWAITING_REVIEW` to `COMPLETE`, recording the approved attempt, implementation SHA, evidence SHA, completion timestamp and review decision path.
4.  Update `BUILD_STATE.md` only if approval changes a durable current-state fact.
5.  Commit with `ALV-ID: record review approval`.
6.  Return the review-closure commit SHA.
7.  Only now may the next Index item begin.

The closure commit SHA is **not required inside the files committed by that same closure commit**; Git history itself is the authority for that commit identity.

### 7.4 Changes required

If review returns `CHANGES_REQUIRED`:

1.  Preserve the rejected attempt and ZIP.
2.  Record the decision under `.alveara/reviews/<ALV-ID>/<attempt>.md`.
3.  Set the story to `CHANGES_REQUIRED`.
4.  Correct the same story; do not begin another story.
5.  Increment the attempt (`R01 → R02`).
6.  Produce a new implementation/evidence pair and a new ZIP.
7.  The next review evaluates the new attempt while preserving prior history.

Never overwrite an earlier attempt handoff or review decision.

### 7.5 Blocked story

If a material blocker is discovered after work/analysis begins, set `BLOCKED`. Whenever technically possible, produce:

`Alveara_Handoff_<ALV-ID>_<implementation-short-sha-or-NOCOMMIT>_<attempt>_BLOCKED.zip`

It contains the normal evidence that exists plus a prominent blocker description, work completed, work not completed, safety/data implications, and the exact decision/input required to resume. A blocker that prevents packaging must be reported explicitly; never fabricate evidence merely to satisfy the package shape.

### 7.6 Mandatory attempt ZIP

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

### 7.7 Package safety

Never package secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, real-data backups, dependency caches, or irrelevant build output. Do not copy the entire repository by default. The package is compact review evidence; current code/tests remain implementation truth.

## **The user should normally upload one handoff ZIP for review, then return one review-decision file to the coding agent for closure.**

## 8. Phase Map — What the Product Becomes

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

## 9. Atomic First-Release Execution Index

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

### Mandatory Phase Gate Checkpoints

The 53 story items remain unchanged, but daily execution has six **non-story stop points**:

| After item | Gate                                                       | Rule                                                             |
|-----------:|------------------------------------------------------------|------------------------------------------------------------------|
|         10 | **Gate A — Foundation Ready**                              | Do not start patient/front-office expansion until Gate A passes. |
|         17 | **Gate B — Patient, Scheduling, and Visit Entry Ready**    | Do not start Clinical Core until Gate B passes.                  |
|         30 | **Gate C — Clinical Core Ready**                           | Do not start Treatment & Finance until Gate C passes.            |
|         37 | **Gate D — Treatment Completion and Financial Core Ready** | Do not start Operations & Data until Gate D passes.              |
|         48 | **Gate E — Operations, Documents, and Data Ready**         | Resolve Gate E before optional AI/release engineering.           |
|         53 | **Gate F — Production Candidate**                          | Required before first-production baseline is declared.           |

At a gate, run the gate checks already defined in the Master Plan and update `.alveara/QUALITY_GATES.md`. A failed gate stops forward phase progression. Repair/reopen the responsible story or stories, run impact revalidation, then reevaluate the gate. Do not lower a predeclared threshold merely to pass.

Gate evaluation is evidence-producing work. When a gate requires a substantial integrated verification run, package its evidence as `Alveara_Gate_<A-F>_<evidence-short-sha>.zip` so the review can be reproduced without turning the gate into another product story.

### Optional AI branch

After item 48 / Gate E:

- **Include AI:** execute `ALV-N007` and `ALV-N012`, review/close each, then continue to `ALV-N013`.
- **Defer AI:** record both stories as `DEFERRED` with product-owner reason and release impact, verify no required core workflow depends on AI, then continue directly to `ALV-N013`.

Deferral is a recorded release decision, not silent skipping.

### Special sequencing notes

- `ALV-N005` intentionally builds the production procedure/fee catalog foundation **before** `STORY-014`; when STORY-014 becomes available, satisfy its immutable course contract against that already-production-grade implementation.
- `ALV-007-C01` establishes the financial ledger/charge foundation **before** `ALV-008-C01` procedure completion creates billable charges.
- `ALV-007-C02` hardens statements/receipts/refunds/corrections after real procedure-linked charges exist.
- `ALV-018-C01` and `ALV-018-C02` intentionally split export from import/migration because their risk profiles are different.
- `ALV-N007` and `ALV-N012` are optional first-release AI enhancements. Deferring them does not excuse any weakness in the core non-AI product.

------------------------------------------------------------------------

## 10. Completion Conditions Are Story-Specific, Not One Generic Checkbox

The `Completion authority` column above tells you how each execution item is judged.

For every ALV story, the **exact behavioral conditions are not duplicated here**. They remain in the Master Plan under:

`Acceptance / stop condition`

The number shown in the Index tells us how many exact story-level acceptance items must pass before the common ALV Completion Contract can even be considered.

That gives us two layers:

**Layer A — Story-specific proof**  
“Did this particular scheduler / odontogram / backup / billing / import story do exactly what its prompt requires?”

**Layer B — Project completion proof**  
“Did it pass tests, preserve its parent, create evidence, update build truth, survive review, and leave no blocking defect?”

A story needs **both** layers to become `COMPLETE`.

------------------------------------------------------------------------

## 11. Quality Gates Are Above Story Completion

Individual story completion does not automatically mean the release is ready.

The Master Plan defines Gate A through Gate F. `.alveara/QUALITY_GATES.md` records the measurable criteria/evidence for those gates.

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

## 11.1 Reopening and Downstream Revalidation

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

## 12. Future Roadmap Index

These are preserved but **not part of first-release execution**:

| ID         | Future capability                                                                | Current tracking rule                                           |
|------------|----------------------------------------------------------------------------------|-----------------------------------------------------------------|
| `ALV-F001` | True Multi-Location and Multi-Practice Operations                                | Roadmap only — promote into an execution-ready story before use |
| `ALV-F002` | Dental Insurance, Benefits, Estimates, Claims, and Clearinghouse Integration     | Roadmap only — promote into an execution-ready story before use |
| `ALV-F003` | Patient Portal and Communications                                                | Roadmap only — promote into an execution-ready story before use |
| `ALV-F004` | Direct Dental Imaging and DICOM/Device Integration                               | Roadmap only — promote into an execution-ready story before use |
| `ALV-F005` | Electronic Prescribing and Pharmacy Connectivity                                 | Roadmap only — promote into an execution-ready story before use |
| `ALV-F006` | Integrated and Online Payments                                                   | Roadmap only — promote into an execution-ready story before use |
| `ALV-F007` | Inventory and Supply Management                                                  | Roadmap only — promote into an execution-ready story before use |
| `ALV-F008` | FHIR/HL7 and External Clinical Interoperability                                  | Roadmap only — promote into an execution-ready story before use |
| `ALV-F009` | Cloud SaaS, Desktop/Mobile Clients, Enterprise Identity, and Off-Site Operations | Roadmap only — promote into an execution-ready story before use |
| `ALV-F010` | Advanced AI, Imaging Analysis, Predictive Analytics, and Benchmarking            | Roadmap only — promote into an execution-ready story before use |
| `ALV-F011` | Specialty Workflow Expansion                                                     | Roadmap only — promote into an execution-ready story before use |
| `ALV-F012` | Accounting and Business-System Integration                                       | Roadmap only — promote into an execution-ready story before use |

When a future story is selected, it must first be promoted into a detailed execution-ready prompt and then added to `EXECUTION_STATUS.json`.

------------------------------------------------------------------------

## 13. Daily User Interaction

### After a course story

Tell me the course story ID and portal result. There is no ALV handoff ZIP for a course-only run unless the course implementation exposes a material production issue requiring review.

### After a non-course ALV attempt

Upload exactly the generated `Alveara_Handoff_<ALV-ID>_<sha>_<attempt>.zip`.

I review it and return `Alveara_Review_<ALV-ID>_<attempt>.md`.

- If `APPROVED`, give that one review file back to the coding/repository agent for the closure-only commit.
- If `CHANGES_REQUIRED`, give the review file back to the agent; it records the decision and begins the next attempt of the **same** story.
- If `BLOCKED`, resolve the named decision/input before resuming.
- Do not manually collect handoff/test/Git/state files from different locations.

------------------------------------------------------------------------

## 14. STEP 0 — Execution-Control Bootstrap

**Do this once before STORY-000. It is not a course story and not one of the 53 product execution items.**

Create and commit:

``` text
.alveara/
├── EXECUTION_INDEX.md
├── EXECUTION_STATUS.json
├── HANDOFF_SCHEMA.md
├── BUILD_STATE.md
├── QUALITY_GATES.md
├── handoffs/
└── reviews/
```

Bootstrap requirements:

1.  Store this Index as `.alveara/EXECUTION_INDEX.md`.
2.  Record control-set versions: Master Plan `3.3`, Execution Index `1.2`, Handoff Schema `2`.
3.  Prepopulate `EXECUTION_STATUS.json` with all 53 first-release items, their type, parent, dependencies, initial status and empty evidence/review fields. Course records also declare `externalVerification: "course_portal"`.
4.  Set only genuinely dependency-ready items to `READY`; others start `PLANNED`. Do not mark anything `COMPLETE`.
5.  Create `HANDOFF_SCHEMA.md` from Section 7, including attempt naming, ZIP validation, package safety, Git-SHA rule and review-closure protocol.
6.  Create `BUILD_STATE.md` as an honest empty/baseline state, not a list of planned features.
7.  Create `QUALITY_GATES.md` with Gate A–F headings and measurable criteria placeholders from the Master Plan; thresholds must be defined before the corresponding final measurement, not rewritten after failure.
8.  Create `handoffs/` and `reviews/` directories/placeholders as needed for Git.
9.  Validate that every ID in the Master Plan's first-release backlog exists exactly once in `EXECUTION_STATUS.json`.
10. Commit the bootstrap separately, e.g. `ALV-CONTROL: initialize execution control`.
11. Only after bootstrap validation begin `STORY-000`.

## From then on: Master Plan = detailed specification; Execution Index = order/operating procedure; `EXECUTION_STATUS.json` = production ledger; attempt handoffs/reviews = history; `BUILD_STATE.md` = current truth; `QUALITY_GATES.md` = integrated readiness.

## 15. One-Line Operating Rule

> **Never start the next ALV story because an implementation agent or reviewer merely said “done.” Advance only after the current attempt is approved, the approval is durably recorded by the review-closure commit, required phase gates pass, and `EXECUTION_STATUS.json` truthfully shows the prior item `COMPLETE` (or deliberately `DEFERRED` where allowed).**
