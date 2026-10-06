import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-013-C01: the structure and lifecycle of a diagnosis on the Diagnoses screen, through the real <App /> against the in-memory fake of the API. What is proven here is the UI's behaviour for each
 * outcome: coding, source and region recorded (all optional) and shown as chips; the same rules applied before anything is sent, with every problem listed and linked; a tooth and a region excluding each
 * other; an amendment that asks why, keeps what was typed through a refusal and a stale change, and shows what it replaced in the history; resolving and reactivating with a reason; links offered only
 * from this patient's own findings and charts; a role that may only read; and the treatment-plan reference still shown as unresolved throughout. STORY-013's own screen tests are untouched.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;
let encounter: string;

async function openTab() {
  window.history.pushState({}, "", `/patients/${A}/diagnoses`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Diagnoses" });
  await screen.findByRole("heading", { name: "Diagnoses on record" });
  return view;
}
const form = () => screen.getByRole("form", { name: "Record a diagnosis" });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;
const item = (label: string) => screen.getByRole("listitem", { name: `Diagnosis: ${label}` });
const posts = (suffix: string) => server.callsToClinical("POST", suffix);
const act = (user: ReturnType<typeof userEvent.setup>, label: string, name: string) => user.click(within(item(label)).getByRole("button", { name }));

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
  encounter = server.addEncounter(A).id;
});
afterEach(() => vi.unstubAllGlobals());

describe("recording with structure", () => {
  it("stays simple with no coding: the structured boxes are optional and send nothing when empty", async () => {
    const user = userEvent.setup();
    await openTab();
    await user.type(within(form()).getByLabelText("Diagnosis"), "Gingivitis");
    await user.click(within(form()).getByRole("button", { name: "Record diagnosis" }));
    await waitFor(() => expect(posts("/diagnoses")).toHaveLength(1));
    expect(posts("/diagnoses")[0].body).toMatchObject({ codingSystem: null, code: null, source: null, sourceNote: null, regionKey: null });
    expect(item("Gingivitis")).not.toHaveTextContent("Region");
    expect(within(item("Gingivitis")).queryByRole("list", { name: "Context of this diagnosis" })).toBeEmptyDOMElement();
  });

  it("sends the coding, source and region typed, and shows them as chips with the coding never presented as checked", async () => {
    const user = userEvent.setup();
    await openTab();
    const f = within(form());
    await user.type(f.getByLabelText("Diagnosis"), "Generalized periodontitis");
    await user.selectOptions(f.getByLabelText("Oral region (optional)"), "UpperArch");
    await user.selectOptions(f.getByLabelText("Coding system"), "ICD-10-CM");
    await user.type(f.getByLabelText("Code"), "K05.311");
    await user.selectOptions(f.getByLabelText("Where it came from"), "Imported");
    await user.type(f.getByLabelText("Source note (optional)"), "From the old chart");
    await user.click(f.getByRole("button", { name: "Record diagnosis" }));

    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis recorded."));
    expect(posts("/diagnoses")[0].body).toMatchObject({ toothKey: null, regionKey: "UpperArch", codingSystem: "ICD-10-CM", code: "K05.311", source: "Imported", sourceNote: "From the old chart" });
    const chips = within(within(item("Generalized periodontitis")).getByRole("list", { name: "Context of this diagnosis" }));
    expect(chips.getByText("Region: Upper arch")).toBeInTheDocument();
    expect(chips.getByText("ICD-10-CM K05.311")).toBeInTheDocument();
    expect(chips.getByText("Imported: From the old chart")).toBeInTheDocument();
    expect(item("Generalized periodontitis")).not.toHaveTextContent(/validated|verified|checked/i);
    expect(within(form()).getByLabelText("Code")).toHaveValue("");                                      // the form is ready for the next one
  });

  it("is about a tooth or a region: choosing one clears the other", async () => {
    const user = userEvent.setup();
    await openTab();
    const f = within(form());
    await user.selectOptions(f.getByLabelText("Tooth (optional)"), "16");
    await user.selectOptions(f.getByLabelText("Oral region (optional)"), "LowerArch");
    expect(f.getByLabelText("Tooth (optional)")).toHaveValue("");
    await user.selectOptions(f.getByLabelText("Tooth (optional)"), "36");
    expect(f.getByLabelText("Oral region (optional)")).toHaveValue("");
  });

  it("hides the source note until a source is chosen and clears it when the source is cleared", async () => {
    const user = userEvent.setup();
    await openTab();
    const f = within(form());
    expect(f.queryByLabelText("Source note (optional)")).not.toBeInTheDocument();
    await user.selectOptions(f.getByLabelText("Where it came from"), "Mapped");
    await user.type(f.getByLabelText("Source note (optional)"), "note");
    await user.selectOptions(f.getByLabelText("Where it came from"), "");
    await user.type(f.getByLabelText("Diagnosis"), "Gingivitis");
    await user.click(f.getByRole("button", { name: "Record diagnosis" }));
    await waitFor(() => expect(posts("/diagnoses")).toHaveLength(1));
    expect(posts("/diagnoses")[0].body).toMatchObject({ source: null, sourceNote: null });
  });
});

