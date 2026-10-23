# Proposal: run only the tests a change can affect (change-impact test selection) for Stage 2 regression

**Status:** PROPOSED, for independent review. Nothing in this document is in force. No code, tooling or cadence rule has been changed for it.
**Raised by:** the product owner, 2026-10-22, after the test-speed initiative (`ALV-N016`) closed.
**Asks of the reviewer:** the decisions in Section 9. The proposal changes how a *Stage 2 broad regression* is defined (`HANDOFF_SCHEMA.md` v3 section 4 and Rule 16), which are reviewer-owned.

## 1. The problem in numbers

The full serial backend suite is the slowest gate of every high-risk story. The test-speed initiative cut it from **94 min 14 s** (ALV-010-C01, 4,300 tests) to **34 min 12 s** (the shared Stage 2 of 2026-10-22, 4,324 tests, `.alveara/handoffs/ALV-N016/R03-regression-evidence/`). Two facts limit how much further the run itself can go:

- **Per-test cost is dominated by overhead, not by the test.** In that run the median test takes **0.05 s**, **2,245 of 4,324 tests take under 50 ms**, and each test's database clone costs about **0.18 s** to restore (602 s across 3,289 clones). The reported test durations add up to 15.4 min of the 34.2 min wall time.
- **Further speed-ups need more infrastructure experiments with uncertain gain.** Parallel lanes were tried and closed (`docs/testing/TESTSPEED_P2_STATUS.md`); on SQL Server Express the 4-scheduler limit caps what lanes can add, and they reintroduced deadlocks and pool timeouts. They are parked, not abandoned.

There is a different kind of saving that needs no infrastructure change: **do not run, at Stage 2, tests that the change provably cannot affect.** The remaining backlog is 15 planned stories (`ALV-N015`, `STORY-009`, `ALV-009-C01`, `ALV-009-C02`, `STORY-017`, `ALV-017-C01`, `STORY-018`, `ALV-018-C01`, `ALV-018-C02`, `ALV-N006`, `ALV-N007`, `ALV-N012`, `ALV-N013`, `ALV-N014`, `ALV-N008`); many add a module on top of the shared platform (this is an expectation from their titles, not a measured fact).

## 2. What already exists, and the precedent that makes this a small step

- **When a full run is required** is already risk-based (Rule 16): high-risk categories, every gate, release and milestone, and every third low/medium story.
- **The two-stage protocol** (`HANDOFF_SCHEMA.md` v3 section 4) already moves the broad run behind the Stage 1 review so it runs once, on equivalent code.
- **Reuse by equivalence was approved on 2026-10-22** for `ALV-N016` R04 and `ALV-007-C01` R03 (`.alveara/reviews/ALV-N016/R04-stage1.md`): the reviewer ruled that re-running 4,324 backend tests was unnecessary because *no backend or backend-test file differed* from a preserved passing run, and decided it with a frozen, deterministic evaluator (`evaluate_reuse.py`) plus one fresh frontend gate.

This proposal generalises that single case. Reuse by equivalence covers the case "nothing in the backend changed". Change-impact selection covers "the backend changed only in module X", by running the tests that can reach X and reusing the preserved result for the rest, under the same discipline: a deterministic rule, frozen before the run, decided by an evaluator with no judgement.

## 3. Principle and non-goals

**Principle:** a Stage 2 broad regression exists to prove that *the change did not break anything*. A test that cannot execute any changed code, and whose inputs did not change, can only produce the result it produced last time. Running it adds time, not evidence, provided that "cannot execute any changed code" is established by a deterministic, conservative, reviewed rule rather than by judgement.

**Non-goals:**
- It does not change what any test asserts, how any test is written, or the existing cadence for *when* a story needs a broad run.
- It does not apply to frontend gates (Vitest about 3 min, Playwright about 25 s, repository checks about 2 s): they stay whole.
- It does not replace full runs at gates, releases, milestones, or after changes to the shared core (Section 5). Those stay full.
- It does not touch the template-database infrastructure, `xunit.runner.json`, or parallelism.

## 4. Proposed mechanism

