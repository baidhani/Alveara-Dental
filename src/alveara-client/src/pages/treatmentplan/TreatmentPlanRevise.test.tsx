import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-015-C01: the plan builder grouped by phase and the revise form, against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's
 * behaviour: procedures shown under their phases with the unplaced ones last, the rationale and the patient explanation shown to every reader, the estimate's source and limits, a revision that needs a reason and
 * sends the whole layout, a removed phase putting its procedures back to unplaced, the server's refusal shown beside the field with what was typed kept, a revision that changes nothing saying so, a stale revision
 * shown as a conflict, and a reference code that is set once.
 */
const phases = [
  { phaseNumber: 1, name: "First visit", plannedVisit: "Today", targetDate: null },
  { phaseNumber: 2, name: "After healing", plannedVisit: "Two weeks later", targetDate: "2030-06-01" },
];
const phased = () => plan({
  currentRevision: 3, referenceCode: "PLAN-7", clinicalRationale: "Deep pockets on the lower left.", patientExplanation: "We clean below the gums first.", phases, activeItemCount: 3, estimateTotal: 200,
  items: [
    item({ id: "it1", itemNumber: 1, phaseNumber: 1 }),
    item({ id: "it2", itemNumber: 2, procedureCode: "FILL-1", procedureDescription: "Filling", fee: 60, phaseNumber: 2 }),
    item({ id: "it3", itemNumber: 3, procedureCode: "POL-1", procedureDescription: "Polish", fee: 20, phaseNumber: null }),
  ],
});
const posts = (api: Api) => api.calls.filter((c) => c.method === "POST");
const open = async (canWrite: boolean) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};
const reviseForm = () => screen.getByRole("form", { name: "Revise this treatment plan" });

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("what a reader sees", () => {
  it("shows procedures under their phases with the timing, the unplaced ones last, the words for staff and patient, the revision and the reference code, and no controls", async () => {
    stubApi({ plans: [phased()] });
    await open(false);
    expect(screen.getByRole("region", { name: "Phase 1: First visit of Gum health plan" })).toHaveTextContent("PERIO-1");
    const second = screen.getByRole("region", { name: "Phase 2: After healing of Gum health plan" });
    expect(second).toHaveTextContent("FILL-1");
    expect(second).toHaveTextContent("Two weeks later");
    expect(second).toHaveTextContent("target");
    expect(screen.getByRole("region", { name: "Not yet placed in a phase of Gum health plan" })).toHaveTextContent("POL-1");
    expect(screen.getAllByRole("heading", { level: 4 }).map((h) => h.textContent)).toEqual(["Phase 1: First visit", "Phase 2: After healing", "Not yet placed in a phase"]);
    expect(screen.getByText(/Deep pockets on the lower left\./)).toBeInTheDocument();
    expect(screen.getByText(/We clean below the gums first\./)).toBeInTheDocument();
    expect(screen.getByText("Revision 3 · Reference code PLAN-7")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Revise plan|Set reference code|Withdraw/ })).not.toBeInTheDocument();
  });

  it("says where the estimate comes from and what it is not, in the server's words", async () => {
    stubApi({ plans: [phased()] });
    await open(false);
    expect(screen.getByText("Estimate total: $200.00")).toBeInTheDocument();
    expect(screen.getByText(/not an insurance estimate and not a guaranteed patient cost/)).toBeInTheDocument();
    expect(screen.getByText(/Source: the practice's fee schedule/)).toHaveTextContent("No insurance benefits, discounts or payment plans are applied");
  });

  it("stays the plain list it was when a plan has no phases, and an empty phase says so", async () => {
    stubApi({ plans: [plan(), plan({ id: "pl2", title: "Phased", phases: [phases[0], phases[1]], items: [item({ id: "x", phaseNumber: 1 })] })] });
    await open(false);
    expect(screen.getByRole("list", { name: "Procedures in Gum health plan" })).toBeInTheDocument();
    expect(screen.getByText("No procedures in this phase yet.")).toBeInTheDocument();
  });
});

