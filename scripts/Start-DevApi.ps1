<#
.SYNOPSIS
  Starts the API against AlveraDev with every file location inside Dev-Data. Runs in the foreground; stop it with Ctrl+C.

.DESCRIPTION
  Sets the settings the API reads from the environment of THIS terminal only:
    ConnectionStrings__Alveara   -> AlveraDev on the configured SQL Server (Windows authentication)
    StorageRoot / DataProtectionKeysPath / Backup__Root -> <root>\Dev-Data\Storage, Keys, Backup
    AdminBootstrapSecret         -> the one-time secret used by New-DevAdmin.ps1
    AuthAttemptRateLimit__PermitLimit -> 500 (so signing in as several users in a row is not locked out)
  Then runs dotnet run, which uses the launch profile: http://localhost:5072. Run Initialize-DevDatabase.ps1 first.
#>
[CmdletBinding()]
param(
    [string]$BootstrapSecret = 'dev-bootstrap-secret'   # dev-only; the same value New-DevAdmin.ps1 sends
)

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

if (Test-PortInUse 5072) {
    throw 'Port 5072 is already in use: an API is already running. Stop it (Ctrl+C in its terminal) and try again.'
}

$exists = (Invoke-DevSql "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$DevDatabaseName'" | Select-Object -First 1).Trim() -eq '1'
if (-not $exists) { throw "$DevDatabaseName does not exist on $SqlServer. Run .\scripts\Initialize-DevDatabase.ps1 first." }

foreach ($dir in $DevStorageDir, $DevKeysDir, $DevBackupDir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$env:ConnectionStrings__Alveara        = $DevConnectionString
$env:StorageRoot                       = $DevStorageDir
$env:DataProtectionKeysPath            = $DevKeysDir
$env:Backup__Root                      = $DevBackupDir
$env:AdminBootstrapSecret              = $BootstrapSecret
$env:AuthAttemptRateLimit__PermitLimit = '500'

Write-Host "API -> $ApiUrl   database $DevDatabaseName on $SqlServer   files under $DevData"
Push-Location -LiteralPath (Join-Path $RepoRoot 'src\Alveara.Api')
try {
    # Windows PowerShell treats text a native command writes to its error stream (a build warning) as an error when errors stop the script; the exit code decides instead.
    $ErrorActionPreference = 'Continue'
    & dotnet run
}
finally { Pop-Location }
