// ALV-N022 A1 (OD-05, briefing 4.3): every write that can change what the patient-safety banner says - an allergy, medication or medical-history item, or a section
// statement, from the chart or from an encounter - must reach the server through the client API modules and be wrapped in withSafetyNotice(...), which tells every
// banner for that patient to refetch the server's summary once the write is saved. This repository check fails on:
//   1. a mutation in services/clinicalRecordApi.ts or one of the named encounter mutations in services/clinicalApi.ts that is not wrapped in withSafetyNotice(;
//   2. any other client source file (tests, fakes and test servers excluded) that sends a write to those routes itself, bypassing the API modules.
//
// Run with: node --test tests/safety-notice-guard.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const SRC = "src/alveara-client/src";
const SKIP = /(\.test\.[cm]?[tj]sx?$|\/test\/|[Ff]ake|TestServer|\.d\.ts$)/;

/** The writes that change the safety picture, per API module. Every export listed here must exist, so the list cannot go stale. */
export const GUARDED = {
  "src/alveara-client/src/services/clinicalRecordApi.ts": ["addRecordItem", "updateRecordItem", "setItemStatus", "removeRecordItem", "setSectionReview"],
  "src/alveara-client/src/services/clinicalApi.ts": ["addEntry", "updateEntry", "removeEntry", "setEntryStatus", "confirmSectionCurrent", "markNoneReported", "clearSectionReview"],
};

/** A write to the clinical-record or encounter entry/section routes (the URL fragments the guarded mutations use). */
const GUARDED_ROUTE = /requestWithCsrf[^(]*\(\s*`[^`]*(clinical-record\/(items|sections)|\/api\/encounters\/[^`]*\/(entries|sections\/))/;

/** The text of one exported arrow-function constant, from its name to the next top-level export. */
export function definitionOf(source, name) {
  const start = source.indexOf(`export const ${name} =`);
  if (start < 0) return null;
  const next = source.indexOf("\nexport ", start + 1);
  return source.slice(start, next < 0 ? undefined : next);
}

/** The guarded names in a module whose definition is missing or not wrapped. */
export function unwrapped(source, names) {
  return names.filter((n) => {
    const def = definitionOf(source, n);
    return def === null || !def.includes("withSafetyNotice(");
  });
}

function sourceFiles(rel, out = []) {
  for (const name of readdirSync(path.join(root, rel))) {
    const r = `${rel}/${name}`;
    if (statSync(path.join(root, r)).isDirectory()) sourceFiles(r, out);
    else if (/\.(ts|tsx)$/.test(name) && !SKIP.test(r)) out.push(r);
  }
  return out;
}

test("every safety-relevant clinical write in the API modules is wrapped in withSafetyNotice", () => {
  for (const [file, names] of Object.entries(GUARDED)) {
    const bad = unwrapped(readFileSync(path.join(root, file), "utf8"), names);
    assert.deepEqual(bad, [], `${file}: not wrapped in withSafetyNotice (or missing): ${bad.join(", ")}`);
  }
});

test("no other client file writes to the clinical-record or encounter entry/section routes directly", () => {
  const offenders = sourceFiles(SRC).filter((f) => !(f in GUARDED) && GUARDED_ROUTE.test(readFileSync(path.join(root, f), "utf8")));
  assert.deepEqual(offenders, []);
});

test("the check catches an unwrapped mutation and a missing one (planted)", () => {
  const planted = "export const addRecordItem = (p) => requestWithCsrf(`/api/patients/${p}/clinical-record/items`, \"POST\", {});\nexport const other = 1;\n";
  assert.deepEqual(unwrapped(planted, ["addRecordItem", "setItemStatus"]), ["addRecordItem", "setItemStatus"]);
  assert.ok(GUARDED_ROUTE.test("requestWithCsrf<X>(`/api/encounters/${id}/entries`, \"POST\", {})"));
  assert.ok(GUARDED_ROUTE.test("requestWithCsrf<X>(`/api/encounters/${id}/sections/${k}/none-reported`, \"POST\", {})"));
  assert.ok(GUARDED_ROUTE.test("requestWithCsrf<X>(`/api/patients/${p}/clinical-record/items`, \"POST\", {})"));
  // other modules' own /entries routes (periodontal charting, billing) are not safety writes
  assert.ok(!GUARDED_ROUTE.test("requestWithCsrf<X>(`/api/periodontal/sessions/${id}/entries`, \"POST\", {})"));
});
