# ALV-N021 A2-R01: parent and dependency regression

Route: post-Gate-E deferred-regression route (execution plan, "Binding post-Gate-E stabilization regression exception (2026-11-11)"). A2 is reviewed on focused proof plus the final evidence run; `fullRegression: DEFERRED_TO_ALV-N028`; not release-certified.

## Parent and dependencies

| Story | Relation | What A2 ran against it |
|---|---|---|
| ALV-N020 (dependency) | identity, session, staff lifecycle | every `*ApiTests` class (F11, 1020/1021, the one failure a pre-A2 stale count, fixed); the chain stages 0-2 (F10, 40/40 incl. the earlier stages) |
| STORY-011 / ALV-011-C01 | the visit flow A2 replaces | `VisitStateMachineTests` brought to the A2 table (their STORY-011 expectations contradict OD-01..OD-05, OD-08..OD-10, each change cited); `PatientFlowApiTests`, `PatientFlowLifecycleTests`, `VisitBoardTests`, `VisitWorkflowTests`, `VisitOccupancyTests`, `VisitPermissionTests` in F8 (747/747); board and flow walkthroughs (F9) |
| STORY-017 / ALV-017-C01 (patient merge) | merge guard on Appointments | `PatientMergeSchemaTests` found the M1 gap; M6 fixes it; F8 includes the class |
| ALV-007-C02 / billing | CollectPayments, checkout financial item | `BillingApiTests`, `BillingLedgerApiTests` (F8), all billing `*ApiTests` (F11) |
| ALV-N003 configuration | blocked time, practice settings | `ConfigurationApiTests`, `PracticeConfigurationServiceTests`, `ConfigurationHubPage.test.tsx` |
| ALV-N006 reports | cancellations report cut by category | reports walkthrough 7/7 (F9) |
| ALV-N019 chain host | clock-advance route (owner decision) | `ChainClockAdvanceTests` (F8), chain 40/40 |

## Pre-A2 defects the final evidence run found (each fixed in its own commit, owner-approved)

| Commit | Found by | Origin | Fix |
|---|---|---|---|
| `a2cc611` | mocked Playwright `architecture-map.spec.ts` | ALV-N020-C02 entered the ledger (`0fa4e2c`) without a story-catalog entry | catalog entry added |
| `265331c` | repo check `brand-inventory.test.mjs` | ALV-N020 (`21b40fc`) moved the unchanged theme key to `useAppTheme.ts` | inventory names the new file |
| `24417e1` | `BackupApiTests` | ALV-N020-C01 (`5e962b9`) added a fifth backup asset class | test asserts the full managed list |

None of the three changes product behaviour.

## Not run (owned by ALV-N028)

The full serial backend suite and the 35 real-backend walkthroughs outside A2; obligations listed in `.alveara/PROGRAM_REGRESSION_LEDGER.md` (section ALV-N021 attempt A2).
