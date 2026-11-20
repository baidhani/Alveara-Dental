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


## Implementation decisions (2026-11-19, slice 3)
| Id | Decision | Owner's words (abridged only where marked) |
|---|---|---|
| MIG-1 (migration packaging) | Three forward migrations | "Use three forward migrations: AddPatientMrn ... AddPatientCountry ... AddPatientAccessAudit ... Each migration's Down path must independently avoid deleting recorded data. In particular, the access-audit migration must refuse Down once access/session audit data exists, rather than deleting it." (Recorded in brief section 6, commit 3413a59.) |
| EXP-1 (MRN in the export file) | Pre-release reset of version 1 | "Keep formatVersion: 1. Update the version-1 contract to include required source.mrn. New exports include the MRN. Imports require it to be a valid 11-digit number. Existing testing-only exported files and fixtures are regenerated to the updated version-1 shape. The imported patient still receives a newly generated local MRN; the exported source MRN is informational only and is never reused, reserved, or treated as identity. Keep strict exact-member validation under the revised version-1 contract." |
| MRN-RB (MRN rollback) | Option B, Down archives; subject to addendum and independent review before code | "PatientMrnArchive is a durable preservation table, keyed by patient GUID with a unique MRN. Down archives every existing (PatientId, MRN) atomically before removing Patients.Mrn. If an archive record already exists, it must match exactly; otherwise fail safely rather than overwrite it. Up restores archived MRNs first, then generates MRNs only for patients without an archived value. The archive must preserve the never-reuse guarantee and must not expose MRNs through ordinary application APIs. The addendum must explicitly disclose that a rollback retains this preservation table ... Add focused migration tests for down → up restoration, archive conflict refusal, uniqueness, and the existing migration-test rollback pattern." Option C rejected ("weakens the 'MRNs are never reused' rule"). See `ALV-N025_MRN_ROLLBACK_ADDENDUM.md`. |
| MRN-EX (MRN guard and the merge stand-aside rule) | Named exception; 51298 absolute | "Keep TR_Patients_MrnImmutable / 51298 absolute. An MRN must never become editable merely because a merge is in progress. Require: Add it to the explicit named-exception list in PatientMergeSchemaTests. Document why: it only rejects an actual MRN change, while a valid merge does not change MRNs. Assert that it does not contain the alveara.patient_merge stand-aside check. Add a focused proof that an attempted MRN update still receives 51298 even when the merge session context is set. Keep the ordinary valid-merge proof, so the exception cannot accidentally block a normal merge." |
| U-016/U-017 (merge-guard regressions) | Fix in ALV-N025 after a reviewed addendum | "U-016 and U-017 are merge-integrity regressions, the same family as U-015, and leaving PatientMergeSchemaTests knowingly red would undermine this story's merge work. Before code, prepare a short documents-only merge-integrity addendum ... Send that addendum for review before implementing the fourth migration." See `ALV-N025_MERGE_INTEGRITY_ADDENDUM.md`. |
| DEM-1 (existing Sex/State values) | Normalize + record | "Limit normalization to exact case/whitespace-insensitive matches of the approved Sex list and US state codes/full names. Create a patient-history entry for each automatic change, but it must clearly identify an automated migration/system action - never invent a staff user as the actor. Leave unmappable values untouched and list them on the review page. Down must not undo valid normalization; disclose that it is intentionally data-preserving." (Supersedes the brief section 6 line "No change to stored Sex and State values" for recognizable variants.) |
| DEM-2 (editing a patient with an old value) | Check only if changed | "Staff must be able to correct unrelated information without first resolving old demographic data. Show the old invalid value with the clear prompt to choose a supported value, and keep the patient on the demographic-review list until corrected." |
| DEM-3 (US State list) | States, DC, territories and military codes | "Use the 50 states, DC, PR, GU, VI, AS, MP, plus: AA - Armed Forces Americas; AE - Armed Forces Europe; AP - Armed Forces Pacific." |
| DEM-4 (Country in the files) | Required in the export file, optional in the CSV | "For the unreleased strict version-1 patient-export format, make patient.country required and regenerate test fixtures. For bulk CSV, make country optional; absent values use the practice default. Explicit CSV/export country values must be validated as ISO alpha-2 codes. Show Country in the readable record and data package." |
| SES-1 (sign-in lifecycle write, 2026-11-20) | Separate save, non-blocking; independent review first | "This preserves the approved behavior that audit-lifecycle tracking must not prevent a clinician from signing in. Calculate one fixed deadline and one sid; use those exact values for both the cookie and the AuditSessions row. If the lifecycle-row save fails, log audit_session_write_failed without PHI and let sign-in continue. That session has no lifecycle/sign-out event, as disclosed. Because this clarifies the previously approved audit-session lifecycle, prepare a short documents-only clarification addendum for independent review before implementing it." See `ALV-N025_SESSION_LIFECYCLE_CLARIFICATION.md`. |
| EXP-2 (export events) | Keep the three existing events | "Do not create duplicate rows. Treat PatientRecordExported as the conceptual audit category, not a new stored event name. The audit page/help text should explain that export activity is represented by: PatientExported, PatientDataPackageExported, PatientReadableRecordExported. Add focused proof that each export path writes exactly one respective event." |
| DOC-1 (document downloads) | Only downloads | "Keep inline viewing represented by the existing DocumentOpened event. Write PatientDocumentDownloaded only when the document is served as a download/attachment." |
| SES-2 (cookies without sid) | Record opens, no dedupe | "For already-issued cookies without sid, record each patient-record opening directly as PatientRecordOpened. Do not create a PatientRecordOpens deduplication row and do not write a sign-out lifecycle event for that legacy session. This is temporary because those cookies expire under the existing session timeout. Add focused proof for this legacy-cookie path." |
| MRG-1 (Q-5cb, merge identity, 2026-11-20) | Keep the survivor's identity | "Do not offer first name, last name, or date of birth as merge choices. Show both identities clearly during merge confirmation. State: 'The surviving record keeps its own name and date of birth. If the other record has the correct identity, cancel this merge and choose that record as the surviving record.' Offer choices for every other differing field. Add a focused test proving a merge with identity differences succeeds without violating the unique key, and a client test for the explanation." (Do not tell staff to correct the name or DOB after the merge: the retired record still holds that unique name-and-DOB key.) |
