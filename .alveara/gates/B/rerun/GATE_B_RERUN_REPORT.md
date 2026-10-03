# Alveara Dental - Gate B (Patient, Scheduling, and Visit Entry Ready) - Rerun Evaluation Report

**Decision: PASS** - all seven blocking criteria pass on the corrected integrated repository; the predeclared thresholds in `.alveara/QUALITY_GATES.md` were not changed. This is the rerun of the same gate after its first evaluation (FAIL / CHANGES_REQUIRED, finding B-REV-01). The first evaluation's report, evidence, independent review and package are preserved unchanged: `.alveara/gates/B/GATE_B_REPORT.md`, `.alveara/gates/B/evidence/`, `.alveara/gates/B/reviews/e0a8629.md`, `.alveara/dist/Alveara_Gate_B_e0a8629.zip` (SHA-256 `1D8FB48F8FA4EDE5EB438F81D45870B0FC0E655C847CB0B29199925DEBED0A81`).

Date: 2026-10-03 (America/Chicago). **Final head under test: `13504387a9ae123f9014c872cf7ade8f74e74ae9`** (clean working tree at the start of the evidence run). The product code is unchanged since the first evaluation's `17130f7` apart from the corrections below, which touch only test configuration, one test, gate tooling and `PROGRESS.md`. All data is synthetic; no real PHI and no secrets are in the evidence.

## What changed since the first evaluation

