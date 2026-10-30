"""Gate E Stage 2 evaluator: decides every frozen criterion from the files run_stage2.sh wrote. No judgement, no threshold is read from anywhere but this file.

usage: python evaluate_stage2.py <out-folder> <baseline backend-tests.csv> <stage1-evidence-run-sha> <run-sha>
Prints one PASS/FAIL line per criterion and writes RESULT.txt in the folder; exit code 0 only when every criterion passes.
"""
import json, os, re, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8')

EXPECTED_TESTS = 5612                # exactly the ALV-N006 Stage 2 full run (the baseline): no new id and no retired id, because no product code has changed since
EXPECTED_NEW = 0
MIN_CLONES = 3000                    # the run must really use the template (about 3,280 clones are expected)
MAX_REAL_FIXTURES = 60               # fixtures on the real create-and-migrate path (about 32 are expected: the migration pipeline and the fixture itself)
CLIENT = {'tsc': None, 'vitest': ('1879', '120'), 'build': None, 'lint': None, 'playwright_mocked': '138', 'repo_checks': '13'}   # the mocked suite is 138 passed and 2 skipped (a data-driven test skips itself when every planned portal story already shows progress)

out, baseline, impl, run = sys.argv[1:5]
results = []


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))


def read(p):
    return open(os.path.join(out, p), encoding='utf-8', errors='replace').read()


def leak(p):
    return {k: int(v) for k, v in re.findall(r'^(\S+)\s+(\d+)\s*$', read(p), re.M)}


summary = read('summary.txt')


def git(*a):
    return subprocess.run(['git', *a], capture_output=True, text=True, encoding='utf-8').stdout.strip()


# 1 run identity and a clean tree
check('run commit is the Gate E Stage 1 closure commit', f'run commit: {run}' in summary, run)
check('working tree clean before and after', 'working tree before: 0 ' in summary and 'working tree after: 0 ' in summary)
src_changed = [f for f in git('diff', '--name-only', impl, run).splitlines() if f.startswith('src/')]
check('no src/ file differs between the Stage 1 evidence run commit and the run commit', not src_changed, f'{len(src_changed)} files')
other_changed = [f for f in git('diff', '--name-only', impl, run).splitlines() if not (f.startswith('.alveara/') or f == 'PROGRESS.md')]
check('only .alveara/ files and PROGRESS.md differ between the Stage 1 evidence run commit and the run commit', not other_changed, f'{len(other_changed)} other files')
check('the template mode switch is not set', 'env: ALVERA_TEST_TEMPLATE=[]' in summary)

# 2 the backend run
m = re.search(r'== backend exit=(\d+) elapsed=(\d+)s', summary)
elapsed = int(m.group(2)) if m else 10**9
check('backend run exit code 0', m and m.group(1) == '0')
# Item 43 sets no backend duration limit, so elapsed time is operational evidence only: it is measured and reported below and never decides the verdict.
trx = open(os.path.join(out, 'trx', 'full_serial.trx'), encoding='utf-8').read()
outcomes = {}
for o in re.findall(r'<UnitTestResult[^>]*outcome="(\w+)"', trx):
    outcomes[o] = outcomes.get(o, 0) + 1
check('discovered and passed are exactly the expected count, none failed or skipped', outcomes == {'Passed': EXPECTED_TESTS}, f'{outcomes} (expected Passed {EXPECTED_TESTS})')
trx_ids = dict(re.findall(r'<UnitTestResult[^>]*testName="([^"]+)"[^>]*outcome="(\w+)"', trx))
cmp = subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'compare_outcomes.py'), baseline, os.path.join(out, 'trx', 'full_serial.trx'), '--expected-new', str(EXPECTED_NEW), '--expected-new-file', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'expected_new_ids.txt'), '--retired-file', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'retired_ids.txt')], capture_output=True, text=True, encoding='utf-8')
open(os.path.join(out, 'compare.txt'), 'w', encoding='utf-8').write(cmp.stdout)
check('same existing test ids and outcomes as the ALV-N006 Stage 2 full-run baseline (no retired id); no new id (none listed, none allowed)', cmp.returncode == 0, cmp.stdout.strip().splitlines()[-1] if cmp.stdout.strip() else 'no output')

