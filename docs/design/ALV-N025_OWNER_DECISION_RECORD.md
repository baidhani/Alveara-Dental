# ALV-N025 owner decision record

**Date:** 2026-11-19. **Source:** the owner's answers to the decisions in `ALV-N025_FINDING_SURVEY.md` (question tool), quoted as given. Where the owner chose a listed option, the option's text is what was chosen.

| Id | Decision | Owner's words / chosen option |
|---|---|---|
| PD-20 (F-038, F-039, Q-5cf; compliance) | Record opens, exports and sign-out/expiry | Chosen option: "Record when a user opens a patient record (once per patient per session, not every page), and every export, print and download, plus sign-out and session expiry. Readable by the audit-log role; kept as long as the audit log. A design document approved before code." |
| D-02a / F-036 | A required reason for a near-match override | Chosen option: "A short required reason before registering anyway; the audit records the matched patient ids and the reason (no names)." |
| F-041 | Link the audit to the History row | Chosen option: "Keep values out of the audit; the audit entry names the fields and links to the patient's own History row (which already keeps old and new values, readable only by those who may see the patient)." |
| F-147 | Fix the wording only | Chosen option: "Name exactly which file can be imported (the single patient file) and say the package is for reading/archiving, not re-import. No new import format." |
| U-011 | Assigned to ALV-N025 | Chosen: "U-011" (a follow-up shows the login name instead of the person; the same root cause as the audit/history actor-name fixes). U-008 is **not** assigned here. |

## PD-21: Medical Record Number (MRN), owner's words
> Keep the existing GUID as the patient's internal primary key and API/database relationship identifier. Do not replace it.
>
> Add a separate, human-readable numeric **Medical Record Number (MRN)** linked one-to-one to the GUID. The patient search experience must support exact MRN lookup.
>
> **Generation**
> 1. When registering a patient, the server obtains the current UTC whole-second value: `DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond`
> 2. The server attempts to assign that numeric value as the MRN.
> 3. If the value already exists, the server increments the candidate by one second and retries until it finds an unused MRN.
> 4. The database must enforce a unique constraint/index on MRN. The server must safely retry after a uniqueness conflict so concurrent registrations cannot result in duplicate MRNs.
> 5. The system must never wait for the next second and must not add a visible suffix such as `-01`.
>
> **Rules**
> - MRNs are immutable and never reused.
> - Existing patients receive deterministic, unique backfilled MRNs that do not conflict with newly created records.
> - An MRN is an operational lookup and reference identifier, not authentication or proof of identity.
> - `CreatedAtUtc` remains the authoritative creation timestamp. Collision handling may make an MRN slightly later than the actual registration second.
> - On patient merge, the surviving patient keeps their MRN. The merged-away MRN remains permanently reserved and must not be reassigned.
> - Display the MRN in the patient header, permitted patient documents and exports, and search results, subject to existing access permissions.

**Fact for the brief:** `Ticks / TicksPerSecond` counts seconds since 0001-01-01, so a 2026 MRN has **11 digits** (about 63,9xx,xxx,xxx). The column is a 64-bit integer. Imported patients are registrations, so they receive MRNs the same way.

## F-032 / F-033: value lists and Country, owner's words
> - Use the listed Sex values: Female, Male, Other, Unknown/unspecified.
> - Add a Country field, preferably using standard country codes internally.
> - For USA, provide the states/DC/territories dropdown stored as two-letter codes.
> - For other countries, label the field State / Province / Region and permit free text.
> - Default country should come from the deployment/practice setting when available; otherwise default to USA.
> - Migrate recognizable existing entries; surface unmappable values for staff review rather than silently changing them.

## What the brief must settle from these
- **PD-20:** a separate design section (purpose, event shape, what "once per patient per session" means server-side, readers, retention, noise controls, PHI-free detail), approved before any logging code. This is a compliance boundary (R05: escalate).
- **PD-21:**
  - the backfill rule: deterministic, from `CreatedAtUtc` in creation order, with collisions moved forward;
  - the concurrency proof for "safe retry";
  - "never reused" across merges and unmerges (a reserved table or the merged row keeping its MRN);
  - search, header, document and export placements;
  - import (an imported patient gets a new MRN, not the source's).
  - This is a data-model boundary (R05: escalate).
- **F-032 / F-033:** the ISO country code (alpha-2) column, the practice-setting default, the mapping of existing Sex and State values, and a review list for unmappable ones; the import and export columns.
- **U-011:** the shared actor-name rule (staff display name, else username, never a bare id; the id kept for auditors) applied across the audit page, change histories, fee and document history, the encounter history and follow-ups.

## Briefing decisions (2026-11-19)
| Id | Decision | Owner's words |
|---|---|---|
| OQ-1 (PD-20 prints) | Record prints | "Record a PatientMaterialPrinted audit event when a user uses an in-application Print button. The event records the print request/intent, not a claim that physical printing succeeded. Native browser printing outside the application's buttons (for example, Ctrl+P) is outside this guarantee. Include actor, patient/resource ID, material type, timestamp, and outcome; keep it PHI-free." |
| OQ-2 (MRN on printed documents) | In ALV-N026 | "ALV-N025 should add MRN to headers, search, and exports. ALV-N026 should version and update printed patient documents, statements, receipts, prescriptions, and their compatibility tests. This avoids an unnecessary document-template expansion inside N025." |
| OQ-5 (one-member household) | Dissolve | "When only one patient remains, dissolve the household and audit the action. Clear the remaining patient's household membership and set their relationship to Self (or the equivalent neutral non-household state)." |
| OQ-3 (blocked duplicates) | Do not log | "Leave routine validation refusals out of the audit log to avoid noisy, low-value records. The deliberate "Register anyway" duplicate override remains audited with its required reason and matched record IDs." |

