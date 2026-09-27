# STORY-004 — Enable appointment scheduling with conflict prevention

As a scheduler, I want to schedule appointments without conflicts, so that provider and operatory availability is respected.

**Release:** r1 · Patient Management and Scheduling (weeks 5–8)
**Owner:** Scheduling Team
**Blocked by:** STORY-002

## The requirement this satisfies

- **REQ-005** (Functional, must) — The system must support scheduling with multiple providers, operatories, appointment types, and configurable durations.

## How to build it

Implement scheduling logic to prevent provider and operatory conflicts.

## Failure paths you must handle

- Double booking
- Unavailable provider
- Operatory conflict
- Audit failure
- Incorrect duration

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a provider is available, when an appointment is scheduled, then it is confirmed without conflicts.
- [ ] Given a provider is double-booked, when a new appointment is attempted, then the system rejects it.
- [ ] Trust: All scheduling actions are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
