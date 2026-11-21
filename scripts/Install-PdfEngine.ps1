<#
.SYNOPSIS
  ALV-N026 S4: provisions the PDF engine (Typst) and its fonts for the dev app or the tests, from the owner-controlled release store, checked against the reviewed manifest.
.DESCRIPTION
  Trust source: build\pdf-engine.manifest.json (reviewed, version-controlled). The store's SHA256SUMS.txt files are never read.
  - Source: <root>\release-artifacts (ALVERA_DATA_ROOT, default C:\.alvera-data). Nothing is downloaded.
  - Target: <root>\Dev-Data\PdfEngine (default) or <root>\Test-Data\PdfEngine (-ForTests).
  - Checks: the Typst zip against the manifest BEFORE unpacking; only typst.exe is taken from it, and checked; both fonts checked;
    vc_redist.x64.exe checked (hash, and a Valid Authenticode signature from Microsoft) but NOT installed - installing the runtime is the installer's job (ALV-N013).
  - Idempotent: a target that already holds exactly the pinned files is left alone; one that holds anything different is refused (never overwritten).
  - Any check that fails stops the run and leaves no half-written target (work is done in a temporary folder and moved into place at the end).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-PdfEngine.ps1 -ForTests
#>
param([switch]$ForTests)
$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape("$PSScriptRoot\DevEnvironment.ps1"))
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function Refuse([string]$why) { throw "REFUSED: $why" }

$manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'build\pdf-engine.manifest.json') -Raw | ConvertFrom-Json
$store = Join-Path $DataRoot 'release-artifacts'
$target = Join-Path $DataRoot ($(if ($ForTests) { 'Test-Data' } else { 'Dev-Data' })) | Join-Path -ChildPath 'PdfEngine'
$expected = [ordered]@{ 'typst.exe' = $manifest.engine.exeSha256 }
foreach ($f in $manifest.fonts.files.PSObject.Properties) { $expected["fonts\$($f.Name)"] = $f.Value }

# Already provisioned: exactly the pinned files and nothing else, or refuse.
if (Test-Path -LiteralPath $target) {
    $present = @(Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object { $_.FullName.Substring($target.Length + 1) })
    $extra = @($present | Where-Object { -not $expected.Contains($_) })
    $wrong = @($expected.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $target $_)) -or (Sha (Join-Path $target $_)) -ne $expected[$_] })
    if ($extra.Count -eq 0 -and $wrong.Count -eq 0) { Write-Host "Already provisioned and verified: $target"; exit 0 }
    Refuse "$target exists but does not hold exactly the pinned files (unexpected: $($extra -join ', '); wrong or missing: $($wrong -join ', ')). It is never overwritten: remove it deliberately, then run again."
}

# The runtime package: checked, never installed here.
$redist = Join-Path $store (Join-Path $manifest.msvcRuntime.storeFolder $manifest.msvcRuntime.redistFile)
if (-not (Test-Path -LiteralPath $redist)) { Refuse "the runtime package is missing from the store." }
if ((Sha $redist) -ne $manifest.msvcRuntime.redistSha256) { Refuse "the runtime package does not match the manifest." }
$sig = Get-AuthenticodeSignature -LiteralPath $redist
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch [regex]::Escape("O=$($manifest.msvcRuntime.requiredSignerOrganization)")) { Refuse "the runtime package is not validly signed by $($manifest.msvcRuntime.requiredSignerOrganization)." }

$work = "$target.partial-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $work 'fonts') -Force | Out-Null
try {
    $zip = Join-Path $store (Join-Path $manifest.engine.storeFolder $manifest.engine.zipFile)
    if (-not (Test-Path -LiteralPath $zip)) { Refuse "the engine zip is missing from the store." }
    if ((Sha $zip) -ne $manifest.engine.zipSha256) { Refuse "the engine zip does not match the manifest." }
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq $manifest.engine.exeEntry } | Select-Object -First 1
        if (-not $entry) { Refuse "the engine zip has no $($manifest.engine.exeEntry)." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $work 'typst.exe'))
    } finally { $archive.Dispose() }
    if ((Sha (Join-Path $work 'typst.exe')) -ne $manifest.engine.exeSha256) { Refuse "typst.exe from the zip does not match the manifest." }

    foreach ($f in $manifest.fonts.files.PSObject.Properties) {
        $from = Join-Path $store (Join-Path $manifest.fonts.storeFolder $f.Name)
        if (-not (Test-Path -LiteralPath $from)) { Refuse "the font $($f.Name) is missing from the store." }
        Copy-Item -LiteralPath $from -Destination (Join-Path $work "fonts\$($f.Name)")
        if ((Sha (Join-Path $work "fonts\$($f.Name)")) -ne $f.Value) { Refuse "the font $($f.Name) does not match the manifest." }
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Move-Item -LiteralPath $work -Destination $target
} catch {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    throw
}
Write-Host "Provisioned and verified: $target"
Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object { '{0}  {1}' -f (Sha $_.FullName), $_.FullName.Substring($target.Length + 1) }
