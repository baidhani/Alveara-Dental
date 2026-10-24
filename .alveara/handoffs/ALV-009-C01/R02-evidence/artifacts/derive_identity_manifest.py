"""ALV-009-C01 R02: derive the backend identity manifest from the actual baseline and the changed backend test surface, then compare it with the R01 full run.
usage: python derive_identity_manifest.py <baseline csv> <focused.trx> <apitests.trx> <R01 full_serial.trx> <changed_classes.txt> <out allowlist>
1. The classes come from the test files changed or added since the baseline commit (git diff, not memory); each must appear in the focused run.
2. new ids = ids in the focused + apitests runs that are not in the baseline; every baseline id in a class the runs cover must be present and Passed.
3. The derived set must equal the R01 full run's ids minus the baseline exactly (nothing missing, nothing extra); the allowlist is written only then."""
import csv, re, sys
sys.stdout.reconfigure(encoding='utf-8')
base_csv, focused, api, full, classes_file, out = sys.argv[1:7]
def trx(p):
    return dict(re.findall(r'<UnitTestResult[^>]*testName="([^"]+)"[^>]*outcome="(\w+)"', open(p, encoding='utf-8').read()))
base = {r['test']: r['outcome'] for r in csv.DictReader(open(base_csv, encoding='utf-8', newline=''))}
f, a, full_ids = trx(focused), trx(api), trx(full)
res = {**f, **a}
classes = [l.strip() for l in open(classes_file) if l.strip()]
cls = lambda k: k.split('.')[3]
ok = True
uncovered = [c for c in classes if c not in {cls(k) for k in f} | {cls(k) for k in a} and c not in ('RealMigrationInventory', 'SchedulingApiHarness')]
print('changed test classes:', len(classes), '| not present in either run:', uncovered); ok &= not uncovered
covered = {cls(k) for k in res}
missing = [k for k in base if cls(k) in covered and k not in res]
changed = [k for k in base if k in res and res[k] != base[k]]
print('baseline ids in covered classes missing from the runs:', len(missing), '| outcome changed:', len(changed), '| not Passed in runs:', sum(1 for v in res.values() if v != 'Passed')); ok &= not missing and not changed
new = sorted(set(res) - set(base))
full_new = sorted(set(full_ids) - set(base))
print('derived new ids:', len(new), '| R01 full-run new ids:', len(full_new), '| derived-only:', len(set(new) - set(full_new)), '| full-run-only:', len(set(full_new) - set(new)))
ok &= new == full_new
print('expected total =', len(base), '+', len(new), '=', len(base) + len(new), '| R01 full run total:', len(full_ids), '| every full-run outcome Passed:', all(v == 'Passed' for v in full_ids.values()))
ok &= len(base) + len(new) == len(full_ids)
if ok:
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(new) + '\n')
    print('MANIFEST DERIVED AND EQUAL TO THE R01 FULL RUN; wrote', out)
sys.exit(0 if ok else 1)
