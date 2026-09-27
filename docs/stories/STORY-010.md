# STORY-010 — Implement document import and categorization

As an admin, I want to import and categorize documents, so that patient records are comprehensive and organized.

**Release:** r4 · Follow-up and Data Management (weeks 17–20)
**Owner:** Document Management Team
**Blocked by:** STORY-008

## The requirement this satisfies

- **REQ-015** (Functional, must) — The system must import and categorize documents and forms, supporting e-signature and metadata linkage.

## How to build it

Develop import logic for documents and forms with metadata linkage.

## Failure paths you must handle

- Incorrect categorization
- Metadata error
- E-signature failure
- Audit failure
- Concurrency issues

## Acceptance — your stop condition

Tick each box as it genuinely passes. This file is yours — the platform reads
the same criteria out of `.colaberry/progress.json`, which Claude Code keeps in
step (see the managed block in CLAUDE.md). Ticking something you have not
actually met only misleads you.

- [ ] Given a document is imported, when categorized, then it is linked with the correct metadata.
- [ ] Given a form is e-signed, when completed, then the signed version is preserved.
- [ ] Trust: All document actions are logged with user and timestamp.

When every box above is ticked, stop and show the demo.
