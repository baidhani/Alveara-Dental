"""ALV-003-C01 R03 shared frontend gate evaluator: decides every frozen criterion from the files run_frontend_gate.sh wrote.
usage: python evaluate_frontend_gate.py <out-folder> <implementation-sha>; exit code 0 only when every criterion passes."""
import os, re, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8')
out, impl = sys.argv[1:3]
DATA_COMMIT = '30e2f69df452e85cac7dab06e907c5c9325cfc09'       # this attempt's implementation: the Stage 1 closure commits change only control records after it
CONTROL_C01 = '69c6f7fe0c4155338b8d752804878d44784348a6'      # ALV-CONTROL-C01's approved implementation (the Command Center correction)
TEST_FILE = 'src/alveara-client/src/pages/PatientRegistrationPage.test.tsx'
ALLOWED = {'.alveara/BUILD_STATE.md', '.alveara/EXECUTION_STATUS.json'}
results = []


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))


def read(p):
    return open(os.path.join(out, p), encoding='utf-8', errors='replace').read()


def git(*a):
    return subprocess.run(['git', *a], capture_output=True, text=True, encoding='utf-8').stdout.strip()


summary = read('summary.txt')
run = re.search(r'run commit: (\w+)', summary).group(1)
check('working tree clean before and after', 'working tree before: 0 ' in summary and 'working tree after: 0 ' in summary)
changed = [f for f in git('diff', '--name-only', DATA_COMMIT, run).splitlines()]
extra = [f for f in changed if f not in ALLOWED and not f.startswith('.alveara/reviews/ALV-CONTROL-C01/') and not f.startswith('.alveara/handoffs/ALV-CONTROL-C01/') and f != 'PROGRESS.md' and f != '.alveara/dist']
since_cc = sorted(f for f in git('diff', '--name-only', CONTROL_C01, run).splitlines() if f.startswith(('src/', 'assets/', 'tests/')) or f == 'index.html')
check('since the approved ALV-CONTROL-C01 implementation exactly one source file changed: the registration test', since_cc == [TEST_FILE], str(since_cc))
check('no file other than the control records differs between the reviewed data commit and the run commit', not [f for f in extra if f.startswith(('src/', 'assets/', 'tests/', 'index.html'))], str(extra))
for gate in ('tsc', 'vitest', 'build', 'lint', 'playwright_mocked', 'repo_checks'):
    code = re.search(rf'== gate {gate} exit=(\d+)', summary)
    text = read(f'gates/{gate}.txt')
    ok = bool(code) and code.group(1) == '0'
    detail = f'exit {code.group(1) if code else "?"}'
    if gate == 'vitest' and ok:
        m = re.search(r'Test Files\s+(\d+) passed \((\d+)\)\s*\n\s*Tests\s+(\d+) passed \((\d+)\)', text)
        ok = bool(m) and m.group(3) == '1545' and m.group(1) == '97'
        detail += ' ' + (f'{m.group(3)} tests in {m.group(1)} files' if m else 'no summary')
    if gate == 'playwright_mocked' and ok:
        ok = '140 passed' in text and ' failed' not in text
        detail += ' ' + ('140 passed' if ok else 'not 140 passed')
    if gate == 'repo_checks' and ok:
        ok = 'pass 13' in text and 'fail 0' in text
        detail += ' ' + ('13 pass, 0 fail' if ok else 'not 13 pass')
    check(f'gate {gate}', ok, detail)
width = max(len(n) for n, _, _ in results)
lines = [f'{"PASS" if ok else "FAIL"}  {n.ljust(width)}  {d}' for n, ok, d in results]
verdict = 'FRONTEND GATE QUALIFIES' if all(ok for _, ok, _ in results) else 'FRONTEND GATE DOES NOT QUALIFY'
text = '\n'.join(lines + ['', verdict])
open(os.path.join(out, 'RESULT.txt'), 'w', encoding='utf-8').write(text + '\n')
print(text)
sys.exit(0 if verdict == 'FRONTEND GATE QUALIFIES' else 1)
