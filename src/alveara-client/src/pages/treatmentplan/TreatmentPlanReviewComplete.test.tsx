import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { P, item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import type { CompletionReadiness, CompletionResult, ItemReadiness } from "../../services/completionApi";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-008-C01: the completion review screen against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI: it is the primary way to complete,
 * it shows what the server says each procedure will do and cannot pick one the server says is not ready, it names what is missing before sending anything, it sends the selection with the readiness's version and
 * a save key that a retry reuses, it shows a completion only after the server confirmed the whole set, and every failure says nothing was completed and keeps what was entered.
 */
const accepted = (id: string, code: string) => item({ id, procedureCode: code, procedureDescription: `${code} wording`, acceptance: "Accepted" });
const acceptedPlan = () => plan({ items: [accepted("it1", "PERIO-1"), accepted("it2", "FILL-1")], acceptanceStatus: "Accepted" });

const row = (over: Partial<ItemReadiness> = {}): ItemReadiness => ({
  itemId: "it1", itemNumber: 1, procedureCode: "PERIO-1", procedureDescription: "PERIO-1 wording", toothKey: null, surface: null, ready: true, notReadyReason: null, chartEffect: "NotApplicable", chartMatches: 0,
  billable: true, fee: 120, chargeExists: false, legacyCompleted: false, ...over,
});
const readinessOf = (items: ItemReadiness[]): CompletionReadiness => ({
  planId: "pl1", rowVersion: "R1", items,
  providers: [{ id: "pv1", name: "Dr. Okafor", specialty: "General dentistry" }, { id: "pv2", name: "Dr. Li", specialty: "Periodontics" }],
  encounters: [{ id: "en1", atUtc: "2030-01-01T10:00:00Z", status: "Draft" }],
});
const resultOf = (replayed = false): CompletionResult => ({
  plan: acceptedPlan(), setId: "s1", correlationId: "c1", replayed,
  procedures: [
    { id: "cp1", planItemId: "it1", itemNumber: 1, procedureCode: "PERIO-1", procedureDescription: "PERIO-1 wording", toothKey: null, surface: null, encounterId: "en1", providerProfileId: "pv1", performedAtUtc: "2030-02-01T10:00:00Z", planRevisionNumber: 1, chartOutcome: "NotApplicable", toothFindingId: null, charge: { outcome: "created", entryNumber: 3, amount: 120 } },
    { id: "cp2", planItemId: "it2", itemNumber: 2, procedureCode: "FILL-1", procedureDescription: "FILL-1 wording", toothKey: "16", surface: "O", encounterId: "en1", providerProfileId: "pv1", performedAtUtc: "2030-02-01T10:00:00Z", planRevisionNumber: 1, chartOutcome: "Updated", toothFindingId: "f1", charge: { outcome: "none", entryNumber: null, amount: null } },
  ],
});

const readinessCalls = (api: Api) => api.calls.filter((c) => c.url.includes("/completion-readiness"));
const completes = (api: Api) => api.calls.filter((c) => c.url.includes("/complete-procedures"));

async function openReview(api: Api, user: ReturnType<typeof userEvent.setup>) {
  render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
  await screen.findByRole("heading", { name: "Plans on record" });
  await user.click(screen.getByRole("button", { name: "Review and complete…" }));
  await screen.findByRole("form", { name: "Review and complete procedures" });
  expect(readinessCalls(api).length).toBeGreaterThan(0);
  return within(screen.getByRole("form", { name: "Review and complete procedures" }));
}

async function fill(f: ReturnType<typeof within>, user: ReturnType<typeof userEvent.setup>) {
  await user.selectOptions(f.getByLabelText("Provider"), "pv1");
  await user.selectOptions(f.getByLabelText("Encounter"), "en1");
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("where completing is offered", () => {
  it("makes Review and complete the only way to complete: the legacy completion controls are gone", async () => {
    stubApi({ plans: [acceptedPlan()] });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Plans on record" });
    expect(screen.getByRole("button", { name: "Review and complete…" })).toHaveClass("btn-primary");
    expect(screen.queryByRole("group", { name: "Legacy completion" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Complete accepted procedures" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Mark .* completed$/ })).not.toBeInTheDocument();
  });

  it("offers neither to a reader or when nothing accepted is left to complete", async () => {
    stubApi({ plans: [acceptedPlan()] });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Plans on record" });
    expect(screen.queryByRole("button", { name: /Review and complete|Complete accepted procedures/ })).not.toBeInTheDocument();
    vi.unstubAllGlobals();
    stubApi({ plans: [plan({ items: [item({ id: "it1", acceptance: "Accepted", completion: "Completed" })] })] });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
    expect((await screen.findAllByRole("heading", { name: "Plans on record" })).length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Review and complete…" })).not.toBeInTheDocument();
  });
});

describe("what the screen shows", () => {
  it("shows for each procedure what the server says it will do and cannot pick one that is not ready", async () => {
    const api = stubApi({
      plans: [acceptedPlan()],
      respond: {
        "/completion-readiness": () => json(readinessOf([
          row({ itemId: "it1", itemNumber: 1, procedureCode: "FILL-1", toothKey: "16", surface: "O", chartEffect: "Updated", chartMatches: 1, fee: 140 }),
          row({ itemId: "it2", itemNumber: 2, procedureCode: "EXAM-1", billable: false, fee: 0 }),
          row({ itemId: "it3", itemNumber: 3, procedureCode: "SCALE-1", chargeExists: true, fee: 70 }),
          row({ itemId: "it4", itemNumber: 4, procedureCode: "CROWN-1", toothKey: "17", ready: false, notReadyReason: "ambiguous_chart_finding", chartEffect: "Ambiguous", chartMatches: 2 }),
          row({ itemId: "it5", itemNumber: 5, procedureCode: "POL-1", ready: false, notReadyReason: "already_completed" }),
        ])),
      },
    });
    const f = await openReview(api, userEvent.setup());
    const table = within(f.getByRole("table", { name: "Procedures to complete" }));
    const rows = table.getAllByRole("row").slice(1);
    expect(rows[0]).toHaveTextContent("FILL-1");
    expect(rows[0]).toHaveTextContent("Tooth 16, O");
    expect(rows[0]).toHaveTextContent("The chart finding will be marked Completed.");
    expect(rows[0]).toHaveTextContent("Charge $140.00");
    expect(rows[1]).toHaveTextContent("No charge (no fee)");
    expect(rows[2]).toHaveTextContent("Charge already exists ($70.00)");
    expect(rows[3]).toHaveTextContent("More than one chart finding could match.");
    expect(rows[3]).toHaveTextContent("2 chart findings match; unselect this procedure or correct the diagnosis links.");
    expect(rows[4]).toHaveTextContent("Already completed.");
    expect(table.getByRole("checkbox", { name: "Complete procedure 4 CROWN-1" })).toBeDisabled();
    expect(table.getByRole("checkbox", { name: "Complete procedure 5 POL-1" })).toBeDisabled();
    expect(table.getByRole("checkbox", { name: "Complete procedure 1 FILL-1" })).toBeChecked();
    expect(f.getByText(/3 procedures will be completed; 1 charge totalling \$140\.00 will be made; 1 already has its charge; 1 has no fee; 1 chart finding will be marked Completed\./)).toBeInTheDocument();
    expect(f.getByRole("button", { name: "Confirm completion of 3 procedures" })).toBeEnabled();
  });

  it("says it could not load what completing would do rather than offering a blind completion, and tries again", async () => {
    const user = userEvent.setup();
    let fail = true;
    const api = stubApi({ plans: [acceptedPlan()], respond: { "/completion-readiness": () => (fail ? json({ error: "server_error", message: "boom" }, 500) : json(readinessOf([row()]))) } });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Plans on record" });
    await user.click(screen.getByRole("button", { name: "Review and complete…" }));
    expect(await screen.findByText(/Could not load what completing would do, so nothing can be completed now/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Confirm completion/ })).not.toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("form", { name: "Review and complete procedures" })).toBeInTheDocument();
    expect(readinessCalls(api)).toHaveLength(2);
  });
});

describe("completing", () => {
  it("names what is missing beside its field and sends nothing", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [acceptedPlan()], respond: { "/completion-readiness": () => json(readinessOf([row()])) } });
    const f = await openReview(api, user);
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect(f.getByText("Choose the provider who performed the care.")).toBeInTheDocument();
    expect(f.getByText("Choose the encounter this care belongs to.")).toBeInTheDocument();
    expect(f.getByLabelText("Provider")).toHaveAccessibleDescription("Choose the provider who performed the care.");
    await user.click(f.getByRole("checkbox", { name: "Complete procedure 1 PERIO-1" }));
    expect(f.getByRole("button", { name: "Confirm completion of 0 procedures" })).toBeDisabled();
    expect(completes(api)).toHaveLength(0);
  });

  it("sends the selection, the provider, the encounter, the time, the readiness's version and a save key, then shows what the server recorded with where to look next", async () => {
    const user = userEvent.setup();
    const api = stubApi({
      plans: [acceptedPlan()],
      respond: { "/completion-readiness": () => json(readinessOf([row({ itemId: "it1" }), row({ itemId: "it2", itemNumber: 2, procedureCode: "FILL-1", toothKey: "16", surface: "O", chartEffect: "Updated", chartMatches: 1 })])), "/complete-procedures": () => json(resultOf()) },
    });
    const f = await openReview(api, user);
    await fill(f, user);
    await user.click(f.getByRole("button", { name: "Confirm completion of 2 procedures" }));

    const sent = completes(api)[0].body as Record<string, unknown>;
    expect(sent).toMatchObject({ rowVersion: "R1", encounterId: "en1", providerId: "pv1", itemIds: ["it1", "it2"] });
    expect(String(sent.idempotencyKey).length).toBeGreaterThan(8);
    expect(sent.performedAtUtc).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
    expect("amount" in sent).toBe(false);                                                            // the charge is never sent

    const done = await screen.findByRole("region", { name: "Completion recorded" });
    expect(within(done).getByText("2 procedures completed")).toBeInTheDocument();
    expect(done).toHaveTextContent("charged $120.00 (account entry 3)");
    expect(done).toHaveTextContent("chart finding marked Completed");
    expect(done).toHaveTextContent("no charge");
    expect(within(done).getByRole("link", { name: "odontogram" })).toHaveAttribute("href", `/patients/${P}/odontogram`);
    expect(within(done).getByRole("link", { name: "account ledger" })).toHaveAttribute("href", `/patients/${P}/billing`);
    expect(within(done).getByRole("link", { name: "clinical history" })).toHaveAttribute("href", "#alv-ch-title");
    expect(api.calls.filter((c) => c.url.includes(`/api/patients/${P}/clinical-history`)).length).toBeGreaterThanOrEqual(2);       // the history reloaded
  });

  it("completes only the procedures left selected", async () => {
    const user = userEvent.setup();
    const api = stubApi({
      plans: [acceptedPlan()],
      respond: { "/completion-readiness": () => json(readinessOf([row({ itemId: "it1" }), row({ itemId: "it2", itemNumber: 2, procedureCode: "FILL-1" })])), "/complete-procedures": () => json(resultOf()) },
    });
    const f = await openReview(api, user);
    await fill(f, user);
    await user.click(f.getByRole("checkbox", { name: "Complete procedure 2 FILL-1" }));
    expect(f.getByText(/1 procedure will be completed; 1 charge totalling \$120\.00 will be made/)).toBeInTheDocument();
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect((completes(api)[0].body as { itemIds: string[] }).itemIds).toEqual(["it1"]);
  });
});

describe("when completing fails", () => {
  const setup = async (response: () => Response) => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [acceptedPlan()], respond: { "/completion-readiness": () => json(readinessOf([row()])), "/complete-procedures": response } });
    const f = await openReview(api, user);
    await fill(f, user);
    return { user, api, f };
  };

  it("says nothing was completed when the save failed, shows no completion and keeps what was entered", async () => {
    const { user, api, f } = await setup(() => json({ error: "save_failed", message: "The completion could not be saved, so nothing was completed. Try again; if it keeps failing, contact support." }, 503));
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was completed: The completion could not be saved, so nothing was completed.");
    expect(screen.queryByRole("region", { name: "Completion recorded" })).not.toBeInTheDocument();
    expect(f.getByLabelText("Provider")).toHaveValue("pv1");
    expect(f.getByLabelText("Encounter")).toHaveValue("en1");
    expect(completes(api)).toHaveLength(1);
  });

  it("lists the procedures a refusal is about beside them, refreshes what the server says and records nothing", async () => {
    const { user, api, f } = await setup(() => json({
      error: "items_not_completable", message: "Some of the selected procedures cannot be completed, so none were. Review the list and try again.",
      fieldErrors: { "items.1": "Procedure 1 is already completed." }, items: [{ itemId: "it1", itemNumber: 1, reason: "already_completed" }],
    }, 409));
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was completed: Some of the selected procedures cannot be completed");
    expect(await f.findByText("Procedure 1 is already completed.")).toBeInTheDocument();
    expect(readinessCalls(api)).toHaveLength(2);                                                      // it looked again
    expect(screen.queryByRole("region", { name: "Completion recorded" })).not.toBeInTheDocument();
  });

  it("says nothing was completed for a stale plan and lets the person reload the review", async () => {
    const { user, api, f } = await setup(() => json({ error: "concurrency_conflict", entityType: "TreatmentPlan", entityId: "pl1" }, 409));
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was completed: this plan changed since you opened this screen. Reload the review and look again.");
    await user.click(f.getByRole("button", { name: "Reload the review" }));
    expect(readinessCalls(api)).toHaveLength(2);
  });

  it("a dropped connection says nothing was completed and a retry of the same request reuses its save key, while a changed request gets a new one", async () => {
    const user = userEvent.setup();
    let calls = 0;
    const api = stubApi({
      plans: [acceptedPlan()],
      respond: { "/completion-readiness": () => json(readinessOf([row()])), "/complete-procedures": () => { calls += 1; if (calls <= 2) throw new TypeError("Failed to fetch"); return json(resultOf()); } },
    });
    const f = await openReview(api, user);
    await fill(f, user);
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was completed: the connection dropped.");
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    await f.findByRole("alert");
    await user.selectOptions(f.getByLabelText("Provider"), "pv2");
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    await screen.findByRole("region", { name: "Completion recorded" });

    const keys = completes(api).map((c) => (c.body as { idempotencyKey: string }).idempotencyKey);
    expect(keys[0]).toBe(keys[1]);                                                                    // the same request, retried
    expect(keys[2]).not.toBe(keys[0]);                                                                // a different provider is a different request
  });
});

