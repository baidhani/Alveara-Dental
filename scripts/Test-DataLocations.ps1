<#
.SYNOPSIS
  Reports (and changes nothing) which databases have files OUTSIDE the one data root. Exit code 1 when any do.

.DESCRIPTION
  The rule: every database and every file the project creates lives under <root> (default C:\.alvera-data), in Dev-Data or Test-Data.
  This lists every user database, where its files are, and flags a database that has any file outside the root: a stray created by hand
  or by a tool that did not give an explicit path. The system databases (master, model, msdb, tempdb) belong to the SQL Server install and are not checked.

.EXAMPLE
  .\scripts\Test-DataLocations.ps1
#>
[CmdletBinding()]
param()

# The repository folder name contains [ ], which PowerShell treats as a wildcard: escape the path, and stop at once if the shared settings cannot load.
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))

$root = ($DataRoot.TrimEnd('\') + '\')
$rows = Invoke-DevSql "SET NOCOUNT ON; SELECT d.name + N'|' + f.physical_name FROM sys.databases d JOIN sys.master_files f ON f.database_id = d.database_id WHERE d.database_id > 4 ORDER BY d.name, f.file_id;"

$stray = New-Object System.Collections.Generic.List[string]
$total = New-Object System.Collections.Generic.HashSet[string]
foreach ($row in @($rows | Where-Object { $_ -and $_.Trim() })) {
    $name, $path = $row.Trim().Split('|', 2)
    [void]$total.Add($name)
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        $stray.Add("$name : $path")
    }
}

Write-Host "Data root: $DataRoot   SQL Server: $SqlServer   user databases: $($total.Count)"
if ($stray.Count -eq 0) {
    Write-Host 'OK: every user database file is under the data root.'
    exit 0
}
Write-Host "OUTSIDE THE ROOT ($($stray.Count) file(s)):"
$stray | ForEach-Object { Write-Host "  $_" }
exit 1
