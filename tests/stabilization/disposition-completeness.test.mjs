// ALV-N019 (R05 PKG-01 Group A): the disposition-completeness check.
//
// Run with: node --test tests/stabilization/disposition-completeness.test.mjs
// The committed register is checked against itself on every run. The real Manual E2E log is outside git, so the "real log" test runs only when
// DISPOSITION_RAW_LOG points at it (and is reported as skipped otherwise); every other case uses small synthetic logs.

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { checkCompleteness, checkRegister, extractRawIds, run } from "../../scripts/stabilization/Test-DispositionCompleteness.mjs";

const root = path.resolve(import.meta.dirname, "..", "..");
const script = path.join(root, "scripts", "stabilization", "Test-DispositionCompleteness.mjs");
const committed = JSON.parse(readFileSync(path.join(root, "docs", "stabilization", "DISPOSITION_REGISTER.json"), "utf8"));

const row = (canonicalKey, over = {}) => ({
  canonicalKey, rawId: canonicalKey, kind: "finding", severity: "S3", disposition: "CONFIRMED", homePackage: "PKG-05", rawLine: 10, excerpt: "x", note: "", canonicalOf: null, ...over,
});
const reg = (...rows) => ({ rows });

test("extractRawIds finds each family once, including suffixed and Q-5x ids", () => {
  const ids = extractRawIds("F-001 and F-044b; D-01, D-02; Q-5cl and Q-065a and Q-G4a; O-E6a; E-01; F-001 again; not a finding: ALV-001, PF-001x");
  assert.deepEqual([...ids].sort(), ["D-01", "D-02", "E-01", "F-001", "F-044b", "O-E6a", "Q-065a", "Q-5cl", "Q-G4a"]);
});

test("happy path: every raw id has a row", () => {
  const out = checkCompleteness("F-001 text. Q-5a text. O-E6a text.", reg(row("F-001"), row("Q-5a", { kind: "question", disposition: "OPEN" }), row("O-E6a", { kind: "observation", disposition: "OBSERVATION" })));
  assert.deepEqual(out.problems, []);
  assert.equal(out.logIdCount, 3);
});

test("failure: a raw id in the log with no row is named", () => {
  const out = checkCompleteness("F-001 and F-002", reg(row("F-001")));
  assert.deepEqual(out.problems, ["raw id F-002 appears in the log but has no disposition row"]);
});

test("failure: an orphan row whose raw id is not in the log is named", () => {
  const out = checkCompleteness("F-001", reg(row("F-001"), row("F-009")));
  assert.deepEqual(out.problems, ["row F-009: rawId F-009 does not appear in the log"]);
});

test("a reused raw id (D-01) is covered by two canonical rows that share one rawId", () => {
  const out = checkCompleteness("D-01 one. D-01 two.", reg(
    row("D-01a", { rawId: "D-01", kind: "design", disposition: "WITHDRAWN", homePackage: "none" }),
    row("D-01b", { rawId: "D-01", kind: "design", disposition: "DESIGN_DECISION_REQUIRED" })));
  assert.deepEqual(out.problems, []);
});

test("a split key (F-044b from F-044) is accepted because it carries the raw id F-044", () => {
  const out = checkCompleteness("F-044", reg(row("F-044"), row("F-044b", { rawId: "F-044", kind: "design", disposition: "DESIGN_DECISION_REQUIRED" })));
  assert.deepEqual(out.problems, []);
});

test("boundary: withdrawn and not-reproduced items are accepted, not treated as missing", () => {
  const out = checkCompleteness("F-201 F-006", reg(row("F-201", { disposition: "WITHDRAWN", homePackage: "PKG-01" }), row("F-006", { disposition: "NOT_REPRODUCED", homePackage: "PKG-15" })));
  assert.deepEqual(out.problems, []);
});

test("failure: two rows with one canonical key", () => {
  const problems = checkRegister(reg(row("F-001"), row("F-001", { rawLine: 11 })));
  assert.ok(problems.some((p) => p.includes("F-001") && p.includes("more than one row")));
});

test("failure: a disposition outside the vocabulary for its kind", () => {
  assert.ok(checkRegister(reg(row("F-001", { disposition: "OPEN" }))).some((p) => p.includes("not allowed for kind finding")));
  assert.ok(checkRegister(reg(row("Q-5a", { kind: "question", disposition: "CONFIRMED" }))).some((p) => p.includes("not allowed for kind question")));
  assert.ok(checkRegister(reg(row("F-001", { kind: "mystery" }))).some((p) => p.includes("kind 'mystery'")));
});

