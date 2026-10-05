import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";
import type { PerioReading } from "./services/perioApi";

/**
 * ALV-012-C01: the comparison of two charts, the charts on record with their new measures, tooth records and links, and the live comparison of the chart in progress - through the real <App /> against
 * the in-memory fake of the API. The arithmetic is proven by the backend tests (PerioComparisonTests) with the same fixture worked out by hand; what is proven here is that the screen shows what the API
 * returned in words (never colour alone), counts unmatched sites separately, says plainly when there is nothing to compare or the comparison could not load, and never calls a cue a diagnosis.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;

const r = (toothKey: string, site: string, pd: number, rec = 0, bleeding = false, suppuration: boolean | null = null, plaque: boolean | null = null): PerioReading =>
  ({ toothKey, site, probingDepthMm: pd, recessionMm: rec, attachmentLossMm: pd + rec, bleeding, suppuration, plaque });

// the fixture worked out by hand in PerioComparisonTests: 4 sites in both (one improved, one worse, two about the same), one only before (11 B), one only now (47 B)
function seed() {
  const earlier = server.perio.addChart(A, {
    recordedAtUtc: "2026-06-01T10:00:00Z", recordedByName: "Hana Hygienist",
    readings: [r("16", "B", 5, 1, true), r("16", "MB", 3), r("26", "DL", 7, 2, true, true), r("26", "L", 2, 0, false, null, true), r("11", "B", 3)],
    teeth: [{ toothKey: "16", mobility: 1, furcation: null, excluded: false }, { toothKey: "26", mobility: null, furcation: 1, excluded: false }],
  });
  const current = server.perio.addChart(A, {
    recordedAtUtc: "2026-09-01T10:00:00Z",
    readings: [r("16", "B", 3, 1), r("16", "MB", 4), r("26", "DL", 9, 2, true), r("26", "L", 2, 0, false, null, false), r("47", "B", 4, 0, true)],
    teeth: [{ toothKey: "16", mobility: 2, furcation: null, excluded: false }, { toothKey: "26", mobility: null, furcation: 1, excluded: false }, { toothKey: "38", mobility: null, furcation: null, excluded: true }],
  });
  return { earlier, current };
}

async function openHistory() {
  window.history.pushState({}, "", `/patients/${A}/periodontal`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Periodontal chart" });
  await within(history()).findAllByRole("group");
  return view;
}
const history = () => screen.getByRole("region", { name: "Charts on record" });
const newest = () => within(history()).getAllByRole("group")[0];
const compareButton = () => within(newest()).getByRole("button", { name: "Compare with the previous chart" });
const panel = () => within(newest()).getByRole("region", { name: /^Comparison:/ });
async function openComparison(user: ReturnType<typeof userEvent.setup>) {
  await user.click(compareButton());
  await within(newest()).findByRole("region", { name: /^Comparison:/ });
}

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("comparing a saved chart with the one before it", () => {
  it("only charts that have an earlier one offer the comparison", async () => {
    seed();
    await openHistory();
    const groups = within(history()).getAllByRole("group");
    expect(within(groups[0]).getByRole("button", { name: "Compare with the previous chart" })).toBeInTheDocument();
    expect(within(groups[1]).queryByRole("button", { name: "Compare with the previous chart" })).not.toBeInTheDocument();
  });

  it("says in words how many sites improved, got worse and stayed the same, with the stated 2 mm rule, and counts unmatched sites separately", async () => {
    seed();
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    const trend = within(panel()).getByRole("status");
    expect(trend).toHaveTextContent("Of 4 sites charted both times, 1 improved (2 mm or more shallower), 1 got worse (2 mm or more deeper) and 2 are about the same.");
    expect(trend).toHaveTextContent("The mean change in probing depth at those sites is +0.3 mm.");
    expect(trend).toHaveTextContent("1 site was charted only in the earlier chart and 1 only in this one, so they are not counted above.");
    expect(calls()).toEqual([`/api/patients/${A}/periodontal/comparison`]);
  });

  it("names only the side that has sites charted once, and is silent when every site is in both", async () => {
    server.perio.addChart(A, { recordedAtUtc: "2026-06-01T10:00:00Z", readings: [r("16", "B", 5), r("16", "MB", 3)] });
    server.perio.addChart(A, { recordedAtUtc: "2026-09-01T10:00:00Z", readings: [r("16", "B", 5)] });
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    expect(within(panel()).getByRole("status")).toHaveTextContent("1 site was charted only in the earlier chart, so it is not counted above.");
    expect(within(panel()).getByRole("status")).not.toHaveTextContent("only in this");
  });

  it("says nothing about unmatched sites when every site was charted both times", async () => {
    server.perio.addChart(A, { recordedAtUtc: "2026-06-01T10:00:00Z", readings: [r("16", "B", 5)] });
    server.perio.addChart(A, { recordedAtUtc: "2026-09-01T10:00:00Z", readings: [r("16", "B", 3)] });
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    expect(within(panel()).getByRole("status")).toHaveTextContent("Of 1 site charted both times, 1 improved");
    expect(within(panel()).getByRole("status")).not.toHaveTextContent("not counted above");
  });

  it("shows the whole-chart figures then and now with the plain difference", async () => {
    seed();
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    const table = within(panel()).getByRole("table", { name: /Whole-chart figures/ });
    const row = (measure: string) => [...within(table).getByRole("row", { name: new RegExp(`^${measure.replace(/[()]/g, "\\$&")}`) }).querySelectorAll("th,td")].map((c) => c.textContent);
    expect(row("Sites charted")).toEqual(["Sites charted", "5", "5", "0"]);
    expect(row("Mean probing depth (mm)")).toEqual(["Mean probing depth (mm)", "4.0", "4.4", "+0.4"]);
    expect(row("Mean attachment loss (mm)")).toEqual(["Mean attachment loss (mm)", "4.6", "5.0", "+0.4"]);
    expect(row("Sites that bled on probing (%)")).toEqual(["Sites that bled on probing (%)", "40", "40", "0"]);
    expect(row("Sites 4 mm or deeper")).toEqual(["Sites 4 mm or deeper", "2", "3", "+1"]);
    expect(row("Sites 6 mm or deeper")).toEqual(["Sites 6 mm or deeper", "1", "1", "0"]);
    expect(row("Sites with pus")).toEqual(["Sites with pus", "1", "0", "-1"]);
    expect(row("Sites with plaque (% of those assessed)")).toEqual(["Sites with plaque (% of those assessed)", "100", "0", "-100"]);
  });

  it("lists only the sites that changed at first, each result in words, and every site on request; teeth in the numbering shown", async () => {
    seed();
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    const sites = within(panel()).getByRole("table", { name: /Site by site/ });
    const cells = (row: HTMLElement) => [...row.querySelectorAll("th,td")].map((c) => c.textContent);
    const body = () => within(sites).getAllByRole("row").slice(1).map(cells);
    expect(body()).toEqual([
      ["3", "mid buccal", "5", "3", "-2", "6 to 4", "Yes to No", "Improved", "recession"],                                                          // FDI 16 is Universal 3
      ["8", "mid facial", "3", "-", "-", "3 to -", "No to no reading", "Only in the earlier chart", "-"],                                          // FDI 11 is Universal 8, a front tooth
      ["14", "distal palatal", "7", "9", "+2", "9 to 11", "Yes to Yes", "Worse", "6 mm or deeper, recession, bleeding"],                            // FDI 26 is Universal 14
      ["31", "mid buccal", "-", "4", "-", "- to 4", "no reading to Yes", "Only in this chart", "4 mm or deeper, bleeding"],                          // FDI 47 is Universal 31
    ]);
    await user.click(within(panel()).getByLabelText("Show every site, including those that did not change"));
    expect(body()).toHaveLength(6);
    expect(body().filter((c) => c[7] === "About the same").map((c) => `${c[0]} ${c[1]}`).sort()).toEqual(["14 mid palatal", "3 mesial buccal"]);
  });

  it("lists the teeth whose state or grades changed, in words", async () => {
    seed();
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    const list = within(panel()).getByText("Teeth whose state or grades changed").parentElement!;
    const items = within(list).getAllByRole("listitem").map((li) => li.textContent);
    expect(items).toEqual([
      "Tooth 3: charted both times; mobility 1 then, 2 now.",
      "Tooth 8: charted then, not recorded now.",
      "Tooth 31: not recorded then, charted now.",
      "Tooth 17: not recorded then, excluded now.",
    ]);
  });

  it("says that the marks are not a diagnosis, and hides the comparison again on request", async () => {
    seed();
    const user = userEvent.setup();
    await openHistory();
    await openComparison(user);
    expect(within(panel()).getByText(/They are not a diagnosis and do not replace the clinician's judgement\./)).toBeInTheDocument();
    await user.click(within(newest()).getByRole("button", { name: "Hide the comparison" }));
    expect(within(newest()).queryByRole("region", { name: /^Comparison:/ })).not.toBeInTheDocument();
  });

  it("a comparison that could not load says so and never reads as no change", async () => {
    seed();
    server.failClinical(`GET /api/patients/${A}/periodontal/comparison`, json(500, { error: "server_error", message: "boom" }));
    const user = userEvent.setup();
    await openHistory();
    await user.click(compareButton());
    expect(await within(newest()).findByRole("alert")).toHaveTextContent("Could not load the comparison. Do not assume nothing changed.");
    expect(within(newest()).queryByText(/are about the same/)).not.toBeInTheDocument();
  });

  it("when the server finds nothing earlier to compare with it says that", async () => {
    seed();
    server.failClinical(`GET /api/patients/${A}/periodontal/comparison`, json(404, { error: "no_previous_chart", message: "There is no earlier finalized chart to compare with." }));
    const user = userEvent.setup();
    await openHistory();
    await user.click(compareButton());
    expect(await within(newest()).findByText("There is no earlier finalized chart to compare with.")).toBeInTheDocument();
  });

  it("has no axe violations with the comparison open", async () => {
    seed();
    const user = userEvent.setup();
    const { container } = await openHistory();
    await openComparison(user);
    await user.click(within(panel()).getByLabelText("Show every site, including those that did not change"));
    expect(await axe(container)).toHaveNoViolations();
  });
});

const calls = () => [...new Set(server.callsToClinical("GET", "/periodontal/comparison").map((c) => new URL(c.url, "http://x").pathname))];

describe("the charts on record show the new measures, tooth records and links", () => {
  it("shows pus and plaque columns only for a chart that recorded them, with a blank shown as not assessed, and the tooth records in words", async () => {
    seed();
    await openHistory();
    const groups = within(history()).getAllByRole("group");
    const first = within(groups[0]).getByRole("table", { name: /^Readings in the chart/ });
    expect(within(first).queryByRole("columnheader", { name: "Pus" })).not.toBeInTheDocument();              // nothing recorded pus in that chart
    expect(within(first).getByRole("columnheader", { name: "Plaque" })).toBeInTheDocument();
    expect(within(first).getAllByRole("row").find((x) => x.textContent?.startsWith("14L"))!.textContent).toMatch(/No$/);                // the plaque column: 26 L was assessed and had none
    expect(within(first).getAllByRole("row").find((x) => x.textContent?.startsWith("14DL"))!.textContent).toMatch(/not assessed$/);
    expect(within(first).getAllByRole("row").find((x) => x.textContent?.startsWith("3B"))!.textContent).toContain("not assessed");
    const teeth = within(groups[0]).getByRole("table", { name: "Whole-tooth records in this chart" });
    expect([...within(teeth).getAllByRole("row").map((x) => [...x.querySelectorAll("th,td")].map((c) => c.textContent))]).toEqual([
      ["Tooth", "Mobility (grade)", "Furcation (grade)", "Charted"], ["3", "2", "not assessed", "Yes"], ["14", "not assessed", "1", "Yes"], ["17", "not assessed", "not assessed", "Not charted"],
    ]);
  });

  it("a chart saved before the new measures existed looks exactly as it did", async () => {
    server.perio.addChart(A, { readings: [r("16", "B", 5, 1, true)] });
    await openHistory();
    const table = within(history()).getByRole("table", { name: /^Readings in the chart/ });
    expect(within(table).getAllByRole("columnheader").map((h) => h.textContent)).toEqual(["Tooth", "Site", "Probing depth (mm)", "Recession (mm)", "Attachment loss (mm)", "Bleeding"]);
    expect(within(history()).queryByRole("table", { name: "Whole-tooth records in this chart" })).not.toBeInTheDocument();
  });

  it("lists the links with who made them, and adds a new one by reference which then shows at once", async () => {
    server.perio.addChart(A, { readings: [r("16", "B", 5)], links: [{ linkType: "Diagnosis", reference: "dx-1", linkedByName: "Dr. Okafor", linkedAtUtc: "2026-09-02T10:00:00Z" }] });
    const user = userEvent.setup();
    await openHistory();
    const group = newest();
    expect(within(group).getByRole("list", { name: "Linked records" })).toHaveTextContent("Diagnosis: dx-1 (linked by Dr. Okafor");
    const form = within(group).getByRole("form", { name: "Link this chart" });
    expect(within(form).getByRole("button", { name: "Add link" })).toBeDisabled();                          // nothing typed yet
    await user.selectOptions(within(form).getByLabelText("Link to"), "TreatmentPlan");
    await user.type(within(form).getByLabelText("Reference"), "plan-7");
    await user.click(within(form).getByRole("button", { name: "Add link" }));
    await waitFor(() => expect(within(group).getByRole("list", { name: "Linked records" })).toHaveTextContent("Treatment plan: plan-7"));
    expect(server.callsToClinical("POST", "/links")[0].body).toEqual({ linkType: "TreatmentPlan", reference: "plan-7" });
    expect(within(group).getByRole("status")).toHaveTextContent("Linked.");
  });

  it("a link that fails says so and keeps what was typed; a chart with no links says so; a read-only role sees the links but cannot add", async () => {
    server.perio.addChart(A, { readings: [r("16", "B", 5)] });
    server.failClinical(`POST /api/periodontal/charts`, json(503, { error: "save_failed", message: "The change could not be saved, so nothing was recorded." }));
    const user = userEvent.setup();
    await openHistory();
    expect(within(newest()).getByText("This chart is not linked to a diagnosis, treatment plan, encounter or history entry.")).toBeInTheDocument();
    const form = within(newest()).getByRole("form", { name: "Link this chart" });
    await user.type(within(form).getByLabelText("Reference"), "dx-2");
    await user.click(within(form).getByRole("button", { name: "Add link" }));
    expect(await within(newest()).findByRole("alert")).toHaveTextContent("Not linked: The change could not be saved, so nothing was recorded.");
    expect(within(form).getByLabelText("Reference")).toHaveValue("dx-2");
  });

  it("a read-only role sees the links but has no form to add one", async () => {
    server.permissions = READ_ONLY;
    server.perio.addChart(A, { readings: [r("16", "B", 5)], links: [{ linkType: "Encounter", reference: "enc-3", linkedByName: "Dr. Okafor", linkedAtUtc: "2026-09-02T10:00:00Z" }] });
    await openHistory();
    expect(within(newest()).getByRole("list", { name: "Linked records" })).toHaveTextContent("Encounter: enc-3");
    expect(within(newest()).queryByRole("form", { name: "Link this chart" })).not.toBeInTheDocument();
  });
});

describe("the chart in progress compared with the last finalized chart", () => {
  async function openSteps(user: ReturnType<typeof userEvent.setup>) {
    window.history.pushState({}, "", `/patients/${A}/periodontal`);
    render(<App />);
    await screen.findByRole("heading", { name: "Periodontal chart" });
    await within(history()).findAllByRole("group");
    await user.click(screen.getByRole("button", { name: "Step by step (keyboard)" }));
    await user.click(await screen.findByRole("button", { name: "Start a step-by-step chart" }));
    await screen.findByLabelText("Probing depth (mm)");
  }

  it("is not offered when there is no finalized chart to compare with", async () => {
    const user = userEvent.setup({ delay: null });
    window.history.pushState({}, "", `/patients/${A}/periodontal`);
    render(<App />);
    await screen.findByRole("heading", { name: "Periodontal chart" });
    await user.click(screen.getByRole("button", { name: "Step by step (keyboard)" }));
    await user.click(await screen.findByRole("button", { name: "Start a step-by-step chart" }));
    await screen.findByLabelText("Probing depth (mm)");
    expect(screen.queryByRole("button", { name: "Compare with the last finalized chart" })).not.toBeInTheDocument();
  });

  it("compares the saved sites of the draft with the latest finalized chart, says what is not in it yet, and refreshes when more is saved", async () => {
    seed();
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    // the sweep starts at 18, so give the draft a reading on 16 by saving three teeth's worth: easiest is to put the cursor on 16 with the strip
    await user.click(screen.getByRole("button", { name: /^Tooth 3, upper right first molar/ }));
    await user.keyboard("2{Enter}1{Enter}");                                                  // 16 distal buccal, the first site of the tooth in the order
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(server.perio.sessions[0].readings.size).toBe(1));
    await user.click(screen.getByRole("button", { name: "Compare with the last finalized chart" }));
    const live = await screen.findByRole("region", { name: /^Comparison: The chart in progress with / });
    expect(within(live).getByRole("status")).toHaveTextContent("Of 0 sites charted both times");          // the earlier chart has no 16 DB, so no site matches yet
    expect(within(live).getByRole("status")).toHaveTextContent("5 sites were charted only in the earlier chart and 1 only in this one");
    await user.click(screen.getByLabelText("Probing depth (mm)"));
    await user.keyboard("3{Enter}1{Enter}");                                                  // a second site (16 mid buccal), typed but not saved
    expect(screen.getByText(/1 entered but not saved yet is not in it\. Save what you have entered to include/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(screen.getByRole("region", { name: /^Comparison: The chart in progress with / })).toHaveTextContent("Of 1 site charted both times, 0 improved (2 mm or more shallower), 0 got worse (2 mm or more deeper) and 1 are about the same."));   // the second site typed, 16 mid buccal at 3 mm, is also 3 mm in the September chart
  });

  it("matches a site in the draft with the same site in the last chart and calls it improved when it is 2 mm shallower", async () => {
    seed();
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await user.click(screen.getByRole("button", { name: /^Tooth 3, upper right first molar/ }));
    await user.keyboard("2{Enter}1{Enter}");                                                 // 16 distal buccal (not in the September chart)
    await user.keyboard("1{Enter}0{Enter}");                                                 // 16 mid buccal now 1 mm; in the latest chart (September) it was 3
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(server.perio.sessions[0].readings.size).toBeGreaterThan(0));
    await user.click(screen.getByRole("button", { name: "Compare with the last finalized chart" }));
    const live = await screen.findByRole("region", { name: /^Comparison: The chart in progress with / });
    expect(within(live).getByRole("status")).toHaveTextContent(/Of [1-9] sites? charted both times, 1 improved/);
  });
});
