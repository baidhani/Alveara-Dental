"""Re-qualification by reuse (Option A of DECISION_REQUEST.md; used only if the reviewer approves it): decides, with no judgement, whether the preserved backend and template
evidence of the failed shared Stage 2 (run commit ba51d2e) still holds for the current closure tree.

usage: python evaluate_reuse.py <story: ALV-N016|ALV-007-C01> <run-commit>
Prints one PASS/FAIL line per criterion; exit code 0 only when every one passes. The fresh frontend gate is evaluated separately by evaluate_frontend_gate.py of ALV-CONTROL-C01
(run on the same closure tree); both must qualify.
"""
import os, re, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8')
story, run = sys.argv[1:3]
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *(['..'] * 5)))
PRESERVED = {'ALV-N016': 'RESULT_ALV-N016.txt', 'ALV-007-C01': 'RESULT_ALV-007-C01.txt'}[story]
SHARED_RUN = 'ba51d2e321c227713001871537898a584590f084'
results = []


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))


def git(*a):
    return subprocess.run(['git', *a], capture_output=True, text=True, encoding='utf-8', cwd=ROOT).stdout.strip()


text = open(os.path.join(ROOT, '.alveara/handoffs/ALV-N016/R03-regression-evidence', PRESERVED), encoding='utf-8').read()
lines = [l for l in text.splitlines() if l.startswith(('PASS', 'FAIL'))]
failed = [l for l in lines if l.startswith('FAIL')]
check('the preserved shared run had exactly one failed line, the mocked Playwright gate, and every other criterion passed', len(failed) == 1 and 'gate playwright_mocked' in failed[0] and len(lines) > 15, f'{len(lines)} lines, {len(failed)} failed')
check('the preserved run commit is the one the failed review examined', git('rev-parse', SHARED_RUN) == SHARED_RUN)
backend_diff = git('diff', '--name-only', SHARED_RUN, run, '--', 'src/Alveara.Api', 'src/Alveara.Api.Tests').splitlines()
check('no backend or backend-test file differs between the preserved run commit and this run commit', not backend_diff, str(backend_diff))
other = [f for f in git('diff', '--name-only', SHARED_RUN, run).splitlines()
         if not (f.startswith('.alveara/') or f == 'PROGRESS.md' or f.startswith('src/alveara-client/') or f.startswith('assets/') or f == 'index.html' or f.startswith('tests/'))]
check('nothing outside the control records and the Command Center client differs (no build, package, solution or configuration file)', not other, str(other))
check('the changes since the preserved run are exactly the approved ALV-CONTROL-C01 files', not [f for f in git('diff', '--name-only', '69c6f7fe0c4155338b8d752804878d44784348a6', run).splitlines() if f.startswith(('src/', 'assets/', 'tests/')) or f == 'index.html'], 'implementation 69c6f7f vs run commit')
check('the working tree is clean', git('status', '--porcelain') == '')
width = max(len(n) for n, _, _ in results)
out = [f'{"PASS" if ok else "FAIL"}  {n.ljust(width)}  {d}' for n, ok, d in results]
verdict = 'REUSE EQUIVALENCE HOLDS' if all(ok for _, ok, _ in results) else 'REUSE EQUIVALENCE DOES NOT HOLD'
print('\n'.join(out + ['', verdict]))
sys.exit(0 if verdict == 'REUSE EQUIVALENCE HOLDS' else 1)
