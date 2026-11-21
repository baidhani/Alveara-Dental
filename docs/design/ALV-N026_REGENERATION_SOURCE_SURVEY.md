# ALV-N026 regeneration source survey (read-only, for the pre-implementation brief)

**Date:** 2026-11-21. **Status:** survey findings plus decisions requested. It answers the R03 review's Addendum 01 ("map the exact database source and version for every printed material, and prove its regeneration inputs meet this contract").

**How it was done:** I read the code at `4a4e7ab`. No code, database or configuration was changed.

**Contract being checked (Addendum 01):**
- regenerate only from immutable or versioned sources;
- where a historical value cannot be rebuilt, store the smallest immutable canonical render-data record;
- the content hash must match, otherwise `integrity_mismatch`;
- keep the original issue date and template version.

**Legend:**
- **I** = immutable (refused by a database trigger, or append-only);
- **V** = versioned (a value as of any instant is recoverable from appended versions or events with timestamps);
- **M** = mutable current state, which fails the contract unless it is captured at issue.

## Summary
| Material | Issue record today | Content hash today | Mutable inputs (M) | Meets the contract today? |
|---|---|---|---|---|
| Statement / receipt (`FinancialDocument`) | Yes, appended, never edited | Yes, stored; reprint refuses on mismatch (`document_content_changed`) | None. The envelope captures names and practice at issue | **Yes** |
| Prescription (`p1`) | No (audit event `PrescriptionPrinted` only) | Computed, **not stored** | Patient name and date of birth, prescriber display name, practice, status (current) | **No** |
| Treatment-plan patient copy | No (patient-access print event only) | None (built in the browser) | Practice; diagnosis label and item states read as *current* | **No** |
| Clinical record copy (readable record) | Export history only | None | Many domains hold current state (see section 5) | **No**, and not practical to make historical |

## 1. Statements and receipts: already compliant
**Issue record:** `FinancialDocument`. It is appended and never edited, and holds:
- the cut-offs: entry, allocation, release and responsibility numbers, plus `AsOfUtc`;
- `TemplateVersion` and `ContentHash`;
- `EnvelopeJson`: the patient, addressee and practice as displayed, and (v2+) the frozen aging basis.

**Sources** (`BillingService.Documents.cs`, `LoadSnapshotAsync`):

| Source | Status | Notes |
|---|---|---|
| Ledger entries | I | Procedure code and description are copied onto each entry when it is posted |
| Allocations, releases, responsibility history | I | Append-only |
| Completed procedures and amendments | V | Capped at `AsOfUtc` |
| Payment method name | I | `TR_BillingPaymentMethods_Identity` refuses a name change; `NoDelete` refuses deletion |

**Reprint:** rebuilds with the document's own template version and envelope, recomputes the hash, and refuses on a mismatch or an unsupported template.

**Needed for PDF:** only a new template version (v4) and the render bundle identity (engine, fonts, runtime) on the issue record. No new source is required.

## 2. Prescriptions: not compliant today
**Print path:** `PrescriptionLifecycleService.PrintAsync`. It writes the audit event `PrescriptionPrinted`, but no issue row and no stored hash.

| Input | Source | Status |
|---|---|---|
| Medication, dose, frequency, allergy outcome and matches, review note, prescribed time | `Prescription` | I (refused on change or delete) |
| Instructions, quantity, refills, supersedes | `PrescriptionDetail` | I |
| Status (Active, Cancelled, Discontinued, Superseded) and superseded-by | `PrescriptionEvent` | V: append-only with `OccurredAtUtc`, at most one ending event, so **status as of any instant is derivable** |
| Patient name, date of birth | `Patients` (current) | **M** |
| Prescriber name | Current user display name (`ClinicalNames`) | **M** |
| Practice name, phone, address | `PracticeSettings` (current singleton, no history) | **M** |
| Decision-support limitation text | Code constant | Fixed per release; must be part of the template version |

**The `p1` canonical string** contains the current status and the mutable patient and prescriber names. It does **not** contain the practice. It was never stored.

## 3. Treatment-plan patient copy: not compliant today
**Today:** built in the browser (`TreatmentPlanPatientCopy.tsx`) from the plan view and printed with `window.print()`. `recordPrint` writes the patient-access print event. There is no template version and no hash.

| Input | Source | Status |
|---|---|---|
| Title, patient explanation | `TreatmentPlanRevision` | I |
| Phases and item placement | `TreatmentPlanRevisionPhase` / `TreatmentPlanRevisionItem` | I |
| Fee | `TreatmentPlanItem.Fee`, copied from the catalog version when proposed | I |
| Procedure code and description | `ProcedureDefinition` / `ProcedureVersion` | I (versions are appended, never edited) |
| Withdrawal | Set once on the item | I |
| Acceptance, scheduling and completion per item | `TreatmentPlanItemState` | V: append-only with `Sequence` and `RecordedAtUtc`. The copy reads the *latest*; as-of needs a cut-off |
| Diagnosis label | `Diagnoses.Label` (current) | M, **but** `DiagnosisVersion` holds `Label` and `OccurredAtUtc`, so it is V via the versions |
| Practice | `PracticeSettings` (current) | **M** |
| Plan version, reference code | `TreatmentPlan.CurrentRevision`, reference | The revision number is fixed per issue; the reference code needs checking in the brief |

## 4. Practice details: mutable everywhere except financial documents
`PracticeSettings` is a mutable singleton with no history. Financial documents avoid this by storing the practice in the envelope. Every other material needs the same capture at issue.

## 5. Clinical record copy (readable record): different in kind
`PatientReadableRecordService` renders an HTML record across about 20 export domains (`ExportDomainCatalog`).

