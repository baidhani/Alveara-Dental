import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeCalendarServer } from "./test/fakeCalendarServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-004-C01: the calendar through the real <App /> (real router, shell, auth, patient picker) against an in-memory fake of the scheduling API.
 * The scheduling rules are proven by the backend tests and the real-backend walkthrough; what is proven here is that the calendar shows the server's
 * assignments faithfully (positions, filters, statuses), that every action asks correctly and explains each outcome in words, that stale edits and
 * dropped connections are handled, and that people without the permission cannot change anything.
 */
const ANN = "aaaaaaaa-0000-0000-0000-000000000001";
const BO = "bbbbbbbb-0000-0000-0000-000000000002";
const DAY = "2030-01-14"; // a Monday
const PAST = "2020-01-13"; // long past, so "the start time has passed" without faking the clock

let server: FakeCalendarServer;

function open(path = "/calendar") {
  window.history.pushState({}, "", path);
  return render(<App />);
}

async function openOn(day: string) {
  const view = open();
  const date = await screen.findByLabelText("Date");
  fireEvent.change(date, { target: { value: day } });
  await screen.findByRole("heading", { name: /\d{4}-\d{2}-\d{2}/ });
  return view;
}

beforeEach(() => {
  server = new FakeCalendarServer();
  server.add(makePatient({ id: ANN, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: BO, firstName: "Bo", lastName: "Kim", dateOfBirth: "1990-01-01", phone: "555-020-0200" }));
  for (const p of server.providers) p.weeklyAvailability.push({ dayOfWeek: 1, startLocal: "08:00", endLocal: "17:00" } as never);
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const block = (id: string) => document.querySelector(`[data-appointment="${id}"]`) as HTMLElement;
const drawer = () => screen.getByRole("dialog");
const calls = (method: string, suffix: string) => server.callsToSchedule(method, "/api/appointments").filter((c) => c.url.includes(suffix));

describe("the day view", () => {
  it("shows one column per provider with each appointment placed by its time and duration (one pixel per minute from 07:00)", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, minutes: 60, patientName: "Ann Lee" });
    const b = server.seed({ start: `${DAY}T13:30`, minutes: 90, provider: "prov-b", operatory: "op-2", patientName: "Bo Kim" });
    await openOn(DAY);

    const heading = screen.getByRole("heading", { name: "Monday 2030-01-14" });
    expect(heading).toBeInTheDocument();
    const columns = document.querySelectorAll(".cal__column");
    expect(columns).toHaveLength(2);
    expect(within(columns[0] as HTMLElement).getByText("Dr. Rivera")).toBeInTheDocument();
    expect(within(columns[1] as HTMLElement).getByText("Dr. Patel")).toBeInTheDocument();

    await waitFor(() => expect(block(a.id)).toBeInTheDocument());
    expect(block(a.id).parentElement).toBe(columns[0].querySelector(".cal__body"));
    expect(block(a.id).style.top).toBe("120px"); // 09:00 is 120 minutes after 07:00
    expect(block(a.id).style.height).toBe("60px");
    expect(block(b.id).parentElement).toBe(columns[1].querySelector(".cal__body"));
    expect(block(b.id).style.top).toBe("390px"); // 13:30
    expect(block(b.id).style.height).toBe("90px");
    expect(block(a.id)).toHaveAccessibleName("09:00 to 10:00, Ann Lee, Exam, Dr. Rivera, Op 1, Scheduled");
  });

  it("shades the hours outside a provider's working hours and marks the working window", async () => {
    await openOn(DAY);
    await waitFor(() => expect(document.querySelectorAll(".cal__working")).toHaveLength(2)); // both providers work Monday 08:00-17:00
    const working = document.querySelector('[data-working="08:00-17:00"]') as HTMLElement;
    expect(working.style.top).toBe("60px");
    expect(working.style.height).toBe("540px");
    expect(document.querySelector(".cal__body--off")).toBeInTheDocument();
  });

  it("filters by provider (one column) and by operatory, asking the server for the same filter", async () => {
    server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    server.seed({ start: `${DAY}T10:00`, provider: "prov-b", operatory: "op-2", patientName: "Bo Kim" });
    await openOn(DAY);
    await waitFor(() => expect(document.querySelectorAll(".cal__block")).toHaveLength(2));

    await userEvent.selectOptions(screen.getByLabelText("Provider"), "Dr. Patel");
    await waitFor(() => expect(document.querySelectorAll(".cal__column")).toHaveLength(1));
    await waitFor(() => expect(document.querySelectorAll(".cal__block")).toHaveLength(1));
    expect(screen.getByRole("button", { name: /Bo Kim/ })).toBeInTheDocument();
    expect(calls("GET", "providerId=prov-b").length).toBeGreaterThan(0);

    await userEvent.selectOptions(screen.getByLabelText("Provider"), "All providers");
    await userEvent.selectOptions(screen.getByLabelText("Operatory"), "Op 1");
    await waitFor(() => expect(document.querySelectorAll(".cal__block")).toHaveLength(1));
    expect(screen.getByRole("button", { name: /Ann Lee/ })).toBeInTheDocument();
    expect(calls("GET", "operatoryId=op-1").length).toBeGreaterThan(0);
  });

  it("moves a day or a week at a time, jumps to a date, and the heading and the requested range follow", async () => {
    await openOn(DAY);
    await userEvent.click(screen.getByRole("button", { name: "Next day" }));
    expect(await screen.findByRole("heading", { name: "Tuesday 2030-01-15" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Previous day" }));
    await userEvent.click(screen.getByRole("button", { name: "Previous day" }));
    expect(await screen.findByRole("heading", { name: "Sunday 2030-01-13" })).toBeInTheDocument();
    expect(calls("GET", "from=2030-01-13&to=2030-01-13").length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole("button", { name: "Today" }));
    await waitFor(() => expect(screen.getByLabelText("Date")).not.toHaveValue("2030-01-13"));
  });

  it("every request for the calendar asks for all statuses so cancelled and no-show appointments stay visible", async () => {
    await openOn(DAY);
    await waitFor(() => expect(calls("GET", "includeAll=true").length).toBeGreaterThan(0));
  });
});

describe("the week view", () => {
  it("shows Monday to Sunday for the week containing the chosen day and places overlapping appointments side by side", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, minutes: 60, patientName: "Ann Lee" });
    const b = server.seed({ start: `${DAY}T09:30`, minutes: 60, provider: "prov-b", operatory: "op-2", patientName: "Bo Kim" });
    const c = server.seed({ start: "2030-01-16T11:00", minutes: 30, patientName: "Cy Poe" });
    await openOn("2030-01-17"); // a Thursday
    await userEvent.click(screen.getByRole("button", { name: "Week" }));

    expect(await screen.findByRole("heading", { name: "Week of 2030-01-14 to 2030-01-20" })).toBeInTheDocument();
    const heads = [...document.querySelectorAll(".cal__column .cal__head strong")].map((e) => e.textContent);
    expect(heads).toEqual(["Monday 2030-01-14", "Tuesday 2030-01-15", "Wednesday 2030-01-16", "Thursday 2030-01-17", "Friday 2030-01-18", "Saturday 2030-01-19", "Sunday 2030-01-20"]);
    await waitFor(() => expect(block(a.id)).toBeInTheDocument());
    expect(block(a.id).parentElement).toBe(block(b.id).parentElement); // same day column
    expect(block(a.id).style.left).toBe("0%");
    expect(block(b.id).style.left).toBe("50%"); // overlapping: side by side
    expect(block(a.id).style.width).toBe("calc(50% - 4px)");
    expect(block(c.id).style.width).toBe("calc(100% - 4px)"); // alone on its day
    expect(calls("GET", "from=2030-01-14&to=2030-01-20").length).toBeGreaterThan(0);
  });
});

