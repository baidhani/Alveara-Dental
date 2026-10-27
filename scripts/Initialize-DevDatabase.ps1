<#
.SYNOPSIS
  Creates the AlveraDev database (files inside Dev-Data\Database) and applies every migration. Safe to run again.

.DESCRIPTION
  1. Creates the Dev-Data folders if missing.
  2. Creates AlveraDev with explicit data and log files in <root>\Dev-Data\Database, unless it already exists, and makes sure READ_COMMITTED_SNAPSHOT is on
     (what EF Core's creator sets and the application's concurrency depends on; an existing database without it is repaired, which resets its connections).
  3. Builds the API and applies the migrations (dotnet ef database update). With nothing new to apply this changes nothing.

  -Reset first DROPS AlveraDev and its files (every user, patient and record in it is lost), then recreates it. It asks for confirmation;
  -Confirm:$false skips the question. Nothing else is ever dropped: the name is fixed to AlveraDev.

.EXAMPLE
  .\scripts\Initialize-DevDatabase.ps1
.EXAMPLE
  .\scripts\Initialize-DevDatabase.ps1 -Reset
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$Reset
)

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

foreach ($dir in $DevDatabaseDir, $DevStorageDir, $DevKeysDir, $DevBackupDir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$exists = (Invoke-DevSql "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$DevDatabaseName'" | Select-Object -First 1).Trim() -eq '1'

if ($Reset -and $exists) {
    if ($PSCmdlet.ShouldProcess("$DevDatabaseName on $SqlServer", 'DROP DATABASE and delete its files (all dev data is lost)')) {
        Invoke-DevSql "ALTER DATABASE [$DevDatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DevDatabaseName];" | Out-Null
        Write-Host "Dropped $DevDatabaseName."
        $exists = $false
    }
    else {
        Write-Host 'Reset cancelled; nothing was changed.'
        return
    }
}

if (-not $exists) {
    $data = (Join-Path $DevDatabaseDir "$DevDatabaseName.mdf").Replace("'", "''")
    $log  = (Join-Path $DevDatabaseDir "${DevDatabaseName}_log.ldf").Replace("'", "''")
    Invoke-DevSql "CREATE DATABASE [$DevDatabaseName] ON (NAME = N'${DevDatabaseName}_data', FILENAME = N'$data') LOG ON (NAME = N'${DevDatabaseName}_log', FILENAME = N'$log');" | Out-Null
    Write-Host "Created $DevDatabaseName in $DevDatabaseDir"
}
else {
    Write-Host "$DevDatabaseName already exists; keeping its data."
}

# READ_COMMITTED_SNAPSHOT ON is what EF Core's own database creator sets. The application's concurrency depends on it (without it, concurrent inserts that a trigger reads
# deadlock), so a database created with explicit file paths must be given it too, and an older database that lacks it is repaired. Idempotent.
$rcsi = (Invoke-DevSql "SET NOCOUNT ON; SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'$DevDatabaseName'" | Select-Object -First 1).Trim()
if ($rcsi -ne '1') {
    Invoke-DevSql "ALTER DATABASE [$DevDatabaseName] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;" | Out-Null
    Write-Host "Turned READ_COMMITTED_SNAPSHOT on for $DevDatabaseName (connections to it were reset)."
}

# ALLOW_SNAPSHOT_ISOLATION ON lets the all-patients export (ALV-018-C01) read every table at one moment, so the package cannot show a half-finished change. Nothing else uses it,
# and the export checks the setting and falls back to reading table by table when it is off, so this is an improvement, not a requirement. It adds no behaviour to ordinary
# queries and, with READ_COMMITTED_SNAPSHOT already on, almost no cost. Idempotent; it waits for open transactions rather than resetting connections.
$snapshot = (Invoke-DevSql "SET NOCOUNT ON; SELECT snapshot_isolation_state FROM sys.databases WHERE name = N'$DevDatabaseName'" | Select-Object -First 1).Trim()
if ($snapshot -ne '1') {
    Invoke-DevSql "ALTER DATABASE [$DevDatabaseName] SET ALLOW_SNAPSHOT_ISOLATION ON;" | Out-Null
    Write-Host "Turned ALLOW_SNAPSHOT_ISOLATION on for $DevDatabaseName."
}

$apiProject = Join-Path $RepoRoot 'src\Alveara.Api'
Push-Location -LiteralPath $apiProject
try {
    # Windows PowerShell treats text a native command writes to its error stream (a build warning) as an error when errors stop the script; the exit code decides instead.
    $ErrorActionPreference = 'Continue'
    & dotnet build --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & dotnet ef database update --no-build --connection $DevConnectionString
    if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed.' }
}
finally {
    Pop-Location
}

Write-Host "$DevDatabaseName is ready on $SqlServer."
