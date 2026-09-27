# STORY-013 — Allow structured diagnosis linked to patient, encounter, and treatment plan

As a clinician, I want to record structured diagnoses linked to patient, encounter, and treatment plan, so that I can maintain comprehensive patient records.

**Release:** r2 · Clinical Documentation and Treatment Planning (weeks 9–12)
**Owner:** Clinician
**Blocked by:** STORY-005

## The requirement this satisfies

- **REQ-010** (Functional, must) — The system must allow structured diagnosis linked to patient, encounter, and treatment plan.

## How to build it

Integrate diagnosis entry into the clinical documentation module, ensuring linkage to patient and treatment plan.

## Failure paths you must handle

- Diagnosis fails to link to patient record.
- Incorrect diagnosis data is accepted.
- System fails to log diagnosis entries.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient encounter, when a diagnosis is recorded, then it is linked to the patient and treatment plan.
- [ ] Given a diagnosis entry error, when saved, then the system prompts for correction.
- [ ] Trust: All diagnosis entries are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
