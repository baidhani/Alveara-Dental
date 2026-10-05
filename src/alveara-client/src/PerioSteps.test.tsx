import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";
import { sweep } from "./pages/perio/perioSiteModel";
import type { PerioReading } from "./services/perioApi";

/**
 * ALV-012-C01: step-by-step charting, through the real <App /> against the in-memory fake of the API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is
 * the keyboard flow (a whole mouth typed without the mouse, the cursor following the documented order and never losing its place), the quick keys, the saving as the cursor leaves a tooth, and every
 * failure path: a refused save keeps what was typed and points at the entry, a stale draft shows the conflict banner and keeps it, a dropped connection keeps it, a missing tooth is skipped, and a
 * role that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;

async function openSteps(user: ReturnType<typeof userEvent.setup>) {
  window.history.pushState({}, "", `/patients/${A}/periodontal`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Periodontal chart" });
  await user.click(screen.getByRole("button", { name: "Step by step (keyboard)" }));
  return view;
}
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-perio__sessionstatus"))!;
const where = () => screen.getByRole("heading", { level: 3, name: /^Tooth \S+, / });
const depth = () => screen.getByLabelText("Probing depth (mm)");
const recession = () => screen.getByLabelText("Recession (mm)");
const posts = (suffix = "/entries") => server.callsToClinical("POST", suffix);
async function start(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Start a step-by-step chart" }));
  await screen.findByLabelText("Probing depth (mm)");
}
const reading = (toothKey: string, site: string, pd: number, rec = 0, bleeding = false): PerioReading => ({ toothKey, site, probingDepthMm: pd, recessionMm: rec, attachmentLossMm: pd + rec, bleeding });

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("starting and the first site", () => {
  it("offers to start a chart, and starting shows the first site of the entry order with the depth box focused", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    expect(status()).toHaveTextContent("No chart is in progress.");
    await start(user);
    expect(where()).toHaveTextContent("Tooth 1, distal buccal (FDI 18, upper right third molar) · site 1 of 192");     // Universal 1 is FDI 18
    expect(depth()).toHaveFocus();
    expect(status()).toHaveTextContent("0 of 192 sites entered; all saved.");
    expect(server.callsToClinical("POST", "/periodontal/sessions")).toHaveLength(1);
  });
});

describe("the keyboard flow", () => {
  it("depth, Enter, recession, Enter saves the site and lands on the next one in the order, still in the depth box", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("3{Enter}");
    expect(recession()).toHaveFocus();
    await user.keyboard("1{Enter}");
    expect(where()).toHaveTextContent(/^Tooth 1, mid buccal/);
    expect(depth()).toHaveFocus();
    expect(depth()).toHaveValue("");
    expect(posts()).toHaveLength(0);                                                       // nothing is sent while the cursor stays on the tooth
    expect(status()).toHaveTextContent("1 of 192 sites entered; 1 not saved yet.");
  });

  it("the three sites of a tooth are saved together when the cursor moves to the next tooth, and not before", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("3{Enter}1{Enter}4{Enter}0{Enter}");
    expect(posts()).toHaveLength(0);
    await user.keyboard("2{Enter}0{Enter}");                                               // the third site of tooth 18; the next is tooth 17
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(where()).toHaveTextContent(/^Tooth 2, distal buccal/);
    expect((posts()[0].body as { readings: unknown[] }).readings).toEqual([
      expect.objectContaining({ toothKey: "18", site: "DB", probingDepthMm: 3, recessionMm: 1 }), expect.objectContaining({ toothKey: "18", site: "B", probingDepthMm: 4, recessionMm: 0 }),
      expect.objectContaining({ toothKey: "18", site: "MB", probingDepthMm: 2, recessionMm: 0 }),
    ]);
    await waitFor(() => expect(status()).toHaveTextContent("3 of 192 sites entered; all saved."));
    expect(server.perio.sessions[0].readings.size).toBe(3);
  });

  it("an entry that is not a whole number from 0 to 15 is refused in place with nothing buffered and the cursor not moved", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("99{Enter}");
    expect(await screen.findByText("Probing depth must be a whole number from 0 to 15 mm.")).toBeInTheDocument();
    expect(depth()).toHaveAttribute("aria-invalid", "true");
    await user.clear(depth());
    await user.keyboard("4{Enter}");                                                       // the depth is now right, so Enter goes to the recession
    await user.keyboard("{Enter}");                                                        // an empty recession is not read as 0
    expect(await screen.findByText("Enter the recession for this site, or 0 if there is none.")).toBeInTheDocument();
    expect(where()).toHaveTextContent(/^Tooth 1, distal buccal/);
    expect(status()).toHaveTextContent("0 of 192 sites entered; all saved.");
  });

  it("B, S and P toggle bleeding, pus and plaque while typing, X marks the tooth not charted, and pus and plaque are only recorded when asked for", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("b");                                                              // bleeding toggles from the keyboard
    expect(screen.getByLabelText(/Bleeding on probing/)).toBeChecked();
    await user.keyboard("s");                                                              // pus is not being recorded, so S does nothing
    expect(screen.queryByLabelText(/Pus \(S\)/)).not.toBeInTheDocument();
    await user.click(screen.getByLabelText("Pus (suppuration)"));
    await user.click(screen.getByLabelText("Plaque"));
    await user.click(depth());
    await user.keyboard("s");
    await user.keyboard("p");
    expect(screen.getByLabelText(/Pus \(S\)/)).toBeChecked();
    expect(screen.getByLabelText(/Plaque \(P\)/)).toBeChecked();
    await user.keyboard("5{Enter}2{Enter}");
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect((posts()[0].body as { readings: unknown[] }).readings).toEqual([expect.objectContaining({ bleeding: true, suppuration: true, plaque: true, probingDepthMm: 5, recessionMm: 2 })]);

    await user.click(depth());                                                              // back into the depth box after clicking Save
    await user.keyboard("x");                                                              // the cursor is now on tooth 18 mesial buccal: skip the whole tooth
    await waitFor(() => expect(where()).toHaveTextContent(/^Tooth 2, distal buccal/));
    await waitFor(() => expect(server.perio.sessions[0].teeth.get("18")?.excluded).toBe(true));
    expect(server.perio.sessions[0].readings.size).toBe(0);                                // its earlier reading went with it
  });

  it("an unticked optional measure is saved as not assessed, not as none", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("3{Enter}0{Enter}");
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect((posts()[0].body as { readings: Record<string, unknown>[] }).readings[0]).toMatchObject({ suppuration: null, plaque: null });
  });

  it("Shift+Enter goes back a site and shows what is already entered there, which can be retyped", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("3{Enter}1{Enter}");
    await user.keyboard("{Shift>}{Enter}{/Shift}");
    expect(where()).toHaveTextContent(/^Tooth 1, distal buccal/);
    expect(depth()).toHaveValue("3");
    expect(recession()).toHaveValue("1");
    await user.clear(depth());
    await user.keyboard("6{Enter}");
    await user.clear(recession());
    await user.keyboard("2{Enter}");
    expect(where()).toHaveTextContent(/^Tooth 1, mid buccal/);                                // goes on to the next site still waiting for a reading
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect([...server.perio.sessions[0].readings.values()]).toEqual([expect.objectContaining({ probingDepthMm: 6, recessionMm: 2 })]));
  });

  it("a whole mouth - all 192 sites - is typed without the mouse, the cursor never losing its place, and finishes as one chart", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    const order = sweep();
    // each site: a depth that is its place in the order (0 to 9), recession 0
    await user.keyboard(order.slice(0, 24).map((_, i) => `${i % 8}{Enter}0{Enter}`).join(""));
    expect(where()).toHaveTextContent(/^Tooth 9, mesial facial/);                          // site 25 is 21 mesial buccal: the left side of the arch, travelling away from the middle
    expect(status()).toHaveTextContent("24 of 192 sites entered");
    await user.keyboard(order.slice(24).map((_, i) => `${(i + 24) % 8}{Enter}0{Enter}`).join(""));
    await waitFor(() => expect(status()).toHaveTextContent("192 of 192 sites entered"));
    await user.click(screen.getByRole("button", { name: "Finish and save the chart" }));
    await waitFor(() => expect(status()).toHaveTextContent("Chart saved with 192 sites. It is now in the charts on record."));
    const chart = server.perio.charts.at(-1)!;
    expect(chart.readings).toHaveLength(192);
    expect(new Set(chart.readings.map((r) => `${r.toothKey}${r.site}`)).size).toBe(192);
    expect(chart.readings.find((r) => r.toothKey === "18" && r.site === "DB")!.probingDepthMm).toBe(0);   // the first site of the order got the first digit
    expect(chart.readings.find((r) => r.toothKey === "21" && r.site === "MB")!.probingDepthMm).toBe(24 % 8);
    expect(within(screen.getByRole("region", { name: "Charts on record" })).getAllByRole("group")).toHaveLength(1);
  }, 60000);
});

describe("teeth that are not charted or are missing", () => {
  it("a tooth the odontogram records as missing is skipped, listed, and cannot be jumped to", async () => {
    server.perio.absentTeeth = ["18"];
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    expect(where()).toHaveTextContent(/^Tooth 2, distal buccal/);                          // FDI 17: tooth 18 is passed over
    expect(screen.getByText(/Missing in the odontogram, so skipped: tooth 1\./)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Tooth 1, upper right third molar: missing in the odontogram/ })).toBeDisabled();
    expect(status()).toHaveTextContent("0 of 186 sites entered");
  });

  it("a tooth marked not charted can be put back, and then it is in the order again", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("x");
    await waitFor(() => expect(where()).toHaveTextContent(/^Tooth 2, distal buccal/));
    await user.click(await screen.findByRole("button", { name: /^Tooth 1, upper right third molar: not charted\. Chart this tooth/ }));
    await waitFor(() => expect(server.perio.sessions[0].teeth.size).toBe(0));
    await user.click(screen.getByRole("button", { name: /^Tooth 1, upper right third molar: not started/ }));
    expect(where()).toHaveTextContent(/^Tooth 1, distal buccal/);
  });

  it("mobility and furcation are set per tooth, furcation only where the tooth has more than one root, and are saved with the tooth", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.selectOptions(screen.getByLabelText("Mobility (grade)"), "2");
    await user.selectOptions(screen.getByLabelText("Furcation (grade)"), "3");                // 18 is a molar
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(server.perio.sessions[0].teeth.get("18")).toMatchObject({ mobility: 2, furcation: 3, excluded: false }));
    await user.click(screen.getByRole("button", { name: /^Tooth 6, upper right canine/ }));                      // FDI 13
    expect(screen.queryByLabelText("Furcation (grade)")).not.toBeInTheDocument();
    expect(screen.getByText("This tooth has a single root, so there is no furcation to grade.")).toBeInTheDocument();
  });
});

describe("failures never lose what was typed", () => {
  it("a refused save keeps the entries, marks the problem, moves to the entry to correct, and a later save goes through", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    server.failClinical("POST /api/periodontal/sessions", json(400, {
      error: "validation_failed", message: "Some entries need correcting.", problems: [{ toothKey: "18", site: "B", field: "toothKey", code: "tooth_absent", message: "Tooth 18 is recorded as missing in the odontogram, so it cannot be charted." }],
    }));
    await user.keyboard("3{Enter}1{Enter}4{Enter}0{Enter}2{Enter}0{Enter}");                // finishing the tooth triggers the save
    const alert = await screen.findByRole("alert", {}, { timeout: 3000 }).catch(() => null);
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: some entries need correcting. Everything you entered is still here."));
    expect(alert ?? screen.getAllByRole("alert")[0]).toBeInTheDocument();
    expect(screen.getByText(/Tooth 1, mid buccal:/)).toBeInTheDocument();
    expect(screen.getByText(/Tooth numbers in these messages from the server are FDI numbers/)).toBeInTheDocument();
    expect(where()).toHaveTextContent(/^Tooth 1, mid buccal/);                              // the cursor went to the entry to correct, and shows what was typed there
    expect(depth()).toHaveValue("4");
    expect(server.perio.sessions[0].readings.size).toBe(0);
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));      // the server accepts it now
    await waitFor(() => expect(server.perio.sessions[0].readings.size).toBe(3));
    await waitFor(() => expect(screen.queryByText("Some entries need correcting")).not.toBeInTheDocument());
  });

  it("a draft changed by someone else shows the conflict banner and keeps what was typed; after reloading, the save goes through", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    server.perio.touchSession(server.perio.sessions[0].id);                                  // another person saves behind the screen
    await user.keyboard("3{Enter}1{Enter}");
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Not saved: someone else changed this chart. What you entered is still here.");
    expect(server.perio.sessions[0].readings.size).toBe(0);
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
    expect(status()).toHaveTextContent("1 of 192 sites entered; 1 not saved yet.");
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(server.perio.sessions[0].readings.size).toBe(1));
  });

  it("a dropped connection says so and keeps what was typed; after the retry the draft holds each site once", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.keyboard("3{Enter}1{Enter}");
    server.dropNextResponse("POST /entries");                                                // the server stores the batch, the answer is lost
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: the connection dropped. What you entered is still here; try again."));
    expect(server.perio.sessions[0].readings.size).toBe(1);
    await user.click(screen.getByRole("button", { name: "Save what I have entered" }));        // the screen holds the old version, so this is a conflict, not a duplicate
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    await user.click(await screen.findByRole("button", { name: "Save what I have entered" }));
    await waitFor(() => expect(status()).toHaveTextContent(/all saved/));
    expect(server.perio.sessions[0].readings.size).toBe(1);
  });

  it("finishing with an incomplete chart still works: only entered sites are saved, and finalizing an empty one is refused and the draft stays open", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    expect(screen.getByRole("button", { name: "Finish and save the chart" })).toBeDisabled();       // nothing entered yet
    await user.keyboard("3{Enter}1{Enter}");
    await user.click(screen.getByRole("button", { name: "Finish and save the chart" }));
    await waitFor(() => expect(status()).toHaveTextContent("Chart saved with 1 site."));
    expect(server.perio.charts.at(-1)!.readings).toHaveLength(1);
  });
});

describe("discarding, resuming and the read-only role", () => {
  it("discarding asks first; keeping leaves the draft, discarding closes it so a new one can start", async () => {
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    await start(user);
    await user.click(screen.getByRole("button", { name: "Discard this chart" }));
    const ask = screen.getByRole("alertdialog", { name: "Discard this chart?" });
    await user.click(within(ask).getByRole("button", { name: "Keep charting" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Discard this chart" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Discard" }));
    await waitFor(() => expect(status()).toHaveTextContent("The chart in progress was discarded."));
    expect(server.perio.sessions[0].status).toBe("Abandoned");
    expect(await screen.findByRole("button", { name: "Start a step-by-step chart" })).toBeInTheDocument();
  });

  it("a draft someone started is offered in the grid view and resumed where it left off", async () => {
    server.perio.addSession(A, { readings: [reading("18", "DB", 3), reading("18", "B", 4), reading("18", "MB", 2)] });
    const user = userEvent.setup({ delay: null });
    window.history.pushState({}, "", `/patients/${A}/periodontal`);
    render(<App />);
    await screen.findByRole("heading", { name: "Periodontal chart" });
    expect(await screen.findByRole("note")).toHaveTextContent("A step-by-step chart is in progress (3 sites saved so far).");
    await user.click(screen.getByRole("button", { name: "Continue it" }));
    expect(await screen.findByRole("heading", { level: 3, name: /^Tooth 2, distal buccal/ })).toBeInTheDocument();      // 18 is done, so it resumes at 17
    expect(status()).toHaveTextContent("3 of 192 sites entered; all saved.");
  });

  it("a role that may only read sees how far the draft has got and has no way to start or change one", async () => {
    server.permissions = READ_ONLY;
    server.perio.addSession(A, { readings: [reading("18", "DB", 3)] });
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    expect(await screen.findByText(/1 of 192 sites entered so far\. Your role can read this but cannot change it\./)).toBeInTheDocument();
    expect(screen.queryByLabelText("Probing depth (mm)")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Finish and save the chart" })).not.toBeInTheDocument();
  });

  it("a read-only role with no draft open is told it cannot start one", async () => {
    server.permissions = READ_ONLY;
    const user = userEvent.setup({ delay: null });
    await openSteps(user);
    expect(await screen.findByText("Your role can read charts but cannot start one.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Start a step-by-step chart" })).not.toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no axe violations on the entry screen, with problems shown, and with the discard question open", async () => {
    const user = userEvent.setup({ delay: null });
    const { container } = await openSteps(user);
    await start(user);
    expect(await axe(container)).toHaveNoViolations();
    await user.keyboard("99{Enter}");
    await screen.findByText("Probing depth must be a whole number from 0 to 15 mm.");
    expect(await axe(container)).toHaveNoViolations();
    await user.click(screen.getByRole("button", { name: "Discard this chart" }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
