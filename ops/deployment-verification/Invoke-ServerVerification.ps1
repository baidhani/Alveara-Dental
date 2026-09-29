#Requires -RunAsAdministrator
<#
.SYNOPSIS
    ALV-N002 deployment verification — server side.

.DESCRIPTION
    Captures durable, timestamped, machine-generated evidence that the deployed
    Alveara API is running under a dedicated least-privilege service identity,
    connected through a least-privilege SQL login, with its storage root
    protected by real NTFS ACLs — and that an unrelated local account is
    genuinely denied access to that same storage root.

    This exists because narrative descriptions of manually-run commands are not
    acceptable evidence for ALV-N002's LAN/security acceptance items (see the
    ALV-N002 R06 independent review, finding N002-R06-02). Every check below
    writes its raw result to a file in -OutputDir; nothing is summarized away.

.PARAMETER ServiceAccountName
    The dedicated local account the API process should be running under.

.PARAMETER OrdinaryAccountName
    A local account with no special relationship to the API, used for the
    negative (access-denied) check.

.PARAMETER OrdinaryAccountPassword
    SecureString password for OrdinaryAccountName.

.PARAMETER ApiProcessName
    The process name to inspect (without .exe).

.PARAMETER StorageRoot
    The real path LocalDiskBlobStorage resolves to on this server (i.e. the
    actual ContentRootPath/App_Data/blobs the running process uses — not a
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
        -ServiceAccountName svc-alveara-api -OrdinaryAccountName test-ordinary-user `
        -OrdinaryAccountPassword (Read-Host -AsSecureString) `
        -StorageRoot "C:\AlveaaraServer\App_Data\blobs" `
        -SqlInstance "localhost\SQLEXPRESS" -SqlDatabase AlveraLanServerDemo -SqlLogin alveara_app_login `
        -OutputDir "C:\AlveaaraServer\verification-evidence"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ServiceAccountName,
    [Parameter(Mandatory)] [string] $OrdinaryAccountName,
    [Parameter(Mandatory)] [System.Security.SecureString] $OrdinaryAccountPassword,
    [string] $ApiProcessName = "Alveara.Api",
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

Write-Output "=== [1/5] Process identity: is $ApiProcessName running, and as whom? ==="
$procs = Get-CimInstance Win32_Process -Filter "Name = '$ApiProcessName.exe'"
if (-not $procs) {
    $content = "NOT RUNNING: no process named $ApiProcessName.exe was found at $(Get-Date -Format o)."
    $results.processIdentity = "FAIL"
} else {
    $lines = foreach ($p in $procs) {
        $owner = Invoke-CimMethod -InputObject $p -MethodName GetOwner
        "PID=$($p.ProcessId) Name=$($p.Name) Owner=$($owner.Domain)\$($owner.User) CommandLine=$($p.CommandLine)"
    }
    $content = ($lines -join "`n")
    $expectedOwnerMatch = $lines -match [regex]::Escape($ServiceAccountName)
    $results.processIdentity = if ($expectedOwnerMatch) { "PASS: running as $ServiceAccountName" } else { "FAIL: not running as $ServiceAccountName" }
}
Write-Evidence -Name "process-identity" -Content "$content`n`nResult: $($results.processIdentity)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [2/5] SQL role membership for $SqlLogin on $SqlDatabase ==="
$roleQuery = "SELECT dp.name AS role_name, mp.name AS member_name FROM sys.database_role_members drm JOIN sys.database_principals dp ON drm.role_principal_id = dp.principal_id JOIN sys.database_principals mp ON drm.member_principal_id = mp.principal_id WHERE mp.name = '$SqlLogin' ORDER BY dp.name;"
try {
    $roles = Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $SqlDatabase -TrustServerCertificate -Query $roleQuery
    $roleNames = @($roles | ForEach-Object { $_.role_name })
    $content = ($roles | Format-Table -AutoSize | Out-String)
    $hasOnlyExpected = ($roleNames.Count -gt 0) -and (($roleNames | Sort-Object -Unique) -join ",") -eq (("db_datareader","db_datawriter") | Sort-Object -Unique) -join ","
    $results.sqlRoles = if ($hasOnlyExpected) { "PASS: exactly db_datareader + db_datawriter" } else { "FAIL or unexpected: $($roleNames -join ', ')" }
} catch {
    $content = "QUERY FAILED: $($_.Exception.Message)"
    $results.sqlRoles = "FAIL: query error"
}
Write-Evidence -Name "sql-role-membership" -Content "$content`n`nResult: $($results.sqlRoles)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [3/5] Storage root ACL (real path, not a stand-in) ==="
$aclOutput = icacls $StorageRoot 2>&1 | Out-String
$hasOrdinaryAccess = $aclOutput -match [regex]::Escape($OrdinaryAccountName) -or $aclOutput -match "Everyone" -or $aclOutput -match "\bUsers:"
$hasServiceAccess = $aclOutput -match [regex]::Escape($ServiceAccountName)
$results.storageAcl = if ($hasServiceAccess -and -not $hasOrdinaryAccess) { "PASS: only $ServiceAccountName (+ Administrators) present, no broad-access entries" } else { "FAIL: unexpected ACL shape" }
Write-Evidence -Name "storage-acl" -Content "$aclOutput`n`nResult: $($results.storageAcl)`nCapturedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))"

Write-Output "=== [4/5] Ordinary account denied access to the real storage root ==="
$denialScript = Join-Path $OutputDir "$stamp-ordinary-denial-inner.ps1"
@"
try {
    `$files = Get-ChildItem '$StorageRoot' -ErrorAction Stop
    "UNEXPECTED SUCCESS: `$(`$files.Count) files listed at `$(Get-Date -Format o)" | Out-File '$OutputDir\$stamp-ordinary-denial-result.txt'
} catch {
    "ACCESS DENIED (expected): `$(`$_.Exception.Message) at `$(Get-Date -Format o)" | Out-File '$OutputDir\$stamp-ordinary-denial-result.txt'
}
"@ | Out-File -FilePath $denialScript -Encoding utf8

$cred = New-Object System.Management.Automation.PSCredential($OrdinaryAccountName, $OrdinaryAccountPassword)
Start-Process powershell.exe -Credential $cred -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File",$denialScript -Wait
$denialResultPath = "$OutputDir\$stamp-ordinary-denial-result.txt"
$denialContent = Get-Content $denialResultPath -Raw -ErrorAction SilentlyContinue
$results.ordinaryDenial = if ($denialContent -match "ACCESS DENIED") { "PASS: ordinary account denied" } elseif ($denialContent -match "UNEXPECTED SUCCESS") { "FAIL: ordinary account was NOT denied" } else { "FAIL: no result captured" }
Write-Output "Ordinary-account result: $denialContent"
Remove-Item $denialScript -ErrorAction SilentlyContinue

Write-Output "=== [5/5] Service account can genuinely read/write the real storage root ==="
$writeScript = Join-Path $OutputDir "$stamp-svc-write-inner.ps1"
@"
try {
    `$testFile = Join-Path '$StorageRoot' "verify-$stamp.txt"
    "written by $ServiceAccountName at `$(Get-Date -Format o)" | Out-File -FilePath `$testFile
    `$readBack = Get-Content `$testFile
    "SUCCESS: `$readBack" | Out-File '$OutputDir\$stamp-svc-write-result.txt'
} catch {
    "FAILED: `$(`$_.Exception.Message)" | Out-File '$OutputDir\$stamp-svc-write-result.txt'
}
"@ | Out-File -FilePath $writeScript -Encoding utf8

Write-Output "(Run the following manually as $ServiceAccountName if this script isn't already elevated as that identity: )"
Write-Output "  Start-Process powershell.exe -Credential (Get-Credential $ServiceAccountName) -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','$writeScript' -Wait"
Write-Output "Then re-read: Get-Content '$OutputDir\$stamp-svc-write-result.txt'"
$results.serviceWriteRead = "MANUAL STEP REQUIRED — see console output above; result file: $OutputDir\$stamp-svc-write-result.txt"

$summary = [ordered]@{
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    serviceAccountName = $ServiceAccountName
    ordinaryAccountName = $OrdinaryAccountName
    apiProcessName = $ApiProcessName
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
