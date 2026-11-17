# ALV-N022 A3-R01: parent and dependency regression

- Parent regression: n/a (new production story). Broad regression `DEFERRED_TO_ALV-N028`.
- Dependency (focused): every test class of the changed services (Clearance, Safety API and context, PerioSession service and API, Prescription service and workflow, AllergyCheck, Diagnosis service and API) plus `TypedLinkGuardMigrationTests` and `RealMigrationInventoryTests`: 297/297. The client test files of every touched screen and their neighbours (45 files): 917/917 on `63f74ff`. The new walkthrough and ten neighbour walkthroughs whose screens or text A3 touched: 110/110.
- Not run: other backend classes, the full vitest and mocked Playwright suites, other walkthroughs, the chain. Stage 2 will be defined by the reviewer.
