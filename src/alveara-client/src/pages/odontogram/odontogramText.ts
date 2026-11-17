import { FINDING_STATES } from "../../services/odontogramApi";
import type { Finding, FindingState } from "../../services/odontogramApi";
import { showInstant } from "../../time/practiceTime";

export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
export const when = (iso: string) => showInstant(iso);

/** What each state means, in words: the legend and the chart show the word, never only a colour or a border. */
export const STATE_MEANING: Record<FindingState, string> = {
  Existing: "already in the mouth",
  Diagnosed: "found, not yet planned",
  Planned: "treatment planned",
  Completed: "treatment completed",
};

/** The state a tooth shows on the chart when it has findings in several: what most needs attention first (diagnosed, then planned, then completed, then existing). */
const PRIORITY: FindingState[] = ["Diagnosed", "Planned", "Completed", "Existing"];

export function headlineState(findings: Finding[]): FindingState | null {
  return PRIORITY.find((s) => findings.some((f) => f.state === s)) ?? null;
}

export const findingsByTooth = (findings: Finding[]) => {
  const map = new Map<string, Finding[]>();
  for (const f of findings) map.set(f.toothKey, [...(map.get(f.toothKey) ?? []), f]);
  return map;
};

export const stateClass = (state: FindingState) => `alv-odonto__state--${state.toLowerCase()}`;
export const ALL_STATES = FINDING_STATES;

/** What a finding is, in a few words: "Caries (Occlusal surface)" or "Crown (whole tooth)". The label is the condition's own, from the catalogue. */
export const describeFinding = (conditionLabel: string, surfaceName: string | null) => `${conditionLabel} (${surfaceName ? `${surfaceName} surface` : "whole tooth"})`;

/**
 * ALV-N022 A3 (F-091, OD-09, display only): the tooth is drawn as not present ONLY when it has an active finding of an Absent condition in state Existing or Completed. A Diagnosed or
 * Planned missing-tooth finding shows its state like any other finding, without the mark. Nothing about extraction intent or implants is inferred; the recording rule is unchanged.
 */
export const isDrawnAbsent = (findings: Finding[], absentConditions: ReadonlySet<string>) =>
  findings.some((f) => f.status === "Active" && absentConditions.has(f.condition) && (f.state === "Existing" || f.state === "Completed"));
