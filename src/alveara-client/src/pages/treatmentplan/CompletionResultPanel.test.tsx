import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { CompletionResultPanel } from "./CompletionResultPanel";
import type { CompletionResult } from "../../services/completionApi";

// ALV-N022 A3 (D-3, Q-5aj): when no chart finding was linked to a completed procedure, the result says so in neutral words; nothing else changes.

const result = (chartOutcome: string): CompletionResult => ({
  plan: {} as CompletionResult["plan"], setId: "s1", correlationId: "c1", replayed: false, adoptionReason: null,
  procedures: [{ id: "cp1", planItemId: "it1", itemNumber: 1, procedureCode: "L3000", procedureDescription: "Scaling", toothKey: "26", surface: "M", encounterId: "en1", providerProfileId: "pv1",
    performedAtUtc: "2030-02-01T10:00:00Z", planRevisionNumber: 1, chartOutcome, toothFindingId: null, charge: { outcome: "created", entryNumber: 1, amount: 0 }, adopted: false, adoptionReason: null }],
} as unknown as CompletionResult);

const show = (outcome: string) => render(<MemoryRouter><CompletionResultPanel result={result(outcome)} patientId="pt" onDone={vi.fn()} /></MemoryRouter>);

describe("CompletionResultPanel chart wording", () => {
  it("says no chart finding was linked, so the chart was not changed", () => {
    show("NotApplicable");
    expect(screen.getByText(/no chart finding was linked to this procedure, so the chart was not changed/)).toBeInTheDocument();
  });

  it("keeps the other outcomes as they were", () => {
    show("Updated");
    expect(screen.getByText(/chart finding marked Completed/)).toBeInTheDocument();
    expect(screen.queryByText(/no chart finding was linked/)).not.toBeInTheDocument();
  });
});
