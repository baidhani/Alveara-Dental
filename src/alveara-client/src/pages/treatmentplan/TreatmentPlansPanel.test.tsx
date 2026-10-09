import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PatientDetail } from "../../services/patientsApi";
import type { PlanItem, PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * STORY-015: the treatment plan screen against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's behaviour for each outcome:
 * a plan created with the item the person chose, gaps named beside their fields before anything is sent, the server's refusal shown beside the field with what was typed kept, withdrawals that need a
 * reason, a stale change shown as a conflict, honest empty and failed states, and a reader who sees everything but gets no controls and never calls the catalog.
 */
const P = "aaaaaaaa-0000-0000-0000-000000000001";
const patient = { id: P } as PatientDetail;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const diagnosis = (over: Partial<Diagnosis> = {}): Diagnosis => ({
  id: "dx1", patientId: P, encounterId: "e1", encounterAtUtc: "2030-01-01T10:00:00Z", label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, treatmentPlanReferenceState: null,
  status: "Active", recordedByName: "Dr. Okafor", recordedAtUtc: "2030-01-01T10:00:00Z", updatedByName: null, updatedAtUtc: null, withdrawnByName: null, withdrawnAtUtc: null, withdrawnReason: null, rowVersion: "D1", ...over,
});
const procedure = (over: Partial<PlanningProcedure> = {}): PlanningProcedure => ({
  procedureId: "pr1", versionId: "v1", versionNumber: 1, codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", dentition: "Both", fee: 120, currency: "USD", ...over,
});
const item = (over: Partial<PlanItem> = {}): PlanItem => ({
  id: "it1", itemNumber: 1, diagnosisId: "dx1", diagnosisLabel: "Chronic periodontitis", procedureId: "pr1", procedureVersionId: "v1", procedureVersionNumber: 1, procedureCodeSystem: "Local", procedureCode: "PERIO-1",
  procedureDescription: "Periodontal maintenance", toothKey: null, surface: null, fee: 120, currency: "USD", isWithdrawn: false, withdrawnReason: null, withdrawnAtUtc: null, withdrawnByName: null,
  createdAtUtc: "2030-01-02T10:00:00Z", createdByName: "Dr. Okafor", ...over,
});
const plan = (over: Partial<TreatmentPlan> = {}): TreatmentPlan => ({
  id: "pl1", patientId: P, title: "Gum health plan", status: "Proposed", activeItemCount: 1, estimateTotal: 120, currency: "USD",
  estimateLabel: "Practice fee estimate: the practice's catalog fee when each item was proposed. It is not an insurance estimate and not a guaranteed patient cost.", items: [item()],
  createdAtUtc: "2030-01-02T10:00:00Z", createdByName: "Dr. Okafor", updatedAtUtc: null, updatedByName: null, withdrawnReason: null, withdrawnAtUtc: null, withdrawnByName: null, rowVersion: "R1", ...over,
});

interface Api {
  plans: TreatmentPlan[];
  diagnoses: Diagnosis[];
  procedures: PlanningProcedure[];
  calls: { url: string; method: string; body: Record<string, unknown> | null }[];
  respond: Record<string, (body: Record<string, unknown> | null) => Response>;
}

function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { plans: [plan()], diagnoses: [diagnosis()], procedures: [procedure(), procedure({ procedureId: "pr2", code: "FILL-1", description: "Filling", scope: "ToothSurface", fee: 80 })], calls: [], respond: {}, ...over };
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    api.calls.push({ url: u, method, body });
    for (const [fragment, handler] of Object.entries(api.respond)) if (u.includes(fragment)) return Promise.resolve(handler(body));
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/api/procedures/active")) return Promise.resolve(json(api.procedures));
    if (u.includes("/diagnoses")) return Promise.resolve(json({ patientId: P, diagnoses: api.diagnoses }));
    if (u.includes("/treatment-plans") && method === "GET") return Promise.resolve(json({ patientId: P, plans: api.plans }));
    if (method === "POST") return Promise.resolve(json(api.plans[0] ?? plan()));
    return Promise.resolve(json({}));
  }));
  return api;
}

