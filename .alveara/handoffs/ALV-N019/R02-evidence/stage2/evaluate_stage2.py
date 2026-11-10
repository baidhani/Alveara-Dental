"""ALV-N019 R02 Stage 2 evaluator: decides every frozen criterion from the files run_stage2.sh wrote. No judgement, no threshold is read from anywhere but this file.

usage: python evaluate_stage2.py <out-folder> <baseline backend-tests.csv> <approved-snapshot-sha> <run-sha>
Prints one PASS/FAIL line per criterion and writes RESULT.txt in the folder; exit code 0 only when every criterion passes.
"""
import json, os, re, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8')

EXPECTED_TESTS = 5697                # the 5,612 of the ALV-N006 R01 Stage 2 run (the baseline), no retired id, plus the 85 ids in expected_new_ids.txt (ALV-N019)
EXPECTED_NEW = 85
MIN_CLONES = 3000                    # the run must really use the template (about 3,280 clones are expected)
MAX_REAL_FIXTURES = 60               # fixtures on the real create-and-migrate path (about 32 are expected: the migration pipeline and the fixture itself)
CLIENT = {'vitest': ('1879', '120'), 'build': None, 'lint': None, 'tsc': None, 'playwright_mocked': '138', 'repo_checks': '49'}   # the mocked suite is 138 passed and 2 skipped (a data-driven test skips itself when every planned portal story already shows progress)

out, baseline, snapshot, run = sys.argv[1:5]       # snapshot: the approved snapshot, the commit that holds the implementation 54806a3 and the catalog data
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
check('the run commit is recorded', f'run commit: {run}' in summary, run)
tree_before = read('tree_before.txt') if os.path.exists(os.path.join(out, 'tree_before.txt')) else None
tree_after = read('tree_after.txt') if os.path.exists(os.path.join(out, 'tree_after.txt')) else None
check('no tracked file changed before or after the run, and the run added or removed no file (the untracked listing is identical before and after)', 'tracked changes before: 0 ' in summary and 'tracked changes after: 0' in summary and tree_before is not None and tree_before == tree_after, 'identical listings' if tree_before is not None and tree_before == tree_after else 'listings differ or missing')
differs = [f for f in git('diff', '--name-only', snapshot, run).splitlines() if not (f.startswith('.alveara/') or f == 'PROGRESS.md')]
check('no product, test, script, configuration, solution, package or catalog file differs between the approved snapshot and the run commit (only .alveara records and PROGRESS.md may)', not differs and '.alveara/story_catalog.json' not in git('diff', '--name-only', snapshot, run).splitlines(), f'{len(differs)} files')
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
check('same existing test ids and outcomes as the ALV-N006 R01 Stage 2 baseline (no retired id); exactly the 85 listed new ids (no more, no fewer)', cmp.returncode == 0, cmp.stdout.strip().splitlines()[-1] if cmp.stdout.strip() else 'no output')

# 3 the template's own telemetry (role run): reported as information only (it is not part of the reviewer's section 12.2 contract)
events = [json.loads(l) for l in read('telemetry.jsonl').splitlines() if l.strip()] if os.path.exists(os.path.join(out, 'telemetry.jsonl')) else []
mine = [e for e in events if e.get('role') == 'run']
summ = [e for e in mine if e['event'] == 'template_summary']
tele_info = f"INFO  template telemetry (information only): {len([e for e in mine if e['event'] == 'template_built'])} template built; " + (f"clones {summ[-1]['context'].get('clones')}, fallbacks {summ[-1]['context'].get('fallbacks')}, real-migration fixtures {summ[-1]['context'].get('real_migration_fixtures')}" if summ else 'no summary event')

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

# 6 the five real-backend walkthroughs of section 12.2
for gate, want in {'walk_playwright.auth.config.ts': '12', 'walk_playwright.patient-portability.config.ts': '7', 'walk_playwright.patient-export-package.config.ts': '10', 'walk_playwright.patient-merge.config.ts': '9', 'walk_playwright.bulk-import.config.ts': '13'}.items():
    text = read(f'gates/{gate}.txt')
    code = re.search(rf'== gate {gate} exit=(\d+)', summary)
    check(f'walkthrough {gate}: {want} passed, none failed, exit 0', bool(code) and code.group(1) == '0' and re.search(rf'(?<!\d){want} passed', text) is not None and not re.search(r'[1-9]\d* failed', text), '')

# 7 one stage-0 chain run with the injected-clock host
chain = read('gates/chain_stage0.txt') if os.path.exists(os.path.join(out, 'gates', 'chain_stage0.txt')) else ''
code = re.search(r'== gate chain_stage0 exit=(\d+)', summary)
check('chain stage 0: runner exit 0', bool(code) and code.group(1) == '0', f'exit {code.group(1) if code else "?"}')
check('chain stage 0: the host reported the injected clock', 'Clock mode: injected' in chain)
check('chain stage 0: nine records PASS, none FAIL, TRACKED_EXCEPTION or SKIPPED_BLOCKED', 'stage 0: PASS 9, FAIL 0, TRACKED_EXCEPTION 0, SKIPPED_BLOCKED 0; exit 0' in chain)
evp = os.path.join(out, 'chain_evidence', 'evidence.json')
ev = json.loads(open(evp, encoding='utf-8-sig').read()) if os.path.exists(evp) else {'records': []}
check('chain stage 0: the evidence has nine PASS records, each stamped clockMode injected', len(ev['records']) == 9 and all(r['outcome'] == 'PASS' and r['clockMode'] == 'injected' for r in ev['records']), f"{len(ev['records'])} records")

width = max(len(n) for n, _, _ in results)
lines = [f'{"PASS" if ok else "FAIL"}  {n.ljust(width)}  {d}' for n, ok, d in results]
verdict = 'STAGE 2 QUALIFIES' if all(ok for _, ok, _ in results) else 'STAGE 2 DOES NOT QUALIFY'
info = ([f'INFO  backend elapsed (operational evidence, not a criterion): {elapsed}s = {elapsed/60:.1f} min'] if elapsed < 10**9 else ['INFO  backend elapsed: not recorded']) + [tele_info]
text = '\n'.join(lines + [''] + info + ['', verdict])
open(os.path.join(out, 'RESULT_ALV-N019_R02.txt'), 'w', encoding='utf-8').write(text + '\n')
print(text)
sys.exit(0 if verdict == 'STAGE 2 QUALIFIES' else 1)
