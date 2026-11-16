import { describe, it, expect } from "vitest";
import { emptyVitals, vitalsPayload } from "./clinicalNotesApi";

// ALV-N021 A3 (inventory row 69): the measured time is sent as the plain practice-local text the control holds; the browser never turns it into an instant
describe("vitalsPayload", () => {
  it("sends measuredAtLocal exactly as typed and no measuredAtUtc", () => {
    const body = vitalsPayload({ ...emptyVitals(), measuredAt: "2030-01-14T09:30", pulseBpm: "70" }, "v1");
    expect(body).toEqual({ rowVersion: "v1", pulseBpm: 70, measuredAtLocal: "2030-01-14T09:30" });
  });
  it("leaves the time out when it is blank (the server then uses now)", () => {
    expect("measuredAtLocal" in vitalsPayload({ ...emptyVitals(), pulseBpm: "70" }, "v1")).toBe(false);
  });
});
