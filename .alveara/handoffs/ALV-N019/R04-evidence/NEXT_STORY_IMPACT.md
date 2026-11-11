# ALV-N019 R04: next-story impact

Next scheduled item: ALV-N020 (item 57), not started and not authorized until ALV-N019 is `COMPLETE`.

- **Test infrastructure:** every fixture owner now initialises through `InitializeOwnerAsync`; a new test class that sets up after the fixture must do the same, and `FixtureOwnerGuardTests` fails otherwise. The warm-up adds one pooled open per fixture (milliseconds; a full clone cycle measured about 0.3 s, mostly the restore).
- **SqlClient upgrade:** `SqlClientPoolPruner` finds an internal SqlClient timer by name; its mechanism test fails loudly if a SqlClient upgrade renames it, which then needs a deliberate update of the preflight.
- **Test baseline:** the R02 Stage 2 `backend-tests.csv` (5,697 ids) stays the baseline; R04 adds 18 ids (`artifacts/new-test-ids.txt`); the R04 Stage 2 run (after approval) produces the next baseline file.
- Everything else in the R01, R02 and R03 notes still holds.
