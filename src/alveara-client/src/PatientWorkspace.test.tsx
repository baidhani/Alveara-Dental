import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";
import { FakePatientServer, json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-003-C01: the patient workspace, header and search exercised through the real <App /> (real router, shell, auth and patient
 * context) against an in-memory fake of the patient API. What is proven here is the UI's behavior; the rules themselves are proven
 * by the backend tests and the real-backend browser walkthrough.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const B = "bbbbbbbb-0000-0000-0000-000000000002";
const G = "cccccccc-0000-0000-0000-000000000003";

let server: FakePatientServer;

function go(path: string) {
  act(() => {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  });
}

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  server = new FakePatientServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: B, firstName: "Ben", lastName: "Moss", dateOfBirth: "1990-01-01", phone: "555-020-0200" }));
  server.add(makePatient({ id: G, firstName: "Gina", lastName: "Hale", dateOfBirth: "1960-01-01", phone: "555-030-0300" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const header = () => screen.getByRole("region", { name: "Patient context" });

describe("patient header and workspace", () => {
  it("shows the patient in context in the shared header and the details in the workspace, with only the tabs that exist", async () => {
    open(`/patients/${A}`);

    await waitFor(() => expect(within(header()).getByText("Ann Lee")).toBeInTheDocument());
    expect(within(header()).getByText(/DOB 1985-03-09 \(40 y\)/)).toBeInTheDocument();
    expect(within(header()).getByText("Female")).toBeInTheDocument();
    expect(within(header()).getByText("555-010-0100")).toBeInTheDocument();
    expect(await screen.findByLabelText("First name *")).toHaveValue("Ann");

    const tabs = within(screen.getByRole("navigation", { name: "Patient workspace sections" })).getAllByRole("link").map((l) => l.textContent);
    expect(tabs).toEqual(["Details", "Household & guarantor", "History"]); // no placeholder tabs for modules that do not exist
    // nothing invented: the header carries no balance, diagnosis, alert or unsigned-work counts
    expect(header().textContent).not.toMatch(/balance|diagnos|alert|unsigned|allerg/i);
  });

  it("shows 'No patient selected' with a way to find one when none is in context, and Close patient returns to that state", async () => {
    open("/patients");
    expect(await within(await screen.findByRole("region", { name: "Patient context" })).findByText(/No patient selected/)).toBeInTheDocument();
    expect(within(header()).getByRole("link", { name: "Find a patient" })).toHaveAttribute("href", "/patients");

    go(`/patients/${A}`);
    await waitFor(() => expect(within(header()).getByText("Ann Lee")).toBeInTheDocument());
    await userEvent.click(within(header()).getByRole("button", { name: "Close patient" }));
    expect(within(header()).getByText(/No patient selected/)).toBeInTheDocument();
  });

  it("switching patients never leaves the previous patient's identity or data visible while the next one loads", async () => {
    server.patients.get(A)!.householdId = "h1";
    server.patients.get(A)!.householdRelationship = "Self";
    server.patients.get(B)!.householdId = "h1";
    server.patients.get(B)!.householdRelationship = "Spouse";
    open(`/patients/${A}/household`);
    await waitFor(() => expect(within(header()).getByText("Ann Lee")).toBeInTheDocument());

    let release: () => void = () => {};
    server.detailDelay.set(B, new Promise<void>((resolve) => { release = resolve; }));
    await userEvent.click(await screen.findByRole("link", { name: "Ben Moss" })); // switch via the household table

    // while Ben is loading: Ann is gone from the header AND the page; nothing of hers remains on screen
    await waitFor(() => expect(within(header()).getByText("Loading patient…")).toBeInTheDocument());
    expect(within(header()).queryByText("Ann Lee")).not.toBeInTheDocument();
    expect(screen.queryByText(/Ann Lee/)).not.toBeInTheDocument();
    expect(screen.getByText("Loading patient…", { selector: "span" })).toBeInTheDocument();

    release();
    await waitFor(() => expect(within(header()).getByText("Ben Moss")).toBeInTheDocument());
    expect(within(header()).queryByText("Ann Lee")).not.toBeInTheDocument();
  });

  it("discards a slow response for a patient who is no longer the one selected", async () => {
    let releaseA: () => void = () => {};
    server.detailDelay.set(A, new Promise<void>((resolve) => { releaseA = resolve; }));
    open(`/patients/${A}`);
    await waitFor(() => expect(within(header()).getByText("Loading patient…")).toBeInTheDocument());

    go(`/patients/${B}`); // the user moves on before Ann's record has arrived
    await waitFor(() => expect(within(header()).getByText("Ben Moss")).toBeInTheDocument());

    releaseA(); // Ann's late answer must not replace Ben
    await act(async () => { await Promise.resolve(); });
    expect(within(header()).getByText("Ben Moss")).toBeInTheDocument();
    expect(within(header()).queryByText("Ann Lee")).not.toBeInTheDocument();
    expect(await screen.findByLabelText("First name *")).toHaveValue("Ben");
  });

  it("an unknown patient shows a clear not-found message instead of an empty or stale page", async () => {
    open("/patients/dddddddd-0000-0000-0000-000000000009");
    expect(await screen.findByText("That patient was not found", { selector: "p" })).toBeInTheDocument();
    expect(within(header()).getByRole("alert")).toHaveTextContent("That patient was not found.");
    expect(screen.getByRole("link", { name: "Find a patient", description: "" })).toBeInTheDocument();
  });

  it("a server error loading the patient offers a retry", async () => {
    server.fail(`GET /api/patients/${A}`, json(500, { error: "boom" }));
    open(`/patients/${A}`);
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    await waitFor(() => expect(within(header()).getByText("Ann Lee")).toBeInTheDocument());
  });
});

describe("editing a patient", () => {
  it("saves only on request, sends the version it read, and shows the saved values", async () => {
    open(`/patients/${A}`);
    const city = await screen.findByLabelText("City *");
    expect(screen.getByRole("button", { name: "Save changes" })).toBeDisabled(); // nothing changed yet

    await userEvent.clear(city);
    await userEvent.type(city, "Dallas");
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(server.callsTo("PUT", `/api/patients/${A}`)).toHaveLength(1));
    const put = server.callsTo("PUT", `/api/patients/${A}`)[0];
    expect(put.body).toMatchObject({ city: "Dallas", firstName: "Ann", rowVersion: "v1" });
    expect(put.headers["X-CSRF-Token"]).toBe("csrf-1");
    await waitFor(() => expect(screen.getByLabelText("City *")).toHaveValue("Dallas"));
    expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument();
  });

  it("prompts for a required field that was cleared and sends nothing", async () => {
    open(`/patients/${A}`);
    await userEvent.clear(await screen.findByLabelText("Phone *"));
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

    expect(await screen.findByText("Phone is required.")).toBeInTheDocument();
    expect(server.callsTo("PUT", "/api/patients")).toHaveLength(0);
    await waitFor(() => expect(screen.getByLabelText("Phone *")).toHaveFocus());
  });

  it("shows the server's field messages for values it rejects", async () => {
    server.fail(`PUT /api/patients/${A}`, json(400, { error: "validation_failed", message: "x", fieldErrors: { email: "Email must look like name@example.com." } }));
    open(`/patients/${A}`);
    await userEvent.type(await screen.findByLabelText("Email"), "nope");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("Email must look like name@example.com.")).toBeInTheDocument();
  });

  it("a conflicting edit by someone else is never overwritten: the user is told, and Reload brings in the current version", async () => {
    open(`/patients/${A}`);
    const city = await screen.findByLabelText("City *");
    server.patients.get(A)!.city = "Houston"; // someone else saves first...
    server.patients.get(A)!.version = 2;      // ...which moves the version on

    await userEvent.clear(city);
    await userEvent.type(city, "Dallas");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

    const banner = await screen.findByText("Someone else changed this while you were editing");
    expect(banner).toBeInTheDocument();
    expect(server.patients.get(A)!.city).toBe("Houston"); // nothing was overwritten

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(screen.getByLabelText("City *")).toHaveValue("Houston"));
    expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument();
  });

  it("is read-only without the EditPatients permission", async () => {
    server.permissions = ["ViewPatientRecords"];
    open(`/patients/${A}`);

    expect(await screen.findByText("You can view this patient but not change them.")).toBeInTheDocument();
    expect(screen.getByLabelText("First name *")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Save changes" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Inactivate patient" })).not.toBeInTheDocument();
  });

  it("inactivating asks first, keeps the record, and the header then shows Inactive; reactivating restores it", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValueOnce(false).mockReturnValue(true);
    open(`/patients/${A}`);
    const button = await screen.findByRole("button", { name: "Inactivate patient" });

    await userEvent.click(button); // declined
    expect(server.callsTo("PUT", `/api/patients/${A}/active`)).toHaveLength(0);

    await userEvent.click(button); // confirmed
    await waitFor(() => expect(within(header()).getByText("Inactive")).toBeInTheDocument());
    expect(server.callsTo("PUT", `/api/patients/${A}/active`)[0].body).toMatchObject({ isActive: false, rowVersion: "v1" });
    expect(server.patients.has(A)).toBe(true);

    await userEvent.click(await screen.findByRole("button", { name: "Reactivate patient" }));
    await waitFor(() => expect(within(header()).queryByText("Inactive")).not.toBeInTheDocument());
    confirm.mockRestore();
  });

  it("shows why a guarantor cannot be inactivated", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    server.fail(`PUT /api/patients/${A}/active`, json(409, { error: "guarantor_in_use", message: "This patient is the guarantor for 1 active patient. Give them another guarantor before inactivating." }));
    open(`/patients/${A}`);

    await userEvent.click(await screen.findByRole("button", { name: "Inactivate patient" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("guarantor for 1 active patient");
    expect(within(header()).queryByText("Inactive")).not.toBeInTheDocument();
  });
});

