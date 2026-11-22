# ALV-N026 R01: changed files

From the ALV-N025 closure `318bf3c` (origin/main) to the final implementation commit `baaeb51`: 218 files (103 A;1 D;114 M;). The range also holds the ALV-N026 design documents and reviews, the ALV-N025 ledger correction `9ece51a` and the chat-handoff documents; `git diff --name-status 318bf3c..baaeb51`.

## Commits

- `ed39e24` ALV-N026: read-only finding survey (PKG-12: 23 register rows, Q-5cl documents part, OQ-2 and U-008 carried in; 7 owner decisions listed) (documents only)
- `b51ea51` ALV-N026: owner decisions PD-24, PD-11, PD-25, F-164, print method (server-side PDF, checkpoint), D-06, OQ-2, U-008 (assigned) (documents only)
- `b7f2f2c` ALV-N026: PD-11 jurisdiction scope decision and PDF library comparison (PDFsharp 6.5 found AGPL; Typst/Skia spikes next) (documents only)
- `53155bf` ALV-N026: PDF library proposal R01 for independent review (Typst 0.15.1) and measured spike results (documents only)
- `ee86acf` ALV-N026: PDF library proposal R02 (response to R01 CHANGES_REQUIRED; isolation and same-machine determinism evidence; second-machine proof pending) (documents only)
- `230a61b` ALV-N026: Typst proof kit R02 response (checksum-before-extraction, in-kit network and file-boundary controls, expanded results; author run PASS) (documents only)
- `4ed2f13` ALV-N026: Typst proof kit R03 (immutability correction: no expected-output writing, mandatory comparison; tamper checks) (documents only)
- `833d906` ALV-N026: record Typst proof kit R03 review (APPROVED for the second-machine evidence run only) (documents only)
- `0e1f842` ALV-N026: record Typst second-machine run 1 (FAIL: engine runtime DLL missing) and its review (R04 remediation approved) (documents only)
- `a1aaf64` ALV-N026: Typst proof kit R04 (app-local VC++ runtime, startup preflight, stricter controls) and its review (CHANGES_REQUIRED) (documents only)
- `6fa6265` ALV-N026: Typst proof kit R05 (runtime provenance, version reconciliation, raw dependency output, author-run evidence) (documents only)
- `2e36ca5` ALV-N026: record Typst proof kit R05 review (APPROVED for one repeat clean-VM run only) (documents only)
- `4466a57` ALV-N026: record Typst second-machine run 2 with kit R05 (PASS on clean Pacific-time VM) (documents only)
- `57d983c` ALV-N026: record Typst second-machine run 2 review (APPROVED as determinism and boundary evidence) (documents only)
- `72703f9` ALV-N026: PDF library proposal R03 (accepted cross-machine evidence, installer-managed VC++ runtime prerequisite, open approval items) (documents only)
- `161d1a8` ALV-N026: record PDF library proposal R03 review (CHANGES_REQUIRED: retain issued PDFs as immutable artifacts) (documents only)
- `4a4e7ab` ALV-N026: record PDF proposal R03 review addendum 01 (owner direction: no issued-PDF store; versioned regeneration contract) (documents only)
- `d19b50c` ALV-N026: record owner PDF output fidelity decision (no byte equivalence) and the read-only regeneration source survey (documents only)
- `70b15c2` ALV-N026: record owner decisions D1-D4 on prescription, legacy p1, treatment-plan copy and clinical record copy regeneration (documents only)
- `f25936a` ALV-N026: PDF library proposal R04 (fidelity standard, versioned regeneration, readiness-proven runtime install) and pre-implementation brief revision 1 (documents only)
- `727c863` ALV-N026: record review of PDF proposal R04 and brief revision 1 (CHANGES_REQUIRED) (documents only)
- `c12c5dc` ALV-N026: brief revision 2 and PDF proposal R05 delta (A1/A2 per review, import read rule, merge-scope proofs, hashes diagnostic only) (documents only)
- `dc5dbc9` ALV-N026: PD-11 research note for gate G2 (US federal/common baseline print fields, sources, exclusions) (documents only)
- `ef2e893` ALV-N026: record R02 package review (CHANGES_REQUIRED: official sources, layout wording, PD-11 decisions) (documents only)
- `7d7aaf5` ALV-N026: brief revision 3 and PD-11 note revision 2 (official sources, layout wording, field classes, p2 requirements) (documents only)
- `eef675f` ALV-N026: record final design review (G1, G2, G3 APPROVED; G4 pending) and apply its two PD-11 wording corrections (documents only)
- `56fe7c9` wip: ALV-N026 slice S1 - PD-24 document classification, ManageDocuments, U-008
- `95a321d` wip: ALV-N026 slice S2 - document screen fixes (F-130, F-131, F-125, F-171, Q-5bk, Q-5bs, Q-5bj, Q-5cx)
- `16cdc17` wip: ALV-N026 slice S3 - backup alerts in the administrators' inbox, System Status sections, F-153, F-154
- `290a874` ALV-N026: G4 release-store creation record (folders created and verified outside the repo) (documents only)
- `90ec6c2` ALV-N026: record G4 review (CHANGES_REQUIRED: store ACL) and G4 R02 record (ACL restricted to owner, SYSTEM, Administrators; artifacts unchanged) (documents only)
- `b58624b` ALV-N026: record G4 R02 review (APPROVED; G4 closed) (documents only)
- `c41520c` wip: ALV-N026 slice S4 - PDF engine service, readiness, trusted manifest and provisioning
- `afafd76` wip: ALV-N026 slice S5 - statements and receipts as server-made PDFs (template v4 with MRN)
- `b557cdc` ALV-N026: record owner decision on issue-row ownership (prescription and plan-copy issues owned through their parent) and align the brief (documents only)
- `68c926a` wip: ALV-N026 slice S6 - prescriptions as server-made PDFs (p2 issues, record copy, watermark) Append-only PrescriptionIssues owned through the prescription (no PatientId, per the ownership decision); p2 template with boundary statement and watermark; optional prescriber details on the staff profile (Admin, NPI 10 digits); issue and PDF routes; p1 browser print retired (410); F-165 plurals and the Q-5am label; client Print (PDF) and Record copy (PDF).
- `c9a75e5` ALV-N026: record the chat handoff package for continuing at S7 (documents only)
- `9ece51a` ALV-N025: regression-ledger correction - add the missing ALV-N025 section and obligation rows (documents only)
- `92c075a` ALV-N026: refresh the chat handoff package after review, challenge and cold-start tests (documents only)
- `4aefde6` ALV-N026: refresh the chat handoff package (round 3) after the review, hardest-setting challenge and cold-start run (documents only)
- `5d85ede` ALV-N026: refresh the chat handoff package (round 4) - must-know rules block, required reading, brief section 5 supersession (documents only)
- `03831e5` wip: ALV-N026 S7 - treatment-plan patient copy made on the server (D3)
- `1ab6962` wip: ALV-N026 S8 - clinical record copy as a server PDF (D4); old browser prescription print removed
- `f75e33e` wip: ALV-N026 S8 repair - U-022/U-020 fixed: PatientRecordOpens named exception, appointment-link merge handling, export catalog
- `77caf04` ALV-N026: documents, prescriptions, printing and backup integrity - implementation complete
- `baaeb51` ALV-N026: restore the allergy-check statement on the p2 prescription PDF

