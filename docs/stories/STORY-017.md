# STORY-017 — Support duplicate detection and controlled merge of patient records

As a system administrator, I want to detect and merge duplicate patient records, so that I can maintain accurate patient data.

**Release:** r4 · Follow-up and Data Management (weeks 17–20)
**Owner:** System Administrator
**Blocked by:** STORY-014

## The requirement this satisfies

- **REQ-018** (Functional, must) — The system must support duplicate detection and controlled merge of patient records, preserving linked data.

## How to build it

Implement duplicate detection algorithms and a controlled merge interface, ensuring data integrity and logging.

## Failure paths you must handle

- Duplicate records are not detected.
- Merge conflicts are not resolved correctly.
- System fails to log merge actions.

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given duplicate patient records, when detected, then the system suggests a controlled merge.
- [ ] Given a merge conflict, when resolved, then the system preserves linked data.
- [ ] Trust: All merge actions are logged with user ID and timestamp.

When every box above is ticked, stop and show the demo.
