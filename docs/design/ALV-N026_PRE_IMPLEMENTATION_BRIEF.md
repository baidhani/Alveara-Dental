# ALV-N026 pre-implementation brief (revision 3): documents, prescriptions, printing, backup and status

**Story:** ALV-N026 (plan item 63, R05 PKG-12). **Date:** 2026-11-21. **Status:** AWAITING_REVIEW (design only). No code, migration or configuration has been changed.

**Revision 3 answers `ALV-N026_R02_PACKAGE_REVIEW.md` (CHANGES_REQUIRED). It is part of the corrected R03 package, together with proposal R05 and PD-11 note revision 2.**

| Package review item | Where it is applied |
|---|---|
| 1. R04 references advanced to R05; G1 not approved until the package is approved | Inputs; section 0 (G1); sections 1 and 2.1 |
| 2. The no-hash policy is consistent; the financial hash is internal evidence only | Sections 3 and 3.1 |
| 3. Terminology: "General U.S.-oriented prescription layout; not a federal or state compliance profile" | Sections 1, 3.2a and 9 |
| 4 to 7. PD-11 corrections (official sources, field classes, the four decisions, controlled-substance and signature boundary) | `ALV-N026_PD-11_RESEARCH_NOTE.md` revision 2; section 3.2a here (p2 field requirements); section 6 (tests) |

**Revision 2 answered** `ALV-N026_PRE_IMPLEMENTATION_BRIEF_REVIEW.md` (CHANGES_REQUIRED). The owner accepted the reviewer's positions on A1 and A2 on 2026-11-21.**

| Review item | Where it is applied |
|---|---|
| 1. A1: Dentist does not get `ManageDocuments` by default | Sections 1 (Group A), 2.1 and 10 |
| 2. A2: the conservative category mapping; reclassification is configuration (`ManageDocumentSettings`) | Sections 1, 2.1 and 5 |
| 3. Import requires `ImportDocuments` plus the read right for the chosen category | Sections 1, 2.1 and 6 |
| 4. Merge scope approved as a tested implementation detail | Sections 3, 5 and 6 |
| 5. No hash is a mandatory acceptance check; two precise integrity cases | Sections 3 and 6 |
| 6. PD-11 research authorized (official US federal sources and identified state examples) | Section 0 (G2) |

**Inputs:**
- `ALV-N026_FINDING_SURVEY.md`: 23 PKG-12 rows plus OQ-2 and U-008;
- `ALV-N026_OWNER_DECISION_RECORD.md`;
- `ALV-N026_PD-11_JURISDICTION_SCOPE_DECISION.md`;
- `ALV-N026_PDF_OUTPUT_FIDELITY_REQUIREMENT_DECISION.md`;
- `ALV-N026_REGENERATION_SOURCE_SURVEY.md` (D1 to D5);
- `ALV-N026_PDF_LIBRARY_PROPOSAL-R05.md` (a delta on its base, `ALV-N026_PDF_LIBRARY_PROPOSAL-R04.md`).

**Plan acceptance (item 63):** documents and signed forms keep their integrity and appropriate visibility; print output is accurate and privacy-safe; prescriptions keep their lifecycle truth; backup, restore and missing-file paths communicate safely.

## 0. Gates before code (nothing below is coded until its gate passes)
| Gate | Covers | State |
|---|---|---|
| G1. PDF library proposal R05 (with its R04 base) approved, as part of the corrected R03 package (escalation: new dependency) | Every PDF slice (S4 to S8) | AWAITING_REVIEW. Not approved until the corrected package is approved |
| G2. PD-11 research note independently reviewed | Prescription **content fields** (S6 fields part) | AWAITING_REVIEW: `ALV-N026_PD-11_RESEARCH_NOTE.md` revision 2 (official sources only) |
| G3. This brief approved, including the permission-model change (`ManageDocuments`, PD-24) | S1 | AWAITING_REVIEW |
| G4. Release-store folders created and approved (after G1) | Adding the engine to the build | Folders created and verified 2026-11-21 (`ALV-N026_G4_RELEASE_STORE_RECORD.md`); awaiting reviewer confirmation |

Slices S2 and S3 (backup, status and the document UI fixes) depend only on G3.

