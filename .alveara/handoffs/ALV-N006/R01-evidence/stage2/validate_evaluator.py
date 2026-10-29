"""ALV-N006 R01: focused validation (adapted from ALV-017-C01 R03; no id is retired here) of the Stage 2 evaluator (evaluate_stage2.py) and its comparison (compare_outcomes.py).

It builds a synthetic Stage 2 output folder in which every substantive criterion is met (the real baseline ids plus exactly the allowlisted new ids, all passed; template, leak, client, walkthrough and
performance files as a passing run writes them) and runs the real evaluator over it. Item 43 sets no backend duration limit, so:
  * a backend run of 80 minutes (and one of 3 minutes) with every substantive criterion met must be ACCEPTED, and the elapsed time must only be reported;
  * a failed or skipped backend test, a missing baseline id, a changed baseline outcome, an id outside the allowlist and an allowlisted id that did not run must each be REJECTED.
It changes no production behaviour and runs no test suite. usage: python validate_evaluator.py   (from anywhere; exit code 0 only when every scenario behaves as stated)
"""
import csv, html, json, os, re, shutil, subprocess, sys, tempfile

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, *(['..'] * 5)))
BASELINE = os.path.join(ROOT, '.alveara', 'handoffs', 'ALV-018-C02', 'R01-regression-evidence', 'backend-tests.csv')
EVALUATOR = os.environ.get('ALVERA_EVALUATOR') or os.path.join(HERE, 'evaluate_stage2.py')   # the override exists only to run the same scenarios against the R01 evaluator as a control
ALLOWLIST = os.path.join(HERE, 'expected_new_ids.txt')
RETIRED = os.path.join(HERE, 'retired_ids.txt')

