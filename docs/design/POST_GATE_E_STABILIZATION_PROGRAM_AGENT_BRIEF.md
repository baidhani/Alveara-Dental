# Post-Gate-E Stabilization Program — Agent Brief

Date: 2026-11-09

## Authority and current authorization

The product owner adopted the ten-package Post-Gate-E Stabilization Program. The authoritative execution order and binding working rules are in `GPT Docs/Alveara_Dental_Execution_Plan_v4.md`. The detailed source plan is `18-STABILIZATION-PLAN-R05.md` in `C:\.alvera-data\Manual-E2E\alveara-manual-e2e-reviewer-package-v6-2026-11-09.zip`; its independent approval is recorded in `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05_REVIEW.md`.

Only **ALV-N019** is currently `READY`. All later stabilization packages and all release-engineering work remain unavailable. Do not begin `ALV-N020` or any later item. Do not begin `ALV-N013`, `ALV-N014`, or `ALV-N008`.

The active sequence is:

| Item | ID | Package |
|---:|---|---|
| 56 | ALV-N019 | DentalKeep brand migration, stabilization foundation, and automated chain harness |
| 57 | ALV-N020 | Identity, session, and staff lifecycle integrity |
| 58 | ALV-N021 | Date, scheduling, and visit-transition integrity |
| 59 | ALV-N022 | Clinical safety, record integrity, finalization, and addenda |
| 60 | ALV-N023 | Treatment-plan, scheduling, follow-up, and revision integrity |
| 61 | ALV-N024 | Financial issuance and reconciliation integrity |
| 62 | ALV-N025 | Access, audit, patient identity, import, and merge integrity |
| 63 | ALV-N026 | Documents, prescriptions, printing, and backup/restore integrity |
| 64 | ALV-N027 | Messaging, follow-ups, reports, navigation, configuration, and onboarding |
| 65 | ALV-N028 | Complete automated accumulated-state chain and stabilization exit evidence |

`ALV-N028` does not itself authorize deployment. Release engineering remains paused until the owner selects and closes any additional features, completes one owner-timed final manual E2E, and explicitly clears the transition.

## Mandatory briefing before every package

Before changing code, tests, schema, configuration, UI, documentation, or runtime assets for **every** package above, create:

`docs/design/<ALV-ID>_PRE_IMPLEMENTATION_BRIEF.md`

Then stop and submit that briefing for independent review. Do not start implementation, create an implementation handoff, or run an expensive regression until the briefing is approved.

Each briefing must contain all of the following:

1. The intended user-visible and technical result.
2. A detailed itemized description of every planned creation, modification, removal, redesign, migration, compatibility effect, and retained behavior.
3. For each fix, the observed behavior, expected behavior, likely root cause, and how the proposed change corrects it.
4. The full list of finding, question, observation, design-note, and manual-step identifiers addressed by the package.
5. The **complete raw-log entry** for every listed identifier, copied from the approved Manual E2E source with its heading, context, observation, result, severity/notes, and source location. A summary or identifier-only list is not acceptable.
6. A source-to-acceptance table showing the raw item, intended acceptance criterion, test type, exact expected assertion, and resulting evidence location.
7. Affected permissions, authorization boundaries, audit/history behavior, data/migration effects, integrations, accessibility, recovery, performance, and release impacts.
8. Changed-file inventory based on the actual repository, with a clear distinction between expected changes and areas inspected but intentionally untouched.
9. Focused Stage 1 tests and, when broad regression is required, the frozen Stage 2 contract. Follow the existing two-stage and change-impact-selection policy. Do not run the Stage 2 contract before review approval.
10. Open decisions that need owner, clinical, compliance, or reviewer input. Do not silently select a product policy that the R05 decision register leaves open.

The mandatory raw source is the v6 package above, especially `manual-e2e-findings.md`, `01-test-cases.md`, and `18-STABILIZATION-PLAN-R05.md`. If that source is unavailable, stop and report the blocker instead of reconstructing or abbreviating its findings.

## DentalKeep identity decision

The public product name is now **DentalKeep**.

For new and changed public-facing work, use DentalKeep in application copy, visual branding, product metadata, release/install/operator documentation, screenshots, and public-facing test expectations.

Do not mass-rename technical or historical identifiers. Preserve course artifacts, immutable handoffs/reviews, `ALV-*` identifiers, `Alveara_Handoff_*` package names, database names, schemas, namespaces, assembly names, persisted keys, and compatibility-sensitive paths unless a later reviewed design explicitly authorizes a safe migration.

ALV-N019 must begin with a complete brand inventory that classifies every proposed occurrence as:

- public-facing change to DentalKeep;
- historical/control evidence retained as Alveara; or
- compatibility-sensitive identifier retained pending a separately reviewed migration.

The ALV-N019 briefing must identify every user-visible Alveara Dental occurrence and its proposed DentalKeep replacement. It must also list every intentionally retained occurrence and why it remains unchanged. Do not apply any rename before that briefing is approved.

## Immediate required action: ALV-N019 briefing only

Prepare `docs/design/ALV-N019_PRE_IMPLEMENTATION_BRIEF.md` for review. Its scope is R05 PKG-01 and PKG-15 plus the DentalKeep brand inventory. Address F-201 and every applicable source-map/runner/provenance obligation from R05. Explain the controlled chain environment, tracked-exception register, runner safety boundaries, dependency advisory process, brand inventory, and the exact focused tests proposed.

Do not implement the runner, change the product name, update execution status, or run a full suite in this step. Submit the briefing and wait for its review decision.
