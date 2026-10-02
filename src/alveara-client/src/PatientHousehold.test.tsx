import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";
import { FakePatientServer, json, makePatient } from "./test/fakePatientServer";

/** ALV-003-C01: the household and guarantor editor, through the real <App /> against an in-memory fake of the patient API. */
const MOM = "aaaaaaaa-0000-0000-0000-000000000001";
const KID = "bbbbbbbb-0000-0000-0000-000000000002";
const GRANDPA = "cccccccc-0000-0000-0000-000000000003";

let server: FakePatientServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  server = new FakePatientServer();
  server.add(makePatient({ id: MOM, firstName: "Mia", lastName: "Lee", dateOfBirth: "1980-01-01", phone: "555-010-0001" }));
  server.add(makePatient({ id: KID, firstName: "Cal", lastName: "Lee", dateOfBirth: "2015-01-01", phone: "555-010-0002" }));
  server.add(makePatient({ id: GRANDPA, firstName: "Gus", lastName: "Hale", dateOfBirth: "1950-01-01", phone: "555-010-0003" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

async function pick(label: string, search: string, resultName: RegExp) {
  await userEvent.type(screen.getByLabelText(label), search);
  const results = await screen.findByRole("list", { name: `${label} results` });
  await userEvent.click(within(results).getByRole("button", { name: resultName }));
}

describe("household and guarantor editor", () => {
  it("shows household and guarantor as independent: a guarantor outside the household, and members with their own relationship", async () => {
    server.patients.get(MOM)!.householdId = "h1";
    server.patients.get(MOM)!.householdRelationship = "Self";
    server.patients.get(KID)!.householdId = "h1";
    server.patients.get(KID)!.householdRelationship = "Child";
    server.patients.get(KID)!.guarantorId = GRANDPA; // not a member of the household
    open(`/patients/${KID}/household`);

    expect(await screen.findByTestId("current-guarantor")).toHaveTextContent("Gus Hale is responsible for this patient.");
    const table = screen.getByRole("table", { name: "Household members" });
    const rows = within(table).getAllByRole("row").slice(1).map((r) => within(r).getAllByRole("cell").map((c) => c.textContent));
    expect(rows).toEqual([["Cal Lee (this patient)", "Child", "Active"], ["Mia Lee", "Self", "Active"]]);
    expect(within(table).queryByText("Gus Hale")).not.toBeInTheDocument(); // the guarantor is not a household member
  });

  it("says plainly when a patient is responsible for themselves and is in no household", async () => {
    open(`/patients/${MOM}/household`);
    expect(await screen.findByText("This patient is responsible for themselves.")).toBeInTheDocument();
    expect(screen.getByText("This patient is not in a household.")).toBeInTheDocument();
  });

  it("lists the patients this patient is the guarantor for", async () => {
    server.patients.get(KID)!.guarantorId = MOM;
    open(`/patients/${MOM}/household`);
    const list = await screen.findByText("This patient is the guarantor for:");
    expect(within(list.parentElement!).getByRole("link", { name: "Cal Lee" })).toHaveAttribute("href", `/patients/${KID}`);
  });

  it("sets a guarantor by searching for and choosing a patient, sending the version it read", async () => {
    open(`/patients/${KID}/household`);
    await screen.findByTestId("current-guarantor");
    expect(screen.getByRole("button", { name: "Set guarantor" })).toBeDisabled(); // nobody chosen yet

    await pick("Choose a guarantor", "gus", /Gus Hale/);
    expect(screen.getByTestId("picker-selected")).toHaveTextContent("Gus Hale (born 1950-01-01)");
    await userEvent.click(screen.getByRole("button", { name: "Set guarantor" }));

    await waitFor(() => expect(server.callsTo("PUT", `/api/patients/${KID}/guarantor`)).toHaveLength(1));
    expect(server.callsTo("PUT", `/api/patients/${KID}/guarantor`)[0].body).toEqual({ guarantorPatientId: GRANDPA, rowVersion: "v1" });
    expect(await screen.findByText(/Gus Hale/, { selector: "a" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Make self-responsible" })).toBeInTheDocument();
  });

  it("clears a guarantor with Make self-responsible", async () => {
    server.patients.get(KID)!.guarantorId = GRANDPA;
    open(`/patients/${KID}/household`);
    await userEvent.click(await screen.findByRole("button", { name: "Make self-responsible" }));

    await waitFor(() => expect(server.callsTo("PUT", `/api/patients/${KID}/guarantor`)[0].body).toEqual({ guarantorPatientId: null, rowVersion: "v1" }));
    expect(await screen.findByText("This patient is responsible for themselves.")).toBeInTheDocument();
  });

  it("shows the server's reason next to the control when a relationship is invalid", async () => {
    server.fail(`PUT /api/patients/${KID}/guarantor`, json(400, { error: "invalid_relationship", message: "That patient has a guarantor of their own. A guarantor must be responsible for themselves." }));
    open(`/patients/${KID}/household`);
    await screen.findByTestId("current-guarantor");
    await pick("Choose a guarantor", "mia", /Mia Lee/);
    await userEvent.click(screen.getByRole("button", { name: "Set guarantor" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A guarantor must be responsible for themselves.");
    expect(screen.getByTestId("current-guarantor")).toHaveTextContent("responsible for themselves"); // nothing changed
  });

  it("adds another patient to the household using THEIR current version, and shows both members", async () => {
    open(`/patients/${MOM}/household`);
    await screen.findByText("This patient is not in a household.");
    expect(screen.getByRole("button", { name: "Add to household" })).toBeDisabled();

    await pick("Choose a patient to add", "cal", /Cal Lee/);
    expect(screen.getByRole("button", { name: "Add to household" })).toBeDisabled(); // a relationship is needed too
    await userEvent.selectOptions(screen.getByLabelText("Their relationship to this household"), "Child");
    await userEvent.click(screen.getByRole("button", { name: "Add to household" }));

    await waitFor(() => expect(server.callsTo("PUT", `/api/patients/${KID}/household`)).toHaveLength(1));
    expect(server.callsTo("PUT", `/api/patients/${KID}/household`)[0].body).toEqual({ anchorPatientId: MOM, relationship: "Child", rowVersion: "v1" });
    const table = await screen.findByRole("table", { name: "Household members" });
    expect(within(table).getByText("Cal Lee")).toBeInTheDocument();
  });

  it("removes a patient from the household only after confirming, leaving the others in it", async () => {
    server.patients.get(MOM)!.householdId = "h1";
    server.patients.get(MOM)!.householdRelationship = "Self";
    server.patients.get(KID)!.householdId = "h1";
    server.patients.get(KID)!.householdRelationship = "Child";
    const confirm = vi.spyOn(window, "confirm").mockReturnValueOnce(false).mockReturnValue(true);
    open(`/patients/${KID}/household`);
    const button = await screen.findByRole("button", { name: "Remove Cal from household" });

    await userEvent.click(button); // declined
    expect(server.callsTo("PUT", `/api/patients/${KID}/household`)).toHaveLength(0);
    await userEvent.click(button); // confirmed

    await waitFor(() => expect(server.callsTo("PUT", `/api/patients/${KID}/household`)[0].body).toEqual({ anchorPatientId: null, relationship: null, rowVersion: "v1" }));
    expect(await screen.findByText("This patient is not in a household.")).toBeInTheDocument();
    expect(server.patients.get(MOM)!.householdId).toBe("h1");
    confirm.mockRestore();
  });

  it("changes a patient's own relationship label within the household", async () => {
    server.patients.get(MOM)!.householdId = "h1";
    server.patients.get(MOM)!.householdRelationship = "Self";
    server.patients.get(KID)!.householdId = "h1";
    server.patients.get(KID)!.householdRelationship = "Other";
    open(`/patients/${KID}/household`);

    const select = await screen.findByLabelText("Relationship of Cal Lee to the household");
    expect(screen.getByRole("button", { name: "Save relationship" })).toBeDisabled(); // unchanged
    await userEvent.selectOptions(select, "Child");
    await userEvent.click(screen.getByRole("button", { name: "Save relationship" }));

    await waitFor(() => expect(server.callsTo("PUT", `/api/patients/${KID}/household`)[0].body).toEqual({ anchorPatientId: MOM, relationship: "Child", rowVersion: "v1" }));
  });

  it("a stale change shows the shared conflict message instead of overwriting", async () => {
    open(`/patients/${KID}/household`);
    await screen.findByTestId("current-guarantor");
    server.patients.get(KID)!.version = 5; // someone else changed this patient after the page loaded

    await pick("Choose a guarantor", "gus", /Gus Hale/);
    await userEvent.click(screen.getByRole("button", { name: "Set guarantor" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(server.patients.get(KID)!.guarantorId).toBeNull();
  });

  it("the picker is fully keyboard operable: type, Tab to a result, press Enter", async () => {
    open(`/patients/${KID}/household`);
    await screen.findByTestId("current-guarantor");
    const input = screen.getByLabelText("Choose a guarantor");
    await userEvent.type(input, "gus");
    await screen.findByRole("list", { name: "Choose a guarantor results" });

    await userEvent.tab(); // from the input to the first result
    expect(screen.getByRole("button", { name: /Gus Hale/ })).toHaveFocus();
    await userEvent.keyboard("{Enter}");
    expect(screen.getByTestId("picker-selected")).toHaveTextContent("Gus Hale");
  });

  it("never offers the patient themselves (or an existing member) as a choice", async () => {
    server.patients.get(MOM)!.householdId = "h1";
    server.patients.get(MOM)!.householdRelationship = "Self";
    server.patients.get(KID)!.householdId = "h1";
    server.patients.get(KID)!.householdRelationship = "Child";
    open(`/patients/${KID}/household`);
    await screen.findByRole("table", { name: "Household members" });

    await userEvent.type(screen.getByLabelText("Choose a guarantor"), "lee");
    await waitFor(() => expect(screen.getByRole("list", { name: "Choose a guarantor results" })).toBeInTheDocument());
    const names = within(screen.getByRole("list", { name: "Choose a guarantor results" })).getAllByRole("button").map((b) => b.textContent);
    expect(names.some((n) => n?.includes("Cal Lee"))).toBe(false); // the patient themselves
    expect(names.some((n) => n?.includes("Mia Lee"))).toBe(true);

    await userEvent.type(screen.getByLabelText("Choose a patient to add"), "lee");
    expect(await screen.findByText("0 patients found")).toBeInTheDocument(); // both Lees are already members, so nobody is offered
    expect(screen.queryByRole("list", { name: "Choose a patient to add results" })).not.toBeInTheDocument();
  });

  it("is read-only without EditPatients", async () => {
    server.permissions = ["ViewPatientRecords"];
    server.patients.get(KID)!.guarantorId = GRANDPA;
    open(`/patients/${KID}/household`);

    expect(await screen.findByText(/view this patient's household and guarantor but not change them/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Set guarantor" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Choose a guarantor")).not.toBeInTheDocument();
    expect(screen.getByTestId("current-guarantor")).toHaveTextContent("Gus Hale");
  });
});
