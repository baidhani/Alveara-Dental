# Alveara Dental — Quality Gates

Predeclared measurable criteria for Gates A–F. No gate may be marked passed by invention or by lowering a threshold after seeing a failure. Each criterion below is a **stop-point requirement**, not a story; it is only satisfiable once the stories that feed it are implemented. At bootstrap, every criterion is `NOT YET EVALUABLE` because no production implementation exists.

Format per criterion: **Method** (test/measurement) · **Environment/Dataset** · **Threshold/Rubric** · **Current State** · **Evidence** · **Date** · **Notes/Blockers**.

---

## Gate A — Foundation Ready

**Gate A rerun 2026-10-01: PASS.** All nine criteria pass on the integrated repository (thresholds unchanged). Report: `.alveara/gates/A/rerun/GATE_A_RERUN_REPORT.md`. The first evaluation the same day was FAIL (A2 accessibility; `.alveara/gates/A/GATE_A_REPORT.md`, review `.alveara/gates/A/reviews/4b69d32.md`); the responsible stories were corrected, independently approved and closed. STORY-003 is authorized through its course-portal prompt.

*Stop point after item 10 (`ALV-N004`). Do not begin patient/front-office expansion (item 11, `STORY-003`) until this gate passes.*

| # | Criterion | Method | Environment/Dataset | Threshold/Rubric | Current State | Evidence | Date | Notes |
|---|---|---|---|---|---|---|---|---|
| A1 | Root STORY-000 Command Center still passes and coexists with the separate production app | Re-run STORY-000's 5 Done-means checks against the repo after `ALV-N001`/`ALV-N002` land | Local + course portal | All 5 criteria remain true; root `index.html` unchanged/unreplaced | PASS | `.alveara/gates/A/rerun/evidence/a1-command-center.json` (real drill-downs activated, verified count asserted); node coexistence 7/7 | 2026-10-01 | STORY-000 verified 5/5 by the course portal; the root Command Center is unchanged and coexists with the production app. |
| A2 | Production shell/design foundation is coherent and accessible | Manual + automated accessibility pass (axe or equivalent) on shell screens | Local dev build | No critical/serious axe violations on shell navigation | PASS | `.alveara/gates/A/rerun/evidence/a2-axe-results.json` (20 default + 28 state scans, 0 critical/serious); badge-uses, audit-keyboard, mocked suite 40/40 | 2026-10-01 | Depends on `ALV-N001` First evaluation FAIL (5 serious axe hits attributed to ALV-N001/ALV-001-C01/ALV-002-C01) was repaired, independently approved and closed; this rerun PASS. |
| A3 | Local server/database/migration/time/money/offline/background-job/database-topology invariants are tested | Automated test suite covering each invariant | Local Windows server + test DB | 100% of declared invariant tests pass | PASS | `.alveara/gates/A/rerun/evidence/backend-results-by-class.md` (375/375), node topology tests | 2026-10-01 | Depends on `ALV-N002` | |  |
| A4 | Privacy-safe versioned measurement-event convention is available before later metric-producing stories rely on it | Code inspection + emit/consume test of one sample event | Local dev build | Event schema versioned; no PHI fields present | PASS | MeasurementEventTests 16/16 | 2026-10-01 | Depends on `ALV-N002` | |  |
| A5 | Authentication/RBAC/MFA/session controls pass | Automated auth test suite + manual MFA walkthrough | Local dev build, seeded test users across all 7 roles | 100% pass; lockout/session-timeout behavior verified | PASS | auth/MFA/session suites 100%; real-browser MFA walkthrough; 7 roles in `a6-role-navigation.json` | 2026-10-01 | Depends on `STORY-001` + `ALV-001-C01` | |  |
| A6 | Authorization-aware navigation/session UX passes | Manual walkthrough per role + automated route-guard tests | Local dev build | Every role sees only authorized navigation; no client-only enforcement | PASS | `.alveara/gates/A/rerun/evidence/a6-role-navigation.json` (asserts per-role page access and API statuses from permissions) | 2026-10-01 | Depends on `ALV-N009` |
| A7 | Shared audit/concurrency/lifecycle primitives pass without pretending later domain semantics already exist | Automated test suite for primitives only | Local dev build | 100% pass; no domain-specific finalize/void logic present yet | PASS | audit/concurrency/lifecycle/idempotency suites; RecordLifecycleGuard has no domain caller | 2026-10-01 | Depends on `STORY-002` + `ALV-002-C01` | |  |
| A8 | Practice/staff/provider/operatory/scheduling configuration works | Manual CRUD walkthrough + automated tests | Local dev build, sample practice config | All configuration entities create/edit/list correctly | PASS | configuration suites + real-browser practice configuration walkthrough | 2026-10-01 | Depends on `ALV-N003` | |  |
| A9 | Encrypted full-state backup can be created, verified and restored for every persistent asset class that exists at this point | Full backup → verify → restore drill | Local dev build with seeded data | Restore drill succeeds; recovery-key path independently tested | PASS | backup suites 114/114 on real SQL Server + GnuPG interop; `.alveara/gates/A/rerun/evidence/a9-*` (browser 12/12 extended and 4/4 at default timing) | 2026-10-01 | Depends on `ALV-N004` Default-timing browser result is latency-dependent (first evaluation 4/4 failed at about 6 s key generation; rerun 4/4 passed). |

