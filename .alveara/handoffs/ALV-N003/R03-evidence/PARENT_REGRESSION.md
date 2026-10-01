# ALV-N003 R03 — Parent Regression

**N/A — justified.** `ALV-N003` is a New Production story with no course parent (`parentCourseStory: null`).

Dependency regression: this attempt changed only frontend files confined to the configuration editors (`AvailabilityTab`, `PracticeTab`, `ConfigEntityPanel` and its CSS) and their tests; no backend, routing, auth, audit or shared-shell source changed. The full backend (262), frontend (122) and real-browser (11) suites pass, including every `ALV-001-C01`, `ALV-002-C01`, `ALV-N002` and `ALV-N009` regression.
