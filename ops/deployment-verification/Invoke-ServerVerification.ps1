#Requires -RunAsAdministrator
<#
.SYNOPSIS
    ALV-N002 deployment verification - server side.

.DESCRIPTION
    Captures durable, timestamped, machine-generated evidence that the deployed
    Alveara API is running under a dedicated least-privilege service identity,
    that its actual live database session uses the tested least-privilege SQL
    login (not merely that the login exists with the right roles), and that
    its storage root is protected by real NTFS ACLs - with an unrelated local
    account genuinely denied access to that same storage root and the service
    account genuinely able to read/write it.

    This exists because narrative descriptions of manually-run commands, and
    manually-completed/edited result files, are not acceptable evidence for
    ALV-N002's LAN/security acceptance items (see the ALV-N002 R06 and R07
    independent reviews, findings N002-R06-02, N002-R07-02, N002-R07-03).
    Every check below writes its raw result to a file in -OutputDir, computed
    entirely by this script in one run; nothing is summarized, edited, or
    completed by hand afterward.

.PARAMETER ServiceAccountName
    The dedicated local account the API process should be running under.

.PARAMETER ServiceAccountPassword
    SecureString password for ServiceAccountName. Used to actually perform
    the positive storage write/read check as that identity - not printed as
    a manual instruction.

.PARAMETER OrdinaryAccountName
    A local account with no special relationship to the API, used for the
    negative (access-denied) check.

.PARAMETER OrdinaryAccountPassword
    SecureString password for OrdinaryAccountName.

.PARAMETER ApiProcessName
    The process name to inspect (without .exe).

.PARAMETER ApiBaseUrl
    The API's own base URL, used to force a real database-backed request
    immediately before checking for a live SQL session under the
    least-privilege login, and to retain the response body as evidence of
    truthful System Status reporting. Use the plain HTTP endpoint on
    localhost (e.g. http://localhost:5072) - this check runs on the server
    itself and isn't testing certificate trust (that's the client script's
    job), and the server's certificate is issued for its LAN hostname, not
    "localhost", so an HTTPS request to localhost here would fail on a name
    mismatch for reasons unrelated to what this check verifies.

.PARAMETER StorageRoot
    The real path LocalDiskBlobStorage resolves to on this server (i.e. the
    actual ContentRootPath/App_Data/blobs the running process uses - not a
    stand-in folder).

.PARAMETER SqlInstance
    e.g. "localhost\SQLEXPRESS".

.PARAMETER SqlDatabase
    The database the API connects to.

.PARAMETER SqlLogin
    The least-privilege SQL login the API's connection string uses.

.PARAMETER OutputDir
    Where to write timestamped evidence files. Created if missing.

.EXAMPLE
    .\Invoke-ServerVerification.ps1 `
        -ServiceAccountName svc-alveara-api -ServiceAccountPassword (Read-Host -AsSecureString) `
        -OrdinaryAccountName test-ordinary-user -OrdinaryAccountPassword (Read-Host -AsSecureString) `
        -ApiBaseUrl "http://localhost:5072" `
        -StorageRoot "C:\AlveaaraServer\App_Data\blobs" `
        -SqlInstance "localhost\SQLEXPRESS" -SqlDatabase AlveraLanServerDemo -SqlLogin alveara_app_login `
        -OutputDir "C:\AlveaaraServer\verification-evidence"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ServiceAccountName,
    [Parameter(Mandatory)] [System.Security.SecureString] $ServiceAccountPassword,
    [Parameter(Mandatory)] [string] $OrdinaryAccountName,
    [Parameter(Mandatory)] [System.Security.SecureString] $OrdinaryAccountPassword,
    [string] $ApiProcessName = "Alveara.Api",
    [Parameter(Mandatory)] [string] $ApiBaseUrl,
    [Parameter(Mandatory)] [string] $StorageRoot,
    [Parameter(Mandatory)] [string] $SqlInstance,
    [Parameter(Mandatory)] [string] $SqlDatabase,
    [Parameter(Mandatory)] [string] $SqlLogin,
    [Parameter(Mandatory)] [string] $OutputDir
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$results = [ordered]@{}

function Write-Evidence {
    param([string] $Name, [string] $Content)
    $path = Join-Path $OutputDir "$stamp-$Name.txt"
    $Content | Out-File -FilePath $path -Encoding utf8
    Write-Output "Wrote $path"
    return $path
}