describe("accessibility", () => {
  it("has no accessibility violations with the review open and after a refusal", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [acceptedPlan()], respond: { "/completion-readiness": () => json(readinessOf([row(), row({ itemId: "it2", itemNumber: 2, ready: false, notReadyReason: "withdrawn" })])) } });
    const container = render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>).container;
    await screen.findByRole("heading", { name: "Plans on record" });
    await user.click(screen.getByRole("button", { name: "Review and complete…" }));
    const f = within(await screen.findByRole("form", { name: "Review and complete procedures" }));
    await user.click(f.getByRole("button", { name: "Confirm completion of 1 procedure" }));
    expect(await f.findByText("Choose the provider who performed the care.")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
    expect(readinessCalls(api).length).toBeGreaterThan(0);
  });
});

describe("the clinical history", () => {
  it("adds who performed it, when, what was charged and the chart effect for a procedure completed through the workflow, and nothing extra for an old one", async () => {
    const api = stubApi({ plans: [acceptedPlan()] });
    api.history = [
      { planId: "pl1", planTitle: "Gum health plan", itemId: "it1", itemNumber: 1, procedureCodeSystem: "Local", procedureCode: "FILL-1", procedureDescription: "Filling", toothKey: "16", surface: "O", completedByName: "Dr. Okafor", completedAtUtc: "2030-02-01T10:00:00Z", planRevisionAtCompletion: 1,
        completedProcedureId: "cp1", providerName: "Dr. Li", performedAtUtc: "2030-02-01T09:30:00Z", encounterId: "en1", chargeEntryNumber: 3, chargeAmount: 140, chartOutcome: "Updated" },
      { planId: "pl1", planTitle: "Gum health plan", itemId: "it2", itemNumber: 2, procedureCodeSystem: "Local", procedureCode: "PERIO-1", procedureDescription: "Maintenance", toothKey: null, surface: null, completedByName: "Dr. Okafor", completedAtUtc: "2030-01-01T10:00:00Z", planRevisionAtCompletion: 1 },
    ];
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} /></MemoryRouter>);
    const list = await screen.findByRole("list", { name: "Completed procedures" });
    const [authoritative, old] = within(list).getAllByRole("listitem");
    expect(authoritative).toHaveTextContent("Performed by Dr. Li");
    expect(authoritative).toHaveTextContent("charged $140.00 (account entry 3)");
    expect(authoritative).toHaveTextContent("chart finding completed");
    expect(old).toHaveTextContent("Completed by Dr. Okafor");
    expect(old).not.toHaveTextContent("Performed by");
  });
});
