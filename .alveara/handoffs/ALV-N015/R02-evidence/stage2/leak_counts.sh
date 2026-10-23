#!/bin/sh
# usage: leak_counts.sh <label>; the artifact counts Stage 2 compares before and after (SQL Server .\SQLEXPRESS and the Test-Data folders under C:\.alvera-data).
echo "== artifact counts: $1 $(date -Iseconds)"
sqlcmd -S ".\SQLEXPRESS" -E -h -1 -W -Q "SET NOCOUNT ON; SELECT 'databases_total', COUNT(*) FROM sys.databases WHERE database_id>4; SELECT 'AlveraTest', COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraTest[_]%'; SELECT 'AlveraTemplate', COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraTemplate[_]%'; SELECT 'AlveraRestore', COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraRestore[_]%'; SELECT 'sql_logins', COUNT(*) FROM sys.server_principals WHERE type='S' AND name NOT IN ('sa') AND name NOT LIKE '##%';"
for d in Storage Keys Backup Database Temp; do printf "folder_%s " "$d"; ls "/c/.alvera-data/Test-Data/$d" 2>/dev/null | wc -l; done
printf "template_backups_in_Temp "; ls /c/.alvera-data/Test-Data/Temp 2>/dev/null | grep -c "^AlveraTemplate_.*\.bak$"
