// ALV-N019 R02 (review correction 02): the accumulated-chain runner's self-tests for resilience.
//   - dirty-tree label: the commit a run is stamped with names uncommitted or untracked changes (no SQL Server needed);
//   - refusal when the target database already exists: nothing is created and the existing database is NOT dropped (SQL Server);
//   - refusal when the run folder already exists: nothing is created and the existing folder is NOT deleted (SQL Server);
//   - interrupted run: the API is killed right after start-up and the owned database and run folder are still removed (SQL Server, the built API, ports 5072 and 5173 free).
// There is no resume feature: every run is a fresh run and its evidence says so. (A hard kill of the runner process itself cannot run its cleanup; that case is not covered.)
//
// Run with: node --test tests/stabilization/chain-runner-resilience.test.mjs
// The tests that need SQL Server or the ports are skipped, with the reason printed, on a machine that has neither. They only ever create, look at and drop databases and folders named by their own random run id.

import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { randomBytes } from "node:crypto";
import { spawnSync } from "node:child_process";

const root = path.resolve(import.meta.dirname, "..", "..");
const runner = path.join(root, "scripts", "Invoke-AccumulatedChain.ps1");
const safety = path.join(root, "scripts", "chain", "ChainSafety.ps1");
const dataRoot = process.env.ALVERA_DATA_ROOT ?? "C:\\.alvera-data";
const sqlServer = process.env.ALVERA_SQL_SERVER ?? ".\\SQLEXPRESS";
const newRunId = () => randomBytes(16).toString("hex");

function ps(script, env = {}, timeout = 120_000) {
  const r = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script], { encoding: "utf8", timeout, env: { ...process.env, ...env } });
  return { status: r.status, out: `${r.stdout}\n${r.stderr}` };
}
const sql = (query) => spawnSync("sqlcmd", ["-S", sqlServer, "-E", "-b", "-h", "-1", "-W", "-Q", query], { encoding: "utf8", timeout: 60_000 });
const sqlUp = (() => { try { return sql("SELECT 1").status === 0; } catch { return false; } })();
const portsFree = (() => { const r = ps("if (Get-NetTCPConnection -LocalPort 5072,5173 -State Listen -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"); return r.status === 0; })();
const apiBuilt = existsSync(path.join(root, "src", "Alveara.Api", "bin", "Debug", "net10.0", "Alveara.Api.dll"));
const skipSql = sqlUp ? false : `SQL Server ${sqlServer} is not reachable with sqlcmd`;
const skipPorts = skipSql || (portsFree ? false : "port 5072 or 5173 is in use (the runner checks the ports first)");
const skipRun = skipSql || (portsFree ? false : "port 5072 or 5173 is in use") || (apiBuilt ? false : "the API has not been built (dotnet build src/Alveara.Api)");
const dbExists = (db) => sql(`SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'${db}'`).stdout.trim() === "1";
// powershell -Command does not pass a script's exit code on by itself: end with its $LASTEXITCODE
const runnerCommand = (args) => `& ([WildcardPattern]::Escape($env:RUNNER)) ${args}; exit $LASTEXITCODE`;
const runnerEnv = { RUNNER: runner };

// ---------- dirty-tree label (no SQL) ----------

