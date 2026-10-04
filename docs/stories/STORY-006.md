# STORY-006 — Implement interactive odontogram

As a dentist, I want an interactive odontogram, so that I can accurately record dental conditions and treatments.

**Release:** r2 · Clinical Documentation and Treatment Planning (weeks 9–12)
**Owner:** Clinical Documentation Team
**Blocked by:** STORY-005

## The requirement this satisfies

- **REQ-008** (Functional, must) — The system must provide an interactive odontogram with tooth and surface selection, supporting existing, diagnosed, planned, and completed states.

## How to build it

Develop an interactive odontogram interface for recording dental conditions.

## Failure paths you must handle

- Incorrect tooth selection
- Data entry error
- Odontogram update failure
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [x] Given a tooth is selected, when a condition is recorded, then it is saved with the correct lifecycle state.
- [x] Given a completed treatment, when recorded, then the odontogram updates to reflect the change.
- [x] Trust: All odontogram updates are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
