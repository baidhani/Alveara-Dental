import subprocess, os, re

R = r'C:\Users\firas\Documents\AI-Project-[Alveara-Dental]\src'
T = R + r'\Alveara.Api.Tests'
FILTER = 'FullyQualifiedName~TestDatabaseTemplateTests|FullyQualifiedName~RealMigrationInventoryTests'


def run(cmd):
    return subprocess.run(cmd, shell=True, capture_output=True, text=True, encoding='utf-8', errors='replace', cwd=R)


def read(p):
    return open(p, encoding='utf-8', newline='').read()


def write(p, s):
    open(p, 'w', encoding='utf-8', newline='').write(s)


# (name, file, search, replace)
MUTANTS = [
    ("the logical file names are not read from the backup (wrong names)", 'TestDatabaseTemplate.cs', 'return new TemplateInfo(backup, data, log, expected.ToList());', 'return new TemplateInfo(backup, data + "_wrong", log, expected.ToList());'),
    ("the migration history is not checked against the code", 'TestDatabaseTemplate.cs', 'if (!applied.SequenceEqual(expected))', 'if (false)'),
    ("a stale template is reused when the code's migrations changed", 'TestDatabaseTemplate.cs', 'if (_info is not null && _info.Migrations.SequenceEqual(expected)) return _info;', 'if (_info is not null) return _info;'),
    ("the partial clone is not removed after a failed restore", 'TestDatabaseFixture.cs', 'try { await DropDatabaseAsync(onlyIfExists: true); }\r\n            catch (Exception dropFailure)', 'try { await Task.CompletedTask; }\r\n            catch (Exception dropFailure)'),
    ("a failed restore is raised instead of falling back", 'TestDatabaseFixture.cs', '            _template!.RecordFallback(_databaseName, reason);\r\n            return false;', '            _template!.RecordFallback(_databaseName, reason);\r\n            throw;'),
    ("a template build failure is turned into a quiet fallback", 'TestDatabaseFixture.cs', '        catch (TemplateBuildException) { throw; }', '        catch (TemplateBuildException) { return false; }'),
    ("the template backup is not removed on dispose", 'TestDatabaseTemplate.cs', 'if (_info is not null) TryDeleteBackup(_info.BackupPath);', ''),
    ("a leftover is deleted instead of only reported", 'TestDatabaseTemplate.cs', 'found.Add($"backup {Path.GetFileName(file)} ({(DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalHours:0.0} h old)");', 'found.Add($"backup {Path.GetFileName(file)} ({(DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalHours:0.0} h old)"); File.Delete(file);'),
    ("the template is built without READ_COMMITTED_SNAPSHOT on the clone side (setting changed after migrate)", 'TestDatabaseTemplate.cs', 'SqlConnection.ClearPool((SqlConnection)db.Database.GetDbConnection());\r\n            }\r\n            migrateMs', 'SqlConnection.ClearPool((SqlConnection)db.Database.GetDbConnection());\r\n            }\r\n            await ExecuteMasterAsync($"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE");\r\n            migrateMs'),
    ("a real-migration class loses its attribute", 'DocumentSchemaTests.cs', '[RealMigrations]\r\npublic class DocumentSchemaTests', 'public class DocumentSchemaTests'),
    ("the base class ignores the real-migration attribute", 'SafetyTestBase.cs', 'GetType().IsDefined(typeof(RealMigrationsAttribute), inherit: true) ? new RealMigrationTestDatabaseFixture() : new TestDatabaseFixture();', 'new TestDatabaseFixture();'),
    ("a migration-calling class is missing from the inventory", 'RealMigrationInventory.cs', '        new(typeof(PerioMigrationTests), RealMigrationReason.RollsTheSchemaBackAndForward, true),\r\n', ''),
    ("telemetry carries the connection string", 'TestDatabaseTemplate.cs', 'context });', 'context, connection = AlveraTestEnvironment.MasterConnectionString });'),
]

results = []
for name, f, a, b in MUTANTS:
    path = T + '\\' + f
    raw = read(path)
    crlf = '\r\n' in raw
    orig = raw
    search = a if crlf else a.replace('\r\n', '\n')
    rep = b if crlf else b.replace('\r\n', '\n')
    if raw.count(search) != 1:
        results.append((name, 'SEARCH TEXT NOT FOUND exactly once (%d)' % raw.count(search)))
        print(results[-1], flush=True)
        continue
    try:
        write(path, raw.replace(search, rep))
        build = run('dotnet build Alveara.Api.Tests --nologo -v q')
        if ' error ' in build.stdout:
            results.append((name, 'DID NOT COMPILE'))
        else:
            r = run('dotnet test Alveara.Api.Tests --no-build --filter "%s" --nologo' % FILTER)
            line = [l.strip() for l in r.stdout.splitlines() if 'Passed!' in l or 'Failed!' in l]
            failed = sorted({m for m in re.findall(r'^\s+Failed (\S+)', r.stdout, re.M)})
            results.append((name, (line[0][:50] if line else 'no result') + ' | ' + ', '.join(x.split('.')[-1][:60] for x in failed[:3])))
    finally:
        write(path, orig)
    print(results[-1], flush=True)
run('dotnet build Alveara.Api.Tests --nologo -v q')
print('--- summary')
for n, r in results:
    print(('KILLED ' if 'Failed!' in r else 'SURVIVED/OTHER ') + n + ' -> ' + r)
