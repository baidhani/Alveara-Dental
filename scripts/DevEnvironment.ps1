# Shared settings for the dev scripts (Initialize-DevDatabase, New-DevAdmin, Start-DevApi, Start-DevClient).
# Dot-source it (see the first lines of any script here; the repository path contains [ ], so the path must be escaped):
#   $ErrorActionPreference = 'Stop'; . ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))
#
# EVERY database file and every file the dev app writes lives under ONE root, in its Dev-Data subfolder:
#   <root>\Dev-Data\Database   the AlveraDev .mdf and .ldf files
#   <root>\Dev-Data\Storage    uploaded documents (StorageRoot)
#   <root>\Dev-Data\Keys       the data-protection key ring (DataProtectionKeysPath)
#   <root>\Dev-Data\Backup     backups, staging, restore and import folders (Backup:Root)
# Tests use the Test-Data sibling; nothing here touches it.
#
# Two environment variables override the defaults for a machine that differs from the development machine:
#   ALVERA_SQL_SERVER   the SQL Server instance (default .\SQLEXPRESS)
#   ALVERA_DATA_ROOT    the single data root, an absolute path (default C:\.alvera-data)
# The SQL Server service account must be able to read and write the folders under the root.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

$SqlServer = if ($env:ALVERA_SQL_SERVER) { $env:ALVERA_SQL_SERVER } else { '.\SQLEXPRESS' }
$DataRoot  = if ($env:ALVERA_DATA_ROOT)  { $env:ALVERA_DATA_ROOT }  else { 'C:\.alvera-data' }

if (-not [System.IO.Path]::IsPathRooted($DataRoot)) {
    throw "ALVERA_DATA_ROOT must be an absolute path (got '$DataRoot')."
}

$DevData         = Join-Path $DataRoot 'Dev-Data'
$DevDatabaseDir  = Join-Path $DevData 'Database'
$DevStorageDir   = Join-Path $DevData 'Storage'
$DevKeysDir      = Join-Path $DevData 'Keys'
$DevBackupDir    = Join-Path $DevData 'Backup'

# The one database these scripts ever create or drop. Never derived from user input.
$DevDatabaseName = 'AlveraDev'
$DevConnectionString = "Server=$SqlServer;Database=$DevDatabaseName;Trusted_Connection=True;TrustServerCertificate=True"

$ApiUrl    = 'http://localhost:5072'      # fixed: the client's dev proxy (vite.config.ts) and Properties\launchSettings.json expect it
$ClientUrl = 'http://localhost:5173'

function Test-PortInUse([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Invoke-DevSql([string]$Sql, [string]$Database = 'master') {
    # -E = Windows authentication, -b = stop with a non-zero exit code on a SQL error. The statement text never contains user input.
    $ErrorActionPreference = 'Continue'    # the exit code decides, not text on the error stream
    $out = & sqlcmd -S $SqlServer -d $Database -E -b -h -1 -W -Q $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "SQL failed on $SqlServer : $($out -join ' ')" }
    return $out
}
