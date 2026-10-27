<#
.SYNOPSIS
  Runs ONE real-backend browser walkthrough on its own fresh database, then removes everything it created.

.DESCRIPTION
  1. Creates a uniquely named database (AlveraWalk_<name>_<id>) with explicit files in <root>\Test-Data\Database and applies every migration.
  2. Starts the real API on http://localhost:5072 with that database and with its storage, key ring and backup folders under <root>\Test-Data\.
  3. Runs `npx playwright test --config=<Config>` from src\alveara-client (the config starts the Vite dev server itself).
  4. ALWAYS, even when the walkthrough fails or this script is interrupted: stops the API, drops the database (its files go with it) and deletes the folders it made.
  The exit code is the walkthrough's own (0 = all passed). It never touches Dev-Data or AlveraDev.

.PARAMETER Config
  The Playwright config file, relative to src\alveara-client (for example playwright.billing.config.ts or gate-c/playwright.c2.config.ts).

.PARAMETER OutputVariable
  The environment variable the spec reads for its output folder (for example BILLING_E2E_OUT). Optional: without it the spec uses its own default.

.PARAMETER OutputDir
  Where that output goes. Default: <root>\Test-Data\Temp\walkthrough-output\<database>. The folder is KEPT after the run (it is the evidence); delete it when you no longer need it.

.PARAMETER SkipBuild
  Do not rebuild the API first (use when it was just built).

.EXAMPLE
  .\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.procedures.config.ts -OutputVariable PROCEDURES_E2E_OUT
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_./-]+\.ts$')][string]$Config,
    [ValidatePattern('^[A-Z][A-Z0-9_]*$')][string]$OutputVariable,
    [string]$OutputDir,
    [switch]$SkipBuild
)

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

$TestData     = Join-Path $DataRoot 'Test-Data'
$TestDbDir    = Join-Path $TestData 'Database'
$id           = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$short        = ([IO.Path]::GetFileNameWithoutExtension($Config) -replace '[^A-Za-z0-9]', '_')
if ($short.Length -gt 40) { $short = $short.Substring(0, 40) }
$Database     = "AlveraWalk_${short}_$id"
$secret       = 'e2e-real-backend-secret'
$apiProject   = Join-Path $RepoRoot 'src\Alveara.Api'
$clientDir    = Join-Path $RepoRoot 'src\alveara-client'
$runFolders   = @('Storage', 'Keys', 'Backup') | ForEach-Object { Join-Path (Join-Path $TestData $_) "walkthrough-$Database" }
if (-not $OutputDir) { $OutputDir = Join-Path $TestData "Temp\walkthrough-output\$Database" }

if (Test-PortInUse 5072) { throw 'Port 5072 is already in use (a dev API?). Stop it first; a walkthrough needs the port.' }
if ($Database -notmatch '^[A-Za-z0-9_]{1,100}$') { throw "Unsafe database name '$Database'." }

