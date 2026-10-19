<#
.SYNOPSIS
  Empties the Test-Data folder: drops every database whose files live there, then deletes what is left inside it. Dev-Data is never touched.

.DESCRIPTION
  Use it after a crashed or interrupted test run, or whenever you want a clean slate. It
    1. finds the databases on the SQL Server whose files are ALL under <root>\Test-Data and drops them (a database stuck in RECOVERY_PENDING is
       dropped without the single-user step, which SQL Server cannot run on it),
    2. then deletes everything inside Test-Data\Database, Backup, Storage, Keys and Temp (the folders themselves stay), EXCEPT files that a
       database still in use owns.
  A database with ANY file outside Test-Data is skipped and reported, and so are its files here (AlveraDev lives in Dev-Data and is never matched).
  A failure on one item is reported and the rest carry on. Exit code 0 = everything cleared; 1 = something was skipped or failed.
  This includes walkthrough output kept in Test-Data\Temp\walkthrough-output: copy evidence you need before running it.
  It asks for confirmation; -Confirm:$false skips the question and -WhatIf only shows what would happen.

.EXAMPLE
  .\scripts\Clear-TestData.ps1 -WhatIf
.EXAMPLE
  .\scripts\Clear-TestData.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param()

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

$TestData = Join-Path $DataRoot 'Test-Data'
$prefix   = $TestData.TrimEnd('\') + '\'

function Test-UnderTestData([string]$Path) { return $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }

# Every file of every user database, as "database|path".
$files = @{}      # database -> list of physical paths
foreach ($row in @(Invoke-DevSql "SET NOCOUNT ON; SELECT d.name + N'|' + f.physical_name FROM sys.databases d JOIN sys.master_files f ON f.database_id = d.database_id WHERE d.database_id > 4;" | Where-Object { $_ -and $_.Trim() })) {
    $name, $path = $row.Trim().Split('|', 2)
    if (-not $files.ContainsKey($name)) { $files[$name] = New-Object System.Collections.Generic.List[string] }
    $files[$name].Add($path)
}

$dropped = 0; $skipped = 0; $failed = 0
$keepPaths = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)   # files a surviving database owns

foreach ($name in @($files.Keys | Sort-Object)) {
    $paths = $files[$name]
    $under = @($paths | Where-Object { Test-UnderTestData $_ }).Count
    if ($under -eq 0) { continue }                                       # not a Test-Data database (AlveraDev, or a stray elsewhere)
    $outside = $paths.Count - $under
    if ($name -notmatch '^[A-Za-z0-9_]{1,128}$') { Write-Warning "Skipping '$name': not a plain database name."; $skipped++; $paths | ForEach-Object { [void]$keepPaths.Add($_) }; continue }
    if ($outside -ne 0) { Write-Warning "Skipping ${name}: ${outside} of its files are outside Test-Data."; $skipped++; $paths | ForEach-Object { [void]$keepPaths.Add($_) }; continue }
    if ($PSCmdlet.ShouldProcess("$name on $SqlServer", 'DROP DATABASE (its files are under Test-Data)')) {
        try {
            try { Invoke-DevSql "ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name];" | Out-Null }
            catch { Invoke-DevSql "DROP DATABASE [$name];" | Out-Null }       # RECOVERY_PENDING: the ALTER cannot run, but a plain DROP can
            $dropped++
        }
        catch { Write-Warning "Could not drop ${name}: $($_.Exception.Message)"; $failed++; $paths | ForEach-Object { [void]$keepPaths.Add($_) } }
    }
}

$deleted = 0; $kept = 0
foreach ($sub in 'Database', 'Backup', 'Storage', 'Keys', 'Temp') {
    $folder = Join-Path $TestData $sub
    if (-not (Test-Path -LiteralPath $folder)) { continue }
    foreach ($item in @(Get-ChildItem -LiteralPath $folder -Force)) {
        $owned = @($keepPaths | Where-Object { $_.Equals($item.FullName, [StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith($item.FullName + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        if ($owned) { Write-Warning "Keeping $($item.FullName): a database that was not dropped owns it."; $kept++; continue }
        if ($PSCmdlet.ShouldProcess($item.FullName, 'Delete')) {
            try { Remove-Item -LiteralPath $item.FullName -Recurse -Force; $deleted++ }
            catch { Write-Warning "Could not delete $($item.FullName): $($_.Exception.Message)"; $failed++ }
        }
    }
}

Write-Host "Test-Data: $dropped database(s) dropped, $skipped skipped, $deleted item(s) deleted, $kept kept because a database owns them, $failed failure(s). Root: $TestData"
if (($skipped + $failed + $kept) -gt 0) { exit 1 }
exit 0
