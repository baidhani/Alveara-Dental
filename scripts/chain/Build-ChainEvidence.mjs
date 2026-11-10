// ALV-N019 (R05 PKG-01, section 6): turns one chain run's raw results into the evidence file and the run's exit code.
//
// Inputs (all written by scripts/Invoke-AccumulatedChain.ps1 inside the run's evidence folder):
//   --playwright <json>   Playwright's JSON report for the stage spec
//   --runner <json>       steps the runner itself performed (for example the COPY_ONLY checkpoint): [{ stepId, title, passed, detail }]
//   --meta <json>         { runId, stage, implementationSha, schemaVersion, resumed, clockMode }
//   --out <file>          where the evidence file is written
// Each record is { runId, stage, stepId, title, outcome, evidencePath, implementationSha, schemaVersion, resumed, clockMode, findingIds, entries, staleEntries, detail }.
// A failing step tagged with a finding that has an OPEN entry in docs/stabilization/TRACKED_EXCEPTIONS.json is TRACKED_EXCEPTION, never PASS.
// Exit: 0 all steps PASS (or only non-blocking exceptions), 1 any FAIL / SKIPPED_BLOCKED / blocking TRACKED_EXCEPTION, 2 an input could not be read.

import { readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { findingIdsIn, loadTrackedExceptions, resolveOutcome, runExitCode } from "../stabilization/trackedExceptions.mjs";

const STEP_ID = /^([A-Z]{1,2}-\d{2,3}[a-z]?|\d+\.\d{2})\b/;

/** Flattens Playwright's nested suites into [{ title, status, error }]. status: passed | failed | skipped | timedOut | interrupted. */
export function flattenPlaywright(report) {
  const out = [];
  const walk = (suite) => {
    for (const spec of suite.specs ?? []) {
      for (const t of spec.tests ?? []) {
        const results = t.results ?? [];
        const last = results[results.length - 1];
        out.push({ title: spec.title, status: t.status === "expected" ? "passed" : t.status === "skipped" ? "skipped" : last?.status ?? "failed", error: last?.error?.message ?? "" });
      }
    }
    for (const child of suite.suites ?? []) walk(child);
  };
  for (const s of report.suites ?? []) walk(s);
  return out;
}

export function stepIdOf(title) {
  const m = STEP_ID.exec(title);
  return m ? m[1] : title.split(/\s/)[0];
}

/** Builds the evidence records and the exit code. Pure: same inputs, same output. */
export function buildEvidence({ playwright, runnerSteps, meta, evidencePath }, trackedRegister, { requireNoBlocking = false } = {}) {
  const steps = [
    ...flattenPlaywright(playwright).map((t) => ({ title: t.title, passed: t.status === "passed", skipped: t.status === "skipped", detail: t.error })),
    ...runnerSteps.map((s) => ({ title: s.title, passed: s.passed === true, skipped: false, detail: s.detail ?? "", stepId: s.stepId })),
  ];
  const records = steps.map((s) => {
    const findingIds = findingIdsIn(s.title);
    const base = s.skipped ? { outcome: "SKIPPED_BLOCKED", entries: [], staleEntries: [] } : resolveOutcome({ passed: s.passed, findingIds }, trackedRegister);
    return {
      runId: meta.runId, stage: meta.stage, stepId: s.stepId ?? stepIdOf(s.title), title: s.title, outcome: base.outcome, evidencePath,
      implementationSha: meta.implementationSha, schemaVersion: meta.schemaVersion, resumed: meta.resumed === true, clockMode: meta.clockMode,
      findingIds, entries: base.entries, staleEntries: base.staleEntries, detail: s.detail,
    };
  });
  const exitCode = runExitCode(records, trackedRegister, { requireNoBlocking });
  const counts = Object.fromEntries(["PASS", "FAIL", "TRACKED_EXCEPTION", "SKIPPED_BLOCKED"].map((o) => [o, records.filter((r) => r.outcome === o).length]));
  return { evidence: { evidenceSchemaVersion: 1, runId: meta.runId, stage: meta.stage, label: meta.resumed === true ? "RESUMED" : "FRESH", counts, exitCode, records }, exitCode };
}

/** Reads JSON the PowerShell runner wrote; Windows PowerShell 5.1 puts a byte-order mark at the start of a UTF-8 file, which JSON.parse rejects. */
export function readJson(file) { return JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, "")); }

function arg(argv, name) { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : undefined; }

export function run(argv, write = (line) => console.log(line)) {
  const files = { playwright: arg(argv, "--playwright"), runner: arg(argv, "--runner"), meta: arg(argv, "--meta"), out: arg(argv, "--out") };
  for (const [k, v] of Object.entries(files)) if (!v) { write(`ERROR missing --${k}`); return 2; }
  let playwright, runnerSteps, meta, tracked;
  try {
    playwright = readJson(files.playwright);
    runnerSteps = readJson(files.runner);
    meta = readJson(files.meta);
    tracked = loadTrackedExceptions();
  } catch (e) { write(`ERROR ${e.message}`); return 2; }
  const { evidence, exitCode } = buildEvidence({ playwright, runnerSteps, meta, evidencePath: path.dirname(path.resolve(files.out)) }, tracked, { requireNoBlocking: argv.includes("--require-no-blocking") });
  writeFileSync(files.out, JSON.stringify(evidence, null, 2) + "\n", "utf8");
  for (const r of evidence.records) write(`${r.outcome.padEnd(17)} ${r.stepId}  ${r.title}${r.staleEntries.length ? `   (stale tracked entry: ${r.staleEntries.join(", ")})` : ""}`);
  write(`${evidence.label} run ${meta.runId} stage ${meta.stage}: ${Object.entries(evidence.counts).map(([k, v]) => `${k} ${v}`).join(", ")}; exit ${exitCode}`);
  return exitCode;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = run(process.argv.slice(2));
