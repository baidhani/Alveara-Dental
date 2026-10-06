import { describe, it, expect } from "vitest";
import { CODING_SYSTEMS, REGIONS, SOURCES, checkStructure, structureInputFromDraft, EMPTY_STRUCTURE } from "./diagnosisStructureRules";
import type { StructureInput } from "./diagnosisStructureRules";

/**
 * ALV-013-C01: the client mirror of the server's structure rules (coding, source, region), held to the SAME cases and the SAME codes as the backend DiagnosisStructureRulesTests: a diagnosis needs no
 * coding; a coding system and its code come together; an unknown system, source or region is refused by name; a code is never checked against a code set; a source note belongs to an imported or mapped
 * diagnosis; a region and a tooth exclude each other; every problem is reported together.
 */
const base: StructureInput = { toothKey: null, codingSystem: null, code: null, source: null, sourceNote: null, regionKey: null };
const codes = (over: Partial<StructureInput>) => checkStructure({ ...base, ...over }).problems.map((p) => `${p.field}:${p.code}`);

describe("an entry with no coding", () => {
  it("is accepted and is manual and uncoded", () => {
    const c = checkStructure(base);
    expect(c.problems).toEqual([]);
    expect(c.value).toEqual({ codingSystem: null, code: null, source: "Manual", sourceNote: null, regionKey: null });
  });
});

describe("coding", () => {
  it.each([["ICD-10-CM", "K05.311"], ["SNODENT", "1234567"], ["Local", "PERIO_2"], ["Local", "a-b.c_9"]])("%s with %s survives normalization unchanged", (system, code) => {
    expect(checkStructure({ ...base, codingSystem: system, code }).value).toMatchObject({ codingSystem: system, code });
  });

  it("trims the code and allows exactly 30 characters, not 31", () => {
    expect(checkStructure({ ...base, codingSystem: "ICD-10-CM", code: "  K05.3  " }).value?.code).toBe("K05.3");
    expect(codes({ codingSystem: "Local", code: "A".repeat(30) })).toEqual([]);
    expect(codes({ codingSystem: "Local", code: "A".repeat(31) })).toEqual(["code:too_long"]);
  });

  it.each(["icd-10-cm", "ICD10", "SNOMED", "", " ", "Local "])("refuses the unknown or differently spelled system %j by name", (system) => {
    expect(codes({ codingSystem: system, code: "X1" })).toContain("codingSystem:unsupported_system");
  });

  it("needs the code with the system and the system with the code", () => {
    expect(codes({ codingSystem: "ICD-10-CM" })).toEqual(["code:coding_incomplete"]);
    expect(codes({ code: "K05" })).toEqual(["codingSystem:coding_incomplete"]);
  });

  it.each(["", "   "])("refuses the blank code %j rather than reading it as no code", (code) => {
    expect(codes({ codingSystem: "Local", code })).toContain("code:blank");
  });

  it.each(["K05 3", "K05/3", "K05,3", "é1", "K05\n3"])("refuses the code %j for a character a code cannot have", (code) => {
    expect(codes({ codingSystem: "Local", code })).toContain("code:invalid_characters");
  });

  it("never checks a code against a code set", () => {
    expect(codes({ codingSystem: "ICD-10-CM", code: "ZZZ99.9" })).toEqual([]);
    expect(codes({ codingSystem: "SNODENT", code: "0" })).toEqual([]);
  });

  it("lists exactly the three coding systems, as the server does", () => expect([...CODING_SYSTEMS]).toEqual(["ICD-10-CM", "SNODENT", "Local"]));
});

describe("source", () => {
  it.each(["Manual", "Imported", "Mapped"])("accepts %s", (source) => expect(checkStructure({ ...base, source }).value?.source).toBe(source));
  it.each(["manual", "Unknown", "", " "])("refuses %j, and a blank source is not manual", (source) => expect(codes({ source })).toEqual(["source:unsupported_source"]));

  it("lets a note belong to an imported or mapped diagnosis only, and normalizes and bounds it", () => {
    expect(checkStructure({ ...base, source: "Imported", sourceNote: "  From   the old chart " }).value?.sourceNote).toBe("From the old chart");
    expect(codes({ source: "Mapped", sourceNote: "n".repeat(200) })).toEqual([]);
    expect(codes({ source: "Mapped", sourceNote: "n".repeat(201) })).toEqual(["sourceNote:too_long"]);
    expect(codes({ sourceNote: "from somewhere" })).toEqual(["sourceNote:source_required"]);
    expect(codes({ source: "Manual", sourceNote: "from somewhere" })).toEqual(["sourceNote:source_required"]);
    expect(codes({ source: "Imported", sourceNote: "   " })).toEqual(["sourceNote:blank"]);
    expect(codes({ source: "Imported", sourceNote: "two\nlines" })).toEqual(["sourceNote:invalid_characters"]);
  });

  it("lists exactly the three sources", () => expect([...SOURCES]).toEqual(["Manual", "Imported", "Mapped"]));
});

describe("region", () => {
  it.each([...REGIONS])("accepts %s without a tooth", (region) => expect(checkStructure({ ...base, regionKey: region }).value?.regionKey).toBe(region));
  it.each(["fullmouth", "Mouth", "", " "])("refuses %j, and a blank region is not none", (region) => expect(codes({ regionKey: region })).toEqual(["regionKey:unknown_region"]));
  it("is about a tooth or a region, not both", () => {
    expect(codes({ regionKey: "UpperArch", toothKey: "16" })).toEqual(["regionKey:conflicts_with_tooth"]);
    expect(codes({ toothKey: "16" })).toEqual([]);
  });
  it("lists the nine regions the server lists", () => expect(REGIONS).toHaveLength(9));
});

describe("the whole entry", () => {
  it("reports every problem together and accepts nothing", () => {
    const c = checkStructure({ toothKey: "16", codingSystem: "SNOMED", code: null, source: "Wrong", sourceNote: "x", regionKey: "Nowhere" });
    expect(c.value).toBeNull();
    const found = c.problems.map((p) => `${p.field}:${p.code}`);
    for (const expected of ["codingSystem:unsupported_system", "code:coding_incomplete", "source:unsupported_source", "regionKey:unknown_region"]) expect(found).toContain(expected);
  });

  it("reads an empty box as not supplied", () => {
    expect(structureInputFromDraft("", EMPTY_STRUCTURE)).toEqual(base);
    expect(structureInputFromDraft("16", { ...EMPTY_STRUCTURE, code: "   " })).toMatchObject({ toothKey: "16", code: "   " });          // spaces are typed, so they are refused as blank, as the server does
  });
});