describe("cancelled and no-show appointments", () => {
  it("stay on the calendar, distinct from scheduled ones and labelled in words", async () => {
    const live = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const cancelled = server.seed({ start: `${DAY}T10:00`, status: "Cancelled", cancelReason: "Illness", patientName: "Bo Kim", provider: "prov-b", operatory: "op-2" });
    const noShow = server.seed({ start: `${DAY}T11:00`, status: "NoShow", patientName: "Cy Poe" });
    await openOn(DAY);
    await waitFor(() => expect(block(live.id)).toBeInTheDocument());

    expect(block(live.id).dataset.status).toBe("Scheduled");
    expect(block(cancelled.id).dataset.status).toBe("Cancelled");
    expect(block(cancelled.id)).toHaveClass("cal__block--cancelled");
    expect(block(cancelled.id)).toHaveTextContent("10:00–11:00 · Cancelled"); // the status is written in the block's first line
    expect(block(noShow.id)).toHaveClass("cal__block--noshow");
    expect(block(noShow.id)).toHaveTextContent("11:00–12:00 · No-show");
    expect(block(live.id)).not.toHaveTextContent("Cancelled");
    expect(block(live.id)).not.toHaveTextContent(" · No-show");
    expect(block(cancelled.id)).toHaveAccessibleName(/Cancelled$/);
  });
});

