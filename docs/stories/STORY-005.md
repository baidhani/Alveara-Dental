# STORY-005 — Develop clinical documentation module

As a clinician, I want to document patient encounters, so that clinical history is accurately recorded.

**Release:** r2 · Clinical Documentation and Treatment Planning (weeks 9–12)
**Owner:** Clinical Documentation Team
**Blocked by:** STORY-003

## The requirement this satisfies

- **REQ-007** (Functional, must) — The system must support structured clinical documentation including medical and dental history, allergies, and medications.

## How to build it

Create structured forms for medical and dental history documentation.

## Failure paths you must handle

- Incomplete documentation
- Amendment failure
- Data loss
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient encounter, when documented, then the system records medical and dental history.
- [ ] Given an encounter note is finalized, when an amendment is needed, then the original is preserved with an addendum.
- [ ] Trust: All documentation changes are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
