# Alveara Dental — Requirements

A modern dental EHR and PMS for general dentistry, with core clinical and administrative functions usable offline, and a client-independent domain model for future expansion.

This is the source of truth for what you are building. Your Claude Code prompts
point here. If you sharpen a requirement, edit it — your version is the real one.

| Kind | Meaning |
|---|---|
| Functional | something the system does |
| Safety | a guardrail, with a check that enforces it |
| Reliability | how it behaves when something fails |
| Constraint | a technology or vendor you must use — context, not a task |

## Audit

### REQ-003 — Safety · must

The system must audit account and role changes with user and timestamp.

Fulfilled by: STORY-002

## Billing

### REQ-014 — Functional · must

The system must handle billing with charges, payments, and adjustments, preserving financial history.

Fulfilled by: STORY-007

## Clinical Documentation

### REQ-007 — Functional · must

The system must support structured clinical documentation including medical and dental history, allergies, and medications.

Fulfilled by: STORY-005

## Data Portability

### REQ-019 — Functional · must

The system must provide data portability with patient export and import capabilities.

Fulfilled by: STORY-018

## Deployment

### REQ-020 — Constraint

The system must support local Windows server deployment with a responsive web client usable offline.

Context for the stories that use it — constraints do not get their own story.

## Diagnosis

### REQ-010 — Functional · must

The system must allow structured diagnosis linked to patient, encounter, and treatment plan.

Fulfilled by: STORY-013

## Documents/Forms

### REQ-015 — Functional · must

The system must import and categorize documents and forms, supporting e-signature and metadata linkage.

Fulfilled by: STORY-010

## Duplicate Merge

### REQ-018 — Functional · must

The system must support duplicate detection and controlled merge of patient records, preserving linked data.

Fulfilled by: STORY-017

## Follow-up/Tasks

### REQ-017 — Functional · must

The system must enable follow-up tasks with recall intervals, reminders, and completion history.

Fulfilled by: STORY-009

## Odontogram

### REQ-008 — Functional · must

The system must provide an interactive odontogram with tooth and surface selection, supporting existing, diagnosed, planned, and completed states.

Fulfilled by: STORY-006

## Patient Flow

### REQ-006 — Functional · must

The system must track patient flow states from scheduled to completed, including check-in and treatment.

Fulfilled by: STORY-011

## Patient Registration

### REQ-004 — Functional · must

The system must allow patient registration with demographics, contact information, and family/household relationships.

Fulfilled by: STORY-003

## Periodontal Charting

### REQ-009 — Functional · must

The system must support periodontal charting with probing depth, recession, and bleeding.

Fulfilled by: STORY-012

## Prescriptions/Safety

### REQ-016 — Functional · must

The system must support prescriptions with medication, dosage, and allergy checks.

Fulfilled by: STORY-016

## Procedure Completion

### REQ-013 — Functional · must

The system must transition planned care into completed procedures, updating clinical history and billing.

Fulfilled by: STORY-008

## Procedure/Fee Catalog

### REQ-011 — Functional · must

The system must manage a procedure/fee catalog with codes, descriptions, and fees.

Fulfilled by: STORY-014

## Scheduling

### REQ-005 — Functional · must

The system must support scheduling with multiple providers, operatories, appointment types, and configurable durations.

Fulfilled by: STORY-004

## Security

### REQ-001 — Safety · must

The system must support unique user accounts with secure password storage and configurable session timeout.

Fulfilled by: STORY-001

### REQ-002 — Safety · must

The system must provide role-based access control (RBAC) for dentist, hygienist, assistant, front desk, billing, office manager, and admin roles.

Fulfilled by: STORY-001

## Treatment Planning

### REQ-012 — Functional · must

The system must support treatment planning with diagnosis linkage, proposed procedures, and fee estimates.

Fulfilled by: STORY-015
