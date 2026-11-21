# Lists the import and delay-import DLL names of a PE file by reading its headers directly (no external tools).
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$b = [IO.File]::ReadAllBytes($Path)
$pe = [BitConverter]::ToInt32($b, 0x3C)
if ([BitConverter]::ToUInt32($b, $pe) -ne 0x4550) { throw 'not a PE file' }
$machine = [BitConverter]::ToUInt16($b, $pe + 4)
$opt = $pe + 24
$magic = [BitConverter]::ToUInt16($b, $opt)
$dd = if ($magic -eq 0x20B) { $opt + 112 } else { $opt + 96 }
$nsec = [BitConverter]::ToUInt16($b, $pe + 6)
$sec = $opt + [BitConverter]::ToUInt16($b, $pe + 20)
function Convert-Rva([uint32]$rva) {
    for ($i = 0; $i -lt $nsec; $i++) {
        $s = $sec + 40 * $i; $va = [BitConverter]::ToUInt32($b, $s + 12)
        $size = [Math]::Max([BitConverter]::ToUInt32($b, $s + 8), [BitConverter]::ToUInt32($b, $s + 16))
        if ($rva -ge $va -and $rva -lt $va + $size) { return $rva - $va + [BitConverter]::ToUInt32($b, $s + 20) }
    }
    throw "RVA $rva not in any section"
}
function Read-CString([int]$o) { $e = $o; while ($b[$e]) { $e++ }; [Text.Encoding]::ASCII.GetString($b, $o, $e - $o) }
'file: ' + (Split-Path -Leaf $Path)
'sha256: ' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
'machine: 0x{0:X4}  optional-header magic: 0x{1:X3}' -f $machine, $magic
foreach ($entry in @(@{ idx = 1; label = 'import'; size = 20; nameAt = 12 }, @{ idx = 13; label = 'delay-import'; size = 32; nameAt = 4 })) {
    $rva = [BitConverter]::ToUInt32($b, $dd + 8 * $entry.idx)
    if (-not $rva) { "$($entry.label): (none)"; continue }
    $o = Convert-Rva $rva; $count = 0
    while ($n = [BitConverter]::ToUInt32($b, $o + $entry.nameAt)) { "$($entry.label): " + (Read-CString (Convert-Rva $n)); $o += $entry.size; $count++ }
    "$($entry.label) count: $count"
}
