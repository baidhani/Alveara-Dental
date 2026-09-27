# STORY-014 — Manage procedure/fee catalog with codes, descriptions, and fees

As a billing manager, I want to manage a procedure/fee catalog with codes, descriptions, and fees, so that I can ensure accurate billing.

**Release:** r3 · Billing and Procedure Completion (weeks 13–16)
**Owner:** Billing Manager
**Blocked by:** STORY-007

## The requirement this satisfies

- **REQ-011** (Functional, must) — The system must manage a procedure/fee catalog with codes, descriptions, and fees.

## How to build it

Develop a procedure/fee catalog management interface, ensuring data validation and logging.

## Failure paths you must handle

- Procedure fails to save in the catalog.
- Incorrect procedure data is accepted.
- System fails to log catalog changes.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a new procedure, when added to the catalog, then it includes a code, description, and fee.
- [ ] Given an incorrect procedure entry, when saved, then the system prompts for correction.
- [ ] Trust: All catalog changes are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
