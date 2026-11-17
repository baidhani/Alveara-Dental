import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DiagnosisHistory } from "./diagnosis/DiagnosisHistory";
import { ClinicalHistorySection } from "./treatmentplan/ClinicalHistorySection";
import { PerioGrid } from "./perio/PerioGrid";
import { PerioChartLinks } from "./perio/PerioChartExtras";
import { DiagnosisItem } from "./diagnosis/DiagnosisItem";
import { listDiagnoses, type Diagnosis } from "../services/diagnosisApi";
import { linkPerioChart, type PerioChart } from "../services/perioApi";

// ALV-N022 A3 display rows: F-099 (one numbering system per screen; the stored FDI key is never shown raw) and F-097 (each perio box has a visible label).

vi.mock("../services/diagnosisApi", async (orig) => ({
  ...(await orig<typeof import("../services/diagnosisApi")>()),
  listDiagnoses: vi.fn(async () => ({ diagnoses: [
    { id: "dx-own", label: "Generalized periodontitis", status: "Active" },
    { id: "dx-gone", label: "Withdrawn gingivitis", status: "Withdrawn" },
  ] })),
  getDiagnosisHistory: vi.fn(async () => ({ id: "d1", versions: [{ versionNumber: 1, changeType: "Recorded", label: "Caries", toothKey: "16", notes: null, treatmentPlanReference: null,
    status: "Active", actorName: "Dr. Okafor", occurredAtUtc: "2030-01-10T15:00:00Z", reason: null, codingSystem: null, code: null, source: "Manual", sourceNote: null, regionKey: null }] })),
}));
vi.mock("../services/perioApi", async (orig) => ({
  ...(await orig<typeof import("../services/perioApi")>()),
  linkPerioChart: vi.fn(async () => ({ id: "c1", patientId: "pat-1", recordedAtUtc: "2030-02-01T10:00:00Z", recordedByName: "Dr. Okafor", readingCount: 1, readings: [],
    links: [{ linkType: "Diagnosis", reference: "dx-own", diagnosisId: "dx-own", diagnosisLabel: "Generalized periodontitis", linkedByName: "Dr. Okafor", linkedAtUtc: "2030-02-01T10:05:00Z" }] })),
}));
vi.mock("../services/treatmentPlansApi", async (orig) => ({
  ...(await orig<typeof import("../services/treatmentPlansApi")>()),
  getClinicalHistory: vi.fn(async () => ({ entries: [{ id: "h1", procedureCode: "L3000", procedureDescription: "Scaling", toothKey: "26", surface: "M", performedAtUtc: "2030-02-01T10:00:00Z", planTitle: "Plan",
    planId: "p1", completedProcedureId: null, providerName: null, encounterId: null }] })),
}));

describe("F-099 one numbering system per screen", () => {
  it("the diagnosis history shows the tooth in the screen's numbering, not as a raw FDI key", async () => {
    render(<DiagnosisHistory id="d1" numbering="Universal" />);
    expect(await screen.findByText(/Caries \(tooth 3\)/)).toBeInTheDocument();
    expect(screen.queryByText(/FDI/)).not.toBeInTheDocument();
  });

  it("the clinical history uses the numbering the plan rows use", async () => {
    render(<ClinicalHistorySection patientId="p" refreshKey={0} />);
    expect(await screen.findByText(/tooth 14/)).toBeInTheDocument();
    expect(screen.queryByText(/tooth 26/)).not.toBeInTheDocument();
  });
});

describe("F-097 visible labels on the perio boxes", () => {
  it("shows Depth and Rec. beside every site's two boxes", () => {
    render(<PerioGrid draft={{}} problems={new Map()} numbering="Universal" disabled={false} onChange={vi.fn()} />);
    const depth = screen.getAllByText("Depth");
    const rec = screen.getAllByText("Rec.");
    expect(depth.length).toBe(rec.length);
    expect(depth.length).toBe(32 * 6);                    // 32 teeth, six sites each
  });
});

describe("Q-5aa and F-098: typed perio-diagnosis links on both sides", () => {
  it("the diagnosis card lists the perio charts linked to it", () => {
    const d = { id: "dx-own", patientId: "pat-1", encounterId: "e1", encounterAtUtc: "2030-01-10T15:00:00Z", label: "Generalized periodontitis", toothKey: null, notes: null, treatmentPlanReference: null,
      treatmentPlanReferenceState: null, status: "Active", recordedByName: "Dr. Okafor", recordedAtUtc: "2030-01-10T15:00:00Z", updatedByName: null, updatedAtUtc: null, rowVersion: "r",
      linkedFromPerioCharts: [{ examId: "c1", recordedAtUtc: "2030-02-01T10:00:00Z", linkedByName: "Dr. Li", linkedAtUtc: "2030-02-01T10:05:00Z" }] } as unknown as Diagnosis;
    render(<ul><DiagnosisItem diagnosis={d} encounters={[]} numbering="Universal" canWrite={false} onChanged={vi.fn()} onConflict={vi.fn()} /></ul>);
    expect(screen.getByText(/Periodontal charts linked to this diagnosis: chart of .* \(linked by Dr\. Li\)\./)).toBeInTheDocument();
  });

  it("the perio picker offers only this patient's current diagnoses, sends the chosen id, and labels an old free-text link as legacy", async () => {
    const user = userEvent.setup();
    const chart = { id: "c1", patientId: "pat-1", recordedAtUtc: "2030-02-01T10:00:00Z", recordedByName: "Dr. Okafor", readingCount: 1, readings: [],
      links: [{ linkType: "Diagnosis", reference: "dx-typed-long-ago", linkedByName: "Dr. Okafor", linkedAtUtc: "2026-09-02T10:00:00Z" }] } as unknown as PerioChart;
    render(<PerioChartLinks chart={chart} canWrite onChanged={vi.fn()} />);
    expect(screen.getByRole("list", { name: "Linked records" })).toHaveTextContent("Diagnosis: dx-typed-long-ago (free-text reference, legacy)");
    const form = screen.getByRole("form", { name: "Link this chart" });
    const picker = await within(form).findByRole("combobox", { name: "Diagnosis" });
    await within(picker).findByRole("option", { name: "Generalized periodontitis" });
    expect(listDiagnoses).toHaveBeenCalledWith("pat-1", {}, expect.anything());              // this chart's patient only
    expect(within(picker).getAllByRole("option").map((o) => o.textContent)).toEqual(["Choose a diagnosis", "Generalized periodontitis"]);   // the withdrawn one is not offered
    expect(within(form).queryByLabelText("Reference")).not.toBeInTheDocument();             // never typed
    await user.selectOptions(picker, "dx-own");
    await user.click(within(form).getByRole("button", { name: "Add link" }));
    expect(linkPerioChart).toHaveBeenCalledWith("c1", "Diagnosis", "", "dx-own");
  });
});
