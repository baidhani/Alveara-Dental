import { describe, it, expect } from "vitest";
import { ALL_KEYS, DEFAULT_NUMBERING, LOWER_PERMANENT, PRIMARY_KEYS, UPPER_PERMANENT, displayTooth, isAnterior, isToothKey, parseTooth, surfacesFor, toothName } from "./toothNumbering";
import type { NumberingSystem } from "./toothNumbering";

// STORY-006: the display-only numbering systems, held to the same reference teeth as the backend's ToothNumberingTests (Alveara.Api.Tests), so the chart and the server can never read
// a tooth differently. The stored identity is the FDI key; Universal and Palmer are only how it is shown.

const SYSTEMS: NumberingSystem[] = ["Universal", "Fdi", "Palmer"];

describe("the tooth keys", () => {
  it("are 52: 32 permanent and 20 primary, with no duplicates", () => {
    expect(UPPER_PERMANENT.length + LOWER_PERMANENT.length).toBe(32);
    expect(PRIMARY_KEYS).toHaveLength(20);
    expect(new Set(ALL_KEYS).size).toBe(52);
  });

  it.each([["11", true], ["48", true], ["55", true], ["85", true], ["19", false], ["10", false], ["49", false], ["56", false], ["86", false], ["90", false], ["1", false], ["111", false], [" 11", false], ["", false], ["1A", false]])(
    "%j is a key: %s", (key, valid) => expect(isToothKey(key)).toBe(valid),
  );
  it("null and undefined are not keys", () => {
    expect(isToothKey(null)).toBe(false);
    expect(isToothKey(undefined)).toBe(false);
  });
});

describe("numbering systems", () => {
  it.each(SYSTEMS)("%s shows each of the 52 teeth once and reads it back", (system) => {
    const shown = ALL_KEYS.map((k) => displayTooth(k, system));
    expect(new Set(shown).size).toBe(52);
    for (const key of ALL_KEYS) expect(parseTooth(displayTooth(key, system), system)).toBe(key);
  });

  it.each([
    ["18", "1"], ["11", "8"], ["21", "9"], ["28", "16"], ["38", "17"], ["31", "24"], ["41", "25"], ["48", "32"],
    ["55", "A"], ["51", "E"], ["61", "F"], ["65", "J"], ["75", "K"], ["71", "O"], ["81", "P"], ["85", "T"],
  ])("Universal matches the published chart: FDI %s is %s", (key, universal) => expect(displayTooth(key, "Universal")).toBe(universal));

  it.each([
    ["11", "UR1"], ["18", "UR8"], ["21", "UL1"], ["28", "UL8"], ["31", "LL1"], ["38", "LL8"], ["41", "LR1"], ["48", "LR8"],
    ["51", "URA"], ["55", "URE"], ["61", "ULA"], ["75", "LLE"], ["81", "LRA"], ["85", "LRE"],
  ])("Palmer matches the published chart: FDI %s is %s", (key, palmer) => expect(displayTooth(key, "Palmer")).toBe(palmer));

  it("FDI display is the key itself, and Universal is the default system", () => {
    for (const k of ALL_KEYS) expect(displayTooth(k, "Fdi")).toBe(k);
    expect(DEFAULT_NUMBERING).toBe("Universal");
    expect(displayTooth("16")).toBe("3");
  });

  it("Universal runs 1 to 32 and A to T without gaps", () => {
    const permanent = [...UPPER_PERMANENT, ...LOWER_PERMANENT].map((k) => Number(displayTooth(k, "Universal"))).sort((a, b) => a - b);
    expect(permanent).toEqual(Array.from({ length: 32 }, (_, i) => i + 1));
    expect(PRIMARY_KEYS.map((k) => displayTooth(k, "Universal")).sort().join("")).toBe("ABCDEFGHIJKLMNOPQRST");
  });

  it.each([
    ["1", "Universal", "18"], [" 32 ", "Universal", "48"], ["a", "Universal", "55"], ["ur1", "Palmer", "11"], [" LLe ", "Palmer", "75"], ["48", "Fdi", "48"],
  ] as const)("parse reads what a person types: %j in %s is %s", (text, system, key) => expect(parseTooth(text, system)).toBe(key));

  it.each([
    ["0", "Universal"], ["33", "Universal"], ["U", "Universal"], ["1a", "Universal"], ["UR9", "Palmer"], ["UR0", "Palmer"], ["URF", "Palmer"], ["XX1", "Palmer"], ["UR", "Palmer"],
    ["19", "Fdi"], ["A", "Fdi"], ["", "Universal"],
  ] as const)("parse refuses what is not exactly a tooth instead of guessing: %j in %s", (text, system) => expect(parseTooth(text, system)).toBeNull());

  it("displaying something that is not a key is a defect, not a blank", () => {
    expect(() => displayTooth("19", "Universal")).toThrow();
    expect(() => displayTooth("", "Palmer")).toThrow();
  });
});

describe("the chart layout and the surfaces", () => {
  it("draws the upper arch 18..11 then 21..28 and the lower arch 48..41 then 31..38, as a dentist looks at the patient", () => {
    expect(UPPER_PERMANENT.join(" ")).toBe("18 17 16 15 14 13 12 11 21 22 23 24 25 26 27 28");
    expect(LOWER_PERMANENT.join(" ")).toBe("48 47 46 45 44 43 42 41 31 32 33 34 35 36 37 38");
  });

  it("anterior teeth (positions 1 to 3) have Incisal and Facial surfaces; posterior ones Occlusal and Buccal - the same rule as the server", () => {
    expect(isAnterior("11")).toBe(true);
    expect(isAnterior("53")).toBe(true);
    expect(isAnterior("14")).toBe(false);
    expect(isAnterior("55")).toBe(false);
    expect(surfacesFor("21")).toEqual(["M", "I", "D", "F", "L"]);
    expect(surfacesFor("36")).toEqual(["M", "O", "D", "B", "L"]);
  });

  it("names a tooth in plain words whatever system is shown", () => {
    expect(toothName("18")).toBe("upper right third molar");
    expect(toothName("31")).toBe("lower left central incisor");
    expect(toothName("54")).toBe("upper right primary first molar");
  });
});