describe("incorrect structure", () => {
  it("sends nothing while the coding is half given or wrong, lists every problem linked to its box, and keeps what was typed", async () => {
    const user = userEvent.setup();
    await openTab();
    const f = within(form());
    await user.type(f.getByLabelText("Diagnosis"), "Perio");
    await user.selectOptions(f.getByLabelText("Coding system"), "Local");
    await user.type(f.getByLabelText("Code"), "A 1");
    await user.click(f.getByRole("button", { name: "Record diagnosis" }));
    const list = await screen.findByRole("alert");
    expect(posts("/diagnoses")).toHaveLength(0);
    expect(within(list).getByText(/A code can contain only letters, digits, dots, hyphens and underscores/)).toBeInTheDocument();
    await user.click(within(list).getByRole("button", { name: "Code" }));
    expect(f.getByLabelText("Code")).toHaveFocus();
    expect(f.getByLabelText("Diagnosis")).toHaveValue("Perio");

    await user.clear(f.getByLabelText("Code"));
    await user.click(f.getByRole("button", { name: "Record diagnosis" }));
    expect(within(await screen.findByRole("alert")).getByText(/A coding system needs its code/)).toBeInTheDocument();
    expect(posts("/diagnoses")).toHaveLength(0);
  });

  it("shows the server's refusal the same way when it disagrees, with everything typed kept", async () => {
    const user = userEvent.setup();
    await openTab();
    server.failClinical(`POST /api/patients/${A}/diagnoses`, json(400, { error: "validation_failed", message: "The diagnosis has entries that need correcting. Nothing was saved.", problems: [{ field: "code", code: "invalid_characters", message: "The server says no." }] }));
    const f = within(form());
    await user.type(f.getByLabelText("Diagnosis"), "Perio");
    await user.selectOptions(f.getByLabelText("Coding system"), "Local");
    await user.type(f.getByLabelText("Code"), "A1");
    await user.click(f.getByRole("button", { name: "Record diagnosis" }));
    expect(within(await screen.findByRole("alert")).getByText(/The server says no/)).toBeInTheDocument();
    expect(f.getByLabelText("Code")).toHaveValue("A1");
  });
});