- **Versioned or append-only domains:** encounters and addenda, clinical record items, diagnoses, chart, treatment plans, billing, prescriptions and others.
- **Current-state domains:** registration demographics (with `PatientHistory`), recall plan, follow-up tasks, messages and notifications, document status and others.

Proving an exact as-of reconstruction for every domain is a large separate project. Storing its canonical content would be a full duplicate of the record (PHI), which is what the owner direction rejected.

## Decisions requested (recommendations in **bold**)
**D1. Prescription p2 issue record.**
- **Add an immutable `PrescriptionIssue` row**, mirroring `FinancialDocument`. It holds:
  - the prescription id;
  - the issue time;
  - the status as of issue and the event cut-off (the last `PrescriptionEvent` id/time);
  - the template version and the canonical-content hash;
  - the render bundle id;
  - a small envelope JSON: patient name and date of birth, prescriber name and practice as printed.
- Regeneration rebuilds the medication content from the immutable rows, the status as of the cut-off from events, and the identity text from the envelope, then requires the hash to match.

**D2. Legacy `p1` prescription "reprints".** No hash was stored, and the identity fields were mutable, so the historical printout **cannot be verified**.
- **Refuse a `p1` "reprint" as such.**
- Instead, offer a **new p2 issue**, a "new copy issued <date>", built from the immutable prescription record with its status as of that new issue.
- It is never labelled or presented as the earlier printout.

**D3. Treatment-plan patient copy.**
- **Move the copy to the server** as canonical content with an immutable `TreatmentPlanCopyIssue` row. It holds:
  - the plan id and revision number;
  - the item-state cut-off (the highest `TreatmentPlanItemState.Sequence` per item, or a single as-of instant);
  - the template version and hash;
  - the bundle id;
  - an envelope (practice).
- Diagnosis labels come as of issue from `DiagnosisVersion`.
- Pre-PDF browser copies were never hashed, so they are never reprinted as originals. A new copy is a new issue.

**D4. Clinical record copy.**
- **Do not offer historical regeneration.**
- Each print is a **new issue as of now**, recorded with its as-of instant, hash and bundle id.
- A later request produces a new copy with the new date, never presented as an earlier one.
- (The alternative, storing its canonical content, would duplicate the full record and conflicts with the owner direction.)

**D5. Statements and receipts:** **keep the existing contract.** Add template v4 (PDF) and the bundle id to `FinancialDocument`. No new source is needed.

## Owner decisions on D1 to D4 (2026-11-21, verbatim intent)
These replace the recommendations above wherever the two differ.

**D1. Prescriptions: yes, an issue row.**
- Add an immutable `PrescriptionIssue` row recording:
  - the status and event cut-off;
  - the template version;
  - the rendering bundle id;
  - the small identity and practice envelope needed to reproduce what was shown.
- **Do not store the generated PDF.**
- **A PDF-byte hash is not required.** A canonical-content hash may be kept as **optional diagnostic evidence only**.

**D2. Legacy `p1` prescriptions: refuse, and offer a new copy.**
- Do not present a `p1` PDF as a historical reprint.
- Offer a new p2 **record copy** generated today from the immutable prescription record, using today's applicable status.
- It keeps the **original prescribed date** and never implies it is the earlier printout.
- Inactive prescriptions (cancelled, discontinued, superseded) carry the required warning or watermark.

**D3. Treatment-plan copy: yes, a server issue.**
- Create an immutable `TreatmentPlanCopyIssue` row using:
  - the plan revision;
  - the item-state cut-off;
  - the diagnosis labels at issue;
  - the practice envelope.
- Old browser copies are not recreated as originals. A later copy is a new server-issued copy.

**D4. Clinical record copy: a new issue as of now.**
- No historical reconstruction across the ~20 domains, and no full PHI duplicate.
- Each request creates a new "clinical record copy generated as of [date/time]" from current controlled records, with issuance and audit metadata.
- It never claims to be an earlier historical copy.

**D5 (statements and receipts):** the existing contract is kept, as recommended. Under the fidelity decision, its existing canonical-content hash check is source verification, not a PDF-byte check.

**Point for the brief (from D1):** with the hash optional for prescriptions, the "cannot verify, then refuse" rule (fidelity decision point 6) rests on three things:
- the immutability of the sources (database triggers on `Prescription` and `PrescriptionDetail`, and the append-only `PrescriptionEvent`);
- the stored event cut-off;
- the stored envelope.

A missing or inconsistent source or cut-off refuses with `integrity_mismatch`.

## Fidelity standard (owner decision, `ALV-N026_PDF_OUTPUT_FIDELITY_REQUIREMENT_DECISION.md`)
**Byte-for-byte PDF equality is not a requirement.** The hashes in this survey are **canonical-content** hashes, taken over the source data a document shows, never over PDF bytes. A changed PDF encoding after an engine, runtime or font update is acceptable when the content checks pass.

## Tests the brief will carry (per material)
- The regenerated canonical content equals the issued canonical-content hash (source-data verification).
- **Exact assertions on the rendered document:**
  - patient identifiers and MRN;
  - dates and amounts;
  - procedure and service information;
  - prescriber and practice;
  - status, watermark and required notices.

  These are checked by PDF text extraction, plus visual and layout checks that tolerate internal encoding and metadata differences.
- A changed mutable source (patient renamed, practice changed, prescriber renamed, diagnosis corrected, a later item state) does **not** change a regenerated document.
- A tampered issue row or source gives `integrity_mismatch`.
- Template-version selection, and the legacy-reprint rule (D2).
- Authorization, audit, missing-source and error paths. Idempotent issue (the same key gives the same issue).
- An optional byte comparison for a fixed bundle is a diagnostic only, never an acceptance gate.
