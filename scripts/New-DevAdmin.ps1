<#
.SYNOPSIS
  Creates the first administrator of a new AlveraDev database (once per database). Safe to run again: an existing administrator is reported, not changed.

.DESCRIPTION
  There is no screen for this. It calls the API's one-time bootstrap endpoint with the secret the API was started with (Start-DevApi.ps1
  uses the same default). The API must be running. A second call answers "bootstrap_already_used", which this script treats as success.
  Keep the account's role as Admin; if it is changed, reset the database with Initialize-DevDatabase.ps1 -Reset.
#>
[CmdletBinding()]
param(
    [string]$Username = 'admin',
    [string]$Password = 'AlveraDev-Admin-1!',            # dev-only credential for a local database that holds no real data
    [string]$BootstrapSecret = 'dev-bootstrap-secret'    # must match the API's AdminBootstrapSecret
)

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

$body = @{ username = $Username; password = $Password; secret = $BootstrapSecret } | ConvertTo-Json
try {
    $created = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/auth/bootstrap-admin" -ContentType 'application/json' -Body $body -TimeoutSec 30
    Write-Host "Created administrator '$($created.username)' with role $($created.role)."
}
catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    $detail = ''
    if ($_.Exception.Response) { $detail = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() }
    if ($status -eq 409) { Write-Host 'The first administrator already exists for this database; nothing was changed.'; return }
    if ($status -eq 401) { throw "The API refused the bootstrap secret ($detail). Start the API with the same secret you pass here." }
    if ($status -eq 0)   { throw "Could not reach the API at $ApiUrl. Start it first: .\scripts\Start-DevApi.ps1" }
    throw "Bootstrap failed (HTTP $status): $detail"
}
