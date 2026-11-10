"""ALV-N019 R02: focused validation (adapted from ALV-N006 R01) of the Stage 2 evaluator (evaluate_stage2.py) and its comparison (compare_outcomes.py).

It builds a synthetic Stage 2 output folder in which every substantive criterion is met (the real baseline ids plus exactly the allowlisted new ids, all passed; client gates, the five walkthroughs, the chain
run and its evidence as a passing run writes them) and runs the real evaluator over it. There is no backend duration limit, so a long and a short backend run with every criterion met are both ACCEPTED and the
elapsed time is only reported. Each of these must be REJECTED: a failed or skipped backend test, a missing baseline id, a changed baseline outcome, an id outside the allowlist, an allowlisted id that did not
run, a runner proof with a wrong output variable, a file outside .alveara and PROGRESS.md that differs between the approved snapshot and the run commit, a chain run that did not report the injected clock, a chain
evidence record that is not PASS, a failed walkthrough, and a dirty working tree.
It changes no production behaviour and runs no test suite. usage: python validate_evaluator.py   (from anywhere; exit code 0 only when every scenario behaves as stated)
"""
import csv, html, json, os, shutil, subprocess, sys, tempfile

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, *(['..'] * 5)))
BASELINE = os.path.join(ROOT, '.alveara', 'handoffs', 'ALV-N006', 'R01-regression-evidence', 'backend-tests.csv')
EVALUATOR = os.environ.get('ALVERA_EVALUATOR') or os.path.join(HERE, 'evaluate_stage2.py')
ALLOWLIST = os.path.join(HERE, 'expected_new_ids.txt')
RETIRED = os.path.join(HERE, 'retired_ids.txt')

