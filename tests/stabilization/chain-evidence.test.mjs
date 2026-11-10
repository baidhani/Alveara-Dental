// ALV-N019 (R05 PKG-01): the chain evidence builder turns raw results into records and an exit code.
//
// Run with: node --test tests/stabilization/chain-evidence.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { buildEvidence, flattenPlaywright, readJson, run, stepIdOf } from "../../scripts/chain/Build-ChainEvidence.mjs";

const meta = { runId: "0123456789abcdef0123456789abcdef", stage: 0, implementationSha: "abc123", schemaVersion: "20261001_X", resumed: false, clockMode: "real" };
const entry = (findingId, over = {}) => ({ findingId, package: "PKG-02", owner: "ALV-N020", exitCondition: "ALV-N020 is closed by independent review with its approved evidence.", blocking: true, status: "OPEN", ...over });
const tracked = (...entries) => ({ entries });
const report = (...specs) => ({ suites: [{ suites: [{ specs: specs.map(([title, status]) => ({ title, tests: [{ status, results: [{ status: status === "expected" ? "passed" : "failed", error: status === "expected" ? undefined : { message: "boom" } }] }] })) }] }] });
const build = (pw, runnerSteps, reg, opts) => buildEvidence({ playwright: pw, runnerSteps, meta, evidencePath: "/e" }, reg, opts);

test("stepIdOf reads the leading source id of a title", () => {
  assert.equal(stepIdOf("S-09a a wrong secret"), "S-09a");
  assert.equal(stepIdOf("1.10 sign out then Back (F-037)"), "1.10");
  assert.equal(stepIdOf("E10 checkpoint"), "E10");
});

test("flattenPlaywright walks nested suites and maps expected/unexpected/skipped", () => {
  const flat = flattenPlaywright(report(["S-09a x", "expected"], ["S-09b y", "unexpected"], ["S-09c z", "skipped"]));
  assert.deepEqual(flat.map((t) => t.status), ["passed", "failed", "skipped"]);
  assert.equal(flat[1].error, "boom");
});

test("all steps pass -> every record PASS, exit 0, metadata on every record", () => {
  const { evidence, exitCode } = build(report(["S-09a x", "expected"]), [{ stepId: "E10", title: "E10 checkpoint", passed: true }], tracked());
  assert.equal(exitCode, 0);
  assert.equal(evidence.label, "FRESH");
  assert.deepEqual(evidence.counts, { PASS: 2, FAIL: 0, TRACKED_EXCEPTION: 0, SKIPPED_BLOCKED: 0 });
  for (const r of evidence.records) assert.deepEqual([r.runId, r.stage, r.implementationSha, r.schemaVersion, r.resumed, r.clockMode, r.evidencePath], [meta.runId, 0, "abc123", "20261001_X", false, "real", "/e"]);
});

test("a failing step is FAIL (exit 1); a failing runner step is FAIL; a skipped step is SKIPPED_BLOCKED (exit 1)", () => {
  assert.equal(build(report(["S-09a x", "unexpected"]), [], tracked()).exitCode, 1);
  const r = build(report(["S-09a x", "expected"]), [{ stepId: "E10", title: "E10 c", passed: false, detail: "no space" }], tracked());
  assert.equal(r.exitCode, 1); assert.equal(r.evidence.records[1].outcome, "FAIL"); assert.equal(r.evidence.records[1].detail, "no space");
  const s = build(report(["S-09a x", "skipped"]), [], tracked());
  assert.equal(s.evidence.records[0].outcome, "SKIPPED_BLOCKED"); assert.equal(s.exitCode, 1);
});

test("a failing step tagged with an OPEN tracked finding is TRACKED_EXCEPTION, keeps the run red, and is not reported as PASS", () => {
  const r = build(report(["1.10 sign out then Back (F-037)", "unexpected"]), [], tracked(entry("F-037")));
  assert.equal(r.evidence.records[0].outcome, "TRACKED_EXCEPTION");
  assert.deepEqual(r.evidence.records[0].entries, ["F-037"]);
  assert.equal(r.exitCode, 1);
  assert.equal(build(report(["1.10 x (F-037)", "unexpected"]), [], tracked(entry("F-037", { blocking: false }))).exitCode, 0);
});

test("a step that passes while its tracked entry is still OPEN is PASS and names the stale entry", () => {
  const r = build(report(["1.10 x (F-037)", "expected"]), [], tracked(entry("F-037")));
  assert.equal(r.evidence.records[0].outcome, "PASS"); assert.deepEqual(r.evidence.records[0].staleEntries, ["F-037"]); assert.equal(r.exitCode, 0);
});

test("a resumed run is labelled RESUMED; requireNoBlocking fails on an OPEN blocking entry even when every step passes", () => {
  const resumed = buildEvidence({ playwright: report(["S-09a x", "expected"]), runnerSteps: [], meta: { ...meta, resumed: true }, evidencePath: "/e" }, tracked());
  assert.equal(resumed.evidence.label, "RESUMED"); assert.equal(resumed.evidence.records[0].resumed, true);
  assert.equal(build(report(["S-09a x", "expected"]), [], tracked(entry("F-037")), { requireNoBlocking: true }).exitCode, 1);
});

test("idempotent: the same inputs give the same evidence", () => {
  const a = build(report(["S-09a x", "expected"]), [], tracked()); const b = build(report(["S-09a x", "expected"]), [], tracked());
  assert.deepEqual(a, b);
});

test("command line: exit 2 on a missing input, 0 on a clean run with the evidence file written", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "chainev-"));
  try {
    const w = (n, v) => { const p = path.join(dir, n); writeFileSync(p, JSON.stringify(v)); return p; };
    const lines = [];
    assert.equal(run(["--playwright", w("p.json", report(["S-09a x", "expected"])), "--runner", w("r.json", []), "--meta", w("m.json", meta)], (l) => lines.push(l)), 2, "no --out");
    assert.equal(run(["--playwright", path.join(dir, "missing.json"), "--runner", w("r.json", []), "--meta", w("m.json", meta), "--out", path.join(dir, "e.json")], (l) => lines.push(l)), 2, "unreadable report");
    lines.length = 0;
    const code = run(["--playwright", w("p.json", report(["S-09a x", "expected"])), "--runner", w("r.json", []), "--meta", w("m.json", meta), "--out", path.join(dir, "e.json")], (l) => lines.push(l));
    assert.equal(code, 0, lines.join("\n"));
    assert.equal(JSON.parse(readFileSync(path.join(dir, "e.json"), "utf8")).records.length, 1);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("readJson accepts a file that starts with a byte-order mark (Windows PowerShell 5.1 writes one)", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "chainbom-"));
  try {
    const f = path.join(dir, "bom.json"); writeFileSync(f, "﻿[{\"stepId\":\"E10\"}]", "utf8");
    assert.deepEqual(readJson(f), [{ stepId: "E10" }]);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
