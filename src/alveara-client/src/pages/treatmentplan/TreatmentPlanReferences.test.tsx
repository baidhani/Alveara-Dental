import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { P, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import type { ReconciliationItem, ReconciliationResult } from "../../services/treatmentPlansApi";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-015-C01: the old diagnosis references panel, against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's behaviour: only people who may
 * change plans see it; reviewing is read-only and says what would be tied and what cannot be tied safely and why; tying matches is a separate act that says what it did; a reference with no safe match offers only
 * this patient's plans to choose from and shows the server's refusal beside the choice; and a failure to check says so instead of showing an empty list.
 */
const match: ReconciliationItem = { diagnosisId: "dx1", diagnosisLabel: "Chronic periodontitis", reference: "plan-7", outcome: "Eligible", planId: "pl1", planTitle: "Gum health plan", method: "ReferenceCode", reason: null };
const nomatch: ReconciliationItem = { diagnosisId: "dx2", diagnosisLabel: "Caries", reference: "old note", outcome: "NoMatch", planId: null, planTitle: null, method: null,
  reason: "No plan of this patient has this text as its reference code or id. It stays unresolved." };
const ambiguous: ReconciliationItem = { ...nomatch, diagnosisId: "dx3", diagnosisLabel: "Gingivitis", reference: "dup", outcome: "Ambiguous", reason: "More than one plan of this patient matches this text. It stays unresolved; choose the plan yourself." };
const result = (over: Partial<ReconciliationResult> = {}): ReconciliationResult => ({ patientId: P, applied: false, matched: 1, alreadyResolved: 0, unresolved: 2, items: [match, nomatch, ambiguous], ...over });
const applied = () => result({ applied: true, unresolved: 2, items: [{ ...match, outcome: "Resolved" }, nomatch, ambiguous] });
const respondReconcile = () => ({ "/reconcile-references": (b: Record<string, unknown> | null) => json(b?.apply ? applied() : result()) });
const posts = (api: Api) => api.calls.filter((c) => c.method === "POST");
const open = async (canWrite = true) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};
const section = () => screen.getByRole("region", { name: "Old diagnosis references" });

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("reviewing the old references", () => {
  it("is offered only to people who may change plans", async () => {
    stubApi({ plans: [plan()] });
    await open(false);
    expect(screen.queryByRole("region", { name: "Old diagnosis references" })).not.toBeInTheDocument();
  });

  it("reviews without changing anything, saying what would be tied and what stays unresolved and why", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan({ referenceCode: "PLAN-7" })], respond: respondReconcile() });
    await open();
    expect(posts(api)).toHaveLength(0);
    await user.click(within(section()).getByRole("button", { name: "Review old diagnosis references" }));
    const list = await within(section()).findByRole("list", { name: "Old diagnosis references" });
    expect(posts(api)[0].url).toContain(`/api/patients/${P}/treatment-plans/reconcile-references`);
    expect(posts(api)[0].body).toEqual({ apply: false });
    const items = within(list).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Chronic periodontitis");
    expect(items[0]).toHaveTextContent("Reference: plan-7");
    expect(items[0]).toHaveTextContent("Can be tied to “Gum health plan”.");
    expect(items[1]).toHaveTextContent("No matching plan. It stays unresolved.");
    expect(items[2]).toHaveTextContent("More than one plan matches. It stays unresolved.");
    expect(within(section()).getByRole("button", { name: "Tie 1 matching reference to its plan" })).toBeInTheDocument();
  });

  it("ties the matches only when asked, says how many, and then offers no second tie", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan({ referenceCode: "PLAN-7" })], respond: respondReconcile() });
    await open();
    await user.click(within(section()).getByRole("button", { name: "Review old diagnosis references" }));
    await user.click(await within(section()).findByRole("button", { name: "Tie 1 matching reference to its plan" }));
    expect(posts(api)[1].body).toEqual({ apply: true });
    expect(await within(section()).findByText("1 reference tied to plans. Each diagnosis keeps its original text.")).toBeInTheDocument();
    expect(within(section()).getAllByRole("listitem")[0]).toHaveTextContent("Tied to “Gum health plan”.");
    expect(within(section()).queryByRole("button", { name: /Tie \d+ matching/ })).not.toBeInTheDocument();
  });

  it("says there are none when no diagnosis has an old reference, and says so when it cannot check", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan()], respond: { "/reconcile-references": () => json(result({ matched: 0, unresolved: 0, items: [] })) } });
    await open();
    await user.click(within(section()).getByRole("button", { name: "Review old diagnosis references" }));
    expect(await within(section()).findByText("No diagnosis of this patient has an old treatment-plan reference.")).toBeInTheDocument();
    vi.unstubAllGlobals();

    stubApi({ plans: [plan()], respond: { "/reconcile-references": () => json({ error: "server_error", message: "boom" }, 500) } });
    document.body.innerHTML = "";
    await open();
    await user.click(within(section()).getByRole("button", { name: "Review old diagnosis references" }));
    expect(await within(section()).findByRole("alert")).toHaveTextContent("Could not check the references: boom");
    expect(within(section()).queryByRole("list", { name: "Old diagnosis references" })).not.toBeInTheDocument();
  });
});

