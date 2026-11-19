# ALV-N025 Briefing R02 — Independent Review

**Decision:** CHANGES_REQUIRED

**Reviewed package:** `Alveara_Briefing_ALV-N025_203aff4_R02.zip`  
**Package SHA-256:** `501A6AD84B9E8448E49891B0D7862B91FC4CF1F6CB372A41755135CC83E38A7F`  
**Scope:** documents-only briefing review. No code, schema, configuration, or test run is authorized.

## R01 corrections resolved

1. **BR-01 resolved in design.** R02 replaces the infeasible expired-cookie observation with an audit-only `AuditSessions` lifecycle record, conditional terminal-state transitions, an idempotent server-owned expiry processor, explicit-sign-out suppression, and revocation handling. Authentication remains governed solely by the fixed cookie deadline and security stamp; the audit record cannot authorize, prolong, or revive a session.
2. **BR-02 resolved in design.** Audit data remains PHI-free; it carries only field names and an opaque history-entry reference. The History resource keeps its independent patient-record permission, the UI withholds the link without that permission, and planned tests cover both direct API and direct-navigation denial.

## Required correction

### BR-03 — Remove the superseded expired-cookie test assertion

Section 8 still lists this old R01 requirement in `PatientAccessTrailTests`: “expiry when the expired cookie returns.” R02 correctly withdrew that mechanism in section 0 and replaced it with the durable lifecycle processor in section 4. Retaining both is contradictory and could cause implementation of the very unobservable expired-ticket behavior R01 rejected.

Replace that phrase with a lifecycle-processor assertion, for example: “expiry is written by the audit-session processor at the fixed deadline, independently of a returning cookie.” Keep the detailed `AuditSessionLifecycleTests` proof as the authoritative expiry test. No code, migration, test execution, or other document change is needed for this correction.

## Resubmission boundary

Return a documents-only R03 correcting BR-03. Once it is accepted, implementation may start within the approved scope and focused-testing policy.
