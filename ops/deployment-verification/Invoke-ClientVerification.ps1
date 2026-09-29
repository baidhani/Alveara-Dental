#Requires -RunAsAdministrator
<#
.SYNOPSIS
    ALV-N002 deployment verification  -  LAN client side.

.DESCRIPTION
    Captures durable, timestamped, machine-generated evidence that a genuinely
    separate LAN client can reach the deployed Alveara shell/API with real
    (non-bypassed) TLS validation, cannot reach the server's database port or
    storage filesystem directly, and can still do all of that while its OWN
    public internet access is unavailable  -  with a before/during/after
    timestamp trail proving the isolation window and the LAN success were
    simultaneous, not sequential claims stitched together afterward.

    Exists because narrative descriptions of manually-run commands are not
    acceptable evidence for ALV-N002's LAN/security acceptance items (see the
    ALV-N002 R06 independent review, finding N002-R06-02).

.PARAMETER ServerHostName
    The LAN hostname the server's certificate is issued for (e.g. alveara-server.local).

.PARAMETER ServerIp
    The server's real LAN IP (used for the direct SQL-port/file-share denial checks).

.PARAMETER HttpsPort
    The port the API's HTTPS listener is bound to.

.PARAMETER SqlPort
    The port SQL Server would be listening on if it were reachable remotely (default 1433).

.PARAMETER StorageShareUncPath
    A UNC path guessing at whether the storage root is exposed as a file share
    (e.g. \\<ServerIp>\AlveaaraServer\App_Data\blobs). Expected to NOT exist.

.PARAMETER BlockDurationMinutes
    How long to block this machine's own public internet before it
    auto-reverts (self-reverting Scheduled Task safety net).

.PARAMETER OutputDir
    Where to write timestamped evidence files. Created if missing.

.EXAMPLE
    .\Invoke-ClientVerification.ps1 `
        -ServerHostName alveara-server.local -ServerIp 192.168.1.216 -HttpsPort 7180 `
        -StorageShareUncPath "\\192.168.1.216\AlveaaraServer\App_Data\blobs" `
        -BlockDurationMinutes 5 -OutputDir "C:\AlveaaraClient\verification-evidence"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ServerHostName,
    [Parameter(Mandatory)] [string] $ServerIp,
    [int] $HttpsPort = 7180,
    [int] $SqlPort = 1433,
    [Parameter(Mandatory)] [string] $StorageShareUncPath,
    [int] $BlockDurationMinutes = 5,
    [Parameter(Mandatory)] [string] $OutputDir
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$routes = @("/", "/system-status", "/api/health", "/api/systemstatus")

function Invoke-RouteSweep {
    param([string] $Label)
    $lines = foreach ($route in $routes) {
        $url = "https://$($ServerHostName):$($HttpsPort)$route"
        try {
            $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
            "OK  $($resp.StatusCode)  $url  CapturedAtUtc=$((Get-Date).ToUniversalTime().ToString('o'))"
        } catch {
            "FAIL $url : $($_.Exception.Message)  CapturedAtUtc=$((Get-Date).ToUniversalTime().ToString('o'))"
        }
    }
    $content = "=== Route sweep: $Label ===`n" + ($lines -join "`n")
    $path = Join-Path $OutputDir "$stamp-routes-$($Label -replace '\s','-').txt"
    $content | Out-File -FilePath $path -Encoding utf8
    Write-Output $content
    return $lines
}

$results = [ordered]@{}

Write-Output "=== [1/4] Baseline route sweep (before any block) ==="
$baseline = Invoke-RouteSweep -Label "baseline"
$results.baselineRoutes = if ($baseline -notmatch "FAIL") { "PASS: all routes succeeded" } else { "FAIL: at least one route failed" }

Write-Output "=== [2/4] Direct database/storage bypass checks (should fail) ==="
$sqlTest = Test-NetConnection -ComputerName $ServerIp -Port $SqlPort -WarningAction SilentlyContinue
$shareExists = Test-Path -LiteralPath $StorageShareUncPath -ErrorAction SilentlyContinue
$bypassContent = @"
SQL port test ($ServerIp`:$SqlPort): TcpTestSucceeded=$($sqlTest.TcpTestSucceeded)
Storage share test ($StorageShareUncPath): Exists=$shareExists
CapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))
"@
$bypassContent | Out-File -FilePath (Join-Path $OutputDir "$stamp-bypass-checks.txt") -Encoding utf8
Write-Output $bypassContent
$results.directDbBlocked = if (-not $sqlTest.TcpTestSucceeded) { "PASS: SQL port not reachable" } else { "FAIL: SQL port was reachable" }
$results.directStorageBlocked = if (-not $shareExists) { "PASS: no file share exposes storage root" } else { "FAIL: storage root reachable via file share" }

