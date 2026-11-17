# ALV-N022 A2-R02: parent and dependency regression

- Parent regression: n/a (new production story). Broad regression `DEFERRED_TO_ALV-N028`; ledger line added.
- Dependency (focused): every diagnosis test class (service, API, schema, rules, reconciliation and the four structure classes) because the guard sits on the `Diagnoses` table, the two new classes, and the migration inventory: 266/266.
- Not run: completion classes (unchanged since A2-R01's 125/125), the full migration pipeline from an empty database (`MigrationTests`), other walkthroughs, mocked Playwright. The template every test used was built by the real migrations including the new one.
