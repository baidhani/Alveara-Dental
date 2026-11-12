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
  ALV-N020 R02 (case 13.05): while Playwright runs, the runner watches <evidence>\api-control\ for a spec's request to stop or start ITS OWN API process (the one it
  started, by process id; never another process): 'stop.request' -> stop it, wait until port 5072 is free, write 'stopped.ack'; 'start.request' -> refuse if the port is
  busy, start the same host with the same settings (same database, key ring and storage, so sessions survive), wait for health, write 'started.ack'. Each action is
  recorded as a runner step (API-STOP, API-START). The chain spec reaches the folder through CHAIN_API_CONTROL.
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
    [switch]$SkipBuild,
    # injected (default): the chain host, which runs the unchanged production application with an injected test clock (PD-32). real: the production API itself with the real clock.
    [ValidateSet('injected', 'real')][string]$ClockMode = 'injected',
    # Self-test hooks (tests/stabilization/chain-runner-resilience.test.mjs). Normal runs use neither.
    [ValidatePattern('^[0-9a-f]{32}$')][string]$RunId,
    [ValidateSet('None', 'KillApiAfterStart')][string]$InjectFault = 'None'
)

$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))
. ([WildcardPattern]::Escape("$PSScriptRoot\chain\ChainSafety.ps1"))

$runId       = if ($RunId) { $RunId } else { New-ChainRunId }
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
$clockModeSeen = 'unknown'      # what the running host reports, never what was asked for

foreach ($port in 5072, 5173) {
    if (Test-PortInUse $port) { Write-Host "Port $port is already in use: stop the dev app first; a chain run needs both ports."; exit 2 }
}

# A run owns only what it creates. If the database or the run folder already exists it is somebody else's (or a leftover): refuse before creating anything, and never drop or delete it.
try { $existing = (Invoke-DevSql "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database'" | Select-Object -First 1).Trim() }
catch { Write-Host "Cannot check whether $Database exists, so the run is refused: $_"; exit 2 }
if ($existing -ne '0') { Write-Host "Refusing: the database $Database already exists. A chain run only uses a database it created itself and never drops one it did not create."; exit 2 }
if (Test-Path -LiteralPath $runRoot) { Write-Host "Refusing: the run folder $runRoot already exists. A chain run only deletes a folder it created itself."; exit 2 }

foreach ($dir in $dbDir, $storageDir, $keysDir, $backupDir) { [void](Assert-RunOwnedPath $dir $runRoot $DataRoot); New-Item -ItemType Directory -Force -Path $dir | Out-Null }
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$ownsRunFolder = $true
$created = $false

$sha = Get-ChainImplementationSha $RepoRoot

# One snapshot of the chain database for the E10 invariant (scripts\chain\E10Invariant.mjs decides): rows per table, the differential base (from sys.master_files; it stays NULL until a non-COPY_ONLY full backup sets it) and the restores recorded for it.
function Get-ChainSnapshot([string]$Db) {
    Assert-ChainDatabase $Db $runId
    $tables = [ordered]@{}
    foreach ($r in (Invoke-DevSql "SET NOCOUNT ON; SELECT t.name + '|' + CAST(SUM(p.rows) AS varchar(20)) FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) GROUP BY t.name ORDER BY t.name" $Db)) {
        if ("$r".Trim() -match '^(.+)\|(\d+)$') { $tables[$Matches[1]] = [int64]$Matches[2] }
    }
    $lsn = (Invoke-DevSql "SET NOCOUNT ON; SELECT ISNULL(CAST(MAX(differential_base_lsn) AS varchar(40)), 'null') FROM sys.master_files WHERE database_id = DB_ID(N'$Db') AND type = 0" | Select-Object -First 1).Trim()
    $restores = [int]((Invoke-DevSql "SET NOCOUNT ON; SELECT COUNT(*) FROM msdb.dbo.restorehistory WHERE destination_database_name = N'$Db'" | Select-Object -First 1).Trim())
    return [ordered]@{ tables = $tables; differentialBaseLsn = $(if ($lsn -eq 'null') { $null } else { $lsn }); restoreCount = $restores }
}