# 3 the template's own telemetry (role run)
events = [json.loads(l) for l in read('telemetry.jsonl').splitlines() if l.strip()] if os.path.exists(os.path.join(out, 'telemetry.jsonl')) else []
mine = [e for e in events if e.get('role') == 'run']
built = [e for e in mine if e['event'] == 'template_built']
summ = [e for e in mine if e['event'] == 'template_summary']
fallbacks = [e for e in mine if e['event'] == 'template_restore_fallback']
check('the run built exactly one template and it was not rebuilt', len(built) == 1 and bool(summ) and summ[-1]['context']['rebuilds'] == 0, f'built {len(built)}')
check("zero fallback to create-and-migrate by the run's own template", not fallbacks and bool(summ) and summ[-1]['context']['fallbacks'] == 0, f'{len(fallbacks)} fallback events')
check('no template build failure', not [e for e in mine if e['event'] == 'template_build_failed'])
clones = summ[-1]['context']['clones'] if summ else 0
check(f'the template was really used (at least {MIN_CLONES} clones)', clones >= MIN_CLONES, f'{clones} clones')
real = summ[-1]['context']['real_migration_fixtures'] if summ else 10**9
check(f'only the named real-path classes bypass the template (at most {MAX_REAL_FIXTURES} fixtures)', real <= MAX_REAL_FIXTURES, f'{real} real-migration fixtures')

# 3b R02: the runner proved, before running, that every walkthrough writes into the out-folder
proof = read('runner_proof.txt') if os.path.exists(os.path.join(out, 'runner_proof.txt')) else ''
check('runner proof: every walkthrough output goes to the out-folder outside the repository', 'every walkthrough output goes to the runner folder' in proof and 'WRONG' not in proof, proof.strip().splitlines()[-1] if proof.strip() else 'missing')

# 4 artifacts
b, a = leak('leak_before.txt'), leak('leak_after.txt')
check('Storage, Temp, Database, Backup and Keys entries, databases, SQL logins and template backups unchanged', b == a, f'before {b} after {a}')
check('no AlveraTemplate database or backup left', a.get('AlveraTemplate', 1) == 0 and a.get('template_backups_in_Temp', 1) == 0)

# 5 client and repository gates
for gate, want in CLIENT.items():
    path = os.path.join(out, 'gates', f'{gate}.txt')
    text = read(f'gates/{gate}.txt') if os.path.exists(path) else ''
    code = re.search(rf'== gate {gate} exit=(\d+)', summary)
    ok = bool(code) and code.group(1) == '0'
    if gate == 'vitest' and ok:
        mm = re.search(r'Test Files\s+(\d+) passed \((\d+)\)\s*\n\s*Tests\s+(\d+) passed \((\d+)\)', text)
        ok = bool(mm) and mm.group(3) == want[0] and mm.group(1) == want[1]
    elif gate == 'playwright_mocked' and ok:
        ok = f'{want} passed' in text and '2 skipped' in text and not re.search(r'[1-9]\d* failed', text)
    elif gate == 'repo_checks' and ok:
        ok = f'pass {want}' in text and 'fail 0' in text
    check(f'gate {gate}', ok, f'exit {code.group(1) if code else "?"}')

# 6 the real-backend walkthroughs: the twelve Gate E Stage 1 walkthroughs (billing-ledger with the repair 2be1d73, 11 of 11) and the five others the ALV-N006 Stage 2 ran
for gate, want in {'walk_playwright.documents.config.ts': '14', 'walk_playwright.documents-library.config.ts': '12', 'walk_playwright.follow-ups.config.ts': '8', 'walk_playwright.work-queue.config.ts': '8', 'walk_playwright.external-cases.config.ts': '9', 'walk_playwright.patient-merge.config.ts': '9', 'walk_playwright.patient-export-package.config.ts': '10', 'walk_playwright.patient-portability.config.ts': '7', 'walk_playwright.bulk-import.config.ts': '13', 'walk_playwright.reports.config.ts': '7', 'walk_playwright.notifications.config.ts': '10', 'walk_playwright.billing-ledger.config.ts': '11', 'walk_playwright.flow.config.ts': '10', 'walk_playwright.permission-policy.config.ts': '7', 'walk_playwright.permission-policy-admin.config.ts': '8', 'walk_playwright.workspace.config.ts': '14', 'walk_playwright.patients.config.ts': '7'}.items():
    text = read(f'gates/{gate}.txt')
    check(f'walkthrough {gate}: {want} passed, none failed', re.search(rf'(?<!\d){want} passed', text) is not None and not re.search(r'[1-9]\d* failed', text), '')

width = max(len(n) for n, _, _ in results)
lines = [f'{"PASS" if ok else "FAIL"}  {n.ljust(width)}  {d}' for n, ok, d in results]
verdict = 'STAGE 2 QUALIFIES' if all(ok for _, ok, _ in results) else 'STAGE 2 DOES NOT QUALIFY'
info = [f'INFO  backend elapsed (operational evidence, not a criterion): {elapsed}s = {elapsed/60:.1f} min'] if elapsed < 10**9 else ['INFO  backend elapsed: not recorded']
text = '\n'.join(lines + [''] + info + ['', verdict])
open(os.path.join(out, 'RESULT_GATE_E.txt'), 'w', encoding='utf-8').write(text + '\n')
print(text)
sys.exit(0 if verdict == 'STAGE 2 QUALIFIES' else 1)
