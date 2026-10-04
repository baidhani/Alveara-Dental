# Test results — ALV-N011 R01

All runs on the final implementation commit `9a3ac31b5675c273e3a44d5815c012cc82e861e5` (real SQL Server LocalDB for backend and real-backend runs).

| Suite | Command | Result |
|---|---|---|
| Backend, full | `dotnet test` in `src/Alveara.Api.Tests` | **1,369 of 1,369 passed**, 0 failed, 0 skipped (1 h 13 m). Up from 1,232; 137 new. |
| Frontend, full | `npx vitest run` | **733 of 733 passed** (59 files). Up from 684; 49 new. |
| Type check / build / lint | `npx tsc -b`, `npm run build`, `npm run lint` | tsc exit 0, build ok, lint 0 errors (one `set-state-in-effect` warning pattern shared with existing pages) |
| Mocked browser | `npx playwright test` | **94 of 94 passed** |
| Repository checks | `node --test tests/*.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.safety.config.ts` | **11 of 11 passed** (re-run on the final code after the earlier runs' test-side fixes); 12 axe scans (6 screens/states x light and dark), **0 critical/serious** |
| Visit board walkthrough (modified surface) | `playwright.board.config.ts` | **15 of 15 passed** |
| Dependency / neighbour walkthroughs | clinical (STORY-005) 9/9, companion (ALV-005-C01) 9/9, workspace 14/14, forms 10/10, patients 7/7, flow (STORY-011) 10/10, calendar 12/12, schedule 10/10, auth 12/12 | all passed on the new build |

## New backend tests (137)
| Class | Passed | Covers |
|---|---:|---|
| `SafetySchemaTests` | 26 | database rules: categories/severities/statuses (case-sensitive), resolved-alert stamp, active uniqueness, no delete, append-only histories and acknowledgements, clearance stamps and the no-skipped-step check, open-clearance uniqueness |
| `SafetyAlertServiceTests` | 35 | every category, nothing invented, source required, lifecycle and history (who/when/why), acknowledged-versus-resolved (per person, per revision, stale refused), source item rules and stale-source flag, audit (PHI-free, audit failure stores nothing), stale edits, six-way races |
| `ClearanceServiceTests` | 21 | the workflow, waiting cannot be resolved, reasons required, cancel, missing document and later attachment, quiet repeats, uniqueness under six simultaneous requests, stale edits, audit |
| `SafetyContextServiceTests` | 13 | the downstream context from the record (provenance, severity mapping, inactive/removed excluded), gaps, ordering, resolved alerts kept, read-only, counts-only summary, the **minimal board indicator** (two booleans, batch read in one pass) |
| `SafetyApiTests` | 33 | 401 for every endpoint, read / write / acknowledge role matrices, the workflow over HTTP, CSRF, row version and revision, stable refusals, and **board authorization and minimization** (exact JSON shape, no diagnosis/allergy/medication anywhere, roles without the permission get nothing, billing 403) |
| `ClinicalPermissionTests` (extended) | +9 | `ViewSafetyIndicator` roles, ordinal, never wider than clinical read |

## New frontend tests (49)
`Safety.test.tsx` 20 (strip in the header and encounter, nothing-recorded and gaps in words, ordering and wording, acknowledge-still-active, stale acknowledge, resolve and reopen with reasons, source required, refused and dropped saves, conflict banner, history, failed load, clearance workflow incl. missing document, read-only role, axe), `FlowBoardSafety.test.tsx` 4 (indicator words, no category/name/severity, nothing without the permission, axe), `safetyContrast.test.ts` 25 (WCAG AA contrast, no status colour as text, link classes).

## Mutation checks
Forcing `includeSafety: true` made `Roles_that_run_the_board_but_do_not_hold_the_indicator_permission...` fail for both roles; removing the "must be received before resolved" rule made the service test and the API workflow test fail; both were reverted.

## Disclosures
- Earlier runs of the new walkthrough stopped at **test-side** races (a text match that also matched an open textarea, in three places) and at a real finding - the audit text disclosed the alert category (fixed, see HANDOFF). The 11 of 11 above is the run on the final code.
- The clinical and safety screens were exercised at desktop width only; there is no tablet-width run.

Raw outputs are in `artifacts/` (`test-runs/`, `safety-walkthrough/`, `regression/`).
