import subprocess, os, re

R = r'C:\Users\firas\Documents\AI-Project-[Alveara-Dental]\src'
T = R + r'\Alveara.Api.Tests'
FILTER = 'FullyQualifiedName~RealMigrationInventoryTests'


def run(cmd):
    return subprocess.run(cmd, shell=True, capture_output=True, text=True, encoding='utf-8', errors='replace', cwd=R)


def read(p):
    b = open(p, 'rb').read()
    return b, b.decode('utf-8-sig')


# (name, file, [(search, replace), ...])
MUTANTS = [
    ("a rollback class is marked as using the real path again", 'DocumentSchemaTests.cs', [('\npublic class DocumentSchemaTests', '\n[RealMigrations]\npublic class DocumentSchemaTests')]),
    ("a rollback class is listed in the wrong group (as migrating from an empty database)", 'RealMigrationInventory.cs', [('new(typeof(DocumentSchemaTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true)', 'new(typeof(DocumentSchemaTests), RealMigrationReason.MigratesFromAnEmptyDatabase, true)')]),
    ("the migration-pipeline class loses its real fixture (it would get a template clone)", 'MigrationTests.cs', [('IClassFixture<RealMigrationTestDatabaseFixture>', 'IClassFixture<TestDatabaseFixture>'), ('public MigrationTests(RealMigrationTestDatabaseFixture fixture)', 'public MigrationTests(TestDatabaseFixture fixture)')]),
    ("a rollback class is dropped from the inventory", 'RealMigrationInventory.cs', [('        new(typeof(PerioMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone, true),\n', '')]),
    ("the base class gives every class the real fixture (the attribute check is ignored)", 'SafetyTestBase.cs', [('GetType().IsDefined(typeof(RealMigrationsAttribute), inherit: true) ? new RealMigrationTestDatabaseFixture() : new TestDatabaseFixture();', 'new RealMigrationTestDatabaseFixture();')]),
]

results = []
for name, f, pairs in MUTANTS:
    path = T + '\\' + f
    b, raw = read(path)
    bom = b[:3] == b'\xef\xbb\xbf'
    crlf = '\r\n' in raw
    s = raw.replace('\r\n', '\n')
    ok = True
    for a, c in pairs:
        if s.count(a) != 1:
            ok = False
            results.append((name, 'SEARCH TEXT NOT FOUND exactly once (%d): %s' % (s.count(a), a[:50])))
            break
        s = s.replace(a, c)
    if not ok:
        print(results[-1], flush=True)
        continue
    out = (s.replace('\n', '\r\n') if crlf else s).encode('utf-8')
    try:
        open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out)
        build = run('dotnet build Alveara.Api.Tests --nologo -v q')
        if ' error ' in build.stdout:
            results.append((name, 'DID NOT COMPILE'))
        else:
            r = run('dotnet test Alveara.Api.Tests --no-build --filter "%s" --nologo' % FILTER)
            line = [l.strip() for l in r.stdout.splitlines() if 'Passed!' in l or 'Failed!' in l]
            failed = sorted(set(re.findall(r'^\s+Failed (\S+)', r.stdout, re.M)))
            results.append((name, (line[0][:50] if line else 'no result') + ' | ' + ', '.join(x.split('.')[-1][:55] for x in failed[:3])))
    finally:
        open(path, 'wb').write(b)
    print(results[-1], flush=True)
run('dotnet build Alveara.Api.Tests --nologo -v q')
print('--- summary')
for n, r in results:
    print(('KILLED ' if 'Failed!' in r else 'SURVIVED/OTHER ') + n + ' -> ' + r)
