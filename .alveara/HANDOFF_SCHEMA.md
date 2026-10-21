# Alveara Dental — Handoff Schema (v3)

Durable repository contract for how every non-course (`ALV-*`) story is executed, packaged, reviewed, and closed. This file is the canonical reference `HANDOFF_SCHEMA.md` referenced throughout `.alveara/`.

## 1. Status Lifecycle

Normal single-stage lifecycle:

```
PLANNED → READY → IN_PROGRESS → AWAITING_REVIEW → COMPLETE
```

Two-stage lifecycle, when permitted:

```
PLANNED → READY → IN_PROGRESS → AWAITING_REVIEW → AWAITING_REGRESSION → AWAITING_REVIEW → COMPLETE
```

Exceptional states:

- `AWAITING_REGRESSION` — a Stage 1 review accepted the implementation conditionally; the exact broad-regression contract is still required before final review.
- `BLOCKED` — cannot proceed because a dependency, requirement, credential, architecture decision, defect, or an inconclusive regression environment blocks truthful completion.
- `CHANGES_REQUIRED` — implementation ran, review found work that must be corrected before completion.
- `DEFERRED` — product owner deliberately postponed the story; impact/reason must be recorded.
- `REOPENED` — a previously complete story later regressed or must be changed.
- `REVALIDATION_REQUIRED` — a completed downstream story may have been affected by a reopened dependency and must be impact-reviewed/regression-verified before its prior completion can be trusted again.

**Who may declare `COMPLETE`?** Never the coding agent alone. A coding-agent run ends at `AWAITING_REVIEW`. Only a durable review-closure commit (Section 4) may move a story to `COMPLETE`.

## 2. Immutable Attempts

- Attempts are numbered `R01`, `R02`, `R03`, ...
- A prior attempt's handoff, evidence, ZIP, review decision, and any regression addendum are **never overwritten**.
- A regression addendum belongs to exactly one attempt and is additive; it never replaces that attempt's Stage 1 handoff, evidence, ZIP, or review decision.
- `CHANGES_REQUIRED` preserves the rejected attempt in place and starts a new, incremented attempt of the *same* story.
- `BLOCKED` (Section 6) preserves whatever attempt evidence exists; it never fabricates missing evidence to look complete.

## 3. Commit Order

For every attempt:

1. **Implementation commit** — the actual story work. Capture its full SHA.
2. **Evidence/status commit** — `.alveara/handoffs/<ID>/<attempt>.md`, updated `BUILD_STATE.md`, `EXECUTION_STATUS.json` (moved to `AWAITING_REVIEW`), and any affected `QUALITY_GATES.md` evidence. Capture its full SHA.
3. **Validated handoff ZIP** — generated only after both commits exist (Section 5).
4. **Two-stage only: regression evidence commit** — after a Stage 1 conditional approval, record `.alveara/handoffs/<ID>/<attempt>-regression.md`, the required status/build-state changes, and no product code, using `<ALV-ID>: record regression evidence`.

A committed file is **never required to contain the SHA of the commit that contains that same file.** The attempt handoff records the implementation SHA but not its own (evidence) commit's SHA; the generated ZIP's `MANIFEST.json` and `evidence/GIT_STATE.md` record both SHAs once they are both known.

The coding agent stops at `AWAITING_REVIEW` after step 3 and never self-certifies `COMPLETE`. A two-stage story resumes only after the reviewer records its Stage 1 conditional approval.

## 4. Review Decisions and Closure

The reviewer returns exactly one of:

- `APPROVED`
- `APPROVED_PENDING_REGRESSION` — Stage 1 only; the implementation review passed, but full regression and final review remain required.
- `CHANGES_REQUIRED`
- `BLOCKED`
- `REOPENED`

### Two-stage conditional approval and final regression review

Use this flow for every future story, milestone, release, and gate that requires expensive broad regression after `ALV-010-C01`. A current attempt already running broad regression remains on its existing flow. The reviewer may require single-stage evidence only when broad regression itself is necessary to prove a specific acceptance criterion before implementation can be assessed; record that concrete reason in the review artifact.

