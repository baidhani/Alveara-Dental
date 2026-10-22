// ALV-CONTROL-C01: every execution-control status that .alveara/HANDOFF_SCHEMA.md allows has a human-readable label in assets/status-labels.js,
// the label table allows nothing the schema does not, every status in the ledger is allowed, and the pages that show statuses read that one table.
//
// Run with: node --test tests/status-labels.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import vm from "node:vm";

const root = path.resolve(import.meta.dirname, "..");
const read = (rel) => readFileSync(path.join(root, rel), "utf-8").replace(/\r\n/g, "\n");

function allowedStatuses() {
  const schema = read(".alveara/HANDOFF_SCHEMA.md");
  const start = schema.indexOf("## 1. Status Lifecycle");
  const end = schema.indexOf("\n## 2.", start);
  assert.ok(start >= 0 && end > start, "HANDOFF_SCHEMA.md section 1 (Status Lifecycle) must exist");
  const section = schema.slice(start, end);
  const found = new Set();
  for (const fence of section.matchAll(/```[^\n]*\n([\s\S]*?)```/g)) for (const part of fence[1].split(/→|\n/)) if (/^[A-Z][A-Z_]+$/.test(part.trim())) found.add(part.trim());
  for (const bullet of section.matchAll(/^- `([A-Z][A-Z_]+)`/gm)) found.add(bullet[1]);
  return [...found];
}

function labels() {
  const context = {};
  vm.runInNewContext(`${read("assets/status-labels.js")}\nthis.labels = EXECUTION_STATUS_LABELS;`, context);
  return { ...context.labels };
}

test("the schema parser finds the statuses the reviewer named, so this guard cannot pass on an empty list", () => {
  const allowed = allowedStatuses();
  for (const named of ["PLANNED", "READY", "IN_PROGRESS", "AWAITING_REVIEW", "AWAITING_REGRESSION", "COMPLETE", "BLOCKED", "CHANGES_REQUIRED", "DEFERRED", "REOPENED", "REVALIDATION_REQUIRED"])
    assert.ok(allowed.includes(named), `${named} must be found in HANDOFF_SCHEMA.md section 1`);
});

test("every status the schema allows has a label, and the table has no status the schema does not allow", () => {
  assert.deepEqual(Object.keys(labels()).sort(), allowedStatuses().sort());
});

test("every label is a readable phrase, never the raw status, and no two statuses share a label", () => {
  const all = labels();
  for (const [status, label] of Object.entries(all)) {
    assert.match(label, /^[A-Z][a-z]+( [a-z]+)*$/, `${status} label "${label}"`);
    assert.notEqual(label, status);
  }
  assert.equal(new Set(Object.values(all)).size, Object.keys(all).length);
});

test("every status in the execution ledger is one the schema allows", () => {
  const allowed = new Set(allowedStatuses());
  const ledger = JSON.parse(read(".alveara/EXECUTION_STATUS.json").replace(/^\uFEFF/, ""));
  for (const r of ledger.records) assert.ok(allowed.has(r.status), `${r.storyId} has status ${r.status}`);
});

test("the Architecture Map and the Engineering stories list both read the one label table, and the page loads it first", () => {
  assert.match(read("assets/map.js"), /MAP_STATUS_LABELS = EXECUTION_STATUS_LABELS/);
  assert.match(read("assets/app.js"), /ENGINEERING_STATUS_LABELS = EXECUTION_STATUS_LABELS/);
  const html = read("index.html");
  const order = ["assets/status-labels.js", "assets/map.js", "assets/app.js"].map((s) => html.indexOf(s));
  assert.ok(order.every((i) => i >= 0) && order[0] < order[1] && order[1] < order[2], "status-labels.js must load before map.js and app.js");
});
