# Alveara Dental — Stories

18 stories across 5 releases, walking-skeleton first:
the earliest release proves the thinnest end-to-end path including the trust
spine, and later releases stack features on top of something already working.

## Before the releases — start here

- **[STORY-000](stories/STORY-000.md)** — Build your Command Center

The first thing you build, on day one, before any part of the system itself. It is
the page you keep open for the rest of the programme and demo from. It belongs to no
release and fulfils none of your requirements, because it is the window onto your
system rather than a part of it.

## r0 · Foundation and Security — weeks 1–4

**Goal:** Establish core security and foundational infrastructure.
**Done when you can show:** Demonstrate secure login, RBAC, and audit capabilities holding under concurrent access.

- **[STORY-001](stories/STORY-001.md)** — Implement secure user authentication and RBAC
- **[STORY-002](stories/STORY-002.md)** — Establish audit logging for critical actions

## r1 · Patient Management and Scheduling — weeks 5–8

**Goal:** Enable patient registration and scheduling workflows.
**Done when you can show:** Show end-to-end patient registration and scheduling with conflict prevention.

- **[STORY-003](stories/STORY-003.md)** — Implement patient registration workflow _(waits on STORY-002)_
- **[STORY-004](stories/STORY-004.md)** — Enable appointment scheduling with conflict prevention _(waits on STORY-002)_
- **[STORY-011](stories/STORY-011.md)** — Track patient flow states from scheduled to completed _(waits on STORY-004)_

## r2 · Clinical Documentation and Treatment Planning — weeks 9–12

**Goal:** Support clinical documentation and treatment planning.
**Done when you can show:** Demonstrate clinical documentation linked to treatment planning and odontogram updates.

- **[STORY-005](stories/STORY-005.md)** — Develop clinical documentation module _(waits on STORY-003)_
- **[STORY-006](stories/STORY-006.md)** — Implement interactive odontogram _(waits on STORY-005)_
- **[STORY-012](stories/STORY-012.md)** — Support periodontal charting with probing depth, recession, and bleeding _(waits on STORY-005)_
- **[STORY-013](stories/STORY-013.md)** — Allow structured diagnosis linked to patient, encounter, and treatment plan _(waits on STORY-005)_
- **[STORY-015](stories/STORY-015.md)** — Support treatment planning with diagnosis linkage, proposed procedures, and fee estimates _(waits on STORY-005)_

## r3 · Billing and Procedure Completion — weeks 13–16

**Goal:** Enable billing workflows and procedure completion.
**Done when you can show:** Complete a treatment plan, generate billing, and update patient financial history.

- **[STORY-007](stories/STORY-007.md)** — Enable billing workflow with financial history preservation _(waits on STORY-006)_
- **[STORY-008](stories/STORY-008.md)** — Complete treatment plan and update clinical history _(waits on STORY-006)_
- **[STORY-014](stories/STORY-014.md)** — Manage procedure/fee catalog with codes, descriptions, and fees _(waits on STORY-007)_
- **[STORY-016](stories/STORY-016.md)** — Support prescriptions with medication, dosage, and allergy checks _(waits on STORY-013)_

## r4 · Follow-up and Data Management — weeks 17–20

**Goal:** Support follow-up tasks, document management, and data portability.
**Done when you can show:** Manage follow-up tasks, import/export patient data, and categorize documents.

- **[STORY-009](stories/STORY-009.md)** — Manage follow-up tasks and reminders _(waits on STORY-008)_
- **[STORY-010](stories/STORY-010.md)** — Implement document import and categorization _(waits on STORY-008)_
- **[STORY-017](stories/STORY-017.md)** — Support duplicate detection and controlled merge of patient records _(waits on STORY-014)_
- **[STORY-018](stories/STORY-018.md)** — Provide data portability with patient export and import capabilities _(waits on STORY-017)_
