// ALV-N019 (R05 PKG-01 Group A): proves that every raw id in the Manual E2E findings log has a disposition row, and that the register is internally sound.
//
// Usage:  node scripts/stabilization/Test-DispositionCompleteness.mjs --log <path to manual-e2e-findings.md> [--register <path>]
//         (the log path may also come from the DISPOSITION_RAW_LOG environment variable)
// Exit:   0 = complete and sound, 1 = problems found (one line each), 2 = an input could not be read or parsed (never mistaken for a pass).
// Pure function of its two inputs: it writes nothing, so running it twice gives the same answer.

import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// The raw id families used in the log: findings F-nnn(x), design notes D-nn(x), open questions Q-..., observations O-..., the enhancement idea E-nn.
const RAW_ID = /\b(F-\d{3}[a-z]?|D-\d{2}[a-z]?|Q-[0-9A-Za-z]+|O-[A-Za-z0-9]+|E-\d{2})\b/g;

export const FINDING_DISPOSITIONS = ["CONFIRMED", "NOT_REPRODUCED", "DUPLICATE", "WITHDRAWN", "DESIGN_DECISION_REQUIRED"];
export const QUESTION_DISPOSITIONS = ["OPEN", "ANSWERED"];
export const OBSERVATION_DISPOSITIONS = ["OBSERVATION"];
const KINDS = { finding: FINDING_DISPOSITIONS, design: FINDING_DISPOSITIONS, question: QUESTION_DISPOSITIONS, observation: OBSERVATION_DISPOSITIONS };
const HOME_PACKAGE = /^(none|PKG-\d{2}(, PKG-\d{2})*)$/;   // one or more R05 packages, or none (answered, withdrawn)

/** Every distinct raw id mentioned anywhere in the log text. */
export function extractRawIds(logText) {
  const ids = new Set();
  for (const m of logText.matchAll(RAW_ID)) ids.add(m[1]);
  return ids;
}

/** Problems inside the register itself (no log needed). Returns an array of one-line strings; empty = sound. */
export function checkRegister(register) {
  const problems = [];
  if (!register || !Array.isArray(register.rows)) return ["the register has no rows array"];
  const seen = new Map();
  for (const row of register.rows) {
    const where = `row ${row?.canonicalKey ?? "(no canonicalKey)"}`;
    if (typeof row.canonicalKey !== "string" || row.canonicalKey === "") { problems.push(`${where}: canonicalKey missing`); continue; }
    if (seen.has(row.canonicalKey)) problems.push(`${where}: canonicalKey is used by more than one row`);
    seen.set(row.canonicalKey, row);
    if (typeof row.rawId !== "string" || !new RegExp(`^${RAW_ID.source}$`).test(row.rawId)) problems.push(`${where}: rawId '${row.rawId}' is not a valid raw id`);
    const allowed = KINDS[row.kind];
    if (!allowed) { problems.push(`${where}: kind '${row.kind}' is not one of ${Object.keys(KINDS).join(", ")}`); continue; }
    if (!allowed.includes(row.disposition)) problems.push(`${where}: disposition '${row.disposition}' is not allowed for kind ${row.kind} (${allowed.join(", ")})`);
    if (!HOME_PACKAGE.test(String(row.homePackage))) problems.push(`${where}: homePackage '${row.homePackage}' is not PKG-nn (a comma-separated list is allowed) or none`);
    if (!Number.isInteger(row.rawLine) || row.rawLine < 1) problems.push(`${where}: rawLine must be a positive integer`);
  }
  for (const row of register.rows) {
    if (row.disposition === "DUPLICATE") {
      if (!row.canonicalOf) problems.push(`row ${row.canonicalKey}: a DUPLICATE must name the canonical row it links to (canonicalOf)`);
      else if (!seen.has(row.canonicalOf)) problems.push(`row ${row.canonicalKey}: canonicalOf '${row.canonicalOf}' is not a row in the register`);
      else if (row.canonicalOf === row.canonicalKey) problems.push(`row ${row.canonicalKey}: canonicalOf points at itself`);
    }
  }
  return problems;
}

/**
 * Completeness of the register against the log text.
 * - every raw id in the log needs at least one row (a reused or split id has several rows sharing one rawId);
 * - every row's rawId must occur in the log (an orphan row is a stale or mistyped row).
 */
export function checkCompleteness(logText, register) {
  const problems = checkRegister(register);
  const logIds = extractRawIds(logText);
  const covered = new Set((register.rows ?? []).map((r) => r.rawId));
  for (const id of [...logIds].sort()) if (!covered.has(id)) problems.push(`raw id ${id} appears in the log but has no disposition row`);
  for (const row of register.rows ?? []) if (row.rawId && !logIds.has(row.rawId)) problems.push(`row ${row.canonicalKey}: rawId ${row.rawId} does not appear in the log`);
  return { problems, logIdCount: logIds.size, rowCount: (register.rows ?? []).length };
}

export function sha256Hex(bytes) { return createHash("sha256").update(bytes).digest("hex"); }

function parseArgs(argv) {
  const args = { log: process.env.DISPOSITION_RAW_LOG ?? "", register: "" };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--log") args.log = argv[++i] ?? "";
    else if (argv[i] === "--register") args.register = argv[++i] ?? "";
    else throw new Error(`unknown argument '${argv[i]}'`);
  }
  return args;
}

/** Runs the check as the command line does; returns the exit code and writes its report with the given sink. */
export function run(argv, write = (line) => console.log(line)) {
  let args;
  try { args = parseArgs(argv); } catch (e) { write(`ERROR ${e.message}`); return 2; }
  if (!args.log) { write("ERROR no log path: pass --log <manual-e2e-findings.md> or set DISPOSITION_RAW_LOG"); return 2; }
  const registerPath = args.register || path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "docs", "stabilization", "DISPOSITION_REGISTER.json");
  let logBytes, register;
  try { logBytes = readFileSync(args.log); } catch (e) { write(`ERROR cannot read the log '${args.log}': ${e.message}`); return 2; }
  try { register = JSON.parse(readFileSync(registerPath, "utf8")); } catch (e) { write(`ERROR cannot read the register '${registerPath}': ${e.message}`); return 2; }

  const { problems, logIdCount, rowCount } = checkCompleteness(logBytes.toString("utf8"), register);
  const actual = sha256Hex(logBytes);
  const built = register.source?.rawLogSha256;
  write(`log sha256 ${actual}${built === actual ? " (matches the register)" : ` (the register was built from ${built ?? "an unrecorded log"}; ids below are still checked against this log)`}`);
  if (problems.length > 0) {
    for (const p of problems) write(`PROBLEM ${p}`);
    write(`FAIL ${problems.length} problem(s); ${logIdCount} raw ids in the log, ${rowCount} register rows`);
    return 1;
  }
  write(`OK all ${logIdCount} raw ids have a disposition (${rowCount} register rows)`);
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = run(process.argv.slice(2));
}
