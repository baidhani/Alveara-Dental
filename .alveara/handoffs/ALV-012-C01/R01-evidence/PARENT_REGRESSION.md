# Parent regression - ALV-012-C01 R01

**Parent course story: `STORY-012`** (portal-verified 2026-10-05T14:22:15Z at `26354bd`, 3 of 3 criteria). Its completion contract is immutable. This attempt extended its service, views, panel and history compatibly; its Done Means were run **again after that change**.

| Parent Done Means | Proof on this tree |
|---|---|
| Given a patient record, when periodontal charting is performed, then the system records probing depth, recession, and bleeding | `PerioServiceTests` (a chart with depth, recession and bleeding, in site order, derived attachment loss) and `PerioApiTests` (the workflow over HTTP) pass unchanged in the full run; frontend `Perio.test.tsx` "saves depth, recession and bleeding..." passes unchanged; the **unchanged STORY-012 walkthrough** step `ACCEPTANCE 1`. |
| Given incorrect charting data, when saved, then the system rejects the input and prompts for correction | `PerioRulesTests`, `PerioServiceTests` ("Incorrect data is refused naming every problem and nothing at all is saved"), `PerioApiTests` and `Perio.test.tsx` ("incorrect data (acceptance 2)") pass unchanged; the unchanged walkthrough step `ACCEPTANCE 2`. |
| Trust: all charting entries are logged with user ID and timestamp | `PerioServiceTests` ("Every chart is logged with the user and a timestamp..." and the failed-audit rollback), `PerioApiTests`; the unchanged walkthrough step `TRUST` (one audit entry per chart, none for refused or repeated saves, no clinical content). A chart finalized from a session writes the same single `PerioExamRecorded` entry. |

## What this attempt changed that a parent test could see
- `PerioReadingInput` and the chart views gained optional trailing fields (suppuration, plaque, teeth, links); every existing call compiles and behaves as before, and a chart saved without them reads the same.
- The history response now also carries `teeth` and `links` arrays (empty for old charts); the screen shows the extra columns only when a chart has them.
- `PerioPanel` gained an "Entry method" switch with the grid as the default; the grid's markup and behaviour are unchanged.
- Parent test code touched: none. The shared fake API was extended; the STORY-012 tests that use it pass unchanged.

## Dependencies
`ALV-005-C01` and `ALV-N011` and the shared patient workspace: the safety strip stays in the patient header on every new screen; the workspace, clinical, clinical companion, safety, board, flow, forms, patients, calendar, schedule, auth and both odontogram walkthroughs passed on this tree (see `TEST_RESULTS.md`). The odontogram is read (never written) to find teeth recorded missing.
