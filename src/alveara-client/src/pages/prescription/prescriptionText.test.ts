import { describe, it, expect } from "vitest";
import { frequencyLabel } from "./prescriptionText";

/** ALV-N026 (Q-5am): only the label changes; every other stored frequency reads as stored. */
describe("frequencyLabel", () => {
  it("reads a single dose as such and leaves the others as stored", () => {
    expect(frequencyLabel("Once")).toBe("Once only (single dose)");
    expect(frequencyLabel("Once daily")).toBe("Once daily");
    expect(frequencyLabel("As needed")).toBe("As needed");
  });
});
