"""ALV-007-C01 R02 Stage 2 evaluator: decides every frozen criterion from the files run_stage2.sh wrote. No judgement, no threshold is read from anywhere but this file.

usage: python evaluate_stage2.py <out-folder> <baseline backend-tests.csv> <implementation-sha> <run-sha>
Prints one PASS/FAIL line per criterion and writes RESULT.txt in the folder; exit code 0 only when every criterion passes.
"""
import json, os, re, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8')

CEILING_SECONDS = 120 * 60           # no story time criterion; a sanity bound of two hours (the ALV-N016 55-minute ceiling is that story's, not this one's)
EXPECTED_TESTS = 4324                # the 4,300 of the ALV-010-C01 baseline, the 19 new ALV-N016 template and inventory tests, and the 5 new ledger read-consistency tests
EXPECTED_NEW = 24
CLIENT = {'tsc': None, 'vitest': ('1544', '97'), 'build': None, 'lint': None, 'playwright_mocked': '136', 'repo_checks': '8'}

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
check('run commit is the Stage 1 closure commit', f'run commit: {run}' in summary, run)
check('working tree clean before and after', 'working tree before: 0 ' in summary and 'working tree after: 0 ' in summary)
src_changed = [f for f in git('diff', '--name-only', impl, run).splitlines() if f.startswith('src/')]
check('no src/ file differs between the implementation and the run commit', not src_changed, f'{len(src_changed)} files')
check('the template mode switch is not set', 'env: ALVERA_TEST_TEMPLATE=[]' in summary)

# 2 the backend run
m = re.search(r'== backend exit=(\d+) elapsed=(\d+)s', summary)
elapsed = int(m.group(2)) if m else 10**9
check('backend run exit code 0', m and m.group(1) == '0')
check('backend elapsed is within the two-hour sanity bound', elapsed <= CEILING_SECONDS, f'{elapsed}s = {elapsed/60:.1f} min (bound {CEILING_SECONDS//60} min)')
trx = open(os.path.join(out, 'trx', 'full_serial.trx'), encoding='utf-8').read()
outcomes = {}
for o in re.findall(r'<UnitTestResult[^>]*outcome="(\w+)"', trx):
    outcomes[o] = outcomes.get(o, 0) + 1
check('discovered and passed are exactly the expected count, none failed or skipped', outcomes == {'Passed': EXPECTED_TESTS}, f'{outcomes} (expected Passed {EXPECTED_TESTS})')
cmp = subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'compare_outcomes.py'), baseline, os.path.join(out, 'trx', 'full_serial.trx'), '--expected-new', str(EXPECTED_NEW)], capture_output=True, text=True, encoding='utf-8')
open(os.path.join(out, 'compare.txt'), 'w', encoding='utf-8').write(cmp.stdout)
check('same existing test ids and outcomes as the ALV-010-C01 baseline; exactly the 24 new ids', cmp.returncode == 0, cmp.stdout.strip().splitlines()[-1] if cmp.stdout.strip() else 'no output')

# 3 the template's own telemetry (role run)
events = [json.loads(l) for l in read('telemetry.jsonl').splitlines() if l.strip()] if os.path.exists(os.path.join(out, 'telemetry.jsonl')) else []
mine = [e for e in events if e.get('role') == 'run']
built = [e for e in mine if e['event'] == 'template_built']
summ = [e for e in mine if e['event'] == 'template_summary']
fallbacks = [e for e in mine if e['event'] == 'template_restore_fallback']
check('the run built exactly one template and it was not rebuilt', len(built) == 1 and bool(summ) and summ[-1]['context']['rebuilds'] == 0, f'built {len(built)}')
check("zero fallback to create-and-migrate by the run's own template", not fallbacks and bool(summ) and summ[-1]['context']['fallbacks'] == 0, f'{len(fallbacks)} fallback events')
check('no template build failure', not [e for e in mine if e['event'] == 'template_build_failed'])
trx_ids = dict(re.findall(r'<UnitTestResult[^>]*testName="([^"]+)"[^>]*outcome="(\w+)"', trx))
ledger = {k: v for k, v in trx_ids.items() if 'BillingLedgerReadConsistencyTests' in k}
check('the 5 ledger read-consistency tests ran and passed', len(ledger) == 5 and set(ledger.values()) == {'Passed'}, f'{len(ledger)} found, outcomes {sorted(set(ledger.values()))}')
check('the concurrency test that exposed the defect passed (D5 financial history)', any('D5_financial_history' in k and v == 'Passed' for k, v in trx_ids.items()))

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
        ok = f'{want} passed' in text
    elif gate == 'repo_checks' and ok:
        ok = f'pass {want}' in text and 'fail 0' in text
    check(f'gate {gate}', ok, f'exit {code.group(1) if code else "?"}')

width = max(len(n) for n, _, _ in results)
lines = [f'{"PASS" if ok else "FAIL"}  {n.ljust(width)}  {d}' for n, ok, d in results]
verdict = 'STAGE 2 QUALIFIES' if all(ok for _, ok, _ in results) else 'STAGE 2 DOES NOT QUALIFY'
text = '\n'.join(lines + ['', verdict])
open(os.path.join(out, 'RESULT.txt'), 'w', encoding='utf-8').write(text + '\n')
print(text)
sys.exit(0 if verdict == 'STAGE 2 QUALIFIES' else 1)
