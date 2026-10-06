import { describe, it, expect } from "vitest";
import { checkDiagnosis, entryFromDraft, normalizeLine, normalizeNotes } from "./diagnosisRules";

/**
 * STORY-013: the form's rules are held to the server's. These are the same cases as the backend's DiagnosisRulesTests, with the same field and code for each, so a diagnosis that the screen accepts is
 * one the server accepts, and a mistake the server would refuse is caught before anything is sent.
 */
const E = "11111111-1111-1111-1111-111111111111";
const input = (over: Partial<{ encounterId: string | null; label: string | null; toothKey: string | null; notes: string | null; treatmentPlanReference: string | null }> = {}) =>
  ({ encounterId: E, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, ...over });
const codes = (over: Parameters<typeof input>[0]) => checkDiagnosis(input(over)).problems.map((p) => `${p.field}:${p.code}`);

describe("a valid entry", () => {
  it("comes back exactly as it would be stored", () => {
    const c = checkDiagnosis(input({ label: "  Caries   on the occlusal surface ", toothKey: "16", notes: "Sensitive to cold.\r\nReview in 2 weeks.  ", treatmentPlanReference: "  plan-2026-07  " }));
    expect(c.problems).toEqual([]);
    expect(c.value).toEqual({ encounterId: E, label: "Caries on the occlusal surface", toothKey: "16", notes: "Sensitive to cold.\nReview in 2 weeks.", treatmentPlanReference: "plan-2026-07" });
  });

  it("needs only a label and an encounter", () => expect(checkDiagnosis(input()).value).toEqual({ encounterId: E, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null }));
  it.each(["11", "48", "55", "85"])("accepts the FDI tooth %s, permanent or primary", (t) => expect(codes({ toothKey: t })).toEqual([]));
});

describe("the label", () => {
  it.each([null, "", "   ", " "])("refuses %j as missing", (label) => expect(codes({ label })).toEqual(["label:required"]));
  it("may be 200 characters and not 201, measured after trimming and collapsing spaces", () => {
    expect(codes({ label: "a".repeat(200) })).toEqual([]);
    expect(codes({ label: "a".repeat(201) })).toEqual(["label:too_long"]);
    expect(codes({ label: `  ${"a".repeat(200)}  ` })).toEqual([]);
    expect(codes({ label: `a${" ".repeat(300)}b` })).toEqual([]);
    expect(codes({ label: Array(100).fill("ab").join(" ") })).toEqual(["label:too_long"]);
  });
  it.each(["line one\nline two", "tab\there", "bell\u0007", "null\u0000char"])("refuses %j as not one line", (label) => expect(codes({ label })).toEqual(["label:invalid_characters"]));
});

describe("the tooth", () => {
  it.each(["", " ", "19", "1", "111", " 16", "abc", "56", "90"])("refuses %j, and a blank tooth is not the same as none", (toothKey) => expect(codes({ toothKey })).toEqual(["toothKey:unknown_tooth"]));
});

describe("the notes", () => {
  it("may span lines, may be 1000 characters and not 1001, and blank notes are no notes", () => {
    expect(checkDiagnosis(input({ notes: "a\r\nb\rc" })).value!.notes).toBe("a\nb\nc");
    expect(codes({ notes: "n".repeat(1000) })).toEqual([]);
    expect(codes({ notes: "n".repeat(1001) })).toEqual(["notes:too_long"]);
    expect(checkDiagnosis(input({ notes: "  \n  " })).value!.notes).toBeNull();
  });
  it.each(["bell\u0007", "null\u0000char", "escape\u001b"])("refuses %j as a character that cannot be saved", (notes) => expect(codes({ notes })).toEqual(["notes:invalid_characters"]));
});

describe("the treatment-plan forward reference", () => {
  it("is none when absent", () => expect(checkDiagnosis(input()).value!.treatmentPlanReference).toBeNull());
  it.each([["plan-7", "plan-7"], ["  plan-7  ", "plan-7"], ["Plan   7", "Plan 7"], ["plan 7", "plan 7"], ["PLAN-7", "PLAN-7"]])("trims, collapses spaces and keeps case: %j becomes %j", (given, stored) =>
    expect(checkDiagnosis(input({ treatmentPlanReference: given })).value!.treatmentPlanReference).toBe(stored));
  it.each(["", "   ", "  "])("refuses %j as blank rather than dropping it", (treatmentPlanReference) => expect(codes({ treatmentPlanReference })).toEqual(["treatmentPlanReference:blank"]));
  it("may be 100 characters and not 101", () => {
    expect(codes({ treatmentPlanReference: "p".repeat(100) })).toEqual([]);
    expect(codes({ treatmentPlanReference: "p".repeat(101) })).toEqual(["treatmentPlanReference:too_long"]);
    expect(codes({ treatmentPlanReference: `   ${"p".repeat(100)}   ` })).toEqual([]);
  });
  it.each(["plan\n7", "plan\t7", "plan\r7", "plan\u00007"])("refuses %j as not one line", (treatmentPlanReference) => expect(codes({ treatmentPlanReference })).toEqual(["treatmentPlanReference:invalid_characters"]));
  it("is opaque text whatever it says: identifiers, urls and sentences are stored as normalized, never judged", () => {
    for (const text of ["3f2504e0-4f89-11d3-9a0c-0305e82c3301", "https://example.test/plans/1", "the plan Dr Okafor discussed", "ABC-123"])
      expect(checkDiagnosis(input({ treatmentPlanReference: text })).value!.treatmentPlanReference).toBe(normalizeLine(text));
  });
});

describe("together", () => {
  it("refuses a missing encounter or no entry at all", () => expect(codes({ encounterId: null })).toEqual(["encounterId:required"]));
  it("reports every problem together and hands back nothing partial", () => {
    const c = checkDiagnosis(input({ label: "", toothKey: "19", notes: "bell\u0007", treatmentPlanReference: "   ", encounterId: null }));
    expect(c.problems.map((p) => `${p.field}:${p.code}`)).toEqual(["encounterId:required", "label:required", "toothKey:unknown_tooth", "notes:invalid_characters", "treatmentPlanReference:blank"]);
    expect(c.value).toBeNull();
    expect(c.problems.every((p) => p.message.length > 0)).toBe(true);
  });
  it.each(["  a   b  ", "plan  7", "x", "a b"])("normalizing %j twice gives the same result", (text) => expect(normalizeLine(normalizeLine(text))).toBe(normalizeLine(text)));
  it("a checked entry checked again is accepted and unchanged", () => {
    const first = checkDiagnosis(input({ label: "  Gingivitis ", toothKey: "36", notes: " note \r\n more ", treatmentPlanReference: "  plan   9 " })).value!;
    expect(checkDiagnosis(first).value).toEqual(first);
    expect(normalizeNotes(first.notes!)).toBe(first.notes);
  });
});

describe("the form's draft", () => {
  it("treats an empty optional box as not supplied but a box of spaces as blank, as the server does", () => {
    const draft = { encounterId: E, label: "x", toothKey: "", notes: "", treatmentPlanReference: "" };
    expect(checkDiagnosis(entryFromDraft(draft)).problems).toEqual([]);
    expect(checkDiagnosis(entryFromDraft({ ...draft, treatmentPlanReference: "   " })).problems.map((p) => p.code)).toEqual(["blank"]);
    expect(entryFromDraft({ ...draft, encounterId: "" }).encounterId).toBeNull();
  });
});
