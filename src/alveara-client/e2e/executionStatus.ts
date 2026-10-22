import { readFileSync } from "node:fs";
import path from "node:path";
import vm from "node:vm";

/**
 * ALV-CONTROL-C01: the status vocabulary the Command Center specs check against, with nothing duplicated.
 *  - allowedStatuses(): the statuses .alveara/HANDOFF_SCHEMA.md section 1 permits (its lifecycle lines and its exceptional-state bullets), the authoritative set.
 *  - statusLabels(): the labels the page itself uses, read from assets/status-labels.js (the one table map.js and app.js both read).
 * The specs assert the two agree exactly, so a status added to the schema without a label (or a label for a status the schema does not allow) fails here.
 */
const ROOT = path.resolve(process.cwd(), "..", "..");

export function allowedStatuses(): string[] {
  const schema = readFileSync(path.join(ROOT, ".alveara", "HANDOFF_SCHEMA.md"), "utf-8").replace(/\r\n/g, "\n");
  const start = schema.indexOf("## 1. Status Lifecycle");
  const end = schema.indexOf("\n## 2.", start);
  if (start < 0 || end < 0) throw new Error("HANDOFF_SCHEMA.md section 1 (Status Lifecycle) was not found");
  const section = schema.slice(start, end);
  const found = new Set<string>();
  for (const fence of section.matchAll(/```[^\n]*\n([\s\S]*?)```/g)) for (const part of fence[1].split(/→|\n/)) if (/^[A-Z][A-Z_]+$/.test(part.trim())) found.add(part.trim());
  for (const bullet of section.matchAll(/^- `([A-Z][A-Z_]+)`/gm)) found.add(bullet[1]);
  return [...found];
}

export function statusLabels(): Record<string, string> {
  const source = readFileSync(path.join(ROOT, "assets", "status-labels.js"), "utf-8");
  const context: { labels?: Record<string, string> } = {};
  vm.runInNewContext(`${source}\nthis.labels = EXECUTION_STATUS_LABELS;`, context);
  return { ...context.labels };
}