New-Item -ItemType Directory -Force -Path $TestDbDir | Out-Null
foreach ($f in $runFolders) { New-Item -ItemType Directory -Force -Path $f | Out-Null }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$api = $null
$exitCode = 1
try {
    $mdf = (Join-Path $TestDbDir "$Database.mdf").Replace("'", "''")
    $ldf = (Join-Path $TestDbDir "${Database}_log.ldf").Replace("'", "''")
    Invoke-DevSql "CREATE DATABASE [$Database] ON (NAME = N'${Database}_data', FILENAME = N'$mdf') LOG ON (NAME = N'${Database}_log', FILENAME = N'$ldf'); ALTER DATABASE [$Database] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [$Database] SET ALLOW_SNAPSHOT_ISOLATION ON;" | Out-Null     # RCSI on, as EF Core's creator sets it; snapshot isolation on, as a production database has it (ALV-018-C01: the all-patients export reads one snapshot)
    Write-Host "Database $Database created in $TestDbDir"

    $connection = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
    Push-Location -LiteralPath $apiProject
    try {
        # Windows PowerShell treats text a native command writes to its error stream (a build warning) as an error when errors stop the script; the exit code decides instead.
        $ErrorActionPreference = 'Continue'
        if (-not $SkipBuild) { & dotnet build --nologo -v q; if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' } }
        & dotnet ef database update --no-build --connection $connection
        if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed.' }
    }
    finally { $ErrorActionPreference = 'Stop'; Pop-Location }

    $dll = Join-Path $apiProject 'bin\Debug\net10.0\Alveara.Api.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw "The built API was not found at $dll." }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = '"' + $dll + '"'
    $psi.WorkingDirectory = $apiProject
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $psi.EnvironmentVariables['ASPNETCORE_URLS'] = $ApiUrl
    $psi.EnvironmentVariables['ConnectionStrings__Alveara'] = $connection
    $psi.EnvironmentVariables['AdminBootstrapSecret'] = $secret
    $psi.EnvironmentVariables['AuthAttemptRateLimit__PermitLimit'] = '500'
    $psi.EnvironmentVariables['FollowUps__ReminderPollSeconds'] = '3'   # STORY-009: the follow-up reminder pass runs every 3 s here (60 s by default) so a walkthrough sees a reminder promptly
    $psi.EnvironmentVariables['FollowUps__WorkPollSeconds'] = '3'   # ALV-009-C01: the recall and unscheduled-treatment passes run every 3 s here (300 s by default)
    $psi.EnvironmentVariables['StorageRoot'] = $runFolders[0]
    $psi.EnvironmentVariables['DataProtectionKeysPath'] = $runFolders[1]
    $psi.EnvironmentVariables['Backup__Root'] = $runFolders[2]
    $api = [System.Diagnostics.Process]::Start($psi)

    $healthy = $false
    for ($i = 0; $i -lt 40 -and -not $healthy; $i++) {
        if ($api.HasExited) { throw "The API exited at start-up (exit code $($api.ExitCode))." }
        try { $healthy = (Invoke-WebRequest "$ApiUrl/api/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $healthy) { throw 'The API did not become healthy in time.' }
    Write-Host "API healthy on $ApiUrl; running $Config ..."

    $env:E2E_DB_NAME = $Database
    $env:E2E_BOOTSTRAP_SECRET = $secret
    $env:ALVERA_SQL_SERVER = $SqlServer
    if ($OutputVariable) { Set-Item -Path "Env:$OutputVariable" -Value $OutputDir }
    Push-Location -LiteralPath $clientDir
    try {
        $ErrorActionPreference = 'Continue'
        & npx playwright test "--config=$Config"
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = 'Stop'; Pop-Location }
}
finally {
    $ErrorActionPreference = 'Continue'
    Remove-Item Env:E2E_DB_NAME, Env:E2E_BOOTSTRAP_SECRET -ErrorAction SilentlyContinue
    if ($OutputVariable) { Remove-Item "Env:$OutputVariable" -ErrorAction SilentlyContinue }
    if ($api -and -not $api.HasExited) {
        try { Stop-Process -Id $api.Id -Force } catch { Write-Warning "Could not stop the API process $($api.Id): $_" }
        [void]$api.WaitForExit(15000)
    }
    # The drop retries: the API's last pooled connections close a moment after the process ends.
    $dropped = $false
    for ($i = 1; $i -le 5 -and -not $dropped; $i++) {
        try {
            Invoke-DevSql "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END" | Out-Null
            $dropped = $true
        }
        catch { Start-Sleep -Seconds 2 }
    }
    if ($dropped) { Write-Host "Database $Database dropped (its files went with it)." }
    else { Write-Warning "Could not drop $Database; remove it by hand: DROP DATABASE [$Database]" }
    foreach ($f in $runFolders) { Remove-Item -LiteralPath $f -Recurse -Force -ErrorAction SilentlyContinue }
    if (@(Get-ChildItem -LiteralPath $OutputDir -Force -ErrorAction SilentlyContinue).Count -eq 0) {
        Remove-Item -LiteralPath $OutputDir -Force -ErrorAction SilentlyContinue      # a run that produced nothing leaves no empty folder
        Write-Host 'The run produced no output files.'
    }
    else { Write-Host "Output kept in $OutputDir" }
}

exit $exitCode