head = subprocess.run(['git', '-C', ROOT, 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
baseline_ids = [r['test'] for r in csv.DictReader(open(BASELINE, encoding='utf-8', newline=''))]
allow_ids = [html.unescape(l.rstrip('\r\n')) for l in open(ALLOWLIST, encoding='utf-8') if l.strip()]
retired_ids = [l.rstrip('\r\n') for l in open(RETIRED, encoding='utf-8') if l.strip()]
assert len(baseline_ids) == 5612 and len(allow_ids) == 85 and len(retired_ids) == 0, (len(baseline_ids), len(allow_ids), len(retired_ids))
assert sum('.ChainClockSeamTests.' in n for n in allow_ids) == 6 and sum('.PatientMergeHostileInputTests.' in n for n in allow_ids) == 67, 'the new classes must be in the allowlist (checked here by two of them)'

GATES = ['vitest', 'build', 'lint', 'tsc', 'playwright_mocked', 'repo_checks', 'walk_playwright.auth.config.ts', 'walk_playwright.patient-portability.config.ts', 'walk_playwright.patient-export-package.config.ts',
         'walk_playwright.patient-merge.config.ts', 'walk_playwright.bulk-import.config.ts', 'chain_stage0']
TEXTS = {'vitest': 'Test Files  120 passed (120)\n      Tests  1879 passed (1879)\n', 'playwright_mocked': '  2 skipped\n  138 passed (20s)\n', 'repo_checks': 'ℹ pass 49\nℹ fail 0\n',
         'walk_playwright.auth.config.ts': '  12 passed (30s)\n', 'walk_playwright.patient-portability.config.ts': '  7 passed (30s)\n', 'walk_playwright.patient-export-package.config.ts': '  10 passed (30s)\n',
         'walk_playwright.patient-merge.config.ts': '  9 passed (30s)\n', 'walk_playwright.bulk-import.config.ts': '  13 passed (30s)\n',
         'chain_stage0': 'Clock mode: injected\nFRESH run cba2d2fc01564eaa8dabb4ef1f329854 stage 0: PASS 9, FAIL 0, TRACKED_EXCEPTION 0, SKIPPED_BLOCKED 0; exit 0\nEvidence kept in C:\\x\\y\n'}
GOOD_PROOF = 'OK    playwright.auth.config.ts: runner passes no output variable; x reads nothing\nOK    playwright.patient-merge.config.ts: runner passes PATIENT_MERGE_E2E_OUT; x reads PATIENT_MERGE_E2E_OUT\nevery walkthrough output goes to the runner folder\n'
BAD_PROOF = 'WRONG playwright.patient-merge.config.ts: runner passes PATIENT_MERGES_E2E_OUT; x reads PATIENT_MERGE_E2E_OUT\nPROBLEM x\n1 problem(s)\n'


def trx(results):
    rows = ''.join(f'<UnitTestResult testName="{html.escape(name, quote=True)}" outcome="{outcome}" />\n' for name, outcome in results)
    return f'<?xml version="1.0"?>\n<TestRun>\n<Results>\n{rows}</Results>\n</TestRun>\n'


def build(folder, results, elapsed_seconds, proof=None, texts=None, records=None, tree_after=0, tree_listing_after=None):
    os.makedirs(os.path.join(folder, 'trx'))
    os.makedirs(os.path.join(folder, 'gates'))
    os.makedirs(os.path.join(folder, 'chain_evidence'))
    open(os.path.join(folder, 'trx', 'full_serial.trx'), 'w', encoding='utf-8').write(trx(results))
    lines = [f'run commit: {head}', f'approved snapshot: {head}', 'tracked changes before: 0 (0 = none); untracked paths before: 2 (the unrelated design drafts)', 'env: ALVERA_TEST_TEMPLATE=[]', f'== backend exit=0 elapsed={elapsed_seconds}s']
    lines += [f'== gate {g} exit=0 elapsed=1s' for g in GATES]
    lines += [f'tracked changes after: {tree_after}']
    open(os.path.join(folder, 'summary.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    merged = {**TEXTS, **(texts or {})}
    for g in GATES:
        open(os.path.join(folder, 'gates', f'{g}.txt'), 'w', encoding='utf-8').write(merged.get(g, 'ok\n'))
    recs = records if records is not None else [{'stepId': f'S{i}', 'outcome': 'PASS', 'clockMode': 'injected'} for i in range(9)]
    open(os.path.join(folder, 'chain_evidence', 'evidence.json'), 'w', encoding='utf-8').write(json.dumps({'records': recs}))
    listing = '?? docs/design/MANUAL_E2E_STABILIZATION_PLAN.md\n?? docs/design/POST_GATE_E_STABILIZATION_PROGRAM_AGENT_BRIEF.md\n'
    open(os.path.join(folder, 'tree_before.txt'), 'w', encoding='utf-8').write(listing)
    open(os.path.join(folder, 'tree_after.txt'), 'w', encoding='utf-8').write(listing if tree_listing_after is None else tree_listing_after)
    events = [{'role': 'run', 'event': 'template_built', 'context': {}}, {'role': 'run', 'event': 'template_summary', 'context': {'rebuilds': 0, 'fallbacks': 0, 'clones': 3369, 'real_migration_fixtures': 32}}]
    open(os.path.join(folder, 'telemetry.jsonl'), 'w', encoding='utf-8').write('\n'.join(json.dumps(e) for e in events) + '\n')
    open(os.path.join(folder, 'runner_proof.txt'), 'w', encoding='utf-8').write(GOOD_PROOF if proof is None else proof)
    leak = 'databases_total 1\nAlveraTest 0\nAlveraTemplate 0\ntemplate_backups_in_Temp 0\nfolder_Storage 3920\n'
    for n in ('leak_before.txt', 'leak_after.txt'):
        open(os.path.join(folder, n), 'w', encoding='utf-8').write(leak)


def good():
    return [(n, 'Passed') for n in baseline_ids if n not in retired_ids] + [(n, 'Passed') for n in allow_ids]


def run_evaluator(results, elapsed_seconds, snapshot=None, **kw):
    folder = tempfile.mkdtemp(prefix='alvn019-eval-')
    try:
        build(folder, results, elapsed_seconds, **kw)
        p = subprocess.run([sys.executable, EVALUATOR, folder, BASELINE, snapshot or head, head], capture_output=True, text=True, encoding='utf-8', cwd=ROOT)
        return p.returncode, p.stdout
    finally:
        shutil.rmtree(folder, ignore_errors=True)


def replace_outcome(results, index, outcome):
    results[index] = (results[index][0], outcome)
    return results


BASE_COMMIT = '3d70b69'   # an older commit: the diff between it and HEAD contains product, test and script files, so a run whose approved snapshot were this commit must be rejected
scenarios = [
    # name, results, elapsed seconds, extra arguments, expected exit code, text that must appear in the output
    ('A. 80 minutes with every substantive criterion met is accepted and the time is only reported', good(), 80 * 60, {}, 0, 'INFO  backend elapsed (operational evidence, not a criterion): 4800s = 80.0 min'),
    ('B. 3 minutes with every substantive criterion met is accepted', good(), 180, {}, 0, 'STAGE 2 QUALIFIES'),
    ('C. one backend test failed is rejected', replace_outcome(good(), 100, 'Failed'), 80 * 60, {}, 1, 'FAIL  discovered and passed are exactly the expected count'),
    ('D. one backend test was skipped is rejected', replace_outcome(good(), 200, 'NotExecuted'), 80 * 60, {}, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('E. a baseline id is missing (count made up with an extra id) is rejected', good()[1:] + [('Alveara.Api.Tests.Invented.Extra', 'Passed')], 80 * 60, {}, 1, 'FAIL  same existing test ids and outcomes'),
    ('F. a baseline id is missing and nothing replaces it is rejected', good()[1:], 80 * 60, {}, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('G. a baseline outcome changed (Passed to Failed) is rejected', replace_outcome(good(), 5, 'Failed'), 3 * 60, {}, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('H. an allowlisted new id did not run (replaced by an id outside the allowlist) is rejected', good()[:-1] + [('Alveara.Api.Tests.Invented.NotInTheAllowlist', 'Passed')], 80 * 60, {}, 1, 'FAIL  same existing test ids and outcomes'),
    ('I. one extra id beyond the allowlist is rejected', good() + [('Alveara.Api.Tests.Invented.OneTooMany', 'Passed')], 80 * 60, {}, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('J. a runner whose proof shows a wrong output variable is rejected', good(), 80 * 60, {'proof': BAD_PROOF}, 1, 'FAIL  runner proof'),
    ('K. a product, test or script file differs between the approved snapshot and the run commit is rejected', good(), 80 * 60, {'snapshot': BASE_COMMIT}, 1, 'FAIL  no product, test, script, configuration, solution, package or catalog file differs'),
    ('L. a chain run that did not report the injected clock is rejected', good(), 80 * 60, {'texts': {'chain_stage0': 'Clock mode: real\nFRESH run x stage 0: PASS 9, FAIL 0, TRACKED_EXCEPTION 0, SKIPPED_BLOCKED 0; exit 0\n'}}, 1, 'FAIL  chain stage 0: the host reported the injected clock'),
    ('M. a chain evidence record that is not PASS is rejected', good(), 80 * 60, {'records': [{'stepId': f'S{i}', 'outcome': 'PASS' if i else 'FAIL', 'clockMode': 'injected'} for i in range(9)]}, 1, 'FAIL  chain stage 0: the evidence has nine PASS records'),
    ('N. a chain evidence record stamped with the real clock is rejected', good(), 80 * 60, {'records': [{'stepId': f'S{i}', 'outcome': 'PASS', 'clockMode': 'real' if i == 3 else 'injected'} for i in range(9)]}, 1, 'FAIL  chain stage 0: the evidence has nine PASS records'),
    ('O. a walkthrough with one failed test is rejected', good(), 80 * 60, {'texts': {'walk_playwright.patient-merge.config.ts': '  8 passed (30s)\n  1 failed\n'}}, 1, 'FAIL  walkthrough walk_playwright.patient-merge.config.ts'),
    ('P. a walkthrough with fewer tests than expected is rejected', good(), 80 * 60, {'texts': {'walk_playwright.bulk-import.config.ts': '  12 passed (30s)\n'}}, 1, 'FAIL  walkthrough walk_playwright.bulk-import.config.ts'),
    ('Q. a mocked-suite failure is rejected', good(), 80 * 60, {'texts': {'playwright_mocked': '  2 skipped\n  2 failed\n  136 passed (20s)\n'}}, 1, 'FAIL  gate playwright_mocked'),
    ('R. a tracked file changed by the run is rejected', good(), 80 * 60, {'tree_after': 3}, 1, 'FAIL  no tracked file changed before or after the run'),
    ('S. a file added by the run (an extra untracked path) is rejected', good(), 80 * 60, {'tree_listing_after': '?? docs/design/MANUAL_E2E_STABILIZATION_PLAN.md\n?? docs/design/POST_GATE_E_STABILIZATION_PROGRAM_AGENT_BRIEF.md\n?? src/alveara-client/bulk-import-e2e-out/\n'}, 1, 'FAIL  no tracked file changed before or after the run'),
    ('T. the unrelated untracked design drafts present before and after the run are accepted', good(), 80 * 60, {}, 0, 'STAGE 2 QUALIFIES'),
]

failures = 0
for name, results, seconds, kw, expected_exit, expected_text in scenarios:
    code, text = run_evaluator(results, seconds, **kw)
    ok = code == expected_exit and expected_text in text
    failures += 0 if ok else 1
    print(f'{"OK  " if ok else "WRONG"}  {name}  (exit {code}, expected {expected_exit})')
    if not ok:
        print('      expected the output to contain:', expected_text)
        print('\n'.join('      | ' + l for l in [l for l in text.splitlines() if l.startswith('FAIL')] + text.splitlines()[-12:]))
print()
print('every scenario behaved as stated' if failures == 0 else f'{failures} scenario(s) behaved differently from the statement')
sys.exit(0 if failures == 0 else 1)