Write-Output "=== [3/4] Block this client's own public internet, prove it, prove LAN still works, same window ==="
$beforePing = & ping -n 2 8.8.8.8 2>&1 | Out-String
New-NetFirewallRule -DisplayName "Alveara-TempBlockInternet" -Direction Outbound -RemoteAddress Internet -Action Block -Profile Any | Out-Null
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument '-NoProfile -Command "Remove-NetFirewallRule -DisplayName ''Alveara-TempBlockInternet'' -ErrorAction SilentlyContinue"'
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes($BlockDurationMinutes)
Register-ScheduledTask -TaskName "Alveara-UndoInternetBlock" -Action $action -Trigger $trigger -RunLevel Highest -Force | Out-Null
$blockAppliedAtUtc = (Get-Date).ToUniversalTime().ToString('o')

Start-Sleep -Seconds 2
$duringPing = & ping -n 2 8.8.8.8 2>&1 | Out-String
$duringRoutes = Invoke-RouteSweep -Label "during-block"

$isolationContent = @"
Block applied at (UTC): $blockAppliedAtUtc
Scheduled auto-revert at: $((Get-Date).AddMinutes($BlockDurationMinutes))

--- Public-internet probe BEFORE block ---
$beforePing

--- Public-internet probe DURING block (expected to fail) ---
$duringPing
"@
$isolationContent | Out-File -FilePath (Join-Path $OutputDir "$stamp-isolation-window.txt") -Encoding utf8
Write-Output $isolationContent

$publicInternetBlockedDuringWindow = $duringPing -match "100% loss|General failure|Request timed out"
$lanSucceededDuringWindow = $duringRoutes -notmatch "FAIL"
$results.simultaneousIsolation = if ($publicInternetBlockedDuringWindow -and $lanSucceededDuringWindow) {
    "PASS: public internet failed and LAN routes succeeded in the same window"
} else {
    "FAIL: could not confirm both conditions held simultaneously"
}

Write-Output "=== [4/4] Waiting for auto-revert, then confirming restoration ==="
Start-Sleep -Seconds ([Math]::Max(5, ($BlockDurationMinutes * 60) - 5))
$deadline = (Get-Date).AddMinutes(2)
do {
    Start-Sleep -Seconds 5
    $ruleGone = -not (Get-NetFirewallRule -DisplayName "Alveara-TempBlockInternet" -ErrorAction SilentlyContinue)
} while (-not $ruleGone -and (Get-Date) -lt $deadline)

$afterPing = & ping -n 2 8.8.8.8 2>&1 | Out-String
$revertContent = @"
Rule removed by scheduled task: $ruleGone
--- Public-internet probe AFTER revert (expected to succeed) ---
$afterPing
CapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))
"@
$revertContent | Out-File -FilePath (Join-Path $OutputDir "$stamp-revert-confirmation.txt") -Encoding utf8
Write-Output $revertContent
$results.autoRevertClean = if ($ruleGone -and ($afterPing -notmatch "100% loss|General failure")) { "PASS: block cleanly auto-reverted" } else { "FAIL: manual cleanup required" }
if (-not $ruleGone) {
    Remove-NetFirewallRule -DisplayName "Alveara-TempBlockInternet" -ErrorAction SilentlyContinue
    Write-Output "Manually removed lingering block rule."
}
Unregister-ScheduledTask -TaskName "Alveara-UndoInternetBlock" -Confirm:$false -ErrorAction SilentlyContinue

$summary = [ordered]@{
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    serverHostName = $ServerHostName
    serverIp = $ServerIp
    httpsPort = $HttpsPort
    sqlPort = $SqlPort
    storageShareUncPath = $StorageShareUncPath
    blockDurationMinutes = $BlockDurationMinutes
    results = $results
}
$summaryPath = Join-Path $OutputDir "$stamp-SUMMARY.json"
$summary | ConvertTo-Json -Depth 5 | Out-File -FilePath $summaryPath -Encoding utf8
Write-Output ""
Write-Output "=== Summary written to $summaryPath ==="
$summary.results.GetEnumerator() | ForEach-Object { Write-Output "$($_.Key): $($_.Value)" }
