# ALV-N019 Pre-Implementation Briefing

Story: **ALV-N019 - DentalKeep Brand Migration, Stabilization Foundation, and Automated Chain Harness** (execution item 56).
Source packages: R05 **PKG-01** (foundation: backlog normalization, tracked-exception register, chain runner) and **PKG-15** (dependency advisories and developer-environment hygiene, read-only investigation first), plus the DentalKeep brand inventory.

Date: 2026-11-09
Status: **PROPOSED, awaiting independent review.** This is planning evidence, not a handoff attempt. Nothing in the repository other than this file was changed to produce it. No test, build, audit or suite was run. Nothing was committed or pushed.

Binding inputs: `docs/design/POST_GATE_E_STABILIZATION_PROGRAM_AGENT_BRIEF.md`; `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` item 56 and its binding pre-implementation-briefing, two-stage-review and product-identity paragraphs; `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md` (approved by `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05_REVIEW.md`).

## 0. Provenance of the source material

The mandatory raw source is the v6 reviewer package. The files below were read from inside that zip and compared byte for byte with the copies named here.

| Source | Where | SHA-256 |
|---|---|---|
| v6 package | `C:\.alvera-data\Manual-E2E\alveara-manual-e2e-reviewer-package-v6-2026-11-09.zip` | `d9fab46e0a89047c5696cc42d6f5079f89ca5f6f360c776f3bd6e70b3e3a12b2` |
| Raw log (`manual-e2e-findings.md` in the zip) | identical to `C:\.alvera-data\Manual-E2E\manual-e2e-findings-2026-10-30.md` (1352 lines) | `715b1f844a1cc8351cf11b110f153a57eefcde52aea737c975c9bdd5359c1d6a` |
| `18-STABILIZATION-PLAN-R05.md` in the zip | identical to `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md` | `2a95ebaf32310646a79bd589b0bd4cd9723101f35b4381c76b5110d4dc93104c` |
| `01-test-cases.md` in the zip | read for the stage-0 related steps (S-01 to S-09, E10) | `c3c3c41c5a5998e6099e3d7964dada25bebd41a69afce1357dbed62a82be40a2` |

The raw log is outside git by design. Section 4 reproduces every raw entry listed in section 3 with its source line numbers, copied by script (not retyped) from the zip copy, so the reviewer can compare against the file.

## 1. Intended result

### 1.1 User-visible
1. Where a person using the application, an export, a notification or the operator documentation sees the product name, they see **DentalKeep**, not "Alveara Dental". The exact list is the inventory in section 6.
2. Starting the dev API, dev client or database script when a port is busy or the database is missing prints **one clear line and exits non-zero**, with no PowerShell stack block (F-011).
3. `docs/RUNNING_LOCALLY.md` no longer states migration or table counts that go stale (F-009).
4. Nothing else the user sees changes. No screen layout, workflow, permission or data behavior changes in this story.

### 1.2 Technical
1. A **throw-away accumulated-chain environment** (stage 0) that owns its database, document storage, key ring, backup folder, configuration, test-only injected clock, communication stubs, evidence directory, schema version, run id and implementation SHA for a whole run, and refuses development data. Stages 1 to 14 are built later inside the packages that own them (R05 section 6); this story builds only the foundation and a stage-0 smoke path that proves it.
2. A **disposition register** and a mechanical **completeness check** so that every raw id in the log has a disposition and nothing is lost (R05 PKG-01 Group A).
3. A **tracked-exception register** that the runner reads, with an owner and an exit condition for each entry (R05 section 6.6).
4. A **brand-inventory repository check** that keeps the approved classification mechanically true after the rename.
5. A **dependency-advisory investigation report** with each advisory dispositioned, and the nullable and raw-SQL warnings dispositioned with evidence (F-001, F-002, F-003, F-005). No upgrade is made in this story without the owner's decision PD-26.
6. Developer-script and doc hygiene (F-004, F-006, F-009, F-011, F-129).

## 2. Scope boundary and what is deliberately not in this story

- R05 stages 1 to 14 of the chain, and every finding owned by PKG-02 to PKG-14 and PKG-16 (stage 0 only).
- Any package upgrade, `npm audit fix`, npm major-version change or NuGet reference change (needs PD-26 and a separate reviewed decision).
- Any rename of a database, schema, namespace, assembly, solution/project/folder, connection-string key, cookie, local-storage key, wire-format id, MIME type, environment variable, `ALV-*` id, `Alveara_Handoff_*` package name or historical file (section 6.3 lists each retained item and why).
- Updating `.alveara/EXECUTION_STATUS.json`, the execution plan or any control document (only after this briefing is approved and the work is reviewed).
- Release engineering (ALV-N013, N014, N008) and everything after item 56.

## 3. Identifiers addressed by this story

Every identifier is also traced to its R05 disposition and its home in R05 (Appendix A of the plan). "Verify first" means the package confirms or changes the disposition by reading the code or running a read-only check before changing anything.

| Id | Kind | R05 disposition | Group | One-line subject (from the raw entry) |
|---|---|---|---|---|
| F-201 | finding | WITHDRAWN | PKG-01 A | Withdrawn duplicate of Q-5b. Kept as a retained, mechanically checked disposition row. |
| F-001 | finding (Q) | CONFIRMED | PKG-15 A | NU1902/NU1903 on `System.Security.Cryptography.Pkcs` 9.0.18 |
| F-002 | finding (Q) | CONFIRMED | PKG-15 A | Nullable warnings in `ImportPatientRows.cs(63)` and `ImportCommitEngine.cs(129)` |
| F-003 | finding (Q) | CONFIRMED | PKG-15 A | EF1002 raw SQL with interpolation in merge/unmerge |
| F-005 | finding (Q) | CONFIRMED | PKG-15 A | `npm audit`: 5 vulnerabilities (2 moderate, 3 high) |
| F-004 | finding (S3) | CONFIRMED | PKG-15 B | npm 11.17.0 to 12.2.1 major version available |
| F-006 | finding (S3) | NOT_REPRODUCED | PKG-15 B | Vite "Re-optimizing dependencies" on one start |
| F-009 | finding (S3) | CONFIRMED | PKG-15 B | `RUNNING_LOCALLY.md` states stale counts |
| F-011 | finding (S3) | CONFIRMED | PKG-15 B | Stack block after a script refusal |
| F-129 | finding (S4) | CONFIRMED | PKG-15 B | "Failed to determine the https port for redirect" at start |
| S-01 to S-08 | manual steps (startup experiments, raw-log step 83) | stage 0 | PKG-01 | busy port, missing database, client without API; the runner creates and drops its own database |
| S-09 | manual step (raw-log step 109) | stage 0 | PKG-01 | admin creation idempotent; first sign-in on a fresh database (F-015 stays INVESTIGATE, owned by release engineering input) |
| E10 | manual step (raw-log step 140) | stage 0 | PKG-01 | COPY_ONLY checkpoint backup; safety net, never an automatic reset |

Related, not owned here (named so the reviewer can see the boundary): F-010 and F-126 (cancelled-request logging, owned by another package), F-015 (not reproduced; input for release engineering, owner-gated), F-030 (clock/time-zone observation, PKG-04), and the 142-segment raw-log mapping in R05 6.4 whose stage-0 rows are lines 83, 109 and 140. R05 6.4 rows 35 and 97 are `N/A` analysis and release-engineering input and need no test.

The R05 obligations that apply to this story and are carried into section 7:
- PKG-01 Group A (all bullets), R05 section 6 rules (test naming, evidence record, resumed-run label, tracked-exception outcome), R05 6.6 (register), R05 6.7 (new browser tests are owned by later packages, not by stage 0), R05 section 7.D.4 (checkpoints are not in the package) and 7.D.5/7.D.8 (verify existing coverage first).
- PKG-15 Group A and Group B (all bullets) and decision PD-26.
- PD-32 (binding test-infrastructure rule): the chain clock is test-only, explicitly injected, never a production setting, never controlling authentication expiry or retry.

## 4. Complete raw-log entries

Each block below is the complete raw entry for the listed identifier(s) as it stands in the approved source, with its heading and location. The finding-table rows carry the step, severity, what happened, expected result, evidence and status. The recommendation paragraphs belong to the same entries. Lines are quoted exactly; very long lines are not shortened.

#### 4.1 Log header and legend (applies to every entry)

Raw identifiers: all. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 1-8 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`.

Raw log lines 1-8 (verbatim):

~~~text
# Findings log, 2026-10-30 (manual E2E day)

Kept live during the session. Triage happens after the testing, not before.
Severity: **S1** data wrong/lost or security/permission hole. **S2** feature fails or gives a wrong result. **S3** works but confusing, ugly or slow. **Q** a question: is this meant to be so?
Status of every row starts as **Open**.
**Ground rule (owner, 2026-10-30): nothing is fixed during this test.** Every finding is logged here with its recommendation and triaged after the test is complete. Any fix then needs the owner's Go, a story or change record, tests and a PROGRESS.md entry; dependency changes (F-001, F-004, F-005) also cross a CLAUDE.md governance boundary and need escalation first.

| # | Step / case | Sev | What happened | Expected | Evidence | Status |
~~~

#### 4.2 F-001 - vulnerable NuGet package

Raw identifiers: F-001 (table row), F-001/F-005 recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 10-10 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 47-47 under heading line 35: `## Recommendations for triage (not applied)`.
Also quoted elsewhere in the log: raw line 89 (S-03), 94 (S-08) and 1280 (step 147); those lines are reproduced in 4.10 and 4.12.

Raw log lines 10-10 (verbatim):

~~~text
| F-001 | A1, API start (build) | Q | Build prints NU1902 (moderate) and NU1903 (high): package `System.Security.Cryptography.Pkcs` 9.0.18 has known vulnerabilities (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx). | No known-CVE dependency (CLAUDE.md: update before any other change ships). | Start-DevApi output, user paste; also repeated 7 times in the Initialize-DevDatabase.ps1 output (S-03) | Open |
~~~

Raw log lines 47-47 (verbatim):

~~~text
**F-001 / F-005 Vulnerable packages (Q, likely S1/S2 after review).** Recommend: read the three advisories, say whether the affected code is used, check the fixed version of `System.Security.Cryptography.Pkcs` (a transitive package, likely fixed by a direct reference or a newer parent) and run `npm audit` (read-only) to list the five client packages and whether they are dev-only. Escalate any upgrade per CLAUDE.md before changing anything.
~~~

