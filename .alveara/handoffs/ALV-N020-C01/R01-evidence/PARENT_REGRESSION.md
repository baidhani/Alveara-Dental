# ALV-N020-C01 R01: parent and dependency regression

- **Parent course story:** none (`parentRegression: "n/a"`).
- **Dependency ALV-N020** (COMPLETE): its sign-in, session, outage and identity behavior is untouched by design and was re-proven: chain stage 0 + stage 1 20/20 (24 PASS) and the auth walkthrough 12/12 on the new sign-in page; LoginPage, AuthContext, App.routeGuards and App.disconnected tests pass.
- **Backup subsystem (ALV-N004/N019 fixture):** a new asset class changes what a backup covers; the backup service, restore, document backup/restore tests and the auth walkthrough's backup step pass, with two expectations updated to include the new class.
- **Permission subsystem (ALV-N017):** untouched role policy; 103/103 permission-related tests.
- **Broad regression:** `DEFERRED_TO_ALV-N028`; obligations in `.alveara/PROGRAM_REGRESSION_LEDGER.md`.
