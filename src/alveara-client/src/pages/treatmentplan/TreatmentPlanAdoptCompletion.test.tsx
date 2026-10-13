import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import type { CompletionReadiness, CompletionResult, ItemReadiness } from "../../services/completionApi";
import { adoptionEffectWords } from "./TreatmentPlanAdoptCompletion";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-008-C02: the screen that adopts a procedure completed the old way, against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; here: the action appears only when a
 * completed procedure exists, the form lists only legacy completions, says in words what adopting will do (from the server's readiness), names what is missing before sending anything, sends the plan's version
 * and a key that a retry reuses, and every failure says nothing was recorded and keeps what was entered.
 */
const legacyPlan = () => plan({ items: [item({ id: "it1", procedureCode: "PERIO-1", acceptance: "Accepted", completion: "Completed" }), item({ id: "it2", itemNumber: 2, procedureCode: "FILL-1", acceptance: "Accepted" })], acceptanceStatus: "Accepted" });

const row = (over: Partial<ItemReadiness> = {}): ItemReadiness => ({
  itemId: "it1", itemNumber: 1, procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", toothKey: null, surface: null, ready: false, notReadyReason: "already_completed", chartEffect: "NotApplicable", chartMatches: 0,
  billable: true, fee: 120, chargeExists: false, legacyCompleted: true, ...over,
});
const readinessOf = (items: ItemReadiness[]): CompletionReadiness => ({
  planId: "pl1", rowVersion: "R1", items,
  providers: [{ id: "pv1", name: "Dr. Okafor", specialty: "General dentistry" }],
  encounters: [{ id: "en1", atUtc: "2030-01-01T10:00:00Z", status: "Draft" }],
});
const resultOf = (replayed = false): CompletionResult => ({
  plan: legacyPlan(), setId: "s1", correlationId: "c1", replayed, adoptionReason: "Completed before records existed",
  procedures: [{ id: "cp1", planItemId: "it1", itemNumber: 1, procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", toothKey: null, surface: null, encounterId: "en1", providerProfileId: "pv1", performedAtUtc: "2030-02-01T10:00:00Z", planRevisionNumber: 1, chartOutcome: "NotApplicable", toothFindingId: null, charge: { outcome: "created", entryNumber: 1, amount: 120 }, adopted: true, adoptionReason: "Completed before records existed" }],
});

const adopts = (api: Api) => api.calls.filter((c) => c.url.includes("/adopt-completion"));
const ready = (items: ItemReadiness[] = [row()]) => ({ "/completion-readiness": () => json(readinessOf(items)) });

async function openAdopt(user: ReturnType<typeof userEvent.setup>) {
  render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
  await screen.findByRole("heading", { name: "Plans on record" });
  await user.click(screen.getByRole("button", { name: "Adopt earlier completions…" }));
  return within(await screen.findByRole("form", { name: "Adopt an earlier completion" }));
}

async function fill(f: ReturnType<typeof within>, user: ReturnType<typeof userEvent.setup>) {
  await user.selectOptions(f.getByLabelText("Provider"), "pv1");
  await user.selectOptions(f.getByLabelText("Encounter"), "en1");
  await user.type(f.getByLabelText("Why is this being recorded now?"), "Completed before records existed");
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("where adopting is offered", () => {
  it("is offered to a writer when a procedure is completed, and not otherwise", async () => {
    stubApi({ plans: [legacyPlan()] });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Plans on record" });
    expect(screen.getByRole("button", { name: "Adopt earlier completions…" })).toBeInTheDocument();
    vi.unstubAllGlobals();

    stubApi({ plans: [plan({ items: [item({ acceptance: "Accepted" })], acceptanceStatus: "Accepted" })] });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
    expect((await screen.findAllByRole("heading", { name: "Plans on record" })).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("button", { name: "Adopt earlier completions…" })).toHaveLength(1);              // still only the first render's
  });

  it("is not offered to a reader", async () => {
    stubApi({ plans: [legacyPlan()] });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Plans on record" });
    expect(screen.queryByRole("button", { name: "Adopt earlier completions…" })).not.toBeInTheDocument();
  });
});

describe("the adoption form", () => {
  it("lists only the legacy completions and says what adopting will do, from the server's readiness", async () => {
    stubApi({ plans: [legacyPlan()], respond: ready([row({ chartEffect: "Updated", chartMatches: 1 }), row({ itemId: "it2", itemNumber: 2, procedureCode: "FILL-1", legacyCompleted: false })]) });
    const f = await openAdopt(userEvent.setup());
    expect(f.getAllByRole("option", { name: /PERIO-1/ })).toHaveLength(1);
    expect(f.queryByRole("option", { name: /FILL-1/ })).not.toBeInTheDocument();
    expect(f.getByText(/Adopting PERIO-1: the linked chart finding will be marked Completed; a charge of \$120\.00 will be made\./)).toBeInTheDocument();
    expect(f.getByText(/The earlier completion is not changed/)).toBeInTheDocument();
  });

  it("describes each effect in words and refuses an ambiguous chart match", () => {
    expect(adoptionEffectWords(row({ billable: false, fee: 0 }))).toBe("no chart finding changes; it has no fee, so nothing is charged.");
    expect(adoptionEffectWords(row({ chartEffect: "AlreadyCompleted", chargeExists: true }))).toBe("the linked chart finding is already Completed and is left alone; its existing charge is kept.");
    expect(adoptionEffectWords(row({ chartEffect: "Ambiguous", chartMatches: 2 }))).toContain("more than one chart finding could match");
  });

  it("says there is nothing to adopt when the server finds no legacy completion", async () => {
    stubApi({ plans: [legacyPlan()], respond: ready([row({ legacyCompleted: false })]) });
    render(<MemoryRouter><TreatmentPlansPanel patient={patient} canWrite /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Plans on record" });
    await userEvent.setup().click(screen.getByRole("button", { name: "Adopt earlier completions…" }));
    expect(await screen.findByText(/nothing to adopt/)).toBeInTheDocument();
  });

  it("names what is missing beside its field and sends nothing", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [legacyPlan()], respond: ready() });
    const f = await openAdopt(user);
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    expect(await f.findByText("Choose the provider who performed the care.")).toBeInTheDocument();
    expect(f.getByText("Choose the encounter this care belongs to.")).toBeInTheDocument();
    expect(f.getByText("Say why this is being recorded now.")).toBeInTheDocument();
    expect(adopts(api)).toHaveLength(0);
  });

  it("sends the plan's version, the choices and the reason, then reports the adoption and closes", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [legacyPlan()], respond: { ...ready(), "/adopt-completion": () => json(resultOf()) } });
    const f = await openAdopt(user);
    await fill(f, user);
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    expect(await screen.findByText(/PERIO-1 adopted: its provider, encounter and time are recorded/)).toBeInTheDocument();
    const call = adopts(api)[0];
    expect(call.url).toContain("/api/treatment-plans/pl1/items/it1/adopt-completion");
    expect(call.body).toMatchObject({ rowVersion: "R1", encounterId: "en1", providerId: "pv1", reason: "Completed before records existed" });
    expect(typeof call.body?.idempotencyKey).toBe("string");
    expect(screen.queryByRole("form", { name: "Adopt an earlier completion" })).not.toBeInTheDocument();
  });

  it("reuses the save key when the same request is sent again after a dropped connection", async () => {
    const user = userEvent.setup();
    let calls = 0;
    const api = stubApi({ plans: [legacyPlan()], respond: { ...ready(), "/adopt-completion": () => { calls += 1; if (calls === 1) throw new TypeError("Failed to fetch"); return json(resultOf(true)); } } });
    const f = await openAdopt(user);
    await fill(f, user);
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was recorded: the connection dropped");
    expect(f.getByLabelText("Why is this being recorded now?")).toHaveValue("Completed before records existed");   // what was entered is kept
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    await screen.findByText(/adopted earlier; nothing new was recorded/);
    expect(adopts(api)).toHaveLength(2);
    expect(adopts(api)[1].body?.idempotencyKey).toBe(adopts(api)[0].body?.idempotencyKey);
  });

  it("shows the server's field errors beside the fields and its refusals as nothing recorded", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [legacyPlan()], respond: { ...ready(), "/adopt-completion": () => json({ error: "validation_failed", message: "Fix the marked fields.", fieldErrors: { performedAtUtc: "The performed time cannot be before this plan was created." } }, 400) } });
    const f = await openAdopt(user);
    await fill(f, user);
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    expect(await f.findByText("The performed time cannot be before this plan was created.")).toBeInTheDocument();
    expect(f.getByRole("alert")).toHaveTextContent("Nothing was recorded: check the fields marked below.");
  });

  it("says nothing was recorded for a refusal, and for a stale plan", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [legacyPlan()], respond: { ...ready(), "/adopt-completion": () => json({ error: "not_a_legacy_completion", message: "Procedure 1 already has a completion record, so it is not a legacy completion." }, 409) } });
    const f = await openAdopt(user);
    await fill(f, user);
    await user.click(f.getByRole("button", { name: "Adopt PERIO-1" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Nothing was recorded: Procedure 1 already has a completion record");
  });

  it("has no accessibility violations with the form open", async () => {
    stubApi({ plans: [legacyPlan()], respond: ready() });
    await openAdopt(userEvent.setup());
    expect(await axe(document.body)).toHaveNoViolations();
  });
});