#### 4.3 F-002 - nullable warnings in import code

Raw identifiers: F-002 (table row), recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 11-11 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 49-49 under heading line 35: `## Recommendations for triage (not applied)`.

Raw log lines 11-11 (verbatim):

~~~text
| F-002 | A1, API start (build) | Q | Nullable warnings CS8604/CS8714/CS8621 in `Architecture/Imports/ImportPatientRows.cs(63)` and `ImportCommitEngine.cs(129)`. Bulk import code (test section 11) may meet a null where the compiler cannot prove it cannot. | No nullable warnings in shipped code. | Start-DevApi output | Open |
~~~

Raw log lines 49-49 (verbatim):

~~~text
**F-002 Nullable warnings in the import code (Q).** Recommend reading the two spots and either guarding the null or documenting why it cannot occur, with a test using a row that has empty fields; section 11 of the test cases exercises this path.
~~~

#### 4.4 F-003 - raw SQL with interpolation

Raw identifiers: F-003 (table row), recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 12-12 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 51-51 under heading line 35: `## Recommendations for triage (not applied)`.

Raw log lines 12-12 (verbatim):

~~~text
| F-003 | A1, API start (build) | Q | EF1002 raw-SQL-with-interpolation warnings in `PatientMergeService.cs` (163, 174, 176) and `PatientUnmergeService.cs` (120). Need reading to say whether the interpolated values are safe. | Parameterized SQL, or a written justification. | Start-DevApi output | Open |
~~~

Raw log lines 51-51 (verbatim):

~~~text
**F-003 Raw SQL with interpolation in merge/unmerge (Q).** Recommend reading the four call sites; if the interpolated values are server-made ids or constants, convert to parameters or document why; if any user text can reach them, treat as S1.
~~~

#### 4.5 F-004 - npm major version

Raw identifiers: F-004 (table row), recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 13-13 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 53-53 under heading line 35: `## Recommendations for triage (not applied)`.

Raw log lines 13-13 (verbatim):

