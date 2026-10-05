import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { OdontogramPanel } from "./pages/odontogram/OdontogramPanel";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";
import type { PatientDetail } from "./services/patientsApi";

/**
 * ALV-006-C01 UI: permanent, primary and mixed dentition as three views of the same findings (switching loses nothing and asks the server for nothing), recording on a primary tooth with
 * only the conditions that apply to it, the practice's condition catalogue (everyone reads it, people who manage clinical templates extend and retire it, retiring stops new findings and
 * leaves old ones as they were), a tooth's whole history, links shown on a finding, and arrow-key movement around the chart - through the real <App /> against an in-memory fake of the API.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const MANAGER = ["ViewPatientRecords", "ViewClinicalDocumentation", "ManageClinicalNotes", "ManageClinicalTemplates"];
let server: FakeClinicalServer;

async function openChart() {
  window.history.pushState({}, "", `/patients/${A}/odontogram`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Odontogram" });
  return view;
}
const tooth = (label: string) => screen.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;
const detail = () => screen.getByRole("region", { name: /^Tooth \S+:/ });
const findingList = () => within(detail()).getByRole("list", { name: /^Findings on/ });
const group = (name: string) => screen.getByRole("group", { name });
const teethIn = (name: string) => within(group(name)).getAllByRole("button");
const numberOf = (b: HTMLElement) => b.querySelector(".alv-odonto__tooth-number")!.textContent;
const catalogue = () => screen.getByText(/^Condition types \(/).closest("details")!;
const patient = () => makePatient({ id: A, firstName: "Ann", lastName: "Lee" }) as unknown as PatientDetail;
const writes = () => server.callsToClinical("POST", "/odontogram");

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("permanent, primary and mixed dentition", () => {
  it("opens as the permanent chart for a patient with nothing on a primary tooth, and offers all three views", async () => {
    await openChart();
    expect(within(group("Teeth shown")).getAllByRole("button").map((b) => b.textContent)).toEqual(["Permanent teeth", "Primary teeth", "Mixed (both)"]);
    expect(screen.getByRole("button", { name: "Permanent teeth" })).toHaveAttribute("aria-pressed", "true");
    expect(teethIn("Upper teeth")).toHaveLength(16);
    expect(teethIn("Lower teeth")).toHaveLength(16);
    expect(screen.queryByRole("group", { name: /primary teeth/ })).not.toBeInTheDocument();
  });

  it("opens as the mixed chart when the patient already has a finding on a primary tooth", async () => {
    server.odontogram.addFinding(A, { toothKey: "55", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    expect(screen.getByRole("button", { name: "Mixed (both)" })).toHaveAttribute("aria-pressed", "true");
    expect(tooth("A").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
    expect(screen.queryByRole("heading", { name: /not drawn in this view/ })).not.toBeInTheDocument();      // the primary finding is drawn, so nothing is left out
  });

  it("the primary view draws the 20 primary teeth lettered A to T in Universal, and the mixed view draws all 52 in four arches", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(screen.getByRole("button", { name: "Primary teeth" }));
    expect(teethIn("Upper primary teeth").map(numberOf).join(" ")).toBe("A B C D E F G H I J");
    expect(teethIn("Lower primary teeth").map(numberOf).join(" ")).toBe("T S R Q P O N M L K");
    expect(screen.queryByRole("group", { name: "Upper teeth" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Mixed (both)" }));
    expect(teethIn("Upper permanent teeth")).toHaveLength(16);
    expect(teethIn("Upper primary teeth")).toHaveLength(10);
    expect(teethIn("Lower primary teeth")).toHaveLength(10);
    expect(teethIn("Lower permanent teeth")).toHaveLength(16);
    expect(screen.getAllByRole("button", { name: /^Tooth / })).toHaveLength(52);
  });

  it("switching the view changes only what is drawn: no request is made, the selected tooth and its findings stay, and nothing is lost", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    server.odontogram.addFinding(A, { toothKey: "55", surface: "O", condition: "Caries", state: "Planned" });
    await openChart();
    await user.click(tooth("3"));
    const requests = server.clinicalCalls.length;
    await user.click(screen.getByRole("button", { name: "Primary teeth" }));
    expect(screen.getByRole("heading", { name: "Recorded on teeth not drawn in this view" }).closest("section")).toHaveTextContent("Tooth 3 (upper right first molar): Caries, Occlusal surface: Diagnosed");
    expect(tooth("A").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Planned");
    expect(within(findingList()).getByText("Caries", { selector: "strong" })).toBeInTheDocument();       // tooth 3's details are still open
    await user.click(screen.getByRole("button", { name: "Permanent teeth" }));
    expect(screen.getByRole("heading", { name: "Recorded on teeth not drawn in this view" }).closest("section")).toHaveTextContent("Tooth A (upper right primary second molar)");
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
    expect(server.clinicalCalls.length).toBe(requests);                                                   // a view is not a request
    expect(server.odontogram.findings.size).toBe(2);
  });

  it("names primary teeth in every numbering system and keeps the key as the stored identity", async () => {
    render(<OdontogramPanel patient={patient()} numbering="Palmer" />);
    await screen.findByRole("heading", { name: "Odontogram" });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Primary teeth" }));
    expect(teethIn("Upper primary teeth").map(numberOf).join(" ")).toBe("URE URD URC URB URA ULA ULB ULC ULD ULE");
  });

  it("records a finding on a primary tooth, offering only the conditions that apply to primary teeth and the surfaces that exist on it", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(screen.getByRole("button", { name: "Primary teeth" }));
    await user.click(tooth("A"));                                                                         // Universal A is FDI 55, a primary second molar
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    const form = screen.getByRole("form", { name: "Record a finding" });
    const conditions = [...(within(form).getByLabelText("Condition") as HTMLSelectElement).options].map((o) => o.textContent);
    expect(conditions).toEqual(["Choose…", "Caries", "Crown", "Missing tooth", "Restoration", "Root canal"].sort((a, b) => (a === "Choose…" ? -1 : b === "Choose…" ? 1 : 0)).filter((c) => c !== "Implant"));
    expect(conditions).not.toContain("Implant");                                                          // implants are not placed in primary teeth
    await user.selectOptions(within(form).getByLabelText("Condition"), "Caries");
    expect([...(within(form).getByLabelText("Surface") as HTMLSelectElement).options].map((o) => o.textContent)).toEqual(["Choose…", "Mesial", "Occlusal", "Distal", "Buccal", "Lingual"]);
    await user.selectOptions(within(form).getByLabelText("Surface"), "O");
    await user.selectOptions(within(form).getByLabelText("State"), "Diagnosed");
    await user.click(within(form).getByRole("button", { name: "Record finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("Caries (Occlusal surface) recorded on tooth A as Diagnosed."));
    expect(writes()[0].body).toEqual({ toothKey: "55", surface: "O", condition: "Caries", state: "Diagnosed" });     // the stored identity is the FDI key
  });

  it("offers an implant on a permanent tooth", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    expect([...(within(screen.getByRole("form", { name: "Record a finding" })).getByLabelText("Condition") as HTMLSelectElement).options].map((o) => o.textContent)).toContain("Implant");
  });

  it("shows the server's refusal of a tooth that is recorded as missing, in words, and stores nothing", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", condition: "Missing", state: "Existing" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    const form = screen.getByRole("form", { name: "Record a finding" });
    await user.selectOptions(within(form).getByLabelText("Condition"), "Crown");
    await user.selectOptions(within(form).getByLabelText("State"), "Planned");
    await user.click(within(form).getByRole("button", { name: "Record finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: This tooth is recorded as missing, so no other finding can be recorded on it."));
    expect(server.odontogram.findings.size).toBe(1);
  });

  it("says, when a condition marks the tooth as missing, what that will do", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    await user.selectOptions(within(screen.getByRole("form", { name: "Record a finding" })).getByLabelText("Condition"), "Missing");
    expect(screen.getByText(/marks the tooth as not there/)).toBeInTheDocument();
  });
});

describe("the condition catalogue", () => {
  it("lists the conditions with what each means and who added it, to anyone who can read the chart, and offers a reader no way to change it", async () => {
    await openChart();
    const panel = catalogue();
    expect(panel.querySelector("summary")).toHaveTextContent("Condition types (6 active)");
    const items = within(panel).getAllByRole("listitem").filter((li) => li.className === "alv-odonto__condition");
    expect(items).toHaveLength(6);
    const implant = items.find((li) => li.textContent!.includes("Implant"))!;
    expect(implant).toHaveTextContent("Code Implant · recorded on the whole tooth · for permanent teeth · stands in for a missing tooth · added by System");
    expect(items.find((li) => li.textContent!.includes("Missing tooth"))).toHaveTextContent("marks the tooth as missing");
    expect(within(panel).queryByRole("button", { name: /Retire|Reactivate|Add a condition type/ })).not.toBeInTheDocument();
  });

  it("lets a person who manages clinical templates add a condition, and it can then be recorded", async () => {
    const user = userEvent.setup();
    server.permissions = MANAGER;
    await openChart();
    await user.click(screen.getByText(/^Condition types \(/));
    await user.click(screen.getByRole("button", { name: "Add a condition type" }));
    const form = screen.getByRole("form", { name: "Add a condition type" });

    await user.click(within(form).getByRole("button", { name: "Add condition type" }));                  // nothing chosen: refused before anything is sent
    expect(within(form).getByText("A label is required.")).toBeInTheDocument();
    expect(within(form).getByText("Choose where it is recorded.")).toBeInTheDocument();
    expect(within(form).getByText("Choose which teeth it applies to.")).toBeInTheDocument();
    expect(writes()).toHaveLength(0);

    await user.type(within(form).getByLabelText("Label"), "Tooth fracture");
    expect(within(form).getByLabelText("Code")).toHaveValue("ToothFracture");                           // suggested from the label, still editable
    await user.selectOptions(within(form).getByLabelText("Recorded on"), "Surface");
    await user.selectOptions(within(form).getByLabelText("Applies to"), "Both");
    await user.click(within(form).getByRole("button", { name: "Add condition type" }));
    await waitFor(() => expect(status()).toHaveTextContent("Tooth fracture added to the catalogue."));
    expect(writes()[0].body).toEqual({ code: "ToothFracture", label: "Tooth fracture", scope: "Surface", appliesTo: "Both", toothEffect: "None" });
    expect(catalogue().querySelector("summary")).toHaveTextContent("Condition types (7 active)");

    await user.click(tooth("3"));                                                                         // the new condition is available to record
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    expect([...(within(screen.getByRole("form", { name: "Record a finding" })).getByLabelText("Condition") as HTMLSelectElement).options].map((o) => o.textContent)).toContain("Tooth fracture");
  });

  it("refuses a malformed code before sending, and shows the server's refusal of a duplicate against the code field", async () => {
    const user = userEvent.setup();
    server.permissions = MANAGER;
    await openChart();
    await user.click(screen.getByText(/^Condition types \(/));
    await user.click(screen.getByRole("button", { name: "Add a condition type" }));
    const form = screen.getByRole("form", { name: "Add a condition type" });
    await user.type(within(form).getByLabelText("Label"), "Fracture");
    await user.clear(within(form).getByLabelText("Code"));
    await user.type(within(form).getByLabelText("Code"), "fracture");
    await user.selectOptions(within(form).getByLabelText("Recorded on"), "Surface");
    await user.selectOptions(within(form).getByLabelText("Applies to"), "Both");
    await user.click(within(form).getByRole("button", { name: "Add condition type" }));
    expect(within(form).getByText(/starting with a capital letter/)).toBeInTheDocument();
    expect(writes()).toHaveLength(0);

    await user.clear(within(form).getByLabelText("Code"));
    await user.type(within(form).getByLabelText("Code"), "Caries");                                       // already in the catalogue, with a different meaning
    await user.click(within(form).getByRole("button", { name: "Add condition type" }));
    expect(await within(form).findByText("That code is already used.")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Not saved: There is already a condition with the code 'Caries'. Choose another code.");
    expect(within(form).getByLabelText("Label")).toHaveValue("Fracture");                                  // what was typed is still there
  });

  it("retiring needs a reason, takes the condition off the record form, leaves existing findings reading as they did, and can be undone", async () => {
    const user = userEvent.setup();
    server.permissions = MANAGER;
    server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown", state: "Existing" });
    await openChart();
    await user.click(screen.getByText(/^Condition types \(/));
    const row = () => within(catalogue()).getAllByRole("listitem").filter((li) => li.className === "alv-odonto__condition").find((li) => li.textContent!.startsWith("Crown"))!;
    await user.click(within(row()).getByRole("button", { name: "Retire the Crown condition" }));
    const form = screen.getByRole("form", { name: "Retire condition: Crown" });
    await user.click(within(form).getByRole("button", { name: "Retire condition" }));
    expect(within(form).getByText("Say why.")).toBeInTheDocument();                                       // refused before anything is sent
    expect(writes()).toHaveLength(0);
    await user.type(within(form).getByLabelText(/Why is/), "Replaced by a more specific condition");
    await user.click(within(form).getByRole("button", { name: "Retire condition" }));
    await waitFor(() => expect(status()).toHaveTextContent("Crown retired."));
    expect(catalogue().querySelector("summary")).toHaveTextContent("Condition types (5 active, 1 retired)");
    expect(row()).toHaveTextContent("Retired");

    await user.click(tooth("3"));
    expect(within(findingList()).getByText("Crown", { selector: "strong" })).toBeInTheDocument();         // the existing finding still reads "Crown"
    await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
    expect([...(within(screen.getByRole("form", { name: "Record a finding" })).getByLabelText("Condition") as HTMLSelectElement).options].map((o) => o.textContent)).not.toContain("Crown");

    await user.click(within(row()).getByRole("button", { name: "Reactivate the Crown condition" }));
    await user.click(screen.getByRole("button", { name: "Reactivate condition" }));
    await waitFor(() => expect(status()).toHaveTextContent("Crown reactivated."));
    expect(catalogue().querySelector("summary")).toHaveTextContent("Condition types (6 active)");
  });

  it("shows each condition's history with who and why", async () => {
    const user = userEvent.setup();
    server.permissions = MANAGER;
    const t = server.odontogram.addConditionType({ code: "Sealant", label: "Sealant" });
    await openChart();
    await user.click(screen.getByText(/^Condition types \(/));
    await user.click(screen.getByRole("button", { name: `Retire the Sealant condition` }));
    await user.type(screen.getByLabelText(/Why is/), "Not used here");
    await user.click(screen.getByRole("button", { name: "Retire condition" }));
    await waitFor(() => expect(status()).toHaveTextContent("Sealant retired."));
    await user.click(screen.getByLabelText("History of the Sealant condition"));
    const lines = (await within(catalogue()).findAllByText(/^Retired$|^Created$/, { selector: ".alv-clinical__history-what" })).map((e) => e.closest("li")!.textContent!.replace(/\s+/g, " "));
    expect(lines[0]).toMatch(/^Created .* by Dr\. Okafor$/);
    expect(lines[1]).toMatch(/^Retired · Reason: Not used here .* by Dr\. Okafor$/);
    expect(t.id).toBeTruthy();
  });

  it("a stale change to the catalogue shows the conflict banner naming a condition type, and a reload brings the current list", async () => {
    const user = userEvent.setup();
    server.permissions = MANAGER;
    server.odontogram.addConditionType({ code: "Sealant", label: "Sealant" });
    await openChart();
    await user.click(screen.getByText(/^Condition types \(/));
    server.odontogram.bumpType("Sealant");
    await user.click(screen.getByRole("button", { name: "Retire the Sealant condition" }));
    await user.type(screen.getByLabelText(/Why is/), "Not used");
    await user.click(screen.getByRole("button", { name: "Retire condition" }));
    await waitFor(() => expect(status()).toHaveTextContent("Not saved: someone else changed this."));
    expect(screen.getByRole("alert")).toHaveTextContent("This condition type record was updated by someone else");
    await user.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("says the catalogue could not be loaded - and that a finding cannot be recorded - instead of showing an empty list", async () => {
    const user = userEvent.setup();
    server.failClinical("GET /api/odontogram/condition-types", json(500, { error: "server_error", message: "boom" }));
    await openChart();
    expect(screen.getByText(/The condition catalogue could not be loaded/)).toBeInTheDocument();
    await user.click(tooth("3"));
    expect(screen.getByText(/The condition catalogue could not be loaded, so a finding cannot be recorded now/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Record a finding on this tooth" })).not.toBeInTheDocument();
  });
});

describe("a tooth's history and the links on a finding", () => {
  it("lists everything that ever happened to the tooth, oldest first, withdrawn findings included, and follows a change made while it is open", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(within(detail()).getByLabelText("History of tooth 3"));
    expect(await screen.findByText("Recorded", { selector: ".alv-clinical__history-what" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Everything recorded on tooth 3, oldest first" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }));
    await waitFor(() => expect(status()).toHaveTextContent("is now Planned."));
    await user.click(screen.getByRole("button", { name: "Withdraw: Caries (Occlusal surface)" }));
    await user.type(screen.getByLabelText(/Why is/), "Wrong tooth");
    await user.click(screen.getByRole("button", { name: "Withdraw finding" }));
    await waitFor(() => expect(status()).toHaveTextContent("withdrawn."));

    const list = screen.getByRole("list", { name: "Everything recorded on tooth 3, oldest first" });
    await waitFor(() => expect(within(list).getAllByRole("listitem")).toHaveLength(3));
    const lines = within(list).getAllByRole("listitem").map((li) => li.textContent!.replace(/\s+/g, " "));
    expect(lines[0]).toMatch(/^Recorded - Caries, Occlusal surface · Diagnosed .* by Dr\. Okafor$/);
    expect(lines[1]).toMatch(/^State changed - Caries, Occlusal surface · Planned/);
    expect(lines[2]).toMatch(/^Withdrawn - Caries, Occlusal surface · Planned · withdrawn · Reason: Wrong tooth .* by Dr\. Okafor$/);   // gone from the chart, still on the tooth's history
    expect(tooth("3")).toHaveAccessibleName("Tooth 3, upper right first molar: nothing recorded");
  });

  it("says plainly when nothing has ever been recorded on the tooth, and when the history could not load", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("20"));
    await user.click(within(detail()).getByLabelText("History of tooth 20"));
    expect(await screen.findByText("Nothing has ever been recorded on this tooth.")).toBeInTheDocument();

    server.failClinical(`GET /api/patients/${A}/odontogram/teeth/38/history`, json(500, { error: "server_error", message: "boom" }));
    await user.click(tooth("17"));
    await user.click(within(detail()).getByLabelText("History of tooth 17"));
    expect(await screen.findByText("Could not load the tooth's history. Close it and open it again.")).toBeInTheDocument();
  });

  it("shows the records a finding is linked to, with who linked them, and the link in the tooth's history", async () => {
    const user = userEvent.setup();
    const f = server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    server.odontogram.addLink(f.id, "Diagnosis", "DX-1001");
    server.odontogram.addLink(f.id, "Procedure", "PROC-77");
    await openChart();
    await user.click(tooth("3"));
    const links = within(detail()).getByRole("list", { name: "Records linked to Caries (Occlusal surface)" });
    expect(within(links).getAllByRole("listitem").map((li) => li.textContent!.replace(/\s+/g, " ").replace(/ by .*/, ""))).toEqual(["Linked Diagnosis: DX-1001", "Linked Procedure: PROC-77"]);
    expect(links).toHaveTextContent("by Dr. Okafor");

    await user.click(within(detail()).getByLabelText("History of tooth 3"));
    const timeline = await screen.findByRole("list", { name: "Everything recorded on tooth 3, oldest first" });
    const lines = within(timeline).getAllByRole("listitem").map((li) => li.textContent!.replace(/\s+/g, " "));
    expect(lines.map((l) => l.split(" - ")[0])).toEqual(["Recorded", "Linked", "Linked"]);
    expect(lines[1]).toContain("Link: Diagnosis: DX-1001");
    expect(lines[2]).toContain("Link: Procedure: PROC-77");
  });
});

