# ALV-N026 chat handoff (repo copy)

> Copy of the memory note `project_handoff_package_2026_11_21.md` (2026-11-21). The memory copy is the working one and may be newer; this copy exists so the handoff survives without the memory folder. Memory-note names in `[[...]]` refer to files in the agent memory folder.

# Handoff package for a new chat (written 2026-11-21, session CC-20261115-m7x2)

This note is the entry point. **Two copies exist:** this memory note (the working copy, it wins) and `docs/design/ALV-N026_CHAT_HANDOFF.md` in the repo (a snapshot). When you change this note in a way that matters, refresh the repo copy in the next documents commit you make. It tells you what to read, what the state is, how this owner works, how reviews work (old and new), and exactly what to do next. It is point-in-time: **verify before relying on it** (section 9). Where it conflicts with `CLAUDE.md`, this note and the rule notes it names win.

## 0. Read in this order (all in this memory folder)
1. **This note.**
2. `project_session_handoff_gate_e.md` (START HERE): the non-negotiables. Its Gate E state is history.
3. `reference_agent_operating_manual.md`: the loop, the reply style, evidence discipline, tool quirks, and CLAUDE.md precedence (section 0).
4. `feedback_flag_cost_before_long_runs.md`: the test-run rule (TOP PRIORITY) - plus `feedback_standing_working_rules.md`, `feedback_review_before_tests.md`, `feedback_no_coauthored_by.md`, `feedback_long_run_last.md`, `feedback_detailed_test_steps.md`, `feedback_read_full_briefing_scope.md`, `feedback_include_setlocation_command.md`.
5. `project_status_roadmap.md` (programme position; its N023-N025 lines are history now) and `project_stabilization_phase.md` (why the programme exists; the owner's binding release sequence).
6. `project_n026_handoff.md`: the detailed ALV-N026 history (gates G1-G4, every review, S1-S6 results, lessons).
7. When you package: `reference_alv_handoff_procedure.md` (mechanics), `project_n023_handoff.md` section 5c (the CURRENT review route), `reference_reviewer_lessons.md`, `reference_agent_tools.md`.
8. As needed: `reference_repo_environment_map.md` (the real layout; CLAUDE.md's folder map is wrong), `reference_measured_durations.md`, `project_unassigned_findings.md`.

**Stale notes (do not trust their state lines; this package supersedes them):** START HERE's top banner (says ALV-N023 is in progress) and `project_status_roadmap.md` sections 1-2 (say N025 in progress, N026 planned). Both now carry a banner pointing here; their RULES still apply.

Then in the repo: `CLAUDE.md` (root), the tail of `PROGRESS.md`, `docs/design/ALV-N026_PRE_IMPLEMENTATION_BRIEF.md` (rev 3; the approved scope; sections 3, 6 and 7), `docs/design/ALV-N026_ISSUE_ROW_OWNERSHIP_DECISION.md`, `docs/design/ALV-N026_R03_FINAL_DESIGN_REVIEW.md`, `.alveara/PROGRAM_REGRESSION_LEDGER.md`, `.alveara/EXECUTION_STATUS.json`.

## 1. The project in five lines
- **Alveara Dental / DentalKeep:** a dental EHR and practice-management system for general dentistry. ASP.NET Core (.NET 10) API + EF Core + SQL Server Express in `src/Alveara.Api`, tests in `src/Alveara.Api.Tests` (xUnit, real SQL), React + Vite + TypeScript client in `src/alveara-client` (Vitest, Playwright).
- **Governance:** the execution plan `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` (items by number), `.alveara/HANDOFF_SCHEMA.md` v3, `.alveara/EXECUTION_STATUS.json` (`records[]`, key `storyId`), `.alveara/BUILD_STATE.md`, `.alveara/QUALITY_GATES.md`, `PROGRESS.md` (append-only).
- **People:** the owner (Firas) decides everything outward-facing and every Go. An **independent reviewer** (another AI) reviews ZIP packages and documents; the owner uploads them and pastes back the review file path.
- **Phase:** the post-Gate-E **stabilization programme**, items 56-65 (ALV-N019..ALV-N028), from plan R05 (`docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md`, packages PKG-01..PKG-16). Gate E passed 2026-10-29. COMPLETE: N019, N020, N020-C01, N020-C02, N021, N022, N023, N024, N025.
- **Current story:** ALV-N026 (item 63, PKG-12): Documents, Prescriptions, Printing, and Backup/Restore Integrity. EXECUTION_STATUS still says PLANNED (it changes only at packaging).

## 2. State at hand-over (verified 2026-11-21)
- HEAD = the commit "ALV-N026: refresh the chat handoff package (round 3) ...", whose parent is `92c075a` (a commit cannot name its own SHA; S6 is `68c926a`, the N025 ledger correction `9ece51a`), tree clean, branch main. origin/main = `318bf3c`; **40 commits unpushed** at hand-over (the count grows with every commit; re-check with `git rev-list --count origin/main..HEAD`). Never push without the owner's say-so.
- **ALV-N026 slices (brief section 7):**

| Slice | What | Commit | Focused result |
|---|---|---|---|
| S1 | PD-24 document classification, ManageDocuments (Admin+OM), Dentist ImportDocuments | 56fe7c9 | 243/243 + 128/128, client 93/93 |
| S2 | Document UI fixes, availability route | 95a321d | 183/183, client 98/98 |
| S3 | Backup alerts, System Status admin sections | 16cdc17 | 149/151 (2 = U-020), client 98/98 |
| S4 | PDF engine (Typst 0.15.1), readiness, manifest, provisioning | c41520c | engine 13/13, script 3/3 |
| S5 | Statements/receipts as PDFs, template v4 + MRN | afafd76 | 76 (rerun 11/11), client 72/73 (U-018) |
| S6 | Prescriptions p2: issue rows, record copy, watermark, p1 retired, F-165, Q-5am | 68c926a | 45/45 (78 s), client 34/34 |
| **S7** | **Treatment-plan copy on the server (D3)** | not started | - |
| **S8** | **Clinical record copy (D4); client print flows switched to PDFs** | not started | - |

- **N025 ledger gap: FIXED.** Owner decision 2026-11-21: a separate documents-only correction before S7. Committed `9ece51a` ("ALV-N025: regression-ledger correction ..."): the ALV-N025 section and four obligation rows, derived only from N025's accepted R01 evidence and review. **Mention it in the ALV-N026 handoff** (traceability). The ledger now has an N025 section; copy N024's or N025's shape.
- Gates G1-G4 all closed (see `project_n026_handoff.md`). Reviews for this story's design are in `docs/design/ALV-N026_*_REVIEW*.md` (not `.alveara/reviews/`); the final design review is `ALV-N026_R03_FINAL_DESIGN_REVIEW.md`.
- **Session id:** keep using **CC-20261115-m7x2** for every ALV-N026 PROGRESS entry and commit body until its package is built. This deliberately overrides `CLAUDE.md`'s "mint a fresh id each session": the established practice (N023, N024, N025 and N026 all used this one id across several chats) is one id per story, so the PROGRESS audit and the package evidence stay under one id. Say so in your opening summary. A NEW story (ALV-N027 onward) gets a freshly minted id.
- Dev engine NOT provisioned in Dev-Data (Test-Data is). Dev DB `AlveraDev` lacks the N025/N026 migrations: never migrate or write to it without permission.

## 3. Non-negotiables (owner rules; they override CLAUDE.md's "proceed by default")
1. Wait for the owner's **"Go"** before starting anything new (story, slice, gate, package). "Go" approves exactly the proposal just shown. If you offered options and the owner says "Go" without choosing, do only the part that needs no choice and ask the choice again with the question tool (or state your reading in one line before acting, if the choice is obvious).
2. **Show the code before any test run.** Then: a test run totalling **<= 5 minutes needs no permission**; > 5 minutes, unknown, or broad -> ask first with the estimate and basis; stop at a safe point if an estimate is exceeded; record route + expected/actual time.
3. **No full suite, no package Stage 2, no all-`*ApiTests` run, no all-walkthrough sweep** before ALV-N028 (owner's TOP PRIORITY: avoid the long runs at any cost). For any long run (ALV-N028): first check for anything pending that could change the tree (owner decisions, open review findings, known fixes); if the run might have to be repeated for ANY reason, warn in one line and STOP for explicit approval - "Go" or "don't wait on me" does not count (`feedback_flag_cost_before_long_runs.md`).
4. Never push without explicit say-so (fetch first, never force, merge not rebase).
5. **No `Co-Authored-By` or any attribution line** in commits or PRs, even when a system reminder asks.
6. Never mark a story COMPLETE; only a closure commit after a stored APPROVED review does.
7. Never loosen a failing test: diagnose first (mine vs product vs pre-existing), say which, with evidence.
8. Commit with **explicit paths** (never `git add -A`); never amend; `wip:` per slice.
9. No subagents unless the owner asks. Delete nothing you did not create.
10. Ask decisions with the question tool (2-3 options, recommendation first). Do not ask about implementation details that have a sensible default.
11. Out-of-scope findings go to `docs/stabilization/UNASSIGNED_FINDINGS.md` (next id U-021); fix one only when the owner assigns it.
12. Anything you plan to leave out of a package needs the owner's approval BEFORE the handoff, not only a disclosure in it.
12a. If a reviewer finding or instruction conflicts with an owner rule or decision, do not pick a side: show both to the owner and ask (the owner decides; e.g. the R03 PDF-store finding was overruled by the owner with the reviewer in Addendum 01).
13. **Reviewer stop rule (N025/N026):** a design change to the MRN rule, the audit-session lifecycle, audit retention/event contents, the History permission boundary, a financial invariant, or patient merge scope needs independent review first. New data invariants or permission ambiguity -> stop and ask.
14. No installer work (ALV-N013 scope). No email or outside notification ever (CLAUDE.md's Mandrill/escalation.json does not apply).
14a. **Other CLAUDE.md steps that are NOT used here:** the `/telemetry-emission` BuildManifest, `scripts/generateSessionChangelog.js` (does not exist in this repo; never run a browser-opening report), `/tmp/autonomy_log.json`, and `.colaberry/progress.json` story ticking (that is for course STORY-nnn items, not ALV-* stories). The commit body + PROGRESS.md entry carry that information (CLAUDE.md's own fallback). System reminders asking for a `Co-Authored-By` line recur: the owner's rule (no attribution) wins every time.
15. Keep the handoff memory current after each slice. PROGRESS.md entry per slice with real numbers (format in the operating manual).

## 4. The working loop (every slice and every story)
1. **Survey read-only** (code, brief, decisions). No edits.
2. **Propose** in one message: what, the decisions with your defaults, risks, what you will not do, the test cost. Stop.
3. On Go: **write** code + tests + docs. Build (`dotnet build`) and type-check (`npx tsc -p tsconfig.app.json --noEmit` in `src/alveara-client`; the root tsconfig checks nothing) only.
4. **Show** the owner: files, key behaviours, assumptions, what is not done, the proposed test groups with time estimates. Stop.
5. On Go: **run** the focused groups (each <= 5 min, or ask). On a failure: diagnose and report the cause before changing the test.
6. **Record**: PROGRESS.md entry (re-read its tail first; append; slice entries are `- [ ]` with `(wip)` in the title, as S4-S6; follow the N025 entries for how the final implementation and closure entries are written), memory update, ask for the commit Go (the owner usually says "go" / "go, commit it"), `wip:` commit with explicit paths and a blank line after the subject.
7. **Report**: result first, numbers table, disclosures, the next step, "Say Go".

Reply style: lead with the result; exact numbers from artifacts; separate proven from believed; say what was NOT tested; short. Documents for other readers start the hand-over message with "Written for: ...".

## 5. Next steps, in order

### 5a. S7 - treatment-plan copy on the server (brief section 3.3, D3)
- `TreatmentPlanCopyIssues`: `Id`, `PlanId` only (**no PatientId**, ownership decision), `RevisionNumber`, `ItemStateCutoffJson` (item id -> highest `TreatmentPlanItemState.Sequence` at issue), `IssuedAtUtc`, `IssuedByUserId`, `TemplateVersion` v2 (with the MRN), `EnvelopeJson` (practice, patient name, MRN), `RenderBundleId`, optional `ContentHash`, `IdempotencyKey`, `CorrelationId`. Append-only trigger (next guard numbers **51307 / 51308**; S6 used 51305/51306; check `grep -rn "5130[7-9]"` first). Not in `PatientOwnedData` or the merge registry.
- Rebuild sources table: brief section 3.3 (revision content, item states up to the cut-off, DiagnosisVersion labels, envelope). Read it fully before proposing.
- Pattern to copy: S6 (`src/Alveara.Api/Architecture/Prescribing/PrescriptionIssues.cs`, `PrescriptionPdf.cs`, template `src/Alveara.Api/Pdf/Templates/prescription/p2/main.typ`, its pin in `templates.manifest.json`, the csproj `EmbeddedResource` line, migration `20261121235742_*` with trigger + guarded Down, routes in `PrescriptionWorkflowController`, tests `PrescriptionIssueOwnershipTests` and the S6 block in `PrescriptionWorkflowApiTests`). Add the new table to `ExportDomainCatalog` (the treatment_plans group).
- Current client copy: `src/alveara-client/src/pages/treatmentplan/TreatmentPlanPatientCopy.tsx` with its own test `TreatmentPlanPatientCopy.test.tsx` (mounted by `TreatmentPlanCard.tsx`; print styles in `TreatmentPlan.css`; `TreatmentPlanN023.test.tsx` also touches it). Today it is built in the browser from the live plan and prints with `recordPrint(...)` then `window.print()`; the practice header comes from `GET api/treatment-plans/practice-header`. **That browser copy is "v1"** (as p1 was for prescriptions): the server template is v2, the first server version; there is no server v1 template to find; switch it to `openPdf(async () => url)` from `services/pdfApi.ts`.
- **Required proof in the slice (brief 3.3):** the diagnosis label as of an instant equals the `DiagnosisVersion` with the greatest `OccurredAtUtc` at or before it, and the plan's reference code cannot change. **Careful: the brief's word "immutable" is imprecise.** The database rule is SET ONCE (`TR_TreatmentPlans_Revision`, THROW 51099 in migration `AddTreatmentPlanCompletion`): a plan can go from no code to a code at any time, even after a copy was issued, but never change an existing code. So store the code at issue (e.g. `ReferenceCodeAtIssue`) and compare it on rebuild; a null stays null on the reprint; if either fails, the copy refuses with `integrity_mismatch` rather than guessing (the brief gives no status code: propose one, 409 like the other refusals, and say it is your choice). Test both, including the refusal.
- Permission: plans are read with `ViewClinicalDocumentation` (see `TreatmentPlansController`); use it for issuing and the PDF, as S6 let readers issue. Practice-local dates: `IPracticeClock.ToPracticeLocal` (S5 `FinancialDocumentPdf`, S6 `PrescriptionPdf`); a reprint keeps the issue's date. No measured duration exists for the treatment-plan test classes: estimate from S6 (45 tests, 78 s) and ask if a total may pass 5 minutes.
- Required tests: brief section 6 row for the plan copy; ownership: direct update/delete refused, and after an approved merge of the plan's patient the issue is reached only through the survivor's plan; a later edit/revision/item-state change does not change a reprint; PdfPig rendered text (never bytes).

### 5b. S8 - clinical record copy (D4) and the remaining client print flows
- `ClinicalRecordCopyIssue` is **directly patient-owned** (it has no parent) - so it DOES need `PatientId`, `PatientOwnedData`/merge-registry entries and merge-scope triggers. That is merge-scope work: follow the stop rule; propose the design and ask whether a reviewer check is needed before code. Content source: `PatientReadableRecordService` (brief assumption A3). Always "generated as of" now; a second request is a new issue.
- Switch every remaining browser print to the server PDF. Remove now-dead code: `src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx` (also referenced by `components/dateOnly.test.tsx`) and `PrescriptionLifecycleService.PrintAsync` + `PrescriptionPrintContent` + their tests in `PrescriptionWorkflowServiceTests` (lines ~41, 515-556). Show the owner before deleting.
- **Design gap to raise when proposing S8:** brief 3.4 lists no `EnvelopeJson` for `ClinicalRecordCopyIssues` (the other issue tables have one). Propose adding one (practice + identity as printed) or explain why it is not needed; ask the owner. Entity name singular `ClinicalRecordCopyIssue`, table plural.
- Optional, ask the owner: the staff-profile UI for the four prescriber fields (route `PUT /api/config/staff/{id}/prescriber` exists; screen in `PeopleTabs.tsx` not built).

### 5c. ALV-N026 package (after S8) - the CURRENT focused-proof route
1. Map every brief section 6 test row and every finding id to evidence first (`feedback_read_full_briefing_scope.md`).
2. Final implementation commit `ALV-N026: <what>` with `Story: ALV-N026` trailer; then a focused Stage 1 sweep **on that exact tree**: the focused groups per slice (propose groups with times; ask if the total may exceed 5 minutes), client gates (tsc -p tsconfig.app.json, lint, `vite build`, focused vitest - launch vitest from PowerShell), `node --test tests/*.test.mjs` from the repo root, any new real-backend spec alone, negative controls (break one rule, run only the catching class, restore byte-exact, `negative-controls.txt`), leak counts before/after.
3. Files: `.alveara/handoffs/ALV-N026/R01.md` + `R01-evidence/` (TEST_RESULTS, ACCEPTANCE_EVIDENCE, CHANGED_FILES, DEMO_EVIDENCE, PARENT_REGRESSION, NEXT_STORY_IMPACT, artifacts; copy ALV-N025's `R01-evidence/` layout; a SECRET_SCAN file is needed only if the scan has hits, as N023 did). EXECUTION_STATUS ALV-N026 record only -> AWAITING_REVIEW, attempt R01, `fullRegression: "DEFERRED_TO_ALV-N028"`, `releaseCertified: false` (edit with Python json; keep CRLF/no trailing newline; only that record's lines change). BUILD_STATE line. Append an `## ALV-N026 (item 63)` section + obligation rows to `.alveara/PROGRAM_REGRESSION_LEDGER.md`. **Copy the ALV-N024 section** (the last one in the file, `## ALV-N024 (item 61)`, plus its rows in the obligations table at the top). (The ALV-N025 section was missing and was added by the correction `9ece51a`; see section 2.)
4. Commits, following the most recent story (ALV-N025: 855a1bf, 6872c0a, 318bf3c): `ALV-N026: record engineering handoff (R01, Stage 1 focused proof, AWAITING_REVIEW)`, then `ALV-N026: record execution status and evidence commit (R01)`. (N023/N024 used `ALV-N0xx R01: record Stage 1 engineering handoff ...`; either convention has been accepted; use N025's.)
5. ZIP `.alveara/dist/Alveara_Handoff_ALV-N026_<impl7>_R01.zip` with a copy of the latest saved builder and validator - they live in the MEMORY folder's `agent-tools/` (not in the repo): `build_zip_n024_r02.py` and `validate_zip_n024_r02.py` (no N025 builder was saved; adapt ids, attempt and paths; the generic `validate_zip.py` is also there): inventory == members, SHAs match, secret scan (show any hit to the owner). **No commit after the package.** Give the owner the path and SHA-256; stop at AWAITING_REVIEW.
6. The review arrives as a path; store it unchanged as `.alveara/reviews/ALV-N026/R01.md`. APPROVED (final focused-proof) -> one closure-only commit `ALV-N026: record review approval (R01 APPROVED; closure only)` (as N025's 318bf3c: status COMPLETE, review block, `stage2: "NOT_REQUIRED_BY_REVIEW_R01"`, completedAt UTC, ledger Approved line; see N025's closure for the shape). Stage 1 approved with a small frozen focused Stage 2 -> run exactly that contract once (ask if > 5 min), then the closure. CHANGES_REQUIRED -> record it, reproduce each finding, fix only those, rerun focused proof, attempt R02 with new files.
- **Disclosures to carry** (already approved or recorded): PdfPig 0.1.16 test-only (Apache-2.0; evidence `docs/design/evidence/ALV-N026-S5/PDFPIG_TEST_DEPENDENCY.md`); S5 bundle id stored in the v4 EnvelopeJson, not a column; the issue-row ownership deviation from the brief (no PatientId on prescription/plan-copy issues); S6: `/print` retired with 410, readers may issue, prescriber UI deferred (if not done in S8), Q-5ao covered by existing service tests; U-018 (flaky BillingStatementsPanel), U-019 (readable export lists clinical document names), U-020 (ExportDomainCatalogTests missing tables from N020-C01/N025); untested: ViewClinicalDocumentation override path, the route-level PDF 503 (engine-level only), migration Down paths not exercised. Also mention the ALV-N025 regression-ledger correction `9ece51a` (owner decision 2026-11-21). Also disclose any rule slip that happens during S7/S8 (for example a test run before the code was shown), as N025 did.

### 5d. After ALV-N026 (each only on the owner's Go)
- **ALV-N027 (item 64)**: Messaging, Follow-Ups, Reports, Navigation, Configuration, Onboarding. Start NOT with code: read-only survey of its R05 findings -> pre-implementation brief (+ source appendix, finding survey, owner decision record) -> reviewer approves the brief -> Go -> slices. Copy `docs/design/ALV-N025_*` / `ALV-N026_*` for shape.
- **ALV-N029 (65a)** DEFERRED: the owner decides whether it runs.
- **ALV-N028 (65)**: the ONE full chain from an empty environment + the release-candidate full regression over every ledger obligation, under the OLD two-stage process (section 6a): Stage 1 approval, then one frozen, owner-approved long Stage 2.
- Then the **owner's binding release sequence**: added features (STAB-22; N030-N032 proposed in `docs/design/ALV-N021_PLAN_CHANGE_PROPOSAL.md`, not in the plan - never add them to the plan or EXECUTION_STATUS yourself) -> tested and added to the chain -> owner's manual E2E -> owner's explicit clear-to-go -> release. Release engineering (N008, N013 which must enable ALLOW_SNAPSHOT_ISOLATION, N014) only when the owner says so. AI items N007/N012 DEFERRED.

## 6. The two review processes

### 6a. OLD: two-stage (schema v3) - used for stories before the programme and for gates; returns only for ALV-N028/gates
- Stage 1: final implementation commit, a Stage 1 sweep (focused + every `*ApiTests` ~19 min + client gates + mocked Playwright + walkthroughs + leak counts), a frozen Stage 2 contract (`stage2/run_stage2.sh`, `evaluate_stage2.py`, expected test ids), handoff + evidence + three commits, ZIP. Review -> `APPROVED_PENDING_REGRESSION` -> closure `record stage 1 review`.
- Stage 2: the full serial backend suite (~85 min, machine idle) run EXACTLY ONCE on the closure commit, only after asking the owner; Monitor + 10-minute heartbeat; nothing changes in the repo during the run. Regression evidence + regression ZIP (includes the raw trx; never delete it) -> final review -> closure `record review approval`.
- Gates add a gate report and a FRESH full regression (Gate C was rejected for reusing a story's run).
- Recipe: `reference_alv_handoff_procedure.md`; gate procedure: `reference_gate_e_playbook.md`; tools: `make_stage2.py`, `build_regression_zip.py`, `validate_regression_zip.py`, `verify_stage2_in_zip.py`, `build_gate_zip.py`. **Do NOT use any of these before ALV-N028.**

### 6b. CURRENT: focused proof (items 56-65, plan line 148, 2026-11-11)
- Each package closes on independently approved focused proof, `fullRegression: "DEFERRED_TO_ALV-N028"`, `releaseCertified: false`, plus its ledger section and obligations for N028. No full suite, no package Stage 2 unless the reviewer names a small focused contract.
- Each story starts with a briefing the reviewer approves BEFORE code; addenda for any scope change found during building (N024 and N025 each had approved addenda).
- Steps: section 5c above; details `project_n023_handoff.md` section 5c.
- What the reviewer rejects (`reference_reviewer_lessons.md`): wrong or mismatched commit identities, MANIFEST numbers that differ from TEST_RESULTS, a commit after the package, undisclosed deviations, claims not backed by a test, partial proof not approved beforehand.

## 7. Technical facts and lessons you will need
- **Repo path has `[ ]`:** in PowerShell use `Set-Location -LiteralPath '<repo>'` then a relative `& .\scripts\X.ps1` (absolute `-File`/`&` fails). Every PowerShell block given to the owner starts with the Set-Location line. Bash heredocs break on quotes/backslashes: write Python scripts to the scratchpad with the Write tool and run them.
- **Walkthroughs (real backend):** from PowerShell at the repo root: `& .\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.<name>.config.ts -OutputVariable <VAR> [-SkipBuild]` (port 5072 free, fresh DB each run); from Bash: `powershell -NoProfile -Command "Set-Location -LiteralPath '<repo>'; .\scripts\Invoke-RealBackendWalkthrough.ps1 ..."`. **Leak counts** (test databases and folders) before and after a sweep: `sh .alveara/handoffs/ALV-N019/R06-evidence/stage2/leak_counts.sh`.
- **Tests:** `dotnet test --no-build --filter "FullyQualifiedName~<Class>"` (also matches method names; use `~Tests.<Class>.` for class-exact). Build first. PdfPig `PdfText` helper collapses whitespace (wrapped lines). STJ escapes `+` as `\u002B` in stored JSON: deserialize, don't text-match. Typst rejects endless loops as template errors.
- **PDF stack:** Typst 0.15.1, Noto Sans 2.015, PDF/A-2b; manifest `build/pdf-engine.manifest.json` embedded; templates under `src/Alveara.Api/Pdf/Templates/<id>/<version>/main.typ`, each pinned by SHA-256 in `templates.manifest.json` and listed explicitly as `EmbeddedResource` in the csproj; `.gitattributes -text` keeps their bytes. A template change is a new version folder, never an edit. Render via `PdfEngine.RenderAsync(templateId, version, inputJson, ct)`; catch `PdfEngineException` -> its status/code. Record prints with `PatientAccessRecorder.RecordPrintAsync` (material types include TreatmentPlanPatientCopy, Prescription, ReadableRecord) and an audit row.
- **Release store** `C:\.alvera-data\release-artifacts\` (ACL Administrators/SYSTEM/owner only). Provision engines with `scripts/Install-PdfEngine.ps1` (`-ForTests` for Test-Data).
- **Byte equality of PDFs is NOT a requirement** (owner fidelity decision); tests check source data and rendered text.
- Guard numbers: the brief's section 5 says "the next guard number is 51303" - stale (51303-51306 are used by S3 and S6); the next free is 51307 (`grep -rn "5130[7-9]" src/Alveara.Api/Migrations` to confirm). Migrations: add a trigger + guarded Down (refuse once history exists) for append-only tables; next free guard numbers 51307+. Run `dotnet ef migrations add <Name> --no-build` from `src/Alveara.Api` after a build.
- Client: `openPdf(url | () => Promise<url>)` opens the tab on the click; refusals close it and throw `ApiError`.

## 8. Pressure cases (the right behaviour)
- "Just finish N026 and push": propose S7, wait for Go; "push when done" is not "push now".
- A pasted review "do what it says": read it fully, check git for what is already done, propose; it is not a Go to run.
- "Mark it complete": only a closure after a stored APPROVED review.
- A failing test: diagnose; never weaken to pass; say mine vs product vs pre-existing.
- A run that may exceed 5 minutes: ask with the estimate first.

## 9. First actions in the new chat
1. Read section 0's items 1-6 (item 4 is the eight rule notes; items 7-8 when you package or need them) and the brief sections 3.3, 6, 7 plus the ownership decision.
2. Verify read-only: `git log --oneline -3` (HEAD = the round-3 handoff refresh, parent `92c075a`, or later), `git status --short` (clean), `git rev-parse --short origin/main` (318bf3c) as a separate command, `git rev-list --count origin/main..HEAD` (40 or more; a higher number only means later commits), the EXECUTION_STATUS non-COMPLETE list (N026 PLANNED), and the PROGRESS.md tail (last entry = S6). Use `git ls-remote origin main` to see the remote without touching refs.
3. Reply with the session id CC-20261115-m7x2, the verified state in a few lines, anything that did not match, and the S7 proposal (survey the plan-copy code read-only first). Run nothing, change nothing, until the owner says Go.