describe("amending", () => {
  it("changes the structure with a reason, shows what it replaced in the history, and leaves the plan reference unresolved", async () => {
    server.diagnoses.add(A, encounter, { label: "Periodontitis", toothKey: "16", treatmentPlanReference: "plan-2026-07" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Amend");
    const amend = within(screen.getByRole("form", { name: "Amend this diagnosis" }));
    expect(amend.getByLabelText("Tooth (optional)")).toHaveValue("16");                               // starts with what the diagnosis has now
    await user.selectOptions(amend.getByLabelText("Oral region (optional)"), "UpperRight");
    expect(amend.getByLabelText("Tooth (optional)")).toHaveValue("");
    await user.selectOptions(amend.getByLabelText("Coding system"), "Local");
    await user.type(amend.getByLabelText("Code"), "P-9");
    await user.type(amend.getByLabelText("Why is this being amended?"), "Moved to the region and coded");
    await user.click(amend.getByRole("button", { name: "Save amendment" }));

    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis amended."));
    expect(posts("/amend")[0].body).toMatchObject({ toothKey: null, regionKey: "UpperRight", codingSystem: "Local", code: "P-9", source: null, reason: "Moved to the region and coded" });
    const li = item("Periodontitis");
    expect(li).toHaveTextContent("Region: Upper right quadrant");
    expect(li).toHaveTextContent("Local P-9");
    expect(li).toHaveTextContent("Treatment plan reference (unresolved): plan-2026-07");

    await act(user, "Periodontitis", "History");
    const rows = within(await within(li).findByRole("table")).getAllByRole("row").slice(1).map((r) => within(r).getAllByRole("cell").map((c) => c.textContent));
    expect(rows[0][2]).toBe("Recorded");
    expect(rows[0][3]).toBe("Periodontitis (tooth 16 FDI)");                                           // what the amendment replaced is still there
    expect(rows[1][2]).toBe("Amended (structure)");
    expect(rows[1][5]).toBe("Moved to the region and coded");
    expect(rows[1][6]).toBe("region Upper right quadrant; Local P-9");
    expect(rows.every((r) => r[4] === "plan-2026-07 (unresolved)")).toBe(true);
  });

  it("asks why, lists every problem, sends nothing, and keeps what was typed", async () => {
    server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Amend");
    const amend = within(screen.getByRole("form", { name: "Amend this diagnosis" }));
    await user.selectOptions(amend.getByLabelText("Coding system"), "SNODENT");
    await user.click(amend.getByRole("button", { name: "Save amendment" }));
    const list = within(await screen.findByRole("alert"));
    expect(list.getByText(/Say why this diagnosis is being amended/)).toBeInTheDocument();
    expect(list.getByText(/A coding system needs its code/)).toBeInTheDocument();
    expect(posts("/amend")).toHaveLength(0);
    expect(amend.getByLabelText("Coding system")).toHaveValue("SNODENT");
  });

  it("a stale amendment shows the conflict and keeps what was typed", async () => {
    const d = server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Amend");
    server.diagnoses.touch(d.id);
    const amend = within(screen.getByRole("form", { name: "Amend this diagnosis" }));
    await user.selectOptions(amend.getByLabelText("Oral region (optional)"), "FullMouth");
    await user.type(amend.getByLabelText("Why is this being amended?"), "Wider than first thought");
    await user.click(amend.getByRole("button", { name: "Save amendment" }));
    expect(await screen.findByText(/someone else changed this diagnosis/)).toBeInTheDocument();
    expect(amend.getByLabelText("Why is this being amended?")).toHaveValue("Wider than first thought");
  });

  it("a dropped connection keeps what was typed and says nothing was saved", async () => {
    server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Amend");
    server.dropNextResponse("POST /amend");
    const amend = within(screen.getByRole("form", { name: "Amend this diagnosis" }));
    await user.selectOptions(amend.getByLabelText("Coding system"), "Local");
    await user.type(amend.getByLabelText("Code"), "A1");
    await user.type(amend.getByLabelText("Why is this being amended?"), "Coded");
    await user.click(amend.getByRole("button", { name: "Save amendment" }));
    expect(await screen.findByText(/the connection dropped/)).toBeInTheDocument();
    expect(amend.getByLabelText("Code")).toHaveValue("A1");
  });
});

describe("resolving and reactivating", () => {
  it("marks a diagnosis resolved with a reason, keeps it on the list marked Resolved, and makes it active again with a reason", async () => {
    server.diagnoses.add(A, encounter, { label: "Gingivitis" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Gingivitis", "Mark resolved");
    const resolve = within(screen.getByRole("form", { name: "Mark this diagnosis resolved" }));
    expect(resolve.getByRole("button", { name: "Mark resolved" })).toBeDisabled();                    // a reason is needed first
    await user.type(resolve.getByLabelText("Why is this diagnosis resolved?"), "Healed at review");
    await user.click(resolve.getByRole("button", { name: "Mark resolved" }));
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis marked resolved."));
    expect(posts("/resolve")[0].body).toMatchObject({ reason: "Healed at review" });
    expect(item("Gingivitis")).toHaveTextContent("Resolved");
    expect(within(item("Gingivitis")).queryByRole("button", { name: "Mark resolved" })).not.toBeInTheDocument();

    await act(user, "Gingivitis", "Make active again");
    const back = within(screen.getByRole("form", { name: "Make this diagnosis active again" }));
    await user.type(back.getByLabelText("Why is this diagnosis active again?"), "Came back");
    await user.click(back.getByRole("button", { name: "Make active again" }));
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis made active again."));
    expect(item("Gingivitis")).not.toHaveTextContent("Resolved");

    await act(user, "Gingivitis", "History");
    const rows = within(await within(item("Gingivitis")).findByRole("table")).getAllByRole("row").slice(1).map((r) => within(r).getAllByRole("cell")[2].textContent);
    expect(rows).toEqual(["Recorded", "Marked resolved", "Made active again"]);
  });

  it("a stale resolve shows the conflict, says it was not resolved and keeps the reason", async () => {
    const d = server.diagnoses.add(A, encounter, { label: "Gingivitis" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Gingivitis", "Mark resolved");
    server.diagnoses.touch(d.id);
    const resolve = within(screen.getByRole("form", { name: "Mark this diagnosis resolved" }));
    await user.type(resolve.getByLabelText("Why is this diagnosis resolved?"), "Healed");
    await user.click(resolve.getByRole("button", { name: "Mark resolved" }));
    expect(await screen.findByText("Not resolved: someone else changed this diagnosis. Reload and try again.")).toBeInTheDocument();
    expect(resolve.getByLabelText("Why is this diagnosis resolved?")).toHaveValue("Healed");
  });

  it("a withdrawn diagnosis offers none of it", async () => {
    server.diagnoses.add(A, encounter, { label: "Wrong one", status: "Withdrawn" });
    const user = userEvent.setup();
    await openTab();
    await user.click(screen.getByLabelText("Show withdrawn diagnoses"));
    const li = await screen.findByRole("listitem", { name: "Diagnosis: Wrong one" });
    for (const name of ["Correct", "Amend", "Mark resolved", "Make active again", "Link", "Withdraw"]) expect(within(li).queryByRole("button", { name })).not.toBeInTheDocument();
    expect(within(li).getByRole("button", { name: "History" })).toBeInTheDocument();
  });
});

describe("linking", () => {
  it("offers only this patient's own findings and charts, links one with who and when, and does not offer it again", async () => {
    server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown" });
    server.odontogram.addFinding("someone-else", { toothKey: "26", condition: "Crown" });
    server.perio.addChart(A, { readings: [], recordedAtUtc: "2026-09-01T10:00:00Z" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Link");
    const link = within(await screen.findByRole("form", { name: "Link this diagnosis" }));
    const options = link.getAllByRole("option").map((o) => o.textContent);
    expect(options).toHaveLength(3);                                                                    // the prompt and this patient's two records
    expect(options.join("|")).toContain("Finding: Crown on tooth 16");
    expect(options.join("|")).toContain("Periodontal chart of");
    expect(options.join("|")).not.toContain("tooth 26");
    await user.selectOptions(link.getByLabelText("Link to"), options[1]!);
    await user.click(link.getByRole("button", { name: "Link diagnosis" }));

    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis linked."));
    expect(posts("/links")[0].body).toMatchObject({ linkType: "Finding" });
    const links = within(within(item("Periodontitis")).getByRole("list", { name: "Linked records" }));
    expect(links.getByText(/Crown on tooth 16/)).toBeInTheDocument();
    expect(links.getByText(/linked by Dr. Okafor/)).toBeInTheDocument();

    await act(user, "Periodontitis", "Link");
    const again = within(await screen.findByRole("form", { name: "Link this diagnosis" }));
    expect(again.getAllByRole("option").map((o) => o.textContent).join("|")).not.toContain("Crown on tooth 16");
  });

  it("says there is nothing to link when there are no findings or charts, rather than showing an empty box", async () => {
    server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Link");
    expect(await screen.findByRole("note")).toHaveTextContent("There is nothing left to link");
    expect(screen.queryByLabelText("Link to")).not.toBeInTheDocument();
  });

  it("a list that cannot load says so instead of showing none", async () => {
    server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    server.failClinical(`GET /api/patients/${A}/periodontal/charts`, json(500, { error: "server_error", message: "boom" }));
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Link");
    expect(await screen.findByText(/Could not load this patient's findings and charts/)).toBeInTheDocument();
    expect(screen.queryByText(/nothing left to link/)).not.toBeInTheDocument();
  });

  it("a refused link says so and changes nothing", async () => {
    const d = server.diagnoses.add(A, encounter, { label: "Periodontitis" });
    server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown" });
    server.failClinical(`POST /api/diagnoses/${d.id}/links`, json(404, { error: "link_target_not_found", message: "That record was not found for this patient. Choose one of this patient's records.", problems: [] }));
    const user = userEvent.setup();
    await openTab();
    await act(user, "Periodontitis", "Link");
    const link = within(await screen.findByRole("form", { name: "Link this diagnosis" }));
    await user.selectOptions(link.getByLabelText("Link to"), link.getAllByRole("option")[1]!);
    await user.click(link.getByRole("button", { name: "Link diagnosis" }));
    expect(await screen.findByText(/Not linked: .*not found for this patient/)).toBeInTheDocument();
    expect(within(item("Periodontitis")).queryByRole("list", { name: "Linked records" })).not.toBeInTheDocument();
  });
});

describe("a role that can only read", () => {
  it("sees the chips, the links and the history, with no form and no way to change anything", async () => {
    const finding = server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown" });
    const d = server.diagnoses.add(A, encounter, { label: "Periodontitis", codingSystem: "Local", code: "A1", regionKey: "UpperArch", status: "Resolved" });
    server.diagnoses.addLink(d.id, "Finding", finding.id, "Crown on tooth 16");
    server.permissions = READ_ONLY;
    await openTab();
    const li = item("Periodontitis");
    expect(li).toHaveTextContent("Local A1");
    expect(li).toHaveTextContent("Region: Upper arch");
    expect(li).toHaveTextContent("Resolved");
    expect(within(li).getByRole("list", { name: "Linked records" })).toHaveTextContent("Crown on tooth 16");
    for (const name of ["Correct", "Amend", "Mark resolved", "Make active again", "Link", "Withdraw"]) expect(within(li).queryByRole("button", { name })).not.toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Record a diagnosis" })).not.toBeInTheDocument();
    expect(within(li).getByRole("button", { name: "History" })).toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no axe violations with chips and links shown, and with the amend, resolve and link forms open", async () => {
    const finding = server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown" });
    const d = server.diagnoses.add(A, encounter, { label: "Periodontitis", codingSystem: "Local", code: "A1", regionKey: "UpperArch", source: "Imported", sourceNote: "From the old chart", treatmentPlanReference: "plan-1" });
    server.diagnoses.addLink(d.id, "Finding", finding.id, "Crown on tooth 16");
    server.perio.addChart(A, { readings: [], recordedAtUtc: "2026-09-01T10:00:00Z" });
    const user = userEvent.setup();
    const { container } = await openTab();
    expect(await axe(container)).toHaveNoViolations();
    await act(user, "Periodontitis", "Amend");
    await screen.findByRole("form", { name: "Amend this diagnosis" });
    expect(await axe(container)).toHaveNoViolations();
    await act(user, "Periodontitis", "Mark resolved");
    await screen.findByRole("form", { name: "Mark this diagnosis resolved" });
    expect(await axe(container)).toHaveNoViolations();
    await act(user, "Periodontitis", "Link");
    await screen.findByRole("form", { name: "Link this diagnosis" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