describe("history", () => {
  it("lists changes newest first, describing relationship changes without exposing ids", async () => {
    server.history[A] = [
      { id: "1", changedAtUtc: "2026-10-02T10:00:00Z", changedByUserId: "u", changeType: "GuarantorChanged", fieldName: "guarantorPatientId", oldValue: null, newValue: G },
      { id: "2", changedAtUtc: "2026-10-02T09:00:00Z", changedByUserId: "u", changeType: "Updated", fieldName: "city", oldValue: "Austin", newValue: "Dallas" },
      { id: "3", changedAtUtc: "2026-10-02T08:00:00Z", changedByUserId: "u", changeType: "Inactivated", fieldName: "isActive", oldValue: "True", newValue: "False" },
    ];
    open(`/patients/${A}/history`);

    const table = await screen.findByRole("table", { name: /Changes to this patient record/ });
    const rows = within(table).getAllByRole("row").slice(1).map((r) => within(r).getAllByRole("cell").slice(1).map((c) => c.textContent));
    expect(rows).toEqual([["Guarantor", "None", "Set"], ["City", "Austin", "Dallas"], ["Status", "Active", "Inactive"]]);
    expect(table.textContent).not.toContain(G);
    expect(screen.getByText(/Registered /)).toBeInTheDocument();
  });

  it("explains an empty history", async () => {
    open(`/patients/${A}/history`);
    expect(await screen.findByText("No changes yet")).toBeInTheDocument();
  });
});