describe("arrow keys", () => {
  it("move between teeth within an arch, to its ends, and to the arch above and below, without selecting anything", async () => {
    const user = userEvent.setup();
    await openChart();
    tooth("3").focus();
    await user.keyboard("{ArrowRight}");
    expect(tooth("4")).toHaveFocus();
    await user.keyboard("{ArrowLeft}{ArrowLeft}");
    expect(tooth("2")).toHaveFocus();
    await user.keyboard("{End}");
    expect(tooth("16")).toHaveFocus();
    await user.keyboard("{ArrowRight}");                                                                  // the end of the arch: stays put
    expect(tooth("16")).toHaveFocus();
    await user.keyboard("{Home}");
    expect(tooth("1")).toHaveFocus();
    await user.keyboard("{ArrowLeft}");
    expect(tooth("1")).toHaveFocus();
    await user.keyboard("{ArrowDown}");
    expect(within(group("Lower teeth")).getAllByRole("button")).toContain(document.activeElement);
    await user.keyboard("{ArrowUp}");
    expect(within(group("Upper teeth")).getAllByRole("button")).toContain(document.activeElement);
    expect(screen.getByText("Select a tooth to see what is recorded on it.")).toBeInTheDocument();      // moving is not selecting
  });

  it("move through the mixed chart's four arches, and Enter still selects", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(screen.getByRole("button", { name: "Mixed (both)" }));
    tooth("8").focus();
    await user.keyboard("{ArrowDown}");
    expect(within(group("Upper primary teeth")).getAllByRole("button")).toContain(document.activeElement);
    await user.keyboard("{ArrowDown}");
    expect(within(group("Lower primary teeth")).getAllByRole("button")).toContain(document.activeElement);
    await user.keyboard("{ArrowDown}");
    expect(within(group("Lower permanent teeth")).getAllByRole("button")).toContain(document.activeElement);
    await user.keyboard("{Enter}");
    expect(document.activeElement).toHaveAttribute("aria-pressed", "true");
  });
});

describe("accessibility", () => {
  it("has no axe violations with the mixed chart, a tooth's history, the catalogue and its add form open", async () => {
    const user = userEvent.setup();
    server.permissions = MANAGER;
    server.odontogram.addFinding(A, { toothKey: "55", surface: "O", condition: "Caries", state: "Diagnosed" });
    server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown", state: "Existing" });
    const { container } = await openChart();
    await user.click(tooth("3"));
    await user.click(within(detail()).getByLabelText("History of tooth 3"));
    await screen.findByText("Recorded", { selector: ".alv-clinical__history-what" });
    await user.click(screen.getByText(/^Condition types \(/));
    await user.click(screen.getByRole("button", { name: "Add a condition type" }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