## Files

| Status | Path |
|---|---|
| M | `.alveara/PROGRAM_REGRESSION_LEDGER.md` |
| A | `.gitattributes` |
| M | `PROGRESS.md` |
| A | `build/pdf-engine.manifest.json` |
| M | `docs/DOCUMENTS.md` |
| A | `docs/design/ALV-N026_CHAT_HANDOFF.md` |
| A | `docs/design/ALV-N026_FINDING_SURVEY.md` |
| A | `docs/design/ALV-N026_G4_RELEASE_STORE_RECORD.md` |
| A | `docs/design/ALV-N026_G4_RELEASE_STORE_RECORD_R02.md` |
| A | `docs/design/ALV-N026_G4_RELEASE_STORE_REVIEW.md` |
| A | `docs/design/ALV-N026_G4_RELEASE_STORE_REVIEW_R02.md` |
| A | `docs/design/ALV-N026_ISSUE_ROW_OWNERSHIP_DECISION.md` |
| A | `docs/design/ALV-N026_OWNER_DECISION_RECORD.md` |
| A | `docs/design/ALV-N026_PD-11_JURISDICTION_SCOPE_DECISION.md` |
| A | `docs/design/ALV-N026_PD-11_RESEARCH_NOTE.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_COMPARISON.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R01.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R02.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R03.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R04.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL-R05.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL_R01_REVIEW.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL_R03_REVIEW.md` |
| A | `docs/design/ALV-N026_PDF_LIBRARY_PROPOSAL_R03_REVIEW_ADDENDUM_01.md` |
| A | `docs/design/ALV-N026_PDF_OUTPUT_FIDELITY_REQUIREMENT_DECISION.md` |
| A | `docs/design/ALV-N026_PRE_IMPLEMENTATION_BRIEF.md` |
| A | `docs/design/ALV-N026_PRE_IMPLEMENTATION_BRIEF_REVIEW.md` |
| A | `docs/design/ALV-N026_R02_PACKAGE_REVIEW.md` |
| A | `docs/design/ALV-N026_R03_FINAL_DESIGN_REVIEW.md` |
| A | `docs/design/ALV-N026_REGENERATION_SOURCE_SURVEY.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R01_REVIEW.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R02.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R02_REVIEW.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R03.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R03_REVIEW.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R04.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R04_REVIEW.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R05.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_KIT_R05_REVIEW.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN1.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN1_REVIEW.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN2.md` |
| A | `docs/design/ALV-N026_TYPST_PROOF_SECOND_MACHINE_RUN2_REVIEW.md` |
| A | `docs/design/evidence/ALV-N026-S5/PDFPIG_TEST_DEPENDENCY.md` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/MSVC-RUNTIME-SOURCE-R05.txt` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/author-run-console.txt` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/imports-typst.txt` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/imports-vcruntime140.txt` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/list-pe-imports.ps1` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/results-CentralStandardTime.json` |
| A | `docs/design/evidence/ALV-N026-typst-kit-R05/vm-run2-results-PacificStandardTime.json` |
| M | `docs/stabilization/UNASSIGNED_FINDINGS.md` |
| A | `scripts/Install-PdfEngine.ps1` |
| M | `scripts/Start-DevApi.ps1` |
| M | `src/Alveara.Api.Tests/Alveara.Api.Tests.csproj` |
| M | `src/Alveara.Api.Tests/AlveraTestEnvironment.cs` |
| A | `src/Alveara.Api.Tests/BackupAlertApiTests.cs` |
| A | `src/Alveara.Api.Tests/BackupAlertTests.cs` |
| M | `src/Alveara.Api.Tests/BillingDocumentTemplateVersionServiceTests.cs` |
| M | `src/Alveara.Api.Tests/BillingStatementsApiTests.cs` |
| A | `src/Alveara.Api.Tests/ClinicalRecordCopyApiTests.cs` |
| A | `src/Alveara.Api.Tests/ClinicalRecordCopyOwnershipTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentApiTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentBackupRestoreTests.cs` |
| A | `src/Alveara.Api.Tests/DocumentClassificationAccessApiTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentContextServiceTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentImportServiceTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentIntegrityServiceTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentLibraryApiTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentLibraryFilterTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentLibraryServiceTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentOrphanServiceTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentPermissionTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentSettingsServiceTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentStorageApiTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentTestBase.cs` |
| M | `src/Alveara.Api.Tests/ExportDomainCatalogTests.cs` |
| M | `src/Alveara.Api.Tests/ExternalCaseApiTests.cs` |
| M | `src/Alveara.Api.Tests/ExternalCaseControllerPermissionTests.cs` |
| M | `src/Alveara.Api.Tests/ExternalCaseServiceTests.cs` |
| A | `src/Alveara.Api.Tests/InstallPdfEngineScriptTests.cs` |
| A | `src/Alveara.Api.Tests/PatientIdExceptionTests.cs` |
| M | `src/Alveara.Api.Tests/PatientMergeApiTests.cs` |
| M | `src/Alveara.Api.Tests/PatientMergeSchemaTests.cs` |
| M | `src/Alveara.Api.Tests/PatientOwnedDataTests.cs` |
| A | `src/Alveara.Api.Tests/PdfEngineTests.cs` |
| A | `src/Alveara.Api.Tests/PrescriptionIssueOwnershipTests.cs` |
| A | `src/Alveara.Api.Tests/PrescriptionPdfTextTests.cs` |
| M | `src/Alveara.Api.Tests/PrescriptionWorkflowApiTests.cs` |
| M | `src/Alveara.Api.Tests/PrescriptionWorkflowServiceTests.cs` |
| M | `src/Alveara.Api.Tests/RetiredRecordGuardTests.cs` |
| A | `src/Alveara.Api.Tests/TreatmentPlanCopyApiTests.cs` |
| A | `src/Alveara.Api.Tests/TreatmentPlanCopyIssueOwnershipTests.cs` |
| A | `src/Alveara.Api.Tests/TreatmentPlanItemAppointmentLinkMergeTests.cs` |
| M | `src/Alveara.Api/Alveara.Api.csproj` |
| A | `src/Alveara.Api/Architecture/Backup/BackupAlerts.cs` |
| M | `src/Alveara.Api/Architecture/Backup/BackupEntities.cs` |
| M | `src/Alveara.Api/Architecture/Backup/BackupRestoreService.cs` |
| M | `src/Alveara.Api/Architecture/Backup/BackupService.cs` |
| M | `src/Alveara.Api/Architecture/Backup/BackupSupport.cs` |
| M | `src/Alveara.Api/Architecture/Billing/BillingService.Documents.cs` |
| M | `src/Alveara.Api/Architecture/Billing/FinancialDocumentBuilder.cs` |
| M | `src/Alveara.Api/Architecture/Billing/FinancialDocumentContracts.cs` |
| A | `src/Alveara.Api/Architecture/Billing/FinancialDocumentPdf.cs` |
| M | `src/Alveara.Api/Architecture/Configuration/StaffProviderService.cs` |
| A | `src/Alveara.Api/Architecture/Documents/DocumentAccess.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentEditService.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentEntities.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentImportService.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentLibraryService.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentMapper.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentSettingsService.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentStatusService.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentViews.cs` |
| M | `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseService.Documents.cs` |
| M | `src/Alveara.Api/Architecture/Identity/IdentityEntities.cs` |
| M | `src/Alveara.Api/Architecture/Identity/Permission.cs` |
| M | `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs` |
| M | `src/Alveara.Api/Architecture/Identity/PermissionRegistry.json` |
| M | `src/Alveara.Api/Architecture/Notifications/NotificationEntities.cs` |
| A | `src/Alveara.Api/Architecture/Patients/Merge/PatientIdExceptions.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Merge/PatientOwnedData.cs` |
| A | `src/Alveara.Api/Architecture/Patients/Portability/Package/ClinicalRecordCopy.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Portability/Package/ExportDomainCatalog.cs` |
| A | `src/Alveara.Api/Architecture/Pdf/PdfEngine.cs` |
| A | `src/Alveara.Api/Architecture/Pdf/PdfManifests.cs` |
| A | `src/Alveara.Api/Architecture/Prescribing/PrescriptionIssues.cs` |
| M | `src/Alveara.Api/Architecture/Prescribing/PrescriptionLifecycleService.cs` |
| A | `src/Alveara.Api/Architecture/Prescribing/PrescriptionPdf.cs` |
| M | `src/Alveara.Api/Architecture/Prescribing/PrescriptionWorkflowViews.cs` |
| M | `src/Alveara.Api/Architecture/Storage/IBlobStorage.cs` |
| A | `src/Alveara.Api/Architecture/Treatment/TreatmentPlanCopyIssues.cs` |
| A | `src/Alveara.Api/Architecture/Treatment/TreatmentPlanCopyPdf.cs` |
| A | `src/Alveara.Api/Controllers/BackupAlertsController.cs` |
| M | `src/Alveara.Api/Controllers/BillingStatementsController.cs` |
| A | `src/Alveara.Api/Controllers/ClinicalRecordCopiesController.cs` |
| M | `src/Alveara.Api/Controllers/ConfigurationController.cs` |
| M | `src/Alveara.Api/Controllers/DocumentsController.cs` |
| M | `src/Alveara.Api/Controllers/ExternalCasesController.cs` |
| M | `src/Alveara.Api/Controllers/PrescriptionWorkflowController.cs` |
| M | `src/Alveara.Api/Controllers/SystemStatusController.cs` |
| A | `src/Alveara.Api/Controllers/TreatmentPlanCopyIssuesController.cs` |
| M | `src/Alveara.Api/Data/AlveraDbContext.cs` |
| M | `src/Alveara.Api/Data/DataExportHistoryModel.cs` |
| M | `src/Alveara.Api/Data/DocumentModel.cs` |
| M | `src/Alveara.Api/Data/NotificationModel.cs` |
| M | `src/Alveara.Api/Data/PrescriptionWorkflowModel.cs` |
| M | `src/Alveara.Api/Data/TreatmentPlanModel.cs` |
| M | `src/Alveara.Api/Data/TreatmentPlanRevisionModel.cs` |
| A | `src/Alveara.Api/Migrations/20261121172403_AddDocumentClassificationAndManageDocuments.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261121172403_AddDocumentClassificationAndManageDocuments.cs` |
| A | `src/Alveara.Api/Migrations/20261121191310_AddBackupAlerts.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261121191310_AddBackupAlerts.cs` |
| A | `src/Alveara.Api/Migrations/20261121235742_AddPrescriptionIssuesAndPrescriberDetails.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261121235742_AddPrescriptionIssuesAndPrescriberDetails.cs` |
| A | `src/Alveara.Api/Migrations/20261122014701_AddTreatmentPlanCopyIssues.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261122014701_AddTreatmentPlanCopyIssues.cs` |
| A | `src/Alveara.Api/Migrations/20261122025201_AddClinicalRecordCopyIssues.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261122025201_AddClinicalRecordCopyIssues.cs` |
| A | `src/Alveara.Api/Migrations/20261122140138_AddTreatmentPlanItemAppointmentLinksMergeScope.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261122140138_AddTreatmentPlanItemAppointmentLinksMergeScope.cs` |
| M | `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` |
| A | `src/Alveara.Api/Pdf/Templates/clinical-record-copy/v1/main.typ` |
| A | `src/Alveara.Api/Pdf/Templates/financial-legacy/v1/main.typ` |
| A | `src/Alveara.Api/Pdf/Templates/prescription/p2/main.typ` |
| A | `src/Alveara.Api/Pdf/Templates/receipt/v4/main.typ` |
| A | `src/Alveara.Api/Pdf/Templates/selftest/v1/main.typ` |
| A | `src/Alveara.Api/Pdf/Templates/statement/v4/main.typ` |
| A | `src/Alveara.Api/Pdf/Templates/templates.manifest.json` |
| A | `src/Alveara.Api/Pdf/Templates/treatment-plan-copy/v2/main.typ` |
| M | `src/Alveara.Api/Program.cs` |
| M | `src/alveara-client/src/app/AppShell.permissionAwareNav.test.tsx` |
| M | `src/alveara-client/src/app/AppShell.tsx` |
| A | `src/alveara-client/src/app/BackupAlertBanner.test.tsx` |
| A | `src/alveara-client/src/app/BackupAlertBanner.tsx` |
| M | `src/alveara-client/src/app/moduleRegistry.ts` |
| M | `src/alveara-client/src/components/dateOnly.test.tsx` |
| M | `src/alveara-client/src/pages/BackupRecoveryPage.test.tsx` |
| M | `src/alveara-client/src/pages/BackupRecoveryPage.tsx` |
| M | `src/alveara-client/src/pages/PatientWorkspacePage.tsx` |
| M | `src/alveara-client/src/pages/SystemStatusPage.test.tsx` |
| M | `src/alveara-client/src/pages/SystemStatusPage.tsx` |
| M | `src/alveara-client/src/pages/backup/BackupLists.tsx` |
| M | `src/alveara-client/src/pages/backup/RestoreWizard.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingDocuments.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingStatementsPanel.test.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentEditForm.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentEntries.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentLibraryFeatures.test.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentSettingsPage.test.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentSettingsPage.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentStoragePage.tsx` |
| M | `src/alveara-client/src/pages/documents/Documents.css` |
| M | `src/alveara-client/src/pages/documents/DocumentsPanel.test.tsx` |
| M | `src/alveara-client/src/pages/documents/DocumentsPanel.tsx` |
| M | `src/alveara-client/src/pages/documents/documentsTestHarness.ts` |
| A | `src/alveara-client/src/pages/exports/ClinicalRecordCopyButton.test.tsx` |
| A | `src/alveara-client/src/pages/exports/ClinicalRecordCopyButton.tsx` |
| M | `src/alveara-client/src/pages/notifications/NotificationsPage.tsx` |
| M | `src/alveara-client/src/pages/prescription/PrescriptionForm.tsx` |
| A | `src/alveara-client/src/pages/prescription/prescriptionText.test.ts` |
| M | `src/alveara-client/src/pages/prescription/prescriptionText.ts` |
| M | `src/alveara-client/src/pages/prescription/workflow/DraftComposer.tsx` |
| M | `src/alveara-client/src/pages/prescription/workflow/PrescriptionRecordList.tsx` |
| M | `src/alveara-client/src/pages/prescription/workflow/PrescriptionWorkflowPanel.test.tsx` |
| M | `src/alveara-client/src/pages/prescription/workflow/PrescriptionWorkflowPanel.tsx` |
| D | `src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/TreatmentPlanPatientCopy.test.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/TreatmentPlanPatientCopy.tsx` |
| M | `src/alveara-client/src/services/backupApi.ts` |
| M | `src/alveara-client/src/services/billingStatementsApi.ts` |
| M | `src/alveara-client/src/services/documentsApi.ts` |
| M | `src/alveara-client/src/services/patientExportsApi.ts` |
| A | `src/alveara-client/src/services/pdfApi.test.ts` |
| A | `src/alveara-client/src/services/pdfApi.ts` |
| M | `src/alveara-client/src/services/prescriptionWorkflowApi.ts` |
| M | `src/alveara-client/src/services/treatmentPlansApi.ts` |
| M | `tests/allergy-wording-guard.test.mjs` |
