import { describe, it, expect } from "vitest";
import { bookingDurationHint, durationAfterTypeChange } from "./bookingDuration";

// ALV-N021 A2 (OD-21, F-044b): a booking's duration is filled from the appointment type, stays editable, and a typed length is never replaced.
describe("the duration a booking form shows", () => {
  it("fills the type's length into an empty or untouched field, and follows a type change", () => {
    expect(durationAfterTypeChange("", false, 60)).toBe("60");
    expect(durationAfterTypeChange("60", false, 30)).toBe("30");     // filled by the previous type, not typed: follows the new type
  });

  it("never replaces a length the person typed; a cleared field follows the type again", () => {
    expect(durationAfterTypeChange("45", true, 30)).toBe("45");
    expect(durationAfterTypeChange("  ", true, 30)).toBe("30");
  });

  it("is empty when no type is chosen", () => {
    expect(durationAfterTypeChange("60", false, undefined)).toBe("");
    expect(bookingDurationHint(undefined)).toBe("Choose an appointment type to fill in its length.");
    expect(bookingDurationHint(60)).toBe("Filled in from the appointment type (60 minutes). You can change it.");
  });
});