$api = $null
try {
    Assert-ChainDatabase $Database $runId
    $mdf = (Assert-RunOwnedPath (Join-Path $dbDir "$Database.mdf") $runRoot $DataRoot).Replace("'", "''")
    $ldf = (Assert-RunOwnedPath (Join-Path $dbDir "${Database}_log.ldf") $runRoot $DataRoot).Replace("'", "''")
    Invoke-DevSql "CREATE DATABASE [$Database] ON (NAME = N'${Database}_data', FILENAME = N'$mdf') LOG ON (NAME = N'${Database}_log', FILENAME = N'$ldf'); ALTER DATABASE [$Database] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [$Database] SET ALLOW_SNAPSHOT_ISOLATION ON;" | Out-Null
    $created = $true
    Write-Host "Chain run ${runId}: database $Database created in $dbDir"

    $connection = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
    Push-Location -LiteralPath $apiProject
    try {
        $ErrorActionPreference = 'Continue'
        if (-not $SkipBuild) {
            if ($ClockMode -eq 'injected') { & dotnet build (Join-Path $RepoRoot 'src\Alveara.Api.ChainHost\Alveara.Api.ChainHost.csproj') --nologo -v q } else { & dotnet build --nologo -v q }
            if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
        }
        & dotnet ef database update --no-build --connection $connection
        if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed.' }
    }
    finally { $ErrorActionPreference = 'Stop'; Pop-Location }

    $schemaVersion = (Invoke-DevSql "SET NOCOUNT ON; SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC" $Database | Select-Object -First 1).Trim()

    $dll = if ($ClockMode -eq 'injected') { Join-Path $RepoRoot 'src\Alveara.Api.ChainHost\bin\Debug\net10.0\Alveara.Api.ChainHost.dll' } else { Join-Path $apiProject 'bin\Debug\net10.0\Alveara.Api.dll' }
    if (-not (Test-Path -LiteralPath $dll)) { throw "The built host was not found at $dll." }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = '"' + $dll + '"'
    $psi.WorkingDirectory = $(if ($ClockMode -eq 'injected') { Split-Path -Parent $dll } else { $apiProject })
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $psi.EnvironmentVariables['ASPNETCORE_URLS'] = $ApiUrl
    $psi.EnvironmentVariables['CHAIN_URL'] = $ApiUrl
    $psi.EnvironmentVariables['ConnectionStrings__Alveara'] = $connection
    $psi.EnvironmentVariables['AdminBootstrapSecret'] = $secret
    $psi.EnvironmentVariables['AuthAttemptRateLimit__PermitLimit'] = '500'
    $psi.EnvironmentVariables['StorageRoot'] = $storageDir
    $psi.EnvironmentVariables['DataProtectionKeysPath'] = $keysDir
    $psi.EnvironmentVariables['Backup__Root'] = $backupDir
    $api = [System.Diagnostics.Process]::Start($psi)
    $apiPids = New-Object System.Collections.ArrayList       # every API process this run started (ALV-N020 R02: the 13.05 restart starts a second one)
    [void]$apiPids.Add($api.Id)

    $healthy = $false
    for ($i = 0; $i -lt 40 -and -not $healthy; $i++) {
        if ($api.HasExited) { throw "The API exited at start-up (exit code $($api.ExitCode))." }
        try { $healthy = (Invoke-WebRequest "$ApiUrl/api/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $healthy) { throw 'The API did not become healthy in time.' }
    Write-Host "Host healthy on $ApiUrl; running stage $Stage ..."
    # The clock mode is measured from the running host: the chain host answers /api/chain/clock-mode with a JSON report. The production API serves no such report: it answers an unknown route with its
    # client page (HTML, status 200) or a 404, so only a JSON body with a 'mode' counts as the chain's report.
    $report = $null
    try {
        $reply = Invoke-WebRequest "$ApiUrl/api/chain/clock-mode" -UseBasicParsing -TimeoutSec 5
        if ("$($reply.Headers['Content-Type'])" -like 'application/json*') { $parsed = $reply.Content | ConvertFrom-Json; if ($parsed.PSObject.Properties.Name -contains 'mode') { $report = $parsed } }
    }
    catch { if (-not ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404)) { throw "Could not read the clock mode from the host: $_" } }
    $clockModeSeen = if ($report) { $report.mode } else { 'real' }
    if ($clockModeSeen -ne $ClockMode) { throw "The host reports clock mode '$clockModeSeen' but '$ClockMode' was requested." }
    ConvertTo-Json -InputObject @{ requested = $ClockMode; reported = $clockModeSeen; report = $report } -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDir 'clock-mode.json') -Encoding utf8
    Write-Host "Clock mode: $clockModeSeen"
    $env:CHAIN_CLOCK_MODE = $clockModeSeen
    if ($InjectFault -eq 'KillApiAfterStart') {
        Stop-Process -Id $api.Id -Force
        [void]$api.WaitForExit(15000)
        Write-Host 'INJECTED FAULT: the API was killed right after start-up (self-test); the cleanup below must still remove everything this run created.'
    }

    $env:E2E_DB_NAME = $Database
    $env:E2E_BOOTSTRAP_SECRET = $secret
    $env:ALVERA_SQL_SERVER = $SqlServer
    $env:CHAIN_OUT = $evidenceDir
    # ALV-N020 R02 (13.05): the owned-API control channel. Only this run's own API process is ever stopped or started; every action is a runner step.
    $controlDir = Assert-RunOwnedPath (Join-Path $evidenceDir 'api-control') $evidenceDir $DataRoot
    New-Item -ItemType Directory -Force -Path $controlDir | Out-Null
    $env:CHAIN_API_CONTROL = $controlDir
    function Write-ControlAck([string]$Name, [hashtable]$Body) {
        ConvertTo-Json -InputObject $Body | Set-Content -LiteralPath (Join-Path $controlDir $Name) -Encoding utf8
    }
    function Invoke-ApiControl {
        $stopReq = Join-Path $controlDir 'stop.request'
        $startReq = Join-Path $controlDir 'start.request'
        if (Test-Path -LiteralPath $stopReq) {
            Remove-Item -LiteralPath $stopReq -Force
            $ok = $false; $detail = ''
            try {
                if (-not $apiPids.Contains($script:api.Id)) { throw "process $($script:api.Id) is not one this run started" }
                if (-not $script:api.HasExited) { Stop-Process -Id $script:api.Id -Force; [void]$script:api.WaitForExit(15000) }
                for ($i = 0; $i -lt 30 -and (Test-PortInUse 5072); $i++) { Start-Sleep -Milliseconds 500 }
                if (Test-PortInUse 5072) { throw 'port 5072 is still in use after the owned API stopped' }
                $ok = $true; $detail = "stopped owned API process $($script:api.Id); port 5072 free"
            }
            catch { $detail = "$_" }
            Write-ControlAck 'stopped.ack' @{ ok = $ok; pid = $script:api.Id; at = (Get-Date).ToString('o'); detail = $detail }
            [void]$runnerSteps.Add(@{ stepId = 'API-STOP'; title = 'API-STOP the runner stopped its own API when the 13.05 step asked'; passed = $ok; detail = $detail })
            Write-Host "API control: stop -> $detail"
        }
        if (Test-Path -LiteralPath $startReq) {
            Remove-Item -LiteralPath $startReq -Force
            $ok = $false; $detail = ''
            try {
                if (Test-PortInUse 5072) { throw 'port 5072 is in use by another process: refusing to start a second API' }
                $script:api = [System.Diagnostics.Process]::Start($psi)
                [void]$apiPids.Add($script:api.Id)
                $up = $false
                for ($i = 0; $i -lt 40 -and -not $up; $i++) {
                    if ($script:api.HasExited) { throw "the restarted API exited (exit code $($script:api.ExitCode))" }
                    try { $up = (Invoke-WebRequest "$ApiUrl/api/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { Start-Sleep -Seconds 2 }
                }
                if (-not $up) { throw 'the restarted API did not become healthy in time' }
                $ok = $true; $detail = "started owned API process $($script:api.Id) with the same settings; healthy"
            }
            catch { $detail = "$_" }
            Write-ControlAck 'started.ack' @{ ok = $ok; pid = $script:api.Id; at = (Get-Date).ToString('o'); detail = $detail }
            [void]$runnerSteps.Add(@{ stepId = 'API-START'; title = 'API-START the runner restarted its own API when the 13.05 step asked'; passed = $ok; detail = $detail })
            Write-Host "API control: start -> $detail"
        }
    }

    Push-Location -LiteralPath $clientDir
    try {
        $ErrorActionPreference = 'Continue'
        # Playwright runs as a child process so the runner can answer the control channel while it runs (it was a blocking call before ALV-N020 R02).
        # Started through .NET with an explicit working folder: Start-Process resolves the current location as a wildcard pattern, and the repository path
        # contains '[' and ']' (found by the first R02 chain run). The child shares this console, so its output appears as before.
        $pwInfo = New-Object System.Diagnostics.ProcessStartInfo
        $pwInfo.FileName = 'cmd.exe'
        $pwInfo.Arguments = '/c npx playwright test --config=playwright.chain.config.ts'
        $pwInfo.WorkingDirectory = $clientDir
        $pwInfo.UseShellExecute = $false
        $pw = [System.Diagnostics.Process]::Start($pwInfo)
        while (-not $pw.HasExited) { Invoke-ApiControl; Start-Sleep -Milliseconds 250 }
        $pw.WaitForExit()
        Invoke-ApiControl                                                   # a request written just before the end is still answered and recorded
        Write-Host "Playwright exit code $($pw.ExitCode) (the verdict comes from the evidence builder)."
    }
    finally { $ErrorActionPreference = 'Stop'; Pop-Location }

    # E10: one COPY_ONLY checkpoint (does not disturb the backup chain), kept inside the run folder, verified, removed with the run. Never an automatic reset.
    # The invariant: the file exists and is COPY_ONLY, no restore is recorded, and the live database (rows per table, differential base) is unchanged.
    $bak = Assert-RunOwnedPath (Join-Path $backupDir 'checkpoint-stage0.bak') $runRoot $DataRoot
    $e10Title = 'E10 COPY_ONLY checkpoint: file exists, no restore invoked, live database unchanged'
    try {
        $beforeSnap = Get-ChainSnapshot $Database
        Invoke-DevSql "BACKUP DATABASE [$Database] TO DISK = N'$($bak.Replace("'", "''"))' WITH COPY_ONLY, INIT, CHECKSUM" | Out-Null
        Invoke-DevSql "RESTORE VERIFYONLY FROM DISK = N'$($bak.Replace("'", "''"))' WITH CHECKSUM" | Out-Null
        $afterSnap = Get-ChainSnapshot $Database
        $copyOnly = (Invoke-DevSql "SET NOCOUNT ON; SELECT TOP 1 CAST(is_copy_only AS int) FROM msdb.dbo.backupset WHERE database_name = N'$Database' ORDER BY backup_finish_date DESC" | Select-Object -First 1).Trim()
        $afterSnap['backup'] = [ordered]@{ exists = (Test-Path -LiteralPath $bak); bytes = $(if (Test-Path -LiteralPath $bak) { (Get-Item -LiteralPath $bak).Length } else { 0 }); copyOnly = ($copyOnly -eq '1') }
        ConvertTo-Json -InputObject $beforeSnap -Depth 5 | Set-Content -LiteralPath (Join-Path $evidenceDir 'e10-before.json') -Encoding utf8
        ConvertTo-Json -InputObject $afterSnap -Depth 5 | Set-Content -LiteralPath (Join-Path $evidenceDir 'e10-after.json') -Encoding utf8
        $verdict = & node (Join-Path $PSScriptRoot 'chain\E10Invariant.mjs') --before (Join-Path $evidenceDir 'e10-before.json') --after (Join-Path $evidenceDir 'e10-after.json')
        [void]$runnerSteps.Add(@{ stepId = 'E10'; title = $e10Title; passed = ($LASTEXITCODE -eq 0); detail = ($verdict -join ' | ') })
    }
    catch { [void]$runnerSteps.Add(@{ stepId = 'E10'; title = $e10Title; passed = $false; detail = "$_" }) }
}
catch {
    Write-Host "The chain runner could not complete its setup: $_"
    [void]$runnerSteps.Add(@{ stepId = 'RUN'; title = 'RUN the runner completed its own setup'; passed = $false; detail = "$_" })
}
finally {
    $ErrorActionPreference = 'Continue'
    Remove-Item Env:E2E_DB_NAME, Env:E2E_BOOTSTRAP_SECRET, Env:CHAIN_OUT, Env:CHAIN_CLOCK_MODE, Env:CHAIN_API_CONTROL -ErrorAction SilentlyContinue
    if ($api -and -not $api.HasExited) {
        try { Stop-Process -Id $api.Id -Force } catch { Write-Warning "Could not stop the API process $($api.Id): $_" }
        [void]$api.WaitForExit(15000)
    }
    $dropped = -not $created      # nothing to drop when this run never created the database
    for ($i = 1; $i -le 5 -and -not $dropped; $i++) {
        try {
            Assert-ChainDatabase $Database $runId
            Invoke-DevSql "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END" | Out-Null
            $dropped = $true
        }
        catch { Start-Sleep -Seconds 2 }
    }
    if ($dropped -and $created) { Write-Host "Database $Database dropped (its files went with it)." }
    elseif (-not $dropped) { Write-Warning "Could not drop $Database; remove it by hand: DROP DATABASE [$Database]" }
    try { if ($ownsRunFolder) { Remove-RunOwnedTree $runRoot $runRoot $DataRoot -AllowRunRoot } }
    catch { Write-Warning "Could not remove the run folder ${runRoot}: $_" }
    [void]$runnerSteps.Add(@{ stepId = 'CLEAN'; title = 'CLEAN the database is dropped and the run folder is removed'; passed = ($dropped -and -not (Test-Path -LiteralPath $runRoot)); detail = '' })
}

# The evidence builder decides the verdict. A missing Playwright report (the spec never started) is a hard failure, not an empty pass.
$reportFile = Join-Path $evidenceDir 'playwright-report.json'
if (-not (Test-Path -LiteralPath $reportFile)) { Set-Content -LiteralPath $reportFile -Value '{ "suites": [] }' -Encoding utf8; [void]$runnerSteps.Add(@{ stepId = 'PW'; title = 'PW the stage-0 spec produced a report'; passed = $false; detail = 'no playwright-report.json' }) }
ConvertTo-Json -InputObject @($runnerSteps) -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDir 'runner-steps.json') -Encoding utf8
@{ runId = $runId; stage = $Stage; implementationSha = $sha; schemaVersion = $schemaVersion; resumed = $false; clockMode = $clockModeSeen } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir 'meta.json') -Encoding utf8
& node (Join-Path $PSScriptRoot 'chain\Build-ChainEvidence.mjs') --playwright $reportFile --runner (Join-Path $evidenceDir 'runner-steps.json') --meta (Join-Path $evidenceDir 'meta.json') --out (Join-Path $evidenceDir 'evidence.json')
$exitCode = $LASTEXITCODE
Write-Host "Evidence kept in $evidenceDir"
exit $exitCode
