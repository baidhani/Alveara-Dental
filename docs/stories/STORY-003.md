# STORY-003 — Implement patient registration workflow

As a front desk staff, I want to register patients, so that their information is accurately captured in the system.

**Release:** r1 · Patient Management and Scheduling (weeks 5–8)
**Owner:** Patient Management Team
**Blocked by:** STORY-002

## The requirement this satisfies

- **REQ-004** (Functional, must) — The system must allow patient registration with demographics, contact information, and family/household relationships.

## How to build it

Develop patient registration forms capturing demographics and contact information.

## Failure paths you must handle

- Incomplete registration
- Duplicate entry
- Data validation failure
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [x] Given a new patient, when they provide their information, then the system captures demographics and contact details.
- [x] Given a patient with incomplete information, when they attempt to register, then the system prompts for required fields.
- [x] Trust: All registration actions are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
