import { describe, it, expect } from "vitest";
import type { Appointment } from "../../services/schedulingApi";
import {
  addDays, appointmentsOnDay, axisRange, dayLabel, dayOfWeek, flowWord, hhmm, minutesOf, placeColumn, practiceNow, slotMinuteAt, viewDays, weekStart,
} from "./calendarLayout";

const appt = (id: string, start: string, end: string, over: Partial<Appointment> = {}): Appointment => ({
  id, patientId: "p", patientName: "Pat", providerId: "prov", providerName: "Dr. R", operatoryId: "op", operatoryName: "Op 1", appointmentTypeId: "t",
  appointmentTypeName: "Exam", startUtc: "", endUtc: "", startLocal: start, endLocal: end, durationMinutes: 60, status: "Scheduled", createdAtUtc: "", ...over,
});

describe("calendar dates are practice-local calendar arithmetic", () => {
  it("adds days across month, year and leap-day boundaries without any time zone effects", () => {
    expect(addDays("2030-01-31", 1)).toBe("2030-02-01");
    expect(addDays("2030-12-31", 1)).toBe("2031-01-01");
    expect(addDays("2028-02-28", 1)).toBe("2028-02-29");
    expect(addDays("2030-03-10", 1)).toBe("2030-03-11"); // the daylight-saving change day in Chicago is still a normal day here
    expect(addDays("2030-01-14", -14)).toBe("2029-12-31");
  });

  it("knows the weekday and labels a day with it", () => {
    expect(dayOfWeek("2030-01-14")).toBe(1);
    expect(dayLabel("2030-01-14")).toBe("Monday 2030-01-14");
    expect(dayLabel("2030-01-20")).toBe("Sunday 2030-01-20");
  });

  it("a week is Monday to Sunday whichever day inside it is chosen", () => {
    const week = ["2030-01-14", "2030-01-15", "2030-01-16", "2030-01-17", "2030-01-18", "2030-01-19", "2030-01-20"];
    for (const day of week) expect(viewDays(day, "week")).toEqual(week);
    expect(weekStart("2030-01-20")).toBe("2030-01-14");
    expect(viewDays("2030-01-16", "day")).toEqual(["2030-01-16"]);
  });

  it("reads times from local strings and formats minutes back", () => {
    expect(minutesOf("2030-01-14T09:30")).toBe(570);
    expect(minutesOf("07:05")).toBe(425);
    expect(hhmm(570)).toBe("09:30");
    expect(hhmm(0)).toBe("00:00");
  });

  it("reports the practice's now from its own zone id, not the browser's", () => {
    const instant = new Date("2030-01-14T15:30:00Z");
    expect(practiceNow("America/Chicago", instant)).toBe("2030-01-14T09:30");
    expect(practiceNow("UTC", instant)).toBe("2030-01-14T15:30");
    expect(practiceNow("Asia/Tokyo", instant)).toBe("2030-01-15T00:30");
    expect(practiceNow("America/Chicago", new Date("2030-07-14T15:30:00Z"))).toBe("2030-07-14T10:30"); // daylight time
  });
});

describe("the grid's hours", () => {
  it("default to 07:00-19:00 and widen to whole hours for anything outside", () => {
    expect(axisRange([], [])).toEqual({ startMinute: 420, endMinute: 1140 });
    expect(axisRange([appt("a", "2030-01-14T06:30", "2030-01-14T07:30")], [])).toEqual({ startMinute: 360, endMinute: 1140 });
    expect(axisRange([appt("a", "2030-01-14T18:00", "2030-01-14T20:15")], [])).toEqual({ startMinute: 420, endMinute: 1260 });
    expect(axisRange([], [{ startLocal: "06:00", endLocal: "21:00" }])).toEqual({ startMinute: 360, endMinute: 1260 });
  });
});

