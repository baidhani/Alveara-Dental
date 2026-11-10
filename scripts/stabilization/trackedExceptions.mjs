// ALV-N019 (R05 PKG-01, section 6.6): the tracked-exception register and the rules the accumulated-chain runner applies to it.
//
// A known defect never shows as a green expected-failure (Playwright's test.fail() reports a failing test as a pass). Instead a chain step tagged with a finding id
// that has a blocking OPEN entry is reported TRACKED_EXCEPTION, and the run stays red until the register has no blocking OPEN entry (ALV-N028 requires none).
// Reviewer decision O-3 (2026-11-09): `owner` is the owning ALV-N0xx story id; `exitCondition` is that story's approved acceptance evidence.

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const OUTCOMES = ["PASS", "FAIL", "TRACKED_EXCEPTION", "SKIPPED_BLOCKED"];
const FINDING_ID = /^F-\d{3}[a-z]?$/;
const OWNER = /^ALV-N0\d{2}$/;
const PACKAGE = /^PKG-\d{2}$/;
const MIN_EXIT_CONDITION = 20;

export function defaultRegisterPath() {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "docs", "stabilization", "TRACKED_EXCEPTIONS.json");
}

/**
 * Problems in the register (one line each; empty = sound). A missing owner or exit condition is a hard failure, as is a finding id that
 * the disposition register does not know (pass its rows as knownFindingIds to check that).
 */
export function validateTrackedExceptions(register, knownFindingIds = null) {
  const problems = [];
  if (!register || !Array.isArray(register.entries)) return ["the tracked-exception register has no entries array"];
  const seen = new Set();
  for (const e of register.entries) {
    const where = `entry ${e?.findingId ?? "(no findingId)"}`;
    if (typeof e.findingId !== "string" || !FINDING_ID.test(e.findingId)) { problems.push(`${where}: findingId is not a finding id like F-021`); continue; }
    if (seen.has(e.findingId)) problems.push(`${where}: more than one entry for this finding`);
    seen.add(e.findingId);
    if (knownFindingIds && !knownFindingIds.has(e.findingId)) problems.push(`${where}: the finding is not in the disposition register`);
    if (typeof e.owner !== "string" || !OWNER.test(e.owner)) problems.push(`${where}: owner must be the owning ALV-N0xx story id (missing or malformed: '${e.owner ?? ""}')`);
    if (typeof e.exitCondition !== "string" || e.exitCondition.trim().length < MIN_EXIT_CONDITION) problems.push(`${where}: exitCondition is missing or too short to be a condition`);
    if (typeof e.package !== "string" || !PACKAGE.test(e.package)) problems.push(`${where}: package must be PKG-nn`);
    if (typeof e.blocking !== "boolean") problems.push(`${where}: blocking must be true or false`);
    if (e.status !== "OPEN" && e.status !== "CLOSED") problems.push(`${where}: status must be OPEN or CLOSED`);
    if (e.status === "CLOSED" && !(typeof e.closedEvidence === "string" && e.closedEvidence.trim() !== "")) problems.push(`${where}: a CLOSED entry must say what evidence closed it (closedEvidence)`);
  }
  return problems;
}

/** Reads and validates the register file. Throws (never returns a half-valid register) so a runner cannot start on a bad file. */
export function loadTrackedExceptions(file = defaultRegisterPath(), knownFindingIds = null) {
  let register;
  try { register = JSON.parse(readFileSync(file, "utf8")); } catch (e) { throw new Error(`cannot read the tracked-exception register '${file}': ${e.message}`); }
  const problems = validateTrackedExceptions(register, knownFindingIds);
  if (problems.length > 0) throw new Error(`the tracked-exception register is not valid:\n${problems.join("\n")}`);
  return register;
}

/** Finding ids a step name or tag list mentions (a step's title carries its manual source id and its finding ids). */
export function findingIdsIn(text) {
  return [...new Set([...String(text).matchAll(/\bF-\d{3}[a-z]?\b/g)].map((m) => m[0]))];
}

export function openBlockingEntries(register) {
  return register.entries.filter((e) => e.status === "OPEN" && e.blocking);
}

/**
 * Outcome of one chain step.
 *  - passed, no open entry for the step's findings          -> PASS
 *  - failed, no open entry                                   -> FAIL
 *  - failed, an OPEN entry covers one of its findings        -> TRACKED_EXCEPTION (the known defect; the run stays red)
 *  - passed although an OPEN entry covers it                 -> PASS, with staleEntries naming the entry that should now be closed
 */
export function resolveOutcome({ passed, findingIds }, register) {
  const open = register.entries.filter((e) => e.status === "OPEN" && findingIds.includes(e.findingId));
  if (passed) return { outcome: "PASS", entries: [], staleEntries: open.map((e) => e.findingId) };
  if (open.length > 0) return { outcome: "TRACKED_EXCEPTION", entries: open.map((e) => e.findingId), staleEntries: [] };
  return { outcome: "FAIL", entries: [], staleEntries: [] };
}

/**
 * Exit code of a run. 1 when any step is FAIL, or TRACKED_EXCEPTION with a blocking entry. With requireNoBlocking (ALV-N028's exit run) an OPEN
 * blocking entry anywhere in the register also makes the run fail, whether or not a step reproduced it.
 */
export function runExitCode(results, register, { requireNoBlocking = false } = {}) {
  for (const r of results) {
    if (!OUTCOMES.includes(r.outcome)) throw new Error(`unknown outcome '${r.outcome}'`);
    if (r.outcome === "FAIL" || r.outcome === "SKIPPED_BLOCKED") return 1;
    if (r.outcome === "TRACKED_EXCEPTION") {
      const blocking = r.entries.some((id) => register.entries.some((e) => e.findingId === id && e.status === "OPEN" && e.blocking));
      if (blocking) return 1;
    }
  }
  if (requireNoBlocking && openBlockingEntries(register).length > 0) return 1;
  return 0;
}
