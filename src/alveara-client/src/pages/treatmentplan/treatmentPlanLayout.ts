/**
 * ALV-015-C01: what the revise form and the phase grouping know, as pure functions. The form edits a copy (a draft) of the plan's current layout: its rationale, its patient-facing explanation, its phases and
 * where each live procedure sits; nothing is sent until the person saves with a reason, and the server judges every part again (its messages are the ones shown beside the fields). Field names here are the
 * server's: `reason`, `clinicalRationale`, `patientExplanation`, `phases`, `phases[0].name`, `placements[0].phaseNumber`.
 */
import type { PlanItem, PlanPhase, RevisionDetail, RevisionItem, ReviseInput, TreatmentPlan } from "../../services/treatmentPlansApi";

export const PHASES_MAX = 12;
export const TEXT_MAX = 2000;
export const PHASE_NAME_MAX = 80;
export const VISIT_MAX = 120;

export interface PhaseDraft { number: number; name: string; plannedVisit: string; targetDate: string }
export interface ReviseDraft { reason: string; rationale: string; explanation: string; phases: PhaseDraft[]; placement: Record<string, number | null> }

export interface PhaseGroup { phase: PlanPhase | null; items: PlanItem[] }

/** The live (not withdrawn) procedures: the only ones that can be placed. */
export const liveItems = (plan: TreatmentPlan) => plan.items.filter((i) => !i.isWithdrawn);

/** A copy of the plan's current layout to edit; the reason starts empty because every revision says why. */
export function draftFromPlan(plan: TreatmentPlan): ReviseDraft {
  return {
    reason: "",
    rationale: plan.clinicalRationale ?? "",
    explanation: plan.patientExplanation ?? "",
    phases: plan.phases.map((p) => ({ number: p.phaseNumber, name: p.name, plannedVisit: p.plannedVisit ?? "", targetDate: p.targetDate ?? "" })),
    placement: Object.fromEntries(liveItems(plan).map((i) => [i.id, i.phaseNumber])),
  };
}

/** The smallest phase number not yet used, or null when all twelve are. Numbers are a phase's identity across revisions, so a removed phase's number may be reused. */
export function nextPhaseNumber(phases: readonly PhaseDraft[]): number | null {
  const used = new Set(phases.map((p) => p.number));
  for (let n = 1; n <= PHASES_MAX; n++) if (!used.has(n)) return n;
  return null;
}

/** Removing a phase puts the procedures that sat in it back to "not yet placed", so the person sees where they went before saving. */
export function removePhase(draft: ReviseDraft, number: number): ReviseDraft {
  return {
    ...draft,
    phases: draft.phases.filter((p) => p.number !== number),
    placement: Object.fromEntries(Object.entries(draft.placement).map(([id, phase]) => [id, phase === number ? null : phase])),
  };
}

const hasControl = (value: string, allowLineBreaks = false) => [...value].some((c) => { const code = c.charCodeAt(0); return (code <= 0x1f || code === 0x7f) && !(allowLineBreaks && (c === "\n" || c === "\r" || c === "\t")); });

/** The messages for what is plainly wrong, keyed by the server's field names. Empty when the draft can be sent. */
export function reviseGaps(draft: ReviseDraft): Record<string, string> {
  const gaps: Record<string, string> = {};
  if (draft.reason.trim() === "") gaps.reason = "Say why this plan is being revised.";
  if (draft.rationale.trim().length > TEXT_MAX) gaps.clinicalRationale = `At most ${TEXT_MAX} characters; this has ${draft.rationale.trim().length}.`;
  else if (hasControl(draft.rationale, true)) gaps.clinicalRationale = "Use plain text, without control characters.";
  if (draft.explanation.trim().length > TEXT_MAX) gaps.patientExplanation = `At most ${TEXT_MAX} characters; this has ${draft.explanation.trim().length}.`;
  else if (hasControl(draft.explanation, true)) gaps.patientExplanation = "Use plain text, without control characters.";
  draft.phases.forEach((p, i) => {
    if (p.name.trim() === "") gaps[`phases[${i}].name`] = "Name the phase.";
    else if (p.name.trim().length > PHASE_NAME_MAX) gaps[`phases[${i}].name`] = `At most ${PHASE_NAME_MAX} characters.`;
    if (p.plannedVisit.trim().length > VISIT_MAX) gaps[`phases[${i}].plannedVisit`] = `At most ${VISIT_MAX} characters.`;
  });
  return gaps;
}

/** What is sent: every phase as laid out and the place of every live procedure, so a removed phase never leaves a procedure behind. */
export function toReviseInput(draft: ReviseDraft, live: readonly PlanItem[]): ReviseInput {
  return {
    reason: draft.reason.trim(),
    clinicalRationale: draft.rationale,
    patientExplanation: draft.explanation,
    phases: draft.phases.map((p) => ({ number: p.number, name: p.name, plannedVisit: p.plannedVisit.trim() || null, targetDate: p.targetDate || null })),
    placements: live.map((i) => ({ itemId: i.id, phaseNumber: draft.placement[i.id] ?? null })),
  };
}

/** The plan's procedures grouped under its phases in phase order, then the ones not yet placed. A plan with no phases is one ungrouped list (phase null). */
export function groupItems(plan: TreatmentPlan): PhaseGroup[] {
  if (plan.phases.length === 0) return [{ phase: null, items: plan.items }];
  const phases = [...plan.phases].sort((a, b) => a.phaseNumber - b.phaseNumber);
  const groups: PhaseGroup[] = phases.map((phase) => ({ phase, items: plan.items.filter((i) => i.phaseNumber === phase.phaseNumber) }));
  const loose = plan.items.filter((i) => i.phaseNumber === null || !phases.some((p) => p.phaseNumber === i.phaseNumber));
  return loose.length > 0 ? [...groups, { phase: null, items: loose }] : groups;
}

export interface RevisionGroup { phase: PlanPhase | null; items: RevisionItem[] }

/** The procedures of one saved revision grouped under that revision's phases, then the ones it had not placed. A revision with no phases is one list. */
export function groupRevisionItems(revision: RevisionDetail): RevisionGroup[] {
  if (revision.phases.length === 0) return [{ phase: null, items: revision.items }];
  const phases = [...revision.phases].sort((a, b) => a.phaseNumber - b.phaseNumber);
  const groups: RevisionGroup[] = phases.map((phase) => ({ phase, items: revision.items.filter((i) => i.phaseNumber === phase.phaseNumber) }));
  const loose = revision.items.filter((i) => i.phaseNumber === null || !phases.some((p) => p.phaseNumber === i.phaseNumber));
  return loose.length > 0 ? [...groups, { phase: null, items: loose }] : groups;
}

export const phaseTitle = (phase: PlanPhase) => `Phase ${phase.phaseNumber}: ${phase.name}`;

/** Where a phase falls in time, in words: the planned-visit note and the target date, whichever exist. */
export function phaseWhen(phase: PlanPhase): string | null {
  const parts = [phase.plannedVisit, phase.targetDate ? `target ${new Date(`${phase.targetDate}T00:00:00`).toLocaleDateString()}` : null].filter((x): x is string => !!x);
  return parts.length > 0 ? parts.join(" · ") : null;
}
