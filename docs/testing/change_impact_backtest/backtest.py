"""Change-impact test-selection BACKTEST (read-only analysis; ALV-CONTROL review response of 2026-10-22).

Reads only git history and the preserved full-run results under .alveara/. It checks out nothing, builds nothing, runs no test and changes no repository file except the
report/output folder next to this script. A heuristic mapping is never treated as evidence that a test is safe to omit: the analysis is FAIL-CLOSED (any doubt = full run).

usage: python backtest.py            (writes outputs/*.json and outputs/summary.json)

For every preserved full run I (with a known tree and a known test-id list) and the last earlier preserved QUALIFYING full run P (all tests passed):
  1. F = the files changed between the two historical trees (git diff -M --name-status P I).
  2. Decide FORCED-FULL (surface list, new/renamed/deleted file, unclassified file, ambiguity) or, for modify-only changes of mapped C# files, compute the selection from the
     HISTORICAL type-reference graph (built from the two trees, never from current source).
  3. Reconcile selected + reused == the discovered test list of I (the preserved result file is the discovery inventory of that run).
  4. Compare with the ground truth in I: every test that failed in I, and every test that flipped Passed->Failed against P.
"""
import collections, csv, json, os, re, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import csharp_model

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(HERE, 'outputs')
os.makedirs(OUT, exist_ok=True)
H = '.alveara/handoffs/'

# ------------------------------------------------------------------ the preserved full runs (curated; every row is justified in the report, section 2)
# commit = the tree the run executed on (a single implementation commit whose finish time precedes the commit time, or the stated closure commit)
RUNS = [
    dict(id='005-C01 R01', commit='cd56749', result=H + 'ALV-005-C01/R01-evidence/artifacts/test-runs/backend-full.trx', wall_min=64),
    dict(id='N011 R01', commit='9a3ac31', result=H + 'ALV-N011/R01-evidence/artifacts/test-runs/backend-full.trx', wall_min=73),
    dict(id='006-C01 R01', commit='90f6ebd', result=H + 'ALV-006-C01/R01-evidence/artifacts/test-runs/backend-full.trx', wall_min=94),
    dict(id='006-C01 R02', commit='0b2aceb', result=H + 'ALV-006-C01/R02-evidence/artifacts/test-runs/backend-full.trx', wall_min=96),
    dict(id='012-C01 R01', commit='c4797b2', result=H + 'ALV-012-C01/R01-evidence/artifacts/test-runs/backend-full.trx', wall_min=109),
    dict(id='010-C01 R01', commit='be29f3b', result=H + 'ALV-010-C01/R01-evidence/artifacts/test-runs/backend-tests.csv', wall_min=None),
    dict(id='010-C01 R03', commit='a3b4bc9', result=H + 'ALV-010-C01/R03-regression-evidence/backend-tests.csv', wall_min=94.2),
    dict(id='N016 R01', commit='3ae51e2', result=H + 'ALV-N016/R01-regression-evidence/backend-tests.csv', wall_min=57.8),
    dict(id='N016 R02 (unplanned run)', commit='0da6f36', result=H + 'ALV-N016/R02-evidence/artifacts/unplanned-full-run/tests.csv', wall_min=53.1),
    dict(id='N016 R03', commit='37d2b08', result=H + 'ALV-N016/R03-regression-evidence/backend-tests.csv', wall_min=34.2),
]
# runs examined and excluded from the comparison (reason recorded in the report)
EXCLUDED = {
    '015-C01 R02 trace': 'the preserved trace is the first, failing run of the attempt (18:14 to 19:04); it started before the implementation commit (21:22), so the tree it ran on is not identifiable; the passing second run was not preserved per test',
    '010-C01 R02 trace': 'the same run as 010-C01 R01 (identical tree be29f3b, 4,294 tests); duplicate evidence',
    '006-C01 R01 vs N011 ordering': 'N011 R01 is dated before 006-C01 R01; the chain follows commit dates',
}

# ------------------------------------------------------------------ git helpers (read-only)
def git(*a, binary=False):
    r = subprocess.run(['git', *a], cwd=ROOT, capture_output=True)
    if r.returncode != 0:
        raise RuntimeError('git %s: %s' % (' '.join(a), r.stderr.decode('utf-8', 'replace')))
    return r.stdout if binary else r.stdout.decode('utf-8', 'replace')


