# ALV-N021 A1-R01: parent and dependency regression

- **Parent course story:** none (new production story). `n/a`.
- **Dependency:** ALV-N020 (COMPLETE). A1 touches none of its subsystems (sign-in, session, staff lifecycle).
- **Neighbouring behaviour re-run (focused):** the existing classes of every changed area ran with the new tests: `BillingDocumentBuilderTests` and `BillingDocumentServiceTests` (ALV-007-C02 statements/receipts; one assertion and one token version adjusted for the new current version, see `TEST_RESULTS.md` run 2), `BillingTimelineAgingTests`, `DiagnosisStructureServiceTests`, `TreatmentPlanStateServiceTests`, `PrescriptionServiceTests`, `ProcedureCatalogLifecycleTests`, the client `TreatmentPlanAnswers` tests.
- **Changed existing assertions (disclosed):** `BillingDocumentBuilderTests` now passes template version 1 explicitly (those tests pin the version-1 contract); `BillingDocumentServiceTests` envelope check now asserts no amount or balance in the envelope (the envelope now carries the date basis), and its "unsupported version" token uses 3 instead of 2.
- **Broad regression:** not run; `DEFERRED_TO_ALV-N028`. Rows added to `.alveara/PROGRAM_REGRESSION_LEDGER.md`. Not release-certified.
