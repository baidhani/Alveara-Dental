# ALV-N022 A2-R01: parent and dependency regression

- Parent regression: n/a (new production story). Broad regression `DEFERRED_TO_ALV-N028`; ledger rows added.
- Dependency (focused): every test class of the changed service (DiagnosisServiceTests, DiagnosisApiTests) and of the completion paths whose behaviour A2 must leave unchanged (CompletedProcedureServiceTests, CompletedProcedureAdoptionServiceTests, CompletedProcedureAmendmentTests): 125/125. The adoption picker's client test file in full (12 tests).
- Not run: other diagnosis classes (structure, schema, reconciliation), other completion classes, other walkthroughs (diagnoses, completion, completion-adoption), mocked Playwright. Stage 2 will be defined by the reviewer.
