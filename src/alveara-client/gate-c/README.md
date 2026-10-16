# Gate C evidence tooling (late evaluation after items 31-38)

Gate tooling, not product tests. Methods and thresholds are the predeclared C1-C8 rows in `.alveara/QUALITY_GATES.md`, unchanged.

| File | Criterion | What it does |
|---|---|---|
| `gate-c-c2-safety-points.spec.ts` + `playwright.c2.config.ts` | C2 | Real-backend walkthrough: a synthetic patient with a Critical alert, a severe allergy, a current medication and an open clearance; as a dentist, the safety strip (with those four facts and the details link) must be visible above `<main>` on the planning, completion-review, prescribing, odontogram and diagnoses screens. |
| `src/Alveara.Api.Tests/GateCHistoryIntegrityTests.cs` | C8 | Direct persistence-layer UPDATE/DELETE attempts (own connection, own transaction, rolled back) against signed notes and addenda, diagnoses and history, odontogram history, finalized perio records and sessions, treatment-plan revisions and state history, completed procedures. Each attempt must be rejected by the database (the rejecting trigger is read from `SqlException.Procedure`) and leave its table checksum unchanged; the sanctioned path (addendum, correction, withdrawal with a reason, new chart, revision, amendment) is exercised for each class. Evidence is written to `GATE_C_EVIDENCE_OUT` as `c8-*.json` and `c8-*.md`. |

Known observation recorded, not asserted: a note that is signed but not yet finalized is protected by the service (refused with a clinical error), not by a database trigger.

## Run

C8: `GATE_C_EVIDENCE_OUT=<dir> dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~GateCHistoryIntegrityTests"`

C2 (fresh migrated database and real API, as for the other real-backend walkthroughs): `sh run_e2e.sh <freshDb> gate-c/playwright.c2.config.ts GATE_C_C2_OUT <outdir>` from `src/alveara-client`; see `docs/testing/REAL_BACKEND_E2E.md`.

The `gate-*/` folders are excluded from the product vitest run (`vitest.config.ts`) and checked by `tests/gate-tooling-coexistence.test.mjs`.
