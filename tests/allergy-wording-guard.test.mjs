// ALV-N022 A3 (F-105, OD-08): the allergy check compares the medication's NAME with the recorded allergies; a "no recorded match" must never read as a verdict. Every surface that
// words that outcome - the server check, the saved-prescription words, the print statement - must say it is "not a clearance" and must not use verdict words. This repository check
// fails if one of those texts loses the caveat or gains a word like "safe" or "cleared". (Wording guard only: no allergy-class, interaction, dose or compatibility logic.)
//
// Run with: node --test tests/allergy-wording-guard.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");

/** The surfaces, and how to find the no-match text in each (a line that words the no-match outcome). */
export const SURFACES = [
  { file: "src/Alveara.Api/Architecture/Prescribing/AllergyCheck.cs", marker: /AllergyOutcomes\.NoRecordedMatch, \[\]/ },
  { file: "src/Alveara.Api/Architecture/Prescribing/PrescriptionLifecycleService.cs", marker: /no recorded allergy matched this medication's name/ },
  { file: "src/alveara-client/src/pages/prescription/prescriptionText.ts", marker: /No recorded allergy matched this medication's name/ },
];

/** Words that would turn a name comparison into a clinical verdict. */
export const VERDICT = /\b(safe|safely|cleared|allergy[- ]free|no allergies|no risk|not allergic|compatible)\b/i;

/** The problems with one no-match text: missing caveat, or a verdict word. */
export function problemsOf(text) {
  const problems = [];
  if (!/not a clearance/i.test(text)) problems.push("does not say it is not a clearance");
  const verdict = text.match(VERDICT);
  if (verdict) problems.push(`uses the verdict word "${verdict[0]}"`);
  return problems;
}

test("every surface that words the 'no recorded match' outcome says it is not a clearance and uses no verdict word", () => {
  for (const { file, marker } of SURFACES) {
    const lines = readFileSync(path.join(root, file), "utf8").split(/\r?\n/).filter((l) => marker.test(l));
    assert.ok(lines.length > 0, `${file}: the no-match text was not found (the guard would be checking nothing)`);
    for (const line of lines) assert.deepEqual(problemsOf(line), [], `${file}: ${line.trim()}`);
  }
});

test("the check catches a missing caveat and a planted verdict word", () => {
  assert.deepEqual(problemsOf("No recorded allergy matched. That is a name comparison, not a clearance."), []);
  assert.deepEqual(problemsOf("No recorded allergy matched; safe to prescribe."), ["does not say it is not a clearance", 'uses the verdict word "safe"']);
  assert.deepEqual(problemsOf("No match - cleared, not a clearance."), ['uses the verdict word "cleared"']);
});
