import { describe, it, expect } from "vitest";
import { afterEach } from "vitest";
import { addDaysToDate, formatInstant, isUsableZone, practiceNow, practiceToday, setRenderZone, showInstant, toPracticeLocalInput } from "./practiceTime";
import { formatMoment } from "../pages/exports/exportFormat";

// ALV-N021 A3 (briefing 4.1 and 6): the only client code that renders an instant in the practice zone. Every helper uses the zone it is given (never the
// zone of the machine running the test) and returns null without a usable zone.

const CHICAGO = "America/Chicago";
const KIRITIMATI = "Pacific/Kiritimati";   // UTC+14: its calendar day is ahead of every browser's
// 2030-01-15T03:30:00Z is 2030-01-14 21:30 in Chicago (CST, -6) and 2030-01-15 17:30 in Kiritimati
const LATE_EVENING_IN_CHICAGO = "2030-01-15T03:30:00Z";
const plain = (s: string | null) => s?.replace(/ /g, " ") ?? null;   // newer ICU puts a narrow no-break space before AM/PM

describe("toPracticeLocalInput: the exact value of a datetime-local control", () => {
  it("is ASCII yyyy-MM-ddTHH:mm in the given zone, with seconds only when asked", () => {
    expect(toPracticeLocalInput(LATE_EVENING_IN_CHICAGO, CHICAGO)).toBe("2030-01-14T21:30");
    expect(toPracticeLocalInput("2030-01-15T03:30:45Z", CHICAGO, { seconds: true })).toBe("2030-01-14T21:30:45");
    expect(toPracticeLocalInput(LATE_EVENING_IN_CHICAGO, KIRITIMATI)).toBe("2030-01-15T17:30");
    expect(toPracticeLocalInput("2030-07-14T14:30:00Z", CHICAGO)).toBe("2030-07-14T09:30");   // CDT
    expect(toPracticeLocalInput("2030-01-15T06:00:00Z", CHICAGO)).toBe("2030-01-15T00:00");   // midnight is 00, never 24
  });

  it("does not depend on the browser's locale, digits or calendar", () => {
    const value = toPracticeLocalInput(LATE_EVENING_IN_CHICAGO, CHICAGO)!;
    expect(value).toMatch(/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}$/);
    // the display helper with a Thai or Arabic locale would use other digits or a Buddhist year; the input helper never does
    expect(formatInstant(LATE_EVENING_IN_CHICAGO, CHICAGO, "date", "th-TH-u-ca-buddhist")).toContain("2573");
    expect(value.startsWith("2030")).toBe(true);
  });

  it("is null without a usable zone or a valid instant", () => {
    expect(toPracticeLocalInput(LATE_EVENING_IN_CHICAGO, null)).toBeNull();
    expect(toPracticeLocalInput(LATE_EVENING_IN_CHICAGO, "")).toBeNull();
    expect(toPracticeLocalInput(LATE_EVENING_IN_CHICAGO, "Mars/Olympus")).toBeNull();
    expect(toPracticeLocalInput("not a time", CHICAGO)).toBeNull();
    expect(toPracticeLocalInput(null, CHICAGO)).toBeNull();
  });
});

describe("formatInstant: readable display in the practice zone", () => {
  it("shows the practice wall time, not the machine's or UTC", () => {
    expect(plain(formatInstant(LATE_EVENING_IN_CHICAGO, CHICAGO, "date-time", "en-US"))).toBe("1/14/2030, 9:30:00 PM");
    expect(formatInstant(LATE_EVENING_IN_CHICAGO, KIRITIMATI, "date", "en-US")).toBe("1/15/2030");
    expect(plain(formatInstant(LATE_EVENING_IN_CHICAGO, CHICAGO, "time", "en-US"))).toBe("9:30 PM");
  });

  it("is null (shown as unavailable by the screen) without a usable zone; never the browser zone", () => {
    expect(formatInstant(LATE_EVENING_IN_CHICAGO, null)).toBeNull();
    expect(formatInstant(LATE_EVENING_IN_CHICAGO, undefined)).toBeNull();
    expect(formatInstant(LATE_EVENING_IN_CHICAGO, "Not/AZone")).toBeNull();
    expect(formatInstant("garbage", CHICAGO)).toBeNull();
  });
});

describe("practiceToday / practiceNow", () => {
  const now = new Date(LATE_EVENING_IN_CHICAGO);
  it("give the practice day and wall time, which can differ from the UTC day either way", () => {
    expect(practiceToday(CHICAGO, now)).toBe("2030-01-14");     // UTC is already the 15th
    expect(practiceToday(KIRITIMATI, now)).toBe("2030-01-15");
    expect(practiceToday(CHICAGO, new Date("2031-01-01T05:59:00Z"))).toBe("2030-12-31");   // year end
    expect(practiceNow(CHICAGO, now)).toBe("2030-01-14T21:30");
  });

  it("are null without a usable zone", () => {
    expect(practiceToday(null, now)).toBeNull();
    expect(practiceNow("Not/AZone", now)).toBeNull();
  });

  it("date-only arithmetic on a day has no zone in it", () => {
    expect(addDaysToDate("2030-12-31", 1)).toBe("2031-01-01");
    expect(addDaysToDate("2030-03-01", -1)).toBe("2030-02-28");
    expect(addDaysToDate("2030-03-10", 1)).toBe("2030-03-11");   // a DST day is still one day
  });
});

describe("isUsableZone", () => {
  it("accepts IANA ids the browser knows and refuses the rest", () => {
    expect(isUsableZone(CHICAGO)).toBe(true);
    expect(isUsableZone("Central Standard Time")).toBe(false);   // a Windows id is never published by the server, and is not usable here
    expect(isUsableZone(null)).toBe(false);
  });
});

describe("showInstant: what every screen shows for a server instant", () => {
  afterEach(() => setRenderZone(CHICAGO));
  it("uses the session zone set by the AuthProvider", () => {
    setRenderZone(KIRITIMATI);
    expect(showInstant(LATE_EVENING_IN_CHICAGO, "date")).toBe("1/15/2030");
    setRenderZone(CHICAGO);
    expect(showInstant(LATE_EVENING_IN_CHICAGO, "date")).toBe("1/14/2030");
  });
  it("says 'Time unavailable' without a usable zone, never a browser-zone time", () => {
    setRenderZone(null);
    expect(showInstant(LATE_EVENING_IN_CHICAGO)).toBe("Time unavailable");
    setRenderZone("Central Standard Time");
    expect(showInstant(LATE_EVENING_IN_CHICAGO)).toBe("Time unavailable");
  });
});

describe("formatMoment (reports, export history): rebuilt without the browser fallback", () => {
  it("names the practice zone and says 'Time unavailable' without one", () => {
    expect(plain(formatMoment(LATE_EVENING_IN_CHICAGO, CHICAGO))).toMatch(/Jan 14, 2030, 09:30 PM CST|Jan 14, 2030, 9:30 PM CST|Jan 14, 2030 at 09:30 PM CST/);
    expect(formatMoment(LATE_EVENING_IN_CHICAGO, null)).toBe("Time unavailable");
    expect(formatMoment(LATE_EVENING_IN_CHICAGO, "Not/AZone")).toBe("Time unavailable");
  });
});
