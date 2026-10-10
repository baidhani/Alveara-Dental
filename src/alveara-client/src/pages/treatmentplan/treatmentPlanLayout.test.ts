import { describe, expect, it } from "vitest";
import type { PlanItem, RevisionDetail, TreatmentPlan } from "../../services/treatmentPlansApi";
import { draftFromPlan, groupItems, groupRevisionItems, liveItems, nextPhaseNumber, phaseTitle, phaseWhen, removePhase, reviseGaps, toReviseInput } from "./treatmentPlanLayout";

const item = (id: string, phaseNumber: number | null, over: Partial<PlanItem> = {}): PlanItem => ({
  id, itemNumber: 1, diagnosisId: "d", diagnosisLabel: "Dx", procedureId: "p", procedureVersionId: "v", procedureVersionNumber: 1, procedureCodeSystem: "Local", procedureCode: `C-${id}`, procedureDescription: "Proc",
  toothKey: null, surface: null, fee: 10, currency: "USD", isWithdrawn: false, withdrawnReason: null, withdrawnAtUtc: null, withdrawnByName: null, createdAtUtc: "2030-01-01T00:00:00Z", createdByName: null,
  phaseNumber, acceptance: "Pending", scheduling: "Unscheduled", completion: "NotStarted", plannedDate: null, decisionReason: null, decidedByName: null, recordedByName: null, recordedAtUtc: null, decisionRevision: null, ...over,
});
const plan = (items: PlanItem[], phases: TreatmentPlan["phases"] = []): TreatmentPlan => ({
  id: "pl", patientId: "pt", title: "Plan", status: "Proposed", activeItemCount: items.length, estimateTotal: 0, currency: "USD", estimateLabel: "label", items, createdAtUtc: "2030-01-01T00:00:00Z", createdByName: null,
  updatedAtUtc: null, updatedByName: null, withdrawnReason: null, withdrawnAtUtc: null, withdrawnByName: null, rowVersion: "R1", currentRevision: 1, referenceCode: null, clinicalRationale: null, patientExplanation: null,
  phases, acceptanceStatus: "Pending", acceptedEstimateTotal: 0,
});
const phase = (n: number, name = `P${n}`, visit: string | null = null, date: string | null = null) => ({ phaseNumber: n, name, plannedVisit: visit, targetDate: date });

describe("groupItems", () => {
  it("is one plain list when the plan has no phases", () => {
    const g = groupItems(plan([item("a", null), item("b", null)]));
    expect(g).toHaveLength(1);
    expect(g[0].phase).toBeNull();
    expect(g[0].items).toHaveLength(2);
  });
  it("groups under phases in phase order and ends with the procedures not yet placed", () => {
    const g = groupItems(plan([item("a", 2), item("b", null), item("c", 1), item("d", 2), item("e", 9)], [phase(2), phase(1)]));
    expect(g.map((x) => x.phase?.phaseNumber ?? null)).toEqual([1, 2, null]);
    expect(g.map((x) => x.items.map((i) => i.id))).toEqual([["c"], ["a", "d"], ["b", "e"]]);      // a procedure in a phase the plan lacks is shown as not placed
  });
  it("leaves out the empty unplaced group", () => {
    expect(groupItems(plan([item("a", 1)], [phase(1)]))).toHaveLength(1);
  });
});

