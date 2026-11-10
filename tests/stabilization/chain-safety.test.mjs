// ALV-N019 (R05 PKG-01): proves the accumulated-chain runner's safety rules refuse what they must and contain what they delete.
//
// Run with: node --test tests/stabilization/chain-safety.test.mjs
// Uses only a temporary folder and PowerShell; it never connects to SQL Server and never touches AlveraDev, Dev-Data or any real data root.

import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const root = path.resolve(import.meta.dirname, "..", "..");
const safety = path.join(root, "scripts", "chain", "ChainSafety.ps1");
const RUN = "0123456789abcdef0123456789abcdef";
const OTHER = "fedcba9876543210fedcba9876543210";

/** Runs PowerShell statements after loading ChainSafety.ps1; each `Probe '<label>' { ... }` prints "<label>=OK" or "<label>=REFUSED". */
function ps(dataRoot, body) {
  const script = [
    "$ErrorActionPreference = 'Stop'",
    ". ([WildcardPattern]::Escape($env:SAFETY))",
    "function Probe([string]$l, [scriptblock]$s) { try { & $s | Out-Null; \"$l=OK\" } catch { \"$l=REFUSED\" } }",
    "$DataRoot = $env:DATA_ROOT",
    body,
  ].join("\n");
  const r = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], { encoding: "utf8", env: { ...process.env, SAFETY: safety, DATA_ROOT: dataRoot } });
  assert.equal(r.status, 0, `powershell failed: ${r.stdout}\n${r.stderr}`);
  return Object.fromEntries(r.stdout.split(/\r?\n/).filter((l) => l.includes("=")).map((l) => l.split("=")));
}

function withDataRoot(fn) {
  const dataRoot = mkdtempSync(path.join(tmpdir(), "chainsafe-"));
  try { return fn(dataRoot); } finally { rmSync(dataRoot, { recursive: true, force: true }); }
}

test("database names: only AlveraChain_<this run id> is accepted; AlveraDev and any other name are refused", () => {
  withDataRoot((d) => {
    const r = ps(d, `
Probe 'own' { Assert-ChainDatabase 'AlveraChain_${RUN}' '${RUN}' }
Probe 'dev' { Assert-ChainDatabase 'AlveraDev' '${RUN}' }
Probe 'otherRun' { Assert-ChainDatabase 'AlveraChain_${OTHER}' '${RUN}' }
Probe 'walk' { Assert-ChainDatabase 'AlveraWalk_x_12345678' '${RUN}' }
Probe 'empty' { Assert-ChainDatabase '' '${RUN}' }
Probe 'injection' { Assert-ChainDatabase 'AlveraChain_${RUN}]; DROP DATABASE [AlveraDev' '${RUN}' }
Probe 'badRunId' { Get-ChainDatabaseName 'not-a-run-id' }
Probe 'upperRunId' { Get-ChainDatabaseName '${RUN.toUpperCase()}' }
`);
    assert.deepEqual(r, { own: "OK", dev: "REFUSED", otherRun: "REFUSED", walk: "REFUSED", empty: "REFUSED", injection: "REFUSED", badRunId: "REFUSED", upperRunId: "REFUSED" });
  });
});

test("run folder: under <root>\\Test-Data\\Chain\\<run id>; a relative data root is refused", () => {
  withDataRoot((d) => {
    const r = ps(d, `
Probe 'ok' { Get-ChainRunRoot $DataRoot '${RUN}' }
Probe 'relative' { Get-ChainRunRoot 'relative\\root' '${RUN}' }
Probe 'badId' { Get-ChainRunRoot $DataRoot '..\\..\\Dev-Data' }
"leaf=" + (Split-Path -Leaf (Get-ChainRunRoot $DataRoot '${RUN}'))
"parent=" + (Split-Path -Leaf (Split-Path -Parent (Get-ChainRunRoot $DataRoot '${RUN}')))
`);
    assert.equal(r.ok, "OK"); assert.equal(r.relative, "REFUSED"); assert.equal(r.badId, "REFUSED");
    assert.equal(r.leaf, RUN); assert.equal(r.parent, "Chain");
  });
});

test("run-owned paths: inside the run folder is accepted; the folder itself, siblings, '..' escapes, Dev-Data and other drives are refused", () => {
  withDataRoot((d) => {
    const r = ps(d, `
$run = Get-ChainRunRoot $DataRoot '${RUN}'
Probe 'inside' { Assert-RunOwnedPath (Join-Path $run 'Database\\x.mdf') $run $DataRoot }
Probe 'self' { Assert-RunOwnedPath $run $run $DataRoot }
Probe 'sibling' { Assert-RunOwnedPath (Join-Path (Split-Path -Parent $run) '${OTHER}\\x') $run $DataRoot }
Probe 'prefixTrick' { Assert-RunOwnedPath ($run + 'evil\\x') $run $DataRoot }
Probe 'dotdot' { Assert-RunOwnedPath (Join-Path $run '..\\..\\..\\Dev-Data\\Database\\AlveraDev.mdf') $run $DataRoot }
Probe 'devData' { Assert-RunOwnedPath (Join-Path $DataRoot 'Dev-Data\\Backup\\x.abk') $run $DataRoot }
Probe 'relative' { Assert-RunOwnedPath 'Database\\x.mdf' $run $DataRoot }
Probe 'otherDrive' { Assert-RunOwnedPath 'Z:\\somewhere\\x' $run $DataRoot }
`);
    assert.deepEqual(r, { inside: "OK", self: "REFUSED", sibling: "REFUSED", prefixTrick: "REFUSED", dotdot: "REFUSED", devData: "REFUSED", relative: "REFUSED", otherDrive: "REFUSED" });
  });
});