_cache_tree = {}
_cache_blob = {}


def tree_files(commit):
    if commit not in _cache_tree:
        _cache_tree[commit] = set(git('ls-tree', '-r', '--name-only', commit).splitlines())
    return _cache_tree[commit]


def blob(commit, path):
    k = (commit, path)
    if k not in _cache_blob:
        _cache_blob[k] = git('show', '%s:%s' % (commit, path), binary=True).decode('utf-8', 'replace')
    return _cache_blob[k]


# ------------------------------------------------------------------ preserved results
def secs(d):
    h, m, s = d.split(':')
    return int(h) * 3600 + int(m) * 60 + float(s)


def load_result(path):
    """-> list of (test id, outcome, seconds). The preserved file IS the discovery inventory of that run."""
    full = os.path.join(ROOT, path)
    rows = []
    if path.endswith('.trx'):
        t = open(full, encoding='utf-8', errors='replace').read()
        for m in re.finditer(r'<UnitTestResult\b([^>]*)>', t):
            a = m.group(1)
            n = re.search(r'testName="([^"]*)"', a)
            o = re.search(r'outcome="(\w+)"', a)
            d = re.search(r'duration="([^"]+)"', a)
            rows.append((n.group(1), o.group(1) if o else '?', secs(d.group(1)) if d else 0.0))
    else:
        for r in list(csv.reader(open(full, encoding='utf-8')))[1:]:
            rows.append((r[0], r[1], secs(r[2])))
    return rows


def klass(test_id):
    """Alveara.Api.Tests.Foo.Bar(args) -> Foo (the last class segment before the method)."""
    base = test_id.split('(')[0]
    parts = base.split('.')
    return parts[-2] if len(parts) >= 2 else base


# ------------------------------------------------------------------ the historical model of a commit
PROD = 'src/Alveara.Api/'
TESTS = 'src/Alveara.Api.Tests/'
TYPEDEF = re.compile(r'\b(?:class|record|struct|interface|enum)\s+([A-Za-z_]\w*)')
TOKEN = re.compile(r'\b[A-Za-z_]\w*\b')


def cs_files(commit):
    return sorted(f for f in tree_files(commit) if f.endswith('.cs') and (f.startswith(PROD) or f.startswith(TESTS)) and '/obj/' not in f and '/bin/' not in f)


_model = {}


def model(commit):
    """The HISTORICAL dependency model of one commit, built only from that commit's file contents (see csharp_model.py)."""
    if commit not in _model:
        files = cs_files(commit)
        _model[commit] = csharp_model.build(files, lambda f: blob(commit, f), TESTS)
    return _model[commit]


# ------------------------------------------------------------------ forced-full surfaces (the reviewer's list, fail closed)
NOT_BACKEND = ('.alveara/', 'docs/', 'GPT Docs/', 'src/alveara-client/', '.colaberry/', 'assets/', 'tests/')
NOT_BACKEND_EXACT = ('PROGRESS.md', 'index.html', 'CLAUDE.md', 'README.md', '.gitignore', '.claudeignore')
FORCE_PATH = [
    (r'(^|/)Program\.cs$', 'service registration / startup'), (r'/Controllers/', 'endpoint / controller wiring'), (r'/Middleware/', 'middleware'),
    (r'/Architecture/(Auditing|Concurrency|Idempotency|Identity|Money|Time|Storage|Backup|Lifecycle|Configuration|Logging|BackgroundWork)/', 'shared core (audit, concurrency, idempotency, authorization, money, time, storage, backup, lifecycle, configuration, logging, jobs)'),
    (r'/Data/', 'data models / EF configuration'), (r'/Migrations/', 'migrations'), (r'\.sql$', 'SQL / seed script'), (r'(^|/)(Views|Dtos?|Contracts?)\.cs$|Views\.cs$|Dto\.cs$|Inputs?\.cs$', 'DTO / view contract'),
    (r'\.(csproj|sln|props|targets)$|(^|/)(global\.json|nuget\.config|Directory\.[A-Za-z.]+|\.config/.*)$', 'build / SDK / package / solution / tool-version'),
    (r'xunit\.runner\.json$|\.runsettings$|\.editorconfig$', 'test discovery / runner configuration'),
    (r'/(TestDatabase\w*|RealMigration\w*|SafetyTestBase|SchedulingTestSupport|\w*TestBase|\w*Fixture\w*|TestStorageCleanup|\w*Harness\w*)\.cs$', 'shared test fixture / base class / harness / template machinery'),
]
FORCE_CONTENT = [
    (r'\.Add(Scoped|Transient|Singleton|DbContext|Authentication|Authorization|Controllers|HostedService)\b|\bservices\.', 'dependency-injection / service registration'),
    (r'^\s*(public\s+)?interface\s+I\w+', 'interface declaration'), (r'\b(public|internal)\s+(sealed\s+)?record\s+\w+\s*\(', 'DTO / record contract'),
    (r'\bJson(Serializer|PropertyName|Ignore|Converter)\b|\[Required|\[StringLength|\[Range|IValidatableObject|Validator\b', 'serialization / validation'),
    (r'\b(Assembly\.|Activator\.|Type\.GetType|GetMethod\(|GetProperty\(|Reflection)', 'reflection / dynamic loading'),
    (r'\bOnModelCreating|HasIndex|HasKey|ToTable|HasData|EntityTypeBuilder', 'EF configuration'),
]
SMOKE_PREFIXES = ['Patient', 'Appointment', 'Billing', 'Document', 'Prescription', 'Completion', 'Backup']
GUARD = re.compile(r'(Guard|Inventory|Coexistence|RouteScan|Architecture|NoDirectDb|Catalog)')


