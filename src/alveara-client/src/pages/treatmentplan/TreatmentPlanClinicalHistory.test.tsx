import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { P, item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { ClinicalHistoryEntry } from "../../services/treatmentPlansApi";
import type { CompletionReadiness } from "../../services/completionApi";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * STORY-008, kept by ALV-008-C02: the clinical history beside the plans. These tests used to live with the retired "Complete accepted procedures" button (TreatmentPlanCompleteAccepted.test.tsx, removed); the history
 * itself is unchanged, so its tests move here unchanged except the one that needed a completion to refresh it, which now uses an adoption (see docs/testing/ALV-008-C02_PARENT_REGRESSION.md).
 */
const open = async (canWrite = true) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};
const accepted = (id: string, code: string, over = {}) => item({ id, procedureCode: code, procedureDescription: `${code} wording`, acceptance: "Accepted", ...over });
const twoAccepted = () => plan({ items: [accepted("it1", "PERIO-1"), accepted("it2", "FILL-1", { toothKey: "16", surface: "O" }), item({ id: "it3", procedureCode: "POL-1", acceptance: "Declined" })], acceptanceStatus: "Pending" });
const entry = (over: Partial<ClinicalHistoryEntry> = {}): ClinicalHistoryEntry => ({
  planId: "pl1", planTitle: "Gum health plan", itemId: "it1", itemNumber: 1, procedureCodeSystem: "Local", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", toothKey: null, surface: null,
  completedByName: "Dr. Okafor", completedAtUtc: "2030-02-01T10:00:00Z", planRevisionAtCompletion: 1, ...over,
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the clinical history", () => {
  it("lists each completed procedure with who and when, newest first as served, to a reader as well", async () => {
    const api = stubApi({ plans: [twoAccepted()] });
    api.history = [entry(), entry({ itemId: "it2", itemNumber: 2, procedureCode: "FILL-1", procedureDescription: "Filling", toothKey: "16", surface: "O", completedByName: "Dr. Li" })];
    await open(false);
    const list = await screen.findByRole("list", { name: "Completed procedures" });
    const rows = within(list).getAllByRole("listitem");
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("PERIO-1 Periodontal maintenance");
    expect(rows[0]).toHaveTextContent("Completed by Dr. Okafor");
    expect(rows[0]).toHaveTextContent("Gum health plan, revision 1");
    expect(rows[1]).toHaveTextContent("tooth 16 · surface O");
    expect(rows[1]).toHaveTextContent("Completed by Dr. Li");
  });

  it("says none has been recorded, never that no care was given", async () => {
    stubApi();
    await open(false);
    expect(await screen.findByText(/No procedure has been completed for this patient yet. That means none has been recorded/)).toBeInTheDocument();
  });

  it("a failed load says it could not load rather than showing an empty list", async () => {
    stubApi({ respond: { "/clinical-history": () => json({ error: "server_error", message: "boom" }, 500) } });
    await open(false);
    expect(await screen.findByText("Could not load the clinical history")).toBeInTheDocument();
    expect(screen.queryByText(/No procedure has been completed/)).not.toBeInTheDocument();
  });

  it("refreshes after an adoption so the new care shows straight away", async () => {
    const user = userEvent.setup();
    const completed = plan({ items: [accepted("it1", "PERIO-1", { completion: "Completed" })], acceptanceStatus: "Accepted" });
    const readiness: CompletionReadiness = {
      planId: "pl1", rowVersion: "R1", providers: [{ id: "pv1", name: "Dr. Okafor", specialty: "General dentistry" }], encounters: [{ id: "en1", atUtc: "2030-01-01T10:00:00Z", status: "Draft" }],
      items: [{ itemId: "it1", itemNumber: 1, procedureCode: "PERIO-1", procedureDescription: "PERIO-1 wording", toothKey: null, surface: null, ready: false, notReadyReason: "already_completed", chartEffect: "NotApplicable", chartMatches: 0, billable: true, fee: 120, chargeExists: false, legacyCompleted: true }],
    };
    const api = stubApi({ plans: [completed], respond: { "/completion-readiness": () => json(readiness) } });
    await open();
    expect(await screen.findByText(/No procedure has been completed for this patient yet/)).toBeInTheDocument();
    api.respond["/adopt-completion"] = () => {
      api.history = [entry()];
      return json({ plan: completed, setId: "s1", correlationId: "c1", replayed: false, procedures: [], adoptionReason: "r" });
    };
    await user.click(screen.getByRole("button", { name: "Adopt earlier completions…" }));
    const f = within(await screen.findByRole("form", { name: "Adopt an earlier completion" }));
    await user.selectOptions(f.getByLabelText("Provider"), "pv1");
    await user.selectOptions(f.getByLabelText("Encounter"), "en1");
    await user.type(f.getByLabelText("Why is this being recorded now?"), "Completed before records existed");
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    const list = await screen.findByRole("list", { name: "Completed procedures" });
    expect(within(list).getByText("PERIO-1")).toBeInTheDocument();
    expect(api.calls.filter((c) => c.url.includes(`/api/patients/${P}/clinical-history`)).length).toBeGreaterThanOrEqual(2);
  });

  it("has no accessibility violations with entries listed", async () => {
    const api = stubApi();
    api.history = [entry()];
    const { container } = render(<TreatmentPlansPanel patient={patient} />);
    await screen.findByRole("list", { name: "Completed procedures" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
