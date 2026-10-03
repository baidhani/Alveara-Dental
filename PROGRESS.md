# PROGRESS

- [x] STORY-011: Track patient flow states from scheduled to completed
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: patient flow (Scheduled, CheckedIn, InTreatment, Completed) on the appointment with check-in / start-treatment / complete endpoints, every move logged (history + PHI-free audit) in the same save, and the calendar drawer buttons
  - Verification: 767/767 backend (`dotnet test`, 46 new against real SQL Server), 446/446 frontend (`vitest`, 12 new), `tsc -b` and production build pass, 10/10 real-backend/real-browser walkthrough (`e2e/patient-flow-real-backend.spec.ts`, axe clean in light and dark), 74 mocked browser tests; ALV-004-C01 calendar walkthrough 12/12 and STORY-004 walkthrough 10/10 re-run unchanged
  - Notes: flow is separate from booking status so a visit under way still holds its time; no too-early-to-check-in rule; only ManageAppointments roles can move a patient; no undo. PROGRESS.md was empty before this entry (earlier stories were not logged here).

- [x] ALV-011-C01: Live patient flow board and complete visit-state workflow (companion to STORY-011; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: the full visit chain (confirm, check in, ready, seat, treatment, check out, completed) with one patient per room, visit-time provider/operatory, required-form check-in readiness, new front-office/chairside permissions, and the live visit board at /flow
  - Verification: 957/957 backend (`dotnet test`, 190 new against real SQL Server), 484/484 frontend (`vitest`, 38 new), `tsc -b` and production build pass, 74/74 mocked browser, 15/15 real-backend/real-browser walkthrough (axe clean in light and dark); STORY-011 (10/10), ALV-004-C01 (12/12), STORY-004 (10/10), ALV-N010 (10/10), workspace (14/14), STORY-003 (7/7), Gate A route scans (3/3) and repository checks (7/7) unchanged; auth walkthrough 12/12 on the 3rd and 4th runs (a timing-sensitive recovery-key step failed on the first two, ALV-N004's test, untouched)
  - Notes: STORY-011's seven files are byte-identical to the verified commit; Gate B is not evaluated (F1/F2 contrast findings still open); see .alveara/handoffs/ALV-011-C01/R01.md

- [x] ALV-N001 R04: status text colours meet WCAG AA in both themes (finding F1 and siblings; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: new text-only tokens `--color-success-text` / `--color-warning-text`, dark `--color-info` lightened, and the success/warning notifications and the shell session-warning banner repointed; 4.45:1 / 3.47:1 / 4.42:1 / 4.05:1 / 4.39:1 pairings now >= 4.5:1
  - Verification: 498/498 frontend (`vitest`, 14 new in `statusContrast.test.ts`, 9 of them fail against the old values), `tsc -b`, build and lint pass, mocked Playwright 78/78 (4 new real-browser notification axe tests, which fail against the old CSS at 4.45:1 and 3.46:1), real-backend: Gate A route scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10
  - Notes: colour-only; the ALV-002-C01 banner (F2) and the removal of its three page workarounds are the next separate attempt; the system-status stale banner (ALV-N002) has the same warning-pair defect and is reported, not changed

- [x] ALV-002-C01 R05: conflict-banner title meets WCAG AA and the three page-level workarounds are removed (finding F2; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: the shared ConcurrencyConflictBanner title uses `--color-warning-text` (3.46:1 -> 5.39:1 light); the `.cal-drawer`, `.alv-workspace__form/__panel` and `.flow-board` title overrides are deleted; `patientWorkspaceContrast.test.ts` no longer asserts the title fails; `docs/SCHEDULING.md` updated
  - Verification: 502/502 frontend (`vitest`, 5 new in `conflictBannerContrast.test.ts`: 2 fail against the old CSS incl. a guard that no page re-colours the title), `tsc -b`, build and lint pass, mocked Playwright 78/78, real-backend with the overrides gone: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12; mutation check: reverting only the banner colour fails the board, calendar and workspace walkthroughs on axe color-contrast
  - Notes: colour-only; ALV-N002's `.status-stale-banner` (same warning-pair defect) is the next separate attempt

- [x] ALV-N002 R09: System Status stale-data banner meets WCAG AA (set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: `.status-stale-banner` text uses `--color-warning-text` (3.46:1 -> 5.39:1 light); new shared test helper `styles/contrastSupport.ts` (resolves tokens and CSS fallbacks, composites translucent tints)
  - Verification: 516/516 frontend (`vitest`, 14 new in `systemStatusContrast.test.ts`, 1 fails against the old CSS), `tsc -b`, build and lint pass, mocked Playwright 82/82 (4 new real-browser stale-banner axe tests driving the page clock; they fail against the old CSS at 3.46:1), real-backend: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12
  - Notes: colour-only; ALV-001-C01 (login banner) and ALV-N004 (backup warning text) are the next separate attempts

- [x] ALV-001-C01 R08: login banners meet WCAG AA in both themes (expired-session warning 3.16:1 and MFA success message; set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: `.alv-login__banner--warning` text uses `--color-warning-text`; new `.alv-login__banner--success` class (uses `--color-success-text`) replaces the inline style in `MfaSettingsPage.tsx`
  - Verification: 526/526 frontend (`vitest`, 10 new in `loginContrast.test.ts`, 4 fail against the old code), `tsc -b`, build and lint pass, mocked Playwright 90/90 (8 new real-browser banner axe runs; the warning runs fail against the old CSS at 3.16:1), real-backend: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12 (incl. MFA confirmation)
  - Notes: colour-only plus one inline style moved to CSS; ALV-N004 (backup warning text and its intermittent stale-settings test) is the next separate attempt

- [x] ALV-N004 R06: backup warning text meets WCAG AA and the intermittent stale-settings test is fixed at its cause (set to AWAITING_REVIEW, not COMPLETE)
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `.alv-backup-check--warn` text uses `--color-warning-text` (3.51:1 -> 5.2:1+ light); `BackupRecoveryPage.tsx` no longer re-runs its blocking load (which unmounts the page and the settings form, discarding an edit in progress) when the permission set arrives after the first load - it refreshes in place
  - Verification: 547/547 frontend (`vitest`; 20 new in `backupContrast.test.ts`, 1 fails against the old CSS; 1 new deterministic test in `BackupRecoveryPage.test.tsx` that fails 3/3 against the old page), `tsc -b`, build and lint pass, mocked Playwright 94/94 (4 new real-browser backup axe runs; they fail against the old CSS at 3.51:1), under load the backup test file passes 16/16 loaded runs (before: 1/16 failed; a timeout-only change: 2/16 failed), real-backend: Gate A scans 3/3, board 15/15, calendar 12/12, forms 10/10, workspace 14/14, STORY-003 7/7, scheduling 10/10, STORY-011 10/10, auth 12/12 on the third run (the recovery-key step timed out at 5 s on the first two, disclosed)
  - Notes: a wider timeout alone did NOT fix the flake (failures then took 10 s); the cause was a real reload-on-permission-change defect. FormTemplates.test.tsx 'creates a template...' also timed out at 5 s once under heavy load (ALV-N010's test; reported, not changed)

- [x] Gate B tooling: tablet-width variants of six real-backend walkthroughs with a sideways-scroll probe and its control
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `src/alveara-client/gate-b/` (generator, config, README); `.gitignore` entry for the generated specs. No product code and no existing test changed.
  - Verification: the six generated walkthroughs pass at 768x1024 (8, 15, 11, 11, 13, 11 tests) with 0 px steady sideways overflow; `probe-control` 2/2 (a 1000 px element in a 768 px viewport is recorded, the login page records 0)
  - Notes: the first probe version reported false overflows (301/215/406 px) from transient values while pages were created; the probe now requires a real viewport width and the same overflow at two consecutive samples

- [x] Gate B evaluation: Patient, Scheduling, and Visit Entry Ready - PASS, awaiting independent review
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: `.alveara/gates/B/` (report and evidence), `.alveara/QUALITY_GATES.md` rows B1-B7, `.alveara/BUILD_STATE.md`; no product code changed
  - Verification: backend 957/957 (47 m 22 s), frontend 547/547, mocked Playwright 94/94, repository checks 7/7, desktop real-backend walkthroughs 7 suites (registration 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15), tablet-width (768x1024) variants of six of them 69/69 with 0 px sideways overflow and a passing probe control, Gate A route scans 3/3, auth walkthrough 12/12 on four consecutive default-timing runs
  - Notes: one judgement flagged for the reviewer (B7 blocked-time read-before-lock limitation, classed not high-severity); the four ALV-011-C01 reviewed SHAs are not ancestors (rebase before first push; mapping in BUILD_STATE)

- [x] Gate B first evaluation recorded as FAIL / CHANGES_REQUIRED (independent review finding B-REV-01)
  - Date: 2026-10-02
  - Session: CC-20261002-f7c1
  - What changed: review stored at `.alveara/gates/B/reviews/e0a8629.md`; `.alveara/QUALITY_GATES.md` Gate B rows set BLOCKED with the finding; `.alveara/BUILD_STATE.md`. No product code changed.
  - Verification: reproduced the finding on the reviewed head - `npx vitest run` with `gate-b/tablet-generated/` present fails 7 suites (Playwright specs collected by vitest) while the 547 product tests pass
  - Notes: my own frontend run for the evidence happened before the tablet specs were generated, so the coexistence was never exercised; the correction and a coexistence regression check follow

- [x] Gate B tooling correction (review finding B-REV-01): vitest no longer collects the generated gate evidence specs, with a coexistence regression check
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `src/alveara-client/vitest.config.ts` excludes `gate-*/**` (all present and future gate tooling directories) instead of only `gate-a/**`; new repository check `tests/gate-tooling-coexistence.test.mjs` generates the Gate B output and asserts `vitest list --filesOnly` returns only product tests under `src/`; `gate-b/README.md`. No product code changed.
  - Verification: against the old config the check fails and `npx vitest run` reports 7 failed suites + 547 passing tests; with the fix `npx vitest run` (generated specs present, no command-line exclusions) passes 51 files / 547 tests, `tsc -b`, lint and build pass, and `node --test tests/*.test.mjs` passes 8/8
  - Notes: the first Gate B evaluation ran vitest before generating the tablet specs, so the coexistence was never exercised; a full Gate B rerun on the corrected head follows

- [x] Gate B test stabilization: bounded wait on the server-side recovery-key generation in the auth walkthrough (ALV-N004 R06 / Gate B review condition)
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `src/alveara-client/e2e/auth-real-backend.spec.ts` - the recovery-key box assertion waits up to 30 s (was the 5 s default) and that one test has a 90 s budget; no product code changed
  - Verification: key generation measured 0.7-4.6 s idle (18 samples, three above 4.3 s) and 2.0-6.6 s under CPU load (18 samples, two above 5 s); the unchanged spec failed this exact step in 5 of 6 loaded runs; with the change 0 of 12 loaded runs failed at it (11 of 12 runs passed 12/12; run 9 failed in a different test - practice configuration - which exceeded its 30 s test budget while its Save button stayed disabled, disclosed in the Gate B rerun report)
  - Notes: the second load-only sensitivity is consistent with a second early request resetting the practice form under StrictMode in the dev server (unverified); it belongs to ALV-N003 and is not changed here

- [x] Gate B rerun evaluation - PASS on the corrected head, awaiting independent review
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `.alveara/gates/B/rerun/` (report and evidence), `.alveara/QUALITY_GATES.md` Gate B rows, `.alveara/BUILD_STATE.md`; no product code changed
  - Verification: final head 1350438: `npx vitest run` with the generated gate specs present 51 files / 547 tests, tsc/build/lint exit 0, mocked Playwright 94/94, repository checks 8/8, Gate A route scans 3/3, desktop real-backend walkthroughs (7 suites) and 768x1024 variants (69 tests, 0 px overflow, probe control 2/2), auth walkthrough 12/12 on four consecutive idle default-timing runs; backend 957/957 at b0620bf (identical backend and client source)
  - Notes: B-REV-01 reproduced then corrected with a coexistence check; recovery-key timing stabilized on measured evidence (5/6 failures before under load, 0/12 after); one load-only failure in the practice-configuration step disclosed (cause unproven, ALV-N003)

- [x] STORY-005 backend (steps 1-3 of the paced build): clinical encounters with structured history, allergies and medications; finalize; addendum; audit; API
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `Architecture/Clinical/` (Encounter, EncounterEntry, EncounterSectionMark, EncounterAddendum, EncounterEvent; EncounterService, EncounterReader, rules and views), migration `AddClinicalEncounters` (case-sensitive checks and database triggers that make a finalized encounter, addenda and history immutable and forbid deleting clinical records), `ClinicalController` (10 endpoints), new permission `ViewClinicalDocumentation` (Dentist, Hygienist, Assistant, Admin; not front desk, billing or the practice manager) appended to `Permission`/`PermissionMatrix`
  - Verification: real SQL Server tests `ClinicalEncounterSchemaTests` 34, `EncounterServiceTests` 37, `EncountersApiTests` + `ClinicalPermissionTests` 25+5 (all pass, with the existing migration and permission-matrix tests); mutation checks (audit entry, version check, completeness check, read permission, CSRF dropped) each fail the intended tests
  - Notes: backend checkpoint only - the screen, docs, `.colaberry/progress.json` and the enrichment file are still to come, so STORY-005 is NOT complete; the full backend suite has not been re-run since these changes
