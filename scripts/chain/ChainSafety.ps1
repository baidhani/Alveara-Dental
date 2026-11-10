# ALV-N019 (R05 PKG-01): the safety rules of the accumulated-chain runner, as small functions that can be tested without SQL Server.
#
# The chain runner owns ONE throw-away environment per run: a database named AlveraChain_<32 hex run id> and a folder <root>\Test-Data\Chain\<run id>
# that holds its database files, storage, key ring and backup. These functions are the only gate between the runner and the file system / SQL Server:
# a name or path that is not provably the run's own is refused with a throw, so the runner can never read, write, reset or delete AlveraDev, Dev-Data,
# a real backup, or anything outside its run folder.

Set-StrictMode -Version Latest

$script:ChainDatabasePattern = '^AlveraChain_[0-9a-f]{32}$'
$script:ChainRunIdPattern    = '^[0-9a-f]{32}$'

function New-ChainRunId { return [Guid]::NewGuid().ToString('N') }

function Assert-ChainRunId([string]$RunId) {
    if ($RunId -cnotmatch $script:ChainRunIdPattern) { throw "Refusing: '$RunId' is not a chain run id (32 lower-case hex characters)." }
}

function Get-ChainDatabaseName([string]$RunId) {
    Assert-ChainRunId $RunId
    return "AlveraChain_$RunId"
}

# Only a database this runner created (AlveraChain_<run id>) may be created, written, backed up or dropped. AlveraDev and every other name are refused.
function Assert-ChainDatabase([string]$Database, [string]$RunId) {
    Assert-ChainRunId $RunId
    if ($Database -cne "AlveraChain_$RunId") { throw "Refusing database '$Database': a chain run may only touch its own AlveraChain_$RunId." }
    if ($Database -cnotmatch $script:ChainDatabasePattern) { throw "Refusing database '$Database': not a chain database name." }
}

function Resolve-FullPathNoLinks([string]$Path) {
    # Normalises '..', '.' and mixed separators without needing the path to exist. Relative paths are refused first.
    if (-not [System.IO.Path]::IsPathRooted($Path)) { throw "Refusing relative path '$Path'." }
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-IsInside([string]$Child, [string]$Parent) {
    $c = (Resolve-FullPathNoLinks $Child); $p = (Resolve-FullPathNoLinks $Parent)
    return $c.StartsWith($p + '\', [StringComparison]::OrdinalIgnoreCase)
}

# The run folder for one run: <DataRoot>\Test-Data\Chain\<run id>. Refused when the data root is not absolute or the result would not be under Test-Data.
function Get-ChainRunRoot([string]$DataRoot, [string]$RunId) {
    Assert-ChainRunId $RunId
    $testData = Resolve-FullPathNoLinks (Join-Path $DataRoot 'Test-Data')
    $runRoot  = Resolve-FullPathNoLinks (Join-Path (Join-Path $testData 'Chain') $RunId)
    if (-not (Test-IsInside $runRoot $testData)) { throw "Refusing run folder '$runRoot': not under '$testData'." }
    return $runRoot
}

# A path the run may create, write or delete: strictly inside its own run folder, never the folder itself, never anywhere under Dev-Data.
function Assert-RunOwnedPath([string]$Path, [string]$RunRoot, [string]$DataRoot) {
    $devData = Resolve-FullPathNoLinks (Join-Path $DataRoot 'Dev-Data')
    $full    = Resolve-FullPathNoLinks $Path
    if ($full -eq $devData -or (Test-IsInside $full $devData)) { throw "Refusing '$full': it is under Dev-Data." }
    if (-not (Test-IsInside $full $RunRoot)) { throw "Refusing '$full': it is not inside this run's folder '$RunRoot'." }
    return $full
}

# Output (evidence) lives beside the run folder, still under Test-Data, and is kept after the run. It must never be in or above Dev-Data.
function Assert-EvidencePath([string]$Path, [string]$DataRoot, [string]$RunId) {
    Assert-ChainRunId $RunId
    $expected = Resolve-FullPathNoLinks (Join-Path (Join-Path (Join-Path (Join-Path $DataRoot 'Test-Data') 'Temp') 'chain-output') $RunId)
    $full = Resolve-FullPathNoLinks $Path
    if ($full -ne $expected -and -not (Test-IsInside $full $expected)) { throw "Refusing evidence path '$full': it is not '$expected' or inside it." }
    return $full
}

# Deletes a run-owned folder tree. The run folder itself may be removed at the end of a run (AllowRunRoot); anything else must pass Assert-RunOwnedPath.
function Remove-RunOwnedTree([string]$Path, [string]$RunRoot, [string]$DataRoot, [switch]$AllowRunRoot) {
    $full = Resolve-FullPathNoLinks $Path
    if ($AllowRunRoot -and $full -eq (Resolve-FullPathNoLinks $RunRoot)) {
        $testData = Resolve-FullPathNoLinks (Join-Path $DataRoot 'Test-Data')
        if (-not (Test-IsInside $full $testData)) { throw "Refusing to delete '$full': not under '$testData'." }
        $leaf = Split-Path -Leaf $full
        if ($leaf -cnotmatch $script:ChainRunIdPattern) { throw "Refusing to delete '$full': its name is not a run id." }
    }
    else { $full = Assert-RunOwnedPath $Path $RunRoot $DataRoot }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

# The commit a run is stamped with. A tree with any uncommitted or untracked change is labelled, so the evidence never claims a commit it did not run.
function Get-ChainImplementationSha([string]$RepoRoot) {
    $sha = (& git -C $RepoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or "$sha".Trim() -notmatch '^[0-9a-f]{40}$') { throw "Cannot read the implementation commit of '$RepoRoot'." }
    $sha = "$sha".Trim()
    if (@(& git -C $RepoRoot status --porcelain).Count -gt 0) { return "$sha+uncommitted-changes" }
    return $sha
}