---

## Gate B — Patient, Scheduling, and Visit Entry Ready

*Stop point after item 17 (`ALV-011-C01`). Do not begin Clinical Core (item 18, `STORY-005`) until this gate passes.*

| # | Criterion | Method | Environment/Dataset | Threshold/Rubric | Current State | Evidence | Date | Notes |
|---|---|---|---|---|---|---|---|---|
| B1 | Registration/family/guarantor workflow is production-usable | Manual end-to-end registration walkthrough incl. household/guarantor linkage | Local dev build, representative family dataset | Full workflow completes with no data loss | NOT YET EVALUABLE | — | — | Depends on `STORY-003` + `ALV-003-C01`. Supporting evidence (not an evaluation): `ALV-003-C01` R01 (AWAITING_REVIEW) real-backend walkthrough `.alveara/handoffs/ALV-003-C01/R01-evidence/artifacts/playwright-real-backend/` - registration, duplicate comparison, household and guarantor held independently |
| B2 | Versioned forms/consents/e-signature foundation works and check-in surfaces required-form readiness | Manual walkthrough + automated signature-integrity test | Local dev build | Signed forms preserve exact version/content shown at signing | NOT YET EVALUABLE | — | — | Depends on `ALV-N010` |
| B3 | Patient registration has established one persistent patient-context workspace that later modules extend without stale cross-patient state | Manual patient-switch test across modules | Local dev build, 2+ patients | No stale data visible after switching patient context | NOT YET EVALUABLE | — | — | Cross-cutting rule per Engineering Reference. Supporting evidence (not an evaluation): `ALV-003-C01` R01 persistent patient-context workspace and patient-switch tests (unit, UI, real browser on a slow connection); `ALV-N010` R01 (AWAITING_REVIEW) is now a second module using the extension point (Forms tab; patient-switch and wrong-patient-form tests, unit and real browser); still not an evaluation |
| B4 | Scheduler enforces provider/operatory/blocked-time rules and supports reschedule/cancel/no-show | Automated conflict-detection test suite + manual walkthrough | Local dev build, overlapping-appointment fixtures | Zero double-bookings across fixture set; reschedule/cancel/no-show all functional | NOT YET EVALUABLE | — | — | Depends on `STORY-004` + `ALV-004-C01` |
| B5 | Patient flow/status board works through the visit lifecycle states implemented so far | Manual walkthrough of scheduled→checked-in→in-treatment→completed | Local dev build | All implemented states transition correctly and are visible on the board | NOT YET EVALUABLE | — | — | Depends on `STORY-011` + `ALV-011-C01` |
| B6 | Critical front-desk workflows are keyboard/responsive and preserve course contracts | Manual keyboard-only walkthrough + responsive breakpoint check | Local dev build, desktop + tablet width | No mouse-only interaction blocks task completion | NOT YET EVALUABLE | — | — | |
| B7 | No known high-severity identity/scheduling/history defect remains | Defect log review | `.alveara/EXECUTION_STATUS.json` blockingIssues across items 11–17 | Zero open high-severity items | NOT YET EVALUABLE | — | — | |

