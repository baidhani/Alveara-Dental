// ALV-N019 (R05 PKG-01, section 6.6): the tracked-exception register and the rules the chain runner applies to it.
//
// Run with: node --test tests/stabilization/tracked-exceptions.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { findingIdsIn, loadTrackedExceptions, openBlockingEntries, resolveOutcome, runExitCode, validateTrackedExceptions } from "../../scripts/stabilization/trackedExceptions.mjs";

const root = path.resolve(import.meta.dirname, "..", "..");
const entry = (findingId, over = {}) => ({
  findingId, package: "PKG-05", owner: "ALV-N021", exitCondition: "ALV-N021 is closed by independent review with its approved acceptance evidence.", blocking: true, status: "OPEN", ...over,
});
const reg = (...entries) => ({ entries });

test("the committed register is valid, every finding exists in the disposition register, and the seeded release-blocking set is all there", () => {
  const dispositions = JSON.parse(readFileSync(path.join(root, "docs", "stabilization", "DISPOSITION_REGISTER.json"), "utf8"));
  const known = new Set(dispositions.rows.map((r) => r.canonicalKey));
  const register = loadTrackedExceptions(undefined, known);
  const ids = register.entries.map((e) => e.findingId).sort();
  const expected = ["F-021", "F-037", "F-107", "F-118", "F-103", "F-185", "F-188", "F-221", "F-114", "F-155", "F-014", "F-053", "F-061", "F-071", "F-059", "F-063", "F-065", "F-078", "F-086", "F-084", "F-085", "F-111"].sort();
  assert.deepEqual(ids, expected);
  assert.ok(register.entries.every((e) => e.blocking && e.status === "OPEN"));
});

test("failure: a missing or malformed owner is a hard failure (reviewer decision O-3)", () => {
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { owner: undefined }))).some((p) => p.includes("owner must be the owning ALV-N0xx story id")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { owner: "someone" }))).some((p) => p.includes("owner must be")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { owner: "ALV-N19" }))).some((p) => p.includes("owner must be")));
});

test("failure: a missing or trivially short exit condition is a hard failure", () => {
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { exitCondition: "" }))).some((p) => p.includes("exitCondition")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { exitCondition: "done" }))).some((p) => p.includes("exitCondition")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { exitCondition: undefined }))).some((p) => p.includes("exitCondition")));
});

test("failure: duplicates, bad ids, unknown findings, bad flags and an unevidenced CLOSED entry are reported", () => {
  assert.ok(validateTrackedExceptions(reg(entry("F-021"), entry("F-021"))).some((p) => p.includes("more than one entry")));
  assert.ok(validateTrackedExceptions(reg(entry("G-1"))).some((p) => p.includes("not a finding id")));
  assert.ok(validateTrackedExceptions(reg(entry("F-999")), new Set(["F-021"])).some((p) => p.includes("not in the disposition register")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { blocking: "yes" }))).some((p) => p.includes("blocking must be")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { status: "DONE" }))).some((p) => p.includes("status must be")));
  assert.ok(validateTrackedExceptions(reg(entry("F-021", { status: "CLOSED" }))).some((p) => p.includes("closedEvidence")));
  assert.deepEqual(validateTrackedExceptions(reg(entry("F-021", { status: "CLOSED", closedEvidence: "ALV-N020 review R02 approved" }))), []);
  assert.deepEqual(validateTrackedExceptions({}), ["the tracked-exception register has no entries array"]);
});

