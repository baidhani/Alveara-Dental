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

- [x] STORY-005 screen (step 4 of the paced build): the Clinical tab - encounter list, documenting, finalize review, addenda
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `src/pages/clinical/` (PatientClinicalPanel, EncounterView, SectionPanel, EntryForm, FinalizeReview, AddendumPanel, Clinical.css), `services/clinicalApi.ts`, a `clinical` workspace tab and routes shown only with `ViewClinicalDocumentation`; each change is saved as it is made and typed values survive a failed save, a dropped connection and a conflict reload (refresh in place); start and addendum carry one idempotency key across retries
  - Verification: 599/599 frontend (`vitest`; 32 new component tests incl. every failure path and jest-axe, 20 contrast and link-guard tests), `tsc -b`/build/lint clean, mocked Playwright 94/94, real-backend walkthrough `e2e/clinical-real-backend.spec.ts` 9/9 with axe clean in both themes, regressions workspace 14/14, forms 10/10, Gate A route and role scans 3/3; mutation checks (reload wipes the screen, new key per retry for start and addendum, finalize enabled while unresolved) fail exactly the four intended tests
  - Notes: the real browser caught an unreadable dark-mode link (1.77:1) that jsdom cannot see; fixed by reusing the workspace link class, with a guard test. STORY-005 is NOT complete yet: docs, `.colaberry/progress.json`, the enrichment file and the final full backend run remain

- [x] STORY-005: Develop clinical documentation module (REQ-007) - complete: encounters with structured medical and dental history, allergies and medications; finalize; addendum; audit; screen
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: this entry closes the three checkpoints above (backend `d87ceae`, screen `8184edc`): `docs/CLINICAL_DOCUMENTATION.md`, `docs/stories/STORY-005.md` (all three acceptance boxes ticked), `.colaberry/progress.json` (criteria passed, files touched, tests added, notes) and `.colaberry/enrichment/STORY-005.json` (truth base revision 1); one existing test assertion made robust (`VisitPermissionTests`, see Notes)
  - Verification: full backend suite 1,071 of 1,072 on the final code (54 m 22 s; real SQL Server) - the one failure was `VisitPermissionTests.The_new_permissions_were_appended...`, which hard-coded that the two visit permissions are the LAST enum members; it now asserts they sit directly after `VoidForms` in order, as does the new `ClinicalPermissionTests` check for `ViewClinicalDocumentation`, and the affected permission classes pass 47/47 (no product code changed after the full run); frontend 599/599, `tsc -b`/build/lint clean, mocked Playwright 94/94, real-backend walkthrough `e2e/clinical-real-backend.spec.ts` 9/9 with axe clean in both themes, regressions workspace 14/14, forms 10/10, Gate A route and role scans 3/3
  - Notes: `ViewClinicalDocumentation` is a new permission kept apart from `ViewPatientRecords` (front desk and billing hold it) so they cannot read a medical history; per-encounter documentation only - running history, vitals, SOAP notes and templates are ALV-005-C01; not pushed

