import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";
import { LOWER_PERMANENT, UPPER_PERMANENT } from "./pages/odontogram/toothNumbering";
import { EMPTY_CELL, cellId, readingsFromDraft } from "./pages/perio/perioChartRules";
import { summarize } from "./pages/perio/perioSummary";
import type { PerioReading } from "./services/perioApi";
import type { Draft } from "./pages/perio/perioChartRules";

/**
 * STORY-012: the periodontal chart screen, through the real <App /> against an in-memory fake of the charting API. The rules are proven by the backend tests and the real-backend
 * walkthrough; what is proven here is the UI's behaviour for each outcome: entries sent as the FDI key with whole millimetres, a missing value reported and never defaulted, nothing sent
 * while an entry is wrong, the server's refusal pointing at the exact entry, a retry of an unchanged chart keeping its key while any change gets a new one, a dropped connection keeping
 * what was typed, and a role that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
const TEETH = [...UPPER_PERMANENT, ...LOWER_PERMANENT];
let server: FakeClinicalServer;

async function openChart() {
  window.history.pushState({}, "", `/patients/${A}/periodontal`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Periodontal chart" });
  return view;
}
// Universal numbering is shown, so Universal 3 is FDI 16
const entry = (what: "Probing depth in millimetres" | "Recession in millimetres" | "Bleeding on probing", tooth = "3", site = "mid buccal") =>
  screen.getByRole(what === "Bleeding on probing" ? "checkbox" : "textbox", { name: `${what}, tooth ${tooth}, ${site}` });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;
const saves = () => server.callsToClinical("POST", "/periodontal/charts");
const save = (user: ReturnType<typeof userEvent.setup>) => user.click(screen.getByRole("button", { name: "Save chart" }));

async function chartSite(user: ReturnType<typeof userEvent.setup>, depth: string, recession: string, bleeding = false, tooth = "3", site = "mid buccal") {
  await user.type(entry("Probing depth in millimetres", tooth, site), depth);
  await user.type(entry("Recession in millimetres", tooth, site), recession);
  if (bleeding) await user.click(entry("Bleeding on probing", tooth, site));
}

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("the draft's rules (the same ones the server applies)", () => {
  const draft = (cells: Record<string, Partial<typeof EMPTY_CELL>>): Draft => Object.fromEntries(Object.entries(cells).map(([k, v]) => [k, { ...EMPTY_CELL, ...v }]));

  it("sends only the sites that were touched, in tooth then site order, as numbers", () => {
    const { readings, problems } = readingsFromDraft(draft({ [cellId("11", "MB")]: { pd: "4", rec: "0" }, [cellId("18", "B")]: { pd: "3", rec: "1", bleeding: true } }), TEETH);
    expect(problems).toEqual([]);
    expect(readings).toEqual([
      { toothKey: "18", site: "B", probingDepthMm: 3, recessionMm: 1, bleeding: true },
      { toothKey: "11", site: "MB", probingDepthMm: 4, recessionMm: 0, bleeding: false },
    ]);
  });

  it("lists every problem together and sends nothing when any entry is wrong", () => {
    const { readings, problems } = readingsFromDraft(draft({
      [cellId("16", "B")]: { pd: "99", rec: "1" }, [cellId("16", "MB")]: { pd: "", rec: "2" }, [cellId("17", "B")]: { pd: "3", rec: "" }, [cellId("17", "L")]: { pd: "x", rec: "-1" }, [cellId("26", "B")]: { pd: "3", rec: "1" },
    }), TEETH);
    expect(readings).toEqual([]);                                                       // the valid site is not sent on its own
    expect(problems.map((p) => `${p.toothKey}${p.site}:${p.field}`)).toEqual(["17B:rec", "17L:pd", "17L:rec", "16B:pd", "16MB:pd"]);   // in the order the grid is read: 17 comes before 16 across the upper arch
  });

  it.each([["0", true], ["15", true], ["7", true], ["16", false], ["-1", false], ["1.5", false], ["", false], ["a", false], [" 4 ", true]])("%j as a depth is %s", (text, ok) => {
    const { problems } = readingsFromDraft(draft({ [cellId("16", "B")]: { pd: text, rec: "0", bleeding: text === "" } }), TEETH);
    expect(problems.length === 0).toBe(ok);
  });

  it("a site with only the bleeding box ticked is incomplete (bleeding alone is not a reading)", () => {
    expect(readingsFromDraft(draft({ [cellId("16", "B")]: { bleeding: true } }), TEETH).problems.map((p) => p.field)).toEqual(["pd", "rec"]);
  });
});

describe("charting", () => {
  it("shows the whole mouth with every site's boxes, labelled by tooth and site, and nothing pre-filled", async () => {
    await openChart();
    expect(screen.getAllByRole("table")).toHaveLength(2);
    expect(screen.getAllByRole("textbox")).toHaveLength(32 * 6 * 2);
    expect(screen.getAllByRole("checkbox")).toHaveLength(32 * 6);
    expect(screen.getAllByRole("textbox").every((t) => (t as HTMLInputElement).value === "")).toBe(true);
    expect(status()).toHaveTextContent("0 sites entered. Nothing is saved until you save the chart.");
  });

  it("saves depth, recession and bleeding as numbers on the FDI key whatever numbering is shown, with a key, and says it was saved", async () => {
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "5", "2", true);                                    // Universal 3 = FDI 16, mid buccal
    await chartSite(user, "3", "0", false, "3", "mesial buccal");
    expect(status()).toHaveTextContent("2 sites entered");
    await save(user);

    await waitFor(() => expect(status()).toHaveTextContent("Chart saved with 2 sites."));
    const [call] = saves();
    expect(call.body).toMatchObject({
      readings: [{ toothKey: "16", site: "B", probingDepthMm: 5, recessionMm: 2, bleeding: true }, { toothKey: "16", site: "MB", probingDepthMm: 3, recessionMm: 0, bleeding: false }],
    });
    expect(String((call.body as { idempotencyKey: string }).idempotencyKey).length).toBeGreaterThan(8);
    expect(server.perio.charts).toHaveLength(1);
    const listed = within(screen.getByRole("region", { name: "Charts on record" })).getAllByRole("group");
    expect(listed).toHaveLength(1);                                           // the chart just saved is on record straight away
    expect(listed[0]).toHaveTextContent("by Dr. Okafor — 2 sites on 1 tooth, 50% bleeding (latest)");
  });

  it("Enter moves to the next box, and Shift+Enter back", async () => {
    const user = userEvent.setup();
    await openChart();
    entry("Probing depth in millimetres").focus();
    await user.keyboard("{Enter}");
    expect(entry("Recession in millimetres")).toHaveFocus();
    await user.keyboard("{Shift>}{Enter}{/Shift}");
    expect(entry("Probing depth in millimetres")).toHaveFocus();
  });
});

describe("incorrect data (acceptance 2)", () => {
  it("sends nothing while an entry is wrong, lists every problem, marks each box and moves focus to the list", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.type(entry("Probing depth in millimetres"), "99");
    await user.type(entry("Recession in millimetres"), "1");
    await user.type(entry("Probing depth in millimetres", "3", "mesial buccal"), "4");     // no recession
    await save(user);

    const alert = await screen.findByRole("alert");
    expect(within(alert).getAllByRole("listitem")).toHaveLength(2);
    expect(alert).toHaveTextContent("Tooth 3, mid buccal: Probing depth must be a whole number from 0 to 15 mm.");
    expect(alert).toHaveTextContent("Tooth 3, mesial buccal: Enter the recession for this site, or 0 if there is none.");
    expect(status()).toHaveTextContent("Not saved: some entries need correcting. Nothing was sent.");
    expect(saves()).toHaveLength(0);
    expect(entry("Probing depth in millimetres")).toHaveAttribute("aria-invalid", "true");
    expect(entry("Probing depth in millimetres")).toHaveAccessibleDescription("Probing depth must be a whole number from 0 to 15 mm.");
    await waitFor(() => expect(alert).toHaveFocus());

    await user.click(within(alert).getByRole("button", { name: "Tooth 3, mesial buccal" }));
    expect(entry("Recession in millimetres", "3", "mesial buccal")).toHaveFocus();
  });

  it("an empty chart is not sent", async () => {
    const user = userEvent.setup();
    await openChart();
    await save(user);
    expect(await screen.findByRole("alert")).toHaveTextContent("Enter at least one site before saving.");
    expect(saves()).toHaveLength(0);
  });

  it("when the server refuses it says what to correct, marks the exact boxes, and keeps everything typed", async () => {
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "5", "2");
    server.failClinical(`POST /api/patients/${A}/periodontal/charts`, json(400, {
      error: "validation_failed", message: "The chart has entries that need correcting. Nothing was saved.",
      problems: [{ toothKey: "16", site: "B", field: "probingDepthMm", code: "out_of_range", message: "Probing depth at tooth 16 site B is 5 mm; it must be a whole number from 0 to 15 mm." }],
    }));
    await save(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Probing depth at tooth 16 site B is 5 mm");
    expect(entry("Probing depth in millimetres")).toHaveAttribute("aria-invalid", "true");
    expect(entry("Probing depth in millimetres")).toHaveValue("5");
    expect(entry("Recession in millimetres")).toHaveValue("2");
    expect(server.perio.charts).toHaveLength(0);
  });
});

describe("a save that fails, and retries", () => {
  it("a dropped connection says so, keeps what was typed, and the retry keeps the same key and makes one chart", async () => {
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "4", "1", true);
    server.dropNextResponse("POST /periodontal/charts");                          // the server stores the chart, the answer is lost
    await save(user);
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: the connection dropped. What you entered is still here; save again to retry."));
    expect(entry("Probing depth in millimetres")).toHaveValue("4");

    await save(user);
    await waitFor(() => expect(status()).toHaveTextContent("Chart saved with 1 site."));
    const [first, second] = saves().map((c) => (c.body as { idempotencyKey: string }).idempotencyKey);
    expect(first).toBe(second);
    expect(server.perio.charts).toHaveLength(1);
  });

  it("a failed save (500) says nothing was recorded and keeps the entries; changing a value then saving records a new chart under a new key", async () => {
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "4", "1");
    server.failClinical(`POST /api/patients/${A}/periodontal/charts`, json(503, { error: "save_failed", message: "The chart could not be saved, so nothing was recorded. Try again." }));
    await save(user);
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: The chart could not be saved, so nothing was recorded. Try again."));
    expect(server.perio.charts).toHaveLength(0);

    await save(user);
    await waitFor(() => expect(status()).toHaveTextContent("Chart saved"));
    await user.clear(entry("Probing depth in millimetres"));
    await user.type(entry("Probing depth in millimetres"), "6");
    await save(user);
    await waitFor(() => expect(server.perio.charts).toHaveLength(2));
    const keys = saves().map((c) => (c.body as { idempotencyKey: string }).idempotencyKey);
    expect(new Set(keys).size).toBe(2);                                              // the unchanged retry reused its key; the changed chart got a new one
    expect(keys[0]).toBe(keys[1]);
  });
});

describe("a role that may only read", () => {
  it("shows the grid but disables every box and offers no save", async () => {
    server.permissions = READ_ONLY;
    await openChart();
    expect(screen.queryByRole("button", { name: "Save chart" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("textbox").every((t) => (t as HTMLInputElement).disabled)).toBe(true);
    expect(status()).toHaveTextContent("You can read this but your role cannot chart.");
  });

  it("is not reachable at all without permission to view clinical documentation", async () => {
    server.permissions = ["ViewPatientRecords"];
    window.history.pushState({}, "", `/patients/${A}/periodontal`);
    render(<App />);
    await waitFor(() => expect(screen.queryByRole("heading", { name: "Periodontal chart" })).not.toBeInTheDocument());
    expect(server.callsToClinical("GET", "/periodontal")).toHaveLength(0);
  });
});

describe("accessibility", () => {
  it("has no axe violations on the grid, and none while problems are shown", async () => {
    const user = userEvent.setup();
    const { container } = await openChart();
    expect(await axe(container)).toHaveNoViolations();
    await user.type(entry("Probing depth in millimetres"), "99");
    await save(user);
    await screen.findByRole("alert");
    expect(await axe(container)).toHaveNoViolations();
  });
});

// ---------- the charts on record ----------

const reading = (toothKey: string, site: string, pd: number, rec: number, bleeding: boolean): PerioReading => ({ toothKey, site, probingDepthMm: pd, recessionMm: rec, attachmentLossMm: pd + rec, bleeding });
const OLD = [reading("16", "B", 5, 1, true), reading("16", "MB", 3, 0, false), reading("26", "DL", 7, 2, true), reading("26", "L", 2, 0, false)];
const history = () => screen.getByRole("region", { name: "Charts on record" });

describe("the figures read off a chart", () => {
  it("counts sites, teeth, bleeding and deeper pockets exactly", () => {
    const chart = { id: "x", patientId: A, recordedAtUtc: "2026-09-01T10:00:00Z", recordedByName: "Dr", readingCount: 4, readings: OLD };
    expect(summarize(chart)).toEqual({ sites: 4, teeth: 2, bleedingPercent: 50, deepSites: 2, deepestMm: 7 });
    expect(summarize({ ...chart, readings: [] })).toEqual({ sites: 0, teeth: 0, bleedingPercent: 0, deepSites: 0, deepestMm: 0 });
    expect(summarize({ ...chart, readings: [reading("16", "B", 3, 0, true), reading("16", "MB", 3, 0, false), reading("16", "DB", 3, 0, false)] }).bleedingPercent).toBe(33);   // rounded
  });
});

describe("charts on record", () => {
  it("says none has been taken, and never that the gums are healthy", async () => {
    await openChart();
    await waitFor(() => expect(history()).toHaveTextContent("No periodontal chart has been recorded for this patient. That means none has been taken, not that the gums are healthy."));
  });

  it("lists charts newest first, the latest open with its figures, each reading with the tooth in the numbering shown and the derived attachment loss", async () => {
    server.perio.addChart(A, { recordedAtUtc: "2026-06-01T10:00:00Z", recordedByName: "Hana Hygienist", readings: [reading("16", "B", 3, 0, false)] });
    server.perio.addChart(A, { recordedAtUtc: "2026-09-01T10:00:00Z", readings: OLD });
    await openChart();
    const charts = await within(history()).findAllByRole("group");
    expect(charts).toHaveLength(2);
    expect(charts[0]).toHaveTextContent("by Dr. Okafor — 4 sites on 2 teeth, 50% bleeding (latest)");
    expect(charts[1]).toHaveTextContent("by Hana Hygienist — 1 site on 1 tooth, 0% bleeding");
    expect(charts[0]).toHaveAttribute("open");
    expect(charts[1]).not.toHaveAttribute("open");
    const figures = within(charts[0]).getByRole("list", { name: "Figures for this chart" });
    expect(figures).toHaveTextContent("Bleeding on probing: 50% of sites");
    expect(figures).toHaveTextContent("Sites 4 mm or deeper: 2");
    expect(figures).toHaveTextContent("Deepest site: 7 mm");
    const row = within(within(charts[0]).getByRole("table")).getAllByRole("row").find((r) => r.textContent?.startsWith("14DL"))!;      // FDI 26 is Universal 14
    expect([...row.querySelectorAll("th,td")].map((c) => c.textContent)).toEqual(["14", "DL", "7", "2", "9", "Yes"]);
  });

  it("a failed load says it could not load rather than showing none, lets the person chart anyway, and a retry loads them", async () => {
    server.perio.addChart(A, { readings: OLD });
    server.failClinical(`GET /api/patients/${A}/periodontal/charts`, json(500, { error: "server_error", message: "boom" }));
    const user = userEvent.setup();
    await openChart();
    const alert = await within(history()).findByRole("alert");
    expect(alert).toHaveTextContent("Could not load the charts on record. Do not assume there are none.");
    expect(history()).not.toHaveTextContent("No periodontal chart has been recorded");
    expect(entry("Probing depth in millimetres")).toBeEnabled();
    await user.click(within(alert).getByRole("button", { name: "Try again" }));
    expect(await within(history()).findAllByRole("group")).toHaveLength(1);
  });

  it("a chart that was stored but whose answer was lost is listed once after the retry", async () => {
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "4", "1");
    server.dropNextResponse("POST /periodontal/charts");
    await save(user);
    await waitFor(() => expect(status()).toHaveTextContent("connection dropped"));
    await save(user);
    await waitFor(() => expect(status()).toHaveTextContent("Chart saved"));
    expect(within(history()).getAllByRole("group")).toHaveLength(1);
  });

  it("starts a new chart from an earlier one, saves it as a new chart under a new key, and leaves the earlier chart as it was", async () => {
    server.perio.addChart(A, { readings: OLD });
    const user = userEvent.setup();
    await openChart();
    await user.click(await screen.findByRole("button", { name: "Start a new chart from these values" }));
    expect(entry("Probing depth in millimetres")).toHaveValue("5");
    expect(entry("Bleeding on probing")).toBeChecked();
    expect(entry("Probing depth in millimetres", "14", "distal lingual")).toHaveValue("7");
    expect(status()).toHaveTextContent("4 sites entered");
    await user.clear(entry("Probing depth in millimetres"));
    await user.type(entry("Probing depth in millimetres"), "4");
    await save(user);
    await waitFor(() => expect(server.perio.charts).toHaveLength(2));
    expect(within(history()).getAllByRole("group")).toHaveLength(2);
    expect(server.perio.charts.find((c) => c.key.startsWith("seed"))!.readings).toEqual(OLD);
  });

  it("asks before replacing what was entered but not saved; keeping leaves it, replacing swaps it", async () => {
    server.perio.addChart(A, { readings: OLD });
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "9", "9", false, "5", "mid buccal");
    await user.click(await screen.findByRole("button", { name: "Start a new chart from these values" }));
    const ask = screen.getByRole("alertdialog", { name: "Replace what you have entered?" });
    expect(ask).toHaveTextContent("replaces the 1 site you have entered and not saved");
    await user.click(within(ask).getByRole("button", { name: "Keep what I entered" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(entry("Probing depth in millimetres", "5")).toHaveValue("9");
    await user.click(screen.getByRole("button", { name: "Start a new chart from these values" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Replace" }));
    expect(entry("Probing depth in millimetres", "5")).toHaveValue("");
    expect(entry("Probing depth in millimetres")).toHaveValue("5");
  });

  it("a role that may only read still sees the charts on record, with no way to start a chart", async () => {
    server.permissions = READ_ONLY;
    server.perio.addChart(A, { readings: OLD });
    await openChart();
    expect(await within(history()).findAllByRole("group")).toHaveLength(1);
    expect(screen.queryByRole("button", { name: "Start a new chart from these values" })).not.toBeInTheDocument();
  });

  it("when the server refuses a chart it adds that its tooth numbers are FDI", async () => {
    const user = userEvent.setup();
    await openChart();
    await chartSite(user, "5", "2");
    server.failClinical(`POST /api/patients/${A}/periodontal/charts`, json(400, {
      error: "validation_failed", message: "x", problems: [{ toothKey: "16", site: "B", field: "probingDepthMm", code: "out_of_range", message: "Probing depth at tooth 16 site B is 5 mm; it must be whole." }],
    }));
    await save(user);
    expect(await screen.findByRole("alert")).toHaveTextContent("Tooth numbers in the messages from the server are FDI numbers");
  });

  it("has no axe violations with charts listed", async () => {
    server.perio.addChart(A, { readings: OLD });
    const { container } = await openChart();
    await within(history()).findAllByRole("group");
    expect(await axe(container)).toHaveNoViolations();
  });
});
