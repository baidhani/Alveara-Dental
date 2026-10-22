"""ALV-N016 Stage 2: exact comparison of backend test IDs and outcomes against the ALV-010-C01 R03 Stage 2 baseline.

usage: python compare_outcomes.py <baseline backend-tests.csv> <results.trx> [--expected-new N]
Exit code 0 only when: every baseline test id appears in the results with the same outcome (Passed), no baseline id is missing, no result has an outcome other than Passed,
and the ids that are new are exactly --expected-new in number. Prints every difference.
"""
import csv, re, sys

sys.stdout.reconfigure(encoding='utf-8')

def read_baseline(path):
    out = {}
    with open(path, encoding='utf-8', newline='') as f:
        for row in csv.DictReader(f):
            out[row['test']] = row['outcome']
    return out

def read_trx(path):
    text = open(path, encoding='utf-8').read()
    out = {}
    for m in re.finditer(r'<UnitTestResult[^>]*testName="([^"]+)"[^>]*outcome="(\w+)"', text):
        out[m.group(1)] = m.group(2)                    # the names exactly as the results file writes them (XML escapes kept), the same form the baseline file holds
    return out

def main():
    base, new_expected = read_baseline(sys.argv[1]), 19
    if '--expected-new' in sys.argv:
        new_expected = int(sys.argv[sys.argv.index('--expected-new') + 1])
    res = read_trx(sys.argv[2])
    missing = sorted(set(base) - set(res))
    changed = sorted(k for k in base if k in res and res[k] != base[k])
    bad = sorted(k for k, v in res.items() if v != 'Passed')
    added = sorted(set(res) - set(base))
    print(f'baseline ids: {len(base)}; result ids: {len(res)}; new ids: {len(added)} (expected {new_expected})')
    for label, items in (('MISSING from the results', missing), ('OUTCOME CHANGED', changed), ('NOT PASSED in the results', bad)):
        print(f'{label}: {len(items)}')
        for i in items[:20]:
            print('   ', i)
    print('new ids:')
    for i in added:
        print('   ', i, '->', res[i])
    ok = not missing and not changed and not bad and len(added) == new_expected
    print('COMPARISON', 'PASS' if ok else 'FAIL')
    return 0 if ok else 1

if __name__ == '__main__':
    sys.exit(main())
