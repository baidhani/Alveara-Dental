// ALV-N019 (R05 PKG-15, F-009 and F-129): the running-locally guide keeps no hard-coded counts that go stale, and explains the https-port warning.
//
// Run with: node --test tests/stabilization/docs-hygiene.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";

const guide = readFileSync(path.resolve(import.meta.dirname, "..", "..", "docs", "RUNNING_LOCALLY.md"), "utf8");

test("F-009: step 4 states no count of tables or migrations (a number goes stale again)", () => {
  assert.doesNotMatch(guide, /\b\d+\s+tables\b/i, "a table count is back in RUNNING_LOCALLY.md");
  assert.doesNotMatch(guide, /\b\d+\s+migrations\b/i, "a migration count is back in RUNNING_LOCALLY.md");
  assert.doesNotMatch(guide, /at the time of writing/i, "the 'at the time of writing' phrase is back");
  assert.match(guide, /applies every migration/, "step 4 should still say it applies every migration");
});

test("F-129: the https-port warning is explained as harmless in development", () => {
  assert.match(guide, /Failed to determine the https port for redirect/);
  assert.match(guide, /Harmless in development/);
});

test("the guide uses the product name DentalKeep in its title", () => {
  assert.match(guide, /^# Running DentalKeep on your own machine/m);
});

test("idempotent: reading the guide twice gives the same checks", () => {
  assert.equal(readFileSync(path.resolve(import.meta.dirname, "..", "..", "docs", "RUNNING_LOCALLY.md"), "utf8"), guide);
});