head = subprocess.run(['git', '-C', ROOT, 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
baseline_ids = [r['test'] for r in csv.DictReader(open(BASELINE, encoding='utf-8', newline=''))]
allow_ids = [html.unescape(l.rstrip('\r\n')) for l in open(ALLOWLIST, encoding='utf-8') if l.strip()]
retired_ids = [l.rstrip('\r\n') for l in open(RETIRED, encoding='utf-8') if l.strip()]
assert len(baseline_ids) == 5481 and len(allow_ids) == 131 and len(retired_ids) == 0, (len(baseline_ids), len(allow_ids), len(retired_ids))
assert sum('.ReportCsvTests.' in n for n in allow_ids) == 9 and sum('.ReportExportApiTests.' in n for n in allow_ids) == 15, 'every new class must be in the allowlist (checked here by two of them)'


def trx(results):
    rows = ''.join(f'<UnitTestResult testName="{html.escape(name, quote=True)}" outcome="{outcome}" />\n' for name, outcome in results)
    return f'<?xml version="1.0"?>\n<TestRun>\n<Results>\n{rows}</Results>\n</TestRun>\n'


GOOD_PROOF = 'OK    playwright.patients.config.ts: runner passes PATIENT_E2E_OUT; x reads PATIENT_E2E_OUT\nevery walkthrough output goes to the runner folder\n'
BAD_PROOF = 'WRONG playwright.patients.config.ts: runner passes PATIENTS_E2E_OUT; x reads PATIENT_E2E_OUT\nPROBLEM x\n1 problem(s)\n'


def build(folder, results, elapsed_seconds, proof=None):
    os.makedirs(os.path.join(folder, 'trx'))
    os.makedirs(os.path.join(folder, 'gates'))
    open(os.path.join(folder, 'trx', 'full_serial.trx'), 'w', encoding='utf-8').write(trx(results))
    gates = ['tsc', 'vitest', 'build', 'lint', 'playwright_mocked', 'repo_checks', 'walk_playwright.reports.config.ts', 'walk_playwright.follow-ups.config.ts', 'walk_playwright.flow.config.ts', 'walk_playwright.notifications.config.ts', 'walk_playwright.permission-policy.config.ts', 'walk_playwright.permission-policy-admin.config.ts', 'walk_playwright.workspace.config.ts', 'walk_playwright.patient-export-package.config.ts', 'walk_playwright.patient-portability.config.ts', 'walk_playwright.patient-merge.config.ts', 'walk_playwright.bulk-import.config.ts', 'walk_playwright.patients.config.ts']
    lines = [f'run commit: {head}', f'implementation commit: {head}', 'working tree before: 0 changed paths (0 = clean)', 'env: ALVERA_TEST_TEMPLATE=[]', f'== backend exit=0 elapsed={elapsed_seconds}s']
    lines += [f'== gate {g} exit=0 elapsed=1s' for g in gates]
    lines += ['working tree after: 0 changed paths']
    open(os.path.join(folder, 'summary.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    texts = {'vitest': 'Test Files  120 passed (120)\n      Tests  1879 passed (1879)\n', 'playwright_mocked': '  2 skipped\n  138 passed (20s)\n', 'repo_checks': 'ℹ pass 13\nℹ fail 0\n',
             'walk_playwright.reports.config.ts': '  7 passed (30s)\n', 'walk_playwright.follow-ups.config.ts': '  8 passed (30s)\n', 'walk_playwright.flow.config.ts': '  10 passed (30s)\n', 'walk_playwright.notifications.config.ts': '  10 passed (30s)\n', 'walk_playwright.permission-policy.config.ts': '  7 passed (30s)\n', 'walk_playwright.permission-policy-admin.config.ts': '  8 passed (30s)\n', 'walk_playwright.workspace.config.ts': '  14 passed (30s)\n', 'walk_playwright.patient-export-package.config.ts': '  10 passed (30s)\n', 'walk_playwright.patient-portability.config.ts': '  7 passed (30s)\n', 'walk_playwright.patient-merge.config.ts': '  9 passed (30s)\n', 'walk_playwright.bulk-import.config.ts': '  13 passed (30s)\n', 'walk_playwright.patients.config.ts': '  7 passed (30s)\n'}
    for g in gates:
        open(os.path.join(folder, 'gates', f'{g}.txt'), 'w', encoding='utf-8').write(texts.get(g, 'ok\n'))
    events = [{'role': 'run', 'event': 'template_built', 'context': {}},
              {'role': 'run', 'event': 'template_summary', 'context': {'rebuilds': 0, 'fallbacks': 0, 'clones': 3369, 'real_migration_fixtures': 32}}]
    open(os.path.join(folder, 'telemetry.jsonl'), 'w', encoding='utf-8').write('\n'.join(json.dumps(e) for e in events) + '\n')
    open(os.path.join(folder, 'runner_proof.txt'), 'w', encoding='utf-8').write(GOOD_PROOF if proof is None else proof)
    leak = 'databases_total 1\nAlveraTest 0\nAlveraTemplate 0\ntemplate_backups_in_Temp 0\nfolder_Storage 3920\n'
    for n in ('leak_before.txt', 'leak_after.txt'):
        open(os.path.join(folder, n), 'w', encoding='utf-8').write(leak)


def good():
    return [(n, 'Passed') for n in baseline_ids if n not in retired_ids] + [(n, 'Passed') for n in allow_ids]


def run_evaluator(results, elapsed_seconds, proof=None):
    folder = tempfile.mkdtemp(prefix='alv018c01-eval-')
    try:
        build(folder, results, elapsed_seconds, proof)
        p = subprocess.run([sys.executable, EVALUATOR, folder, BASELINE, head, head], capture_output=True, text=True, encoding='utf-8', cwd=ROOT)
        return p.returncode, p.stdout
    finally:
        shutil.rmtree(folder, ignore_errors=True)


def with_change(change):
    results = good()
    return change(results)


def replace_outcome(results, index, outcome):
    results[index] = (results[index][0], outcome)
    return results


scenarios = [
    # name, results, elapsed seconds, expected exit code, text that must appear in the output
    ('A. 80 minutes with every substantive criterion met is accepted and the time is only reported', good(), 80 * 60, 0, 'INFO  backend elapsed (operational evidence, not a criterion): 4800s = 80.0 min'),
    ('B. 3 minutes with every substantive criterion met is accepted', good(), 180, 0, 'STAGE 2 QUALIFIES'),
    ('C. 80 minutes but one backend test failed is rejected', replace_outcome(good(), 100, 'Failed'), 80 * 60, 1, 'FAIL  discovered and passed are exactly the expected count'),
    ('D. 80 minutes but one backend test was skipped is rejected', replace_outcome(good(), 200, 'NotExecuted'), 80 * 60, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('E. a baseline id is missing (count made up with an extra id) is rejected', good()[1:] + [('Alveara.Api.Tests.Invented.Extra', 'Passed')], 80 * 60, 1, 'FAIL  same existing test ids and outcomes'),
    ('F. a baseline id is missing and nothing replaces it is rejected', good()[1:], 80 * 60, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('G. a baseline outcome changed (Passed to Failed) is rejected', replace_outcome(good(), 5, 'Failed'), 3 * 60, 1, 'STAGE 2 DOES NOT QUALIFY'),
    ('H. an allowlisted new id did not run (replaced by an id outside the allowlist) is rejected', good()[:-1] + [('Alveara.Api.Tests.Invented.NotInTheAllowlist', 'Passed')], 80 * 60, 1, 'FAIL  same existing test ids and outcomes'),
    ('J. a runner whose proof shows a wrong output variable is rejected', good(), 80 * 60, 1, 'FAIL  runner proof'),
    ('I. one extra id beyond the allowlist is rejected', good() + [('Alveara.Api.Tests.Invented.OneTooMany', 'Passed')], 80 * 60, 1, 'STAGE 2 DOES NOT QUALIFY'),
]

failures = 0
for name, results, seconds, expected_exit, expected_text in scenarios:
    code, text = run_evaluator(results, seconds, BAD_PROOF if name.startswith('J.') else None)
    ok = code == expected_exit and expected_text in text
    failures += 0 if ok else 1
    print(f'{"OK  " if ok else "WRONG"}  {name}  (exit {code}, expected {expected_exit})')
    if not ok:
        print('      expected the output to contain:', expected_text)
        print('\n'.join('      | ' + l for l in [l for l in text.splitlines() if l.startswith('FAIL')] + text.splitlines()[-12:]))
print()
print('every scenario behaved as stated' if failures == 0 else f'{failures} scenario(s) behaved differently from the statement')
sys.exit(0 if failures == 0 else 1)