function repoWith(setup) {
  const dir = mkdtempSync(path.join(tmpdir(), "chainlabel-"));
  const git = (...a) => { const r = spawnSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", ...a], { cwd: dir, encoding: "utf8" }); assert.equal(r.status, 0, r.stderr); return r.stdout.trim(); };
  git("init", "-q"); writeFileSync(path.join(dir, "a.txt"), "one\n"); git("add", "a.txt"); git("commit", "-q", "-m", "first");
  setup?.(dir, git);
  return { dir, sha: git("rev-parse", "HEAD") };
}
const labelOf = (dir) => ps(". ([WildcardPattern]::Escape($env:SAFETY)); Get-ChainImplementationSha $env:REPO", { SAFETY: safety, REPO: dir });

test("a clean tree is stamped with its exact commit and no label", () => {
  const { dir, sha } = repoWith();
  try { const r = labelOf(dir); assert.equal(r.status, 0, r.out); assert.equal(r.out.trim(), sha); assert.match(sha, /^[0-9a-f]{40}$/); } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("a modified tracked file labels the commit +uncommitted-changes", () => {
  const { dir, sha } = repoWith((d) => writeFileSync(path.join(d, "a.txt"), "two\n"));
  try { assert.equal(labelOf(dir).out.trim(), `${sha}+uncommitted-changes`); } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("an untracked file also labels the commit +uncommitted-changes", () => {
  const { dir, sha } = repoWith((d) => writeFileSync(path.join(d, "new.txt"), "x\n"));
  try { assert.equal(labelOf(dir).out.trim(), `${sha}+uncommitted-changes`); } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("a folder that is not a repository is refused, never stamped with an invented commit", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "chainnogit-"));
  try { const r = labelOf(dir); assert.notEqual(r.status, 0); assert.match(r.out, /Cannot read the implementation commit|not a git repository/i); } finally { rmSync(dir, { recursive: true, force: true }); }
});

// ---------- refusals when the target already exists (SQL Server) ----------

test("an existing database with the run's name is refused and is NOT dropped", { skip: skipPorts }, () => {
  const id = newRunId(), db = `AlveraChain_${id}`;
  assert.equal(sql(`CREATE DATABASE [${db}]`).status, 0, "could not create the pre-existing database for the test");
  try {
    const r = ps(runnerCommand(`-RunId ${id} -SkipBuild`), runnerEnv);
    assert.equal(r.status, 2, r.out);
    assert.match(r.out, /Refusing: the database .* already exists/);
    assert.ok(dbExists(db), "the runner dropped a database it did not create");
    assert.ok(!existsSync(path.join(dataRoot, "Test-Data", "Chain", id)), "the runner created a run folder before refusing");
  } finally { sql(`IF DB_ID(N'${db}') IS NOT NULL BEGIN ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}]; END`); }
});

test("an existing run folder is refused and is NOT deleted, and no database is created", { skip: skipPorts }, () => {
  const id = newRunId(), folder = path.join(dataRoot, "Test-Data", "Chain", id), marker = path.join(folder, "keep.txt");
  mkdirSync(folder, { recursive: true }); writeFileSync(marker, "someone else's file");
  try {
    const r = ps(runnerCommand(`-RunId ${id} -SkipBuild`), runnerEnv);
    assert.equal(r.status, 2, r.out);
    assert.match(r.out, /Refusing: the run folder .* already exists/);
    assert.equal(readFileSync(marker, "utf8"), "someone else's file", "the runner changed or deleted a file it did not create");
    assert.ok(!dbExists(`AlveraChain_${id}`), "the runner created a database before refusing");
  } finally { rmSync(folder, { recursive: true, force: true }); }
});

test("a run id that is not 32 lower-case hex characters is refused by the runner itself", () => {
  const r = ps("& ([WildcardPattern]::Escape($env:RUNNER)) -RunId NOT-A-RUN-ID -SkipBuild; if ($?) { exit 0 } else { exit 1 }", runnerEnv);     // a parameter-validation error stops the script before it runs, so there is no script exit code to pass on
  assert.notEqual(r.status, 0);
  assert.match(r.out, /does not match|ValidatePattern|Cannot validate argument/i);
});

// ---------- an interrupted run still cleans up (SQL Server, built API, free ports) ----------

test("the API is killed right after start-up: the run fails, its evidence says so, and the owned database and run folder are removed", { skip: skipRun, timeout: 300_000 }, () => {
  const id = newRunId(), db = `AlveraChain_${id}`, evidence = path.join(dataRoot, "Test-Data", "Temp", "chain-output", id);
  try {
    const r = ps(runnerCommand(`-RunId ${id} -SkipBuild -InjectFault KillApiAfterStart`), runnerEnv, 280_000);
    assert.match(r.out, /INJECTED FAULT: the API was killed/, "the fault was not injected");
    assert.equal(r.status, 1, r.out);                                           // red: the stage could not run against a dead API
    assert.ok(!dbExists(db), "the owned database was left behind");
    assert.ok(!existsSync(path.join(dataRoot, "Test-Data", "Chain", id)), "the run folder was left behind");
    const ev = JSON.parse(readFileSync(path.join(evidence, "evidence.json"), "utf8").replace(/^\uFEFF/, ""));
    assert.equal(ev.label, "FRESH");                                            // no resume feature: every run is fresh
    assert.ok(ev.records.some((x) => x.outcome === "FAIL"), "the evidence does not show the failure");
    const clean = ev.records.find((x) => x.stepId === "CLEAN");
    assert.equal(clean?.outcome, "PASS", "the CLEAN record is not PASS");
  } finally {
    sql(`IF DB_ID(N'${db}') IS NOT NULL BEGIN ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}]; END`);
    rmSync(evidence, { recursive: true, force: true });
    rmSync(path.join(dataRoot, "Test-Data", "Chain", id), { recursive: true, force: true });
  }
});
