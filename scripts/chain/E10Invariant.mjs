// ALV-N019 R02 (review correction 03): the E10 checkpoint invariant of the accumulated chain.
//
// E10 is a safety net, never a reset: one COPY_ONLY backup of the chain database, no restore, and the live database unchanged by it.
// The runner takes a snapshot of the chain database before and after the checkpoint and this module decides whether the invariant held:
//   - the backup file exists, is not empty, and the backup is recorded as COPY_ONLY (so the backup chain is not disturbed);
//   - no restore was invoked: the number of restores recorded for the database is the same before and after, and is 0;
//   - the live data is unchanged: every table holds the same number of rows;
//   - the differential base is unchanged (a COPY_ONLY backup must not move it).
// Snapshot shape: { tables: { <name>: <row count> }, differentialBaseLsn: string|null, restoreCount: number }
// plus, on the after-snapshot only: { backup: { exists: boolean, bytes: number, copyOnly: boolean } }.
// Exit (CLI): 0 = invariant held, 1 = violated (one line per violation), 2 = an input could not be read.

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/** Every way the invariant is broken, one line each. Empty = held. Pure: same snapshots, same answer. */
export function checkE10Invariant(before, after) {
  const problems = [];
  const need = (snap, name) => {
    if (!snap || typeof snap !== "object" || !snap.tables || typeof snap.tables !== "object") { problems.push(`the ${name} snapshot is missing or has no tables`); return false; }
    return true;
  };
  const okBefore = need(before, "before"), okAfter = need(after, "after");
  if (!okBefore || !okAfter) return problems;

  const names = new Set([...Object.keys(before.tables), ...Object.keys(after.tables)]);
  for (const n of [...names].sort()) {
    if (!(n in before.tables)) problems.push(`table ${n} appeared during the checkpoint`);
    else if (!(n in after.tables)) problems.push(`table ${n} disappeared during the checkpoint`);
    else if (before.tables[n] !== after.tables[n]) problems.push(`table ${n} changed from ${before.tables[n]} to ${after.tables[n]} rows`);
  }
  if (before.differentialBaseLsn !== after.differentialBaseLsn) problems.push(`the differential base moved from ${before.differentialBaseLsn} to ${after.differentialBaseLsn}: the backup was not COPY_ONLY in effect`);
  if (before.restoreCount !== 0) problems.push(`a restore into the chain database was already recorded before the checkpoint (${before.restoreCount})`);
  if (after.restoreCount !== before.restoreCount) problems.push(`a restore was recorded during the checkpoint (${before.restoreCount} before, ${after.restoreCount} after)`);
  const b = after.backup;
  if (!b || b.exists !== true) problems.push("the checkpoint backup file does not exist");
  else {
    if (!(b.bytes > 0)) problems.push("the checkpoint backup file is empty");
    if (b.copyOnly !== true) problems.push("the checkpoint backup is not recorded as COPY_ONLY");
  }
  return problems;
}

const readJson = (file) => JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, ""));   // Windows PowerShell 5.1 writes a byte-order mark

/** Command line: node E10Invariant.mjs --before <json> --after <json>. */
export function run(argv, write = (line) => console.log(line)) {
  const arg = (name) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : undefined; };
  const files = { before: arg("--before"), after: arg("--after") };
  if (!files.before || !files.after) { write("ERROR pass --before <snapshot.json> and --after <snapshot.json>"); return 2; }
  let before, after;
  try { before = readJson(files.before); after = readJson(files.after); } catch (e) { write(`ERROR cannot read a snapshot: ${e.message}`); return 2; }
  const problems = checkE10Invariant(before, after);
  if (problems.length > 0) { for (const p of problems) write(`VIOLATION ${p}`); write(`FAIL ${problems.length} violation(s)`); return 1; }
  const tables = Object.keys(after.tables).length, rows = Object.values(after.tables).reduce((a, b) => a + b, 0);
  write(`OK the checkpoint left ${tables} tables (${rows} rows) unchanged, the differential base unchanged, no restore recorded, and a non-empty COPY_ONLY backup (${after.backup.bytes} bytes)`);
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = run(process.argv.slice(2));
