<#
.SYNOPSIS
  Creates the Deployment Administrator of a new AlveraDev database through first-run setup (once per database). Safe to run again: a database that is
  already set up is reported, not changed.

.DESCRIPTION
  ALV-N030: the old bootstrap secret and route are gone. This is the dev stand-in for the installer:
    1. issues a one-time setup token with the API's own command (dotnet run -- setup issue-token) against AlveraDev; the token is shown only in this console;
    2. sends it, with the username and password below, to the running API's setup route (POST /api/setup/deployment-administrator).
  The API must be running (.\scripts\Start-DevApi.ps1). The account created is the account-only Deployment Administrator (Administrator access, no staff profile).
  You can also open the setup page in the browser instead of step 2 and type the token there.
#>
[CmdletBinding()]
param(
    [string]$Username = 'admin',
    [string]$Password = 'AlveraDev-Admin-1!'            # dev-only credential for a local database that holds no real data (at least 12 characters)
)

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

$env:ConnectionStrings__Alveara = $DevConnectionString
Push-Location -LiteralPath (Join-Path $RepoRoot 'src\Alveara.Api')
try {
    $ErrorActionPreference = 'Continue'   # build warnings on the error stream must not stop the script; the exit code decides
    $lines = & dotnet run --no-launch-profile -- setup issue-token 2>&1
    $exit = $LASTEXITCODE
}
finally { Pop-Location; $ErrorActionPreference = 'Stop' }

if ($exit -eq 2) { Write-Host 'This database is already set up; nothing was changed.'; return }
if ($exit -ne 0) { throw "The setup token could not be issued (exit $exit). Check that $DevDatabaseName exists and is migrated (.\scripts\Initialize-DevDatabase.ps1)." }
$token = ($lines | ForEach-Object { "$_".Trim() } | Where-Object { $_ -match '^[0-9a-f]{64}$' } | Select-Object -First 1)
if (-not $token) { throw 'The setup command did not print a token.' }

$body = @{ token = $token; username = $Username; password = $Password } | ConvertTo-Json
try {
    $created = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/setup/deployment-administrator" -ContentType 'application/json' -Body $body -TimeoutSec 30
    Write-Host "Created the Deployment Administrator '$($created.username)'."
}
catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    if ($status -eq 0) { throw "Could not reach the API at $ApiUrl. Start it first: .\scripts\Start-DevApi.ps1" }
    $detail = ''
    if ($_.Exception.Response) { $detail = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() }
    throw "Setup failed (HTTP $status): $detail"
}
