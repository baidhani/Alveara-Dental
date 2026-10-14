import type { AllergyOutcome, Prescription } from "../../services/prescriptionApi";

/** The dose as it is read aloud: "500 mg", "1.5 tablet". The number comes from the server's decimal, so no trailing zeros are invented. */
export const doseWords = (p: Pick<Prescription, "doseValue" | "doseUnit">) => `${p.doseValue} ${p.doseUnit}`;

/** What a saved prescription's allergy result means, in words, never as a clearance. */
export function allergyResultWords(p: Pick<Prescription, "allergyOutcome" | "allergyMatches">): string {
  switch (p.allergyOutcome) {
    case "RecordedAllergyMatch": return `The medication matched a recorded allergy (${p.allergyMatches ?? "see the allergy list"}); the prescriber reviewed it.`;
    case "NotEstablished": return "The patient's allergies were not established when this was prescribed, so the allergy check could not say anything; the prescriber confirmed.";
    default: return "No recorded allergy matched this medication's name. That is a name comparison, not a clearance.";
  }
}

export const needsReview = (outcome: AllergyOutcome | undefined) => outcome === "RecordedAllergyMatch" || outcome === "NotEstablished";

/** The label of the prescriber's note box, which says what the note is for. */
export const reviewLabel = (outcome: AllergyOutcome | undefined) =>
  outcome === "RecordedAllergyMatch"
    ? "Why are you prescribing this despite the matching allergy? (your own words, saved with the prescription)"
    : "This patient's allergies are not established. Confirm in your own words that you have asked the patient (saved with the prescription)";
