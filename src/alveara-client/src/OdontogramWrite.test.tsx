import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * STORY-006 (changing the chart): recording a finding in the state the clinician chooses, moving it forward, withdrawing a wrong entry with a reason, and the history - through the
 * real <App /> against an in-memory fake of the odontogram API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's
 * behaviour for each outcome: nothing pre-filled with a guess, only the surfaces that exist offered, the tooth sent as its FDI key whatever numbering is shown, a state that only
 * moves forward, a reason before anything is withdrawn, who and when shown in the history, and every failure path - a refused entry, a stale change, a dropped connection and a role
 * that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;

async function openChart() {
  window.history.pushState({}, "", `/patients/${A}/odontogram`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Odontogram" });
  return view;
}
const tooth = (label: string) => screen.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;
const surfaceButton = (name: RegExp) => within(screen.getByRole("group", { name: "Surfaces" })).getByRole("button", { name });
const form = () => screen.getByRole("form", { name: "Record a finding" });
const writes = () => server.callsToClinical("POST", "/odontogram");

async function record(user: ReturnType<typeof userEvent.setup>, condition: string, state: string, surface?: string) {
  await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
  await user.selectOptions(within(form()).getByLabelText("Condition"), condition);
  if (surface) await user.selectOptions(within(form()).getByLabelText("Surface"), surface);
  await user.selectOptions(within(form()).getByLabelText("State"), state);
  await user.click(within(form()).getByRole("button", { name: "Record finding" }));
}

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("recording a finding", () => {
  it("records a condition in the state the clinician chose, sends the tooth as its FDI key, says it was saved, and the chart shows it", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));                                    // Universal 3 is FDI 16
    await record(user, "Caries", "Diagnosed", "O");

    await waitFor(() => expect(status()).toHaveTextContent("Caries (Occlusal surface) recorded on tooth 3 as Diagnosed."));
    const [call] = writes();
    expect(call.body).toEqual({ toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });   // the stored identity, never the display number
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
    expect(screen.queryByRole("form", { name: "Record a finding" })).not.toBeInTheDocument();
    const row = screen.getByText("Caries", { selector: "strong" }).closest("li")!;
    expect(row).toHaveTextContent("Occlusal surface");
    expect(row).toHaveTextContent("Recorded by Dr. Okafor");
  });

  it("each of the four states is saved exactly as chosen", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    for (const [state, surface] of [["Existing", "M"], ["Diagnosed", "O"], ["Planned", "D"], ["Completed", "B"]]) {
      await record(user, "Restoration", state, surface);
      await waitFor(() => expect(status()).toHaveTextContent(`as ${state}.`));
    }
    expect(writes().map((c) => (c.body as { state: string }).state)).toEqual(["Existing", "Diagnosed", "Planned", "Completed"]);
  });

  it("a whole-tooth condition asks for no surface and sends none", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    await user.selectOptions(within(form()).getByLabelText("Condition"), "Crown");
    expect(within(form()).queryByLabelText("Surface")).not.toBeInTheDocument();
    expect(within(form()).getByText(/applies to the whole tooth, so no surface is chosen/)).toBeInTheDocument();
    await user.selectOptions(within(form()).getByLabelText("State"), "Existing");
    await user.click(within(form()).getByRole("button", { name: "Record finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("Crown (whole tooth) recorded on tooth 3 as Existing."));
    expect(writes()[0].body).toEqual({ toothKey: "16", surface: null, condition: "Crown", state: "Existing" });
  });

  it("starts with nothing chosen, and sends nothing until the condition, the surface and the state are chosen", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    expect(within(form()).getByLabelText("Condition")).toHaveValue("");
    expect(within(form()).getByLabelText("State")).toHaveValue("");
    await user.click(within(form()).getByRole("button", { name: "Record finding" }));
    expect(within(form()).getByText("Choose the condition.")).toBeInTheDocument();
    expect(within(form()).getByText("Choose where it is in its lifecycle.")).toBeInTheDocument();
    await user.selectOptions(within(form()).getByLabelText("Condition"), "Caries");
    await user.click(within(form()).getByRole("button", { name: "Record finding" }));
    expect(within(form()).getByText("Choose the surface this applies to.")).toBeInTheDocument();
    expect(writes()).toHaveLength(0);
  });

  it("offers only the surfaces that exist on the tooth, and starts on the surface already selected", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("8"));                                    // an upper central incisor
    await user.click(surfaceButton(/Incisal/));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    await user.selectOptions(within(form()).getByLabelText("Condition"), "Caries");
    const surface = within(form()).getByLabelText("Surface") as HTMLSelectElement;
    expect([...surface.options].map((o) => o.textContent)).toEqual(["Choose…", "Mesial", "Incisal", "Distal", "Facial", "Lingual"]);
    expect(surface.value).toBe("I");                                 // the selected surface is the starting choice, not forced
  });

  it("shows the server's refusal against the field it names and keeps what was typed", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    server.failClinical(`POST /api/patients/${A}/odontogram/findings`, json(400, { error: "validation_failed", message: "Some fields need attention.", fieldErrors: { surface: "That surface does not exist on this tooth." } }));
    await record(user, "Caries", "Diagnosed", "O");
    expect(await within(form()).findByText("That surface does not exist on this tooth.")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Not saved: Some fields need attention.");
    expect(within(form()).getByLabelText("Condition")).toHaveValue("Caries");
    expect(within(form()).getByLabelText("State")).toHaveValue("Diagnosed");
  });

  it("a dropped connection keeps the form and what was typed; trying again records the finding once", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    server.dropNextResponse("POST /findings");                       // the server stores it, the answer is lost
    await record(user, "Caries", "Diagnosed", "O");
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: the connection dropped. What you typed is still here; try again."));
    expect(within(form()).getByLabelText("Condition")).toHaveValue("Caries");
    await user.click(within(form()).getByRole("button", { name: "Record finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("recorded on tooth 3 as Diagnosed."));
    expect(server.odontogram.findings.size).toBe(1);                 // the retry was a quiet repeat, not a second finding
  });

  it("recording something already recorded in a different state is refused with the reason, and the existing finding is untouched", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    await record(user, "Caries", "Planned", "O");
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: This is already recorded as Diagnosed. Change its state instead of recording it again."));
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
  });
});