### 4.1 The selection rule (deterministic, computed from the diff)
Given the Stage 1 implementation commit `I`, the last **preserved qualifying full run** commit `P` (a full serial run that passed, recorded under `.alveara/handoffs/*/…-regression-evidence/`), and the set `F` of files changed between `P` and `I`:

1. **Forced-full triggers.** If `F` contains any file in the *shared core list* (5.1) or any file the rule cannot classify, the Stage 2 is a **full** run. No selection.
2. **Direct tests.** Otherwise map each changed production file to the production *module* it belongs to, and each module to its test classes (5.2).
3. **Dependent tests (reverse dependencies).** Add the test classes of every module that *references* a changed module, transitively. This is the step that would have caught the 2026-10-22 billing race: the ledger loader was used by completion, adoption and reconciliation, which live in other modules.
4. **Always included.** Every new or changed test class, the architecture/guard tests (route scans, inventory guards, coexistence tests), and a small fixed **smoke set** (Section 5.3).
5. **Everything else is reused** from the preserved run `P`, only if `P`'s evidence is valid for that class (same test ids, same outcome Passed, source files of those classes unchanged since `P`).

The output is a frozen `selection.json` (selected classes, reused classes with the preserved outcome, the reason for each) committed with the Stage 1 package.

### 4.2 The Stage 2 contract changes from "all N pass" to "selected pass, reused unchanged"
The evaluator (Section 6) requires: the selected tests all pass; every reused test id is present in `P`'s results with outcome Passed and its class unchanged; the union equals the current full test list (so nothing is silently dropped); new tests are in the selected set. A reused result is reported as **REUSED** (never "skipped" and never "passed in this run").

### 4.3 The dependency data
The rule needs two maps. Both are generated, committed and reviewed, not hand-judged each time:
- **module to test classes**: derived from class names and namespaces (60 modules by first word today; the largest, Document, is 13.7% of the suite).
- **module reference graph**: derived from the production code's using/namespace references (a compile-time analysis, no execution). The execution plan's declared story dependencies are used as a cross-check, not as the source.

## 5. Safeguards

### 5.1 The shared core list (a change here forces a full run)
At least: `AlveraDbContext` and `Data/*` models, migrations and schema, `Architecture/Auditing`, `Concurrency`, `Idempotency`, `Identity` and permissions, `Money`, `Time`, `Storage`, `Backup`, middleware and `Program.cs`, any `*.csproj`, `Directory.Build.*`, solution and global configuration, test infrastructure (`TestDatabase*`, `SafetyTestBase`, `xunit.runner.json`), and the template machinery. These are exactly the "high-risk" categories of Rule 16 that matter to every module.

### 5.2 Conservative on doubt
A changed file with no module mapping, a new module with no map entry, or a rename the rule cannot resolve forces a full run. The rule may be too cautious; it may never be optimistic.

### 5.3 Smoke set
A fixed cross-module set (about 5% of the suite, chosen once and reviewed: one or two classes per major module: patient, scheduling, billing, documents, prescriptions, completion, backup) runs on every selected Stage 2. It is a cheap tripwire for a mapping error.

### 5.4 A scheduled full run remains
Selection reduces what *a story's Stage 2* must run; it never removes full runs from the repository's life. Proposed floor: a full serial run at **every quality gate and release**, at **every fifth** story that used selection, and **before any push to the shared remote** that follows a selected Stage 2 (so the remote always carries a state that has had a full pass within the last five stories). The cadence is a reviewer decision (Section 9).

### 5.5 A miss is a defect of the rule, not of the story
If a full run ever fails in a class the selection reused, the failure is recorded as `SELECTION_MISS`, the selection rule is reopened, the miss is added as a permanent regression case of the rule, and selection is suspended (full runs only) until the reviewer re-approves it.