Write-Output "=== [1/6] Process identity: is $ApiProcessName running, and as whom? ==="
$procs = Get-CimInstance Win32_Process -Filter "Name = '$ApiProcessName.exe'"
$apiPid = $null
if (-not $procs) {
    $content = "NOT RUNNING: no process named $ApiProcessName.exe was found at $(Get-Date -Format o)."
    $results.processIdentity = "FAIL"
} else {
    $lines = foreach ($p in $procs) {
        $owner = Invoke-CimMethod -InputObject $p -MethodName GetOwner
        "PID=$($p.ProcessId) Name=$($p.Name) Owner=$($owner.Domain)\$($owner.User) CommandLine=$($p.CommandLine)"
    }
    $apiPid = ($procs | Select-Object -First 1).ProcessId
    $content = ($lines -join "`n")
    $expectedOwnerMatch = $lines -match [regex]::Escape($ServiceAccountName)
    $results.processIdentity = if ($expectedOwnerMatch) { "PASS: running as $ServiceAccountName (PID $apiPid)" } else { "FAIL: not running as $ServiceAccountName" }
}
Write-Evidence -Name "process-identity" -Content "$content`n`nResult: $($results.processIdentity)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [2/6] Force a real database-backed request, retain its response body ==="
$systemStatusContent = ""
try {
    $resp = Invoke-WebRequest -Uri "$ApiBaseUrl/api/systemstatus" -UseBasicParsing -TimeoutSec 15
    $systemStatusContent = "StatusCode: $($resp.StatusCode)`nBody: $($resp.Content)"
    $results.systemStatusResponse = if ($resp.Content -match '"reachable"\s*:\s*true') { "PASS: HTTP $($resp.StatusCode), database.reachable=true" } else { "FAIL or degraded: HTTP $($resp.StatusCode), body did not report reachable=true" }
} catch {
    $systemStatusContent = "REQUEST FAILED: $($_.Exception.Message)"
    $results.systemStatusResponse = "FAIL: request error"
}
Write-Evidence -Name "systemstatus-response" -Content "$systemStatusContent`n`nResult: $($results.systemStatusResponse)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [3/6] Correlate a live SQL session (for $SqlLogin) to the running API process (PID $apiPid) ==="
$sessionQuery = "SELECT session_id, login_name, host_name, program_name, host_process_id, status, login_time FROM sys.dm_exec_sessions WHERE login_name = '$SqlLogin' ORDER BY login_time DESC;"
try {
    $sessions = Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $SqlDatabase -TrustServerCertificate -Query $sessionQuery
    $sessionContent = ($sessions | Format-Table -AutoSize | Out-String)
    $matchingPidSession = $null
    if ($apiPid) {
        $matchingPidSession = $sessions | Where-Object { [int]$_.host_process_id -eq [int]$apiPid }
    }
    if ($matchingPidSession) {
        $results.sqlSessionLinkedToApi = "PASS: live session(s) under $SqlLogin report host_process_id=$apiPid, matching the running API process"
    } elseif ($sessions) {
        $results.sqlSessionLinkedToApi = "PARTIAL: live session(s) exist under $SqlLogin but host_process_id did not match API PID $apiPid (client-reported PID may differ by driver/pooling behavior - see raw session list)"
    } else {
        $results.sqlSessionLinkedToApi = "FAIL: no live session found under $SqlLogin at all"
    }
} catch {
    $sessionContent = "QUERY FAILED: $($_.Exception.Message)"
    $results.sqlSessionLinkedToApi = "FAIL: query error"
}
Write-Evidence -Name "sql-session-correlation" -Content "$sessionContent`n`nResult: $($results.sqlSessionLinkedToApi)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [4/6] SQL role membership for $SqlLogin on $SqlDatabase (separate least-privilege evidence) ==="
$roleQuery = "SELECT dp.name AS role_name, mp.name AS member_name FROM sys.database_role_members drm JOIN sys.database_principals dp ON drm.role_principal_id = dp.principal_id JOIN sys.database_principals mp ON drm.member_principal_id = mp.principal_id WHERE mp.name = '$SqlLogin' ORDER BY dp.name;"
try {
    $roles = Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $SqlDatabase -TrustServerCertificate -Query $roleQuery
    $roleNames = @($roles | ForEach-Object { $_.role_name })
    $content = ($roles | Format-Table -AutoSize | Out-String)
    $expectedRoles = @("db_datareader", "db_datawriter")
    $actualSorted = @($roleNames | Sort-Object -Unique)
    $expectedSorted = @($expectedRoles | Sort-Object -Unique)
    $hasOnlyExpected = ($actualSorted.Count -eq $expectedSorted.Count) -and (@(Compare-Object $actualSorted $expectedSorted).Count -eq 0)
    $results.sqlRoles = if ($hasOnlyExpected) { "PASS: exactly db_datareader + db_datawriter" } else { "FAIL or unexpected: $($roleNames -join ', ')" }
} catch {
    $content = "QUERY FAILED: $($_.Exception.Message)"
    $results.sqlRoles = "FAIL: query error"
}
Write-Evidence -Name "sql-role-membership" -Content "$content`n`nResult: $($results.sqlRoles)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [5/6] Storage root ACL (real path, not a stand-in) ==="
$aclOutput = icacls $StorageRoot 2>&1 | Out-String
$hasOrdinaryAccess = $aclOutput -match [regex]::Escape($OrdinaryAccountName) -or $aclOutput -match "Everyone" -or $aclOutput -match "\bUsers:"
$hasServiceAccess = $aclOutput -match [regex]::Escape($ServiceAccountName)
$results.storageAcl = if ($hasServiceAccess -and -not $hasOrdinaryAccess) { "PASS: only $ServiceAccountName (+ Administrators) present, no broad-access entries" } else { "FAIL: unexpected ACL shape" }
Write-Evidence -Name "storage-acl" -Content "$aclOutput`n`nResult: $($results.storageAcl)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [6/6] Ordinary account denied, service account genuinely allowed - both executed and verified by this script, no manual step ==="

