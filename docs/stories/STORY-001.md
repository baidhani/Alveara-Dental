# STORY-001 — Implement secure user authentication and RBAC

As an admin, I want secure user authentication and role-based access control, so that user access is appropriately managed.

**Release:** r0 · Foundation and Security (weeks 1–4)
**Owner:** Security Team
**Blocked by:** nothing — you can start this now

## The requirement this satisfies

- **REQ-001** (Safety, must) — The system must support unique user accounts with secure password storage and configurable session timeout.
- **REQ-002** (Safety, must) — The system must provide role-based access control (RBAC) for dentist, hygienist, assistant, front desk, billing, office manager, and admin roles.

## How to build it

Implement user authentication using secure password storage and RBAC for defined roles.

## Failure paths you must handle

- Incorrect password entry
- Account lockout
- Role misassignment
- Audit failure
- Session timeout

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a new user, when they register, then their account is created with secure password storage.
- [ ] Given a user, when they attempt to log in with incorrect credentials multiple times, then their account is locked.
- [ ] Trust: All account and role changes are audited with user and timestamp.

When every box above is ticked, stop and show the demo.
