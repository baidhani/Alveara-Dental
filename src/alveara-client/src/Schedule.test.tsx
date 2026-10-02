import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeScheduleServer } from "./test/fakeScheduleServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * STORY-004: the schedule page through the real <App /> (real router, shell, auth, patient picker) against an in-memory fake of the scheduling
 * API. The conflict rules themselves are proven by the backend tests and the real-backend walkthrough; what is proven here is that the page asks
 * correctly, explains each outcome in plain words, never books twice on a retry, and respects the user's permissions.
 */
const ANN = "aaaaaaaa-0000-0000-0000-000000000001";
const BO = "bbbbbbbb-0000-0000-0000-000000000002";
const DAY = "2030-01-14";

let server: FakeScheduleServer;

function open(path = "/schedule") {
  window.history.pushState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  server = new FakeScheduleServer();
  server.add(makePatient({ id: ANN, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: BO, firstName: "Bo", lastName: "Kim", dateOfBirth: "1990-01-01", phone: "555-020-0200" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const bookCalls = () => server.callsToSchedule("POST", "/api/appointments");

async function pickPatient(name: string, query = name.split(" ")[0]) {
  await userEvent.type(await screen.findByLabelText(/Patient/, { selector: "input" }), query);
  await userEvent.click(await screen.findByRole("button", { name: new RegExp(name) }));
}

async function fill({ provider = "Dr. Rivera", operatory = "Op 1", type = /Exam/, date = DAY, time = "09:00", duration = "" } = {}) {
  await userEvent.selectOptions(await screen.findByLabelText("Provider *"), provider);
  await userEvent.selectOptions(screen.getByLabelText("Operatory *"), operatory);
  await userEvent.selectOptions(screen.getByLabelText("Appointment type *"), screen.getByRole("option", { name: type }));
  const d = screen.getByLabelText("Date *");
  await userEvent.clear(d);
  await userEvent.type(d, date);
  const t = screen.getByLabelText("Start time *");
  await userEvent.clear(t);
  await userEvent.type(t, time);
  if (duration) await userEvent.type(screen.getByLabelText(/Duration in minutes/), duration);
}

const book = async () => userEvent.click(await screen.findByRole("button", { name: /Book (appointment|again)/ }));

describe("the schedule page and its permissions", () => {
  it("is in the navigation and shows the booking form to roles that can book", async () => {
    open();
    expect(await screen.findByRole("form", { name: "Schedule an appointment" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Schedule" })).toHaveAttribute("aria-current", "page");
  });

  it("a role that can only view the schedule sees the day's appointments but no booking form", async () => {
    server.permissions = ["ViewSchedule"];
    server.addAppointment({ start: `${DAY}T09:00` });
    open();
    expect(await screen.findByText(/can view the schedule but not book/)).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Schedule an appointment" })).not.toBeInTheDocument();
  });

  it("without the permission to view the schedule neither the link nor the page is available", async () => {
    server.permissions = ["ViewPatientRecords"];
    open();
    await waitFor(() => expect(screen.queryByRole("link", { name: "Schedule" })).not.toBeInTheDocument());
    expect(screen.queryByRole("form", { name: "Schedule an appointment" })).not.toBeInTheDocument();
    expect(server.callsToSchedule("GET", "/api/appointments")).toHaveLength(0);
  });

  it("shows an error when the scheduling options cannot be loaded", async () => {
    server.permissions = ["ViewSchedule", "ManageAppointments"];
    vi.stubGlobal("fetch", vi.fn((url: string) => (String(url).includes("/api/config/scheduling") ? Promise.resolve(json(500, { error: "boom" })) : Promise.resolve(json(200, { permissions: server.permissions, username: "u", role: "FrontDesk", sessionExpiresAtUtc: new Date(Date.now() + 1_800_000).toISOString() })))));
    open();
    expect(await screen.findByText("Could not load the scheduling options")).toBeInTheDocument();
  });
});

describe("booking", () => {
  it("books an available provider, confirms it in words and lists it in the day's schedule, sending the key and CSRF token", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill();
    await book();

    expect(await screen.findByText("Appointment confirmed.")).toBeInTheDocument();
    expect(screen.getByText("Appointment confirmed.").parentElement).toHaveTextContent("Ann Lee with Dr. Rivera in Op 1, 2030-01-14 09:00–10:00 (Exam)");
    const call = bookCalls()[0];
    expect(call.body).toMatchObject({ patientId: ANN, providerId: "prov-a", operatoryId: "op-1", appointmentTypeId: "type-60", startLocal: `${DAY}T09:00`, durationMinutes: null });
    expect(call.headers["Idempotency-Key"]).toMatch(/^book-/);
    expect(call.headers["X-CSRF-Token"]).toBe("csrf-1");
    const table = await screen.findByRole("table", { name: /Booked appointments/ });
    expect(within(table).getByText("09:00–10:00")).toBeInTheDocument();
    expect(screen.queryByText(/Ann Lee/, { selector: "span" })).not.toBeInTheDocument(); // the patient picker is cleared for the next booking
  });

  it("uses the typed duration when one is given", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill({ duration: "90" });
    await book();

    expect(await screen.findByText("Appointment confirmed.")).toBeInTheDocument();
    expect(bookCalls()[0].body).toMatchObject({ durationMinutes: 90 });
    expect(screen.getByText("Appointment confirmed.").parentElement).toHaveTextContent("09:00–10:30");
  });

  it("asks for the missing details instead of sending anything", async () => {
    open();
    await book();
    expect(await screen.findByText(/Choose the patient, provider, operatory, appointment type, date and time first/)).toBeInTheDocument();
    expect(bookCalls()).toHaveLength(0);
  });

  it("a second click while the request is going out sends one request", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill();
    await userEvent.dblClick(await screen.findByRole("button", { name: "Book appointment" }));

    await screen.findByText("Appointment confirmed.");
    expect(bookCalls()).toHaveLength(1);
  });
});

describe("after a confirmation", () => {
  it("Book stays disabled until something changes, so a quick second click cannot replace the confirmation", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill();
    await book();
    await screen.findByText("Appointment confirmed.");
    expect(screen.getByRole("button", { name: "Book appointment" })).toBeDisabled();

    await pickPatient("Bo Kim");
    expect(screen.getByRole("button", { name: "Book appointment" })).toBeEnabled();
  });
});

describe("refusals are explained in plain words and nothing is booked", () => {
  it("a double-booked provider: names the provider and the time already taken", async () => {
    server.addAppointment({ start: `${DAY}T09:00`, minutes: 60 });
    open();
    await pickPatient("Bo Kim");
    await fill({ operatory: "Op 2", time: "09:30" });
    await book();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Dr. Rivera already has an appointment from 09:00 to 10:00.");
    expect(alert).toHaveTextContent("Nothing was booked");
    expect(server.appointments).toHaveLength(1);
  });

  it("an operatory already in use: names the operatory and the time", async () => {
    server.addAppointment({ start: `${DAY}T09:00`, minutes: 60 });
    open();
    await pickPatient("Bo Kim");
    await fill({ provider: "Dr. Patel", operatory: "Op 1", time: "09:30" });
    await book();

    expect(await screen.findByRole("alert")).toHaveTextContent("Op 1 is already in use from 09:00 to 10:00.");
    expect(server.appointments).toHaveLength(1);
  });

  it("an unavailable provider: gives the reason", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill({ time: "06:00" });
    await book();

    expect(await screen.findByRole("alert")).toHaveTextContent("Dr. Rivera is not available: that time is outside the provider's working hours.");
    expect(server.appointments).toHaveLength(0);
  });

  it("an incorrect duration: shows the server's message", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill({ duration: "7" });
    await book();

    expect(await screen.findByRole("alert")).toHaveTextContent("Duration must be between 5 and 480 minutes, in steps of 5.");
    expect(server.appointments).toHaveLength(0);
  });

  it("the refusal clears when the patient is changed, and a corrected request then books", async () => {
    server.addAppointment({ start: `${DAY}T09:00`, minutes: 60 });
    open();
    await pickPatient("Bo Kim");
    await fill({ operatory: "Op 2", time: "09:30" });
    await book();
    await screen.findByRole("alert");

    const time = screen.getByLabelText("Start time *");
    await userEvent.clear(time);
    await userEvent.type(time, "10:00"); // exactly when the other appointment ends
    await book();

    expect(await screen.findByText("Appointment confirmed.")).toBeInTheDocument();
    expect(server.appointments).toHaveLength(2);
    expect(new Set(bookCalls().map((c) => c.headers["Idempotency-Key"])).size).toBe(2); // a changed request gets a new key
  });
});

describe("a dropped connection never books twice", () => {
  it("says the outcome is unknown, and Book again reuses the same key so exactly one appointment exists", async () => {
    open();
    await pickPatient("Ann Lee");
    await fill();
    server.dropNextBookResponse = true;
    await book();

    expect(await screen.findByText(/could not confirm whether the appointment was booked/)).toBeInTheDocument();
    expect(screen.getByText(/cannot be booked twice/)).toBeInTheDocument();
    expect(server.appointments).toHaveLength(1); // it WAS stored; the UI did not know

    await userEvent.click(screen.getByRole("button", { name: "Book again" }));

    expect(await screen.findByText("Appointment confirmed.")).toBeInTheDocument();
    const keys = bookCalls().map((c) => c.headers["Idempotency-Key"]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(server.appointments).toHaveLength(1);
  });
});

describe("the day's schedule", () => {
  it("lists the day's appointments earliest first and can be filtered by provider", async () => {
    server.addAppointment({ start: `${DAY}T11:00`, patientName: "Late Patient" });
    server.addAppointment({ start: `${DAY}T09:00`, provider: "prov-b", operatory: "op-2", patientName: "Early Patient" });
    open();
    const date = await screen.findByLabelText("Day");
    await userEvent.clear(date);
    await userEvent.type(date, DAY);

    const rows = async () => within(await screen.findByRole("table", { name: /Booked appointments/ })).getAllByRole("row").slice(1).map((r) => r.textContent);
    await waitFor(async () => expect((await rows())[0]).toContain("Early Patient"));
    expect((await rows())[1]).toContain("Late Patient");

    await userEvent.selectOptions(screen.getByLabelText("Provider"), "Dr. Patel");
    await waitFor(async () => expect(await rows()).toHaveLength(1));
    expect((await rows())[0]).toContain("Early Patient");
  });

  it("says nothing is booked on an empty day", async () => {
    open();
    expect(await screen.findByText("Nothing booked")).toBeInTheDocument();
  });

  it("shows an error when the schedule cannot be loaded", async () => {
    server.failSchedule("GET /api/appointments", json(500, { error: "boom" }));
    open();
    expect(await screen.findByText("Could not load the schedule")).toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("the form, a refusal and a confirmation have no detectable violations", async () => {
    server.addAppointment({ start: `${DAY}T09:00`, minutes: 60 });
    const { container } = open();
    await screen.findByRole("form", { name: "Schedule an appointment" });
    expect(await axe(container)).toHaveNoViolations();

    await pickPatient("Bo Kim");
    await fill({ operatory: "Op 2", time: "09:30" });
    await book();
    await screen.findByRole("alert");
    expect(await axe(container)).toHaveNoViolations();

    const time = screen.getByLabelText("Start time *");
    await userEvent.clear(time);
    await userEvent.type(time, "13:00");
    await book();
    await screen.findByText("Appointment confirmed.");
    expect(await axe(container)).toHaveNoViolations();
  });
});
