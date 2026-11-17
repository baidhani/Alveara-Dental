# ALV-N022 A1-R01: test results (focused proof; 2026-11-17, Windows 11, local SQL Server, Chromium)

Implementation `6414ffa`. Every figure below is from a run on that tree unless marked otherwise.

## Final runs

| Group | Command / route | Result | Duration |
|---|---|---|---|
| Backend focused (9 classes) | `dotnet test src/Alveara.Api.Tests --filter` AllergyCheckTests, ClinicalEncounterSchemaTests, ClinicalRecordApiTests, ClinicalRecordServiceTests, EncounterServiceTests, EncountersApiTests, SafetyContextServiceTests, EncounterChartRoutingTests, EncounterChartApiTests | **232 / 232 passed** | 2 m 51 s (176 s wall) |
| Client vitest | `ChartSectionPanel.test.tsx`, `safetyApi.withSafetyNotice.test.ts` | **12 / 12** | a few seconds (on `ff68e9d`; neither file nor its subject changed in `6414ffa`) |
| Repo guard | `node --test tests/safety-notice-guard.test.mjs` | **3 / 3** | < 1 s (on `ff68e9d`; unchanged since) |
| Type check | `tsc -b` (client); `dotnet build` (0 errors; no warning in a touched file); spec files `tsc --noEmit` | clean | - |
| Walkthrough: encounter-chart (new) | `Invoke-RealBackendWalkthrough.ps1 -Config playwright.encounter-chart.config.ts` | **1 / 1** | 27 s |
| Walkthrough: clinical (rewritten) | `... playwright.clinical.config.ts` | **9 / 9** | 57 s |
| Walkthrough: safety (rewritten) | `... playwright.safety.config.ts` | **11 / 11** | 51 s |
| Walkthrough: clinical-companion (unchanged) | `... playwright.clinical-companion.config.ts` | **9 / 9** | 62 s |

Each walkthrough runs on its own fresh database. Per-test backend list: `artifacts/backend-focused-tests.csv` (derived from the trx; 232 rows, all Passed).

Per class: AllergyCheckTests 27, ClinicalEncounterSchemaTests 34, ClinicalRecordApiTests 30, ClinicalRecordServiceTests 35, EncounterChartApiTests 10 (new), EncounterChartRoutingTests 20 (new), EncounterServiceTests 38, EncountersApiTests 25, SafetyContextServiceTests 13.

## History of the runs (in order)

1. Diagnostic run of the 201 existing tests on the first A1 code: 177 passed, 24 failed (2 m 36 s). Classified: 3 defects in my A1 code (fixed in code, tests unchanged), 21 expected from the approved behaviour (rewritten, none loosened).
2. 227 / 227 after the fixes and rewrites, plus 26 new (2 m 44 s). Vitest 12/12. Guard: first run failed on a false match (perio's own `/entries` route); the pattern was narrowed with planted cases, 3/3.
3. Walkthroughs: the first batch never started (my wrapper used PowerShell's reserved `$args`); the second failed step 4 of the new spec (it assumed the add form closes after a refusal; it stays open by design) - test corrected; the third: 1/1, 9/9, 11/11, 9/9.
4. Negative controls C1-C8 (below).
5. Proof-gap pass against briefing section 10: the contradiction rule completed in code; 5 server tests and a Safety-tab browser step added. 232 / 232; brief controls B1-B3; the new spec failed once (the unsaved-changes guard blocked leaving with the add form open - correct behaviour; the spec now asserts it), then 1/1; the three other walkthroughs re-run on the final tree.

## Rewritten earlier tests (21; same intent, none loosened)

- `EncounterServiceTests` (12): A_documented_encounter (reads the chart and provenance; one EncounterEntry for dental), A_none_reported_section (dental keeps the encounter rule; chart rule in a new test), Adding_the_same_entry_twice, Fixing_only_the_capital_letters, An_entry_can_be_changed, A_removed_entry, Six_simultaneous_adds (all on dental history, same assertions), Finalizing_records_who_and_when (also: chart item and provenance untouched), Every_documentation_change_is_audited (encounter audit and chart audit separately; history = sum), Quiet_repeats (each count against its own before), If_the_audit_entry_cannot_be_written (both paths), A_stale_editor (chart items).
- `EncountersApiTests` (5 tests, 8 cases): role theory (4 cases), clinician workflow, change/remove (dental), stale editor, audit. The role-write test was strengthened (chart count).
- `ClinicalRecordServiceTests` (1): none known refused only with an active item; allowed with only resolved items; unknown keeps the empty-section rule.
- `A_section_with_nothing_to_report` passed unchanged after the who/when fix.

## New tests

- `EncounterChartRoutingTests` (20): routing per kind; allergy canonicalization; active match; resolved match with explicit reactivation; quiet replay; six simultaneous encounter adds -> one record; two simultaneous chart adds with different values -> one record, loser gets `existingItem`; allergy-only inner-space rule vs medications and history; medication confirm-current; medication update appends a version recorded here; removal only of an item added here; another patient's item / encounter refused; legacy entries read-only with the legacy-mark contradiction; this encounter's none known flagged when an item is added later; the chart's none known flagged beside an active item; finalized provenance unchanged after a chart change and append-only refusal; restated none known recorded; withdraw only a statement made here; reactivating withdraws none known; stale encounter editor refused.
- `EncounterChartApiTests` (10 cases): 401 on both new routes; 7-role matrix on both; CSRF on both; 409 `duplicate_item` body with `existingItem` (absent on other refusals).
- `EncounterServiceTests` (+1): on the chart, adding withdraws this encounter's none reported; none reported refused with an active item.
- Client: `ChartSectionPanel.test.tsx` (9), `safetyApi.withSafetyNotice.test.ts` (3). Repo: `safety-notice-guard.test.mjs` (3).
- Browser: `encounter-chart-real-backend.spec.ts` (1 test, 6 steps).

## Negative controls (one break at a time; the named test run; restored from a byte backup; SHA-256 compared)

| # | Planted break | Expected to fail | Result | Restored |
|---|---|---|---|---|
| C1 | `OnChart` -> false (allergy etc. back to the encounter) | routing test | caught | identical |
| C2 | `duplicate_item` without `existingItem` | active-match test | caught | identical |
| C3 | none known allowed with an active item | chart none-reported test | caught | identical |
| C4 | remove an item not added here | removal test | caught | identical |
| C5 | legacy entries editable | legacy read-only test | caught | identical |
| C6 | chart write does not touch the encounter | stale-editor test | caught | identical |
| C7 | `confirmSectionCurrent` not wrapped | repo guard | caught ("not wrapped ... confirmSectionCurrent") | identical |
| C8 | a status preselected in the reactivate form | panel vitest | caught | identical |
| B1 | brief (a): duplicate lookup limited to the encounter | active-match test | **survived** - the unique index still refuses and returns `existingItem` | identical |
| B2 | brief (b): `EncounterId` dropped from the version | routing test | caught | identical |
| B3 | brief (c): allergy name rule applied to medications | boundary test | caught | identical |

Each server control failed exactly its 1 targeted test with no compile error. Outputs: `artifacts/negative-controls/`. `git status` identical before and after.

## Not run
Full backend suite, full vitest, build, lint, mocked Playwright, all other walkthroughs, the chain (`DEFERRED_TO_ALV-N028`); browsers other than Chromium.
