# Binding Testing Cadence Notice

**Effective:** 2026-10-13  
**Authority:** `GPT Docs/Alveara_Dental_Execution_Plan_v4.md`, `GPT Docs/Alveara_Dental_Engineering_Product_Reference_v2.md`, and `.alveara/HANDOFF_SCHEMA.md`

## Purpose

Use focused evidence on every story and reserve the most expensive broad regression runs for a controlled cadence. This does not relax any story acceptance criterion, parent-course contract, quality gate, or explicit testing requirement in an individual execution-plan prompt.

## Required before every story handoff

Run and record:

- the story's acceptance tests;
- exact parent-course regression for a companion story, or justified `N/A` for a new-production story;
- material failure-path, authorization, data-integrity, and concurrency tests that apply;
- targeted integration tests for every changed subsystem; and
- any test, walkthrough, or evidence expressly required by that story's prompt.

`TEST_RESULTS.md` must name the exact commands and pass/fail/skip results. It must also declare whether the story is low/medium risk or high risk, and why.

## Broad-regression cadence

For low/medium-risk stories only, full backend, frontend, repository-check, and mocked-browser regression may be deferred, but no more than three eligible stories may pass between successful broad regressions. The third eligible story must run the broad regression before it is packaged for review.

A full regression is immediately required when a story changes any of the following:

- migrations or database schema;
- authentication, authorization, audit, transactions, idempotency, or concurrency primitives;
- money or billing;
- backup/restore or encryption;
- shared middleware, runtime, production build, or deployment configuration; or
- test infrastructure.

A prompt that explicitly requires a full suite is stricter than this cadence and must be followed.


## Two-stage review timing

For every story, milestone, release, and gate beginning after `ALV-010-C01` that requires broad regression under this cadence, Stage 1 carries all required focused evidence and a frozen broad-verification plan while broad regression is pending. After independent `APPROVED_PENDING_REGRESSION`, the coding agent runs the exact Stage 2 contract on code-equivalent approved work. Final `APPROVED` remains mandatory before `COMPLETE` or a gate/release PASS decision. Product/test failures require a new attempt or responsible-work correction; environment-only inconclusive runs are `BLOCKED` and may rerun unchanged. A reviewer may require single-stage evidence only when broad regression is itself required to assess a specific acceptance criterion before implementation review, and must record that reason. See `.alveara/HANDOFF_SCHEMA.md` v3.

## Gates, releases, and milestones

At every quality gate, release, or declared milestone, run the broad regression plus the required real-backend walkthroughs and all gate-specific evidence. Real-backend walkthroughs are therefore not routine substitutes for targeted per-story testing.

## Current application: ALV-008-C02

ALV-008-C02 is high risk. It changes a migration/schema, the authoritative completion workflow, billing integration, odontogram behavior, retired production routes, and parent-test infrastructure. Run the full backend, frontend, repository-check, and mocked-browser regression before its handoff, in addition to its required real-browser adoption/retirement walkthrough and all story-specific evidence.
