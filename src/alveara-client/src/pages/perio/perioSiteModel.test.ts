import { createHash } from "node:crypto";
import { describe, it, expect } from "vitest";
import { ALL_KEYS } from "../odontogram/toothNumbering";
import { SITES } from "./perioChartRules";
import { ARCH_ORDER, CUE_TEXT, cuesOf, describeSite, isMultiRooted, sweep, sweepText } from "./perioSiteModel";

/**
 * ALV-012-C01: the client's six-site model is held to the backend's. The reference sites and the indexes of the sweep are the same ones PerioSiteModelTests asserts, and the SHA-256 of the whole sweep
 * is asserted in both languages, so the entry order on screen can never drift from the order the server uses to say what comes next.
 */
const SWEEP_SHA256 = "c237982f1354edf763319b77e502ec67405691ab0d74f927c7f23f1a2a7d23bd";

describe("describeSite", () => {
  it.each([
    ["16", "DB", "distal buccal"], ["16", "B", "mid buccal"], ["16", "MB", "mesial buccal"], ["16", "DL", "distal palatal"], ["16", "L", "mid palatal"], ["16", "ML", "mesial palatal"],
    ["46", "DL", "distal lingual"], ["46", "ML", "mesial lingual"], ["11", "B", "mid facial"], ["11", "MB", "mesial facial"], ["11", "DL", "distal palatal"], ["33", "DB", "distal facial"], ["33", "L", "mid lingual"],
  ])("tooth %s site %s reads as %s", (tooth, site, text) => expect(describeSite(tooth, site)).toBe(text));

  it("all six sites read differently on every one of the 32 teeth", () => {
    for (const tooth of ARCH_ORDER) expect(new Set(SITES.map((s) => describeSite(tooth, s))).size).toBe(6);
  });

  it.each([["55", "B"], ["16", "X"], ["", "B"]])("throws for %j %j, which is not a site of a permanent tooth", (tooth, site) => expect(() => describeSite(tooth, site)).toThrow());
});

describe("the sweep", () => {
  const order = sweep();
  it("visits every site of every tooth exactly once", () => {
    expect(order).toHaveLength(192);
    expect(new Set(order.map((s) => `${s.toothKey}${s.site}`)).size).toBe(192);
  });

  it("starts at the upper right distal cheek side, snakes back along the tongue side, then does the lower arch the same way", () => {
    const at = (i: number) => `${order[i].toothKey}${order[i].site}`;
    expect([at(0), at(23), at(24), at(47), at(48), at(95), at(96), at(144), at(191)]).toEqual(["18DB", "11MB", "21MB", "28DB", "28DL", "18DL", "48DB", "38DL", "48DL"]);
  });

  it("never jumps more than one tooth within an arch", () => {
    for (let i = 1; i < order.length; i++) {
      const [a, b] = [ARCH_ORDER.indexOf(order[i - 1].toothKey), ARCH_ORDER.indexOf(order[i].toothKey)];
      if (Math.floor(a / 16) !== Math.floor(b / 16)) continue;
      expect(Math.abs(a - b)).toBeLessThanOrEqual(1);
    }
  });

  it("skips excluded teeth without disturbing the order of the rest", () => {
    const skipped = sweep(new Set(["16", "46"]));
    expect(skipped).toHaveLength(180);
    expect(skipped).toEqual(order.filter((s) => s.toothKey !== "16" && s.toothKey !== "46"));
    expect(sweep(new Set(ARCH_ORDER))).toEqual([]);
  });

  it("is the same order as the backend's (checksum of the whole sequence)", () => {
    expect(createHash("sha256").update(sweepText()).digest("hex")).toBe(SWEEP_SHA256);
  });

  it("covers exactly the 32 permanent teeth in arch order, upper 18..28 then lower 48..38", () => {
    expect(ARCH_ORDER).toHaveLength(32);
    expect([ARCH_ORDER[0], ARCH_ORDER[15], ARCH_ORDER[16], ARCH_ORDER[31]]).toEqual(["18", "28", "48", "38"]);
    expect(ARCH_ORDER.every((k) => ALL_KEYS.includes(k))).toBe(true);
  });
});

describe("furcation applies only to multi-rooted teeth", () => {
  it.each([["16", true], ["17", true], ["18", true], ["26", true], ["36", true], ["48", true], ["14", true], ["24", true], ["15", false], ["34", false], ["44", false], ["11", false], ["13", false], ["55", false], ["99", false]])
    ("%s -> %s", (tooth, expected) => expect(isMultiRooted(tooth)).toBe(expected));
});

describe("visual cues (never a diagnosis)", () => {
  const r = (probingDepthMm: number, recessionMm = 0, bleeding = false, suppuration: boolean | null = null) => ({ probingDepthMm, recessionMm, bleeding, suppuration });
  it.each([
    [r(3), ""], [r(4), "deep"], [r(5), "deep"], [r(6), "very_deep"], [r(3, 2), "recession"], [r(3, 0, true), "bleeding"], [r(3, 0, false, true), "suppuration"], [r(3, 0, false, false), ""],
    [r(7, 3, true, true), "very_deep,recession,bleeding,suppuration"],
  ])("%j -> %j", (reading, expected) => expect(cuesOf(reading).join(",")).toBe(expected));

  it("every cue has words, so a mark is never the only signal", () => {
    expect(Object.keys(CUE_TEXT).sort()).toEqual(["bleeding", "deep", "recession", "suppuration", "very_deep"]);
    expect(Object.values(CUE_TEXT).every((t) => t.length > 0)).toBe(true);
  });
});