def classify(P, I):
    """-> (decision, reasons, changed_cs, status_rows). decision is 'FORCED_FULL' or 'SELECT'."""
    rows = [r.split('\t') for r in git('diff', '-M', '--name-status', P, I).splitlines() if r.strip()]
    reasons = []
    cs_changed = []
    for r in rows:
        st, paths = r[0], r[1:]
        for p in paths:
            if p.startswith(NOT_BACKEND) or p in NOT_BACKEND_EXACT:
                continue
            if not (p.startswith('src/Alveara.Api/') or p.startswith('src/Alveara.Api.Tests/')):
                reasons.append((p, 'an unclassified path outside the known non-backend areas'))
                continue
            if st[0] in 'ADRC':
                reasons.append((p, 'new / deleted / renamed / copied file (%s)' % st[0]))
            if not p.endswith('.cs'):
                reasons.append((p, 'a non-C# file in a backend project'))
                continue
            for pat, why in FORCE_PATH:
                if re.search(pat, p):
                    reasons.append((p, why))
            if st[0] == 'M':
                cs_changed.append(p)
                diff = git('diff', '-U0', '-M', P, I, '--', p)
                for line in diff.splitlines():
                    if line[:1] in '+-' and not line.startswith(('+++', '---')):
                        for pat, why in FORCE_CONTENT:
                            if re.search(pat, line[1:]):
                                reasons.append((p, why + ' (a changed line)'))
                                break
    seen = set()
    uniq = []
    for p, w in reasons:
        if (p, w) not in seen:
            seen.add((p, w))
            uniq.append((p, w))
    return ('FORCED_FULL' if uniq else 'SELECT'), uniq, sorted(set(cs_changed)), rows


def select(P, I, ids_I, changed_cs):
    """Selected test classes from the HISTORICAL model of both trees; None if the mapping is ambiguous/incomplete (then the caller forces a full run)."""
    mI, mP = model(I), model(P)
    all_classes = sorted({klass(i) for i in ids_I})
    unmapped = [c for c in all_classes if c not in mI['classes']]
    ambiguous = [c for c in all_classes if len(mI['classes'].get(c, ())) > 1]
    if unmapped or ambiguous:
        return None, dict(unmapped=unmapped, ambiguous=ambiguous)
    changed = set(changed_cs)
    # fail closed: a changed production file that no test class reaches in the graph may still be reached by reflection / DI / assembly scanning (controllers, hosted services...)
    reached = set()
    for c in all_classes:
        f = next(iter(mI['classes'][c]))
        reached |= mI['closure'](f)
    unreached = sorted(f for f in changed if f.startswith(PROD) and f in mI['files'] and f not in reached)
    if unreached:
        return None, dict(unmapped=[], ambiguous=[], unreached=unreached[:20])
    sel = {}
    for c in all_classes:
        f = next(iter(mI['classes'][c]))
        hits = mI['closure'](f) & changed
        # a changed file that existed in P but whose reverse reach differed in P is covered by taking the union of both trees' closures
        if not hits and f in mP['files']:
            hits = mP['closure'](f) & changed
        if hits:
            sel[c] = 'reaches changed ' + ', '.join(sorted(h.split('/')[-1] for h in hits)[:3])
    # always included: changed test classes, guard classes, the deterministic smoke set
    for f in changed:
        if f.startswith(TESTS):
            for n in TYPEDEF.findall(blob(I, f)):
                if n in all_classes:
                    sel.setdefault(n, 'its test file changed')
    for c in all_classes:
        if GUARD.search(c):
            sel.setdefault(c, 'guard / inventory class (always included)')
    for pre in SMOKE_PREFIXES:
        cand = [c for c in all_classes if c.startswith(pre)]
        if cand:
            sel.setdefault(cand[0], 'smoke set (first class alphabetically with prefix %s)' % pre)
    return sel, dict(unmapped=[], ambiguous=[])