---

## Gate C — Clinical Core Ready

*Stop point after item 30 (`STORY-014`). Do not begin Treatment & Finance (item 31, `STORY-007`) until this gate passes.*

| # | Criterion | Method | Environment/Dataset | Threshold/Rubric | Current State | Evidence | Date | Notes |
|---|---|---|---|---|---|---|---|---|
| C1 | Structured history, allergies, medications, vitals and encounter notes work with safe signing/amendment | Manual walkthrough + amendment/addendum test | Local dev build | Signed notes cannot be silently overwritten; amendments preserve prior version | NOT YET EVALUABLE | — | — | Depends on `STORY-005` + `ALV-005-C01` |
| C2 | Safety alerts/medical clearance are visible before planning/completion/prescribing | Manual walkthrough with flagged patient | Local dev build, patient with known allergy/risk flag | Alert visible at each gated point | NOT YET EVALUABLE | — | — | Depends on `ALV-N011` |
| C3 | Odontogram supports primary/permanent/mixed dentition, surfaces, lifecycle and longitudinal history | Manual charting walkthrough across dentition types | Local dev build | All tooth/surface states enterable and historically comparable | NOT YET EVALUABLE | — | — | Depends on `STORY-006` + `ALV-006-C01` |
| C4 | Perio supports explicit six-site full-mouth charting and comparison | Manual full-mouth charting entry + longitudinal comparison view | Local dev build | All 6 sites/tooth enterable; comparison view shows prior vs. current | NOT YET EVALUABLE | — | — | Depends on `STORY-012` + `ALV-012-C01` |
| C5 | Structured diagnosis/provenance/context linkage works | Manual diagnosis-entry walkthrough linked to encounter/treatment plan | Local dev build | Diagnosis traceable to patient, encounter, and plan | NOT YET EVALUABLE | — | — | Depends on `STORY-013` + `ALV-013-C01` |
| C6 | Procedure/fee catalog precedes and feeds treatment planning | Manual + automated check that catalog entries populate plan options | Local dev build, seeded procedure catalog | Treatment plan cannot reference a non-catalog procedure | NOT YET EVALUABLE | — | — | Depends on `ALV-N005` (built before `STORY-014` by design) |
| C7 | Treatment plans preserve revisions, fees/estimates, acceptance/decline and phasing | Manual plan-revision walkthrough | Local dev build | Revision history preserved; no silent overwrite of prior plan version | NOT YET EVALUABLE | — | — | Depends on `STORY-015` + `ALV-015-C01` |
| C8 | No finalized clinical history can be silently overwritten | Automated attempt to overwrite a finalized/signed record | Local dev build | Attempt is rejected or routed through amendment/addendum | NOT YET EVALUABLE | — | — | |

---

## Gate D — Treatment Completion and Financial Core Ready

*Stop point after item 37 (`ALV-016-C01`). Do not begin Operations & Data (item 38, `STORY-010`) until this gate passes.*

