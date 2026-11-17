import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { FinalizeReview } from "./FinalizeReview";
import type { EncounterDetail, EncounterSection } from "../../services/clinicalApi";
import type { RecordItem } from "../../services/clinicalRecordApi";

// ALV-N022 A1: the finalize review counts a chart-backed section's items on the chart (its encounter entries are empty for new encounters); dental history keeps its own count.

const item = (id: string, name: string): RecordItem => ({
  id, kind: "Allergy", name, detail: null, reaction: null, severity: null, dose: null, frequency: null, status: "Active",
  createdAtUtc: "2026-11-17T10:00:00Z", createdByName: null, updatedAtUtc: null, updatedByName: null, rowVersion: "AAAA",
});
const entry = (name: string) => ({ id: name, kind: "DentalHistory", name, detail: null, reaction: null, severity: null, dose: null, frequency: null, createdAtUtc: "2026-11-17T10:00:00Z", createdByUserId: null, updatedAtUtc: null });

const section = (over: Partial<EncounterSection>): EncounterSection => ({ kind: "Allergy", status: "Recorded", entries: [], reviewedAtUtc: null, reviewedByUserId: null, ...over });

function show(sections: EncounterSection[]) {
  const encounter = { sections, notes: [], missingNotes: [], isSigned: false } as unknown as EncounterDetail;
  render(<FinalizeReview encounter={encounter} busy={false} refusal={null} onConfirm={vi.fn()} onCancel={vi.fn()} />);
}

describe("FinalizeReview counts", () => {
  it("counts a chart-backed section's items on the chart", () => {
    show([section({ source: "Chart", chartItems: [item("a", "Penicillin"), item("b", "Latex")] })]);
    expect(screen.getByText("Recorded (2 on the chart)")).toBeInTheDocument();
  });

  it("includes entries the encounter recorded before the chart became the one record", () => {
    show([section({ kind: "Medication", source: "Chart", chartItems: [item("a", "Lisinopril")], entries: [entry("Aspirin") as never] })]);
    expect(screen.getByText("Recorded (2 on the chart)")).toBeInTheDocument();
  });

  it("shows no count for a confirmed chart section with nothing listed, and dental history keeps its own count", () => {
    show([
      section({ kind: "Medication", status: "NoneReported", source: "Chart", chartItems: [] }),
      section({ kind: "DentalHistory", source: "Encounter", entries: [entry("Crown") as never, entry("Bridge") as never] }),
    ]);
    expect(screen.getByText("Reviewed - none reported")).toBeInTheDocument();
    expect(screen.getByText("Recorded (2)")).toBeInTheDocument();
  });
});
