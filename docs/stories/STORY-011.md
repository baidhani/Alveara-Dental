# STORY-011 — Track patient flow states from scheduled to completed

As a receptionist, I want to track patient flow states from scheduled to completed, so that I can manage patient appointments efficiently.

**Release:** r1 · Patient Management and Scheduling (weeks 5–8)
**Owner:** Receptionist
**Blocked by:** STORY-004

## The requirement this satisfies

- **REQ-006** (Functional, must) — The system must track patient flow states from scheduled to completed, including check-in and treatment.

## How to build it

Implement state transitions for patient flow in the appointment module, ensuring each state change is logged.

## Failure paths you must handle

- Patient status fails to update on check-in.
- Patient status fails to update on treatment completion.
- System fails to log status change.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient is scheduled, when they check in, then the system updates their status to 'checked-in'.
- [ ] Given a patient is checked-in, when treatment is completed, then the system updates their status to 'completed'.
- [ ] Trust: All status changes are logged with timestamps and user IDs.

When every box above is ticked, stop and show the demo.
