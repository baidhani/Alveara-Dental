# ALV-N022 A1-R01: parent and dependency regression

- **Parent regression:** n/a (new production story). Broad regression `DEFERRED_TO_ALV-N028`; rows added to `.alveara/PROGRAM_REGRESSION_LEDGER.md`.
- **Dependency regression run (focused):** every existing test class of the touched subsystems - clinical record (service, API), encounters (service, API, schema), allergy check, safety context: 201 earlier tests, all passing after 21 approved-behaviour rewrites (232 with the new tests); the three real-backend walkthroughs that drive the encounter screen and the banner (clinical, safety, clinical-companion), all passing on `6414ffa`.
- **Not run:** the full backend suite, full vitest, build, lint, mocked Playwright (the new spec is in its real-backend exclusion list), other walkthroughs, the chain.
- **Subsystems touched that other stories read:** the encounter detail and list responses (additive fields; moved kinds no longer populate `entries` for new encounters), error bodies (additive `existingItem`), the clinical-record service (allergy canonicalization, OD-04), the safety banner (encounter copy removed).
