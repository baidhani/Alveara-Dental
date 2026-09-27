# STORY-007 — Enable billing workflow with financial history preservation

As a billing specialist, I want to manage billing, so that financial transactions are accurately recorded and preserved.

**Release:** r3 · Billing and Procedure Completion (weeks 13–16)
**Owner:** Billing Team
**Blocked by:** STORY-006

## The requirement this satisfies

- **REQ-014** (Functional, must) — The system must handle billing with charges, payments, and adjustments, preserving financial history.

## How to build it

Implement billing logic to handle charges, payments, and adjustments.

## Failure paths you must handle

- Incorrect billing amount
- Payment misallocation
- Adjustment error
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a procedure is completed, when billed, then the charge is recorded with the correct amount.
- [ ] Given a payment is made, when recorded, then the balance updates and history is preserved.
- [ ] Trust: All billing actions are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
