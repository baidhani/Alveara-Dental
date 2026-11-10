<#
.SYNOPSIS
  Runs the accumulated chain (stage 0 today) on its own throw-away environment, writes the evidence, then removes everything it created.

.DESCRIPTION
  ALV-N019 (R05 PKG-01). One run owns: a database AlveraChain_<run id>, and a folder <root>\Test-Data\Chain\<run id> holding its database files, storage,
  key ring and backup. Steps:
    1. refuse if port 5072 or 5173 is busy, then create the database (explicit files inside the run folder) and apply every migration;
    2. start the real API on it (production composition, unchanged; the real clock) and wait for health;
    3. run the stage-0 Playwright spec (administrator bootstrap, wrong/right/second secret, browser sign-in);
    4. take one COPY_ONLY checkpoint backup into the run folder and verify it (a safety net; the runner never resets anything automatically);
    5. ALWAYS, even on failure or interruption: stop the API, drop the database, delete the run folder;
    6. write <root>\Test-Data\Temp\chain-output\<run id>\evidence.json (kept) and exit with its code.
  Every database name and path goes through scripts\chain\ChainSafety.ps1 first. The runner never touches AlveraDev, Dev-Data, real backups or any path outside its run folder.
  Exit: 0 = every step PASS, 1 = any FAIL, SKIPPED_BLOCKED or blocking TRACKED_EXCEPTION, 2 = the runner could not complete its own setup.

.PARAMETER Stage
  The chain stage to run. Only 0 exists.

.PARAMETER SkipBuild
  Do not rebuild the API first (use when it was just built).

.EXAMPLE
  .\scripts\Invoke-AccumulatedChain.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet(0)][int]$Stage = 0,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))
. ([WildcardPattern]::Escape("$PSScriptRoot\chain\ChainSafety.ps1"))

$runId       = New-ChainRunId
$Database    = Get-ChainDatabaseName $runId
$runRoot     = Get-ChainRunRoot $DataRoot $runId
$evidenceDir = Assert-EvidencePath (Join-Path (Join-Path (Join-Path (Join-Path $DataRoot 'Test-Data') 'Temp') 'chain-output') $runId) $DataRoot $runId
$dbDir       = Join-Path $runRoot 'Database'
$storageDir  = Join-Path $runRoot 'Storage'
$keysDir     = Join-Path $runRoot 'Keys'
$backupDir   = Join-Path $runRoot 'Backup'
$secret      = "chain-$runId"
$apiProject  = Join-Path $RepoRoot 'src\Alveara.Api'
$clientDir   = Join-Path $RepoRoot 'src\alveara-client'
$exitCode    = 2
$runnerSteps = New-Object System.Collections.ArrayList
$schemaVersion = 'unknown'

foreach ($port in 5072, 5173) {
    if (Test-PortInUse $port) { Write-Host "Port $port is already in use: stop the dev app first; a chain run needs both ports."; exit 2 }
}
foreach ($dir in $dbDir, $storageDir, $keysDir, $backupDir) { [void](Assert-RunOwnedPath $dir $runRoot $DataRoot); New-Item -ItemType Directory -Force -Path $dir | Out-Null }
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

$sha = (& git -C $RepoRoot rev-parse HEAD).Trim()
if ((& git -C $RepoRoot status --porcelain)) { $sha = "$sha+uncommitted-changes" }