const posts = (api: Api) => api.calls.filter((c) => c.method === "POST");
const openPanel = async (canWrite: boolean) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};
const createForm = () => screen.getByRole("form", { name: "Create a treatment plan" });

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("reading plans", () => {
  it("shows the plan, its procedure with the fee, the estimate total and what it is, with no controls and without calling the catalog or the diagnoses", async () => {
    const api = stubApi();
    await openPanel(false);
    expect(screen.getByRole("heading", { name: /Gum health plan/ })).toBeInTheDocument();
    expect(screen.getByText("PERIO-1")).toBeInTheDocument();
    expect(screen.getByText(/Fee \$120\.00 \(catalog version 1\)/)).toBeInTheDocument();
    expect(screen.getByText("Estimate total: $120.00")).toBeInTheDocument();
    expect(screen.getByText(/not an insurance estimate/)).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("You can read this but your role cannot change it.");
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add procedure|Rename plan|Withdraw/ })).not.toBeInTheDocument();
    expect(api.calls.some((c) => c.url.includes("/api/procedures") || c.url.includes("/diagnoses"))).toBe(false);
  });

  it("says none has been created, never that no treatment is needed", async () => {
    stubApi({ plans: [] });
    await openPanel(false);
    expect(screen.getByText("No treatment plan has been created for this patient. That means none has been created, not that no treatment is needed.")).toBeInTheDocument();
  });

  it("a failed load says it could not load rather than showing an empty list", async () => {
    stubApi({ respond: { "/treatment-plans": () => json({ error: "server_error", message: "boom" }, 500) } });
    render(<TreatmentPlansPanel patient={patient} />);
    expect(await screen.findByText("Could not load the treatment plans")).toBeInTheDocument();
    expect(screen.queryByText(/No treatment plan has been created/)).not.toBeInTheDocument();
  });

  it("a withdrawn plan and a withdrawn procedure say Withdrawn and why, and leave the procedure's fee out of the total", async () => {
    stubApi({ plans: [plan({ status: "Withdrawn", withdrawnReason: "Entered in error", withdrawnByName: "Dr. Okafor", activeItemCount: 0, estimateTotal: 0, items: [item({ isWithdrawn: true, withdrawnReason: "Patient declined" })] })] });
    await openPanel(true);
    expect(screen.getAllByText("Withdrawn").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText(/Entered in error/)).toBeInTheDocument();
    expect(screen.getByText(/Patient declined/)).toBeInTheDocument();
    expect(screen.getByText("Estimate total: $0.00")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add procedure|Rename plan|Withdraw/ })).not.toBeInTheDocument();     // a withdrawn plan can no longer be changed
  });

  it("has no accessibility violations for an author", async () => {
    stubApi();
    await openPanel(true);
    expect(await axe(document.body)).toHaveNoViolations();
  });
});

describe("creating a plan (acceptance 1 and 2)", () => {
  it("sends the diagnosis, the procedure and the title the person chose, then confirms and reloads", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [] });
    await openPanel(true);
    const f = within(createForm());
    await user.type(f.getByLabelText("Plan title"), "  Gum health plan ");
    await user.selectOptions(f.getByLabelText("Diagnosis this procedure is for"), "dx1");
    await user.selectOptions(f.getByLabelText("Procedure"), "pr1");
    await user.click(f.getByRole("button", { name: "Create treatment plan" }));
    const [post] = posts(api);
    expect(post.url).toContain(`/api/patients/${P}/treatment-plans`);
    expect(post.body).toMatchObject({ title: "Gum health plan", items: [{ diagnosisId: "dx1", procedureId: "pr1", toothKey: null, surface: null }] });
    expect(typeof post.body?.idempotencyKey).toBe("string");
    expect(await screen.findByText('Treatment plan "Gum health plan" created.')).toBeInTheDocument();
  });

  it("names every gap beside its field and sends nothing", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [] });
    await openPanel(true);
    await user.click(within(createForm()).getByRole("button", { name: "Create treatment plan" }));
    expect(await screen.findByText("Give the plan a title.", { selector: "span" })).toBeInTheDocument();
    expect(screen.getByText("Choose the diagnosis this procedure is for.", { selector: "span" })).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);
  });

  it("asks for the tooth and surface only for a procedure done on a tooth surface", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [] });
    await openPanel(true);
    const f = within(createForm());
    expect(f.queryByLabelText("Tooth")).not.toBeInTheDocument();
    await user.selectOptions(f.getByLabelText("Procedure"), "pr2");
    expect(f.getByLabelText("Tooth")).toBeInTheDocument();
    expect(f.getByLabelText("Surface")).toBeInTheDocument();
    await user.selectOptions(f.getByLabelText("Procedure"), "pr1");
    expect(f.queryByLabelText("Tooth")).not.toBeInTheDocument();
  });

  it("shows the server's refusal beside the field it is about and keeps what was typed", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [], respond: { "/treatment-plans": (b) => (b ? json({ error: "validation_failed", message: "no", fieldErrors: { "items[0].diagnosisId": "That diagnosis has been withdrawn. Choose a current diagnosis." } }, 400) : json({ patientId: P, plans: [] })) } });
    await openPanel(true);
    const f = within(createForm());
    await user.type(f.getByLabelText("Plan title"), "Gum health plan");
    await user.selectOptions(f.getByLabelText("Diagnosis this procedure is for"), "dx1");
    await user.selectOptions(f.getByLabelText("Procedure"), "pr1");
    await user.click(f.getByRole("button", { name: "Create treatment plan" }));
    expect(await screen.findByText("That diagnosis has been withdrawn. Choose a current diagnosis.", { selector: "span" })).toBeInTheDocument();
    expect(f.getByLabelText(/^Diagnosis this procedure is for/)).toHaveAttribute("aria-invalid", "true");
    expect(f.getByLabelText("Plan title")).toHaveValue("Gum health plan");
    expect(posts(api)).toHaveLength(1);
  });

  it("without a current diagnosis there is nothing to propose a procedure for, and it says so", async () => {
    stubApi({ plans: [], diagnoses: [] });
    await openPanel(true);
    expect(screen.getByRole("note")).toHaveTextContent("This patient has no current diagnosis.");
    expect(within(createForm()).getByRole("button", { name: "Create treatment plan" })).toBeDisabled();
  });

  it("does not offer a withdrawn diagnosis", async () => {
    stubApi({ plans: [], diagnoses: [diagnosis(), diagnosis({ id: "dx2", label: "Old finding", status: "Withdrawn" })] });
    await openPanel(true);
    expect(within(createForm()).queryByRole("option", { name: /Old finding/ })).not.toBeInTheDocument();
  });
});

