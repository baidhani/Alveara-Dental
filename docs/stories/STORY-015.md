# STORY-015 — Support treatment planning with diagnosis linkage, proposed procedures, and fee estimates

As a dentist, I want to create treatment plans with diagnosis linkage, proposed procedures, and fee estimates, so that I can provide patients with comprehensive care plans.

**Release:** r2 · Clinical Documentation and Treatment Planning (weeks 9–12)
**Owner:** Dentist
**Blocked by:** STORY-005

## The requirement this satisfies

- **REQ-012** (Functional, must) — The system must support treatment planning with diagnosis linkage, proposed procedures, and fee estimates.

## How to build it

Integrate treatment planning into the clinical documentation module, ensuring linkage to diagnosis and fee estimates.

## Failure paths you must handle

- Treatment plan fails to link to diagnosis.
- Incorrect treatment plan data is accepted.
- System fails to log treatment plan entries.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient diagnosis, when creating a treatment plan, then it links to proposed procedures and fee estimates.
- [ ] Given an error in treatment plan entry, when saved, then the system prompts for correction.
- [ ] Trust: All treatment plan entries are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