describe("permissions", () => {
  it("a role that can only view the schedule sees the calendar and appointment details but has no way to book or change anything", async () => {
    server.permissions = ["ViewSchedule"];
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    await openOn(DAY);
    await waitFor(() => expect(block(a.id)).toBeInTheDocument());

    expect(screen.queryByRole("button", { name: "New appointment" })).not.toBeInTheDocument();
    await userEvent.click(block(a.id));
    expect(within(drawer()).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument();
    for (const name of ["Reschedule", "Cancel appointment", "Mark no-show", "Save note"]) expect(within(drawer()).queryByRole("button", { name })).not.toBeInTheDocument();
    expect(within(drawer()).getByLabelText("Note")).toBeDisabled();
    // clicking the empty grid does not start a booking either
    fireEvent.click(document.querySelector(".cal__body") as HTMLElement);
    expect(screen.queryByRole("form", { name: "New appointment" })).not.toBeInTheDocument();
  });

  it("without the permission to view the schedule neither the Calendar link nor the page is available", async () => {
    server.permissions = ["ViewPatientRecords"];
    open();
    await waitFor(() => expect(screen.queryByRole("link", { name: "Calendar" })).not.toBeInTheDocument());
    expect(screen.queryByRole("group", { name: /calendar for/i })).not.toBeInTheDocument();
  });

  it("is in the navigation and shows the New appointment button to roles that can book", async () => {
    open();
    expect(await screen.findByRole("link", { name: "Calendar" })).toHaveAttribute("aria-current", "page");
    expect(await screen.findByRole("button", { name: "New appointment" })).toBeInTheDocument();
  });
});

describe("loading problems", () => {
  it("says so when the scheduling options cannot be loaded", async () => {
    vi.stubGlobal("fetch", vi.fn((url: string) => (String(url).includes("/api/config/scheduling") ? Promise.resolve(json(500, { error: "boom" })) : Promise.resolve(json(200, { permissions: ["ViewSchedule"], username: "u", role: "FrontDesk", sessionExpiresAtUtc: new Date(Date.now() + 1_800_000).toISOString() })))));
    open();
    expect(await screen.findByText("Could not load the calendar")).toBeInTheDocument();
  });

  it("offers Retry when the appointments cannot be loaded", async () => {
    server.failSchedule("GET /api/appointments", json(500, { error: "boom" }));
    open();
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    await waitFor(() => expect(document.querySelector(".cal")).toBeInTheDocument());
  });
});

describe("the appointment drawer", () => {
  it("opens with the appointment's details and history, moves focus into it, and closes with Escape", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", notes: "Bring the old X-rays" });
    await openOn(DAY);
    await userEvent.click(await waitFor(() => { const b = block(a.id); expect(b).toBeInTheDocument(); return b; }));

    const d = drawer();
    expect(within(d).getByRole("heading", { name: "Ann Lee" })).toHaveFocus();
    expect(within(d).getByText("2030-01-14 09:00–10:00 (60 min)")).toBeInTheDocument();
    expect(within(d).getByText("Dr. Rivera")).toBeInTheDocument();
    expect(within(d).getByLabelText("Note")).toHaveValue("Bring the old X-rays");
    expect(await within(d).findByRole("table", { name: /What happened to this appointment/ })).toBeInTheDocument();
    expect(within(d).getByText("Scheduled", { selector: "td" })).toBeInTheDocument();

    fireEvent.keyDown(d, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("books from the New appointment button: the patient is found by name, the type's default duration is shown, and the new appointment appears in the calendar and the drawer", async () => {
    await openOn(DAY);
    await userEvent.click(await screen.findByRole("button", { name: "New appointment" }));
    const form = within(screen.getByRole("form", { name: "New appointment" }));

    await userEvent.type(form.getByLabelText(/Patient/, { selector: "input" }), "Ann");
    await userEvent.click(await form.findByRole("button", { name: /Ann Lee/ }));
    await userEvent.selectOptions(form.getByLabelText("Appointment type *"), form.getByRole("option", { name: /Exam/ }));
    expect(form.getByText("Leave blank to use 60 minutes.")).toBeInTheDocument();
    await userEvent.selectOptions(form.getByLabelText("Provider *"), "Dr. Rivera");
    await userEvent.selectOptions(form.getByLabelText("Operatory *"), "Op 1");
    fireEvent.change(form.getByLabelText("Date *"), { target: { value: DAY } });
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "10:00" } });
    await userEvent.type(form.getByLabelText("Note (optional)"), "Needs an accessible room");
    await userEvent.click(form.getByRole("button", { name: "Book appointment" }));

    expect(await within(drawer()).findByRole("heading", { name: "Ann Lee" })).toBeInTheDocument(); // now showing the booked appointment
    await waitFor(() => expect(document.querySelectorAll(".cal__block")).toHaveLength(1));
    const post = calls("POST", "/api/appointments").find((c) => c.url === "/api/appointments")!;
    expect(post.body).toMatchObject({ patientId: ANN, providerId: "prov-a", operatoryId: "op-1", appointmentTypeId: "type-60", startLocal: `${DAY}T10:00`, durationMinutes: null, notes: "Needs an accessible room" });
    expect(post.headers["Idempotency-Key"]).toMatch(/^book-/);
    expect(within(drawer()).getByLabelText("Note")).toHaveValue("Needs an accessible room");
  });

  it("clicking an empty part of a provider's column starts a booking for that provider and day", async () => {
    await openOn(DAY);
    await waitFor(() => expect(document.querySelectorAll(".cal__body")).toHaveLength(2));
    fireEvent.click(document.querySelectorAll(".cal__body")[1] as HTMLElement);

    const form = within(await screen.findByRole("form", { name: "New appointment" }));
    expect(form.getByLabelText("Provider *")).toHaveValue("prov-b");
    expect(form.getByLabelText("Date *")).toHaveValue(DAY);
  });

  async function bookingWith(over: { provider?: string; operatory?: string; time?: string; patient?: string; patientName?: string }) {
    await openOn(DAY);
    await userEvent.click(await screen.findByRole("button", { name: "New appointment" }));
    const form = within(screen.getByRole("form", { name: "New appointment" }));
    await userEvent.type(form.getByLabelText(/Patient/, { selector: "input" }), over.patient ?? "Bo");
    await userEvent.click(await form.findByRole("button", { name: new RegExp(over.patientName ?? "Bo Kim") }));
    await userEvent.selectOptions(form.getByLabelText("Appointment type *"), form.getByRole("option", { name: /Exam/ }));
    await userEvent.selectOptions(form.getByLabelText("Provider *"), over.provider ?? "Dr. Rivera");
    await userEvent.selectOptions(form.getByLabelText("Operatory *"), over.operatory ?? "Op 2");
    fireEvent.change(form.getByLabelText("Date *"), { target: { value: DAY } });
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: over.time ?? "09:30" } });
    await userEvent.click(form.getByRole("button", { name: "Book appointment" }));
    return form;
  }

  it("explains a double-booked provider, an operatory in use, a patient in two places and an unavailable provider, naming the time that is in the way, and books nothing", async () => {
    server.seed({ start: `${DAY}T09:00`, minutes: 60, patientName: "Ann Lee" }); // Dr. Rivera, Op 1, 09:00-10:00
    await bookingWith({}); // Bo with Dr. Rivera in Op 2 at 09:30
    expect(await screen.findByRole("alert")).toHaveTextContent("Dr. Rivera already has an appointment from 09:00 to 10:00.");
    expect(screen.getByRole("alert")).toHaveTextContent("Nothing was changed");
    expect(server.appointments).toHaveLength(1);
  });

  it("explains an operatory conflict", async () => {
    server.seed({ start: `${DAY}T09:00`, minutes: 60, patientName: "Ann Lee" });
    await bookingWith({ provider: "Dr. Patel", operatory: "Op 1" });
    expect(await screen.findByRole("alert")).toHaveTextContent("Op 1 is already in use from 09:00 to 10:00.");
  });

  it("explains a patient who is already booked elsewhere at that time", async () => {
    server.seed({ start: `${DAY}T09:00`, minutes: 60, patientName: "Bo Kim" }); // Dr. Rivera, Op 1
    const ann = server.appointments[0];
    ann.patientId = BO; // the existing appointment belongs to Bo
    await bookingWith({ provider: "Dr. Patel", operatory: "Op 2" });
    expect(await screen.findByRole("alert")).toHaveTextContent("This patient already has an appointment from 09:00 to 10:00.");
    expect(screen.getByRole("alert")).toHaveTextContent("A patient cannot be in two places at once.");
    expect(server.appointments).toHaveLength(1);
  });

  it("explains an unavailable provider with the reason", async () => {
    await bookingWith({ time: "06:00" });
    expect(await screen.findByRole("alert")).toHaveTextContent("Dr. Rivera is not available: that time is outside the provider's working hours.");
  });

  it("a dropped connection while booking says the outcome is unknown and Book again reuses the same key so it books once", async () => {
    server.dropNextBookResponse = true;
    await bookingWith({ time: "10:00" });
    expect(await screen.findByText(/could not confirm whether the appointment was booked/)).toBeInTheDocument();
    expect(server.appointments).toHaveLength(1); // it WAS stored

    await userEvent.click(screen.getByRole("button", { name: "Book again" }));
    await within(drawer()).findByRole("heading", { name: "Bo Kim" });
    const keys = calls("POST", "/api/appointments").filter((c) => c.url === "/api/appointments").map((c) => c.headers["Idempotency-Key"]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(server.appointments).toHaveLength(1);
  });
});