describe("changing a plan", () => {
  it("withdraws a procedure only with a reason, and sends it with the plan's version", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Withdraw PERIO-1" }));
    const f = within(screen.getByRole("form", { name: "Withdraw PERIO-1 from this plan" }));
    expect(f.getByRole("button", { name: "Withdraw procedure" })).toBeDisabled();
    await user.type(f.getByLabelText("Why is this procedure being withdrawn?"), "Patient declined");
    await user.click(f.getByRole("button", { name: "Withdraw procedure" }));
    const [post] = posts(api);
    expect(post.url).toContain("/api/treatment-plans/pl1/items/it1/withdraw");
    expect(post.body).toEqual({ rowVersion: "R1", reason: "Patient declined" });
    expect(await screen.findByText("PERIO-1 withdrawn from the plan.")).toBeInTheDocument();
  });

  it("withdraws the plan only with a reason", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Withdraw plan" }));
    const f = within(screen.getByRole("form", { name: "Withdraw this treatment plan" }));
    expect(f.getByRole("button", { name: "Withdraw plan" })).toBeDisabled();
    await user.type(f.getByLabelText("Why is this plan being withdrawn?"), "Entered in error");
    await user.click(f.getByRole("button", { name: "Withdraw plan" }));
    expect(posts(api)[0].body).toEqual({ rowVersion: "R1", reason: "Entered in error" });
    expect(await screen.findByText("Treatment plan withdrawn.")).toBeInTheDocument();
  });

  it("renames the plan, and refuses a blank title without sending anything", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Rename plan" }));
    const f = within(screen.getByRole("form", { name: "Rename this treatment plan" }));
    await user.clear(f.getByLabelText("New title"));
    await user.click(f.getByRole("button", { name: "Save title" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Not renamed: Give the plan a title.");
    expect(posts(api)).toHaveLength(0);
    await user.type(f.getByLabelText("New title"), "Periodontal plan");
    await user.click(f.getByRole("button", { name: "Save title" }));
    expect(posts(api)[0].body).toEqual({ rowVersion: "R1", title: "Periodontal plan" });
  });

  it("adds a procedure with its own key and the plan's version", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Add procedure" }));
    const f = within(screen.getByRole("form", { name: "Add a procedure to this treatment plan" }));
    await user.selectOptions(f.getByLabelText("Diagnosis this procedure is for"), "dx1");
    await user.selectOptions(f.getByLabelText("Procedure"), "pr1");
    await user.click(f.getByRole("button", { name: "Add procedure" }));
    const [post] = posts(api);
    expect(post.url).toContain("/api/treatment-plans/pl1/items");
    expect(post.body).toMatchObject({ rowVersion: "R1", diagnosisId: "dx1", procedureId: "pr1" });
    expect(typeof post.body?.idempotencyKey).toBe("string");
    expect(await screen.findByText("Procedure added to the plan.")).toBeInTheDocument();
  });

  it("a stale change shows the conflict banner, says nothing was changed, and reloads on request", async () => {
    const user = userEvent.setup();
    const api = stubApi({ respond: { "/rename": () => json({ error: "concurrency_conflict", message: "stale" }, 409) } });
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Rename plan" }));
    const f = within(screen.getByRole("form", { name: "Rename this treatment plan" }));
    await user.clear(f.getByLabelText("New title"));
    await user.type(f.getByLabelText("New title"), "Other title");
    await user.click(f.getByRole("button", { name: "Save title" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(f.getByRole("alert")).toHaveTextContent("Not renamed: someone else changed this treatment plan.");
    const loads = api.calls.filter((c) => c.method === "GET" && c.url.includes("/treatment-plans")).length;
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    expect(api.calls.filter((c) => c.method === "GET" && c.url.includes("/treatment-plans")).length).toBe(loads + 1);
  });

  it("a refusal to withdraw says why and keeps the reason that was typed", async () => {
    const user = userEvent.setup();
    stubApi({ respond: { "/items/it1/withdraw": () => json({ error: "plan_withdrawn", message: "That treatment plan has been withdrawn, so it cannot be changed." }, 409) } });
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Withdraw PERIO-1" }));
    const f = within(screen.getByRole("form", { name: "Withdraw PERIO-1 from this plan" }));
    await user.type(f.getByLabelText("Why is this procedure being withdrawn?"), "Patient declined");
    await user.click(f.getByRole("button", { name: "Withdraw procedure" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Not withdrawn: That treatment plan has been withdrawn, so it cannot be changed.");
    expect(f.getByLabelText("Why is this procedure being withdrawn?")).toHaveValue("Patient declined");
  });
});
