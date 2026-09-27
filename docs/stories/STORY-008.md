# STORY-008 — Complete treatment plan and update clinical history

As a clinician, I want to complete treatment plans, so that patient care is accurately reflected in their clinical history.

**Release:** r3 · Billing and Procedure Completion (weeks 13–16)
**Owner:** Clinical Documentation Team
**Blocked by:** STORY-006

## The requirement this satisfies

- **REQ-013** (Functional, must) — The system must transition planned care into completed procedures, updating clinical history and billing.

## How to build it

Develop logic to transition treatment plans into completed procedures.

## Failure paths you must handle

- Incomplete treatment update
- Data loss
- Plan revision error
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a treatment plan is accepted, when completed, then the clinical history updates accordingly.
- [ ] Given a treatment plan is revised, when updated, then the original plan is preserved.
- [ ] Trust: All treatment plan changes are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