1. **Stage 1 package:** the normal handoff ZIP contains all required focused evidence, including acceptance, parent-course, failure/security/concurrency, changed-subsystem integration, and prompt-required real-backend walkthrough evidence. `TEST_RESULTS.md` says the broad regression is `PENDING`, states why it is required, and freezes the exact Stage 2 commands, suites, walkthroughs, data/environment requirements, and pass/fail/skip expectations.
2. **Stage 1 decision:** store `APPROVED_PENDING_REGRESSION` at `.alveara/reviews/<ALV-ID>/<attempt>-stage1.md`. A closure-only commit (`<ALV-ID>: record stage 1 review`) changes status from `AWAITING_REVIEW` to `AWAITING_REGRESSION`, records `fullRegression: "REQUIRED"`, and changes no product/test/build/configuration code.
3. **Equivalence before Stage 2:** run only from the Stage 1 review-closure commit. Its diff from the implementation commit may contain only the exact Stage 1 review artifact, `.alveara/EXECUTION_STATUS.json`, and `.alveara/BUILD_STATE.md`. Any other change, including source, tests, migrations, scripts, package/build configuration, or a general documentation edit, voids the Stage 1 approval for that changed tree and requires a new Stage 1 review.
4. **Stage 2 evidence:** execute the frozen Stage 2 contract on that equivalent commit. Record the exact commands, outputs, counts, relevant leak checks, run commit, implementation SHA, and equivalence check in `.alveara/handoffs/<ALV-ID>/<attempt>-regression.md`; package it as `Alveara_Regression_<ALV-ID>_<implementation-short-sha>_<attempt>.zip`. The regression evidence commit changes status back to `AWAITING_REVIEW` with `fullRegression: "PASSED"` only after every frozen check passes.
5. **Final decision:** the reviewer returns `APPROVED` only after inspecting the Stage 2 package. The normal approval closure then produces `COMPLETE`.
6. **Failure:** a product/test regression failure becomes `CHANGES_REQUIRED`, records `fullRegression: "FAILED"`, preserves all Stage 1/2 evidence, and starts a new attempt. An unavailable service, database, machine, credential, or other environment-only inconclusive result is `BLOCKED`, keeps `fullRegression: "REQUIRED"`, and may rerun the unchanged Stage 2 contract without a new implementation attempt once the environment is restored.

### Approval closure (only path to `COMPLETE`)

1. Verify the decision artifact refers to the same story, attempt, implementation SHA, and evidence SHA.
2. Store it at `.alveara/reviews/<ALV-ID>/<attempt>.md`.
3. Update `EXECUTION_STATUS.json` from `AWAITING_REVIEW` to `COMPLETE`: record the approved attempt, implementation SHA, evidence SHA, completion timestamp, and review-decision path.
4. Update `BUILD_STATE.md` only if approval changes a durable current-state fact.
5. Make one **closure-only commit** (message: `<ALV-ID>: record review approval`) before the next story begins.
6. Report the closure commit SHA.

### Changes required

1. Preserve the rejected attempt, its ZIP, and its evidence.
2. Record the decision under `.alveara/reviews/<ALV-ID>/<attempt>.md`.
3. Set the story to `CHANGES_REQUIRED`.
4. Correct the *same* story only — do not begin another story.
5. Increment the attempt (`R01 → R02`).
6. Produce a new implementation/evidence commit pair and a new ZIP.
7. The next review evaluates only the new attempt while prior history remains intact.

## 5. Canonical ZIP Naming and Contents

```
Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip
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

- `HANDOFF.md` records story, attempt, parent/baseline, implementation SHA, scope, architecture/schema/API/UI/security/config/measurement changes, decisions, deviations, limitations/debt, unresolved questions, and next-story impact. It does **not** need the evidence commit SHA.
- `MANIFEST.json` is generated **after** the evidence commit and records control-set versions, story/attempt, parent, implementation SHA, evidence SHA, timestamp/timezone, package inventory, acceptance count, exact test summary, regression/demo result, blockers, gates touched, `containsSecrets:false`, `containsRealPHI:false`.
- `TEST_RESULTS.md` — exact commands/suites and pass/fail/skip counts.
- `ACCEPTANCE_EVIDENCE.md` — maps every exact acceptance item to proving evidence.
- `PARENT_REGRESSION.md` — maps every parent Done Means for companions, or states justified `N/A` for new-production stories.
- `DEMO_EVIDENCE.md` — the visible workflow/states, or justified `N/A`.
- `CHANGED_FILES.md` — groups material changes.
- `GIT_STATE.md` — branch, implementation SHA, evidence SHA, clean/dirty state, relevant commits.
- `NEXT_STORY_IMPACT.md` — assumptions, interfaces, migrations, UI extension points, unresolved decisions, and prompt changes relevant to the next scheduled item.

### Two-stage regression package

```
Alveara_Regression_<ALV-ID>_<implementation-short-sha>_<attempt>.zip
├── MANIFEST.json
├── REGRESSION_RESULTS.md
├── BUILD_STATE.md
├── EXECUTION_STATUS.json
├── evidence/
│   ├── GIT_STATE.md
│   └── <exact result files and leak-check outputs>
└── review/
    └── STAGE1_DECISION.md