| # | Criterion | Method | Environment/Dataset | Threshold/Rubric | Current State | Evidence | Date | Notes |
|---|---|---|---|---|---|---|---|---|
| D1 | Financial ledger/charge/payment foundation reconciles from preserved transactions | Automated reconciliation test: sum(charges) − sum(payments/adjustments) = balance | Local dev build, seeded transaction set (≥50 transactions) | Reconciliation exact to the cent; tolerance = $0.00 | NOT YET EVALUABLE | — | — | Depends on `ALV-007-C01` |
| D2 | One or many planned procedures can complete atomically with exact source linkage, odontogram effects and one billable charge per billable completed procedure | Automated multi-procedure completion test | Local dev build | Zero orphaned charges; zero missing charges; odontogram state matches completed procedures 1:1 | NOT YET EVALUABLE | — | — | Depends on `STORY-008` + `ALV-008-C01` (requires `ALV-007-C01` first) |
| D3 | Statement/receipt/refund/reversal/account UX hardening passes | Manual walkthrough of refund/reversal/statement generation | Local dev build, seeded account with mixed transaction history | All financial corrections preserve full history, no destructive edit | NOT YET EVALUABLE | — | — | Depends on `ALV-007-C02` |
| D4 | Prescription workflow consumes the shared safety context and does not claim unsupported medical dose decision support | Code/UI inspection + manual allergy-check walkthrough | Local dev build, patient with known allergy | Allergy check fires; no dose-appropriateness claim appears in UI copy | NOT YET EVALUABLE | — | — | Depends on `STORY-016` + `ALV-016-C01` |
| D5 | Financial and completed-procedure histories survive duplicate submissions, corrections and stale edits without destructive overwrite | Automated duplicate-submission + concurrent-edit test | Local dev build | Duplicate submission rejected/idempotent; concurrent stale edit detected, not silently applied | NOT YET EVALUABLE | — | — | |

---

## Gate E — Operations, Documents, and Data Ready

*Stop point after item 48 (`ALV-N006`). Resolve before optional AI / release engineering (items 49+).*

| # | Criterion | Method | Environment/Dataset | Threshold/Rubric | Current State | Evidence | Date | Notes |
|---|---|---|---|---|---|---|---|---|
| E1 | Document/image imports are safe, integrity-checked, backed up and recoverable | Automated upload-integrity test + backup/restore drill including documents | Local dev build, sample document/image set | Checksums verified pre/post restore; path-traversal/executable-upload attempts rejected | NOT YET EVALUABLE | — | — | Depends on `STORY-010` + `ALV-010-C01` |
| E2 | Follow-up/recall work queues and due internal reminders operate through durable background work and survive restart without duplication | Automated restart-recovery test on scheduled job | Local dev build | Job resumes after simulated crash/restart with zero duplicate reminders | NOT YET EVALUABLE | — | — | Depends on `STORY-009` + `ALV-009-C01` |
| E3 | Referral/lab tracking works with document links | Manual walkthrough linking referral to case + document | Local dev build | Referral/lab case correctly links to attached documents | NOT YET EVALUABLE | — | — | Depends on `ALV-009-C02` (requires `STORY-009`) |
| E4 | Duplicate detection/merge preserves linked data with recovery strategy as implemented | Automated merge test with linked appointments/charges/documents | Local dev build, seeded duplicate patient pair | Zero linked-record loss post-merge; merge is reversible per implemented recovery strategy | NOT YET EVALUABLE | — | — | Depends on `STORY-017` + `ALV-017-C01` |
| E5 | Versioned export package identifies included/excluded domains and preserves representative file integrity | Automated export + checksum verification | Local dev build, representative dataset | Export manifest lists every included/excluded domain; file checksums match source | NOT YET EVALUABLE | — | — | Depends on `STORY-018` + `ALV-018-C01` |
| E6 | Import/migration dry-runs first, respects duplicate/scheduling/ledger rules, and reports errors without silent loss | Automated dry-run + committed-import test with intentional conflicts | Local dev build, dataset with seeded conflicts | Dry-run reports every conflict before commit; committed import applies same rules as normal entry | NOT YET EVALUABLE | — | — | Depends on `ALV-018-C02` |
| E7 | Reporting reconciles to source records | Automated report-vs-source-table reconciliation | Local dev build, seeded dataset with known totals | Report totals match source-table totals exactly | NOT YET EVALUABLE | — | — | Depends on `ALV-N006` |
| E8 | Baseline/outcome metrics never claim improvement without valid comparable data | Manual review of reporting copy/claims | Local dev build | No "% improvement" claim without a stored baseline measurement | NOT YET EVALUABLE | — | — | Depends on `ALV-N006` |

