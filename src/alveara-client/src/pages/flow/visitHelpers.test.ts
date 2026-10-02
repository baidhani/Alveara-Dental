import { describe, it, expect } from "vitest";
import { ApiError } from "../../services/authApi";
import type { Appointment } from "../../services/schedulingApi";
import { ACTION_LABELS, STATE_LABELS, explainVisitRefusal, needsConfirmation, permissionFor } from "./visitLabels";
import { boardNowMs, durationLabel, elapsedCue } from "./visitTime";

const NOW = Date.parse("2030-01-14T15:00:30.000Z");
const appt = (over: Partial<Appointment>): Appointment => ({
  id: "a", patientId: "p", patientName: "Ann Lee", providerId: "pr", providerName: "Dr. R", operatoryId: "o", operatoryName: "Op 1", appointmentTypeId: "t",
  appointmentTypeName: "Exam", startUtc: "2030-01-14T15:30:00.000Z", endUtc: "2030-01-14T16:30:00.000Z", startLocal: "2030-01-14T09:30", endLocal: "2030-01-14T10:30",
  durationMinutes: 60, status: "Scheduled", createdAtUtc: "2030-01-01T00:00:00Z", ...over,
});

describe("durationLabel", () => {
  it("reads as plain minutes and hours", () => {
    expect(durationLabel(0)).toBe("just now");
    expect(durationLabel(0.9)).toBe("just now");
    expect(durationLabel(-5)).toBe("just now");
    expect(durationLabel(1)).toBe("1 min");
    expect(durationLabel(59.9)).toBe("59 min");
    expect(durationLabel(60)).toBe("1 h");
    expect(durationLabel(65)).toBe("1 h 05 min");
    expect(durationLabel(135)).toBe("2 h 15 min");
    expect(durationLabel(1439)).toBe("23 h 59 min");
    expect(durationLabel(1440)).toBe("1 d");
    expect(durationLabel(1650)).toBe("1 d 3 h");
    expect(durationLabel(28794 * 60 + 50)).toBe("1199 d 18 h"); // a board for a day far ahead stays readable
  });
});

describe("boardNowMs - the server's clock, never the browser's", () => {
  it("is the server time plus only the time elapsed on this machine since the fetch", () => {
    const server = "2030-01-14T15:00:00.000Z";
    expect(boardNowMs(server, 1_000, 1_000)).toBe(Date.parse(server));
    expect(boardNowMs(server, 1_000, 61_000)).toBe(Date.parse(server) + 60_000);
  });

  it("gives the same answer whatever the browser's absolute clock says", () => {
    const server = "2030-01-14T15:00:00.000Z";
    expect(boardNowMs(server, 5_000, 65_000)).toBe(boardNowMs(server, 9_999_999_000, 9_999_999_000 + 60_000));
  });

  it("never runs backwards if the browser clock steps back", () => {
    expect(boardNowMs("2030-01-14T15:00:00.000Z", 10_000, 5_000)).toBe(Date.parse("2030-01-14T15:00:00.000Z"));
  });
});

describe("elapsedCue", () => {
  it("says when a patient who has not arrived is due, or how late they are", () => {
    expect(elapsedCue(appt({}), NOW)).toBe("Starts in 29 min");
    expect(elapsedCue(appt({ flowState: "Confirmed" }), NOW)).toBe("Starts in 29 min");
    expect(elapsedCue(appt({ startUtc: "2030-01-14T15:00:00.000Z" }), NOW)).toBe("Starting now");
    expect(elapsedCue(appt({ startUtc: "2030-01-14T14:45:00.000Z" }), NOW)).toBe("15 min past the start time");
  });

  it("says how long a patient has been in the current step, from the moment the server recorded the move", () => {
    const changed = "2030-01-14T14:48:00.000Z";
    for (const flow of ["CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut"] as const)
      expect(elapsedCue(appt({ flowState: flow, flowChangedAtUtc: changed }), NOW)).toBe("In this step 12 min");
    expect(elapsedCue(appt({ flowState: "Seated", flowChangedAtUtc: "2030-01-14T15:00:10.000Z" }), NOW)).toBe("Just moved to this step");
    expect(elapsedCue(appt({ flowState: "Completed", flowChangedAtUtc: changed }), NOW)).toBe("Completed 12 min ago");
    expect(elapsedCue(appt({ flowState: "Completed", flowChangedAtUtc: "2030-01-14T15:00:10.000Z" }), NOW)).toBe("Completed just now");
  });

  it("has nothing honest to say for cancelled or no-show appointments or a move with no recorded time", () => {
    expect(elapsedCue(appt({ status: "Cancelled" }), NOW)).toBeNull();
    expect(elapsedCue(appt({ status: "NoShow" }), NOW)).toBeNull();
    expect(elapsedCue(appt({ flowState: "Seated" }), NOW)).toBeNull();
  });
});

describe("labels and permissions", () => {
  it("names every state and the button for every move into it (Scheduled has no button: nothing moves back to it)", () => {
    for (const s of ["Scheduled", "Confirmed", "CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut", "Completed"] as const) expect(STATE_LABELS[s]).toBeTruthy();
    expect(ACTION_LABELS.Scheduled).toBeUndefined();
    expect(Object.values(ACTION_LABELS)).toHaveLength(7);
  });

  it("assigns confirm, check in and check out to the front office and every chairside step to the chairside team (mirrors the server)", () => {
    expect(["Confirmed", "CheckedIn", "CheckedOut"].map((t) => permissionFor(t as never))).toEqual(["UpdateVisitFlow", "UpdateVisitFlow", "UpdateVisitFlow"]);
    expect(["Ready", "Seated", "InTreatment", "Completed"].map((t) => permissionFor(t as never))).toEqual(Array(4).fill("UpdateChairsideFlow"));
  });

  it("asks for confirmation only before completing a visit, which is final", () => {
    expect(needsConfirmation("Completed")).toBe(true);
    for (const t of ["Confirmed", "CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut"] as const) expect(needsConfirmation(t)).toBe(false);
  });
});

describe("explainVisitRefusal", () => {
  const refusal = (status: number, code: string, message = "m") => new ApiError(status, code, message);

  it("names the room that is occupied and says nothing was changed", () => {
    const text = explainVisitRefusal(refusal(409, "operatory_occupied"), { operatory: "Op 2" });
    expect(text).toContain("Op 2 already has a patient who is seated or in treatment");
    expect(text).toContain("Nothing was changed");
  });

  it("passes the server's own words through for rule refusals and says nothing changed", () => {
    for (const code of ["invalid_flow_transition", "appointment_in_progress", "appointment_not_scheduled", "visit_completed"])
      expect(explainVisitRefusal(refusal(409, code, "A visit cannot go from ready to completed."), {})).toBe("A visit cannot go from ready to completed. Nothing was changed.");
  });

  it("explains a missing record, a busy schedule, a forbidden role and a dropped connection", () => {
    expect(explainVisitRefusal(refusal(404, "appointment_not_found", "That appointment was not found."), {})).toContain("Refresh the board");
    expect(explainVisitRefusal(refusal(503, "schedule_busy"), {})).toContain("busy");
    expect(explainVisitRefusal(refusal(403, "permission_denied"), {})).toContain("Your role is not allowed");
    expect(explainVisitRefusal(new TypeError("Failed to fetch"), {})).toContain("could not confirm whether that was saved");
  });
});
