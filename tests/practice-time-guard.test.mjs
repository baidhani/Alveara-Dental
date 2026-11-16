// ALV-N021 A3 (briefing section 8, BR-4): the client renders every instant in the PRACTICE zone and derives every "today" from it, through the shared
// helper module src/alveara-client/src/time/practiceTime.ts only. This repository check scans the client source (tests, fakes and test servers excluded)
// and fails on exactly two things outside that module:
//   1. browser-zone formatting of an instant: toLocaleDateString( / toLocaleTimeString( (Date-only methods), toLocaleString( on a Date expression, or a
//      new Intl.DateTimeFormat( without a timeZone;
//   2. browser-local calendar-day derivation: getFullYear() / getMonth() / getDate() / getDay() / getHours() / getMinutes() on a Date.
// It does NOT flag number or money formatting, Date.now() for timers or keys, getTime() instant arithmetic, or date-only values handled in the reviewed
// allowlist below. Each allowlist entry names its file, the exact expression and why; there is no directory-wide exemption, and an entry that no longer
// matches anything fails the check, so the list cannot go stale.
//
// Run with: node --test tests/practice-time-guard.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const SRC = "src/alveara-client/src";
const HELPER = "src/alveara-client/src/time/practiceTime.ts";
const SKIP = /(\.test\.[cm]?[tj]sx?$|\/test\/|[Ff]ake|TestServer|\.d\.ts$)/;

/** Reviewed exceptions: date-only values built from their parts (never instants, never zone-converted). Appendix A rows 72-77. */
export const ALLOWLIST = [
  { file: "src/alveara-client/src/components/dateOnly.ts", expression: "day.getFullYear() !== y || day.getMonth() !== m - 1 || day.getDate() !== d", reason: "date-only: checks that a yyyy-MM-dd value is a real calendar day (row 72)" },
  { file: "src/alveara-client/src/components/dateOnly.ts", expression: "day.toLocaleDateString(undefined, options)", reason: "date-only: formatDateOnly shows a calendar day built from its parts (row 72)" },
  { file: "src/alveara-client/src/pages/externalcases/externalCaseFormat.ts", expression: "new Date(y, m - 1, d).toLocaleDateString(", reason: "date-only: formatDay shows a practice-local yyyy-MM-dd (row 73)" },
  { file: "src/alveara-client/src/pages/procedures/procedureText.ts", expression: "new Date(`${d}T00:00:00`).toLocaleDateString(", reason: "date-only: dateText (row 74)" },
  { file: "src/alveara-client/src/pages/treatmentplan/treatmentPlanRules.ts", expression: "new Date(`${isoDate}T00:00:00`).toLocaleDateString(", reason: "date-only: dayOf (row 75)" },
  { file: "src/alveara-client/src/pages/treatmentplan/treatmentPlanLayout.ts", expression: "new Date(`${phase.targetDate}T00:00:00`).toLocaleDateString(", reason: "date-only: a phase's target date (row 76)" },
];

// rule 1: Date-only formatting methods (never on a number), toLocaleString on a Date expression, Intl.DateTimeFormat without a zone
const DATE_ONLY_METHOD = /\.toLocale(Date|Time)String\(/;
const TO_LOCALE_STRING_ON_DATE = /(new Date\([^)]*\)|\b\w*(Date|date|Utc|At|when|moment|instant)\b\)?)\.toLocaleString\(/;
const INTL_DATE = /new Intl\.DateTimeFormat\(/;
// rule 2: calendar fields of a Date read in the browser zone
const BROWSER_DAY = /\.get(FullYear|Month|Date|Day|Hours|Minutes)\(\)/;

function sourceFiles(rel, out = []) {
  const full = path.join(root, rel);
  if (statSync(full).isDirectory()) { for (const n of readdirSync(full)) sourceFiles(path.join(rel, n), out); return out; }
  const r = rel.replace(/\\/g, "/");
  if (/\.(ts|tsx)$/.test(r) && !SKIP.test(r)) out.push(r);
  return out;
}

const stripComment = (line) => line.replace(/\/\/.*$/, "").replace(/\/\*.*?\*\//g, "");

/** The offending lines of one source text (allowlist not applied). Exported shape for the negative controls below. */
export function offences(text) {
  const lines = text.split(/\r?\n/);
  const found = [];
  lines.forEach((raw, i) => {
    const line = stripComment(raw);
    const intlWithoutZone = INTL_DATE.test(line) && !/timeZone/.test(lines.slice(i, i + 4).join(" "));
    if (DATE_ONLY_METHOD.test(line) || TO_LOCALE_STRING_ON_DATE.test(line) || intlWithoutZone || BROWSER_DAY.test(line)) found.push({ line: i + 1, text: raw.trim() });
  });
  return found;
}

const allowed = (file, text) => ALLOWLIST.some((a) => a.file === file && text.includes(a.expression));

test("no instant is formatted in the browser zone and no 'today' is read from the browser outside the shared helper", () => {
  const bad = [];
  for (const file of sourceFiles(SRC)) {
    if (file === HELPER) continue;
    for (const o of offences(readFileSync(path.join(root, file), "utf8"))) if (!allowed(file, o.text)) bad.push(`${file}:${o.line}: ${o.text}`);
  }
  assert.deepEqual(bad, [], "Use time/practiceTime.ts (showInstant, formatInstant, sessionToday, ...) or add a reviewed allowlist entry:\n" + bad.join("\n"));
});

test("every allowlist entry still matches a line, names a reason, and the shared helper exists", () => {
  for (const a of ALLOWLIST) {
    assert.ok(a.reason && a.reason.length > 10, `entry without a reason: ${a.file}`);
    assert.ok(readFileSync(path.join(root, a.file), "utf8").includes(a.expression), `stale allowlist entry (no such expression): ${a.file}: ${a.expression}`);
  }
  assert.ok(readFileSync(path.join(root, HELPER), "utf8").includes("export function formatInstant("));
});

test("the scan sees the client and each allowlisted line is really one the rules catch (so the allowlist is not hiding nothing)", () => {
  assert.ok(sourceFiles(SRC).length > 300, `the scan found only ${sourceFiles(SRC).length} files`);
  for (const a of ALLOWLIST) {
    const hits = offences(readFileSync(path.join(root, a.file), "utf8")).filter((o) => o.text.includes(a.expression));
    assert.ok(hits.length > 0, `the rules no longer catch the allowlisted expression, so the entry is not needed: ${a.file}: ${a.expression}`);
  }
});

test("negative controls: a browser-zone instant format and a browser 'today' are caught; number formatting and instant arithmetic are not", () => {
  assert.equal(offences("const s = new Date(row.createdAtUtc).toLocaleString();").length, 1);
  assert.equal(offences("const s = when.toLocaleString();").length, 1);
  assert.equal(offences("const s = new Date(x).toLocaleTimeString();").length, 1);
  assert.equal(offences("const f = new Intl.DateTimeFormat(undefined, { hour: 'numeric' });").length, 1);
  assert.equal(offences("const d = new Date(); const t = `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;").length, 1);
  assert.equal(offences("const s = count.toLocaleString();").length, 0);
  assert.equal(offences("const s = amount.toLocaleString('en-US', { style: 'currency', currency: 'USD' });").length, 0);
  assert.equal(offences("const ms = new Date(x).getTime() - Date.now();").length, 0);
  assert.equal(offences("const f = new Intl.DateTimeFormat('en-US', { timeZone: zone });").length, 0);
  assert.equal(offences("// new Date(x).toLocaleString() in a comment").length, 0);
});
