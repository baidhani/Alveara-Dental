# Test results — ALV-005-C01 R01

All runs on the final implementation commit `cd56749b5b48f86225995197969d9ee93171055a` (real SQL Server LocalDB for backend and real-backend runs; no mocks of the database).

| Suite | Command | Result |
|---|---|---|
| Backend, full | `dotnet test` in `src/Alveara.Api.Tests` | **1,232 of 1,232 passed**, 0 failed, 0 skipped (1 h 4 m). Up from 1,072; 160 new. |
| Frontend, full | `npx vitest run` | **684 of 684 passed** (56 files). Up from 599; 85 new. |
| Type check / build / lint | `npx tsc -b`, `npm run build`, `npm run lint` | tsc exit 0, build ok, lint 0 errors (one `set-state-in-effect` warning in `TemplatesPage.tsx`, the same pattern as four existing pages) |
| Mocked browser | `npx playwright test` | **94 of 94 passed** (desktop and tablet projects; includes the accessibility specs) |
| Repository checks | `node --test tests/*.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.clinical-companion.config.ts` | **9 of 9 passed**, run twice on the final code (earlier runs stopped at test-side races, see Disclosures); 18 axe scans (9 screens/states x light and dark), **0 critical/serious** violations |
| Parent walkthrough | `playwright.clinical.config.ts` (STORY-005, unchanged spec) | **9 of 9 passed** on the new build |
| Neighbour walkthroughs | `playwright.workspace.config.ts`, `playwright.forms.config.ts` | workspace **14 of 14**, forms **10 of 10** |

## New backend tests (160)
| Class | Passed | Covers |
|---|---:|---|
| `ClinicalRecordSchemaTests` | 24 | database rules: kinds/statuses (per kind, case-sensitive), one live item per name, no delete, append-only item history and events, review states, vitals checks and edit/void/delete triggers, finalized freeze of notes and vitals, template uniqueness, signature stamp, addendum section |
| `ClinicalRecordServiceTests` | 35 | capture with attribution, status lifecycle, history-preserving correction, entered-in-error, quiet repeats, duplicates, validation, unknown/not-reviewed/none-known semantics, stale-after-change, encounter linkage, audit (PHI-free, audit-failure stores nothing), concurrency (stale edit, six simultaneous adds/withdrawals/status changes) |
| `EncounterNotesServiceTests` | 62 | notes (autosave sequence, quiet repeats, limits, stale, finalized/signed refusals, DB bypass), templates (CRUD, uniqueness, apply, required notes, starter text), signing (blocked by required notes, lock, unsign, finalize), vitals (validation ranges, key replay, void, immutability, audit), amendments (section, replay mismatch, failure) |
| `ClinicalRecordApiTests` | 30 | 401 for every new endpoint, role matrices (read / write / templates), full workflow over HTTP, CSRF, row version, concurrency 409, idempotency keys, stable refusal shapes |
| `ClinicalPermissionTests` (extended) | +10 | `ManageClinicalTemplates` roles and ordinal |

## New frontend tests (85)
`ClinicalRecord.test.tsx` 31 (record, statements in words, change/remove/history, templates page, every failure path, read-only role, axe), `ClinicalNotes.test.tsx` 23 (template, autosave, refused/dropped/stale saves, signing, vitals, finalized read-only, amendment timeline, read-only role, axe), `clinicalNotesContrast.test.ts` 31 (WCAG AA contrast of every new tint, sticky indicator, notes, links).

## Disclosures
- Three earlier runs of the new walkthrough each stopped at a **test-side** race while it was being built (a text match that also matched an open textarea; a status text that also appeared in the history list; a "Saved." text already showing on the other note box). The tests now wait on the save indicator. After those fixes the walkthrough passed 9 of 9 on two consecutive runs on the final code (`run-output.txt` and `run-output-second-run.txt`).
- A product race (simultaneous adds under a "none known" statement) was found by review and fixed before the final runs; its test fails without the fix (mutation-checked) and passes with it.
- Clinical screens were exercised at desktop width only; there is no tablet-width run for them.

Raw outputs are in `artifacts/` (`test-runs/`, `companion-walkthrough/`, `regression/`).