describe("placing appointments in a column", () => {
  const axis = 420;

  it("positions a block by its start and sizes it by its duration (one pixel per minute)", () => {
    const [p] = placeColumn([appt("a", "2030-01-14T09:00", "2030-01-14T10:30")], "2030-01-14", axis);
    expect(p.topPx).toBe(120);
    expect(p.heightPx).toBe(90);
    expect([p.lane, p.lanes]).toEqual([0, 1]);
  });

  it("gives a very short appointment a block tall enough to read and click", () => {
    const [p] = placeColumn([appt("a", "2030-01-14T09:00", "2030-01-14T09:05")], "2030-01-14", axis);
    expect(p.heightPx).toBeGreaterThanOrEqual(22);
  });

  it("puts overlapping appointments side by side and lets non-overlapping ones share a full-width lane", () => {
    const placed = placeColumn([
      appt("a", "2030-01-14T09:00", "2030-01-14T10:00"),
      appt("b", "2030-01-14T09:30", "2030-01-14T10:30"),
      appt("c", "2030-01-14T10:00", "2030-01-14T11:00"),   // starts as a ends: lane 0 is free again, but b is still running
      appt("d", "2030-01-14T13:00", "2030-01-14T14:00"),   // alone, later
    ], "2030-01-14", axis);
    const by = Object.fromEntries(placed.map((p) => [p.appointment.id, p]));
    expect([by.a.lane, by.b.lane, by.c.lane]).toEqual([0, 1, 0]);
    expect([by.a.lanes, by.b.lanes, by.c.lanes]).toEqual([2, 2, 2]);
    expect([by.d.lane, by.d.lanes]).toEqual([0, 1]);
  });

  it("back-to-back appointments are not an overlap", () => {
    const placed = placeColumn([appt("a", "2030-01-14T09:00", "2030-01-14T10:00"), appt("b", "2030-01-14T10:00", "2030-01-14T11:00")], "2030-01-14", axis);
    expect(placed.map((p) => [p.lane, p.lanes])).toEqual([[0, 1], [0, 1]]);
  });

  it("is deterministic whatever order the appointments arrive in", () => {
    const list = [appt("b", "2030-01-14T09:30", "2030-01-14T10:30"), appt("a", "2030-01-14T09:00", "2030-01-14T10:00")];
    const forward = placeColumn(list, "2030-01-14", axis).map((p) => [p.appointment.id, p.lane]);
    const backward = placeColumn([...list].reverse(), "2030-01-14", axis).map((p) => [p.appointment.id, p.lane]);
    expect(forward).toEqual(backward);
  });

  it("clips an appointment that runs past midnight to the day being drawn", () => {
    const [p] = placeColumn([appt("a", "2030-01-14T23:00", "2030-01-15T00:30")], "2030-01-14", 0);
    expect(p.topPx).toBe(23 * 60);
    expect(p.heightPx).toBe(60);
  });

  it("selects the appointments that fall on a day", () => {
    const list = [appt("a", "2030-01-14T09:00", "2030-01-14T10:00"), appt("b", "2030-01-15T09:00", "2030-01-15T10:00"), appt("c", "2030-01-14T23:00", "2030-01-15T00:00")];
    expect(appointmentsOnDay(list, "2030-01-14").map((a) => a.id)).toEqual(["a", "c"]);
    expect(appointmentsOnDay(list, "2030-01-15").map((a) => a.id)).toEqual(["b"]);
  });

  it("turns a click down a column into a 15-minute slot", () => {
    expect(slotMinuteAt(0, 420)).toBe(420);
    expect(slotMinuteAt(14, 420)).toBe(420);
    expect(slotMinuteAt(15, 420)).toBe(435);
    expect(slotMinuteAt(125, 420)).toBe(540);
  });
});

describe("flowWord (STORY-011)", () => {
  it("says nothing for a patient who has not arrived and names each later state in words", () => {
    expect(flowWord({ status: "Scheduled" })).toBeNull();
    expect(flowWord({ status: "Scheduled", flowState: "Scheduled" })).toBeNull();
    expect(flowWord({ status: "Scheduled", flowState: "CheckedIn" })).toBe("Checked in");
    expect(flowWord({ status: "Scheduled", flowState: "InTreatment" })).toBe("In treatment");
    expect(flowWord({ status: "Scheduled", flowState: "Completed" })).toBe("Completed");
  });

  it("never reports a flow for a cancelled or no-show appointment, whatever the data says", () => {
    expect(flowWord({ status: "Cancelled", flowState: "CheckedIn" })).toBeNull();
    expect(flowWord({ status: "NoShow", flowState: "Completed" })).toBeNull();
  });
});
