import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagnosisForm } from "./DiagnosisForm";
import type { EncounterSummary } from "../../services/clinicalApi";

// ALV-N022 A2 (OD-06 as amended 2026-11-17): a finalized encounter accepts no new diagnosis, so the diagnosis form does not offer it and says why.

const enc = (id: string, status: EncounterSummary["status"], day: number): EncounterSummary => ({
  id, appointmentId: null, encounterAtUtc: `2030-01-${String(day).padStart(2, "0")}T15:00:00Z`, status, isComplete: true, entryCount: 0, addendumCount: 0,
});

function show(encounters: EncounterSummary[]) {
  render(<DiagnosisForm patientId="p1" numbering="Universal" encounters={encounters} onSaved={vi.fn()} onConflict={vi.fn()} />);
  return screen.getByLabelText("Encounter") as HTMLSelectElement;
}

const MESSAGE = "Finalized encounters cannot receive new diagnoses. Start a new encounter.";

describe("DiagnosisForm encounter choice", () => {
  it("offers only the draft encounter when the patient has a draft and a finalized one, and says why", () => {
    const select = show([enc("fin", "Finalized", 14), enc("draft", "Draft", 15)]);
    const values = Array.from(select.options).map((o) => o.value);
    expect(values).toEqual(["", "draft"]);
    expect(select.value).toBe("draft");                    // the default is the open encounter, never the finalized one
    expect(screen.getByText(MESSAGE)).toBeInTheDocument();
  });

  it("offers nothing to choose when every encounter is finalized", () => {
    const select = show([enc("fin", "Finalized", 14)]);
    expect(Array.from(select.options).map((o) => o.value)).toEqual([""]);
    expect(select.value).toBe("");
    expect(screen.getByText(MESSAGE)).toBeInTheDocument();
  });

  it("shows no message when no encounter is finalized", () => {
    const select = show([enc("d1", "Draft", 14), enc("d2", "Draft", 15)]);
    expect(Array.from(select.options).map((o) => o.value)).toEqual(["", "d1", "d2"]);
    expect(screen.queryByText(MESSAGE)).not.toBeInTheDocument();
  });
});