| Commit | Change |
|---|---|
| `c416114` | Recorded the independent review (`FAIL / CHANGES_REQUIRED`) and set the Gate B rows BLOCKED; nothing deleted. |
| `6f0c4a4` | **B-REV-01 correction.** `src/alveara-client/vitest.config.ts` now excludes `gate-*/**` (every present and future gate tooling directory) instead of only `gate-a/**`, and a new repository check `tests/gate-tooling-coexistence.test.mjs` generates the Gate B output exactly as the gate does and asks `vitest list --filesOnly` what it would run: only product tests under `src/` may appear, with no command-line exclusions. |
| `b0620bf` | Merge of the portal sync (`.colaberry/` and `artifacts/` only). |
| `1350438` | **Auth timing stabilization** (the reviews' standing condition): the recovery-key assertion in `src/alveara-client/e2e/auth-real-backend.spec.ts` waits up to 30 s instead of the 5 s default, and that one test has a 90 s budget. Evidence below. |

## B-REV-01: the declared frontend command with the generated output present

- **Reproduced first**, on the reviewed head with the seven generated specs present: `npx vitest run` = `7 failed | 51 passed (58)` files, 547 tests passed (`rerun/evidence/b-rev-01-coexistence/vitest-run-AGAINST-OLD-config-with-generated-specs-fails.txt`). My first evaluation ran vitest before the tablet specs were generated, so the coexistence was never exercised - the reviewer's finding is correct and the mistake was mine.
- **The new check fails against the old configuration** (`coexistence-check-AGAINST-OLD-config-fails.txt`: vitest would run `gate-b/tablet-generated/*.tablet.spec.ts`) and **passes with the correction** (`coexistence-check-new-config.txt`).
- **On the final head, with the seven generated specs present** (`frontend/generated-gate-specs-present-during-vitest.txt`) and **no command-line exclusions**: `npx vitest run` = **51 files / 547 tests passed, 0 failed suites** (`frontend/vitest.txt`); `tsc -b`, `npm run build`, `npm run lint` exit 0; repository checks **8 of 8** (`repo-checks.txt`, the seven earlier checks plus the coexistence check).

## Authentication and recovery verification (the `ALV-N004` R06 and Gate B review condition)

The `ALV-N004` R06 review required fresh, retry-free authentication/recovery evidence and, if the timing behaviour persisted, a bounded, evidence-backed test stabilization. It persisted: the first rerun (head `b0620bf`) failed 1 of 4 default-timing runs at the recovery-key step (`auth-first-rerun-at-b0620bf/run1.txt`), so that run was not counted as clean and the stabilization was done.

- **Measurement** (`auth-stabilization/`): real RSA-3072 key generation takes 0.7-4.6 s idle (18 samples, median 2.2 s, three above 4.3 s) and 2.0-6.6 s under CPU load (18 samples, median 2.5 s, two above 5 s) - the 5 s default is inside the distribution.
- **Before** (unchanged spec, same CPU load = two concurrent full vitest suites): **5 of 6** auth walkthrough runs failed at exactly this assertion (`getByLabel('Recovery key file contents')`, 5000 ms).
- **After** (30 s bounded wait, 90 s test budget; same load): **0 of 12** runs failed at it; 11 of 12 passed 12/12.
- **Fresh evidence at default timing on an otherwise idle machine, final head `1350438`, four consecutive runs: 12 of 12 on all four, no retry** (`auth-default-timing-idle-run1.txt` ... `run4.txt`).

## Criterion results

Same walkthroughs, same method and thresholds as the first evaluation, re-run on the final head (evidence under `.alveara/gates/B/rerun/evidence/`, referred to below by directory). "Walkthrough" = a real Chromium driving the real Vite client against the real ASP.NET API and a fresh SQL Server LocalDB database, with axe (WCAG 2.0 A/AA) in light and dark themes. The detailed per-criterion findings (what each walkthrough asserts) are in the first evaluation's report and are unchanged; the rerun results are:

| # | Result | Evidence |
|---|---|---|
| B1 | **PASS** | registration **7/7**, patient workspace **14/14** (`walkthroughs-desktop/registration`, `.../workspace`); backend patient classes 117/117 (`backend/results-by-class.md`); same walkthroughs at 768 px **8/8** and **15/15** (`walkthroughs-tablet-768x1024/`). |
| B2 | **PASS** | forms **10/10** (signature integrity: later template versions leave the signed fingerprint unchanged, replay 200, different key 409; void keeps the signed copy) and board check-in readiness **15/15**; backend 125/125 including `CheckInReadinessTests` 15/15; forms at 768 px **11/11**. |
| B3 | **PASS** | workspace patient-switch on a slow connection (previous patient never shown during load) and forms wrong-patient/other-patient checks; both also at 768 px. |
| B4 | **PASS** | scheduling **10/10**, calendar **12/12** (provider/operatory/patient/hours/blocked-time conflicts refused in words, six simultaneous requests -> one appointment, reschedule with history, cancel, no-show); backend scheduler classes 131/131; at 768 px **11/11** and **13/13**. |
| B5 | **PASS** | STORY-011 flow **10/10**, visit board **15/15** (the whole eight-state chain, one patient per room); backend flow/visit classes 221/221; flow at 768 px **11/11**. |
| B6 | **PASS** | keyboard-only paths in every front-desk walkthrough (registration, workspace, scheduling, calendar reschedule, flow check-in/complete, forms signature, board confirm with focus kept); the six walkthroughs at **768 x 1024**: **69 tests pass, 64 axe scans with 0 blocking, 0 px of steady sideways page scroll**; probe control **2/2**; mocked suite at desktop and tablet widths **94/94**; course contracts exercised by their frozen tests (registration 7/7, scheduling 10/10, flow 10/10, `PatientFlow*`/`AppointmentScheduler*`/`PatientRegistration*` classes inside the 957). |
| B7 | **PASS** | `b7-defect-log.json`: items 11-17 `COMPLETE`, zero `blockingIssues` anywhere, no story in an interim state, no revalidation pending; the five pre-Gate-B corrections closed. The blocked-time read-before-lock limitation was reviewed in the first evaluation and **accepted as not high-severity** by the independent reviewer (`.alveara/gates/B/reviews/e0a8629.md`); nothing since changes that. Full backend 957/957. |

## Integrated verification

| Check | Result | Head | Evidence |
|---|---|---|---|
| Backend `dotnet test` (real SQL Server LocalDB, one database per test class) | **957 of 957**, 0 failed, 0 skipped, 47 min 10 s | `b0620bf` (differs from the final head only by `PROGRESS.md` and the auth spec; empty diff for `src/Alveara.Api`, `src/Alveara.Api.Tests`, `src/alveara-client/src`) | `backend/` |
| Frontend `npx vitest run` with generated Gate B specs present | **547 of 547** (51 files), 0 failed suites | final | `frontend/vitest.txt` |
| `tsc -b` / build / lint | exit 0 / 0 / 0 | final | `frontend/` |
| Mocked Playwright (desktop + tablet projects) | **94 of 94** | final | `frontend/mocked-playwright.txt` |
| Repository checks | **8 of 8** (incl. the coexistence check) | final | `repo-checks.txt` |
| Gate A route scans (A2 axe, 10 routes x 2 themes; A6 seven-role navigation) | **3 of 3**, 0 critical/serious | final | `route-scans/` |
| Desktop real-backend walkthroughs | registration 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15 (all pass); 74 axe scans, 0 blocking | final | `walkthroughs-desktop/` |
| Tablet-width walkthroughs | 69 tests pass; 64 axe scans, 0 blocking; 0 px sideways overflow; probe control 2/2 | final | `walkthroughs-tablet-768x1024/` |
| Authentication walkthrough, four consecutive runs, default timing, idle machine | **12 of 12 on all four** | final | `auth-default-timing-idle-run1.txt` ... `run4.txt` |

## Disclosures

- **A second, load-only timing sensitivity remains, in a different test.** In the 12-run loaded stabilization experiment, run 9 failed in the practice-configuration test (not the recovery-key step): its whole 30 s test budget was spent waiting to click "Save practice information" while the button stayed disabled (it normally takes 2-5 s). It passed in every idle run, including all eight idle runs of this rerun (four at `b0620bf`, four at the final head). The cause is **not proven**; a hypothesis consistent with the code is that React's StrictMode in the dev server starts the practice form's load twice and the slower second response resets the form after the page is already usable (the same shape as the backup-page defect fixed in `ALV-N004` R06). It belongs to `ALV-N003`'s configuration tab, is not high-severity (an unsaved admin edit can be lost under heavy load, no data is corrupted), and was **not changed here**; a follow-up on `ALV-N003` is recommended. The gate's authentication evidence is the idle default-timing runs above.
- **The coexistence defect was in my evaluation, not in the product:** the evidence for B1-B7 was credible, but my frontend run happened before the tablet specs were generated. The check added in `6f0c4a4` prevents recurrence for every future gate directory.
- **The test change is bounded and evidenced, not a loosening:** one assertion's wait (30 s, 4.5x the worst measured latency) and one test's budget (90 s); the assertion itself (the key box appears with a PGP private key block, then is cleared) is unchanged. It is `ALV-N004`'s test, edited under the reviewers' stated condition.
- **A load-sensitive unit test elsewhere** (`src/FormTemplates.test.tsx` "creates a template...", `ALV-N010`) timed out at 5 s in two of three heavily loaded full-suite runs during the `ALV-N004` R06 stress testing; it passed in every unloaded run, including all of this rerun. Reported, not changed.
- **Backend evidence head.** The backend suite ran on `b0620bf`, not on the final head; the final head differs from it only by `PROGRESS.md` and one Playwright spec, and the diff for every backend and client source directory is empty, so no backend result can differ.
- **The four `ALV-011-C01` SHAs** the review names are not ancestors (rebase before first push); on-main equivalents and the mapping are in `.alveara/BUILD_STATE.md` (`851a32b`). Every other reviewed SHA is an ancestor.
- **Limits of the evidence** (unchanged from the first evaluation): walkthroughs run against the Vite dev server proxying a real API; axe scans are representative of the states exercised; the mocked suites do not substitute for the real-API walkthroughs; the tablet variants apply the theme through the same attribute/storage key the toggle sets in the calendar and flow runs because the open appointment drawer covers the toggle at 768 px (see `gate-b/README.md`); the board refreshes by 15 s polling.

## Reproduce

From `src/alveara-client`: `node gate-b/make-tablet-specs.mjs`, then `npx vitest run` (must pass with the generated specs present), `npx tsc -b`, `npm run build`, `npm run lint`, `npx playwright test`; real-backend walkthroughs per `docs/testing/REAL_BACKEND_E2E.md` (a fresh migrated database each): `npx playwright test --config=playwright.<patients|workspace|forms|schedule|calendar|flow|board|auth>.config.ts`, `--config=gate-a/playwright.gate-a.config.ts`, and the tablet variants per `gate-b/README.md`; from the repository root: `dotnet test src/Alveara.Api.Tests` and `node --test tests/*.test.mjs`.

## Repository state and next action

Final head under test `13504387a9ae123f9014c872cf7ade8f74e74ae9`; the rerun evaluation record is the commit that adds this report. **Gate B rerun PASS: the next execution item (18, `STORY-005`) is authorized once this rerun is independently approved**, and it is an original course story - execute it in the course portal through its current portal-generated prompt (do not substitute an ALV prompt), record `STORY-005: record course completion` once the portal has verified it, then continue to item 19 (`ALV-005-C01`). No product code, course state or story state was changed by this evaluation.