## 1. Intended result (per finding)
### Group A: documents and access (PD-24, U-008)
| Id | Result |
|---|---|
| Q-5cn, Q-5cl, F-173 (PD-24) | **Each `DocumentCategory` gets a classification, `Clinical` or `NonClinical`.** The built-in categories are mapped explicitly in the migration (section 5). Practice-added and unrecognized categories default to `Clinical` until explicitly classified (deliberate privacy protection). **Clinical** documents need `ViewClinicalDocumentation` to list, open, download or preview; **non-clinical** ones need `ViewDocuments`. A new permission, **`ManageDocuments`**, is required for edit, inactivate and reactivate, and `ImportDocuments` no longer grants them. `ManageDocuments` never bypasses the read rule. **Changing a category's classification is practice configuration:** it needs the existing `ManageDocumentSettings` (administrator), is audited, and stays separate from `ManageDocuments`. **Import into a category needs both `ImportDocuments` and that category's read right**, so nobody can upload into a category they cannot read. The server enforces all of this; the UI hides what the server would refuse. Direct-API tests cover each path |
| U-008 and permission defaults (review item 1) | **`ManageDocuments`:** Admin and Office Manager. **Dentist:** `ImportDocuments` only, not `ManageDocuments`. **Hygienist and Assistant:** neither by default. `ImportDocuments` allows import only (no edit, inactivate, reactivate or category administration). Existing per-practice overrides are untouched, and a practice may grant `ManageDocuments` through the permission policy |
| F-130, F-131 | Open and Download are not offered for a Missing or Corrupt file; the row says why. Open goes through the app: a failure shows an in-app message naming the practice administrator, never a raw JSON tab |
| F-125 | With a filter set, the empty state says "No documents match this filter" |
| F-171 | Row actions are the primary action plus a "More" menu |
| Q-5bk | A shortened description in the list, with the full text on hover or focus. A test checks that Details history shows edits |
| Q-5bs, F-138 | A case-link mark on rows linked to a referral or lab case (ALV-N022's typed links) |
| Q-5bj | Help text: PDFs open in the browser's viewer (by design) |
| Q-5cx | Check the orphan report's rules (subfolders and non-blob files are not documents). Explain the count on the page. Fix the report if it counts non-blob files |

### Group B: prescriptions and printing (PD-11, F-164, OQ-2, fidelity decision, D1 to D5)
| Id | Result |
|---|---|
| F-108, F-109 | **Controlled documents print as server-side PDFs** (proposal R05): the browser opens or downloads the PDF, never the app page, so there is no browser header or footer, no internal URL and no blank second sheet. The four controlled materials and the clinical record copy move to PDF |
| F-110 (PD-11) | The prescriber's fields come from **actual staff-profile data**, using neutral concepts (credential, licence identifier, issuing jurisdiction). The field list is in section 3.2a (subject to **G2**). Nothing is invented, and the printout states its boundary: **"General U.S.-oriented prescription layout; not a federal or state compliance profile."** The field list and its classification are in the PD-11 note revision 2 (section 3.2a below) |
| F-164, Q-5aq | Cancelled, discontinued and superseded prescriptions print as a **record copy** with a diagonal **"<STATUS> — DO NOT DISPENSE"** watermark plus the status date and reason. Active prescriptions print normally. Prints are audited |
| F-165 | Plural units from a small fixed table (tablet/tablets, capsule/capsules; ml unchanged) |
| Q-5am | The option label reads "Once only (single dose)". Stored values are unchanged |
| Q-5ao | No change; a test proves a second Amend returns the open draft |
| OQ-2 | The MRN on the prescription (p2), the treatment-plan copy (v2), and the statement and receipt (v4). **Earlier versions are regenerated with their own template versions; under the fidelity decision the "byte-for-byte" wording is superseded.** No backfill and no re-versioning of issued documents |
| D1 | `PrescriptionIssue` row (section 3.2) |
| D2 | A legacy `p1` print is never regenerated as historical. Instead, a new p2 **record copy** dated today, from the immutable record, with today's status, keeping the original prescribed date, watermarked if inactive |
| D3 | The treatment-plan copy is built on the server with a `TreatmentPlanCopyIssue` row (section 3.3). Old browser copies are not recreated |
| D4 | The clinical record copy is a new issue "generated as of <date/time>" from current records, with a `ClinicalRecordCopyIssue` row. No historical regeneration |
| D5 | Statements and receipts keep the `FinancialDocument` contract. v4 adds the MRN, PDF output and the render bundle id |

### Group C: backup, restore and System Status (PD-25, D-06)
| Id | Result |
|---|---|
| F-152 (PD-25) | Backup failures and restore results go to **every administrator's in-app inbox**, with a persistent administrator banner. Acknowledgement is audited. System Status stays in warning until the condition is resolved. If creating a notification fails, the durable alert record, the existing file drop and the warning remain. No email |
| F-153 | The restore wizard accepts `.asc` |
| F-154 | Run backup, the restore drill and "Remove the restored copy" get a shared busy, disabled and idempotency-safe state with a lasting result message |
| D-06 | System Status (administrator only) gains three sections. **Backup and recovery:** last good backup, last restore drill, recovery key configured yes/no, overdue state, unresolved PD-25 alerts. **Storage:** document-folder reachability, free space, missing and damaged counts. **Staff accounts:** enabled, disabled and locked totals, and accounts without MFA. Counts and states only, no sensitive detail. **Also (proposal R05):** PDF engine readiness |
| Q-5ch | No change (retention by design) |

## 2. Itemized changes
### 2.1 Server
- **`Permission`:** append `ManageDocuments` (ordinal last). Defaults: Admin and Office Manager only, and it is grantable. U-008: Dentist gets `ImportDocuments`; Hygienist and Assistant get neither.
- **`DocumentCategory.Classification`** (`Clinical` | `NonClinical`, default `Clinical`). It is changed through the existing category-settings endpoints under **`ManageDocumentSettings`**, with an audit event `DocumentCategoryReclassified`.
- **`DocumentsController` and services:**
  - read paths check the category classification against `ViewClinicalDocumentation` / `ViewDocuments`;
  - **import requires `ImportDocuments` plus the read right of the chosen category**;
  - edit, inactivate and reactivate require `ManageDocuments` plus the read right;
  - listing filters out categories the caller cannot read (not 403 per row);
  - **moving a document to another category** (an edit) needs the read right of both the old and the new category.
- **PDF renderer service** (S4): implements proposal R05 (R04 base) sections 2, 4, 5 and 7. It runs `typst.exe` from the configured, hash-pinned location, and exposes `RenderAsync(templateId, templateVersion, canonicalJson)` returning a stream (no file retained). Readiness runs at startup and per batch, and is reported to System Status.
- **Issue services:**
  - `FinancialDocument` v4: adds the MRN to the envelope and content, a `RenderBundleId` column, and a PDF endpoint;
  - `PrescriptionIssueService` (D1, D2, F-164);
  - `TreatmentPlanCopyService` (D3);
  - `ClinicalRecordCopyService` (D4, from `PatientReadableRecordService` content);
  - each with a GET-PDF endpoint for issue and regeneration, an idempotency key and audit.
- **Backup alerts:**
  - a new `InAppBackupNotifier` is registered beside `FileDropBackupNotifier`, writing to the existing notification centre (`NotificationEntities`). This is not a new channel;
  - durable alert records with acknowledgement;
  - the banner data endpoint.
- **System Status:** new admin-only sections built from existing services (backup history, the storage integrity report, account counts, PDF readiness).

### 2.2 Client
- **Documents panel:** classification-aware actions, a state-aware Open and Download, an in-app open-failure message, the filter empty state, a "More" menu, the description column, the case-link mark and help text. Category settings get the classification control.
- **Print flows:** the prescription, plan copy, statement, receipt and clinical record copy open the server PDF. The `PrintPreview` and `window.print()` paths for these materials are removed.
- **Prescriptions:** record-copy wording, the frequency label, and the p1 "new record copy" action.
- **Backup:** `.asc` accepted, the busy state, the administrator banner and inbox entries.
- **System Status:** the three sections plus PDF readiness.

## 3. Data model for printed materials (D1 to D5; fidelity decision)
**Common rules:**
- Issue rows are **append-only**: an INSTEAD OF UPDATE/DELETE trigger refuses any change.
- They hold **no PDF**. Their envelopes hold only the identity and practice text shown.
- **Merge scope (approved by review item 4 as a tested implementation detail).** They are scoped to the patient and join the established merge-scope pattern, with the established guard and stand-aside behaviour. The migration slice records the exact triggers and guard codes, and proves directly in SQL that:
  - an ordinary direct change of a row's patient scope is refused;
  - an approved patient merge reassigns the issue rows correctly without weakening their append-only protection.
- Regeneration: rebuild from the cut-offs and the envelope, then render the recorded template version with the current approved bundle. Missing, inconsistent or unprovable versioned source data gives `integrity_mismatch` (409) **before rendering**, with a log entry carrying its `error_class`.
- **Hashes:** any `ContentHash` is an **optional diagnostic only**, never an acceptance condition (fidelity decision; review item 5). Statements and receipts keep their existing hash check as source verification.

### 3.1 Statements and receipts (D5)
- **Kept:** the existing `FinancialDocument` contract (cut-offs and envelope). Its existing `ContentHash` comparison stays as internal source-verification evidence only; it is **not** an implementation acceptance gate (review item 2).
- **Added:**
  - template v4 (the MRN in the envelope and content);
  - a nullable `RenderBundleId`;
  - a PDF render of the existing content.
- **Unchanged:** v1 to v3 rebuild with their own versions.

### 3.2 `PrescriptionIssues` (D1, D2, F-164)
`Id`, `PrescriptionId`, `PatientId`, `IssueKind` (`Print` | `RecordCopy`), `IssuedAtUtc`, `IssuedByUserId`, `TemplateVersion` (`p2`), `StatusAtIssue`, `EventCutoffId` (the last `PrescriptionEvent` id at issue, or null if none), `EnvelopeJson`, `RenderBundleId`, `ContentHash` (**optional diagnostic**, per D1), `IdempotencyKey`, `CorrelationId`.
- **`EnvelopeJson`** holds the patient name, date of birth and MRN; the prescriber name and (after G2) credential fields from the staff profile; and the practice name, phone and address.
- **Rebuild sources:** `Prescription` and `PrescriptionDetail` (immutable), plus the status and reason as of `EventCutoffId` (from `PrescriptionEvent`), plus the envelope.
- **`IssueKind = RecordCopy`** for an inactive status: the watermark, the status date and the reason.
- **Legacy `p1` (D2):** a request for a historical p1 print is refused (`legacy_print_unverifiable`). The UI offers "Issue a new record copy", which creates a p2 issue dated today with today's status and the original prescribed date.

### 3.2a p2 template and field requirements (PD-11 note revision 2; package review items 3 to 7)
**The layout is a "General U.S.-oriented prescription layout; not a federal or state compliance profile."** Each field's class (L, CS, OPT) and its source basis are in the PD-11 note, section 3.

| Field | Rule |
|---|---|
| Required (L) | Patient full name, address, date of birth, MRN; medication **exactly as recorded** (no "Strength" or "Dosage form" labels); dose; quantity; directions; refills; date of issue and date prescribed; prescriber name; status watermark with date and reason on inactive statuses; the boundary statement on every p2 |
| Shown when present (L/OPT) | Prescriber address and telephone (practice); credential; licence identifier and issuing jurisdiction; NPI (validated as exactly 10 digits only) |
| Never | DEA number; any controlled-substance detection or claim; any compliance claim; empty labels for absent optional fields |
| Refusal | **Only** a missing patient address refuses (`print_incomplete`, naming the item). A missing credential, licence or NPI never refuses |
| Signature | A "Prescriber signature" line may be printed. It is not described as a universal requirement |

**Staff profile:** four nullable fields (`CredentialLabel`, `LicenseNumber`, `LicenseJurisdiction`, `Npi`), edited by an administrator, audited without values, and copied into the `PrescriptionIssue` envelope at issue.

### 3.3 `TreatmentPlanCopyIssues` (D3)
`Id`, `PlanId`, `PatientId`, `RevisionNumber`, `ItemStateCutoffJson` (item id to the highest `TreatmentPlanItemState.Sequence` at issue), `IssuedAtUtc`, `IssuedByUserId`, `TemplateVersion` (`v2`, with the MRN), `EnvelopeJson` (practice, patient name and MRN), `RenderBundleId`, an optional `ContentHash`, `IdempotencyKey`, `CorrelationId`.

**Rebuild sources:**

| Content | Source |
|---|---|
| Title, explanation, phases, placement | The revision (I) |
| Fee | The item (I) |
| Procedure code and description | `ProcedureVersion` (I) |
| Acceptance, scheduling, completion | Item states at the cut-off (V) |
| Diagnosis labels | `DiagnosisVersion` as of `IssuedAtUtc` (V) |
| Practice and identity | The envelope |

**Proof needed in the slice:**
- the diagnosis label as of an instant equals the `DiagnosisVersion` with the greatest `OccurredAtUtc` at or before it;
- the plan's reference code is immutable.

If either fails, the copy refuses (`integrity_mismatch`) rather than guessing.

### 3.4 `ClinicalRecordCopyIssues` (D4)
`Id`, `PatientId`, `GeneratedAsOfUtc`, `IssuedByUserId`, `TemplateVersion`, `RenderBundleId`, an optional `ContentHash`, `IdempotencyKey`, `CorrelationId`.
- The content is built from current records at `GeneratedAsOfUtc` and is titled "Clinical record copy generated as of <date/time>".
- **No regeneration:** a later request is a new issue.
- The existing `ExportPatientReadableRecord` permission and export history are kept.

## 4. Permissions and audit
- **New permission:** `ManageDocuments` (**escalation; G3**).
- **Reads:**
  - PDFs of a material need that material's existing read right (ViewBilling, prescriptions, plan or clinical rights) plus patient access;
  - document reads follow the PD-24 classification.
- **Audit events (PHI-free):**
  - `DocumentCategoryReclassified`;
  - `PrescriptionIssued` (kind and status);
  - `PrescriptionLegacyPrintRefused`;
  - `TreatmentPlanCopyIssued`;
  - `ClinicalRecordCopyIssued`;
  - `FinancialDocumentRegenerated`;
  - `BackupAlertAcknowledged`.

  Plus the existing patient-access print events (ALV-N025), with material type, template version and integrity outcome.
- **Not in the audit:** names, the MRN or content.

## 5. Schema and guards (forward migrations; the next guard number is 51303)
1. **`AddDocumentClassificationAndManageDocuments`:**
   - `DocumentCategories.Classification` (NOT NULL, default `Clinical`);
   - **explicit mapping of the nine built-in categories** (`DocumentCategorySeeds`; review item 2, owner accepted):

     | Classification | Categories |
     |---|---|
     | `Clinical` | Radiograph, Photo, Referral letter, Medical clearance, Lab report, Correspondence, Other |
     | `NonClinical` | Insurance card, Identification |

     Practice-added and unrecognized categories are `Clinical` until explicitly classified.
   - permission defaults (`ManageDocuments`; Dentist `ImportDocuments`), with per-practice overrides untouched.
2. **`AddPrintIssueRecords`:**
   - three issue tables, append-only triggers (51303 onward) and merge-scope membership;
   - `FinancialDocuments.RenderBundleId`.
3. **`AddBackupAlertRecords`:** durable alert and acknowledgement records (append-only).

**Rollback:** each migration has a Down that drops only what it added. Data written into new tables is lost on rollback; this is disclosed. Classification Down is a column drop.

## 6. Tests (focused proof only; full regression is deferred to ALV-N028)
| Area | Tests |
|---|---|
| PD-24 | A role matrix: a clinical and a non-clinical category × list, open, download, preview, import, edit, inactivate × each role. Direct API plus UI. `ManageDocuments` without the read right is refused. **Import into an unreadable category is refused** (direct API). Moving a document into an unreadable category is refused. Reclassification needs `ManageDocumentSettings` and is audited. An unknown or practice-added category is clinical. Defaults per review item 1. Overrides kept on migration |
| Merge scope | Direct SQL: a scope change on each issue table is refused with its guard code. An approved merge reassigns the issue rows, and they stay append-only afterwards |
| Document UI | Missing or corrupt file has no Open or Download; open-failure message; filter empty state; More menu; description column; case-link mark |
| PDF engine | Readiness pass and fail (not startable, wrong version, hash mismatch); timeout and kill; concurrency cap; import lint; isolation environment; no temporary file left behind |
| Fidelity, per material | Each item below |

**Fidelity, per material** (PDF text extraction plus layout checks, never byte hashes):
- **Content:** exact identifiers, MRN, dates, amounts, prescriber and practice, status, watermark and notices; calculations; template selection.
- **Unchanged by later edits:** a later change (patient renamed, practice edited, prescriber renamed, diagnosis corrected, later item state, later ledger entry) does not change a regenerated document.
- **Integrity, two precise cases:**
  - a direct modification of an issue row is refused by its append-only database guard;
  - missing, inconsistent or unprovable versioned source data gives `integrity_mismatch` before any rendering.
- **No hash assertion is an acceptance check:** PDF and canonical-content hashes are optional diagnostics only. Assertions are on historical values, warnings, calculations, version choice and rendered text or layout.
- **Idempotency:** the same key gives the same issue.

**Material-specific tests:**

| Area | Tests |
|---|---|
| Prescriptions | Active print; record copy for each inactive status (watermark, date, reason); p1 historical print refused with a new p2 copy offered; plural units; frequency label; second Amend returns the open draft |
| p2 fields (after G2; PD-11 note section 6) | Required fields present with historical values; medication exactly as recorded with no Strength or Dosage-form labels; optional fields shown when present, with no empty labels and no refusal when absent; missing patient address gives `print_incomplete` with no PDF; boundary statement on every p2; no DEA field; no compliance or controlled-substance-detection text; NPI 10-digit validation (9, 11 or non-digit refused); a later profile edit does not change a regenerated earlier p2; profile edits audited without values |
| Financial | v1 to v3 still rebuild; v4 carries the MRN; no backfill |
| Clinical record copy | Titled "generated as of"; a second request is a new issue |
| Backup and status | In-app notifier delivers to every administrator; banner until resolved; acknowledgement audited; notification failure keeps the durable record and the file drop; `.asc` accepted; busy states; System Status sections admin-only with counts only |

**Test cost:** each slice runs only its focused groups, within the 5-minute rule. Anything longer is asked for first.

## 7. Slices (each committed as wip; the code is shown to the owner before any test run)
| Slice | Content | Gate |
|---|---|---|
| S1 | PD-24 classification, `ManageDocuments`, U-008, migration 1 | G3 |
| S2 | Document UI fixes (F-130/131/125/171, Q-5bk/bs/bj/cx) | G3 |
| S3 | Backup alerts (PD-25), F-153, F-154, System Status (D-06), migration 3 | G3 |
| S4 | PDF renderer service and readiness (engine from the approved store), migration 2 | G1, G4 |
| S5 | Financial v4 PDFs | G1 |
| S6 | Prescription p2: issue rows, record copy and watermark, p1 rule, F-165, Q-5am. The PD-11 fields come after G2 | G1; G2 for the fields |
| S7 | Treatment-plan copy on the server (D3) | G1 |
| S8 | Clinical record copy (D4); client print flows switched to PDFs | G1 |

## 8. Open questions
None open for the owner. A1, A2 and merge scope were resolved by the review and accepted by the owner (2026-11-21). G2 research is authorized; its note goes to independent review before any prescription-content code.

## 9. Out of scope
- The installer work: the redistributable step, readiness at install, the firewall rule and the service account (ALV-N013).
- Prescription jurisdiction profiles (the deferred follow-on story).
- Email or SMTP alerts.
- Any compliance claim for any jurisdiction, payer or pharmacy (the layout is a general U.S.-oriented layout, not a compliance profile).
- Full regression (ALV-N028).

## 10. Assumptions (logged)
- **A1 and A2:** resolved (review items 1 and 2, accepted by the owner). They are no longer assumptions.
- **A3:** the readable-record content (`PatientReadableRecordService`) is the clinical record copy's content source.
- **A4:** the existing notification centre can hold administrator-only backup alerts without a new channel.
