# ALV-N026 R01: next-story impact

## ALV-N027
These unassigned findings could fit there:
- U-018: the flaky `BillingStatementsPanel.test.tsx`;
- U-019: the readable export lists clinical document names;
- U-021: the plan-copy screen's practice-header fetch swallows its error.

The prescriber-details staff screen is also still to build (the API route exists).

## ALV-N028 (full regression)
- See the known risks in PARENT_REGRESSION.
- Exercise the six migrations' Down paths on a populated copy.
- Add real-browser print and missing-file coverage, as plan item 63 requires.
- The PDF engine must be provisioned in each environment (`scripts/Install-PdfEngine.ps1`; Test-Data is provisioned, Dev-Data is not).
- Every PDF test reads rendered text with PdfPig (test-only dependency).

## ALV-N013 (installer)
Installing the PDF engine and its runtime in production is still owed. The firewall rule and the service account also remain with that story.

## Every future patient-id column
It must be either:
- registered in `PatientOwnedData`, with a merge-scope trigger and the stand-aside line; or
- declared in `PatientIdExceptions`, with its merge handling, its export handling and a reason.

PatientOwnedDataTests, ExportDomainCatalogTests and PatientIdExceptionTests check this.

## Every future table
It must be named in `ExportDomainCatalog`.

## Every future printed material
It is a server PDF from a pinned template (`templates.manifest.json`). Its issue row is append-only, and the material is printed through `PatientAccessRecorder.RecordPrintAsync`.

Template text that must keep an exact wording (with apostrophes, for example) goes in Typst strings, not markup.

## Dev database
`AlveraDev` lacks the ALV-N025 and ALV-N026 migrations. They are applied only with the owner's permission.
