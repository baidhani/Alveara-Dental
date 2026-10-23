"""Builds REPORT.md from outputs/*.json and the preserved run records (read-only)."""
import collections, json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import backtest as b

OUT = os.path.join(HERE, 'outputs')
summary = json.load(open(os.path.join(OUT, 'summary.json'), encoding='utf-8'))
relaxed = {r['run']: r for r in json.load(open(os.path.join(OUT, 'relaxed_diagnostic.json'), encoding='utf-8'))}
prof = json.load(open(os.path.join(OUT, 'applicability_profile.json'), encoding='utf-8'))
evals = {}
for s in summary:
    evals[s['run']] = json.load(open(os.path.join(OUT, 'eval_%s.json' % s['run'].replace(' ', '_').replace('(', '').replace(')', '')), encoding='utf-8')) if os.path.exists(os.path.join(OUT, 'eval_%s.json' % s['run'].replace(' ', '_').replace('(', '').replace(')', ''))) else None

# ---- run table
run_rows = []
for r in b.RUNS:
    rows = b.load_result(r['result'])
    failed = sum(1 for x in rows if x[1] == 'Failed')
    cd = b.git('log', '-1', '--format=%ad', '--date=format:%Y-%m-%d %H:%M', r['commit']).strip()
    run_rows.append('| %s | `%s` | %s | %d | %d | %s | %s | `%s` |' % (r['id'], r['commit'], cd, len(rows), failed, ('%.1f min' % r['wall_min']) if r['wall_min'] else 'not recorded', 'qualifying (all passed)' if failed == 0 else 'NOT qualifying (%d failed)' % failed, r['result'].replace('.alveara/handoffs/', '')))

# ---- per change table
chg = []
for s in summary:
    e = json.load(open(os.path.join(OUT, [f for f in os.listdir(OUT) if f.startswith('eval_') and json.load(open(os.path.join(OUT, f), encoding='utf-8'))['run'] == s['run']][0]), encoding='utf-8'))
    cats = collections.Counter(w.split(' (')[0] for _, w in e['forced_full_reasons'])
    top = '; '.join('%s x%d' % (k, v) for k, v in cats.most_common(3))
    rl = relaxed.get(s['run'], {})
    if 'selected_share_of_tests' in rl:
        rtxt = '%d%% of tests / %d%% of test time (%d classes)' % (round(rl['selected_share_of_tests'] * 100), round(rl['selected_share_of_test_time'] * 100), rl['selected_classes'])
    else:
        rtxt = 'full (unreached: %s)' % ', '.join(sorted({u.split('/')[-1] for u in rl.get('unreached', [])})[:2]) if rl.get('unreached') else 'full (mapping incomplete)'
    known = ('%d failed' % s['failed']) if s['failed'] else 'none'
    chg.append('| %s vs %s | %d | **%s** | %s | %s | %s |' % (s['run'], s['baseline'], s['changed_files'], s['decision'], top, rtxt, known))

prof_c = collections.Counter(x['decision'] for x in prof)
reasons = collections.Counter(w for x in prof if x['decision'] == 'FORCED_FULL' for w in x['reasons'])
sel_commits = [x for x in prof if x['decision'] == 'SELECT']

