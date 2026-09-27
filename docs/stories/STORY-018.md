# STORY-018 — Provide data portability with patient export and import capabilities

As a data manager, I want to export and import patient data, so that I can ensure data portability and compliance.

**Release:** r4 · Follow-up and Data Management (weeks 17–20)
**Owner:** Data Manager
**Blocked by:** STORY-017

## The requirement this satisfies

- **REQ-019** (Functional, must) — The system must provide data portability with patient export and import capabilities.

## How to build it

Develop export and import functionalities for patient data, ensuring format compliance and logging.

## Failure paths you must handle

- Exported data is not in a portable format.
- Incorrect import files are accepted.
- System fails to log export/import actions.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient record, when exported, then the system provides a portable data format.
- [ ] Given an incorrect import file, when processed, then the system rejects it and prompts for correction.
- [ ] Trust: All export and import actions are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
