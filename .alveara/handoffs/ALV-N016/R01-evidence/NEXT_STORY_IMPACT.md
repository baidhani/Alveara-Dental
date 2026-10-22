# Next-story impact - ALV-N016 R01

Next scheduled item: **item 42 - `ALV-N015`** (Notification Center and Internal Messaging), then item 43 `STORY-009`. Neither starts before this story's final approval and closure.

- **New tests that need a database follow the existing patterns and get the template automatically** (derive from `SafetyTestBase`, or use `IClassFixture<TestDatabaseFixture>`, or build a `TestDatabaseFixture`). A test that must exercise the real migration path (it rolls the schema back or forward, or tests the fixture) must be added to `RealMigrationInventory` and be marked `[RealMigrations]` (a `SafetyTestBase` class), use `RealMigrationTestDatabaseFixture`, or use the internal hook constructor; `RealMigrationInventoryTests` fails otherwise.
- **A new migration needs nothing special**: the template is built from the code's migrations at the start of every run and checked against them.
- **Telemetry:** set `ALVERA_TEST_TEMPLATE_TELEMETRY` to a file to keep the template's structured events; `role: run` is the run's own template.
- **Measurement switch:** `ALVERA_TEST_TEMPLATE=off` runs every fixture on the real create-and-migrate path (for before and after timing only; never in a qualification run).
- **Not changed:** serial execution, production code, assertions, the test selection. Parallelism, shared databases and SQL tuning stay out of scope (milestone M4).
- **Process:** `HANDOFF_SCHEMA.md` v3 applies: this story is two-stage; ALV-N015 may begin only after the final approval closure.