def evaluate(I_run, P_run, P_rows, I_rows):
    P, I = P_run['commit'], I_run['commit']
    decision, reasons, changed_cs, status_rows = classify(P, I)
    ids_I = [r[0] for r in I_rows]
    ids_P = {r[0]: r[1] for r in P_rows}
    out = dict(run=I_run['id'], run_commit=I, baseline=P_run['id'], baseline_commit=P, tests=len(I_rows), duplicates=len(ids_I) - len(set(ids_I)),
               changed_files=len(status_rows), changed_cs_modified=changed_cs, forced_full_reasons=reasons[:60], forced_full_reason_count=len(reasons))
    failed = [r for r in I_rows if r[1] == 'Failed']
    flipped = [r[0] for r in I_rows if r[1] == 'Failed' and ids_P.get(r[0]) == 'Passed']
    new_failed = [r[0] for r in I_rows if r[1] == 'Failed' and r[0] not in ids_P]
    out.update(failed_in_run=[r[0] for r in failed], flipped_passed_to_failed=flipped, failed_new_tests=new_failed)
    sel = None
    if decision == 'SELECT':
        sel, amb = select(P, I, ids_I, changed_cs)
        if sel is None:
            decision = 'FORCED_FULL'
            out['forced_full_reasons'].append(('(mapping)', 'test classes unmapped or ambiguous in the historical tree: %s' % json.dumps(amb)))
    out['decision'] = decision
    if decision == 'SELECT':
        selected_ids = [i for i in ids_I if klass(i) in sel]
        reused_ids = [i for i in ids_I if klass(i) not in sel]
        out.update(selected_classes=len(sel), selected_tests=len(selected_ids), reused_tests=len(reused_ids),
                   selection={c: w for c, w in sorted(sel.items())},
                   reconciliation=dict(selected_plus_reused=len(selected_ids) + len(reused_ids), discovered=len(ids_I), exact=len(selected_ids) + len(reused_ids) == len(set(ids_I)) and not set(selected_ids) & set(reused_ids)))
        dur = {r[0]: r[2] for r in I_rows}
        tot = sum(dur.values())
        out['estimate'] = dict(selected_share_of_test_time=round(sum(dur[i] for i in selected_ids) / tot, 4) if tot else None, sum_of_test_durations_min=round(tot / 60, 1),
                               wall_min_of_the_full_run=I_run.get('wall_min'))
        reused_failed = [r[0] for r in failed if klass(r[0]) not in sel]
        out['misses'] = reused_failed
        out['reused_ids_missing_or_not_passed_in_baseline'] = [i for i in reused_ids if ids_P.get(i) != 'Passed'][:30]
    else:
        out['misses'] = []
    return out


