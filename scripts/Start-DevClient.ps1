<#
.SYNOPSIS
  Starts the web client (Vite dev server) on http://localhost:5173. Runs in the foreground; stop it with Ctrl+C.

.DESCRIPTION
  The client forwards /api calls to the API on port 5072, so start Start-DevApi.ps1 as well. Installs the npm dependencies the first time.
#>
[CmdletBinding()]
param()

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

if (Test-PortInUse 5173) {
    throw 'Port 5173 is already in use: a client is already running. Stop it (Ctrl+C in its terminal) and try again.'
}

Push-Location -LiteralPath (Join-Path $RepoRoot 'src\alveara-client')
try {
    # Windows PowerShell treats text a native command writes to its error stream (a build warning) as an error when errors stop the script; the exit code decides instead.
    $ErrorActionPreference = 'Continue'
    if (-not (Test-Path -LiteralPath 'node_modules')) {
        & npm install
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' }
    }
    Write-Host "Client -> $ClientUrl   (API expected on $ApiUrl)"
    & npm run dev -- --port 5173
}
finally { Pop-Location }