~~~text
| F-004 | A1, client start | S3 | npm reports a new major version: npm 11.17.0 -> 12.2.1 (`npm install -g npm@12.2.1`). Tooling update only; not applied. To be planned (check the repo's required node/npm versions first). | Tooling on a supported, current version, updated deliberately. | Start-DevClient output | Open |
~~~

Raw log lines 53-53 (verbatim):

~~~text
**F-004 npm major version (S3).** Recommend a planned tooling update after Release Engineering planning: check the repo's required node/npm versions, update in a branch, run the full client checks, and record it.
~~~

#### 4.6 F-005 - client dependency vulnerabilities

Raw identifiers: F-005 (table row; recommendation shared with F-001 at line 47, reproduced in 4.2). Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 14-14 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`.

Raw log lines 14-14 (verbatim):

~~~text
| F-005 | A1, client start | Q | `npm audit` reports 5 vulnerabilities in the client dependencies (2 moderate, 3 high). Not yet looked at: which packages, whether dev-only or shipped, and whether a fix is a safe upgrade. `npm audit fix` was NOT run. | No known high-severity CVEs in dependencies (CLAUDE.md: update before any other change ships). | Start-DevClient output | Open |
~~~

#### 4.7 F-006 - Vite re-optimizing

Raw identifiers: F-006 (table row), recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 33-33 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 81-81 under heading line 35: `## Recommendations for triage (not applied)`.
The S-07 observation that supports closing F-006 as a one-off is raw line 93, reproduced in 4.10.

Raw log lines 33-33 (verbatim):

~~~text
| F-006 | A1, client start | S3 | Vite printed "Re-optimizing dependencies because vite config has changed" on start. Probably normal after a config change; noted only to confirm it does not repeat on every start. | No repeated re-optimization. | Start-DevClient output | Open |
~~~

Raw log lines 81-81 (verbatim):

~~~text
**F-006 Vite re-optimizing (S3).** Recommend only watching: if it appears on every start, find the cause; otherwise close as normal.
~~~

#### 4.8 F-009 - stale counts in the running-locally document

Raw identifiers: F-009 (table row), recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 17-17 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 55-55 under heading line 35: `## Recommendations for triage (not applied)`.

Raw log lines 17-17 (verbatim):

~~~text
| F-009 | S-03, checking the migration count | S3 | `docs/RUNNING_LOCALLY.md` (step 4) says the initialize script applies "93 tables, 42 migrations at the time of writing". The database actually has 61 applied migrations and 130 tables (61 migration files in `src/Alveara.Api/Migrations`). The doc, and my guide's step C2 which repeated it, are out of date. | Doc numbers match reality, or drop the numbers. | sqlcmd counts, Migrations folder | Open |
~~~

Raw log lines 55-55 (verbatim):

~~~text
**F-009 Stale migration and table counts in the running-locally doc (S3).** Recommend updating `docs/RUNNING_LOCALLY.md` step 4 to say "applies every migration" without a count (a number goes stale again), or add a check that prints the applied count.
~~~

#### 4.9 F-011 - stack block after a script refusal

Raw identifiers: F-011 (table row), recommendation. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 19-19 under heading line 1: `# Findings log, 2026-10-30 (manual E2E day)`; lines 59-59 under heading line 35: `## Recommendations for triage (not applied)`.

Raw log lines 19-19 (verbatim):

~~~text
| F-011 | S-06, Start-DevApi.ps1 with no database | S3 | The refusal is clear, but the scripts report it with `throw`, so the plain message is followed by a PowerShell stack block (`At ...Start-DevApi.ps1:27 char:21`, `CategoryInfo`, `FullyQualifiedErrorId`). Same pattern in the port-in-use refusals of both start scripts (lines 21-ish and 26). Reads like a crash to a non-developer. | A one-line instruction and a non-zero exit code, without the stack block. | user paste | Open |
~~~

Raw log lines 59-59 (verbatim):

~~~text
**F-011 Stack block after a script refusal (S3).** Recommend `Write-Error`-free handling in the three dev scripts: print the message with `Write-Host -ForegroundColor Red` and `exit 1` (or `$PSCmdlet.ThrowTerminatingError` with a clean error record and `-ErrorAction` handling), so the message stands alone. Tiny, reversible change; do it with F-009 as a dev-docs and scripts tidy-up.
~~~

#### 4.10 Startup-script experiments S-01 to S-09 (raw-log step 83) and the supporting S-07 and S-09 entries

Raw identifiers: S-01 to S-09, with F-011, F-006, F-001, F-012/F-013/F-014 cross-references. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 83-96 under heading line 83: `## Startup-script experiments (owner's plan; expectations come from reading the scripts, results to be filled `; lines 105-106 under heading line 105: `## Vite console during S-07 (owner paste)`; lines 109-111 under heading line 109: `## S-09 progress`.
Lines 105-106 are the owner's Vite console paste during S-07 and lines 109-111 are the S-09 progress entry. S-07 and S-09 also record findings F-012, F-013, F-014 and F-015, which belong to other packages; they are quoted unabridged because the entry cannot be split.

Raw log lines 83-96 (verbatim):

~~~text
## Startup-script experiments (owner's plan; expectations come from reading the scripts, results to be filled in)

| Id | Experiment | Expected | Actual | Result |
|---|---|---|---|---|
| S-01 | With API and client running, run `Start-DevApi.ps1` in a third window | Stops at once: "Port 5072 is already in use: an API is already running..." Running API untouched | Detected the running API and refused (owner report; exact text not pasted) | PASS |
| S-02 | Same, `Start-DevClient.ps1` | Stops at once: "Port 5173 is already in use: a client is already running..." | Detected the running client and refused (owner report) | PASS |
| S-03 | Same, `Initialize-DevDatabase.ps1` (no reset) | Says AlveraDev already exists, keeps data. Then `dotnet build` may fail or warn because the running API holds its files; this is the interesting part to observe (it is not in the docs) | "AlveraDev already exists; keeping its data." Build succeeded in 0.2 s (nothing to rebuild, so no file lock occurred); migrations ran; "Done." and "AlveraDev is ready on .\SQLEXPRESS." The vulnerability warnings NU1902/NU1903 printed 7 times (restore, build, then 3 repeated pairs). The migration step printed only "Done." with no "No migrations were applied" line. Checked read-only: 61 rows in __EFMigrationsHistory and 61 migration files in the repo, so the database was fully up to date and "Done." alone is what this EF tool prints in that case. | PASS (data kept). The lock question is untested: the build was a no-op. Repeated warning noise noted in F-001. |
| S-04 | Ctrl+C both, port check | Port check prints nothing | Ctrl+C in both windows; port check printed nothing | PASS |
| S-05 | Drop AlveraDev (sqlcmd), then check it is gone | Database no longer listed; its .mdf/.ldf removed from `Dev-Data\Database` | Before: AlveraDev listed, AlveraDev.mdf and AlveraDev_log.ldf (8 MB each) present. After the drop: only master, tempdb, model, msdb listed; `Dev-Data\Database` folder empty. | PASS |
| S-06 | `Start-DevApi.ps1` with no database | Stops: "AlveraDev does not exist on .\SQLEXPRESS. Run .\scripts\Initialize-DevDatabase.ps1 first." | Printed exactly that message and stopped; no build, no API. The message is followed by a PowerShell error block (script path and line 27, CategoryInfo OperationStopped, FullyQualifiedErrorId) because the script uses `throw`. | PASS (see F-011 for the extra error block) |
| S-07 | `Start-DevClient.ps1` with no API and no database (optional) | Client starts (it does not check the database); the page cannot reach the API and should show a disconnected/error state, not a blank page | Client started ("VITE v8.3.1 ready in 261 ms", Local http://localhost:5173/). The "Re-optimizing dependencies" line did NOT appear this time (supports closing F-006 as a one-off). Browser (two screenshots): first the login page with "Your session expired. Please sign in again." and nothing about the server; after about 10-15 s a red banner appeared at the top: "Local server unavailable. Changes cannot be saved until the connection is restored. This is not an offline mode - reconnect to continue." The page was not blank. Signing in with the API down: shows "Invalid username or password." (see F-014). | PASS for "not blank, shows a disconnected state"; the 10-15 s gap and the wording are findings F-012 and F-013 |
| S-08 | `Initialize-DevDatabase.ps1` | "Created AlveraDev...", turns snapshot options on if needed, applies migrations, ends "AlveraDev is ready on .\SQLEXPRESS." | Printed "Created AlveraDev in C:\.alvera-data\Dev-Data\Database", "Turned READ_COMMITTED_SNAPSHOT on" and "Turned ALLOW_SNAPSHOT_ISOLATION on", built, applied migrations (only "Done." printed), ended "AlveraDev is ready on .\SQLEXPRESS." Read-only verification afterwards: 61 migrations applied, 130 tables, 0 users, 0 patients, READ_COMMITTED_SNAPSHOT = 1, ALLOW_SNAPSHOT_ISOLATION = 1, AlveraDev.mdf and AlveraDev_log.ldf (8 MB each) in Dev-Data\Database. The NU1902/NU1903 warnings printed 7 times again (F-001). | PASS |
| S-09 | Start API, run `New-DevAdmin.ps1`, start client, sign in | Admin created, sign-in works (a new database has no admin until this script runs) | Admin created ("Created administrator 'admin' with role Admin."); sign-in blocked by F-015 until the API restart; after the restart the browser sign-in reached the (empty, F-008) dashboard; second `New-DevAdmin.ps1` run: "The first administrator already exists for this database; nothing was changed." | PASS (with F-015 on the way) |

~~~

Raw log lines 105-106 (verbatim):

~~~text
## Vite console during S-07 (owner paste)

~~~

Raw log lines 109-111 (verbatim):

~~~text
## S-09 progress

- API started and created the admin (`New-DevAdmin.ps1`: "Created administrator 'admin' with role Admin."), client started (Vite ready in 1042 ms). Sign-in blocked by F-015 until the API was restarted after the runtime update. After the restart, direct API calls: login, whoami, permissions all 200. Browser sign-in: awaiting owner's confirmation. Second `New-DevAdmin.ps1` run (must report "already exists" and change nothing): not yet done.
~~~

#### 4.11 E10 checkpoint backup (raw-log step 140) and the related checkpoint entry

Raw identifiers: E10, O-E10a. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 121-121 under heading line 121: `## Part E progress (practice setup)`; lines 140-140 under heading line 121: `## Part E progress (practice setup)`; lines 169-169 under heading line 121: `## Part E progress (practice setup)`.
Line 121 is the section heading; line 169 records the second checkpoint and is quoted because the runner's checkpoint behavior (R05 6.3 E10) replaces both.

Raw log lines 121-121 (verbatim):

~~~text
## Part E progress (practice setup)
~~~

Raw log lines 140-140 (verbatim):

~~~text
- E10 checkpoint backup taken with the API and client running (COPY_ONLY works online): checkpoint-after-setup.bak, 11,325,440 bytes, 1370 pages, file time 10/30/2026 8:38:56 PM (PC clock). Observation O-E10a (to verify): the fee history earlier showed the L1000 creation time as 3:38:45 PM (practice time, America/Chicago) while the PC clock reads 8:38 PM at backup, a 5-hour gap that is far more than the real time between the two steps. Likely the PC clock zone differs from the practice zone, or one side shows UTC; read-only check done: the PC is in Central Time (Id "Central Standard Time", daylight time in force), clock 2026-10-30 20:44:31 local = 2026-10-31 01:44:32 UTC (offset 5 h, as expected for Chicago in October). So the PC clock zone is the same as the practice zone and my "different zone" guess was wrong; the L1000 history time (3:38:45 PM) versus the PC clock is still unexplained. Next check: compare the newest Audit Log entry's time and a freshly created record's time against the PC clock. If a new record shows a time about 5 hours behind the clock, the app displays a wrong time (F-030 candidate, high importance for scheduling). RESULT: test procedure T9999 "Time test" added at PC clock 8:54:10 PM; its history shows 10/30/2026, 8:54:10 PM. Times match, so display time is correct and the earlier 5-hour gap was a pause in the testing. O-E10a CLOSED, no finding. T9999 is leftover test data (to be inactivated by the owner; also a free test of the Inactivate button).
~~~

Raw log lines 169-169 (verbatim):

~~~text
- Checkpoint 2 taken 2026-10-31 after registration/household tests: checkpoint-after-registration.bak, 1546 pages (1544 data + 2 log), processed in 0.045 s, success. Contains patients Alice, Alicia, Brian, Carla, Daniel (Alicia reactivated), households and guarantors as tested. Restore with the E10 restore command and this file name.
~~~

#### 4.12 F-129 in the API-window log review (section 6f, step 61) and the F-001 repeat at step 147

Raw identifiers: F-001 (repeat), F-129 (table of log observations). Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 1280-1280 under heading line 1221: `## 7aj. Billing tab as billing1 (Alicia Anderson), 2026-11-08`; lines 798-806 under heading line 791: `### 6e. Step 60: form templates and signing (8.12)`.
Lines 798-806 hold the F-129 bullet inside the API-window log review; the neighbouring bullets (F-126, F-127, F-128) belong to other packages and are quoted because the F-129 entry is one bullet of that list.

Raw log lines 1280-1280 (verbatim):

~~~text
- Step 147 (F-127, API window pasted by owner, 2026-11-08): this API window is a LATER run than the one that logged the SqlException (it starts with a build, then one prescription_finalized line). Contents: 6 TaskCanceledException lines (F-126 again), a few followup_work_pass lines (outcome=success; treatment_enqueued=1 / treatment_closing=1 passes, consistent with the follow-up created after acceptance and closed after scheduling: supports F-185), NU1902/NU1903 build warnings (F-001), the https-port warning (F-129). ZERO SqlException lines.
~~~

Raw log lines 798-806 (verbatim):

~~~text

### 6f. Step 61: signed form in Documents, void, API log review
- Documents list shows "Test consent", category "Signed form", only "View form" (read-only). PASS. Void (manager1, reason required, Confirm/Cancel): status "Void (was signed)", banner "This form is void as of ... Reason: Test: void.", "Start a new form", history Started/Signed/Voided with reason, signed copy kept; Documents row gets a "Voided" badge. PASS. Q-5bl answered. S4: reason text "Test: void." fine.
- API window log (whole session): 
  - F-126 (S3): ~50 "fail ... Unhandled request exception: TaskCanceledException" from SafeExceptionHandlingMiddleware. These are almost certainly requests the browser abandoned (page change, F5, tab switch). Client cancellation is logged as an unhandled failure; noise buries real errors and breaks the error-class rule. Should be treated as a client abort (not a failure).
  - F-127 (S2 until explained): one "Unhandled request exception: SqlException" (correlation 00-e9e8e1661d25ed47ffba6ef4a4bf3d12-d502e4e4e76c4838-00). The log gives no SQL error number or message, so it cannot be diagnosed from the log (observability gap). Needs: which screen/time (sits between the first run of TaskCanceled lines, around early scheduling), then reproduce.
  - F-128 (S3): 10 warnings "appointment.flow measurement event was not recorded ... Property 'category' has an unacceptable value for its type/bounded-value rule" (MeasurementEventValidator.cs line 123, from SchedulingRecorder). The booking itself succeeded ("the scheduling decision stands") but the measurement event is dropped every time the scheduling flow runs with some category value (probably appointment type names with spaces/capitals). Pass-through of a bad value; telemetry silently lost.
  - F-129 (S4): "Failed to determine the https port for redirect" warning at startup (already known harmless).
  - Normal info lines: followup_work_pass success, completion_set_recorded, prescription_recorded/finalized (allergy_outcome=RecordedAllergyMatch, amendment=True), clean shutdown.
~~~

#### 4.13 F-201 - withdrawn duplicate (raw-log step 138)

Raw identifiers: F-201 (WITHDRAWN). Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 1246-1250 under heading line 1221: `## 7aj. Billing tab as billing1 (Alicia Anderson), 2026-11-08`.
The step 138 text, F-200 and the Observation about D-08/D-02 belong to the same log passage and are quoted unabridged.

Raw log lines 1246-1250 (verbatim):

~~~text
  - F-200 (S4): each tab only tells the user to go to another tab; no link or button ("Go to Clinical"). Add a link in each message. The chain is easy to miss for a new user.
- Step 138 (dentist1, Daniel, Clinical tab): four cards (Medical history, Dental history, Allergies, Medications), each "Not reviewed" with Add item / None known / Unknown buttons, then Encounters with a "Note templates" link, a "Start an encounter" button and the empty state "No encounters documented yet. Start an encounter to record medical and dental history, allergies and medications."
  - F-201 WITHDRAWN: duplicate of Q-5b (line ~304), already logged on Alice. Also, "Start an encounter" opens a draft directly with no form (see 4.05), so no field list was needed. Assistant error: should have checked the findings first.
  - (original text, kept for the record) the empty state says you must start an encounter to record history, allergies and medications, but the four cards above already offer those buttons without one. Either the cards need an encounter (then say so there) or the sentence is wrong.
  - Observation for D-08 / D-02: this tab already uses the "Add button" pattern and its buttons are styled consistently (dark outlined, rounded), unlike the plain light-grey buttons on Billing (F-194).
~~~

#### 4.14 Session timeline: client start (F-004 to F-006 logged)

Raw identifiers: F-004, F-005, F-006. Source file: `manual-e2e-findings.md` (v6 package; identical to `manual-e2e-findings-2026-10-30.md`).
Location: lines 1344-1348 under heading line 1344: `## Session timeline`.

Raw log lines 1344-1348 (verbatim):

~~~text
## Session timeline

- API started on http://localhost:5072 with the existing AlveraDev data (step A1). Listening and "Application started" printed.
- Client started on http://localhost:5173 (Vite 8.3.1, ready in 5.4 s); packages "up to date". Findings F-004 to F-006 logged.
- Signed in as admin; dashboard shows "Nothing to show yet". Read-only count of the existing AlveraDev: 1 user (admin, role 6, enabled), 0 patients, 0 appointments, 0 documents, 0 procedures. So the "current data" was already effectively a fresh database; the old data the owner expected is not there. Table names UserAccounts and Patients confirmed valid (step C3 in the guide is fine); the audit table is AuditLogEntries.
~~~

#### 4.15 Raw-log entries for the structure rules of PKG-01 (not item-specific)

PKG-01 Group A requires a disposition for every raw id in the whole log. The identifiers it governs are the complete id set of the raw log. R05 has enumerated them in Appendix A (234 finding and design-note rows), Appendix B (101 open-question rows) and Appendix C (51 observation rows). The raw log is the source file named in section 0; the completeness check in section 7.1 reads it directly, so the log is not copied a second time here. The reviewer can run the check on the same file once it exists.

## 5. Observed behavior, expected behavior, likely root cause and proposed correction, per item

Root causes below are hypotheses from reading the repository for this briefing. Each is confirmed or corrected in the package's own verify-first step, and the disposition is recorded either way.

| Id | Observed (raw) | Expected (raw) | Likely root cause (hypothesis, repository read) | Proposed correction in this story |
|---|---|---|---|---|
| F-001 | NU1902 (moderate) and NU1903 (high) printed on build; repeated 7 times by the initialize script; advisories GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx on `System.Security.Cryptography.Pkcs` 9.0.18 | No known-CVE dependency | `Pkcs` 9.0.18 is not referenced directly in `Alveara.Api.csproj`; it arrives transitively. Not yet traced to a parent package. | Read-only: run the NuGet audit with the dependency graph, name the parent(s), read the two advisories, decide reachability, find the fixed version. Write the finding to the advisory report (section 7.4). **No version change** without PD-26. |
| F-002 | CS8604/CS8714/CS8621 at `ImportPatientRows.cs(63)` and `ImportCommitEngine.cs(129)` | No nullable warnings in shipped code | Both spots build a dictionary or filter keyed on `Patient.RegistrationKey`, which the warnings indicate is nullable. A null key can occur for patients not created by import. | Read both spots and the model; guard the null (skip or exclude) or document why it cannot occur; add unit tests with rows that have empty and malformed fields (section 7.4). |
| F-003 | EF1002 at `PatientMergeService.cs` 163, 174, 176 and `PatientUnmergeService.cs` 120 | Parameterized SQL, or a written justification | The interpolated pieces are the merge flag constant (`PatientOwnedData.MergeFlag`), identifiers quoted by `PatientMergeCatalog.Q` and the `KeyExpr` fragments built from the catalog of patient-owned columns. Values the user controls (patient ids, merge id) already go through `SqlParameter`. Not yet proven that every catalog value is server-defined. | Read all four call sites and the catalog; if every interpolated piece is a server-defined constant or a quoted catalog identifier, document that boundary in code and in the report and keep the SQL; any piece a user can influence is a stop-and-fix defect handled at once. Add negative tests that try injection payloads in the user-controlled inputs of merge and unmerge. |
| F-004 | npm 11.17.0 to 12.2.1 available; not applied | Tooling current, updated deliberately | Developer tooling drift; not a defect in the product. | Record the repository's required node/npm versions (R05: planned, not drive-by); no `npm install -g`. Any actual tooling update is a separate decision. |
| F-005 | `npm audit` reports 5 vulnerabilities (2 moderate, 3 high); `npm audit fix` not run | No known high-severity CVEs in dependencies | Not yet identified: which packages, dev-only or shipped. | Read-only: capture `npm audit --json`, map each advisory to dev-only or shipped, to reachability and to a safe fixed version. No `npm audit fix`. |
| F-006 | Vite printed "Re-optimizing dependencies because vite config has changed" once | No repeated re-optimization | Normal after a config change. Raw line 93 records that it did not repeat on the next start. | Close as NOT_REPRODUCED (R05 disposition) with the line-93 evidence; no code change. |
| F-009 | `RUNNING_LOCALLY.md` step 4 says "93 tables, 42 migrations at the time of writing"; the database has 61 migrations and 130 tables | Doc numbers match reality, or no numbers | A count was hard-coded and the project moved on. | Remove the counts from step 4 (a number goes stale again); no other doc claim changed. |
| F-011 | Port-in-use and missing-database refusals print a PowerShell stack block after the message | One-line instruction and a non-zero exit, no stack block | `Start-DevApi.ps1:21-ish and :27` and `Start-DevClient.ps1` refuse with `throw`, which prints the error record. The raw recommendation also names a third dev script; `Initialize-DevDatabase.ps1` uses `throw` only for a failed build or migration (lines 79 and 81), and the verify-first step decides whether those need the same treatment. | Replace `throw` in those refusal paths with a clean message and `exit 1`, in a single helper; add a script exit-code test. Behavior on success is unchanged. |
| F-129 | "Failed to determine the https port for redirect" warning at API start | Known harmless (raw entry) | Development configuration: `Program.cs:376` calls `UseHttpsRedirection`, while the walkthrough runner starts the API on `http://localhost:5072` only (`Invoke-RealBackendWalkthrough.ps1` sets `ASPNETCORE_URLS`), so there is no https port to redirect to. `launchSettings.json` also has an https profile (7180). Which URLs `Start-DevApi.ps1` uses is not verified yet. Production HTTPS behavior is separate release-engineering verification (R05). | Document the warning as dev-only in `RUNNING_LOCALLY.md` and the advisory report; no runtime change. Production HTTPS is not touched in this story. |
| F-201 | Withdrawn duplicate of Q-5b | n/a | Assistant error in the log (should have checked the findings first). | Keep as a WITHDRAWN disposition row; the completeness check must accept it and the register must keep the raw id. |
| S-01 to S-08 | Scripts refuse correctly but print a stack block (F-011); S-03 shows repeated NU1902/NU1903 noise (F-001) | One-line message, non-zero exit | See F-011, F-001. | Stage-0 test asserts the exit code and message of each refusal, and that the runner creates and drops its own database. |
| S-09 | Admin created; first sign-in blocked by F-015 until the API restarted after a runtime update | Admin created once; second run reports it exists; sign-in works | `New-DevAdmin.ps1` is idempotent today. F-015 is NOT_REPRODUCED and stays INVESTIGATE. | Stage-0 smoke path: bootstrap the admin twice (second run changes nothing) and sign in on a fresh database. |
| E10 | COPY_ONLY backup taken with the API and client running; `.bak` | Safety net, never reset | n/a (manual convenience). | The runner takes a COPY_ONLY checkpoint after a stage when asked and never restores automatically in a full run. |

## 6. Itemized plan

### 6.1 Planned creations, modifications, removals, redesigns, migrations and compatibility effects

**No database migration. No schema change. No permission change. No API contract change. No new NuGet or npm dependency.** Where a runtime code change appears below it is listed with its compatibility effect.

| # | Kind | Item | Group | Compatibility effect / retained behavior |
|---|---|---|---|---|
| 1 | create | `docs/stabilization/DISPOSITION_REGISTER.json`: one row per raw id (F-, D-, Q-5x, Q-065a, O-x, E-, S-, steps), canonical key, disposition, home package, raw ids kept, link to canonical on a duplicate; generated once from R05 Appendices A to C and then maintained by hand | PKG-01 A | New file. The register's header records the SHA-256 of the raw log it was built from. |
| 2 | create | `scripts/stabilization/Test-DispositionCompleteness.mjs` (Node, no dependency): fails if a raw id in a given log has no register row, if a register row names an id that is not in the log, if two rows share a canonical key, or if a duplicate does not link to a canonical row | PKG-01 A | New file; takes the raw-log path as an argument. |
| 3 | create | `tests/stabilization/disposition-completeness.test.mjs`: tests of the check above against synthetic mini-logs (happy, missing, orphan, duplicate-key, withdrawn, reused-key cases) | PKG-01 A | New repository check run with `node --test`. |
| 4 | create | `docs/stabilization/TRACKED_EXCEPTIONS.json` plus a schema and a reader: `{findingId, package, owner, exitCondition, blocking, status}`; seeded with the release-blocking set named in R05 6.6 (F-021, F-037, F-107, F-118, F-103, F-185/F-188/F-221, F-114, F-155/F-014, F-053/F-061/F-071, F-059/F-063/F-065, F-078/F-086, F-084/F-085/F-111) | PKG-01 | New file. Owner values are proposed in section 10 (decision O-3). |
| 5 | create | `scripts/chain/` (PowerShell module and entry script `scripts/Invoke-AccumulatedChain.ps1`): stage-0 runner (design in section 7.2) | PKG-01 | New scripts. `Invoke-RealBackendWalkthrough.ps1`, `DevEnvironment.ps1` and the dev scripts keep working unchanged apart from item 14. |
| 6 | create | `src/alveara-client/playwright.chain.config.ts` and `src/alveara-client/chain/stage-0.spec.ts`: the stage-0 smoke path and the evidence writer (test names carry their source ids) | PKG-01 | New config; added to the `testIgnore` list of `playwright.config.ts` exactly like the existing real-backend specs so the mocked suite is unchanged. |
| 7 | create (conditional on owner approval, section 10 O-1) | A test-only injected clock and communications stubs for the chain environment (design options in section 7.3) | PKG-01 | Production default clock behavior is unchanged. The seam must be unreachable outside the chain environment (PD-32). |
| 8 | create | `docs/design/ALV-N019_DEPENDENCY_ADVISORY_REPORT.md` and the stored audit outputs under the story's evidence directory | PKG-15 A | New report; evidence only. |
| 9 | modify (conditional on the report) | `ImportPatientRows.cs` and `ImportCommitEngine.cs`: null guard, or a documented reason it cannot occur (F-002) | PKG-15 A | Only if the report shows a real null; import behavior for rows with a registration key is unchanged. |
| 10 | modify (conditional on the report) | Merge/unmerge raw SQL: parameterize any piece a user can influence; otherwise add a comment documenting the server-defined boundary (F-003) | PKG-15 A | Behavior unchanged. Merge/unmerge tests must still pass. |
| 11 | modify | `docs/RUNNING_LOCALLY.md`: remove stale counts (F-009), note F-129 as dev-only, DentalKeep title (section 6.2) | PKG-15 B / brand | Docs only. |
| 12 | modify | `scripts/Start-DevApi.ps1`, `scripts/Start-DevClient.ps1` (and `scripts/Initialize-DevDatabase.ps1` lines 79 and 81 if the verify-first step finds the same stack-block problem), with a shared helper in `DevEnvironment.ps1`: one-line refusals, `exit 1`, no stack block (F-011) | PKG-15 B | Same conditions refuse, same texts, exit code 1; a script that succeeds is unchanged. |
| 13 | create | Script exit-code tests for item 12 | PKG-15 B | New tests. |
| 14 | modify (brand) | The public-facing items in section 6.2 | brand | Section 6.2 gives each one with its replacement and its compatibility effect. |
| 15 | create | `docs/stabilization/BRAND_INVENTORY.json` (machine-readable copy of section 6.2 to 6.4) and `tests/brand-inventory.test.mjs`: fails if "Alveara Dental" or a listed public string reappears on a public-facing surface, and fails if a retained identifier is renamed | brand | New repository check. |
| 16 | modify | `tests/story-000-coexistence.test.mjs` line 54 (product shell title expectation: DentalKeep). Line 48 (root Command Center) is unchanged. | brand | Public-facing test expectation, as the program brief requires. |
| 17 | modify | Public-facing expectations in client and server tests that assert a changed string (section 8 lists them) | brand | Expectation text only. |
| 18 | removal | None. No file is deleted. | | |

**Retained behavior (stated so the reviewer can check nothing is silently lost):** every `ALV-*` id and `Alveara_Handoff_*` name; every database, schema, namespace, assembly, project and folder name; the `/api` contract; all stored data; sessions and cookies; the exported wire formats and the ability to import files exported earlier; the ability to restore backups taken earlier; the existing real-backend walkthrough runner.

### 6.2 DentalKeep inventory - class A: public-facing, proposed change to DentalKeep

Counts come from a scan of every tracked text file for the tokens "alveara" and "alvera" (case-insensitive). The scan found 1,101,911 occurrences in 4,731 text files (1,221 binary tracked files, which are zip packages and images, were not scanned and are not rewritten). About 1,085,000 of them are in historical control material (class B); about 17,000 are in live files, of which the large majority are code identifiers (class C). The classes below cover the live files.


| # | Where | Current text | Proposed text | Surface | Compatibility effect / notes |
|---|---|---|---|---|---|
| A-01 | `src/alveara-client/index.html:7` | `<title>Alveara Dental</title>` | `<title>DentalKeep</title>` | browser tab title; product metadata | none |
| A-02 | `src/alveara-client/src/app/AppShell.tsx:73` | brand text beside the logo: `Alveara Dental` | `DentalKeep` | visible on every signed-in screen | none |
| A-03 | `src/alveara-client/src/pages/LoginPage.tsx:81` | "Enter your username and password to access Alveara Dental." | "... to access DentalKeep." | login screen | none |
| A-04 | `src/alveara-client/src/pages/backup/backupMessages.ts:33` and `src/Alveara.Api/Architecture/Backup/BackupCrypto.cs:175,179,189` | "This file is not an Alveara backup." (error code `not_a_backup`) | "This file is not a DentalKeep backup." (code unchanged) | restore screen message | **Coupled string:** `BackupRestoreService.cs:143` tests `Detail.Contains("not an Alveara backup")`. Both change together, and the matcher is changed to test the stable code instead of message text. A test pins the pairing. |
| A-05 | `src/alveara-client/src/pages/exports/ExportCenterPage.tsx:93` | "Take patient records out of Alveara as a file ..." | "... out of DentalKeep ..." | Export patient data page | none |
| A-06 | `src/alveara-client/src/pages/portability/PatientImportPage.tsx:54` and `src/Alveara.Api/Architecture/Patients/Portability/PatientImportValidator.cs:53` | "... a file exported from Alveara ...", "Choose a file exported from Alveara ..." | "... exported from DentalKeep ..." | Import a patient page and rejection message | Files exported earlier are still accepted (the importer never inspects the product name). |
| A-07 | `src/Alveara.Api/Architecture/Patients/Portability/Package/PackageReadme.cs:15,18,50,51` | README inside each export package: "ALVEARA DENTAL - PATIENT DATA PACKAGE", "taken out of Alveara Dental", "Alveara's own layout (format alveara.patient-data-package ...)", "cannot yet be imported back into Alveara" | "DENTALKEEP - PATIENT DATA PACKAGE", "taken out of DentalKeep", "DentalKeep's own layout (format `alveara.patient-data-package` ...)" | text a person reads in an exported ZIP | The format identifier inside the sentence stays `alveara.patient-data-package` (class C). Existing package tests that assert the README text change in the same diff. |
| A-08 | `src/Alveara.Api/Architecture/Backup/BackupSupport.cs:54` | notification body "Alveara backup notification" | "DentalKeep backup notification" | backup notification text | none |
| A-09 | `src/Alveara.Api/Architecture/Patients/Portability/PatientExportFormat.cs:20` (`SystemName`) | value written to `source.system` of every new patient export: "Alveara Dental" | "DentalKeep" | value inside exported JSON | Files exported earlier keep their old value and still import (`PatientImportValidator.cs:93` only checks that `source.system` is text; no code compares its value). `PatientExportServiceTests` asserts the constant, not the literal. |
| A-10 | `src/alveara-client/src/services/patientExportsApi.ts:98,101,104`; `patientPortabilityApi.ts:30`; `PatientExportService`/package service file-name builders (`alveara-patient-<id>.json`, `alveara-patient-package-<id>.zip`, `alveara-all-patients-package-<id>.zip`, `alveara-readable-record-<id>.html`, `alveara-export-` prefixes) | download file names start with `alveara-` | start with `dentalkeep-` | file name the person sees and keeps | **Needs the owner's confirmation (section 10, O-5).** Importers do not match the file name. Tests and walkthroughs that match `alveara-...` names change in the same diff. |
| A-11 | `src/Alveara.Api/Architecture/Backup/BackupService.cs:268` and `src/alveara-client/src/pages/backup/RecoveryKeyPanel.tsx:69` | backup archive name `alveara-backup-<time>-<id>.abk`; recovery key download `alveara-recovery-key-<fp>.asc` | `dentalkeep-backup-...abk`; `dentalkeep-recovery-key-...asc` | file names the operator keeps | **Needs the owner's confirmation (O-5).** Archives are found by the `.abk` extension, not the prefix (`BackupRestoreService.cs:52,85`), so archives with the old prefix keep restoring. Operator documentation in 6.2 A-13 changes with it. |
| A-12 | `docs/RUNNING_LOCALLY.md:1`, `docs/REQUIREMENTS.md:1`, `docs/STORIES.md:1`, `docs/TRACEABILITY.md:1` (titles "Alveara Dental") | document titles | "DentalKeep" | install/operator and project documentation titles | Title lines only. |
| A-13 | `docs/operations/BACKUP_AND_RECOVERY.md:3,8,38,39,40,175`, `docs/operations/DATABASE_OPTIONS.md:3`, `docs/PATIENT_PORTABILITY.md:5,18,26,108-110`, `docs/DESKTOP_FRONTEND_TRANSITION_DECISION.md:8`, `ops/deployment-verification/README.md` and the prose of the two `Invoke-*Verification.ps1` headers ("Alveara shell/API") | product name in prose and example file names | DentalKeep in prose and example file names | release/install/operator documentation | Identifiers inside the same sentences (`AlveraRestore_<id>`, `ConnectionStrings__Alveara`, `src/Alveara.Api`, `alveara.bak`, format ids, `svc-alveara-api`, `alveara_app_login`) stay (class C). The example JSON in `PATIENT_PORTABILITY.md:26` changes with A-09. |

New or changed screenshots: the repository holds no product screenshots outside historical gate evidence (class B). If a later story captures new ones they use DentalKeep.

### 6.3 DentalKeep inventory - class B: historical/control evidence retained as Alveara (not changed)

| # | Where | Occurrences (files) | Why unchanged |
|---|---|---|---|
| B-01 | `.alveara/handoffs/**` | 1,020,226 (1,345) | Immutable handoffs; the program brief forbids rewriting them. |
| B-02 | `.alveara/reviews/**` | 52,769 (136) | Immutable independent reviews. |
| B-03 | `.alveara/gates/**`, `.alveara/*.md/json` (HANDOFF_SCHEMA, QUALITY_GATES, BUILD_STATE, EXECUTION_STATUS, story_catalog), `.alveara/dist/**` | 7,377 (133) + 513 (5) | Control state, gate evidence and zips; the directory name `.alveara` is itself an identifier. |
| B-04 | `GPT Docs/**` (execution plans, archived plans) | 2,360 (8) | Authoritative control documents; changed only by reviewed control updates. |
| B-05 | `PROGRESS.md` | 323 (1) | Historical log. |
| B-06 | `.colaberry/**` | 810 (21) | Course-platform data files written by the platform. |
| B-07 | `docs/design/**` (including the R01-R05 plans and reviews) | 57 (20) | Immutable reviews and design records. |
| B-08 | `docs/testing/**` negative-control and parent-regression records | 525 (34) | Test evidence; the occurrences are almost entirely identifiers (class C). |
| B-09 | `GPT Docs/Archived/**`, `docs/MILESTONES_2026-10-07.md`, `docs/stories/STORY-000.md` | included above or 1 each | Course/project history. `STORY-000.md:74` names the course project. |
| B-10 | Root `index.html`, `assets/*.js` and the `command-center.spec.ts:73` expectation | 2 + 18 + 1 | The root page is the **course Command Center**, which the platform serves; `tests/story-000-coexistence.test.mjs:48` pins that it stays "Alveara Dental - Command Center". **Owner question O-4.** |
| B-11 | `CLAUDE.md` (project instructions, DRI Ali) and `README.md` line 3 ("AI course project for Alveara Dental"), GitHub repo name `baidhani/Alveara-Dental` | 2 + 2 | Course/governance files owned by the DRI; the repository name is an external identifier. **Owner question O-4.** |
| B-12 | Gate tooling READMEs and specs under `src/alveara-client/gate-a..gate-d` | counted in the `src/alveara-client` total | Evidence tooling for closed gates. |
| B-13 | Test fixture data: the practice name "Alveara Dental" entered as sample data in `BillingRefundSchemaTests.cs:62`, `ConfigurationApiTests.cs:209`, `PracticeConfigurationServiceTests.cs:52-65`, `PrescriptionWorkflowPanel.test.tsx:45,480` | 7 lines | A practice's own name is user data, not the product name; changing it would not change what the product shows. |
| B-14 | Code comments and doc comments that mention Alveara | many | Not public-facing; touched only where the same line already changes. |

### 6.4 DentalKeep inventory - class C: compatibility-sensitive identifiers retained pending a separately reviewed migration

| # | Identifier (examples) | Where | Why retained |
|---|---|---|---|
| C-01 | `Alveara.Api` namespaces, `Alveara.Api.Tests`, `AlveraDbContext`, `AlveraTestEnvironment`, assembly names | `src/Alveara.Api/**`, `src/Alveara.Api.Tests/**` (14,885 + 1,738 occurrences, 544 + 343 files) | Namespaces, types and assemblies; a rename is a code-wide refactor with no user benefit and breaks every handoff reference. |
| C-02 | Solution, project and folder names `src/Alveara.Api`, `src/Alveara.slnx`, `src/alveara-client`, package name `alveara-client` | repository layout | Paths used by scripts, tests, CI-like commands and `Alveara_Handoff_*` evidence. |
| C-03 | Database names `AlveraDev`, `AlveraRestore_<id>`, `AlveraWalk_<name>_<id>`, `AlveraTest_<id>`, `AlveraTemplate_<id>`; the chain database will use `AlveraChain_<runid>` for consistency | scripts, tests, docs | Persisted names and test-isolation safety checks (e.g. "never touches AlveraDev"). |
| C-04 | Environment variables `ALVERA_SQL_SERVER`, `ALVERA_DATA_ROOT`, `ALVERA_PERF_EVIDENCE`, `ALVERA_AB_OUT`, `ALVERA_IMPORT_ROWS`, `ALVERA_TEST_TEMPLATE*`; data root `C:\.alvera-data` | scripts, tests, docs | Operator and machine configuration. |
| C-05 | Connection-string key `Alveara` (`ConnectionStrings__Alveara`) | `Program.cs:46`, scripts, docs | Deployed configuration; renaming breaks every existing install. |
| C-06 | CSRF cookie name `Alveara-CSRF` | `Program.cs:270` | Changing it during an upgrade invalidates in-flight sessions; no user benefit. |
| C-07 | Browser storage key `alveara-theme`; DOM events `alveara:patient-requirements-changed`, `alveara:safety-changed` | `AppShell.tsx:13`, `PatientRequirementsContext.tsx:9`, `safetyApi.ts:159`, many e2e specs | Persisted keys; renaming silently resets every user's saved theme. |
| C-08 | Wire-format ids `alveara.patient-export`, `alveara.patient-data-package`, MIME `application/vnd.alveara.signed-form+json`, archive component path `database/alveara.bak` | exports, signed-form descriptor, backup archive | Persisted/interchange formats; existing files must keep reading. |
| C-09 | SQL session/application-lock keys `alveara.patient_merge`, `alveara.admin-availability`; validation/policy keys `alveara.policy-version`, `alveara.externalcase.`, `alveara.followup.` | database triggers, services | Referenced by existing triggers and migrations. |
| C-10 | Data-protection purposes `Alveara.MfaSecret.v1`, `Alveara.MfaChallenge.v1`; backup recovery key user id `Alveara backup recovery key` | `AccountService.cs:59,61`, `BackupCrypto.cs:52` | Changing a purpose string makes stored MFA secrets undecryptable; the recovery key id is embedded in keys already issued. |
| C-11 | Embedded resource name `Alveara.Api.PermissionRegistry.json`; `Alveara.Api.http` host variable | `Alveara.Api.csproj:10`, `.http` file | Code-level resource lookup. |
| C-12 | Operator-script object names: firewall rule `Alveara-TempBlockInternet`, scheduled task `Alveara-UndoInternetBlock`, `svc-alveara-api`, `alveara_app_login`, host `alveara-server.local`, `AlveraLanServerDemo` | `ops/deployment-verification/*.ps1` | Created and removed by name within a run; documented verification procedure. |
| C-13 | Test temporary-name prefixes `alveara-import-test-`, `alveara-backup-test-`, `alveara-blob-test-`, `AlveraTemplate_`, `AlveraMissing_Test_`, `alveara_test_app_` and similar | `src/Alveara.Api.Tests/**` | Cleanup code matches these prefixes to delete leftovers safely. |

### 6.5 Repository check that keeps this inventory true

`tests/brand-inventory.test.mjs` reads `docs/stabilization/BRAND_INVENTORY.json` and the live tree and fails if: (1) the string "Alveara Dental" or a class A "current text" appears in a file the inventory lists as public-facing; (2) a class C identifier is changed or removed (the list is pinned); (3) a new occurrence of "Alveara" appears in a public-facing surface without an inventory row. Historical directories (class B) are excluded by path. A reviewer can see exactly which rows the check enforces.

## 7. Design of each part

### 7.1 Disposition register and completeness check (PKG-01 Group A)

- **Canonical keys.** The log reuses keys (D-01 and D-02 appear twice; F-123 and F-206 were never assigned; a silent 60-minute default is F-044b). The register keeps raw ids exactly and adds a canonical key (D-01a, D-01b, D-02a, D-02b, F-044b) as R05 Appendix A did. Duplicates link to one canonical item; withdrawn (F-199, F-201) and not-reproduced (F-006, F-015) items stay.
- **What the check reads.** The raw log (path given on the command line, or the `DISPOSITION_RAW_LOG` environment variable) and the register. It extracts raw ids with explicit patterns (`F-\d{3}[a-z]?`, `D-\d{2}[a-z]?`, `Q-\d{1,3}[a-z]{0,2}` including the `Q-5x` family, `Q-065a`, `O-[A-Za-z0-9]+`, `E-\d{2}`) and fails with a list of unmatched ids. An id only quoted inside another id's text still needs a row because the log is the authority.
- **Provenance.** The register header carries the SHA-256 of the raw log it was built from (`715b1f84...`). The check prints the actual log hash and states whether it matches, so a register built for an older log is visible.
- **Failure behavior.** Exit code 1 with one line per problem; exit code 2 for an unreadable input (so a missing log is not mistaken for a pass). No silent skips.
- **Idempotency.** Pure function of its two inputs; no writes.
- **R05 text that the register preserves:** the 190-row segment mapping (6.4) stays a reviewed document; the register only carries ids.

### 7.2 Chain runner and environment (stage 0)

Builds on, and reuses the safety rules of, `scripts/Invoke-RealBackendWalkthrough.ps1`. It does not change that script.

**What the runner owns for one run** (R05 PKG-01): database, document storage, key ring, backup folder, configuration, injected test clock, communication stubs, evidence directory, run id, schema version, implementation SHA. All live under `<root>\Test-Data\Chain\<runid>\`; the database is `AlveraChain_<runid>` with explicit files in `<root>\Test-Data\Database`.

**Safety boundaries (all enforced before anything is created, and re-checked before anything is dropped):**
1. Refuses to start when the database name is not `AlveraChain_` + a 32-hex run id, when the target database already exists, when any configured path is outside `<root>\Test-Data`, or when the connection string, the environment or the arguments name `AlveraDev`, `Dev-Data` or any database that is not the one it just created. A self-test proves each refusal.
2. Drop only the database whose name it created, after confirming the name matches the pattern; never a wildcard drop; never `AlveraDev`. Same rule for folders: delete only the run folder it created, verified to be under the chain root.
3. Fixed API port conflict is detected up front (the walkthrough uses 5072; the chain uses its own configured loopback port and refuses when it is busy).
4. No real outbound communication is possible from the chain API process: the only channel found is the file-drop backup notifier writing under the run folder; any other channel found at implementation is replaced by a stub in the test-only composition (section 7.3).
5. The run never imports data from `Dev-Data` and never reads real backups. Test data is synthetic.
6. Stamps `runId`, `implementationSha` (git HEAD, with a flag when the working tree is dirty; a dirty run is labelled and cannot be used as exit evidence), `schemaVersion` (latest applied migration id), the log SHA, and the clock mode into every evidence record.
7. Always cleans up on failure or interrupt (try/finally, like the walkthrough), and leaves the evidence directory.
8. A **resumed** run is labelled `RESUMED` in every record and is never exit evidence. This story builds the label and the refusal to certify; it does not build resume itself beyond a flag.
9. Checkpoints (`COPY_ONLY` `.bak`) are safety nets taken after a stage on request; the runner never restores one during a full run (E10).

**Evidence record** (one JSON line per test, written by the Playwright reporter and by the runner for non-browser steps): `{runId, stage, stepId, outcome, evidencePath, implementationSha, schemaVersion, resumed, clockMode, startedAt, durationMs}`. `outcome` is one of `PASS`, `FAIL`, `TRACKED_EXCEPTION`, `SKIPPED_BLOCKED`. Test names carry the manual source id (R05 section 6 rule). `test.fail()` is not used.

**Tracked exceptions in a run.** The runner loads `TRACKED_EXCEPTIONS.json`. A step tagged with a finding id that has a blocking entry is reported `TRACKED_EXCEPTION`, the run exits non-zero, and the final report lists the entries. ALV-N028 requires the register to hold no blocking entry (R05 PKG-16). A tracked exception with no owner or no exit condition is itself a hard error.

**Stage-0 smoke path** (what proves the foundation; built in this story):
1. create the throw-away environment; 2. apply migrations (`dotnet ef database update`, as the walkthrough does); 3. start the real API with the environment; 4. health check; 5. bootstrap the administrator, run it again and assert the second run reports "already exists" and changes nothing (S-09); 6. sign in as the administrator (browser step through the stage-0 spec); 7. take one `COPY_ONLY` checkpoint (E10); 8. assert the refusal tests for S-01 to S-08 (busy ports, missing database, client without API) as exit-code tests; 9. stop everything and drop the database; 10. write the evidence file and a summary. No later stage is built here.

**Failure-first answers (CLAUDE.md):** (1) *If a step fails* the run records `FAIL`, stops the stage, still cleans up and exits 1. (2) *Retries:* none inside a stage; the runner may retry only the API health wait (40 polls, 3 s timeout each, as the walkthrough) and the database drop (5 attempts, 2 s apart). (3) *Recovery when retries are exhausted:* the leftover database name and folder are printed so an operator can remove them with a documented one-line command. (4) *Handled failure modes:* busy port, existing/foreign database, path outside root, API start failure, health timeout, migration failure, dirty tree, interrupted run. *Not handled:* SQL Server unavailable (fails fast with a clear message), disk full, machine restart mid-run.

**Existing specs affected (R05):** adds a tier beside the isolated walkthroughs; none of the ~40 `*-real-backend.spec.ts` specs is changed. `playwright.config.ts` gets one more `testIgnore` entry.

### 7.3 Injected clock and communication stubs (needs owner approval first)

R05 PKG-01 states: "Shared test infrastructure and any test-only helper need the owner's approval first." The runner above needs no production change. The injected clock and the stubs do, because `Program.cs:53` registers `PracticeClock` directly. The only outbound notification channel found in `Program.cs` is `FileDropBackupNotifier` (line 248, writes a file); whether any other channel exists is checked at implementation, and any found is stubbed. Options, with a recommendation (the choice is the owner's, decision O-1):

| Option | How | Risk |
|---|---|---|
| **1 (recommended)** | A separate test-only composition project or file (`Alveara.Api.ChainHost`) that wraps the production host and replaces `IPracticeClock` and the notifier interfaces after normal registration. The production `Program.cs` is not edited. | New project and build output; production code path untouched; the chain host cannot be started by the production service. |
| 2 | A small seam in `Program.cs`: when `ASPNETCORE_ENVIRONMENT=Chain` **and** a signed chain token file exists, register `ChainClock`/stubs. | A production file contains a test seam; must be proved unreachable in Production by a test. |
| 3 | No injected clock: advance time by editing database rows as the manual pass did. | Reproduces the manual shortcut that R05 replaced; rejected by R05 (cases 3.09, 1.12). |

Binding rules from PD-32 that every option must meet: the clock is test-only and explicit (never a production setting), never controls authentication expiry or retry, and its mode is written into every evidence record. A test proves the production host registers the real clock.

### 7.4 Dependency advisories and developer hygiene (PKG-15)

**Advisory process** (read-only first, nothing upgraded):
1. Capture `dotnet list src/Alveara.Api/Alveara.Api.csproj package --vulnerable --include-transitive` and `dotnet list ... package --include-transitive` (dependency graph), and `npm audit --json` plus `npm ls` in `src/alveara-client`. Store the raw outputs in the evidence directory with the date. These commands query public advisory services; they send package names and versions only, and are the only external calls in this story.
2. For each advisory record: id, package, installed version, fixed version, severity, path (direct or via which parent), shipped or dev-only, reachability (is the vulnerable API called), and a disposition: **not reachable**, **dev-only**, **fixed** (after PD-26) or **accepted risk** (owner).
3. Any upgrade is a deliberate, separately reviewed add (CLAUDE.md "New dependencies require a deliberate add"; the plan gates it on PD-26). `npm audit fix` is never run.
4. F-002 and F-003: read the code, record the disposition in the report, and change code only where the report shows a defect (rows 9 and 10 of 6.1), each with a regression test.
5. Review cadence: the report states the date and how to rerun the same commands, so the next review is comparable.

**Hygiene:** F-009 and F-011 as in sections 5 and 6.1. F-004 and F-006 produce records only. The "stale lines in the manual guides" (R05 7.D.2) live in `00-STEP-BY-STEP.md` and `01-test-cases.md`, which are **not in the repository**; they are corrected in the next reviewer package, not in this story. This story corrects the in-repo `RUNNING_LOCALLY.md` only.

### 7.5 DentalKeep migration approach

Order: (1) approval of the inventory in section 6; (2) apply class A changes, one commit per surface group (client, server messages, export package, docs); (3) add `BRAND_INVENTORY.json` and the check; (4) fix expectations. Search-and-replace is not used: each change is a reviewed edit from the table. The coupled string in A-04 is changed together with its matcher. New export/backup/key file prefixes (A-10, A-11) wait on O-5.

## 8. Affected permissions, audit, data, integrations and release impacts

| Area | Impact |
|---|---|
| Permissions / authorization | None. No role, permission, route or policy changes. The runner uses the existing administrator bootstrap with a run-scoped secret. |
| Audit / history | None. No audit event, history row or clinical record is created or altered. Stage 0 creates only throw-away synthetic data in its own database. |
| Data / migration | None. No schema change; no migration. The runner applies the existing migrations to its own database. |
| Integrations | No new integration. Dependency-advisory commands query public advisory services (package names and versions only). Chain communications are stubbed (section 7.3). |
| Accessibility | A-02/A-03 change text only; layout and semantics unchanged. Accessibility tests that assert the old text change with it. |
| Recovery | Backup and restore keep working with archives made earlier (found by `.abk` extension; component path `database/alveara.bak` unchanged; recovery key id unchanged). |
| Performance | None expected. The runner adds a separate test tier; no runtime hot path changes. |
| Security | No secret introduced. The chain uses a run-scoped bootstrap secret never written to the evidence. No dependency version changes. |
| Release | None. ALV-N019 does not authorize deployment. Release engineering remains paused. |

## 9. Changed-file inventory (from the actual repository)

**Expected changes** (final list confirmed at implementation; counts from this scan):
- Create: `docs/stabilization/{DISPOSITION_REGISTER.json,TRACKED_EXCEPTIONS.json,BRAND_INVENTORY.json}`, `scripts/stabilization/Test-DispositionCompleteness.mjs`, `scripts/Invoke-AccumulatedChain.ps1`, `scripts/chain/*`, `src/alveara-client/playwright.chain.config.ts`, `src/alveara-client/chain/stage-0.spec.ts`, `tests/stabilization/disposition-completeness.test.mjs`, `tests/brand-inventory.test.mjs`, `docs/design/ALV-N019_DEPENDENCY_ADVISORY_REPORT.md`, script exit-code tests, and (conditional on O-1) the clock/stub composition.
- Modify (client): `index.html`, `src/app/AppShell.tsx`, `src/pages/LoginPage.tsx`, `src/pages/backup/{backupMessages.ts,RecoveryKeyPanel.tsx}`, `src/pages/exports/ExportCenterPage.tsx`, `src/pages/portability/PatientImportPage.tsx`, `src/services/{patientExportsApi.ts,patientPortabilityApi.ts}`, and the tests that assert those strings (command-center is **not** among them).
- Modify (server): `BackupCrypto.cs`, `BackupRestoreService.cs` (matcher), `BackupSupport.cs`, `BackupService.cs` (conditional on O-5), `PackageReadme.cs`, `PatientExportFormat.cs`, `PatientImportValidator.cs`, the file-name builders for exports, plus tests that assert those strings. Conditionally (report-driven): `ImportPatientRows.cs`, `ImportCommitEngine.cs`, the merge/unmerge services.
- Modify (docs/scripts): `docs/RUNNING_LOCALLY.md`, `docs/{REQUIREMENTS,STORIES,TRACEABILITY}.md` titles, `docs/operations/{BACKUP_AND_RECOVERY,DATABASE_OPTIONS}.md`, `docs/PATIENT_PORTABILITY.md`, `docs/DESKTOP_FRONTEND_TRANSITION_DECISION.md`, `ops/deployment-verification/README.md` and header prose of its two scripts, `scripts/{Start-DevApi,Start-DevClient,Initialize-DevDatabase,DevEnvironment}.ps1`, `src/alveara-client/playwright.config.ts` (one `testIgnore` entry), `tests/story-000-coexistence.test.mjs` (line 54).
- Control files updated **only after independent approval of the work**: `.alveara/EXECUTION_STATUS.json`, `PROGRESS.md`, handoff package.

**Inspected and intentionally untouched:** all of `.alveara/**` other than the later control update; `GPT Docs/**`; `.colaberry/**`; root `index.html`, `assets/**`, `CLAUDE.md`, `README.md`; every migration and model; `Program.cs`; every class C identifier; the ~40 `*-real-backend.spec.ts` specs (except where one asserts a changed string, listed at implementation); `scripts/Invoke-RealBackendWalkthrough.ps1`; the `docs/design/**` records; the R05 package; `src/alveara-client/public/*.svg` (they contain no product name text).

## 10. Open decisions and owner input needed before implementation

| Id | Decision | Who | Recommendation (not a decision) |
|---|---|---|---|
| O-1 | Approve the test-only injected clock and communication stubs (R05 PKG-01 governance; PD-32 rules), and choose the form (7.3). | owner | Option 1: a separate chain host, production `Program.cs` untouched. |
| O-2 | Approve adding shared test infrastructure (the chain runner, the Playwright chain config, the disposition and brand repository checks) as listed in section 9. | owner | Approve. |
| O-3 | Name the owner of each seeded tracked-exception entry (the register requires an owner and an exit condition). | owner | Owner of the home package for each entry, with the exit condition "the home package's acceptance criteria pass in the chain". |
| O-4 | Course/governance surfaces: leave the root Command Center, `CLAUDE.md`, `README.md` line 3 and the GitHub repository name as Alveara (class B), or schedule a separate change with the DRI for any of them. | owner / DRI (Ali) | Leave as Alveara in this story; the course platform and repository naming are not ours to change here. |
| O-5 | New file-name prefixes: `dentalkeep-` for exports, backups and the recovery key (A-10, A-11), or keep `alveara-` (class C). | owner | Change to `dentalkeep-` for new files; old archives keep working by extension. |
| O-6 | Whether `PatientExportFormat.SystemName` may change to "DentalKeep" (A-09) given that files exported earlier keep the old value. | owner | Yes. |
| O-7 | PD-26: upgrade approval for vulnerable packages. **Not needed for this story**; recorded so no upgrade happens without it. | owner | Decide after the advisory report. |
| O-8 | Whether an `accepted risk` disposition for a dev-only advisory needs the owner's sign-off or only the reviewer's. | owner / reviewer | Reviewer approves the report; the owner signs only an accepted shipped risk. |

R05 decisions that this story does **not** depend on: PD-01, PD-15, PD-17, PD-18, PD-25, PD-36, PD-37 and the rest of the register. None is silently selected here.

## 11. Acceptance criteria and exact expected assertions

Source-to-acceptance table. Test types: **U** unit/repository check, **S** script exit-code test, **B** real-backend browser (stage-0 spec), **R** read-only report. "Evidence" is where the result will be stored in the story's `.alveara/handoffs/ALV-N019/<attempt>-evidence/` folder.

| Raw item | Acceptance criterion (from item 56 / R05) | Type | Exact expected assertion | Evidence |
|---|---|---|---|---|
| F-201, all raw ids | Disposition completeness is mechanically checked; withdrawn kept | U | With the real log: exit 0, "all N raw ids have a disposition". With a log containing an id absent from the register: exit 1 naming the id. A withdrawn id (F-201) and a not-reproduced id (F-006) are accepted. Two rows with one canonical key: exit 1. Unreadable log: exit 2. | `disposition-completeness.txt` |
| R05 6.6 | Tracked exceptions have an owner and an exit condition | U | Register load fails for an entry with no owner or no exit condition; a blocking entry makes the runner exit non-zero and be reported `TRACKED_EXCEPTION`; a non-blocking entry does not. | `tracked-exceptions.txt` |
| S-01 to S-08 (step 83) | The runner creates and drops its own database; scripts refuse with one line | S | Each refusal script run in a controlled setup exits with code 1 and prints exactly one message line and no `At ...:line` stack block (busy 5072, busy 5173, missing database). The runner creates `AlveraChain_<runid>` and the database is absent afterwards. | `startup-refusals.txt` |
| F-011 | Script refusals print the message without a stack block and exit non-zero | S | As above, asserted by exit code and by absence of `CategoryInfo`/`FullyQualifiedErrorId` in the output. | same |
| S-09 (step 109) | Admin creation idempotent; first sign-in on a fresh database | B | First `New-DevAdmin`-equivalent bootstrap creates one administrator; the second reports "already exists" and the user count stays 1; sign-in through the browser returns the dashboard. | `stage0-smoke.json` |
| E10 (step 140) | Checkpoint is a safety net | U + B | A `COPY_ONLY` backup file exists after the checkpoint call and the live database is unchanged; no restore is invoked in a full run (asserted by the runner log). | `stage0-smoke.json` |
| PKG-01 runner | Owns its environment and refuses development data | U + S | Runner self-test: refuses the names `AlveraDev`, `AlveraWalk_x`, a name with no run id, an existing database, and a path outside `Test-Data`; stamps `implementationSha`, `schemaVersion`, `runId`, `clockMode` in the evidence; labels a dirty tree and a resumed run; always cleans up (kill the API mid-run, assert the database and folder are gone). | `runner-selftest.txt` |
| PD-32 | The chain clock is test-only | U | The production host (without the chain composition) registers the real `PracticeClock`; the chain host's clock mode appears in every record. (Conditional on O-1.) | `clock-seam.txt` |
| F-001, F-005 | Dependency advisories investigated before any upgrade | R | Report lists every advisory from the stored `dotnet list --vulnerable` and `npm audit --json` outputs with id, package, versions, path, shipped/dev-only, reachability and disposition; `git diff` shows no change to any `.csproj` or `package.json`/lockfile. | `ALV-N019_DEPENDENCY_ADVISORY_REPORT.md` |
| F-002 | Nullable cases guarded or documented, with tests | U | Rows with empty and malformed fields run through the import dry-run and commit with no exception, no wrong match, and the build prints no CS8604/CS8714/CS8621 at those lines. | `import-null-tests.txt` |
| F-003 | Raw SQL parameterized or the boundary documented; no user-controlled interpolation | U | Merge and unmerge with injection-like values in every user-controlled input (ids, reasons) change nothing unexpected and raise the normal validation error; a code comment and the report name each interpolated piece as server-defined. | `merge-injection-tests.txt` |
| F-009 | Docs no longer state stale counts | U | `docs/RUNNING_LOCALLY.md` step 4 contains no digit-count of tables or migrations; a repository check greps for the old phrase. | `docs-check.txt` |
| F-004, F-006, F-129 | Recorded, not applied; F-006 closed as not reproduced | R | Report states the required node/npm versions, the F-006 evidence (raw line 93) and the F-129 dev-only explanation; no tooling or runtime change. | report |
| Brand inventory | DentalKeep replaces Alveara Dental in approved public-facing surfaces; no historical or compatibility-sensitive identifier rewritten | U + B | `tests/brand-inventory.test.mjs` passes: no class A "current text" remains on a listed surface; every class C identifier is unchanged; class B paths are untouched (`git diff` shows no change under `.alveara/handoffs`, `.alveara/reviews`, `GPT Docs`, `PROGRESS.md`, `.colaberry`). Browser step: the login page and the signed-in header show "DentalKeep" and the document title is "DentalKeep". | `brand-check.txt`, screenshots in evidence |
| A-04 | Coupled message and matcher | U | A backup that is not an archive returns code `not_a_backup` with the DentalKeep message, and the restore check `archive_is_a_backup` reports failed for it (matched by code, not text). | `backup-message-test.txt` |
| A-09, A-10 | Export value and file names | U | A new export contains `source.system == "DentalKeep"`; an export file with the old value still imports; a new file name starts with the approved prefix (if O-5 approves). | `export-name-tests.txt` |
| Failure paths | At least one failure path per feature (CLAUDE.md DoD) | U/S | Each row above includes its failure assertion; idempotency: running the runner twice, the register check twice and the brand check twice gives the same result with no leftover state. | all |

## 12. Test plan

### 12.1 Focused Stage 1 (run only after this briefing is approved and the work exists)
- `node --test tests/stabilization/disposition-completeness.test.mjs tests/brand-inventory.test.mjs tests/story-000-coexistence.test.mjs tests/status-labels.test.mjs tests/gate-tooling-coexistence.test.mjs tests/no-direct-db-access.test.mjs` (the existing repository checks stay green).
- The script exit-code tests and the runner self-test (PowerShell, no network).
- `dotnet test src/Alveara.Api.Tests --filter` for: the import null tests, merge/unmerge injection tests, the backup message pairing (A-04), the export value and name tests (A-09/A-10), and every test file touched for a changed string.
- `npx vitest run` for the client test files that assert a changed string.
- The stage-0 smoke run through `scripts/Invoke-AccumulatedChain.ps1` on a fresh throw-away database (one run).
- The read-only advisory commands, with outputs stored.

### 12.2 Broad regression and the frozen Stage 2 contract
This story is **test infrastructure and identity work** and touches server messages and shared scripts, so item 56 requires the binding two-stage review. The frozen Stage 2 contract is:
1. `dotnet test src/Alveara.Api.Tests` (full backend), `npx vitest run`, `npm run build`, `npm run lint` and `npx tsc -b` in `src/alveara-client`, `node --test tests/*.test.mjs`, and the mocked-browser suite `npx playwright test`.
2. The real-backend walkthroughs whose assertions mention a changed string or whose area is touched (backup and recovery in `auth-real-backend`, `patient-portability-real-backend`, `patient-export-package-real-backend`, `patient-merge-real-backend`, `bulk-import-real-backend`), each through `scripts/Invoke-RealBackendWalkthrough.ps1`, plus the stage-0 chain run.
3. Applied on the unchanged approved tree with no code edit between approval and run. A product or test failure in Stage 2 means a new attempt or a responsible-work correction; an environment-only inconclusive run is `BLOCKED` and may be rerun unchanged.

**Nothing in section 12 is run before this briefing is approved.** Test durations are not estimated here; the measured durations are in the project notes and will be applied when the plan is submitted.

### 12.3 Break and harden notes (CLAUDE.md Build-Break-Harden)
The BREAK list the implementation will exercise: the runner interrupted mid-run (leftover cleanup), a busy port, an existing database with the target name, a path outside the root, a dirty working tree, a missing or truncated raw log, a register with a duplicate canonical key, a tracked exception with no owner, a message-text change that breaks the coupled matcher (A-04), and an old export file or old `.abk` archive meeting the new code. Each has a protecting test listed in section 11.

## 13. Risks and mitigations

| Risk | Mitigation |
|---|---|
| A search-and-replace would silently rename a persisted key or a database name | No bulk rename; the inventory is explicit; the repository check pins class C. |
| The coupled message/matcher (A-04) breaks restore error handling | Matcher changed to the stable code; a test pins the pairing. |
| The chain accidentally touches development data | Name/path pattern refusals and a self-test of each refusal; no `Dev-Data` path is ever accepted. |
| Test-only seams leak into production | Option 1 keeps `Program.cs` untouched; a test proves the production host registers the real clock. |
| Advisory work turns into a drive-by upgrade | Read-only first; `git diff` assertion on `.csproj`/`package.json`; PD-26 required. |
| Old exports, archives or sessions stop working | Class C identifiers and the `.abk` extension rule are unchanged; tests with old-format fixtures. |
| Scope growth into stages 1-14 | Section 2 states the boundary; stage 0 smoke path only. |

## 14. What this briefing asks the reviewer to decide

1. Is the brand inventory (6.2 to 6.4) complete and correctly classified? In particular A-09, A-10, A-11 (values and file names), B-10/B-11 (course surfaces) and C-06/C-07 (cookie and storage keys).
2. Is the stage-0 design (7.2) a sufficient foundation, and is the safety-boundary list (7.2 items 1-9) complete?
3. Which clock/stub form (7.3) may be proposed to the owner, and is the owner approval path (O-1, O-2) correct?
4. Are the acceptance assertions in section 11 specific enough, and is the Stage 1 and frozen Stage 2 plan (section 12) acceptable?
5. After approval, implementation starts; nothing before.

## 15. Method and limits of this briefing

- The inventory counts come from a script over all 5,952 tracked files (4,731 text files scanned, 1,221 binary skipped); the live-surface lines in 6.2 to 6.4 were read individually. The class B and class C rows summarize by area and by token family; the exact per-line list is regenerated by the same script at implementation and stored in `BRAND_INVENTORY.json`.
- The root causes in section 5 are hypotheses from reading the code; no build, test or audit was run for this briefing.
- I did not run `dotnet list package --vulnerable` or `npm audit`; they query external services and belong to the approved investigation step.
- The raw-log entries in section 4 were copied by script from the zip copy of the log; the log itself is not committed to the repository.
- Not claimed: that the 190-row segment mapping or the Appendix A to C dispositions are final; they are R05 and are carried unchanged.
