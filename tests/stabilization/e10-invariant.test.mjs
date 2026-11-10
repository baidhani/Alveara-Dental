// ALV-N019 R02 (review correction 03): the E10 checkpoint invariant. Pure checks on snapshots; no SQL Server.
//
// Run with: node --test tests/stabilization/e10-invariant.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { checkE10Invariant, run } from "../../scripts/chain/E10Invariant.mjs";

const before = () => ({ tables: { UserAccounts: 1, Patients: 0, AuditLogEntries: 7 }, differentialBaseLsn: "12345000000000001", restoreCount: 0 });
const after = (over = {}) => ({ ...before(), backup: { exists: true, bytes: 4096, copyOnly: true }, ...over });

test("held: unchanged rows, unchanged differential base, no restore, a non-empty COPY_ONLY backup", () => {
  assert.deepEqual(checkE10Invariant(before(), after()), []);
});

test("violated: a row count changed", () => {
  const a = after({ tables: { UserAccounts: 1, Patients: 1, AuditLogEntries: 7 } });
  assert.deepEqual(checkE10Invariant(before(), a), ["table Patients changed from 0 to 1 rows"]);
});

test("violated: a table appeared or disappeared", () => {
  assert.deepEqual(checkE10Invariant(before(), after({ tables: { UserAccounts: 1, Patients: 0, AuditLogEntries: 7, Extra: 0 } })), ["table Extra appeared during the checkpoint"]);
  assert.deepEqual(checkE10Invariant(before(), after({ tables: { UserAccounts: 1, Patients: 0 } })), ["table AuditLogEntries disappeared during the checkpoint"]);
});

test("violated: the differential base moved (the backup was not COPY_ONLY in effect)", () => {
  const problems = checkE10Invariant(before(), after({ differentialBaseLsn: "12345000000000999" }));
  assert.equal(problems.length, 1); assert.match(problems[0], /differential base moved/);
});

test("violated: a restore was recorded, or had already been recorded", () => {
  assert.match(checkE10Invariant(before(), after({ restoreCount: 1 })).join("\n"), /a restore was recorded during the checkpoint/);
  assert.match(checkE10Invariant({ ...before(), restoreCount: 2 }, after({ restoreCount: 2 })).join("\n"), /already recorded before the checkpoint/);
});

test("violated: the backup file is missing, empty, or not COPY_ONLY", () => {
  assert.deepEqual(checkE10Invariant(before(), after({ backup: { exists: false, bytes: 0, copyOnly: false } })), ["the checkpoint backup file does not exist"]);
  assert.deepEqual(checkE10Invariant(before(), after({ backup: { exists: true, bytes: 0, copyOnly: true } })), ["the checkpoint backup file is empty"]);
  assert.deepEqual(checkE10Invariant(before(), after({ backup: { exists: true, bytes: 10, copyOnly: false } })), ["the checkpoint backup is not recorded as COPY_ONLY"]);
  assert.deepEqual(checkE10Invariant(before(), { ...before() }), ["the checkpoint backup file does not exist"]);
});

test("boundary: a missing or malformed snapshot is reported, not a crash; an empty database with no tables still passes", () => {
  assert.deepEqual(checkE10Invariant(null, after()), ["the before snapshot is missing or has no tables"]);
  assert.deepEqual(checkE10Invariant(before(), {}), ["the after snapshot is missing or has no tables"]);
  const empty = { tables: {}, differentialBaseLsn: null, restoreCount: 0 };
  assert.deepEqual(checkE10Invariant(empty, { ...empty, backup: { exists: true, bytes: 1, copyOnly: true } }), []);
});

test("several violations are all reported together", () => {
  const a = after({ tables: { UserAccounts: 2, Patients: 0, AuditLogEntries: 7 }, restoreCount: 1, backup: { exists: true, bytes: 5, copyOnly: false } });
  assert.equal(checkE10Invariant(before(), a).length, 3);
});

test("idempotent: the same snapshots give the same answer twice", () => {
  assert.deepEqual(checkE10Invariant(before(), after()), checkE10Invariant(before(), after()));
  const a = after({ restoreCount: 1 });
  assert.deepEqual(checkE10Invariant(before(), a), checkE10Invariant(before(), a));
});

test("command line: 0 when held, 1 with a VIOLATION line when broken, 2 for a missing argument or an unreadable file; a byte-order mark is accepted", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "e10-"));
  try {
    const w = (n, v, bom = false) => { const p = path.join(dir, n); writeFileSync(p, (bom ? "﻿" : "") + JSON.stringify(v)); return p; };
    const lines = [];
    assert.equal(run(["--before", w("b.json", before(), true), "--after", w("a.json", after(), true)], (l) => lines.push(l)), 0);
    assert.match(lines.at(-1), /^OK the checkpoint left 3 tables/);
    lines.length = 0;
    assert.equal(run(["--before", w("b.json", before()), "--after", w("bad.json", after({ restoreCount: 1 }))], (l) => lines.push(l)), 1);
    assert.ok(lines.some((l) => l.startsWith("VIOLATION a restore was recorded")));
    assert.equal(run(["--before", w("b.json", before())], () => {}), 2);
    assert.equal(run(["--before", path.join(dir, "missing.json"), "--after", w("a.json", after())], () => {}), 2);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
