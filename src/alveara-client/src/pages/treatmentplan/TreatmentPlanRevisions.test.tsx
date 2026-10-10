import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import type { RevisionComparison, RevisionDetail, RevisionItem, RevisionSummary } from "../../services/treatmentPlansApi";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-015-C01: the revision history and comparison, against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's behaviour: nothing is
 * requested until the history is opened, every revision is listed with what produced it, who made it, when, why and what the plan came to, any one opens exactly as saved, two are compared with every difference
 * named and the money change signed, and a failure to load says so instead of showing an empty list. Readers may use all of it.
 */
const LABEL = "Practice fee estimate: the practice's catalog fee when each item was proposed. It is not an insurance estimate and not a guaranteed patient cost.";
const summary = (n: number, over: Partial<RevisionSummary> = {}): RevisionSummary => ({
  revisionNumber: n, changeType: "Revised", title: "Gum health plan", reason: null, actorName: "Dr. Okafor", createdAtUtc: "2030-01-0" + n + "T10:00:00Z", procedureCount: 2, estimateTotal: 160, phaseCount: 0, ...over,
});
const revisionItem = (id: string, code: string, over: Partial<RevisionItem> = {}): RevisionItem => ({
  itemId: id, itemNumber: 1, procedureCode: code, procedureDescription: `${code} work`, diagnosisLabel: "Chronic periodontitis", toothKey: null, surface: null, fee: 100, isWithdrawn: false, phaseNumber: null, ...over,
});
const three = [summary(1, { changeType: "Created", procedureCount: 1, estimateTotal: 100 }), summary(2, { changeType: "ItemAdded", procedureCount: 2, estimateTotal: 160 }),
  summary(3, { changeType: "Revised", reason: "Phased for healing", phaseCount: 2, estimateTotal: 160 })];
const detail = (n: number): RevisionDetail => ({
  summary: three[n - 1], clinicalRationale: n === 3 ? "Deep pockets" : null, patientExplanation: null, estimateLabel: LABEL,
  phases: n === 3 ? [{ phaseNumber: 1, name: "First visit", plannedVisit: "Today", targetDate: null }, { phaseNumber: 2, name: "After healing", plannedVisit: null, targetDate: null }] : [],
  items: [revisionItem("a", "PERIO-1", { phaseNumber: n === 3 ? 1 : null }), revisionItem("b", "FILL-1", { fee: 60, phaseNumber: n === 3 ? 2 : null, isWithdrawn: n === 3 })],
});
const comparison = (over: Partial<RevisionComparison> = {}): RevisionComparison => ({
  from: three[1], to: three[2], title: null, clinicalRationale: { from: null, to: "Deep pockets" }, patientExplanation: null,
  phasesAdded: [{ phaseNumber: 1, name: "First visit", plannedVisit: "Today", targetDate: null }], phasesRemoved: [], phasesChanged: [],
  itemsAdded: [], itemsWithdrawn: [revisionItem("b", "FILL-1", { fee: 60 })], itemsMoved: [{ item: revisionItem("a", "PERIO-1", { phaseNumber: 1 }), fromPhase: null, toPhase: 1 }],
  estimateFrom: 160, estimateTo: 100, estimateDelta: -60, estimateLabel: LABEL, ...over,
});
const handlers = (over: Record<string, (b: Record<string, unknown> | null) => Response> = {}) => ({
  "/revisions/compare": () => json(comparison()), "/revisions/1": () => json(detail(1)), "/revisions/3": () => json(detail(3)), "/revisions": () => json(three), ...over,
});
const gets = (api: Api, fragment: string) => api.calls.filter((c) => c.method === "GET" && c.url.includes(fragment));
const open = async (canWrite = false) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the revision history", () => {
  it("asks for nothing until it is opened, then lists every revision with what produced it, who, when, why and what the plan came to", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers() });
    await open();
    expect(gets(api, "/revisions")).toHaveLength(0);
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    const list = await screen.findByRole("list", { name: "Revisions of Gum health plan" });
    const items = within(list).getAllByRole("listitem");
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent("Revision 1: Plan created");
    expect(items[1]).toHaveTextContent("Revision 2: Procedure added");
    expect(items[2]).toHaveTextContent("Revision 3");
    expect(items[2]).toHaveTextContent("Current");
    expect(items[2]).toHaveTextContent("Plan revised");
    expect(items[2]).toHaveTextContent("Dr. Okafor");
    expect(items[2]).toHaveTextContent("Reason: Phased for healing");
    expect(items[2]).toHaveTextContent("2 procedures, $160.00, 2 phases");
    expect(items[0]).toHaveTextContent("1 procedure, $100.00, 0 phases");
    await user.click(screen.getByRole("button", { name: "Hide revision history" }));
    expect(screen.queryByRole("list", { name: "Revisions of Gum health plan" })).not.toBeInTheDocument();
  });

  it("opens any revision exactly as it was saved, grouped by that revision's phases, with the estimate it came to then", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers() });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    await user.click(await screen.findByRole("button", { name: "View revision 3" }));
    const view = await screen.findByRole("region", { name: "Revision 3 as saved" });
    expect(view).toHaveTextContent("Revision 3 as saved: Gum health plan");
    expect(view).toHaveTextContent("Reason: Phased for healing");
    expect(view).toHaveTextContent("Clinical rationale: Deep pockets");
    expect(view).toHaveTextContent("Phase 1: First visit");
    expect(view).toHaveTextContent("Phase 2: After healing");
    expect(view).toHaveTextContent("Withdrawn by then");
    expect(view).toHaveTextContent("Estimate then: $160.00 for 2 procedures.");
    expect(view).toHaveTextContent("not an insurance estimate");

    await userEvent.setup().click(screen.getByRole("button", { name: "View revision 1" }));
    const first = await screen.findByRole("region", { name: "Revision 1 as saved" });
    expect(first).not.toHaveTextContent("Phase 1");                                                    // an earlier revision with no phases shows none
    expect(first).not.toHaveTextContent("Withdrawn by then");
  });
});

