# STORY-012 — Support periodontal charting with probing depth, recession, and bleeding

As a dentist, I want to perform periodontal charting with probing depth, recession, and bleeding, so that I can accurately assess patient periodontal health.

**Release:** r2 · Clinical Documentation and Treatment Planning (weeks 9–12)
**Owner:** Dentist
**Blocked by:** STORY-005

## The requirement this satisfies

- **REQ-009** (Functional, must) — The system must support periodontal charting with probing depth, recession, and bleeding.

## How to build it

Develop a periodontal charting interface within the clinical documentation module, ensuring data validation and logging.

## Failure paths you must handle

- Incorrect data is accepted in charting.
- Charting data fails to save.
- System fails to log charting entries.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient record, when periodontal charting is performed, then the system records probing depth, recession, and bleeding.
- [ ] Given incorrect charting data, when saved, then the system rejects the input and prompts for correction.
- [ ] Trust: All charting entries are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
