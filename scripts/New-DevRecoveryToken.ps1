<#
.SYNOPSIS
  Issues a Deployment Administrator recovery token for AlveraDev and prints it (ALV-N030-C01, brief 18.6). Dev stand-in for the installer's command.

.DESCRIPTION
  Runs the API's own command `recovery issue-token` against AlveraDev. The token is shown once, only in this console, and is valid for 15 minutes; issuing a new one
  cancels the previous one. Type it into the recovery page (http://localhost:5173/recovery) or send it to POST /api/recovery/deployment-administrator with the
  Deployment Administrator's username (or any new username when the installation has none) and a new password of at least 12 characters.
  The installation must already be set up; before setup use .\scripts\New-DevAdmin.ps1 (first-run setup) instead.
#>
[CmdletBinding()]
param()

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

$env:ConnectionStrings__Alveara = $DevConnectionString
Push-Location -LiteralPath (Join-Path $RepoRoot 'src\Alveara.Api')
try {
    $ErrorActionPreference = 'Continue'   # build warnings on the error stream must not stop the script; the exit code decides
    & dotnet run --no-launch-profile -- recovery issue-token
    $exit = $LASTEXITCODE
}
finally { Pop-Location; $ErrorActionPreference = 'Stop' }
if ($exit -eq 2) { Write-Host 'This database is not set up yet; run .\scripts\New-DevAdmin.ps1 (first-run setup) instead.' }
exit $exit
