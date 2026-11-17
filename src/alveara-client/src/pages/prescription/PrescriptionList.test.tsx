import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { PrescriptionList } from "./PrescriptionList";
import type { Prescription } from "../../services/prescriptionApi";

// ALV-N022 A3 (F-106): the Prescriptions tab shows each version's status, instructions, quantity and refills, and which version replaced which; a prescription saved before the
// workflow says those were not recorded (nothing invented).

const rx = (over: Partial<Prescription>): Prescription => ({
  id: "p1", patientId: "pt", medication: "Amoxicillin", doseValue: 500, doseUnit: "mg", frequency: "Every 8 hours", allergyOutcome: "NoRecordedMatch", allergyListState: "Established",
  allergyMatches: null, reviewNote: null, prescribedByName: "Dr. Okafor", prescribedAtUtc: "2030-01-10T15:00:00Z", ...over,
});

describe("PrescriptionList (F-106)", () => {
  it("shows the two versions differently: the amendment active with its details, the original superseded", () => {
    render(<PrescriptionList prescriptions={[
      rx({ id: "p2", status: "Active", hasWorkflowDetails: true, instructions: "Take one capsule with food.", quantityValue: 21, quantityUnit: "capsules", refills: 0, supersedesPrescriptionId: "p1" }),
      rx({ id: "p1", status: "Superseded", hasWorkflowDetails: true, instructions: "Take one capsule.", quantityValue: 15, quantityUnit: "capsules", refills: 0, supersededByPrescriptionId: "p2" }),
    ]} />);
    const [newer, older] = within(screen.getByRole("list", { name: "Prescriptions" })).getAllByRole("listitem");
    expect(newer).toHaveTextContent("Status: Active");
    expect(newer).toHaveTextContent("Take one capsule with food.");
    expect(newer).toHaveTextContent("Quantity 21 capsules · Refills 0");
    expect(newer).toHaveTextContent("This is an amendment; it replaces an earlier version");
    expect(older).toHaveTextContent("Superseded: it was replaced by an amendment.");
    expect(older).toHaveTextContent("Replaced by a later amendment");
    expect(older).toHaveTextContent("Quantity 15 capsules");
  });

  it("says a prescription saved before the workflow has no recorded details, and invents no status", () => {
    render(<PrescriptionList prescriptions={[rx({})]} />);
    const item = screen.getByRole("listitem");
    expect(item).toHaveTextContent("Saved before instructions, quantity and refills were recorded.");
    expect(item).not.toHaveTextContent("Status:");
    expect(item).not.toHaveTextContent("Quantity");
  });
});
