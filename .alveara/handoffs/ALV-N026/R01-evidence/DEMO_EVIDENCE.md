# ALV-N026 R01: demo evidence

**No real-browser walkthrough was run for ALV-N026.** Plan item 63 names "required real-browser missing-file/print coverage", and that is not provided here (see R01 Disclosures). What the reviewer can inspect instead:

| What a person sees | How it is shown | Where |
|---|---|---|
| A statement, receipt, prescription, plan copy or clinical record copy opens as a PDF in the browser's viewer: no browser header or footer, no app URL, no blank second sheet | The server renders the PDF, and its text is read back with PdfPig (rendered text, never bytes) | BillingStatementsApiTests, PrescriptionWorkflowApiTests, TreatmentPlanCopyApiTests, ClinicalRecordCopyApiTests (`artifacts/backend-focused-tests.csv`) |
| An ended prescription prints as "CANCELLED — DO NOT DISPENSE" (or DISCONTINUED / SUPERSEDED), with its date and reason | PDF text | PrescriptionWorkflowApiTests `An_ended_prescription_is_not_printed_to_dispense_...` |
| A prescription states its allergy check: a name comparison, not a clearance | PDF text | PrescriptionWorkflowApiTests, 3 tests (run E/F) |
| The clinical record copy is titled "Clinical record copy generated as of ..." | PDF text | ClinicalRecordCopyApiTests |
| A missing or damaged file shows why and offers no Open or Download; a failed open shows an in-app message naming the practice administrator | Client component tests | `src/pages/documents` vitest |
| Administrators get a backup-failure alert in their inbox and a banner until it is resolved; System Status shows counts only | Server and client tests | BackupAlertTests, BackupAlertApiTests, BackupAlertBanner.test, SystemStatusPage.test |
| The print buttons (statement, receipt, prescription, plan copy, record copy) send one request and open the PDF in a new tab; a refusal shows the server's reason and prints nothing | Client component tests | `pdfApi.test`, TreatmentPlanPatientCopy.test, ClinicalRecordCopyButton.test, PrescriptionWorkflowPanel.test, BillingStatementsPanel.test |