test("failure: a DUPLICATE must link to a row that exists and is not itself", () => {
  assert.ok(checkRegister(reg(row("F-010", { disposition: "DUPLICATE" }))).some((p) => p.includes("must name the canonical row")));
  assert.ok(checkRegister(reg(row("F-010", { disposition: "DUPLICATE", canonicalOf: "F-126" }))).some((p) => p.includes("'F-126' is not a row")));
  assert.ok(checkRegister(reg(row("F-010", { disposition: "DUPLICATE", canonicalOf: "F-010" }))).some((p) => p.includes("points at itself")));
  assert.deepEqual(checkRegister(reg(row("F-010", { disposition: "DUPLICATE", canonicalOf: "F-126" }), row("F-126"))), []);
});

test("failure: malformed rawId, home package and line number are reported", () => {
  const problems = checkRegister(reg(row("F-001", { rawId: "bogus", homePackage: "package 5", rawLine: 0 })));
  assert.equal(problems.length, 3);
});

test("boundary: an empty register against a log with ids reports every id; an empty log with rows reports every row", () => {
  assert.equal(checkCompleteness("F-001 F-002", reg()).problems.length, 2);
  assert.equal(checkCompleteness("no ids here", reg(row("F-001"), row("F-002"))).problems.length, 2);
  assert.deepEqual(checkCompleteness("", reg()).problems, []);
});

test("failure: a register with no rows array is a problem, not a crash", () => {
  assert.deepEqual(checkRegister({}), ["the register has no rows array"]);
});

test("the committed register is internally sound and every row's source is recorded", () => {
  assert.deepEqual(checkRegister(committed), []);
  assert.match(committed.source.rawLogSha256, /^[0-9a-f]{64}$/);
  assert.match(committed.source.reviewerPackageSha256, /^[0-9a-f]{64}$/);
  assert.equal(committed.rows.length, 388);
  const rawIds = new Set(committed.rows.map((r) => r.rawId));
  assert.ok(rawIds.has("F-201") && rawIds.has("Q-G4a") && rawIds.has("D-01"));
  assert.equal(committed.rows.find((r) => r.canonicalKey === "F-201").disposition, "WITHDRAWN");
  assert.equal(committed.rows.find((r) => r.canonicalKey === "F-006").disposition, "NOT_REPRODUCED");
});

test("idempotent: the same inputs give the same answer twice", () => {
  const log = "F-001 F-002 Q-5a";
  const r = reg(row("F-001"));
  assert.deepEqual(checkCompleteness(log, r), checkCompleteness(log, r));
});

test("command line: exit 0 on a complete log, 1 with a PROBLEM line on a gap, 2 on unreadable input", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "disp-"));
  try {
    const registerFile = path.join(dir, "register.json");
    writeFileSync(registerFile, JSON.stringify(reg(row("F-001"))));
    const okLog = path.join(dir, "ok.md"); writeFileSync(okLog, "F-001 only");
    const gapLog = path.join(dir, "gap.md"); writeFileSync(gapLog, "F-001 and F-002");

    const lines = [];
    assert.equal(run(["--log", okLog, "--register", registerFile], (l) => lines.push(l)), 0);
    assert.ok(lines.some((l) => l.startsWith("OK ")));

    lines.length = 0;
    assert.equal(run(["--log", gapLog, "--register", registerFile], (l) => lines.push(l)), 1);
    assert.ok(lines.some((l) => l === "PROBLEM raw id F-002 appears in the log but has no disposition row"));

    lines.length = 0;
    assert.equal(run(["--log", path.join(dir, "missing.md"), "--register", registerFile], (l) => lines.push(l)), 2);
    assert.ok(lines.some((l) => l.startsWith("ERROR cannot read the log")));

    writeFileSync(registerFile, "{ not json");
    assert.equal(run(["--log", okLog, "--register", registerFile], () => {}), 2);
    const saved = process.env.DISPOSITION_RAW_LOG;
    delete process.env.DISPOSITION_RAW_LOG;
    try { assert.equal(run([], () => {}), 2, "no --log and no DISPOSITION_RAW_LOG is an input error, not a pass"); }
    finally { if (saved !== undefined) process.env.DISPOSITION_RAW_LOG = saved; }
    assert.equal(run(["--bogus"], () => {}), 2);

    // the real process exit code, as the runner will see it
    writeFileSync(registerFile, JSON.stringify(reg(row("F-001"))));
    const proc = spawnSync(process.execPath, [script, "--log", gapLog, "--register", registerFile], { encoding: "utf8" });
    assert.equal(proc.status, 1);
    assert.match(proc.stdout, /FAIL 1 problem/);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("the real Manual E2E log: every raw id has a row in the committed register", { skip: process.env.DISPOSITION_RAW_LOG ? false : "set DISPOSITION_RAW_LOG to the manual-e2e-findings.md path to run this" }, () => {
  const lines = [];
  const code = run(["--log", process.env.DISPOSITION_RAW_LOG], (l) => lines.push(l));
  assert.equal(code, 0, lines.join("\n"));
  assert.ok(lines.some((l) => l.includes("(matches the register)")), "the log is not the one the register was built from");
});
