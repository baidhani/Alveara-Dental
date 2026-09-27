# STORY-002 — Establish audit logging for critical actions

As a system auditor, I want audit logging for critical actions, so that changes are traceable.

**Release:** r0 · Foundation and Security (weeks 1–4)
**Owner:** Audit Team
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-003** (Safety, must) — The system must audit account and role changes with user and timestamp.

## How to build it

Set up audit logging for account and role changes, ensuring immutability.

## Failure paths you must handle

- Unauthorized log modification
- Audit log corruption
- Missing audit entries
- Incorrect timestamp
- Unauthorized access

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a user changes their role, when the change is saved, then the action is logged with a timestamp.
- [ ] Given a user attempts to modify an audit log, when they do not have permission, then the action is denied.
- [ ] Trust: Audit logs are immutable and protected from unauthorized modification.

When every box above is ticked, stop and show the demo.