describe("patient search", () => {
  it("lists patients by name, narrows as you type, and offers inactive ones only on request", async () => {
    server.patients.get(G)!.isActive = false;
    open("/patients");

    const table = await screen.findByRole("table", { name: /Patients, in name order/ });
    expect(within(table).getAllByRole("row").slice(1)).toHaveLength(2); // Ann, Ben (Gina is inactive)

    await userEvent.type(screen.getByLabelText("Search patients"), "ben");
    await waitFor(() => expect(within(screen.getByRole("table")).getAllByRole("row").slice(1)).toHaveLength(1));
    expect(within(screen.getByRole("table")).getByRole("link", { name: "Ben Moss" })).toHaveAttribute("href", `/patients/${B}`);
    expect(screen.getByText("1 patient shown")).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText("Search patients"));
    await userEvent.click(screen.getByLabelText("Include inactive patients"));
    await waitFor(() => expect(within(screen.getByRole("table")).getAllByRole("row").slice(1)).toHaveLength(3));
    expect(within(screen.getByRole("table")).getByText("Inactive")).toBeInTheDocument();
  });

  it("says so when nothing matches, and when the search itself fails", async () => {
    open("/patients");
    await userEvent.type(await screen.findByLabelText("Search patients"), "zzz");
    expect(await screen.findByText("No patients found")).toBeInTheDocument();

    server.fail("GET /api/patients", json(500, { error: "boom" }));
    await userEvent.type(screen.getByLabelText("Search patients"), "q");
    expect(await screen.findByText("Could not search patients")).toBeInTheDocument();
  });

  it("opens a patient from the results, putting them in context", async () => {
    open("/patients");
    await userEvent.click(await screen.findByRole("link", { name: "Ben Moss" }));
    await waitFor(() => expect(within(header()).getByText("Ben Moss")).toBeInTheDocument());
    expect(window.location.pathname).toBe(`/patients/${B}`);
  });

  it("offers Register new patient only with the RegisterPatients permission", async () => {
    server.permissions = ["ViewPatientRecords"];
    open("/patients");
    await screen.findByRole("table");
    expect(screen.queryByRole("link", { name: "Register new patient" })).not.toBeInTheDocument();
  });
});