def relaxed(I_run, P_run, P_rows, I_rows):
    """DIAGNOSTIC ONLY (not the rule): graph-only selection with every forced-full surface rule switched OFF, to see whether the type-reference graph by itself would have
    covered the tests that failed in I. New/deleted C# files are treated as changed files (closure taken in both trees). Never evidence that a test is safe to omit."""
    P, I = P_run['commit'], I_run['commit']
    rows = [r.split('	') for r in git('diff', '-M', '--name-status', P, I).splitlines() if r.strip()]
    changed = set()
    for r in rows:
        for p in r[1:]:
            if p.endswith('.cs') and (p.startswith(PROD) or p.startswith(TESTS)):
                changed.add(p)
    ids_I = [x[0] for x in I_rows]
    sel, amb = select(P, I, ids_I, sorted(changed))
    if sel is None:
        return dict(run=I_run['id'], baseline=P_run['id'], note='mapping incomplete', **amb)
    dur = {x[0]: x[2] for x in I_rows}
    tot = sum(dur.values())
    selected = [i for i in ids_I if klass(i) in sel]
    failed = [x[0] for x in I_rows if x[1] == 'Failed']
    reused = [i for i in ids_I if klass(i) not in sel]
    rec = dict(selected_plus_reused=len(selected) + len(reused), discovered=len(ids_I), duplicates=len(ids_I) - len(set(ids_I)), exact=len(selected) + len(reused) == len(set(ids_I)) and not set(selected) & set(reused))
    return dict(run=I_run['id'], baseline=P_run['id'], reconciliation=rec, selection=dict(sorted(sel.items())), changed_cs_files=len(changed), selected_classes=len(sel), selected_tests=len(selected), tests=len(ids_I),
                selected_share_of_tests=round(len(selected) / len(ids_I), 3), selected_share_of_test_time=round(sum(dur[i] for i in selected) / tot, 3) if tot else None,
                failed_in_run=failed, failed_outside_selection=[f for f in failed if klass(f) not in sel])


def profile(limit=None):
    """APPLICABILITY PROFILE (not safety evidence): for each commit that touches src/Alveara.Api*, classify its own diff against its parent with the fail-closed rule."""
    commits = git('log', '--format=%H %s', '--', 'src/Alveara.Api', 'src/Alveara.Api.Tests').splitlines()
    out = []
    for line in commits[:limit]:
        c, subject = line.split(' ', 1)
        parents = git('rev-list', '--parents', '-n', '1', c).split()[1:]
        if len(parents) != 1:
            continue
        decision, reasons, cs, rows = classify(parents[0], c)
        out.append(dict(commit=c[:7], subject=subject[:80], decision=decision, reasons=sorted({w for _, w in reasons})[:4], changed_files=len(rows)))
    return out


def main():
    runs = []
    for r in RUNS:
        rows = load_result(r['result'])
        runs.append((r, rows))
    results = []
    last_ok = None
    for r, rows in runs:
        failed = sum(1 for x in rows if x[1] == 'Failed')
        if last_ok is not None:
            ev = evaluate(r, last_ok[0], last_ok[1], rows)
            results.append(ev)
            json.dump(ev, open(os.path.join(OUT, 'eval_%s.json' % r['id'].replace(' ', '_')), 'w', encoding='utf-8'), indent=1)
        if failed == 0:
            last_ok = (r, rows)
    rel = []
    last = None
    for r, rows in runs:
        failed = sum(1 for x in rows if x[1] == 'Failed')
        if last is not None:
            rel.append(relaxed(r, last[0], last[1], rows))
        if failed == 0:
            last = (r, rows)
    json.dump(rel, open(os.path.join(OUT, 'relaxed_diagnostic.json'), 'w', encoding='utf-8'), indent=1)
    prof = profile()
    json.dump(prof, open(os.path.join(OUT, 'applicability_profile.json'), 'w', encoding='utf-8'), indent=1)
    c = collections.Counter(x['decision'] for x in prof)
    print('PROFILE', dict(c), 'of', len(prof), 'single-parent commits touching src/Alveara.Api*')
    print('RELAXED', json.dumps([{k: v for k, v in x.items() if k in ('run','selected_share_of_tests','selected_share_of_test_time','failed_in_run','failed_outside_selection')} for x in rel], indent=0)[:1800])
    summary = [dict(run=e['run'], baseline=e['baseline'], decision=e['decision'], tests=e['tests'], changed_files=e['changed_files'], forced_reasons=e['forced_full_reason_count'],
                    selected_tests=e.get('selected_tests'), reused_tests=e.get('reused_tests'), failed=len(e['failed_in_run']), flipped=len(e['flipped_passed_to_failed']), misses=len(e['misses']),
                    reconciled=e.get('reconciliation', {}).get('exact')) for e in results]
    json.dump(summary, open(os.path.join(OUT, 'summary.json'), 'w', encoding='utf-8'), indent=1)
    for s in summary:
        print(s)


if __name__ == '__main__':
    main()
