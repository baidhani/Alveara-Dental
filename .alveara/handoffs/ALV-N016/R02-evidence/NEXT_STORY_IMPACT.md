# Next-story impact - ALV-N016 R02

Next scheduled item: **item 42 - `ALV-N015`**, blocked until this story's final approval and closure.

- **Rollback tests start from a clone.** A new test class that rolls the schema back and forward should derive from `SafetyTestBase` (or use `IClassFixture<TestDatabaseFixture>`) and be named in `RealMigrationInventory` under `RollsTheSchemaBackAndForwardOnAClone`; a test whose subject is the migration pipeline from an empty database or the fixture itself uses `RealMigrationTestDatabaseFixture` (or the hook constructor) and is named under the other two groups. `RealMigrationInventoryTests` fails otherwise.
- Otherwise as `R01-evidence/NEXT_STORY_IMPACT.md` (the template is built from the code's migrations each run; telemetry and the measurement switch).
- **A production race in billing's ledger load** was found (`DECISION_REQUEST.md`, section 2); it is a candidate for a billing story or correction and is not part of ALV-N015.