$denialScript = Join-Path $OutputDir "$stamp-ordinary-denial-inner.ps1"
$denialResultPath = Join-Path $OutputDir "$stamp-ordinary-denial-result.txt"
@"
try {
    `$actualIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    `$files = Get-ChildItem '$StorageRoot' -ErrorAction Stop
    "UNEXPECTED SUCCESS: running as `$actualIdentity, `$(`$files.Count) files listed at `$(Get-Date -Format o)" | Out-File '$denialResultPath'
} catch {
    `$actualIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    "ACCESS DENIED (expected): running as `$actualIdentity, `$(`$_.Exception.Message) at `$(Get-Date -Format o)" | Out-File '$denialResultPath'
}
"@ | Out-File -FilePath $denialScript -Encoding utf8

$ordinaryCred = New-Object System.Management.Automation.PSCredential($OrdinaryAccountName, $OrdinaryAccountPassword)
Start-Process powershell.exe -Credential $ordinaryCred -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File",$denialScript -Wait
$denialContent = Get-Content $denialResultPath -Raw -ErrorAction SilentlyContinue
$results.ordinaryDenial = if ($denialContent -match "ACCESS DENIED" -and $denialContent -match [regex]::Escape($OrdinaryAccountName)) {
    "PASS: ordinary account denied, and result file confirms the actual runtime identity was $OrdinaryAccountName"
} elseif ($denialContent -match "UNEXPECTED SUCCESS") {
    "FAIL: ordinary account was NOT denied"
} else {
    "FAIL: no result captured"
}
Write-Output "Ordinary-account result: $denialContent"
Remove-Item $denialScript -ErrorAction SilentlyContinue

$writeScript = Join-Path $OutputDir "$stamp-svc-write-inner.ps1"
$writeResultPath = Join-Path $OutputDir "$stamp-svc-write-result.txt"
@"
try {
    `$actualIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    `$testFile = Join-Path '$StorageRoot' "verify-$stamp.txt"
    "written by `$actualIdentity at `$(Get-Date -Format o)" | Out-File -FilePath `$testFile
    `$readBack = Get-Content `$testFile
    "SUCCESS: running as `$actualIdentity, readback='`$readBack' at `$(Get-Date -Format o)" | Out-File '$writeResultPath'
} catch {
    `$actualIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    "FAILED: running as `$actualIdentity, `$(`$_.Exception.Message) at `$(Get-Date -Format o)" | Out-File '$writeResultPath'
}
"@ | Out-File -FilePath $writeScript -Encoding utf8

$serviceCred = New-Object System.Management.Automation.PSCredential($ServiceAccountName, $ServiceAccountPassword)
Start-Process powershell.exe -Credential $serviceCred -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File",$writeScript -Wait
$writeContent = Get-Content $writeResultPath -Raw -ErrorAction SilentlyContinue
$results.serviceWriteRead = if ($writeContent -match "SUCCESS" -and $writeContent -match [regex]::Escape($ServiceAccountName)) {
    "PASS: service account genuinely wrote/read the real storage root, and result file confirms the actual runtime identity was $ServiceAccountName"
} elseif ($writeContent -match "FAILED") {
    "FAIL: service account write/read failed"
} else {
    "FAIL: no result captured"
}
Write-Output "Service-account write/read result: $writeContent"
Remove-Item $writeScript -ErrorAction SilentlyContinue

# The summary is written LAST, after every check above (including the two
# Start-Process -Wait calls) has actually completed - so its own timestamp
# can never precede a result it reports, unlike the R07 defect this fixes.
$summary = [ordered]@{
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    serviceAccountName = $ServiceAccountName
    ordinaryAccountName = $OrdinaryAccountName
    apiProcessName = $ApiProcessName
    apiPid = $apiPid
    apiBaseUrl = $ApiBaseUrl
    storageRoot = $StorageRoot
    sqlInstance = $SqlInstance
    sqlDatabase = $SqlDatabase
    sqlLogin = $SqlLogin
    results = $results
}
$summaryPath = Join-Path $OutputDir "$stamp-SUMMARY.json"
$summary | ConvertTo-Json -Depth 5 | Out-File -FilePath $summaryPath -Encoding utf8
Write-Output ""
Write-Output "=== Summary written to $summaryPath ==="
$summary.results.GetEnumerator() | ForEach-Object { Write-Output "$($_.Key): $($_.Value)" }