---

## Gate F — Production Candidate

*Stop point after item 53 (`ALV-N008`). Required before first-production baseline is declared.*

| # | Criterion | Method | Environment/Dataset | Threshold/Rubric | Current State | Evidence | Date | Notes |
|---|---|---|---|---|---|---|---|---|
| F1 | Windows clean install, upgrade, schema/config migration and recovery/rollback drill pass | Scripted install/upgrade/rollback drill on clean Windows VM | Clean Windows Server VM | Install, upgrade, and rollback each complete without manual repair | NOT YET EVALUABLE | — | — | Depends on `ALV-N013` |
| F2 | Security/privacy engineering-readiness review has no unresolved high-severity blocker | Security scan (SAST/dependency scan) + manual review using predeclared severity rubric | Full built application | Zero unresolved high/critical findings | NOT YET EVALUABLE | — | — | Depends on `ALV-N014`; scanner/rubric to be predeclared here before evaluation |
| F3 | Full-state backup/restore drill passes on the release candidate | Full backup → verify → restore drill on release-candidate build | Release-candidate build, full seeded dataset | Restore drill succeeds for every persistent asset class in the release | NOT YET EVALUABLE | — | — | |
| F4 | End-to-end visit lifecycle passes: registration → forms/consent → scheduling → check-in/flow → history/safety → odontogram/perio → diagnosis/documentation → treatment plan → procedure completion → billing/payment → follow-up | Scripted end-to-end walkthrough | Release-candidate build | Every stage completes without defect or dead-end | NOT YET EVALUABLE | — | — | Depends on `ALV-N008` |
| F5 | Critical screens meet the agreed accessibility/performance baseline | Automated axe scan + measured response times on critical screens | Release-candidate build | Zero critical/serious axe violations; response times within predeclared thresholds (to be set at evaluation per Gate Evidence Rules) | NOT YET EVALUABLE | — | — | Thresholds must be predeclared before measurement, not chosen after seeing results |
| F6 | Core product works with public internet unavailable and with AI disabled | Manual walkthrough with network disconnected from public internet (LAN only) and AI feature flag off | Release-candidate build, LAN-only environment | All core (non-AI) workflows complete | NOT YET EVALUABLE | — | — | |
| F7 | Optional AI, if included, remains reviewable, source-aware and non-authoritative | Manual review of AI output provenance/citations | Release-candidate build with AI enabled | Every AI output traceable to source; no AI output is the sole record of a clinical/financial fact | NOT YET EVALUABLE | — | — | N/A if `ALV-N007`/`ALV-N012` deliberately deferred |
| F8 | No dead-end placeholder action or misleading sample/live/health/clinical/financial state remains in critical workflows | Manual UI audit of every critical screen | Release-candidate build | Zero placeholder buttons/fake data presented as real in critical paths | NOT YET EVALUABLE | — | — | |
| F9 | Operational runbook and release evidence are complete | Manual review of runbook doc + release evidence package against a predeclared completeness checklist | Release-candidate build, final runbook draft | Every checklist item present and reviewed; no "TBD" placeholder in a required runbook section | NOT YET EVALUABLE | — | — | Checklist itself must be predeclared before evaluation, not authored after the fact |

---

## Change Log

| Date | Change | Reason |
|---|---|---|
| Bootstrap (ALV-CONTROL R01) | Gates A–F created with predeclared criteria, all `NOT YET EVALUABLE` | Initial control bootstrap; no production implementation exists yet |
