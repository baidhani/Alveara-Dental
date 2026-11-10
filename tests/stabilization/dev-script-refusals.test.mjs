// ALV-N019 (R05 PKG-15, F-011 and the S-01 to S-08 startup experiments): the dev start scripts refuse a busy port or a missing database with ONE plain line and exit code 1,
// not a PowerShell stack block.
//
// Run with: node --test tests/stabilization/dev-script-refusals.test.mjs
// The scripts run from a temporary copy beside a copy of DevEnvironment.ps1 whose Test-PortInUse and Invoke-DevSql are replaced, so no port is opened, no SQL Server is contacted
// and AlveraDev is never looked at.

import { test } from "node:test";
import assert from "node:assert/strict";
import { copyFileSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";

const scripts = path.resolve(import.meta.dirname, "..", "..", "scripts");

function runScript(name, overrides) {
  const dir = mkdtempSync(path.join(tmpdir(), "devscript-"));
  try {
    copyFileSync(path.join(scripts, name), path.join(dir, name));
    writeFileSync(path.join(dir, "DevEnvironment.ps1"), readFileSync(path.join(scripts, "DevEnvironment.ps1"), "utf8") + "\n" + overrides + "\n");
    const r = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path.join(dir, name)], { encoding: "utf8", timeout: 60_000 });
    const lines = `${r.stdout}\n${r.stderr}`.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    return { status: r.status, lines };
  } finally { rmSync(dir, { recursive: true, force: true }); }
}

const STACK = /\bAt .*\.ps1:\d+|CategoryInfo|FullyQualifiedErrorId|ScriptStackTrace|\+ ~/;

function assertOneLineRefusal({ status, lines }, expected) {
  assert.equal(status, 1, `exit code (output: ${lines.join(" | ")})`);
  assert.equal(lines.length, 1, `exactly one output line, got: ${lines.join(" | ")}`);
  assert.match(lines[0], expected);
  assert.doesNotMatch(lines.join("\n"), STACK, "no stack block");
}

test("Start-DevApi.ps1 with port 5072 busy: one line, exit 1 (S-01)", () => {
  assertOneLineRefusal(runScript("Start-DevApi.ps1", "function Test-PortInUse([int]$Port) { return $true }\nfunction Invoke-DevSql { throw 'must not be reached' }"), /^Port 5072 is already in use: an API is already running\./);
});

test("Start-DevClient.ps1 with port 5173 busy: one line, exit 1 (S-02)", () => {
  assertOneLineRefusal(runScript("Start-DevClient.ps1", "function Test-PortInUse([int]$Port) { return $true }"), /^Port 5173 is already in use: a client is already running\./);
});

test("Start-DevApi.ps1 with no AlveraDev database: one line naming the fix, exit 1 (S-06)", () => {
  assertOneLineRefusal(runScript("Start-DevApi.ps1", "function Test-PortInUse([int]$Port) { return $false }\nfunction Invoke-DevSql([string]$Sql, [string]$Database = 'master') { return @('0') }"),
    /^AlveraDev does not exist on .+\. Run \.\\scripts\\Initialize-DevDatabase\.ps1 first\.$/);
});

test("the refusals are the only change: the stop helper prints to the error stream and exits 1 without throwing", () => {
  const env = readFileSync(path.join(scripts, "DevEnvironment.ps1"), "utf8");
  assert.match(env, /function Stop-DevScript[\s\S]*?\[Console\]::Error\.WriteLine\(\$Message\)[\s\S]*?exit 1/);
  for (const name of ["Start-DevApi.ps1", "Start-DevClient.ps1"]) {
    const text = readFileSync(path.join(scripts, name), "utf8");
    assert.doesNotMatch(text, /Test-PortInUse[^\n]*\n\s*throw|if \(-not \$exists\) \{ throw/, `${name} still throws on a refusal`);
  }
});

test("idempotent: the same refusal twice gives the same exit code and line", () => {
  const stub = "function Test-PortInUse([int]$Port) { return $true }";
  assert.deepEqual(runScript("Start-DevClient.ps1", stub), runScript("Start-DevClient.ps1", stub));
});
