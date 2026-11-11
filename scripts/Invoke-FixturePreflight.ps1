<#
.SYNOPSIS
  ALV-N019 R04 fixture preflight: proves the test fixture's pool warm-up against the REAL SqlClient pool-pruning condition.

.DESCRIPTION
  Runs two arms through the test project's FixtureForcedPruningTests (see src/Alveara.Api.Tests/FixtureForcedPruningTests.cs):
    1. CONTROL   - warm-up disabled, -ControlCycles forced cycles (default 800); it MUST show at least one authentic
                   first-open pool-acquisition timeout (otherwise the forcing is not proven to reach the race).
    2. TREATMENT - warm-up enabled, -Iterations iterations of -Cycles forced cycles (default 60 x 15); every cycle's first
                   pooled open must succeed and every iteration must leave 0 AlveraTest_* databases and 0 files.
  Each arm writes its raw per-iteration JSON lines (control.jsonl, treatment.jsonl) to -OutFolder, and this script then
  re-verifies those files independently of the test assertions and writes SUMMARY.txt. Exit code 0 only if everything holds.

  Safety: it only touches the isolated AlveraTest_* databases and the Test-Data\Database files that the fixture itself creates and
  removes (the test project's own safety gate refuses any other target). It never touches AlveraDev. It needs the test project to be
  built (dotnet build) and a local SQL Server .\SQLEXPRESS. Not part of the normal test suite; no side effects outside OutFolder.

.EXAMPLE
  powershell -File scripts/Invoke-FixturePreflight.ps1 -OutFolder C:\tmp\r04-preflight
#>
param(
  [Parameter(Mandatory = $true)][string]$OutFolder,
  [int]$ControlCycles = 800,
  [int]$Iterations = 60,
  [int]$Cycles = 15
)

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetDirectoryName($PSScriptRoot)   # not Split-Path: it treats the [ ] of this repository's folder name as a wildcard
$project = [System.IO.Path]::Combine($repo, 'src', 'Alveara.Api.Tests')
New-Item -ItemType Directory -Force -Path $OutFolder | Out-Null

function Invoke-Arm([string]$name, [string]$filter, [hashtable]$vars) {
  foreach ($k in $vars.Keys) { Set-Item -Path "Env:$k" -Value $vars[$k] }
  $log = [System.IO.Path]::Combine($OutFolder, "$name-console.txt")
  $errLog = [System.IO.Path]::Combine($OutFolder, "$name-stderr.txt")
  # System.Diagnostics.Process through cmd.exe redirection: Start-Process and '*>' both break here (the [ ] of this repository's folder name is read as a wildcard,
  # and Windows PowerShell 5.1 turns native stderr text into a terminating error under $ErrorActionPreference = 'Stop').
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'cmd.exe'
  $psi.Arguments = "/c dotnet test --no-build --nologo --filter `"$filter`" --logger `"console;verbosity=detailed`" > `"$log`" 2> `"$errLog`""
  $psi.WorkingDirectory = $project
  $psi.UseShellExecute = $false
  $proc = [System.Diagnostics.Process]::Start($psi)
  $proc.WaitForExit()
  $code = $proc.ExitCode
  foreach ($k in $vars.Keys) { Remove-Item -Path "Env:$k" -ErrorAction SilentlyContinue }
  return $code
}

$started = Get-Date
$common = @{ ALVERA_R04_PREFLIGHT_OUT = $OutFolder }
$controlCode = Invoke-Arm 'control' 'FullyQualifiedName~Control_arm' ($common + @{ ALVERA_R04_CONTROL_CYCLES = "$ControlCycles"; ALVERA_R04_REQUIRE_CONTROL_FAILURE = '1' })
$treatmentCode = Invoke-Arm 'treatment' 'FullyQualifiedName~Treatment_arm' ($common + @{ ALVERA_R04_ITERATIONS = "$Iterations"; ALVERA_R04_CYCLES = "$Cycles" })

# independent verification of the raw per-iteration output
$problems = New-Object System.Collections.Generic.List[string]
$control = @(Get-Content -LiteralPath ([System.IO.Path]::Combine($OutFolder, 'control.jsonl')) | ForEach-Object { $_ | ConvertFrom-Json })
$treatment = @(Get-Content -LiteralPath ([System.IO.Path]::Combine($OutFolder, 'treatment.jsonl')) | ForEach-Object { $_ | ConvertFrom-Json })
$controlCycles = ($control | Measure-Object -Property Cycles -Sum).Sum
$controlTimeouts = ($control | Measure-Object -Property FirstOpenTimeouts -Sum).Sum
if ($controlCycles -lt 800) { $problems.Add("control ran $controlCycles cycles (< 800)") }
if ($controlTimeouts -lt 1) { $problems.Add('control showed no authentic first-open timeout') }
if ($treatment.Count -ne $Iterations) { $problems.Add("treatment ran $($treatment.Count) iterations (expected $Iterations)") }
foreach ($it in $treatment) {
  if ($it.Cycles -ne $Cycles) { $problems.Add("iteration $($it.Iteration): $($it.Cycles) cycles") }
  if ($it.Ok -ne $it.Cycles) { $problems.Add("iteration $($it.Iteration): ok $($it.Ok) of $($it.Cycles)") }
  if ($it.DatabasesAfter -ne 0 -or $it.FilesAfter -ne 0) { $problems.Add("iteration $($it.Iteration): databases left $($it.DatabasesAfter), files left $($it.FilesAfter)") }
  if (-not $it.Passed) { $problems.Add("iteration $($it.Iteration) not passed") }
}
if ($controlCode -ne 0) { $problems.Add("control arm exit code $controlCode") }
if ($treatmentCode -ne 0) { $problems.Add("treatment arm exit code $treatmentCode") }

$treatmentCyclesTotal = ($treatment | Measure-Object -Property Cycles -Sum).Sum
$warmRetries = 0
foreach ($it in $treatment) { foreach ($d in $it.Detail) { $warmRetries += $d.WarmUpRetries } }
$summary = @(
  "ALV-N019 R04 fixture preflight: $(if ($problems.Count -eq 0) { 'PASS' } else { 'FAIL' })",
  "started $($started.ToString('s')), ended $((Get-Date).ToString('s'))",
  "control arm (warm-up OFF): $controlCycles forced cycles, $controlTimeouts authentic first-open pool-acquisition timeouts (required: at least 1 in at least 800 cycles)",
  "treatment arm (warm-up ON): $($treatment.Count) iterations x $Cycles cycles = $treatmentCyclesTotal forced cycles; first-open successes $(($treatment | Measure-Object -Property Ok -Sum).Sum); warm-up races recovered by the bounded retry: $warmRetries",
  "per-iteration cleanup check: fixture databases left $(($treatment | Measure-Object -Property DatabasesAfter -Sum).Sum), files left $(($treatment | Measure-Object -Property FilesAfter -Sum).Sum) (summed over all iterations; required 0)"
)
if ($problems.Count -gt 0) { $summary += 'PROBLEMS:'; $summary += $problems }
$summary | Set-Content -LiteralPath ([System.IO.Path]::Combine($OutFolder, 'SUMMARY.txt')) -Encoding utf8
$summary
if ($problems.Count -gt 0) { exit 1 }
exit 0