describe("comparing two revisions", () => {
  it("compares the previous revision with the current one by default and names every difference with the money change signed", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers() });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    const form = within(await screen.findByRole("form", { name: "Compare two revisions" }));
    expect(form.getByLabelText("Compare revision")).toHaveValue("2");
    expect(form.getByLabelText("with revision")).toHaveValue("3");
    await user.click(form.getByRole("button", { name: "Compare" }));

    expect(gets(api, "/revisions/compare")[0].url).toContain("from=2&to=3");
    const c = await screen.findByRole("region", { name: "Changes from revision 2 to revision 3" });
    expect(c).toHaveTextContent("Clinical rationale changed. Before: (none) After: Deep pockets");
    expect(c).toHaveTextContent("Phases added: Phase 1: First visit");
    expect(c).toHaveTextContent("Procedures withdrawn: FILL-1 FILL-1 work ($60.00)");
    expect(c).toHaveTextContent("PERIO-1 moved from not placed to phase 1.");
    expect(c).toHaveTextContent("Estimate: $160.00 then, $100.00 now (-$60.00)");
    expect(c).toHaveTextContent("not an insurance estimate");
    expect(c).not.toHaveTextContent("Title changed");                                                  // only what differs is named
  });

  it("compares the two revisions the person picks", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers({ "/revisions/compare": () => json(comparison({ from: three[0], title: { from: "Old title", to: "Gum health plan" } })) }) });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    const form = within(await screen.findByRole("form", { name: "Compare two revisions" }));
    await user.selectOptions(form.getByLabelText("Compare revision"), "1");
    await user.click(form.getByRole("button", { name: "Compare" }));
    expect(gets(api, "/revisions/compare")[0].url).toContain("from=1&to=3");
    expect(await screen.findByRole("region", { name: "Changes from revision 1 to revision 3" })).toHaveTextContent("Title changed. Before: Old title After: Gum health plan");
  });

  it("says when two revisions do not differ, and offers no comparison when there is only one revision", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers({ "/revisions/compare": () => json(comparison({ clinicalRationale: null, phasesAdded: [], itemsWithdrawn: [], itemsMoved: [], estimateFrom: 160, estimateTo: 160, estimateDelta: 0 })) }) });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    await user.click(within(await screen.findByRole("form", { name: "Compare two revisions" })).getByRole("button", { name: "Compare" }));
    const c = await screen.findByRole("region", { name: /Changes from revision/ });
    expect(c).toHaveTextContent("No differences in the title, words, phases or procedures.");
    expect(c).toHaveTextContent("(no change)");
    vi.unstubAllGlobals();

    stubApi({ plans: [plan()], respond: handlers({ "/revisions": () => json([three[0]]) }) });
    document.body.innerHTML = "";
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    await screen.findByRole("list", { name: "Revisions of Gum health plan" });
    expect(screen.queryByRole("form", { name: "Compare two revisions" })).not.toBeInTheDocument();
  });
});

describe("when it cannot load", () => {
  it("says it could not load the history rather than showing an empty list", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan()], respond: handlers({ "/revisions": () => json({ error: "server_error", message: "boom" }, 500) }) });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    expect(await screen.findByText(/Could not load the revision history\. Do not assume there is none/)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: /Revisions of/ })).not.toBeInTheDocument();
  });

  it("says so when one revision or a comparison cannot be loaded, and leaves the screen as it was", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers({ "/revisions/compare": () => json({ error: "server_error", message: "boom" }, 500) }) });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    await user.click(within(await screen.findByRole("form", { name: "Compare two revisions" })).getByRole("button", { name: "Compare" }));
    expect(await screen.findByText("Could not load that. Nothing on screen has changed; try again.")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Revisions of Gum health plan" })).toBeInTheDocument();
  });

  it("has no accessibility violations with the history, a revision and a comparison open", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [plan({ currentRevision: 3 })], respond: handlers() });
    await open();
    await user.click(screen.getByRole("button", { name: "Show revision history" }));
    await user.click(await screen.findByRole("button", { name: "View revision 3" }));
    await user.click(within(screen.getByRole("form", { name: "Compare two revisions" })).getByRole("button", { name: "Compare" }));
    await screen.findByRole("region", { name: "Changes from revision 2 to revision 3" });
    expect(await axe(document.body)).toHaveNoViolations();
  });
});