```

The manifest and `GIT_STATE.md` identify the implementation SHA, Stage 1 review-closure SHA, regression run SHA, regression evidence SHA, and the equivalence result. The package contains the frozen contract and its exact outcomes; it does not repeat Stage 1 evidence unnecessarily.

## 6. Blocked Attempt Packaging

If a material blocker is discovered after work/analysis begins, set `BLOCKED`. Whenever technically possible, produce:

```
Alveara_Handoff_<ALV-ID>_<implementation-short-sha-or-NOCOMMIT>_<attempt>_BLOCKED.zip
```

containing whatever normal evidence exists plus a prominent blocker description, work completed, work not completed, safety/data implications, and the exact decision/input required to resume. Never fabricate evidence merely to satisfy the package shape.

## 7. Reopening and Downstream Revalidation

1. Set the reopened story to `REOPENED` and record why.
2. Traverse its declared dependency descendants; identify only those whose assumptions/contracts could be affected.
3. Mark affected completed descendants `REVALIDATION_REQUIRED` — do **not** automatically reopen every later story.
4. Correct and review-close the reopened story using the normal attempt protocol.
5. Rerun targeted regression/integration checks for each affected descendant.
6. Restore an unaffected/revalidated descendant to `COMPLETE` with evidence, or `REOPENED` if correction is actually required.
7. Reevaluate every Gate A–F whose evidence depended on changed behavior.

## 8. Package Safety

Never package secrets, credentials, tokens, password hashes, encryption/recovery keys, real PHI/patient data, real-data backups, dependency caches, or irrelevant build output. Do not copy the entire repository by default. The package is compact review evidence; current code/tests remain implementation truth if packaged prose is ever stale.

## 9. Git Self-Reference Prohibition

A committed file is never required to contain the SHA of the commit that contains that same file. This applies to attempt handoffs (which record the implementation SHA but not their own evidence-commit SHA) and to the `ALV-CONTROL` bootstrap handoff (which records the bootstrap implementation SHA but not its own evidence-commit SHA — the generated ZIP records both once the evidence commit exists).

---

## Standard Production Prompt Rules

Every `ALV-*` coding-agent prompt in the Execution Plan inherits these rules, persisted here so later prompts can rely on them without the user re-supplying them manually.

1. **Read execution state before editing.** Read `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and dependency handoffs when they exist, then inspect current code/tests. Current code/tests are authoritative when they disagree with stale packaged/handoff prose.
2. **Inspect the parent implementation.** If the story has a parent `STORY-xxx`, identify what that course implementation actually built before changing it. Record this later as **Parent baseline observed**.
3. **Preserve course truth.** Run the parent's exact course acceptance behavior before and after companion work. Never remove, weaken, or fake those behaviors.
4. **Protect the course Command Center.** Do not replace or repurpose the root STORY-000 `index.html`/Command Center while building the real product UI.
5. **UI and function together.** Do not finish with backend-only behavior when a user operates the feature. Build the visible workflow using the shared design system.
6. **Authorization is server-side.** UI visibility is convenience; service/API enforcement is the security boundary.
7. **Use shared primitives, own domain semantics.** Reuse audit/concurrency/finalization infrastructure, but let the domain story define its own finalized/amended/reversed lifecycle.
8. **No destructive history.** Finalized clinical and authoritative financial records are corrected by amendment/addendum/void/reversal/inactivation as appropriate, never silent overwrite/delete.
9. **Transactions and idempotency.** Multi-record consequential operations must be atomic where the domain requires it, safely recoverable otherwise, and protected against duplicate submission.
10. **No invented integrations, clinical facts, or unsupported medical decision support.** Surface uncertainty and missing data. Do not claim clinical dose appropriateness, diagnosis, insurance benefit adjudication, or other decision support unless the required authoritative source/validation exists.
11. **Respect foundational data rules.** Use documented time/timezone, money, identity-separation, offline, PHI-safe logging, document-integrity, database-topology, cryptography, audit-coupling, measurement-event, and background-work conventions.
12. **Extend the integrated patient workspace.** Patient-facing/patient-context features should add to the shared patient workspace/header/navigation/status model rather than invent a parallel patient shell.
13. **Own domain configuration where it belongs.** Add configuration such as note templates, document categories, recall defaults or payment methods in the domain story that consumes it rather than growing one speculative global settings story.
14. **Instrument measurable outcomes minimally.** When a story affects an agreed success metric, use the shared privacy-safe/versioned measurement-event convention.
15. **Tests are part of the story.** Include happy path, material failure paths, authorization where applicable, data-integrity/concurrency where applicable, and regression tests for the parent course contract.
16. **Use the risk-based regression cadence.** Every story runs its acceptance, parent-course, material failure/security/concurrency, and changed-subsystem integration tests before packaging. For up to three consecutive low/medium-risk stories, the full backend, frontend, repository-check, and mocked-browser regression runs may be batched; they are mandatory with the third eligible story's handoff. Run the full regression before every high-risk story handoff and whenever the individual story prompt requires it. High-risk includes a migration/schema change; authentication/authorization, audit, transaction, idempotency or concurrency primitive; money/billing; backup/restore; encryption; shared middleware/runtime configuration; production build/deployment configuration; or test infrastructure. At every quality gate, release, or declared milestone, the full regression plus required real-backend walkthroughs and gate-specific evidence remains mandatory, but uses the two-stage protocol in Section 4: Stage 1 reviews focused evidence and the frozen verification plan; Stage 2 runs that broad contract only after `APPROVED_PENDING_REGRESSION`. Future story prompts must use the same sequence rather than requiring broad regression before initial handoff. A reviewer may require single-stage evidence only when broad regression itself is necessary to prove a specific acceptance criterion before implementation can be assessed, and must record that reason. `TEST_RESULTS.md` must state the cadence classification, exact commands, results, and—when broad regression is deferred or pending Stage 2—the batch position/status and next due full-regression story.
17. **Demonstrate truthful UI states.** Include loading/empty/error/permission/offline-or-disconnected states where the workflow can encounter them; never show invented health/status/clinical/financial facts.
18. **Implementation stop condition.** Implementation is ready for packaging only when production acceptance criteria pass, required tests pass, the visible workflow is demonstrated where applicable, and no parent course behavior regressed.
19. **Attempt evidence is mandatory.** Create `.alveara/handoffs/<ALV-STORY-ID>/<attempt>.md`, update `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, and affected quality-gate evidence; preserve prior attempts.
20. **Implementation + evidence commits.** Commit implementation first and capture its SHA; commit attempt handoff/status/build-state evidence separately. Do not require that committed evidence to contain its own evidence-commit SHA.
21. **Attempt ZIP + review closure are mandatory.** Follow this `HANDOFF_SCHEMA.md`, generate and validate `Alveara_Handoff_<ALV-ID>_<implementation-short-sha>_<attempt>.zip`, leave the story `AWAITING_REVIEW`, and stop. For an eligible two-stage story, Stage 1 may end with `fullRegression: "PENDING"`; after `APPROVED_PENDING_REGRESSION`, stop again at `AWAITING_REGRESSION` until the frozen Stage 2 contract is run and reviewed. Only final `APPROVED` creates the closure commit that moves the repository status to `COMPLETE` before another story begins.

## Gate Evidence Rules

Before any gate is evaluated, `.alveara/QUALITY_GATES.md` must carry the measurable criteria relevant to that gate: metric/check, test method/tool, representative dataset or environment, and pass/block threshold or rubric — predeclared, not invented after the fact.

- **Accessibility** — define the critical workflows and target/rubric before the release check; do not retroactively lower it to pass.
- **Performance** — define measurable response/throughput/resource thresholds and representative data volumes before final measurement.
- **Security severity** — define the scanner/rubric and blocking severity policy before reviewing release findings.
- **Recovery** — define what persistent asset classes and representative records/files must survive each restore drill.
- **Data reconciliation** — define expected source/target counts/totals and tolerances before import/export/financial checks.
- If a threshold legitimately changes, record the reason and date; never silently rewrite a failed gate into a pass.

## Global Definition of Production Complete

Alveara Dental v1 is not production-complete until:

- Required course stories are truthfully verified by the portal.
- Every required first-production `ALV-N*` and `ALV-*-C*` story is complete. Only stories explicitly marked optional by the Execution Plan (`ALV-N007`, `ALV-N012`) may be deliberately deferred with documented reason/impact.
- Gates A through F pass with evidence.
- No known high-severity data-integrity, security, backup/restore, installation/upgrade, privacy-exposure, or critical workflow defect remains open.
- Financial history can be reconstructed from preserved authoritative transactions.
- Finalized clinical records cannot be silently overwritten.
- Duplicate merge/import/export do not silently lose linked data.
- Backup recovery includes every persistent asset class actually used by the release.
- The product does **not** claim legal/regulatory certification solely because these engineering controls exist; external assessment remains separate.