describe("rescheduling", () => {
  async function openAppointment(id: string) {
    await openOn(DAY);
    await userEvent.click(await waitFor(() => { const b = block(id); expect(b).toBeInTheDocument(); return b; }));
    return within(drawer());
  }

  it("moves the appointment, sending the version it read, and the calendar and history show where it is now and where it was", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Reschedule" }));
    const form = within(screen.getByRole("form", { name: "Reschedule appointment" }));
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "14:00" } });
    await userEvent.selectOptions(form.getByLabelText("Provider *"), "Dr. Patel");
    await userEvent.selectOptions(form.getByLabelText("Operatory *"), "Op 2");
    await userEvent.click(form.getByRole("button", { name: "Save new time" }));

    expect(await d.findByText("Appointment rescheduled.")).toBeInTheDocument();
    expect(d.getByText("2030-01-14 14:00–15:00 (60 min)")).toBeInTheDocument();
    await waitFor(() => expect(d.getByRole("heading", { name: "Ann Lee" })).toHaveFocus()); // keyboard focus is not lost when the form closes
    expect(d.getByText("Dr. Patel")).toBeInTheDocument();
    const put = calls("PUT", `/${a.id}/reschedule`)[0];
    expect(put.body).toMatchObject({ providerId: "prov-b", operatoryId: "op-2", startLocal: `${DAY}T14:00`, durationMinutes: null, rowVersion: "a1" });
    expect(put.headers["X-CSRF-Token"]).toBe("csrf-1");
    await waitFor(() => expect(block(a.id).style.top).toBe("420px")); // 14:00 in Dr. Patel's column
    expect(await d.findByText("Was 2030-01-14 09:00 with Dr. Rivera in Op 1")).toBeInTheDocument();
  });

  it("explains a conflict and leaves the appointment where it was", async () => {
    server.seed({ start: `${DAY}T14:00`, minutes: 60, provider: "prov-b", operatory: "op-2", patientName: "Bo Kim" });
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Reschedule" }));
    const form = within(screen.getByRole("form", { name: "Reschedule appointment" }));
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "14:30" } });
    await userEvent.selectOptions(form.getByLabelText("Provider *"), "Dr. Patel");
    await userEvent.click(form.getByRole("button", { name: "Save new time" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Dr. Patel already has an appointment from 14:00 to 15:00.");
    expect(a.startLocal).toBe(`${DAY}T09:00`);
    expect(screen.getByRole("form", { name: "Reschedule appointment" })).toBeInTheDocument(); // still open, to try another time
  });

  it("a change made by someone else in the meantime is reported, not overwritten, and Reload shows the current appointment", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Reschedule" }));
    server.changeBehindTheScenes(a.id, { startLocal: `${DAY}T11:00`, endLocal: `${DAY}T12:00` }); // another user moved it
    const form = within(screen.getByRole("form", { name: "Reschedule appointment" }));
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "15:00" } });
    await userEvent.click(form.getByRole("button", { name: "Save new time" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(a.startLocal).toBe(`${DAY}T11:00`); // their change stands
    await userEvent.click(screen.getByRole("button", { name: /Reload/ }));
    expect(await d.findByText("2030-01-14 11:00–12:00 (60 min)")).toBeInTheDocument();
  });

  it("a second click on Save while the request is going out sends one request", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Reschedule" }));
    const form = within(screen.getByRole("form", { name: "Reschedule appointment" }));
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "13:00" } });
    await userEvent.dblClick(form.getByRole("button", { name: "Save new time" }));

    await d.findByText("Appointment rescheduled.");
    expect(calls("PUT", "/reschedule")).toHaveLength(1);
  });

  it("a dropped connection says it could not confirm the change and to reload, instead of claiming success", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Reschedule" }));
    server.dropNextResponseFor = "reschedule";
    const form = within(screen.getByRole("form", { name: "Reschedule appointment" }));
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "13:00" } });
    await userEvent.click(form.getByRole("button", { name: "Save new time" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("could not confirm whether that was saved");
    expect(screen.queryByText("Appointment rescheduled.")).not.toBeInTheDocument();
  });

  it("Keep as it is leaves the appointment untouched", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Reschedule" }));
    await userEvent.click(screen.getByRole("button", { name: "Keep as it is" }));
    expect(screen.queryByRole("form", { name: "Reschedule appointment" })).not.toBeInTheDocument();
    expect(calls("PUT", "/reschedule")).toHaveLength(0);
  });
});