## 6. Tooling and the evaluator contract
- `select_tests.py <P-commit> <I-commit>`: reads the diff and the two maps, writes `selection.json`. Deterministic; no network; no test execution.
- `run_stage2_selected.sh`: runs `dotnet test` with a filter built from `selection.json` (the same preflight as today: the count must be non-zero and is printed first), then the frontend gate unchanged.
- `evaluate_selected.py`: decides, without judgement: run commit equals the closure commit; tree clean; `selection.json` equals the one recomputed from the diff; selected count equals the run's count; all selected Passed; every reused id present in `P` with Passed; selected plus reused equals the full list; no file outside the closure files differs; forced-full triggers absent. It prints `SELECTION STAGE 2 QUALIFIES` or the failing lines.
- All of it is frozen in the Stage 1 package, as `evaluate_reuse.py` was for `ALV-N016` R04.

## 7. Evidence before trust: shadow-mode pilot
No story is allowed to rely on selection until the rule has been shown not to miss.
1. **Backtest on history.** For each completed story with a full-run record, compute what the rule would have selected and compare with what actually failed or was fixed in later full runs. Any past regression that the rule would have reused is a design bug to fix before any pilot.
2. **Shadow pilot.** For the next 3 to 5 stories that need a Stage 2, compute and publish `selection.json`, but **still run the full suite**. Compare: every failure must be inside the selection. The pilot passes only with zero misses.
3. **Switch on** for a bounded number of stories (for example 5), with the scheduled full run of 5.4, then review the results before making it standing policy.

## 8. Expected benefit (estimates, not measurements)
- A full serial Stage 2 backend run is **34 min**. A typical focused selection, from the focused runs we already do, is **3 to 10 min** (the 1,379-test descendant selection took 16 min; the 409-test ledger selection 7 min), plus the smoke set (about 2 min). Estimate: **a selected Stage 2 costs about 8 to 15 min instead of 34**, saving roughly **20 to 25 min per qualifying story**.
- If about 9 of them need a Stage 2 and about 6 of those are eligible for selection, the saving is on the order of **2 to 3 hours** of machine time over the backlog, plus the rerun cost of revisions. This is modest on its own; its main value is that the saving **grows with the suite** (the suite grows with every story) while the full-run cost is paid only at gates and on shared-core changes.
- The cost: the tooling and maps (a short story of its own), the backtest, and the shadow pilot, which is deliberately free of risk because it also runs the full suite.

## 9. Decisions requested from the reviewer
1. **Principle.** Is change-impact selection acceptable in principle for Stage 2 backend regression, on the condition that it is deterministic, conservative and piloted in shadow mode first? If not, the proposal ends here.
2. **Authority and form.** Should the rule be a new story (for example `ALV-CONTROL-C02`), an amendment to `HANDOFF_SCHEMA.md` and Rule 16 after the pilot, or both?
3. **The shared core list (5.1).** Approve, extend or replace it.
4. **The safety net (5.4).** Approve the floor (every gate and release; every fifth selected story; before a push), or set another.
5. **Reuse boundary.** Reuse a preserved result only from a full run the reviewer approved as Stage 2 evidence (my proposal), or also from a story's own focused runs?
6. **Pilot size and exit.** Backtest first, then a shadow pilot of 3 to 5 stories with zero misses required: approve, or set another size.
7. **Miss handling (5.5).** Approve suspension of selection until re-approval after any `SELECTION_MISS`.

## 10. What I recommend
Approve the principle and the backtest only. The backtest is read-only work on history, costs no machine time worth mentioning, and answers the question that decides everything else: would this rule have missed anything that really broke? Only if the backtest finds none should the shadow pilot be authorised.

## 11. Honest limits of this proposal
- The module and reference maps are heuristics (class names and namespaces). Tests that reach code through reflection, dependency injection registration, shared base classes or the database schema can have dependencies the maps do not show; that is why the shared core list, the smoke set, the shadow pilot and the scheduled full runs all exist.
- Reusing a preserved result assumes the machine and database environment behave the same; an environment change (SQL Server edition, Windows update, a new dependency version) must force a full run, and the rule treats `*.csproj`, `global.json` and tool-version files as shared core.
- The savings are estimates from focused runs; the backtest and the pilot would replace them with measurements.
- The existing parked ideas (parallel lanes, the Developer-edition experiment, shared databases per class, cheaper test-database settings) are independent of this proposal and could be combined with it later.
