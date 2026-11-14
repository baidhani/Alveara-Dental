# ALV-N021 A1-R01: test results

2026-11-14. Every run had the owner's explicit Go after the code was shown (owner rule: code review before any test run); each was expected under five minutes, except the negative-control batch (~7 min expected, approved). Server: `dotnet test src/Alveara.Api.Tests --filter ...` from the repository root; client: `npx vitest run ...` and `npx tsc --noEmit -p .` from `src/alveara-client`.

| # | Commit | Command / group | Expected | Actual | Result | Raw |
|---|---|---|---|---|---|---|
| 1 | `7bf310e` tree, builder unchanged | baseline capture: `ALV_CAPTURE_V1_BASELINE=1`, `FinancialDocumentV1Baseline` capture test (refuses to overwrite) | < 5 min | 1 min 05 s | 12 documents written to `Fixtures/financial-documents-v1-baseline.json` | console (PROGRESS.md) |
| 2 | `8d65270` tree | `FinancialDocumentTemplateVersionTests`, `BillingDocumentTemplateVersionServiceTests`, `BillingDocumentBuilderTests`, `BillingDocumentServiceTests`, timeline aging | < 5 min | 1 min 28 s | first run 2 failures (stale `--no-build` binary; old test's "unsupported" V=2 is now current, changed to 3); after rebuild and fix **69 passed** | console (PROGRESS.md) |
| 3 | `a18ce99` tree | `DiagnosisStructureServiceTests`, `BillingTimelineAgingTests`, `TreatmentPlanStateServiceTests`, `PrescriptionServiceTests`; client `dateOnly.test.tsx`, `TreatmentPlanAnswers.test.tsx`; `tsc --noEmit` | < 5 min | ~1 min 40 s | server 65: 64 passed, 1 failed on my test setup (past performed time refused by the DB rule), fixed, class re-run **5/5**; client **23/23**; tsc exit 0 | console (PROGRESS.md) |
| 4 | working tree on `a18ce99` + step-4 changes | `npx vitest run src/components/dateOnly.test.tsx`; `npx tsc --noEmit -p .` | < 1 min | 12 s | **12/12**; tsc exit 0 | console |
| 5 | same | `--filter FullyQualifiedName~ProcedureCatalogLifecycleTests` (F-030) | ~1 min | 1 min 22 s | **18/18** | console |
| 6 | `a18ce99` (committed files mutated one at a time) | negative controls (script below) | ~7 min | 4 min 08 s | F-107 3 failed; F-118 1 failed; perio label 1 failed; F-184 1 failed; each file restored, `git hash-object` identical; rebuild on restored tree exit 0 | `artifacts/focused-proof/negctl_*.log`, `negctl_summary.txt` |

**Negative controls (run 6)**, one rule broken at a time, only the class that must catch it, `git checkout -- <file>` and a hash comparison after each:

| Control | Mutation | Class run | Failed |
|---|---|---|---|
| F-107 | `(born {formatDateOnly(...)})` → `(born {day(...)})` in `PrintPreview.tsx` | `dateOnly.test.tsx` | 3 (Chicago, Honolulu, Kiritimati; UTC rightly passes) |
| F-118 | `clock.ToPracticeLocal(service[...])` → `service[...]` in `BillingService.Timeline.cs` | `BillingTimelineAgingTests` | 1 of 5 |
| Perio label | `clock.ToPracticeLocal(c.RecordedAtUtc)` → `c.RecordedAtUtc` in `DiagnosisService.Structure.cs` | `DiagnosisStructureServiceTests` | 1 of 20 |
| F-184/F-218 | answer row → latest row in `TreatmentPlanService.Queries.cs` | `TreatmentPlanStateServiceTests` | 1 of 14 |

Aging negative controls are tests in the suite (run 2): `Negative_control_a_boundary_affected_version_1_statement_built_by_the_version_2_rule_no_longer_matches`, `Negative_control_a_version_1_row_rebuilt_as_version_2_does_not_match_and_is_not_shown`. The DOB-limit control is inline in its two tests.

**Raw files:** only run 6 saved its output to files. Runs 1-5 were read from the console and recorded the same day in `PROGRESS.md` (session `CC-20261109-8w3d`); there is no raw file for them. A full re-run of the A1 focused group for raw output can be done on request (about 5 minutes).

**Not run:** the full backend suite, the whole client unit suite, mocked Playwright, walkthroughs, the chain and any other broad regression (`DEFERRED_TO_ALV-N028`); lint (no lint-relevant client change beyond three small files; not measured).
