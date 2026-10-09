import { describe, expect, it } from "vitest";
import type { PlanningProcedure } from "../../services/treatmentPlansApi";
import { fieldMessage, formatFee, itemGap, otherMessages, siteNeedOf, titleGap, toItemInput } from "./treatmentPlanRules";

const proc = (scope: string, dentition = "Both"): PlanningProcedure => ({ procedureId: "p1", versionId: "v1", versionNumber: 1, codeSystem: "Local", code: "L1", description: "d", category: "Diagnostic", scope, dentition, fee: 10, currency: "USD" });
const draft = (procedure: PlanningProcedure | null, over: Partial<{ diagnosisId: string; toothKey: string; surface: string }> = {}) => ({ diagnosisId: "d1", procedure, toothKey: "", surface: "", ...over });

describe("siteNeedOf", () => {
  it("asks for a tooth only where the procedure is done on one", () => {
    expect(["WholeMouth", "Arch", "Quadrant", "Tooth", "ToothSurface"].map(siteNeedOf)).toEqual(["none", "none", "none", "tooth", "toothAndSurface"]);
  });
});

describe("itemGap", () => {
  it("names the first thing missing", () => {
    expect(itemGap(draft(null, { diagnosisId: "" }))).toMatch(/diagnosis/);
    expect(itemGap(draft(null))).toMatch(/procedure/);
    expect(itemGap(draft(proc("Tooth")))).toBe("Choose the tooth.");
    expect(itemGap(draft(proc("ToothSurface"), { toothKey: "16" }))).toBe("Choose the surface.");
  });
  it("accepts a complete item and a whole-mouth procedure with no site", () => {
    expect(itemGap(draft(proc("WholeMouth")))).toBeNull();
    expect(itemGap(draft(proc("ToothSurface"), { toothKey: "16", surface: "O" }))).toBeNull();
  });
  it("refuses something that is not a tooth and a tooth of the wrong dentition", () => {
    expect(itemGap(draft(proc("Tooth"), { toothKey: "99" }))).toBe("That is not a tooth.");
    expect(itemGap(draft(proc("Tooth", "Permanent"), { toothKey: "55" }))).toMatch(/permanent/);
    expect(itemGap(draft(proc("Tooth", "Primary"), { toothKey: "16" }))).toMatch(/primary/);
  });
});

describe("toItemInput", () => {
  it("drops a tooth and surface left over from a different procedure", () => {
    expect(toItemInput(draft(proc("WholeMouth"), { toothKey: "16", surface: "o" }))).toEqual({ diagnosisId: "d1", procedureId: "p1", toothKey: null, surface: null });
    expect(toItemInput(draft(proc("Tooth"), { toothKey: "16", surface: "o" }))).toMatchObject({ toothKey: "16", surface: null });
    expect(toItemInput(draft(proc("ToothSurface"), { toothKey: "16", surface: "o" }))).toMatchObject({ toothKey: "16", surface: "O" });
  });
});

describe("titleGap, formatFee and the message lookups", () => {
  it("requires a title within the limit", () => {
    expect([titleGap("  "), titleGap("Plan"), titleGap("x".repeat(201))]).toEqual(["Give the plan a title.", null, expect.stringContaining("at most 200")]);
  });
  it("formats US dollars with cents", () => {
    expect([formatFee(1234.5), formatFee(0)]).toEqual(["$1,234.50", "$0.00"]);
  });
  it("finds a field's message with the item prefix and lists the rest", () => {
    const errors = { title: "T", "items[0].diagnosisId": "D", "items[0]": "same procedure" };
    expect(fieldMessage(errors, "diagnosisId", "items[0].")).toBe("D");
    expect(fieldMessage(errors, "procedureId", "items[0].")).toBeUndefined();
    expect(otherMessages(errors, ["title", "items[0].diagnosisId"])).toEqual(["same procedure"]);
  });
});
