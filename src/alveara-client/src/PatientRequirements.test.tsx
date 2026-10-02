import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakePatientServer, json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-003-C01: "validation appropriate to configurable practice requirements" in the UI. The server is the authority (proven by the backend
 * tests and the real-backend walkthrough); these tests prove the forms mark and prompt for what the practice requires, the settings page
 * saves safely, and a form that does not know a requirement still shows the server's message.
 */
const P = "aaaaaaaa-0000-0000-0000-000000000001";
let server: FakePatientServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  server = new FakePatientServer();
  server.add(makePatient({ id: P, firstName: "Ann", lastName: "Lee", email: "", sex: "" }));
  server.permissions = ["ViewPatientRecords", "RegisterPatients", "EditPatients", "ManagePracticeConfiguration"];
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

async function fillRequired(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("First name *"), "Zed");
  await user.type(screen.getByLabelText("Last name *"), "Moss");
  await user.type(screen.getByLabelText("Date of birth *"), "1990-01-01");
  await user.type(screen.getByLabelText("Phone *"), "555-020-0200");
  await user.type(screen.getByLabelText("Address *"), "1 Main St");
  await user.type(screen.getByLabelText("City *"), "Austin");
  await user.type(screen.getByLabelText("State *"), "TX");
  await user.type(screen.getByLabelText("Postal code *"), "78701");
}

describe("registration form with practice requirements", () => {
  it("by default Email and Sex are optional (STORY-003's behavior)", async () => {
    open("/patients/register");
    expect(await screen.findByLabelText("Email")).not.toBeRequired();
    expect(screen.getByLabelText("Sex")).not.toBeRequired();
    expect(screen.getAllByText("Optional")).toHaveLength(2);
  });

  it("marks Email and Sex as required when the practice requires them, and prompts for them without sending anything", async () => {
    server.settings = { requireEmail: true, requireSex: true, rowVersion: "s1", updatedAtUtc: "2026-10-01T00:00:00Z" };
    const user = userEvent.setup();
    open("/patients/register");
    expect(await screen.findByLabelText("Email *")).toBeRequired();
    expect(screen.getByLabelText("Sex *")).toBeRequired();
    expect(screen.queryByText("Optional")).not.toBeInTheDocument();

    await fillRequired(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    expect(await screen.findByText("Email is required.")).toBeInTheDocument();
    expect(screen.getByText("Sex is required.")).toBeInTheDocument();
    expect(server.callsTo("POST", "/api/patients")).toHaveLength(0);
    await waitFor(() => expect(screen.getByLabelText("Sex *")).toHaveFocus()); // the first invalid field in form order
  });

  it("a form that did not learn the requirement still shows the server's per-field message", async () => {
    server.fail("GET /api/patients/registration-settings", json(500, { error: "boom" })); // the load fails: the form falls back to the basics
    server.fail("POST /api/patients", json(400, { error: "validation_failed", message: "x", fieldErrors: { email: "Email is required." } }));
    const user = userEvent.setup();
    open("/patients/register");
    await screen.findByLabelText("Email");
    await fillRequired(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    expect(await screen.findByText("Email is required.")).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
  });

  it("the details editor prompts for a newly required field on a patient who lacks it", async () => {
    server.settings = { requireEmail: true, requireSex: false, rowVersion: "s1", updatedAtUtc: "2026-10-01T00:00:00Z" };
    open(`/patients/${P}`);
    const city = await screen.findByLabelText("City *");
    await userEvent.clear(city);
    await userEvent.type(city, "Dallas");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

    expect(await screen.findByText("Email is required.")).toBeInTheDocument();
    expect(server.callsTo("PUT", `/api/patients/${P}`)).toHaveLength(0);
  });
});

describe("registration requirements settings page", () => {
  it("loads the current choice, saves with the version it read, confirms, and the registration form picks the change up without a reload", async () => {
    const user = userEvent.setup();
    open("/admin/patient-registration");

    const email = await screen.findByLabelText("An email address");
    expect(email).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Save requirements" })).toBeDisabled(); // nothing changed yet
    await user.click(email);
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Save requirements" }));

    expect(await screen.findByText("Registration requirements saved.")).toBeInTheDocument();
    expect(server.callsTo("PUT", "/api/patients/registration-settings")[0].body).toEqual({ requireEmail: true, requireSex: false, rowVersion: "" });
    expect(server.callsTo("PUT", "/api/patients/registration-settings")[0].headers["X-CSRF-Token"]).toBe("csrf-1");

    await user.click(screen.getByRole("link", { name: "Register Patient" }));
    expect(await screen.findByLabelText("Email *")).toBeRequired(); // the open app learned the new rule
  });

  it("a stale save is refused with the shared conflict message and Reload shows the other administrator's choice", async () => {
    const user = userEvent.setup();
    open("/admin/patient-registration");
    const sex = await screen.findByLabelText("The patient's sex");
    server.settings = { requireEmail: true, requireSex: false, rowVersion: "s9", updatedAtUtc: "2026-10-02T00:00:00Z" }; // someone else saved meanwhile

    await user.click(sex);
    await user.click(screen.getByRole("button", { name: "Save requirements" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(screen.getByLabelText("An email address")).toBeChecked());
    expect(screen.getByLabelText("The patient's sex")).not.toBeChecked();
  });

  it("is available only with ManagePracticeConfiguration (page and nav link)", async () => {
    server.permissions = ["ViewPatientRecords", "RegisterPatients"];
    open("/admin/patient-registration");
    expect(await screen.findByText(/don't have permission/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Patient Registration Settings" })).not.toBeInTheDocument();
  });

  it("shows the nav link to a practice manager and has no detectable accessibility violations", async () => {
    const { container } = open("/admin/patient-registration");
    await screen.findByLabelText("An email address");
    expect(screen.getByRole("link", { name: "Patient Registration Settings" })).toHaveAttribute("aria-current", "page");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("offers Retry when the settings cannot be loaded", async () => {
    server.fail("GET /api/patients/registration-settings", json(500, { error: "boom" }));
    open("/admin/patient-registration");
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    expect(await screen.findByLabelText("An email address")).toBeInTheDocument();
  });
});
