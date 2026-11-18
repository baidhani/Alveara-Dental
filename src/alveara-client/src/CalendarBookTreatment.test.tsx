import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";
import { FakeFlowServer as FakeCalendarServer } from "./test/fakeFlowServer";
import { makePatient } from "./test/fakePatientServer";
import { bookableLabel } from "./pages/calendar/bookableTreatmentText";

/**
 * ALV-N023 (owner decision OQ-1): while booking, the front desk can tick the patient's accepted, unbooked procedures; the booking request names them (planItemIds) so the server links them in the
 * same save. The choices show only code, description, tooth and plan reference - never a plan title or diagnosis - and a patient with nothing accepted says so.
 */
const ANN = "aaaaaaaa-0000-0000-0000-000000000001";
const BO = "bbbbbbbb-0000-0000-0000-000000000002";
const DAY = "2030-01-14";
let server: FakeCalendarServer;

const crown = { planId: "plan-1", itemId: "item-1", itemNumber: 1, procedureCode: "D2740", procedureDescription: "Crown - porcelain", toothKey: "36", surface: null, planReference: "plan-2030-01" };
const cleaning = { planId: "plan-1", itemId: "item-2", itemNumber: 2, procedureCode: "D1110", procedureDescription: "Cleaning", toothKey: null, surface: null, planReference: null };

beforeEach(() => {
  server = new FakeCalendarServer();
  server.add(makePatient({ id: ANN, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: BO, firstName: "Bo", lastName: "Kim", dateOfBirth: "1990-01-01", phone: "555-020-0200" }));
  for (const p of server.providers) p.weeklyAvailability.push({ dayOfWeek: 1, startLocal: "08:00", endLocal: "17:00" } as never);
  server.bookable.set(ANN, [crown, cleaning]);
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

async function startBooking(name: RegExp, typed: string) {
  window.history.pushState({}, "", "/calendar");
  render(<App />);
  fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
  await userEvent.click(await screen.findByRole("button", { name: "New appointment" }));
  const form = within(screen.getByRole("form", { name: "New appointment" }));
  await userEvent.type(form.getByLabelText(/Patient/, { selector: "input" }), typed);
  await userEvent.click(await form.findByRole("button", { name }));
  return form;
}

describe("booking with accepted treatment", () => {
  it("labels each choice with code, description, tooth and plan reference only", () => {
    expect(bookableLabel(crown)).toBe("D2740 Crown - porcelain (tooth 19) - plan plan-2030-01, item 1");
    expect(bookableLabel(cleaning)).toBe("D1110 Cleaning - plan without a reference code, item 2");
  });

  it("names the ticked procedures in the booking request", async () => {
    const form = await startBooking(/Ann Lee/, "Ann");
    const choice = await form.findByRole("checkbox", { name: /D2740 Crown - porcelain/ });
    expect(form.getByRole("group", { name: "For accepted treatment (optional)" })).toBeInTheDocument();
    await userEvent.click(choice);
    await userEvent.selectOptions(form.getByLabelText("Appointment type *"), form.getByRole("option", { name: /Exam/ }));
    await userEvent.selectOptions(form.getByLabelText("Provider *"), "Dr. Rivera");
    await userEvent.selectOptions(form.getByLabelText("Operatory *"), "Op 1");
    fireEvent.change(form.getByLabelText("Date *"), { target: { value: DAY } });
    fireEvent.change(form.getByLabelText("Start time *"), { target: { value: "10:00" } });
    await userEvent.click(form.getByRole("button", { name: "Book appointment" }));
    await screen.findByRole("dialog");
    const post = server.callsToSchedule("POST", "/api/appointments").at(-1)!;
    expect((post.body as { planItemIds?: string[] }).planItemIds).toEqual(["item-1"]);
  });

  it("says so when the patient has no accepted treatment waiting, and sends no procedures", async () => {
    const form = await startBooking(/Bo Kim/, "Bo");
    expect(await form.findByText("This patient has no accepted treatment waiting to be booked.")).toBeInTheDocument();
    expect(form.queryByRole("checkbox")).not.toBeInTheDocument();
  });
});

describe("the appointment view lists what it books (OQ-1)", () => {
  it("shows the booked procedure and lets the front desk unlink it with a reason", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, minutes: 30, patientName: "Ann Lee" });
    server.appointmentTreatment.set(a.id, [crown]);
    window.history.pushState({}, "", "/calendar");
    render(<App />);
    fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
    await userEvent.click(await vi.waitFor(() => { const b = document.querySelector(`[data-appointment="${a.id}"]`) as HTMLElement; expect(b).toBeTruthy(); return b; }));
    const section = await screen.findByRole("region", { name: "Treatment this appointment books" });
    expect(section).toHaveTextContent("D2740 Crown - porcelain (tooth 19) - plan plan-2030-01, item 1");
    await userEvent.click(within(section).getByRole("button", { name: "Unlink D2740" }));
    await userEvent.click(within(section).getByRole("button", { name: "Unlink" }));
    expect(await within(section).findByText("Say why.")).toBeInTheDocument();
    await userEvent.type(within(section).getByLabelText(/Why is this appointment no longer booking D2740/), "Patient will call back");
    await userEvent.click(within(section).getByRole("button", { name: "Unlink" }));
    expect(await within(section).findByText("D2740 is no longer booked by this appointment.")).toBeInTheDocument();
    expect(server.callsToSchedule("POST", "/api/treatment-plans/plan-1/items/item-1/appointment-unlink").at(-1)!.body).toEqual({ reason: "Patient will call back" });
  });
});

