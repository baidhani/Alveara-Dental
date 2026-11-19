# ALV-N024 R01: parent and dependency regression

- **Parent regression:** n/a (a new production story in the stabilization programme). Broad regression is `DEFERRED_TO_ALV-N028`; not release-certified.
- **Dependency (focused, on `1c1184b`):** server 338/338.
  - Every billing class the change touches (the documents, template versions, builder, reconciliation, statements API, aging, ledger schema/service/API, refunds schema/migration/service, responsibility, billing service).
  - The new receipt, concurrency, payer and answer classes.
  - The migration inventory.
  - `TreatmentPlanAppointmentLinkMigrationTests`, whose roll-back passes through this story's Down.
- **Also on `1c1184b`:** client billing and household vitest 83/83; repo checks 58/58; the new browser spec 7/7.
- **Not run (by the route):**
  - the full backend suite, all other `*Tests`, the full vitest and mocked Playwright suites;
  - other real-backend walkthroughs, including `billing-real-backend`, `billing-ledger-real-backend` and `billing-statements-real-backend` (the last two updated in source only);
  - the chain.

  These belong to ALV-N028.