describe("the revise draft", () => {
  const p = plan([item("a", 1), item("b", 2), item("w", 2, { isWithdrawn: true })], [phase(1, "First", "Today", "2030-06-01"), phase(2, "Second")]);

  it("copies the plan's layout, starts with no reason, and only live procedures can be placed", () => {
    const d = draftFromPlan({ ...p, clinicalRationale: "Why", patientExplanation: "In words" });
    expect(d).toMatchObject({ reason: "", rationale: "Why", explanation: "In words", placement: { a: 1, b: 2 } });
    expect(d.phases).toEqual([{ number: 1, name: "First", plannedVisit: "Today", targetDate: "2030-06-01" }, { number: 2, name: "Second", plannedVisit: "", targetDate: "" }]);
    expect(liveItems(p).map((i) => i.id)).toEqual(["a", "b"]);
  });
  it("reuses the smallest free phase number and stops at twelve", () => {
    const d = draftFromPlan(p);
    expect(nextPhaseNumber(d.phases)).toBe(3);
    expect(nextPhaseNumber(removePhase(d, 1).phases)).toBe(1);
    expect(nextPhaseNumber(Array.from({ length: 12 }, (_, i) => ({ number: i + 1, name: "x", plannedVisit: "", targetDate: "" })))).toBeNull();
  });
  it("puts the procedures of a removed phase back to not yet placed", () => {
    const d = removePhase(draftFromPlan(p), 2);
    expect(d.phases.map((x) => x.number)).toEqual([1]);
    expect(d.placement).toEqual({ a: 1, b: null });
  });
  it("names what is plainly wrong under the server's field names", () => {
    const d = { ...draftFromPlan(p), rationale: "x".repeat(2001), explanation: "bell\u0007" };
    d.phases[1] = { ...d.phases[1], name: "  ", plannedVisit: "y".repeat(121) };
    expect(Object.keys(reviseGaps(d)).sort()).toEqual(["clinicalRationale", "patientExplanation", "phases[1].name", "phases[1].plannedVisit", "reason"]);
    expect(reviseGaps({ ...draftFromPlan(p), reason: "Because", rationale: "line one\nline two" })).toEqual({});     // line breaks are fine in the words
  });
  it("sends every phase and the place of every live procedure", () => {
    const d = { ...draftFromPlan(p), reason: "  Merge  ", rationale: "R" };
    d.phases[0] = { ...d.phases[0], targetDate: "" };
    expect(toReviseInput(d, liveItems(p))).toEqual({
      reason: "Merge", clinicalRationale: "R", patientExplanation: "",
      phases: [{ number: 1, name: "First", plannedVisit: "Today", targetDate: null }, { number: 2, name: "Second", plannedVisit: null, targetDate: null }],
      placements: [{ itemId: "a", phaseNumber: 1 }, { itemId: "b", phaseNumber: 2 }],
    });
  });
});

describe("phase wording", () => {
  it("titles a phase and says when it falls", () => {
    expect(phaseTitle(phase(2, "After healing"))).toBe("Phase 2: After healing");
    expect(phaseWhen(phase(1))).toBeNull();
    expect(phaseWhen(phase(1, "x", "Two weeks later"))).toBe("Two weeks later");
    expect(phaseWhen(phase(1, "x", "Later", "2030-06-01"))).toContain("Later · target ");
  });
});

describe("groupRevisionItems", () => {
  const rev = (phases: RevisionDetail["phases"], items: [string, number | null][]): RevisionDetail => ({
    summary: { revisionNumber: 1, changeType: "Created", title: "T", reason: null, actorName: null, createdAtUtc: "2030-01-01T00:00:00Z", procedureCount: items.length, estimateTotal: 0, phaseCount: phases.length },
    clinicalRationale: null, patientExplanation: null, phases, estimateLabel: "label",
    items: items.map(([id, phaseNumber]) => ({ itemId: id, itemNumber: 1, procedureCode: id, procedureDescription: "d", diagnosisLabel: "Dx", toothKey: null, surface: null, fee: 1, isWithdrawn: false, phaseNumber })),
  });
  it("is one list for a revision with no phases", () => {
    const g = groupRevisionItems(rev([], [["a", null], ["b", null]]));
    expect(g.map((x) => x.items.length)).toEqual([2]);
    expect(g[0].phase).toBeNull();
  });
  it("groups by that revision's own phases in order and ends with the procedures it had not placed", () => {
    const g = groupRevisionItems(rev([phase(2), phase(1)], [["a", 2], ["b", null], ["c", 1], ["d", 9]]));
    expect(g.map((x) => x.phase?.phaseNumber ?? null)).toEqual([1, 2, null]);
    expect(g.map((x) => x.items.map((i) => i.itemId))).toEqual([["c"], ["a"], ["b", "d"]]);
  });
});