- [x] ALV-005-C01: Complete clinical documentation - longitudinal record, notes, templates, vitals, signing and amendments (companion to STORY-005; awaiting review)
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `Architecture/Clinical/` (longitudinal record with item status lifecycle, version history, none-known/unknown/not-reviewed statements and reviewer attribution; SOAP/progress/treatment notes; clinical-owned note templates; vitals with void; signing; amendment sections), migration `AddClinicalRecordAndNotes` (case-sensitive checks, 8 triggers 51040-51048), new permission `ManageClinicalTemplates`, `ClinicalRecordController` and `ClinicalNotesController`; frontend record panel, notes with autosave, vitals, templates page, signing review and amendment timeline; docs and real-browser walkthrough
  - Verification: full backend 1,232 of 1,232 (1 h 4 m, real SQL Server; 160 new, STORY-005's own tests unchanged and passing); frontend 684 of 684 (85 new), tsc/build clean, lint no new errors; mocked Playwright 94 of 94; new real-backend walkthrough 9 of 9 twice (18 axe scans, 0 serious/critical); STORY-005 walkthrough 9 of 9, workspace 14 of 14, forms 10 of 10 on the new build
  - Notes: a concurrency defect in my own code (simultaneous adds under a "none known" statement) was found by review and fixed with a test that fails without the fix; two test-side races fixed; status is AWAITING_REVIEW, not COMPLETE

- [x] ALV-N011: Patient safety alerts, medical risk context and clearance tracking (new production; awaiting review)
  - Date: 2026-10-03
  - Session: CC-20261002-f7c1
  - What changed: `Architecture/Safety/` (alerts with source, severity, status and append-only versions; per-person per-revision acknowledgements that never resolve; clearances requested/received/resolved/cancelled with an attachable-later document reference; `SafetyContextService` / `ISafetyContextProvider` read-only projection of record allergies and medications, alerts, clearances and "not established" gaps; minimal board indicator), migration `AddPatientSafety` (case-sensitive checks, 5 triggers 51050-51054), `SafetyController`, permission `ViewSafetyIndicator`, board card `safety` field; frontend safety strip (header and encounter), Safety tab, board indicator; `docs/PATIENT_SAFETY.md`, real-browser walkthrough
  - Verification: full backend 1,369 of 1,369 (1 h 13 m, real SQL Server; 137 new); frontend 733 of 733 (49 new), tsc/build clean; mocked Playwright 94 of 94; new real-backend walkthrough 11 of 11 (axe both themes, 0 serious/critical); regressions on the new build: STORY-005 9/9, ALV-005-C01 9/9, visit board 15/15, workspace 14/14, forms 10/10, patients 7/7, flow 10/10, calendar 12/12, schedule 10/10, auth 12/12; mutation checks on the board permission and the clearance rule
  - Notes: audit text is deliberately generic (not even the alert category) because the audit log's readers cannot read clinical data; the board indicator is limited to the clinical team; status is AWAITING_REVIEW, not COMPLETE


- [ ] STORY-006: Interactive odontogram (in progress; steps 1-3 of 6 done)
  - Date: 2026-10-04
  - Session: CC-20261002-f7c1
  - What changed: step 1, domain and database. `Architecture/Odontogram/` (`ToothKeys`: the 52 FDI/ISO 3950 strings as the single stored tooth identity; `ToothNumbering`: display and parse for Universal (default), FDI and Palmer, display only; `ToothFinding` + append-only `ToothFindingVersion`; the six fixed conditions, four lifecycle states, surface rules by anterior/posterior tooth), `OdontogramModel`, migration `AddOdontogram` (case-sensitive checks for key, condition, state, surface-on-tooth and withdrawn stamp; one active finding per tooth/surface/condition; triggers 51055-51056)
  - Verification: ToothNumberingTests 74 and OdontogramSchemaTests 48 pass against real SQL Server (`dotnet test` filter run, 122 of 122)
  - Notes: item not complete until all steps, tsc, full suites and the portal check. Numbering finding for the reviewer: no story in the master execution plan covers Palmer, FDI display or a practice-level numbering preference (item 22 only specifies a Universal default with an abstraction that can later render FDI/ISO); the mapper supports all three now and the preference setting is deferred as a recorded limit. Other limits: supernumerary teeth and area findings (quadrant/arch/full mouth) are not covered by the key; the condition list is fixed at six until item 22 moves it to a table
  - Step 2 (2026-10-04): `OdontogramService` (record with chosen state, forward-only moves Diagnosed to Planned to Completed or Diagnosed to Completed, withdraw with reason, chart, history), `OdontogramRules`, `OdontogramViews`/`OdontogramException`, DI registration; change + history version + PHI-free audit entry in one save; quiet repeats; row-version conflicts; simultaneous-record retry. Verification: OdontogramServiceTests 65 pass against real SQL Server (139 of 139 with the step 1 tests); mutation checks (all moves legal; withdrawal without history version) failed 8 of 8 targeted tests, then restored
  - Step 3 (2026-10-04): `OdontogramController` (GET chart and finding history need ViewClinicalDocumentation; POST record, state change and withdraw need ManageClinicalNotes + CSRF + row version; stable `error` codes and `fieldErrors`; shared 409 `concurrency_conflict`). Verification: OdontogramApiTests 22 pass against a real API and SQL Server (401 anonymous, role matrix for read and write, CSRF, row-version codes, field errors, malformed body, patient isolation); mutation check (write permission weakened to read) failed the Assistant role case, then restored

- [x] Command Center: Architecture & Execution Map tab driven by live data (side task, not a story)
  - Date: 2026-10-04
  - Session: CC-20261002-f7c1
  - What changed: new `assets/map.js` and an "Architecture Map" tab. The map reads `.alveara/EXECUTION_STATUS.json` (status, gate, type, phase, dependencies, commits, review), `.colaberry/plan.json` (portal story titles), `.colaberry/progress.json` (criteria progress, shows "In progress") and the new `.alveara/story_catalog.json` (subsystem per story, titles of stories not in the plan) at runtime, with no story data in the page. Replaces the static, self-contained `docs/architecture-map/index.html` (kept as the reference copy it was built from, not served by the Command Center). Catalog added because no live file carries subsystem or the engineering story titles
  - Verification: `e2e/architecture-map.spec.ts` 12 tests at desktop and tablet (24 of 24) pass in a real browser against the repo root served statically: card per ledger story, titles/gate/state from the data, filters and reset with focus kept, dialog with Escape and focus return, catalog-missing fallback, data-follows-ledger and in-progress overrides, axe in light and dark with the dialog open (0 serious/critical); `command-center.spec.ts` unchanged and passing (56 of 56 with the map spec)
  - Notes: the catalog is hand-maintained: add one entry when a story is added to the ledger; the spec fails if a ledger story has no entry, an entry has no story, or a story has no title. Portal titles are read from the plan, so they differ from the shortened titles the static page used

- [x] Command Center: Architecture Map legend keys restored (fix to the previous entry)
  - Date: 2026-10-04
  - Session: CC-20261002-f7c1
  - What changed: the legend of clickable keys (one per delivery state and story type present in the data) that the original static page had and the first port left out. Each key applies its filter, shows pressed state, clears on a second press, and keeps keyboard focus; built from the live data so it never lists a state that does not exist
  - Verification: `e2e/architecture-map.spec.ts` 14 tests at desktop and tablet pass with `command-center.spec.ts` (62 of 62), including the new legend tests (exact key set from the data, filter, clear, keyboard) and axe in light and dark (0 serious/critical)
  - Notes: omission in the first port was mine and was not flagged at the time

- [x] Command Center: Architecture Map story-type icons and colours restored (fix to the first port)
  - Date: 2026-10-04
  - Session: CC-20261002-f7c1
  - What changed: each story type has its own icon and colour again (portal: flag, blue; engineering: gear, purple; companion: chain, teal) on cards, legend and the detail dialog, through new `--map-*` tokens with light and dark values; the type is still written as a word beside the icon
  - Verification: `e2e/architecture-map.spec.ts` 16 tests at desktop and tablet pass with `command-center.spec.ts` (66 of 66): icon, word and three distinct colours per theme, plus axe in light and dark (0 serious/critical)