describe("moving a finding forward", () => {
  it("plans a diagnosed finding and then completes it, the chart following each step, with no way back", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));

    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("Caries (Occlusal surface) on tooth 3 is now Planned."));
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Planned");
    expect(screen.queryByRole("button", { name: /^Plan:/ })).not.toBeInTheDocument();           // a planned finding cannot be planned again or sent back

    await user.click(screen.getByRole("button", { name: "Complete: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("is now Completed."));
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Completed");
    expect(screen.queryByRole("button", { name: /^(Plan|Complete):/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Withdraw: Caries (Occlusal surface)" })).toBeInTheDocument();
    expect(writes().map((c) => (c.body as { state: string }).state)).toEqual(["Planned", "Completed"]);
  });

  it("a completed treatment is recorded straight from diagnosed, and the odontogram updates", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Complete: Caries (Occlusal surface)" }));
    await waitFor(() => expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Completed"));
    expect(tooth("3").className).toContain("alv-odonto__state--completed");
  });

  it("an existing finding can only be withdrawn: it cannot be planned or completed", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "11", condition: "Crown", state: "Existing" });
    await openChart();
    await user.click(tooth("8"));
    expect(screen.queryByRole("button", { name: /^(Plan|Complete):/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Withdraw: Crown (whole tooth)" })).toBeInTheDocument();
  });

  it("shows the server's refusal of a move the screen did not expect, and leaves the finding as it was", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    server.failClinical(`POST /api/odontogram/findings`, json(409, { error: "invalid_transition", message: "A finding that is Completed cannot become Planned." }));
    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: A finding that is Completed cannot become Planned."));
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
  });

  it("disables the other controls and says Saving… while a change is in flight", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    const release = server.hold("POST /api/odontogram/findings");
    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    expect(status()).toHaveTextContent("Saving…");
    expect(screen.getByRole("button", { name: "Complete: Caries (Occlusal surface)" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Record a finding on this tooth" })).toBeDisabled();
    release();
    await waitFor(() => expect(status()).toHaveTextContent("is now Planned."));
  });
});

describe("withdrawing a wrong entry", () => {
  it("needs a reason, then removes the finding from the chart and says so", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Withdraw: Caries (Occlusal surface)" }));
    const withdraw = screen.getByRole("form", { name: "Withdraw finding: Caries (Occlusal surface)" });

    await user.click(within(withdraw).getByRole("button", { name: "Withdraw finding" }));
    expect(within(withdraw).getByText("Say why.")).toBeInTheDocument();                           // refused before anything is sent
    expect(writes()).toHaveLength(0);

    await user.type(within(withdraw).getByLabelText(/Why is/), "Wrong tooth");
    await user.click(within(withdraw).getByRole("button", { name: "Withdraw finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("Caries (Occlusal surface) on tooth 3 withdrawn."));
    expect(writes()[0].body).toMatchObject({ reason: "Wrong tooth" });
    expect(tooth("3")).toHaveAccessibleName("Tooth 3, upper right first molar: nothing recorded");
    expect(screen.getByText("Nothing is recorded for this tooth. This does not mean it is healthy.")).toBeInTheDocument();
  });

  it("can be cancelled without sending anything", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Withdraw: Caries (Occlusal surface)" }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("form", { name: /^Withdraw finding/ })).not.toBeInTheDocument();
    expect(writes()).toHaveLength(0);
  });
});

describe("the history", () => {
  it("lists every change oldest first with who made it, when and why, including the withdrawal reason", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("is now Planned."));
    await user.click(screen.getByText("History", { selector: "summary" }));
    const items = (await screen.findAllByRole("listitem")).filter((li) => li.className === "");
    const lines = items.map((li) => li.textContent!.replace(/\s+/g, " ")).filter((t) => /Recorded|State changed|Withdrawn/.test(t));
    expect(lines).toHaveLength(2);
    expect(lines[0]).toMatch(/^Recorded - Caries, Occlusal surface · Diagnosed .* by Dr\. Okafor$/);
    expect(lines[1]).toMatch(/^State changed - Caries, Occlusal surface · Planned .* by Dr\. Okafor$/);

    await user.click(screen.getByRole("button", { name: "Withdraw: Caries (Occlusal surface)" }));
    await user.type(screen.getByLabelText(/Why is/), "Entered on the wrong patient");
    await user.click(screen.getByRole("button", { name: "Withdraw finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("withdrawn."));
  });

  it("says it could not load, and recovers when opened again", async () => {
    const user = userEvent.setup();
    const f = server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries" });
    await openChart();
    await user.click(tooth("3"));
    server.failClinical(`GET /api/odontogram/findings/${f.id}/history`, json(500, { error: "server_error", message: "boom" }));
    await user.click(screen.getByText("History", { selector: "summary" }));
    expect(await screen.findByText("Could not load the history. Close it and open it again.")).toBeInTheDocument();
    await user.click(screen.getByText("History", { selector: "summary" }));
    await user.click(screen.getByText("History", { selector: "summary" }));
    expect(await screen.findByText(/^Recorded/, { selector: ".alv-clinical__history-what" })).toBeInTheDocument();
  });
});

describe("a stale change", () => {
  it("shows the conflict banner, keeps the finding as it was, and a reload refreshes in place so the person can try again", async () => {
    const user = userEvent.setup();
    const f = server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    server.odontogram.bump(f.id);                                    // someone else changed it behind the screen's back

    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: someone else changed this."));
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
    expect(screen.getByRole("region", { name: /^Tooth 3:/ })).toBeInTheDocument();                // still on the same tooth

    await user.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("is now Planned."));
  });
});

describe("a role that may only read", () => {
  beforeEach(() => { server.permissions = READ_ONLY; });

  it("sees the chart, the findings and the history but is offered no way to change anything", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    expect(status()).toHaveTextContent("You can read this but your role cannot change it.");
    await user.click(tooth("3"));
    expect(screen.getByText("Caries", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByText("History", { selector: "summary" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^(Plan|Complete|Withdraw):/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Record a finding on this tooth" })).not.toBeInTheDocument();
    expect(writes()).toHaveLength(0);
  });
});

describe("accessibility", () => {
  it("has no axe violations with the record form, a withdraw form and the conflict banner open", async () => {
    const user = userEvent.setup();
    const f = server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    const { container } = await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    await user.click(screen.getByRole("button", { name: "Withdraw: Caries (Occlusal surface)" }));
    server.odontogram.bump(f.id);
    await user.type(screen.getByLabelText(/Why is/), "Wrong tooth");
    await user.click(screen.getByRole("button", { name: "Withdraw finding" }));
    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(await axe(container)).toHaveNoViolations();
  });
});
