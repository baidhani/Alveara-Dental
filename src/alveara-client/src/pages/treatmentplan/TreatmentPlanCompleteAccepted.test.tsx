import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { P, item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import type { ClinicalHistoryEntry } from "../../services/treatmentPlansApi";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * STORY-008: completing a plan's accepted procedures at once, and the clinical history beside the plans. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI:
 * the button appears only when something accepted can be completed, the confirmation lists exactly what will be completed, nothing is sent until it is confirmed, a refusal or a stale copy says nothing was
 * completed, the history refreshes after a completion, and a failed history load says so rather than showing an empty list.
 */
const posts = (api: Api) => api.calls.filter((c) => c.method === "POST");
const open = async (canWrite = true) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};
const accepted = (id: string, code: string, over = {}) => item({ id, procedureCode: code, procedureDescription: `${code} wording`, acceptance: "Accepted", ...over });
const twoAccepted = () => plan({ items: [accepted("it1", "PERIO-1"), accepted("it2", "FILL-1", { toothKey: "16", surface: "O" }), item({ id: "it3", procedureCode: "POL-1", acceptance: "Declined" })], acceptanceStatus: "Partial" });
const entry = (over: Partial<ClinicalHistoryEntry> = {}): ClinicalHistoryEntry => ({
  planId: "pl1", planTitle: "Gum health plan", itemId: "it1", itemNumber: 1, procedureCodeSystem: "Local", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", toothKey: null, surface: null,
  completedByName: "Dr. Okafor", completedAtUtc: "2030-02-01T10:00:00Z", planRevisionAtCompletion: 1, ...over,
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the complete-accepted button", () => {
  it("is offered only when something accepted can still be completed, and never to a reader", async () => {
    stubApi({ plans: [plan()] });
    await open(true);
    expect(screen.queryByRole("button", { name: "Complete accepted procedures" })).not.toBeInTheDocument();
    vi.unstubAllGlobals();
    stubApi({ plans: [plan({ items: [accepted("it1", "PERIO-1", { completion: "Completed" })] })] });
    await open(true);
    expect(screen.queryByRole("button", { name: "Complete accepted procedures" })).not.toBeInTheDocument();
  });

  it("is not drawn for a reader", async () => {
    stubApi({ plans: [twoAccepted()] });
    await open(false);
    expect(screen.queryByRole("button", { name: "Complete accepted procedures" })).not.toBeInTheDocument();
  });

  it("lists exactly what will be completed and sends nothing until confirmed, then sends the plan's version", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [twoAccepted()] });
    await open();
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    const f = within(screen.getByRole("form", { name: "Complete the accepted procedures" }));
    const list = within(f.getByRole("list", { name: "Procedures that will be completed" }));
    expect(list.getAllByRole("listitem").map((li) => li.textContent)).toEqual(["PERIO-1 · PERIO-1 wording", "FILL-1 · FILL-1 wording · tooth 16 · surface O"]);
    expect(f.getByText(/Completed is final/)).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);
    await user.click(f.getByRole("button", { name: "Confirm: complete 2" }));
    expect(posts(api)[0].url).toContain("/api/treatment-plans/pl1/complete-accepted");
    expect(posts(api)[0].body).toEqual({ rowVersion: "R1" });
    expect(await screen.findByText("2 accepted procedures completed and added to the clinical history.")).toBeInTheDocument();
  });

  it("cancelling sends nothing", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [twoAccepted()] });
    await open();
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("form", { name: "Complete the accepted procedures" })).not.toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);
  });

  it("a refusal says nothing was completed and keeps the confirmation open", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [twoAccepted()], respond: { "/complete-accepted": () => json({ error: "nothing_to_complete", message: "No procedure in this plan has been accepted, so there is nothing to complete." }, 409) } });
    await open();
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    const f = within(screen.getByRole("form", { name: "Complete the accepted procedures" }));
    await user.click(f.getByRole("button", { name: /Confirm: complete/ }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was completed: No procedure in this plan has been accepted, so there is nothing to complete.");
  });

  it("a stale copy shows the conflict banner and says nothing was completed", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [twoAccepted()], respond: { "/complete-accepted": () => json({ error: "concurrency_conflict", entityType: "TreatmentPlan", entityId: "pl1" }, 409) } });
    await open();
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    await user.click(screen.getByRole("button", { name: /Confirm: complete/ }));
    expect(await screen.findByText(/Nothing was completed: this plan changed since you opened it/)).toBeInTheDocument();
    expect(screen.queryByText(/accepted procedures completed and added/)).not.toBeInTheDocument();
  });

  it("a dropped connection says nothing was completed", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [twoAccepted()] });
    await open();
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    const real = globalThis.fetch;
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => (String(url).includes("/complete-accepted") ? Promise.reject(new TypeError("Failed to fetch")) : real(url, init))));
    await user.click(screen.getByRole("button", { name: /Confirm: complete/ }));
    expect(await screen.findByText("Nothing was completed: the connection dropped. Try again.")).toBeInTheDocument();
    expect(api.calls.filter((c) => c.url.includes("/complete-accepted"))).toHaveLength(0);
  });

  it("has no accessibility violations with the confirmation open", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [twoAccepted()] });
    const { container } = render(<TreatmentPlansPanel patient={patient} canWrite />);
    await screen.findByRole("heading", { name: "Plans on record" });
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    expect(await axe(container)).toHaveNoViolations();
  });
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

  it("refreshes after a completion so the new care shows straight away", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [twoAccepted()], respond: {} });
    await open();
    expect(await screen.findByText(/No procedure has been completed for this patient yet/)).toBeInTheDocument();
    api.respond["/complete-accepted"] = () => {
      api.history = [entry()];
      return json(plan({ items: [accepted("it1", "PERIO-1", { completion: "Completed" })] }));
    };
    await user.click(screen.getByRole("button", { name: "Complete accepted procedures" }));
    await user.click(screen.getByRole("button", { name: /Confirm: complete/ }));
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