describe("cancelling, no-shows and notes", () => {
  async function openAppointment(id: string, day = DAY) {
    await openOn(day);
    await userEvent.click(await waitFor(() => { const b = block(id); expect(b).toBeInTheDocument(); return b; }));
    return within(drawer());
  }

  it("cancelling needs a reason; the cancelled appointment stays on the calendar, marked, with its reason, and offers no further changes except a note", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const d = await openAppointment(a.id);
    await userEvent.click(d.getByRole("button", { name: "Cancel appointment" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirm cancellation" })); // no reason yet
    expect(await screen.findByRole("alert")).toHaveTextContent("A reason for the cancellation is required.");
    expect(a.status).toBe("Scheduled");

    await userEvent.type(screen.getByLabelText("Reason for cancelling (required)"), "Patient called to cancel");
    await userEvent.click(screen.getByRole("button", { name: "Confirm cancellation" }));

    expect(await d.findByText("Appointment cancelled.")).toBeInTheDocument();
    expect(d.getByText("Reason cancelled")).toBeInTheDocument();
    expect(d.getByText("Patient called to cancel", { selector: "dd" })).toBeInTheDocument();
    for (const name of ["Reschedule", "Cancel appointment", "Mark no-show"]) expect(d.queryByRole("button", { name })).not.toBeInTheDocument();
    await waitFor(() => expect(block(a.id)).toHaveClass("cal__block--cancelled"));
    expect(block(a.id)).toHaveTextContent("· Cancelled");
    expect(calls("POST", "/cancel").at(-1)!.body).toMatchObject({ reason: "Patient called to cancel", rowVersion: "a1" });
  });

  it("a no-show is only offered once the start time has passed, then marks the appointment and keeps it on the calendar", async () => {
    const future = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const past = server.seed({ start: `${PAST}T09:00`, patientName: "Bo Kim" });

    const d = await openAppointment(future.id);
    expect(d.getByRole("button", { name: "Mark no-show" })).toBeDisabled();
    expect(d.getByText("A no-show can be recorded once the start time has passed.")).toBeInTheDocument();
    fireEvent.keyDown(drawer(), { key: "Escape" });

    const d2 = await openAppointment(past.id, PAST);
    const button = d2.getByRole("button", { name: "Mark no-show" });
    expect(button).toBeEnabled();
    await userEvent.click(button);

    expect((await d2.findAllByText("Marked as a no-show.")).length).toBeGreaterThan(0); // the confirmation and the new history entry
    await waitFor(() => expect(block(past.id)).toHaveClass("cal__block--noshow"));
    expect(block(past.id)).toHaveTextContent("· No-show");
    expect(d2.queryByRole("button", { name: "Mark no-show" })).not.toBeInTheDocument();
  });

  it("a note can be edited and saved, Save note is only enabled when it changed, and it works on a cancelled appointment", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", status: "Cancelled", cancelReason: "Illness" });
    const d = await openAppointment(a.id);
    const save = d.getByRole("button", { name: "Save note" });
    expect(save).toBeDisabled();
    await userEvent.type(d.getByLabelText("Note"), "Wants to rebook next month");
    expect(save).toBeEnabled();
    await userEvent.click(save);

    expect(await d.findByText("Note saved.")).toBeInTheDocument();
    expect(a.notes).toBe("Wants to rebook next month");
    expect(calls("PUT", "/notes")[0].body).toMatchObject({ notes: "Wants to rebook next month", rowVersion: "a1" });
  });
});

describe("accessibility", () => {
  it("the day view, the week view, an appointment drawer and the new-appointment drawer have no detectable violations", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    server.seed({ start: `${DAY}T10:00`, patientName: "Bo Kim", provider: "prov-b", operatory: "op-2", status: "Cancelled", cancelReason: "Illness" });
    const { container } = await openOn(DAY);
    await waitFor(() => expect(block(a.id)).toBeInTheDocument());
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("button", { name: "Week" }));
    await screen.findByRole("heading", { name: /Week of/ });
    await waitFor(() => expect(block(a.id)).toBeInTheDocument());
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(block(a.id));
    await within(drawer()).findByRole("table", { name: /What happened/ });
    expect(await axe(container)).toHaveNoViolations();
    await userEvent.click(within(drawer()).getByRole("button", { name: "Close drawer" }));

    await userEvent.click(screen.getByRole("button", { name: "New appointment" }));
    await screen.findByRole("form", { name: "New appointment" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
