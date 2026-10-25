"""ALV-009-C02 R01: derive the backend identity manifest for the frozen Stage 2 from the actual baseline and the changed backend test surface, and cross-check it two independent ways.

usage: python derive_identity_manifest.py <baseline csv> <focused.trx> <apitests.trx> <dotnet test --list-tests output> <changed_files.txt> <out allowlist> <class-name keywords of the focused filter, comma separated>

1. CLASSES FROM THE SOURCE, NOT FROM MEMORY. <changed_files.txt> is `git diff --name-only --diff-filter=AM <baseline-commit> HEAD -- src/Alveara.Api.Tests`; every class declared in those files must appear in the focused
   or ApiTests run (the R01 miss of ALV-009-C01 was a class declared inside another file).
2. THE WHOLE ASSEMBLY, NOT JUST THE CHANGED FILES. `dotnet test --list-tests` names every test the assembly will run. Ids in it that the baseline does not have are the NEW ids the assembly really contains; every one of them must have run
   (and passed) in the focused or ApiTests run, and every id the runs produced that the baseline lacks must be in that list. (A few theories with object parameters are named differently by --list-tests and by the results file; they are
   baseline ids in both and are set aside by comparing on the class and method name.)
3. The baseline ids of every class the runs cover are present and unchanged.
The allowlist is written only when all three hold.
"""
import csv, html, re, sys
sys.stdout.reconfigure(encoding='utf-8')
base_csv, focused, api, listing, changed_files, out, keywords = sys.argv[1:8]
KEYWORDS = keywords.split(',')


def trx(p):
    return {html.unescape(n): o for n, o in re.findall(r'<UnitTestResult[^>]*testName="([^"]+)"[^>]*outcome="(\w+)"', open(p, encoding='utf-8').read())}


def key(name):                       # class.method, ignoring the theory parameters and the XML escaping, for comparing --list-tests with a results file
    return html.unescape(name).split('(')[0]


base = {html.unescape(r['test']): r['outcome'] for r in csv.DictReader(open(base_csv, encoding='utf-8', newline=''))}
res = {**trx(focused), **trx(api)}
listed = [l.strip() for l in open(listing, encoding='utf-8') if l.strip().startswith('Alveara.Api.Tests.')]
cls = lambda k: k.split('.')[3]
ok = True

# 1 classes from the changed files
classes = []
for f in [l.strip() for l in open(changed_files, encoding='utf-8') if l.strip().endswith('.cs')]:
    classes += re.findall(r'^\s*(?:public\s+)?(?:sealed\s+)?(?:static\s+)?(?:abstract\s+)?(?:partial\s+)?class\s+(\w+)', open(f, encoding='utf-8-sig').read(), re.M)
ran = {cls(k) for k in res}
helpers = {c for c in classes if not any(k.startswith(f'Alveara.Api.Tests.{c}.') for k in listed)}      # classes with no test of their own (helpers, bases, the migration inventory list)
missing_classes = [c for c in classes if c not in ran and c not in helpers]
print(f'changed-file classes: {len(classes)} ({len(helpers)} without tests of their own) | test classes that did not run: {missing_classes}')
ok &= not missing_classes

# 2 the whole assembly
base_keys = {key(k) for k in base}
listed_new = sorted(n for n in listed if n not in base and key(n) not in base_keys)
new_run = sorted(set(res) - set(base))
list_only = sorted(set(listed_new) - set(new_run))
run_only = sorted(set(new_run) - set(listed))
print(f'new in the assembly (--list-tests): {len(listed_new)} | new in the runs: {len(new_run)} | listed but did not run: {len(list_only)} | ran but not listed: {len(run_only)}')
for n in list_only[:5]: print('  LISTED ONLY', n[:160])
for n in run_only[:5]: print('  RAN ONLY', n[:160])
ok &= not list_only and not run_only

# 3 the baseline in the covered classes
# the focused filter matches on any part of a test's full name, so a class whose NAME does not match is only partly run; the baseline check is made for the classes the filter and the ApiTests run select by name
covered = {cls(k) for k in res if any(w in cls(k) for w in KEYWORDS) or cls(k).endswith('ApiTests')}
miss = [k for k in base if cls(k) in covered and k not in res]
changed = [k for k in base if k in res and res[k] != base[k]]
bad = [k for k, v in res.items() if v != 'Passed']
print(f'baseline ids in covered classes missing from the runs: {len(miss)} | outcome changed: {len(changed)} | not Passed in the runs: {len(bad)}')
ok &= not miss and not changed and not bad
print(f'expected total = {len(base)} + {len(new_run)} = {len(base) + len(new_run)}')
if ok:
    # the allowlist is written in the form the results file uses (XML escapes kept), the same form as the baseline file, because that is what the Stage 2 comparison reads
    raw = {html.unescape(n): n for p in (focused, api) for n in re.findall(r'<UnitTestResult[^>]*testName="([^"]+)"', open(p, encoding='utf-8').read())}
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(raw[n] for n in new_run) + '\n')
    print('MANIFEST DERIVED AND CROSS-CHECKED; wrote', out)
sys.exit(0 if ok else 1)
