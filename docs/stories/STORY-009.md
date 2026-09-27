# STORY-009 — Manage follow-up tasks and reminders

As a clinician, I want to manage follow-up tasks, so that patient care is continuous and timely.

**Release:** r4 · Follow-up and Data Management (weeks 17–20)
**Owner:** Follow-up Team
**Blocked by:** STORY-008

## The requirement this satisfies

- **REQ-017** (Functional, must) — The system must enable follow-up tasks with recall intervals, reminders, and completion history.

## How to build it

Implement task management logic for follow-up and reminders.

## Failure paths you must handle

- Missed reminder
- Task completion error
- Data entry error
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a follow-up task is created, when due, then a reminder is sent to the responsible party.
- [ ] Given a task is completed, when marked, then the completion history is updated.
- [ ] Trust: All follow-up actions are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
