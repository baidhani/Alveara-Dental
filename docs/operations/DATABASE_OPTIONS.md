# Required database options

Options of the SQL Server database that Alveara relies on. They are set when the database is created and checked on every install or upgrade. The application never changes them itself: it reports what it finds, and an administrator (or the installer) sets them.

| Option | Needed? | Why | Set by today |
|---|---|---|---|
| `READ_COMMITTED_SNAPSHOT ON` | **Required** | Concurrent inserts that a trigger reads deadlock without it. EF Core's own database creator sets it; a database created with explicit file paths must be given it. | `scripts/Initialize-DevDatabase.ps1`, the test database creation SQL |
| `ALLOW_SNAPSHOT_ISOLATION ON` | **Recommended** | The all-patients export (ALV-018-C01) reads every table inside one snapshot transaction, so the package shows one consistent moment. Without it the export still works: it reads table by table and the manifest says so (`readConsistency: TableByTable`). | `scripts/Initialize-DevDatabase.ps1` (dev only) |

## Checking

```sql
SELECT name, is_read_committed_snapshot_on, snapshot_isolation_state_desc FROM sys.databases WHERE name = N'<database>';
-- expect is_read_committed_snapshot_on = 1 and snapshot_isolation_state_desc = ON
```

## Turning on `ALLOW_SNAPSHOT_ISOLATION`

```sql
ALTER DATABASE [<database>] SET ALLOW_SNAPSHOT_ISOLATION ON;
```

- Safe with data in the database: it rewrites nothing and changes nothing for queries that do not ask for a snapshot. It is a one-time, per-database change that travels with backups and restores.
- It waits for transactions that are open at that moment, so run it at a quiet time, and take a backup first as for any database-level change.
- It needs the right to alter the database, so an administrator runs it; the application does not.
- It is **not** `READ_COMMITTED_SNAPSHOT`, which changes how every query behaves and is already on.

## For the Windows installation story (ALV-N013)

The first-time install must create the production database with both options on, and the upgrade preflight must verify them (and repair a missing `ALLOW_SNAPSHOT_ISOLATION` with the command above, after the verified backup the story already requires). Until that story ships, a production database gets `ALLOW_SNAPSHOT_ISOLATION` only if an administrator runs the command by hand.