describe("revising a plan", () => {
  it("opens with the plan's current layout, needs a reason, and sends nothing until there is one", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [phased()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    const f = within(reviseForm());
    expect(f.getByLabelText("Phase 1 name")).toHaveValue("First visit");
    expect(f.getByLabelText(/^Clinical rationale/)).toHaveValue("Deep pockets on the lower left.");
    expect(f.getByLabelText("FILL-1 Filling")).toHaveValue("2");
    expect(f.getByText("Saving creates revision 4. Revision 3 and every earlier version stay on record.")).toBeInTheDocument();

    await user.click(f.getByRole("button", { name: "Save revision" }));
    expect(f.getAllByText("Say why this plan is being revised.").length).toBeGreaterThan(0);
    expect(posts(api)).toHaveLength(0);
  });

  it("sends the whole layout: every phase and the place of every live procedure, then says which revision it is", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [phased()], respond: { "/revise": () => json({ ...phased(), currentRevision: 4 }) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    const f = within(reviseForm());
    await user.click(f.getByRole("button", { name: "Add a phase" }));
    await user.type(f.getByLabelText("Phase 3 name"), "Review");
    await user.selectOptions(f.getByLabelText("POL-1 Polish"), "3");
    await user.type(f.getByLabelText("Why is this plan being revised?"), "Add a review visit");
    await user.click(f.getByRole("button", { name: "Save revision" }));

    const [post] = posts(api);
    expect(post.url).toContain("/api/treatment-plans/pl1/revise");
    expect(post.body).toMatchObject({
      reason: "Add a review visit", rowVersion: "R1", clinicalRationale: "Deep pockets on the lower left.", patientExplanation: "We clean below the gums first.",
      phases: [{ number: 1, name: "First visit", plannedVisit: "Today", targetDate: null }, { number: 2, name: "After healing", plannedVisit: "Two weeks later", targetDate: "2030-06-01" },
        { number: 3, name: "Review", plannedVisit: null, targetDate: null }],
      placements: [{ itemId: "it1", phaseNumber: 1 }, { itemId: "it2", phaseNumber: 2 }, { itemId: "it3", phaseNumber: 3 }],
    });
    expect(await screen.findByText("Plan revised: this is now revision 4. The earlier version is kept.")).toBeInTheDocument();
  });

  it("puts the procedures of a removed phase back to not yet placed before anything is saved", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [phased()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    const f = within(reviseForm());
    await user.click(f.getByRole("button", { name: "Remove phase 2" }));
    expect(f.queryByLabelText("Phase 2 name")).not.toBeInTheDocument();
    expect(f.getByLabelText("FILL-1 Filling")).toHaveValue("");
    await user.type(f.getByLabelText("Why is this plan being revised?"), "Merge into one visit");
    await user.click(f.getByRole("button", { name: "Save revision" }));
    const [post] = posts(api);
    expect(post.body?.phases).toHaveLength(1);
    expect(post.body?.placements).toEqual([{ itemId: "it1", phaseNumber: 1 }, { itemId: "it2", phaseNumber: null }, { itemId: "it3", phaseNumber: null }]);
  });

  it("names a blank phase beside its field before sending", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [phased()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    const f = within(reviseForm());
    await user.clear(f.getByLabelText("Phase 1 name"));
    await user.type(f.getByLabelText("Why is this plan being revised?"), "Rename");
    await user.click(f.getByRole("button", { name: "Save revision" }));
    expect(f.getAllByText("Name the phase.").length).toBeGreaterThan(0);
    expect(f.getByLabelText(/^Phase 1 name/)).toHaveAttribute("aria-invalid", "true");
    expect(posts(api)).toHaveLength(0);
  });

  it("shows the server's refusal beside the field it is about and keeps what was typed", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [phased()], respond: { "/revise": () => json({ error: "validation_failed", message: "no", fieldErrors: { "phases[1].targetDate": "Choose a date between 2000 and ten years from now." } }, 400) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    const f = within(reviseForm());
    await user.type(f.getByLabelText("Why is this plan being revised?"), "Dates");
    await user.click(f.getByRole("button", { name: "Save revision" }));
    expect((await f.findAllByText("Choose a date between 2000 and ten years from now.")).length).toBeGreaterThan(0);
    expect(f.getByLabelText("Why is this plan being revised?")).toHaveValue("Dates");
    expect(f.getByLabelText("Phase 2 name")).toHaveValue("After healing");
  });

  it("says nothing changed when the server saves no new revision, and shows the conflict banner for a stale one", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [phased()], respond: { "/revise": () => json(phased()) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    await user.type(within(reviseForm()).getByLabelText("Why is this plan being revised?"), "No change");
    await user.click(within(reviseForm()).getByRole("button", { name: "Save revision" }));
    expect(await screen.findByText("Nothing changed, so no new revision was saved.")).toBeInTheDocument();
    vi.unstubAllGlobals();

    stubApi({ plans: [phased()], respond: { "/revise": () => json({ error: "concurrency_conflict", message: "stale" }, 409) } });
    document.body.innerHTML = "";
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    await user.type(within(reviseForm()).getByLabelText("Why is this plan being revised?"), "Stale");
    await user.click(within(reviseForm()).getByRole("button", { name: "Save revision" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(within(reviseForm()).getByLabelText("Why is this plan being revised?")).toHaveValue("Stale");
  });

  it("has no accessibility violations with the revise form open", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [phased()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Revise plan" }));
    expect(await axe(document.body)).toHaveNoViolations();
  });
});

describe("the reference code", () => {
  it("is offered only while the plan has none, and is sent with the plan's version", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan()], respond: { "/reference-code": () => json(plan({ referenceCode: "PLAN-7" })) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Set reference code" }));
    const f = within(screen.getByRole("form", { name: "Set the reference code of this treatment plan" }));
    await user.click(f.getByRole("button", { name: "Set reference code" }));
    expect(f.getByRole("alert")).toHaveTextContent("Not saved: enter the reference code.");
    expect(posts(api)).toHaveLength(0);
    await user.type(f.getByLabelText("Reference code"), "  PLAN-7 ");
    await user.click(f.getByRole("button", { name: "Set reference code" }));
    expect(posts(api)[0].body).toEqual({ rowVersion: "R1", code: "PLAN-7" });
    expect(await screen.findByText("Reference code set.")).toBeInTheDocument();
  });

  it("is not offered once set, and a refusal says why and keeps the code typed", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan({ referenceCode: "PLAN-7" })] });
    await open(true);
    expect(screen.queryByRole("button", { name: "Set reference code" })).not.toBeInTheDocument();
    vi.unstubAllGlobals();

    stubApi({ plans: [plan()], respond: { "/reference-code": () => json({ error: "reference_code_in_use", message: "Another treatment plan of this patient already has that reference code." }, 409) } });
    document.body.innerHTML = "";
    await open(true);
    await user.click(screen.getByRole("button", { name: "Set reference code" }));
    const f = within(screen.getByRole("form", { name: "Set the reference code of this treatment plan" }));
    await user.type(f.getByLabelText("Reference code"), "PLAN-7");
    await user.click(f.getByRole("button", { name: "Set reference code" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Not saved: Another treatment plan of this patient already has that reference code.");
    expect(f.getByLabelText("Reference code")).toHaveValue("PLAN-7");
  });
});
