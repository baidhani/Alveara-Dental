"""ALV-017-C01 R02: proves the Stage 2 runner sends every real-backend walkthrough's output to the folder it names, outside the repository.

usage: python verify_runner_outputs.py <repository-root> <run_stage2.sh> <out-folder>
For every "playwright.<name>.config.ts <VARIABLE>" pair in run_stage2.sh it reads the config's testMatch, reads that spec in src/alveara-client/e2e and checks that the spec reads exactly
process.env.<VARIABLE> for its output folder (the R01 defect: the runner passed PATIENTS_E2E_OUT, the spec reads PATIENT_E2E_OUT, so screenshots went into the repository). It also checks the
out-folder is outside the repository. Prints one line per walkthrough; exit code 0 only when every pair matches.
"""
import os, re, sys

root, script, out = sys.argv[1:4]
root = os.path.abspath(root)
text = open(script, encoding='utf-8').read()
pairs = re.findall(r'"(playwright\.[\w.-]+\.config\.ts) ([A-Z0-9_]+_OUT)"', text)
problems = []
if not pairs:
    problems.append('no walkthrough pairs found in the runner')
out_abs = os.path.abspath(out)
if os.path.commonpath([root, out_abs]) == root:
    problems.append(f'the out-folder {out_abs} is inside the repository {root}')
for config, variable in pairs:
    cfg = open(os.path.join(root, 'src', 'alveara-client', config), encoding='utf-8').read()
    match = re.search(r'testMatch:\s*"([^"]+)"', cfg)
    if not match:
        problems.append(f'{config}: no testMatch'); continue
    spec = open(os.path.join(root, 'src', 'alveara-client', 'e2e', match.group(1)), encoding='utf-8').read()
    variables = sorted(set(re.findall(r'process\.env\.([A-Z0-9_]+_OUT)\b', spec)))
    ok = variables == [variable]
    print(f'{"OK   " if ok else "WRONG"} {config}: runner passes {variable}; {match.group(1)} reads {", ".join(variables) or "nothing"}')
    if not ok:
        problems.append(f'{config}: runner passes {variable} but the spec reads {variables}')
for p in problems:
    print('PROBLEM', p)
print('every walkthrough output goes to the runner folder' if not problems else f'{len(problems)} problem(s)')
sys.exit(1 if problems else 0)
