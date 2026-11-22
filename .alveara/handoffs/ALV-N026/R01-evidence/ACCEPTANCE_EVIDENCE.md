# ALV-N026 R01: acceptance evidence

This file checks ALV-N026 against two sources:
- **plan item 63's acceptance** (`GPT Docs/Alveara_Dental_Execution_Plan_v4.md`);
- **the approved scope:** `docs/design/ALV-N026_PRE_IMPLEMENTATION_BRIEF.md` revision 3, approved by `docs/design/ALV-N026_R03_FINAL_DESIGN_REVIEW.md` with gates G1-G4.

It is amended by:
- `ALV-N026_ISSUE_ROW_OWNERSHIP_DECISION.md`: no PatientId on prescription and plan-copy issues;
- the owner decisions recorded below.

**Evidence:**
- server: `artifacts/backend-focused-tests.csv`;
- client: vitest on `77caf04`;
- negative controls: C1-C6 (`TEST_RESULTS.md`).

## The five acceptance criteria (EXECUTION_STATUS `acceptance.total` = 5)
| # | Criterion | Proof | Status |
|---|---|---|---|
| 1 | Documents and signed forms keep their integrity and appropriate visibility | **Documents:** classification and the read rule (DocumentClassificationAccessApiTests, DocumentPermissionTests; C1); integrity, orphan and storage (DocumentIntegrityServiceTests, DocumentOrphanServiceTests, DocumentStorageApiTests); client documents vitest. **Signed forms:** not changed by ALV-N026 (no finding in the brief); no new proof. | **PARTIAL**: documents are proven; signed forms are untouched and unproven here |
| 2 | Print output is accurate and privacy-safe | Server PDFs for every controlled material, with rendered text read by PdfPig: statements and receipts v4 (BillingStatementsApiTests); prescriptions p2 with the boundary statement, no DEA field, the watermark copy and the restored allergy statement (PrescriptionWorkflowApiTests); the plan copy (TreatmentPlanCopyApiTests, `integrity_mismatch`, C2); the clinical record copy (ClinicalRecordCopyApiTests, PHI-free audit, C3). Reprints show values as issued, not later edits. | PROVEN at the server and client level; **the plan's real-browser print coverage was not run** |
| 3 | Prescriptions keep their lifecycle truth | p2 issue rows are append-only; an ended prescription prints only as a watermarked record copy with its date and reason; p1 is refused as unverifiable; a reprint shows the prescription as issued; the second Amend returns the open draft (PrescriptionWorkflowServiceTests) | PROVEN |
| 4 | Backup and restore, and the missing-file paths, communicate safely | PD-25 in-app alerts to every administrator plus a banner, with audited acknowledgement and a fallback when a channel fails (BackupAlertTests, C4); System Status shows counts only to administrators (BackupAlertApiTests, SystemStatusTests); `.asc` accepted (BackupCryptoTests, client); a missing or corrupt file has no Open or Download and shows an in-app message (DocumentStorageApiTests, DocumentsPanel tests) | PROVEN at the server and client level; **real-browser missing-file coverage not run**; BackupRecoveryHardeningTests and RealProcessDatabaseOutageTests not run (long) |
| 5 | PD-11, PD-24 and PD-25 are resolved before the work that depends on them | PD-11: G2 research note approved, and the p2 field list implemented (NPI validation, optional fields, boundary). PD-24: classification and ManageDocuments (S1). PD-25: backup alerts (S3). All gates were closed before their slices. | PROVEN |

**Agent's assessment: 3 of 5 fully proven** (criteria 2 at the server and client level, 3 and 5). Criteria 1 and 4 are partly unproven for the reasons above. As with ALV-N025, EXECUTION_STATUS keeps `acceptance.passed` at 0 until the review; the reviewer decides whether criteria 1, 2 and 4 count.

## Brief section 6 rows
| Row | Evidence | Gap |
|---|---|---|
| PD-24 role matrix, import into an unreadable category, reclassification, defaults | G1 (354) | Untested: an override granting ViewClinicalDocumentation |
| Merge scope | ClinicalRecordCopyOwnershipTests; TreatmentPlanItemAppointmentLinkMergeTests | Prescription and plan-copy issues have no PatientId (the ownership decision overrides the brief); their append-only guard is proven in the S6/S7 ownership tests |
| Document UI | client documents vitest; DocumentStorageApiTests | Q-5cx: the walkthrough count's root cause was not confirmed |
| PDF engine | PdfEngineTests 13 (ready/not ready, version, hash, font, timeout and kill, concurrency of 2, template lint, isolation, no folder left behind) | none |
| Fidelity per material | BillingStatementsApiTests, PrescriptionWorkflowApiTests, TreatmentPlanCopyApiTests, ClinicalRecordCopyApiTests (rendered text; values as issued; `integrity_mismatch`; idempotency) | v1 and v2 statements are covered only by the older BillingDocumentServiceTests |
| Prescriptions and p2 fields | PrescriptionWorkflowApiTests (one test per row) | the prescriber-details staff screen is not built (the API route exists) |
| Financial | BillingStatementsApiTests (v4 with MRN, v3, hash mismatch) | - |
| Clinical record copy | ClinicalRecordCopyApiTests | - |
| Backup and status | G2 | long backup classes not run |

## Owner decisions during implementation (2026-11-21 and 2026-11-22)
- S8 merge scope for ClinicalRecordCopyIssues: **no reviewer check**. The pattern was pre-approved; direct-SQL proof was mandatory; it is disclosed here.
- No EnvelopeJson on clinical record copies.
- Delete the dead browser-print code and the tests that covered only it.
- **U-022:** a named exception for `PatientRecordOpens` (merge keeps the original record, export excludes it, with a reason). `TreatmentPlanItemAppointmentLinks` is fixed now (registry entry plus migration).
- **U-020:** catalog placements for 7 tables.
- **Allergy statement:** restored on the p2 PDF.
