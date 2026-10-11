# ALV-015-C01 R02 - Next-story impact

- **The limitation recorded in R01 is closed.** The catalog's usage guard now counts treatment plans. Any later story that stores a reference to a catalog procedure (billing, completed-treatment charting, claims) should register its own `IProcedureUsageSource` the same way (a class beside its feature, one line in `Program.cs`, a test through the production wiring); the catalog and its page need no change.
- Billing and later stories that read plans: nothing in the plan contract changed. A completed plan procedure still posts no charge.
- Test helpers: build the catalog with `TreatmentPlanTestBase.CatalogFor(db)` (both sources) rather than constructing `ProcedureCatalogService` with one.
- Behaviour to know: inactivating a procedure that any plan has ever referred to now needs `acknowledgeUsage`, including in tests written earlier.
- STORY-014 is not started and must not start until the review of R02 is recorded.
- Housekeeping recommended to the owner, outside this story: a deterministic version of `BackupCryptoTests.Altering_any_part_of_the_file_is_detected_and_never_yields_plaintext`, and a larger timeout margin for the periodontal vitest tests.