test("evidence path: only the run's own folder under Test-Data\\Temp\\chain-output", () => {
  withDataRoot((d) => {
    const r = ps(d, `
$ev = Join-Path $DataRoot 'Test-Data\\Temp\\chain-output\\${RUN}'
Probe 'ok' { Assert-EvidencePath $ev $DataRoot '${RUN}' }
Probe 'inside' { Assert-EvidencePath (Join-Path $ev 'evidence.json') $DataRoot '${RUN}' }
Probe 'otherRun' { Assert-EvidencePath (Join-Path $DataRoot 'Test-Data\\Temp\\chain-output\\${OTHER}') $DataRoot '${RUN}' }
Probe 'devData' { Assert-EvidencePath (Join-Path $DataRoot 'Dev-Data') $DataRoot '${RUN}' }
Probe 'parent' { Assert-EvidencePath (Join-Path $DataRoot 'Test-Data') $DataRoot '${RUN}' }
`);
    assert.deepEqual(r, { ok: "OK", inside: "OK", otherRun: "REFUSED", devData: "REFUSED", parent: "REFUSED" });
  });
});

test("cleanup containment: deleting inside the run works; deleting Dev-Data, a sibling run or an arbitrary folder is refused and the files survive", () => {
  withDataRoot((d) => {
    const runDir = path.join(d, "Test-Data", "Chain", RUN);
    const otherDir = path.join(d, "Test-Data", "Chain", OTHER);
    const devFile = path.join(d, "Dev-Data", "Database", "AlveraDev.mdf");
    const realBackup = path.join(d, "Dev-Data", "Backup", "real.abk");
    const plainDir = path.join(d, "Test-Data", "NotARun");
    for (const f of [path.join(runDir, "Storage", "doc.bin"), path.join(otherDir, "Storage", "keep.bin"), devFile, realBackup, path.join(plainDir, "keep.txt")]) {
      mkdirSync(path.dirname(f), { recursive: true }); writeFileSync(f, "x");
    }
    const r = ps(d, `
$run = Get-ChainRunRoot $DataRoot '${RUN}'
Probe 'devData' { Remove-RunOwnedTree (Join-Path $DataRoot 'Dev-Data') $run $DataRoot -AllowRunRoot }
Probe 'devFile' { Remove-RunOwnedTree '${devFile.replace(/\\/g, "\\\\")}' $run $DataRoot }
Probe 'backup' { Remove-RunOwnedTree '${realBackup.replace(/\\/g, "\\\\")}' $run $DataRoot }
Probe 'sibling' { Remove-RunOwnedTree '${otherDir.replace(/\\/g, "\\\\")}' $run $DataRoot }
Probe 'plainDirAsRoot' { Remove-RunOwnedTree '${plainDir.replace(/\\/g, "\\\\")}' '${plainDir.replace(/\\/g, "\\\\")}' $DataRoot -AllowRunRoot }
Probe 'runRootWithoutFlag' { Remove-RunOwnedTree $run $run $DataRoot }
Probe 'sub' { Remove-RunOwnedTree (Join-Path $run 'Storage') $run $DataRoot }
`);
    assert.deepEqual(r, { devData: "REFUSED", devFile: "REFUSED", backup: "REFUSED", sibling: "REFUSED", plainDirAsRoot: "REFUSED", runRootWithoutFlag: "REFUSED", sub: "OK" });
    assert.ok(existsSync(devFile) && existsSync(realBackup) && existsSync(path.join(otherDir, "Storage", "keep.bin")) && existsSync(path.join(plainDir, "keep.txt")), "a refused delete removed something");
    assert.ok(!existsSync(path.join(runDir, "Storage")), "the run's own subfolder was not removed");

    const end = ps(d, `
$run = Get-ChainRunRoot $DataRoot '${RUN}'
Probe 'runRoot' { Remove-RunOwnedTree $run $run $DataRoot -AllowRunRoot }
Probe 'again' { Remove-RunOwnedTree $run $run $DataRoot -AllowRunRoot }
`);
    assert.deepEqual(end, { runRoot: "OK", again: "OK" }, "removing the run folder is allowed and safe to repeat");
    assert.ok(!existsSync(runDir) && existsSync(devFile), "the run folder is gone and Dev-Data is untouched");
  });
});
