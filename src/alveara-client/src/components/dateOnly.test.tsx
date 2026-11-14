import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { formatDateOnly } from "./dateOnly";
import { PrescriptionPrint } from "../pages/prescription/workflow/PrintPreview";
import type { PrintContent } from "../services/prescriptionWorkflowApi";

/**
 * ALV-N021 A1 (F-107): a date-only value is the same day in every zone. Node honours a runtime change of TZ, so each case runs in a zone behind UTC (Chicago, Honolulu), at UTC and far ahead
 * (Kiritimati, +14). Negative control: the old way, `new Date("1985-03-14")`, really does print the 13th in Chicago, so the test would catch the defect coming back.
 */
const zones = ["America/Chicago", "Pacific/Honolulu", "UTC", "Pacific/Kiritimati"];
let saved: string | undefined;
beforeEach(() => { saved = process.env.TZ; });
afterEach(() => { if (saved === undefined) delete process.env.TZ; else process.env.TZ = saved; });

const content: PrintContent = {
  prescriptionId: "rx-1", status: "Active", practice: null, patientName: "Ann Lee", patientDateOfBirth: "1985-03-14", prescriberName: "Dr. Dana", medication: "Amoxicillin 500 mg capsule",
  dose: "500 mg", frequency: "Three times daily", instructions: "Take with food.", quantity: "21", refills: 0, prescribedAtUtc: "2026-11-03T15:00:00Z", allergyStatement: "No known allergies.",
  limitationStatement: "Interactions.", supersedesPrescriptionId: null, supersededByPrescriptionId: null, contentHash: "abcdef0123456789",
};

describe("formatDateOnly", () => {
  it.each(zones)("keeps the calendar day in %s", (zone) => {
    process.env.TZ = zone;
    expect(formatDateOnly("1985-03-14", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: undefined })).toBe(new Date(1985, 2, 14).toLocaleDateString(undefined, { year: "numeric", month: "2-digit", day: "2-digit" }));
    expect(formatDateOnly("2030-12-31")).toBe(new Date(2030, 11, 31).toLocaleDateString());
    expect(formatDateOnly("2024-02-29")).toBe(new Date(2024, 1, 29).toLocaleDateString());
  });

  it("negative control: parsing the same text as an instant moves the day in a zone behind UTC", () => {
    process.env.TZ = "America/Chicago";
    expect(new Date("1985-03-14").getDate()).toBe(13);
    expect(formatDateOnly("1985-03-14", { day: "numeric" })).toBe("14");
  });

  it("shows what is not a real date as it came, never as Invalid Date", () => {
    expect(formatDateOnly("")).toBe("");
    expect(formatDateOnly("not a date")).toBe("not a date");
    expect(formatDateOnly("2023-02-29")).toBe("2023-02-29");
  });
});

describe("prescription print (F-107)", () => {
  it.each(zones)("prints the date of birth the header shows, in %s", (zone) => {
    process.env.TZ = zone;
    render(<PrescriptionPrint content={content} />);
    expect(screen.getByText(/For: Ann Lee/)).toHaveTextContent(`(born ${new Date(1985, 2, 14).toLocaleDateString()})`);
  });
});
