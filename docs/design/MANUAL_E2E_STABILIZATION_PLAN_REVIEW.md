# Review: Manual E2E Stabilization Plan v2

**Reviewed package:** `C:\.alvera-data\Manual-E2E\alveara-manual-e2e-reviewer-package-v2-2026-11-09.zip`  
**Reviewed document:** `10-STABILIZATION-PLAN.md`  
**Decision:** `CHANGES_REQUIRED` — planning only; no production implementation is authorized from this proposal yet.

## What is approved in principle

The plan is a substantial improvement over the prior package. It correctly preserves raw finding IDs, identifies duplicates and withdrawn findings, captures the missed high-risk patient-context issue as `F-037`, incorporates the reviewer corrections for `F-091` and `F-104`, and gives the automated accumulated-state E2E chain a coverage map rather than treating it as an optional afterthought.

The goal is approved: complete stabilization before release engineering, then use the automated accumulated-state E2E chain as the repeatable release-validation journey. The plan must be revised before it can become execution authority.

## Required corrections

### 1. Retain the owner-controlled final manual E2E decision

Section 0 correctly records the owner's intended release sequence. The automated accumulated-state E2E chain is being built to replace repeated week-long manual runs during stabilization and routine readiness checks. It does not remove the owner's option to run one final manual E2E after all selected work is complete.

Keep the proposed release sequence, with this clarification:

- all selected stabilization stories close through the two-stage review process;
- the complete automated accumulated-state E2E chain passes with no release-blocking tracked exception;
- required focused regressions, real-backend walkthroughs, quality-gate evidence, and release-engineering checks pass;
- any additional owner-selected features are built, reviewed, and added to automated coverage; then
- the owner reviews the completed product and decides whether to run one final manual E2E before giving an explicit clear-to-go for release engineering.

The final manual E2E is a one-time owner-controlled release decision, not a recurring regression tier and not something to run after each stabilization story.

`STAB-22` may remain as a future placeholder. It does not delay work until the owner supplies and approves a concrete feature list.

### 2. Reduce the decision backlog to genuine product decisions

The proposed 34 decisions would freeze nearly all implementation. Retain decisions that materially set policy, clinical meaning, jurisdiction, or data model. Resolve ordinary UX and implementation choices in the story design.

In particular, do not make session extension a required outcome of `STAB-01`. The current fixed-expiry policy was an intentional security control. `STAB-01` must correct the erroneous sign-out on network failure, preserve work during reconnection where safe, distinguish first visit/expiry/server outage, and leave fixed expiry intact unless the owner separately chooses a policy change.

Likewise, a date-only correctness fix must not wait for a universal user-selectable date-format preference; the preference can be a separate follow-on after the patient-identification defect is corrected.

### 3. Split high-risk corrections from unrelated feature work

The proposed stories are too broad to review safely. Keep shared root causes together only where their acceptance and architecture are genuinely shared.

- Split `STAB-02` into staff/login lifecycle security (`F-021`) and later administrator account-onboarding/user-detail usability (`F-016`, `F-018`).
- Split `STAB-08` into date-only/time correctness (`F-107`, F-118 and related formatter evidence) and later date-format preference work.
- Keep the Calendar/Visit Board permission and transition enforcement in `STAB-03`; move developer-menu visibility and report filter usability to a separate lower-risk story.
- Split `STAB-05` into safety-summary freshness/clinical-record integrity and any clinical-policy change that needs its own decision.
- Split `STAB-10` into (a) receipt/statement idempotency and (b) aging/balance reconciliation and payer selection. These have different invariants and financial test evidence.

The release-blocking work should be narrow enough that an independent reviewer can establish the exact behavior, data invariants, and regression impact from one handoff package.

### 4. Correct uncertain-finding dispositions

`F-015` occurred around a runtime update during the manual run. It is not yet a confirmed production defect. Mark it `NOT_REPRODUCED / INVESTIGATE`, require reproduction with preserved evidence, and do not make speculative health/auth behavior an acceptance criterion.

`F-044` may remain withdrawn as an original prefill defect, but the separate question of a silent 60-minute default needs a distinct design decision and canonical key. Do not bury it under a withdrawn finding.

`F-038/F-039` need a separate audit/compliance design. Do not make broad patient-chart-view logging an assumed fix before defining purpose, readers, retention, and noise controls.

`F-091` and `F-104` retain the reviewer dispositions already recorded: planned/diagnosed missing-tooth coexistence requires a defined clinical meaning; the 14/26 issue is display numbering only.

### 5. Make the automated chain an effective replacement for the manual pass

The coverage-map requirement is correct. Strengthen it as follows:

- Every automated test retains its manual source-step ID and produces machine-readable pass/fail evidence.
- Use run-specific identifiers; names alone are insufficient.
- The runner owns the database, document storage, configuration, clock, communications stubs, evidence directory, schema version, and implementation SHA for the whole run.
- A resume is diagnostic evidence only, never evidence of a complete uninterrupted run.
- A release-blocking known defect cannot be represented as an expected green result. It must fail or appear as a blocking tracked exception.
- Manual-only checks must be narrowly justified visual or subjective checks. They do not recreate the full manual test week.
- Build the runner and the stages needed by each selected correction story in sequence. Do not require 14 separate review cycles merely to construct test infrastructure; include the relevant chain stage with its correction story where practical, then run the complete chain after the selected stabilization set closes.

The fixed clock must be test-only and explicitly injected; it must not become a production setting or accidentally control authentication expiry/retry behavior.

### 6. Fix dependency order

Read-only dependency/advisory investigation can start early, but documentation cleanup and optional npm tooling work must not delay `F-037`, `F-021`, session/outage correction, or treatment-follow-up integrity work.

The revised dependency order should begin with backlog normalization, then narrowly scoped identity/session and staff-login lifecycle corrections, followed by visit-transition rules, date correctness, clinical/scheduling integrity, billing correctness, and the remaining operational/usability work.

## Required next package

Return `R02` of the stabilization plan with:

1. a reduced, dependency-ordered story list containing only bounded reviewable stories;
2. the clarified release gate: the automated chain replaces repeated manual regression runs, while the owner retains the final one-time manual E2E decision after all selected work is complete;
3. a decision register limited to actual product/policy decisions;
4. corrected dispositions for `F-015` and the silent-default question behind `F-044`; and
5. a coverage map showing how each selected manual step becomes automated evidence or a narrowly justified manual-only check.

Do not modify the execution plan or implementation code until this corrected plan is independently approved. After approval, the execution plan must be updated with the authorized stabilization stories before coding begins.