$api = $null
try {
    Assert-ChainDatabase $Database $runId
    $mdf = (Assert-RunOwnedPath (Join-Path $dbDir "$Database.mdf") $runRoot $DataRoot).Replace("'", "''")
    $ldf = (Assert-RunOwnedPath (Join-Path $dbDir "${Database}_log.ldf") $runRoot $DataRoot).Replace("'", "''")
    Invoke-DevSql "CREATE DATABASE [$Database] ON (NAME = N'${Database}_data', FILENAME = N'$mdf') LOG ON (NAME = N'${Database}_log', FILENAME = N'$ldf'); ALTER DATABASE [$Database] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [$Database] SET ALLOW_SNAPSHOT_ISOLATION ON;" | Out-Null
    Write-Host "Chain run ${runId}: database $Database created in $dbDir"

    $connection = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
    Push-Location -LiteralPath $apiProject
    try {
        $ErrorActionPreference = 'Continue'
        if (-not $SkipBuild) { & dotnet build --nologo -v q; if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' } }
        & dotnet ef database update --no-build --connection $connection
        if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed.' }
    }
    finally { $ErrorActionPreference = 'Stop'; Pop-Location }

    $schemaVersion = (Invoke-DevSql "SET NOCOUNT ON; SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC" $Database | Select-Object -First 1).Trim()

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
    $psi.EnvironmentVariables['StorageRoot'] = $storageDir
    $psi.EnvironmentVariables['DataProtectionKeysPath'] = $keysDir
    $psi.EnvironmentVariables['Backup__Root'] = $backupDir
    $api = [System.Diagnostics.Process]::Start($psi)

    $healthy = $false
    for ($i = 0; $i -lt 40 -and -not $healthy; $i++) {
        if ($api.HasExited) { throw "The API exited at start-up (exit code $($api.ExitCode))." }
        try { $healthy = (Invoke-WebRequest "$ApiUrl/api/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $healthy) { throw 'The API did not become healthy in time.' }
    Write-Host "API healthy on $ApiUrl; running stage $Stage ..."

    $env:E2E_DB_NAME = $Database
    $env:E2E_BOOTSTRAP_SECRET = $secret
    $env:ALVERA_SQL_SERVER = $SqlServer
    $env:CHAIN_OUT = $evidenceDir
    Push-Location -LiteralPath $clientDir
    try {
        $ErrorActionPreference = 'Continue'
        & npx playwright test "--config=playwright.chain.config.ts"
        Write-Host "Playwright exit code $LASTEXITCODE (the verdict comes from the evidence builder)."
    }
    finally { $ErrorActionPreference = 'Stop'; Pop-Location }

    # E10: one COPY_ONLY checkpoint (does not disturb the backup chain), kept inside the run folder, verified, removed with the run. Never an automatic reset.
    $bak = Assert-RunOwnedPath (Join-Path $backupDir 'checkpoint-stage0.bak') $runRoot $DataRoot
    try {
        Invoke-DevSql "BACKUP DATABASE [$Database] TO DISK = N'$($bak.Replace("'", "''"))' WITH COPY_ONLY, INIT, CHECKSUM" | Out-Null
        Invoke-DevSql "RESTORE VERIFYONLY FROM DISK = N'$($bak.Replace("'", "''"))' WITH CHECKSUM" | Out-Null
        [void]$runnerSteps.Add(@{ stepId = 'E10'; title = 'E10 COPY_ONLY checkpoint backup is written and verifies'; passed = $true; detail = '' })
    }
    catch { [void]$runnerSteps.Add(@{ stepId = 'E10'; title = 'E10 COPY_ONLY checkpoint backup is written and verifies'; passed = $false; detail = "$_" }) }
}
catch {
    Write-Host "The chain runner could not complete its setup: $_"
    [void]$runnerSteps.Add(@{ stepId = 'RUN'; title = 'RUN the runner completed its own setup'; passed = $false; detail = "$_" })
}
finally {
    $ErrorActionPreference = 'Continue'
    Remove-Item Env:E2E_DB_NAME, Env:E2E_BOOTSTRAP_SECRET, Env:CHAIN_OUT -ErrorAction SilentlyContinue
    if ($api -and -not $api.HasExited) {
        try { Stop-Process -Id $api.Id -Force } catch { Write-Warning "Could not stop the API process $($api.Id): $_" }
        [void]$api.WaitForExit(15000)
    }
    $dropped = $false
    for ($i = 1; $i -le 5 -and -not $dropped; $i++) {
        try {
            Assert-ChainDatabase $Database $runId
            Invoke-DevSql "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END" | Out-Null
            $dropped = $true
        }
        catch { Start-Sleep -Seconds 2 }
    }
    if ($dropped) { Write-Host "Database $Database dropped (its files went with it)." }
    else { Write-Warning "Could not drop $Database; remove it by hand: DROP DATABASE [$Database]" }
    try { Remove-RunOwnedTree $runRoot $runRoot $DataRoot -AllowRunRoot }
    catch { Write-Warning "Could not remove the run folder ${runRoot}: $_" }
    [void]$runnerSteps.Add(@{ stepId = 'CLEAN'; title = 'CLEAN the database is dropped and the run folder is removed'; passed = ($dropped -and -not (Test-Path -LiteralPath $runRoot)); detail = '' })
}

# The evidence builder decides the verdict. A missing Playwright report (the spec never started) is a hard failure, not an empty pass.
$reportFile = Join-Path $evidenceDir 'playwright-report.json'
if (-not (Test-Path -LiteralPath $reportFile)) { Set-Content -LiteralPath $reportFile -Value '{ "suites": [] }' -Encoding utf8; [void]$runnerSteps.Add(@{ stepId = 'PW'; title = 'PW the stage-0 spec produced a report'; passed = $false; detail = 'no playwright-report.json' }) }
ConvertTo-Json -InputObject @($runnerSteps) -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDir 'runner-steps.json') -Encoding utf8
@{ runId = $runId; stage = $Stage; implementationSha = $sha; schemaVersion = $schemaVersion; resumed = $false; clockMode = 'real' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir 'meta.json') -Encoding utf8
& node (Join-Path $PSScriptRoot 'chain\Build-ChainEvidence.mjs') --playwright $reportFile --runner (Join-Path $evidenceDir 'runner-steps.json') --meta (Join-Path $evidenceDir 'meta.json') --out (Join-Path $evidenceDir 'evidence.json')
$exitCode = $LASTEXITCODE
Write-Host "Evidence kept in $evidenceDir"
exit $exitCode