describe("choosing the plan for a reference that has no safe match", () => {
  const review = async (respond: Record<string, (b: Record<string, unknown> | null) => Response> = {}) => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan({ referenceCode: "PLAN-7" }), plan({ id: "pl2", title: "Older plan", status: "Withdrawn", withdrawnReason: "x", withdrawnAtUtc: "2030-01-01T00:00:00Z", withdrawnByName: "Dr. Okafor" })], respond: { ...respondReconcile(), ...respond } });
    await open();
    await user.click(within(section()).getByRole("button", { name: "Review old diagnosis references" }));
    await within(section()).findByRole("list", { name: "Old diagnosis references" });
    return { user, api };
  };

  it("offers only this patient's plans, marks a withdrawn one, and offers nothing for a reference that already matches", async () => {
    await review();
    expect(screen.queryByRole("form", { name: "Choose the plan for Chronic periodontitis" })).not.toBeInTheDocument();
    const choose = within(screen.getByRole("form", { name: "Choose the plan for Caries" }));
    const options = choose.getAllByRole("option").map((o) => o.textContent);
    expect(options).toEqual(["Choose a plan", "Gum health plan (PLAN-7)", "Older plan - withdrawn"]);
  });

  it("will not tie without a chosen plan, then ties and shows the result in place", async () => {
    const { user, api } = await review({ "/resolve-plan-reference": () => json({ ...nomatch, outcome: "Resolved", planId: "pl1", planTitle: "Gum health plan", method: "Chosen", reason: null }) });
    const choose = within(screen.getByRole("form", { name: "Choose the plan for Caries" }));
    await user.click(choose.getByRole("button", { name: "Tie to this plan" }));
    expect(choose.getByRole("alert")).toHaveTextContent("Not tied: choose the plan this reference belongs to.");
    expect(posts(api)).toHaveLength(1);                                                                    // only the review has been sent
    await user.selectOptions(choose.getByLabelText(/^Plan for/), "pl1");
    await user.click(choose.getByRole("button", { name: "Tie to this plan" }));
    expect(posts(api)[1].url).toContain("/api/diagnoses/dx2/resolve-plan-reference");
    expect(posts(api)[1].body).toEqual({ planId: "pl1" });
    expect(await screen.findByText("Tied to “Gum health plan”.")).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Choose the plan for Caries" })).not.toBeInTheDocument();
  });

  it("shows the server's refusal beside the choice and keeps the plan chosen", async () => {
    const { user } = await review({ "/resolve-plan-reference": () => json({ error: "already_resolved", message: "This diagnosis's reference has already been tied to a different treatment plan, and that is final." }, 409) });
    const choose = within(screen.getByRole("form", { name: "Choose the plan for Caries" }));
    await user.selectOptions(choose.getByLabelText(/^Plan for/), "pl2");
    await user.click(choose.getByRole("button", { name: "Tie to this plan" }));
    expect(await choose.findByRole("alert")).toHaveTextContent("Not tied: This diagnosis's reference has already been tied to a different treatment plan, and that is final.");
    expect(choose.getByLabelText(/^Plan for/)).toHaveValue("pl2");
  });

  it("has no accessibility violations", async () => {
    await review();
    expect(await axe(document.body)).toHaveNoViolations();
  });
});