REPORT = f'''
# Change-impact test selection - historical backtest report

**Status:** `AWAITING_REVIEW` (read-only analysis; nothing in force). **Authority:** `docs/design/CHANGE_IMPACT_TEST_SELECTION_REVIEW_RESPONSE.md`.
**Recommendation (Section 8): `NOT_READY_FOR_SHADOW_PILOT`.** The strict, fail-closed rule would have forced a full run in **9 of 9** evaluated historical changes and in **{prof_c['FORCED_FULL']} of {len(prof)}** backend-touching commits, so "zero misses" is vacuously true and says nothing about safety; a graph-only diagnostic also produced one would-be miss that exposes a gap in the miss definition.

Tooling: `backtest.py`, `csharp_model.py`, `make_report.py` in this folder; raw per-run outputs in `outputs/`. Reproduce with `python backtest.py && python make_report.py` (git history only; no checkout, build or test run).

## 1. Historical commits and full-run artifacts examined

Every artifact is a preserved per-test result of a complete serial backend run. The result file is the **discovery inventory of that run** (it lists every test the run executed); the commit is the tree the run executed on (a single implementation commit whose run finished minutes before the commit time, or the stated closure commit; in each handoff the run is stated to be on that tree).

| Run | Commit | Commit date | Tests | Failed | Wall time | Qualifying? | Artifact |
|---|---|---|---|---|---|---|---|
{chr(10).join(run_rows)}

**Examined and excluded:** `015-C01 R02` trace: it is the attempt's first, failing run (18:14 to 19:04), started before the implementation commit (21:22), so its tree is not identifiable, and the passing second run was not preserved per test; `010-C01 R02` trace: the same run and tree as `010-C01 R01` (4,294 tests, `be29f3b`), a duplicate.

**Comparability.** Pass/fail outcomes are comparable across all rows. Wall times are **not** comparable: the first five runs (2026-10-03 to 10-05) were 64 to 109 minutes on the pre-TESTSPEED fixture; the later rows are on later infrastructure. Timing is therefore used only as the ratio of per-test durations inside one run.

## 2. Every evaluated historical change

Baseline P = the last earlier preserved **qualifying** full run; I = the evaluated run. "Strict rule" is the rule with all of the reviewer's mandatory corrections (fail closed on runtime and cross-cutting surfaces; any new, renamed, deleted, unmapped or ambiguously mapped file forces a full run). "Graph-only diagnostic" switches every forced-full surface rule off to show what the dependency graph alone would pick (never to be used as evidence of safety).

| Change (I vs P) | Files changed | Strict rule | Main forced-full reasons | Graph-only diagnostic | Known failures in I |
|---|---|---|---|---|---|
{chr(10).join(chg)}

Full per-change detail (every changed file, every forced-full reason with its file, and for the graph-only diagnostic every selected class with the reason it was selected) is in `outputs/eval_*.json` and `outputs/relaxed_diagnostic.json`.

**What the forced-full reasons are (not noise).** The three smallest changes show it: `006-C01 R02` (55 files) adds a migration, changes the data model and adds two test files; `010-C01 R03` changes shared test fixtures and harnesses and adds test files; `N016 R03` adds a test file and changes the shared template/inventory test machinery. All three are surfaces the reviewer named as forced-full. One classifier over-trigger was found and fixed during the work (`GPT Docs/` was treated as an unclassified path); every other reason was checked against the file named.

## 3. Test-inventory reconciliation

- **Strict rule:** every evaluated change was FORCED_FULL, so the selection is the whole discovered list and there is nothing to reconcile (selected = discovered, reused = none).
- **Graph-only diagnostic:** for every run that produced a selection (`010-C01 R03`, `N016 R01`, `N016 R02 (unplanned run)`, `N016 R03`), selected plus reused equals the discovered list exactly, with no duplicates and no test in both sets (`reconciliation.exact` is `true` in `outputs/relaxed_diagnostic.json`; the duplicate count is 0).
- Runs with an incomplete mapping (unreached production files) are not reconciled; they are full runs by rule.

## 4. Misses and potential misses

| Case | Strict rule | Graph-only diagnostic | Assessment |
|---|---|---|---|
| Any change where the strict rule selects and a reused class later fails | none exists: the strict rule selected in 0 of 9 evaluated changes | n/a | **No actual miss, but the result is vacuous** |
| `012-C01 R01`: `BackupCryptoTests.A_file_that_is_not_a_backup_is_rejected_with_its_own_reason` failed (Passed in P) | FORCED_FULL (new files, migrations, controllers) | full (unreached controllers and migrations) | a **random-input flake** fixed later by `TESTQ-BACKUPCRYPTO-DETERMINISTIC` (`020160f`); not attributable to the change; covered by a full run in both modes |
| `N016 R02 unplanned run`: `GateDHistoryIntegrityTests.D5_financial_history...` failed (Passed in P) | FORCED_FULL (shared test infrastructure and template machinery) | **would be a SELECTION_MISS**: the diagnostic selects only 15% of tests and `GateDHistoryIntegrityTests` is outside it | a **latent production race** in `BillingService.LoadLedgerAsync` (later corrected by `ALV-007-C01` R02/R03, which proved it with 4 of 5 deterministic tests failing on the old code); not caused by the change that was being tested |

**Conclusion of the table.** Under the reviewer's literal definition ("a later qualifying full run that finds a failure in a test class the selector reused") the `D5` case would count as a miss in any selection that did not force a full run, although the change under test did not cause it. Selection will keep meeting latent defects and flaky tests that a full run catches by luck, so the definition needs an **attribution step** before a pilot (Section 7: a failure counts as a `SELECTION_MISS` only if it reproduces on the evaluated tree and not on the preserved baseline tree, or is shown to be caused by the change); without it either selection is suspended by every latent defect, or the definition is meaningless. I am not changing the definition, only reporting that it needs the reviewer's decision.

## 5. Proposed forced-full trigger list (all of it fail-closed)

A change forces a full run if it touches, or the analysis cannot conclusively exclude, any of:
- **New, renamed, deleted, copied, unmapped or ambiguously mapped** production or test C# file; any non-C# file in a backend project; any path outside the known non-backend areas (`.alveara/`, `docs/`, `GPT Docs/`, `src/alveara-client/`, `.colaberry/`, `assets/`, `tests/*.mjs`, a short list of root documents).
- **Paths:** `Program.cs`; `Controllers/`; `Middleware/`; `Architecture/{{Auditing, Concurrency, Idempotency, Identity, Money, Time, Storage, Backup, Lifecycle, Configuration, Logging, BackgroundWork}}/`; `Data/`; `Migrations/`; `*.sql`; DTO and view contract files (`*Views.cs`, `*Dto.cs`, `*Input(s).cs`); `*.csproj`, `*.sln`, `*.props`, `*.targets`, `global.json`, `nuget.config`, `Directory.*`, `.config/`; `xunit.runner.json`, `*.runsettings`, `.editorconfig`; shared test fixtures, base classes, harnesses and the template-database machinery (`TestDatabase*`, `RealMigration*`, `SafetyTestBase`, `*TestBase`, `*Fixture*`, `*Harness*`, `SchedulingTestSupport`, `TestStorageCleanup`).
- **Changed lines** that add or remove: dependency-injection or service registration (`AddScoped/AddTransient/AddSingleton/AddDbContext/...`, `services.`); an interface declaration; a record or DTO contract; serialization or validation (`Json*`, `[Required]`, `[StringLength]`, `IValidatableObject`); reflection or dynamic loading (`Assembly.`, `Activator.`, `Type.GetType`, `GetMethod(`, `GetProperty(`); EF configuration (`OnModelCreating`, `HasIndex`, `HasKey`, `ToTable`, `HasData`).
- **A changed production file that no test class reaches in the dependency graph** (it may still be reached through reflection, DI or assembly scanning: controllers, migrations, hosted services).
- **Discovery or mapping incomplete:** a test class missing from the historical class map, defined in more than one file, or a test id that cannot be matched to a preserved result.

## 6. Estimated savings (from preserved timing evidence only; estimates)

- **Under the strict rule: none on this history.** The rule selected in 0 of 9 evaluated story-sized changes, and in only **{prof_c['SELECT']} of {len(prof)}** backend-touching commits ({prof_c['FORCED_FULL']} forced full). The {prof_c['SELECT']} that qualify are small modify-only commits of 1 to 7 files (`{", ".join(x["commit"] for x in sel_commits[:7])}`), mostly test-only or tiny fixes. The most common forced-full reasons across the {len(prof)} commits: {"; ".join("%s (%d)" % (k, v) for k, v in reasons.most_common(5))}.
- **Graph-only (not the rule, no safety claim):** where a selection existed it covered 15% to 80% of tests and 19% to 96% of test time (the reused part would save, on the sum of per-test durations: `N016 R02 (unplanned run)` about 81%, `010-C01 R03` about 45%, `N016 R03` about 24%, `N016 R01` about 4%). On the current 34-minute run that would be roughly 8 minutes at best for a ledger-sized fix, and 0 for the others; and those are the cases the strict rule already forces full.
- **Therefore the savings stated in the proposal (20 to 25 minutes per qualifying story) are not supported by the preserved history.** The one case that is supported and already approved is reuse by equivalence when no backend or backend-test file changed (`ALV-N016` R04): it saves the full 34 minutes.

## 7. Proposed frozen `selection.json` schema and evaluator contract (for any later shadow pilot)

```json
{{
  "schemaVersion": 1,
  "baseline": {{"runCommit": "<sha>", "evidencePath": "<.alveara/...>", "resultCount": 4324, "allPassed": true, "environment": {{"sqlEdition": "Express", "dotnetSdk": "<version>", "tool versions file hash": "<sha256>"}}}},
  "candidate": {{"closureCommit": "<sha>", "implementationCommit": "<sha>", "discoveredTests": 4324, "discoveryEvidence": "<path of the actual test-discovery list for this tree>"}},
  "changedFiles": [{{"status": "M|A|D|R|C", "path": "<path>", "classification": "mapped|non-backend|forced-full-surface|unreached|unclassified", "reasons": ["..."]}}],
  "decision": "SELECT | FORCED_FULL",
  "forcedFullReasons": [{{"path": "<path>", "rule": "<rule id>"}}],
  "selectedClasses": [{{"class": "<name>", "file": "<path>", "because": "reaches <changed file> | changed test file | guard | smoke"}}],
  "reusedClasses": [{{"class": "<name>", "baselineTests": 12, "baselineOutcome": "Passed", "sourceUnchangedSinceBaseline": true}}],
  "reconciliation": {{"selected": 0, "reused": 0, "discovered": 0, "duplicates": 0, "exact": true}},
  "rulesVersion": "<hash of the trigger list and maps>"
}}
```

**Evaluator contract (deterministic, no judgement; fails on any doubt):** (1) the run commit is the Stage 1 closure commit and the tree is clean; (2) `selection.json` equals the one recomputed from the diff and the maps; (3) discovery is the real discovery list of the closure tree and every class, filter, test id and preserved result matches (any gap fails); (4) forced-full triggers are absent when `decision` is SELECT; (5) selected plus reused equals discovered, with no duplicates; (6) all selected tests passed in the run; (7) every reused test is Passed in a baseline that is itself a qualifying full run on a comparable environment, and its class source is unchanged since the baseline; (8) no environment, tool-version or configuration file changed; (9) the audit floor: a full run at every gate, release, milestone and shared-core change, and after every fifth selected Stage 2 not already covered by one of those events (no full run before every push); (10) an attribution rule for a `SELECTION_MISS` (below). It prints `SELECTION STAGE 2 QUALIFIES` or the failing lines.

**Proposed miss definition for the reviewer's decision:** a later qualifying full run that fails in a class the selector reused is a `SELECTION_MISS` only if the failure reproduces on the evaluated tree and does not occur on the baseline tree, or is shown to be caused by a change between them; a failure that is shown to be pre-existing (a flaky test or a latent defect reproducible on the baseline tree) is recorded as a `LATENT_FAILURE_FOUND_BY_FULL_RUN`, fixed under its owner, and does not suspend selection but is counted and reported.

## 8. Recommendation: `NOT_READY_FOR_SHADOW_PILOT`

Exact gaps:
1. **The rule almost never applies to story-sized changes.** 9 of 9 evaluated changes and {prof_c['FORCED_FULL']} of {len(prof)} commits are forced full under the mandatory corrections, chiefly new files, data model and EF configuration, controllers, DTOs, DI registrations and shared test fixtures. A pilot would mostly measure nothing.
2. **"Zero misses" is vacuous.** The strict rule made no selection on the history, so there is no evidence of safety to show. The graph-only diagnostic selected in only 4 changes and has 1 would-be miss (`D5`, a latent race), which is a gap in the miss definition (Section 4), not a graph error.
3. **The preserved evidence is too thin:** 10 preserved runs (9 comparisons), 2 with any failure, both non-attributable; the older runs have wall times on different infrastructure.
4. **The static graph is still heuristic.** The namespace-aware, nested-type-aware, comment-stripped type-reference graph discriminates (the billing ledger loader is reached by 41% of test classes, the document import service by 28%) but cannot see reflection, DI or assembly scanning (117 production files are reached by no test class, controllers and migrations among them) and treats every same-named type as a possible reference. A real implementation needs semantic (compiler-level) analysis of each closure state and an actual `dotnet test --list-tests` inventory, not name matching.
5. **The attribution rule for a `SELECTION_MISS` (Section 7) must be decided** before any pilot, otherwise the first latent defect suspends selection.

**What I would do instead (for the reviewer to consider, not a request):** keep **reuse by equivalence** (approved, saves the full run when the backend did not change, deterministic and cheap) and extend it only to cases that need no dependency analysis: documentation-only, control-record-only and frontend-only changes. Measure how often the rest of the backlog is of that kind before investing in selection.
'''
open(os.path.join(HERE, 'REPORT.md'), 'w', encoding='utf-8', newline='').write(REPORT.strip().replace('\r\n', '\n').replace('\n', '\r\n') + '\r\n')
print('report written', len(REPORT))
