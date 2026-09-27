# STORY-016 — Support prescriptions with medication, dosage, and allergy checks

As a clinician, I want to prescribe medications with dosage and allergy checks, so that I can ensure patient safety.

**Release:** r3 · Billing and Procedure Completion (weeks 13–16)
**Owner:** Clinician
**Blocked by:** STORY-013

## The requirement this satisfies

- **REQ-016** (Functional, must) — The system must support prescriptions with medication, dosage, and allergy checks.

## How to build it

Develop a prescription module with allergy and dosage checks, ensuring data validation and logging.

## Failure paths you must handle

- Allergy check fails during prescription.
- Incorrect dosage is accepted.
- System fails to log prescription entries.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a patient record, when prescribing medication, then the system checks for allergies and dosage accuracy.
- [ ] Given an incorrect prescription entry, when saved, then the system prompts for correction.
- [ ] Trust: All prescription entries are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