test("loadTrackedExceptions throws on an unreadable or invalid file instead of returning a half-valid register", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "tex-"));
  try {
    assert.throws(() => loadTrackedExceptions(path.join(dir, "missing.json")), /cannot read the tracked-exception register/);
    const bad = path.join(dir, "bad.json"); writeFileSync(bad, JSON.stringify(reg(entry("F-021", { owner: "" }))));
    assert.throws(() => loadTrackedExceptions(bad), /not valid/);
    const good = path.join(dir, "good.json"); writeFileSync(good, JSON.stringify(reg(entry("F-021"))));
    assert.equal(loadTrackedExceptions(good).entries.length, 1);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("findingIdsIn reads the finding ids out of a step title", () => {
  assert.deepEqual(findingIdsIn("1.10 sign out then Back (F-037) and F-155/F-014, F-037 again"), ["F-037", "F-155", "F-014"]);
  assert.deepEqual(findingIdsIn("1.01 open the app while signed out"), []);
});

test("resolveOutcome: a failing step covered by an OPEN entry is TRACKED_EXCEPTION, never PASS", () => {
  const r = reg(entry("F-037"));
  assert.deepEqual(resolveOutcome({ passed: false, findingIds: ["F-037"] }, r), { outcome: "TRACKED_EXCEPTION", entries: ["F-037"], staleEntries: [] });
});

test("resolveOutcome: a failing step with no entry is FAIL; a passing step is PASS", () => {
  const r = reg(entry("F-037"));
  assert.equal(resolveOutcome({ passed: false, findingIds: ["F-099"] }, r).outcome, "FAIL");
  assert.equal(resolveOutcome({ passed: false, findingIds: [] }, r).outcome, "FAIL");
  assert.equal(resolveOutcome({ passed: true, findingIds: [] }, r).outcome, "PASS");
});

test("resolveOutcome: a step that now passes although its entry is OPEN is PASS and names the stale entry", () => {
  const r = reg(entry("F-037"));
  assert.deepEqual(resolveOutcome({ passed: true, findingIds: ["F-037"] }, r), { outcome: "PASS", entries: [], staleEntries: ["F-037"] });
});

test("resolveOutcome: a CLOSED entry no longer excuses a failure", () => {
  const r = reg(entry("F-037", { status: "CLOSED", closedEvidence: "ALV-N020 approved" }));
  assert.equal(resolveOutcome({ passed: false, findingIds: ["F-037"] }, r).outcome, "FAIL");
});

test("runExitCode: blocking TRACKED_EXCEPTION or FAIL or SKIPPED_BLOCKED makes the run red; a non-blocking exception does not", () => {
  const blocking = reg(entry("F-037"));
  const nonBlocking = reg(entry("F-037", { blocking: false }));
  const tracked = [{ outcome: "PASS", entries: [] }, { outcome: "TRACKED_EXCEPTION", entries: ["F-037"] }];
  assert.equal(runExitCode(tracked, blocking), 1);
  assert.equal(runExitCode(tracked, nonBlocking), 0);
  assert.equal(runExitCode([{ outcome: "FAIL", entries: [] }], nonBlocking), 1);
  assert.equal(runExitCode([{ outcome: "SKIPPED_BLOCKED", entries: [] }], nonBlocking), 1);
  assert.equal(runExitCode([{ outcome: "PASS", entries: [] }], blocking), 0);
});

test("runExitCode: requireNoBlocking (ALV-N028's exit run) fails while any blocking entry is OPEN, even with every step passing", () => {
  const passing = [{ outcome: "PASS", entries: [] }];
  assert.equal(runExitCode(passing, reg(entry("F-037")), { requireNoBlocking: true }), 1);
  assert.equal(runExitCode(passing, reg(entry("F-037", { status: "CLOSED", closedEvidence: "approved" })), { requireNoBlocking: true }), 0);
  assert.equal(runExitCode(passing, reg(entry("F-037", { blocking: false })), { requireNoBlocking: true }), 0);
  assert.equal(openBlockingEntries(reg(entry("F-037"), entry("F-021", { blocking: false }))).length, 1);
});

test("runExitCode rejects an outcome it does not know", () => {
  assert.throws(() => runExitCode([{ outcome: "GREEN", entries: [] }], reg()), /unknown outcome/);
});

test("idempotent: resolving the same step twice gives the same outcome", () => {
  const r = reg(entry("F-037"));
  const step = { passed: false, findingIds: ["F-037"] };
  assert.deepEqual(resolveOutcome(step, r), resolveOutcome(step, r));
});
