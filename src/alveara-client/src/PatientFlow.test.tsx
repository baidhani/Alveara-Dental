import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeFlowServer } from "./test/fakeFlowServer";
import { makePatient } from "./test/fakePatientServer";

/**
 * STORY-011, reworked for ALV-N021 A2: a patient's visit from the calendar drawer, through the real <App /> against the in-memory visit fake. The rules
 * (who, when, the one path, the checkout gate) are proven by the backend tests; what is proven here is that the drawer draws exactly the server's actions
 * for the signed-in user (the same list the Visit board uses), moves through visits/{id}/state with the version it loaded, shows blocked actions with the
 * server's reason, asks for a reason or category where the server needs one, says who the visit waits for, and explains every refusal in words.
 */
const ANN = "aaaaaaaa-0000-0000-0000-000000000001";
const DAY = "2030-01-14";
const ALL = ["ViewPatientRecords", "ViewSchedule", "ManageAppointments", "UpdateVisitFlow", "UpdateChairsideFlow", "ReassignAppointmentProvider"];

let server: FakeFlowServer;

beforeEach(() => {
  server = new FakeFlowServer();
  server.permissions = ALL;
  server.add(makePatient({ id: ANN, firstName: "Ann", lastName: "Lee" }));
  for (const p of server.providers) p.weeklyAvailability.push({ dayOfWeek: 1, startLocal: "08:00", endLocal: "17:00" } as never);
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const block = (id: string) => document.querySelector(`[data-appointment="${id}"]`) as HTMLElement;
const moves = (id: string) => server.callsToSchedule("POST", "/api/visits").filter((c) => c.url === `/api/visits/${id}/state`);

async function openAppointment(id: string) {
  window.history.pushState({}, "", "/calendar");
  const view = render(<App />);
  fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
  await screen.findByRole("heading", { name: /\d{4}-\d{2}-\d{2}/ });
  await userEvent.click(await waitFor(() => { const b = block(id); expect(b).toBeInTheDocument(); return b; }));
  const d = within(screen.getByRole("dialog"));
  await d.findByRole("table", { name: /What happened/ });
  return { view, d };
}

describe("moving a patient through the visit", () => {
  it("offers Confirm first (no check-in shortcut), then Check in, moving through visits/{id}/state with the version it loaded and saying who is next", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    expect(await d.findByRole("button", { name: "Confirm" })).toBeEnabled();
    expect(d.queryByRole("button", { name: "Check in" })).not.toBeInTheDocument();
    expect(d.getByText("Waiting for the front desk to confirm.")).toBeInTheDocument();

    await userEvent.click(d.getByRole("button", { name: "Confirm" }));
    expect(await d.findByText("Confirmed. Waiting for the front desk to check the patient in.")).toBeInTheDocument();
    expect(moves(a.id).at(-1)!.body).toEqual({ target: "Confirmed", rowVersion: "a1" });

    await userEvent.click(await d.findByRole("button", { name: "Check in" }));
    expect(await d.findByText("Patient checked in. Waiting for chairside staff to mark the patient ready.")).toBeInTheDocument();
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Checked in");
    expect(d.queryByText("Scheduled", { selector: ".cal-badge" })).not.toBeInTheDocument();   // F-055: one chip, the visit's state
    expect(d.getByText("Checked in at", { selector: "dt" })).toBeInTheDocument();
    await waitFor(() => expect(block(a.id)).toHaveTextContent("09:00–10:00 · Checked in"));
    for (const name of ["Reschedule", "Cancel appointment", "Mark no-show"]) expect(d.queryByRole("button", { name })).not.toBeInTheDocument(); // arrived
  });

  it("walks the whole path from the drawer to Completed, asking before completing, and every step is in the history", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    for (const name of ["Confirm", "Check in", "Mark ready", "Seat patient", "Start treatment", "End treatment / ready for checkout"]) {
      await userEvent.click(await d.findByRole("button", { name }));
      await waitFor(() => expect(d.queryByRole("button", { name })).not.toBeInTheDocument());
    }
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Ready for checkout");

    await userEvent.click(await d.findByRole("button", { name: "Complete visit" }));
    expect(d.getByText("Complete the visit for Ann Lee? This cannot be undone.")).toBeInTheDocument();
    await userEvent.click(d.getByRole("button", { name: "Yes, complete the visit" }));
    expect(await d.findByText("Visit completed.", { selector: "p" })).toBeInTheDocument();
    expect(a.flowState).toBe("Completed");
    const history = d.getByRole("table", { name: /What happened/ });
    await waitFor(() => expect(within(history).getByText("Visit completed")).toBeInTheDocument());
    for (const step of ["Confirmed", "Patient checked in", "Marked ready", "Seated", "Treatment started", "Ready for checkout"]) expect(within(history).getByText(step)).toBeInTheDocument();
  });

  it("undoes a check-in only with a reason, and the reason shows in the history", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedIn" });
    const { d } = await openAppointment(a.id);
    await userEvent.click(await d.findByRole("button", { name: "Undo check-in" }));
    await userEvent.click(d.getByRole("button", { name: "Undo check-in" }));   // the form's submit, no reason yet
    expect(d.getByText("Give a reason.")).toBeInTheDocument();

    await userEvent.type(d.getByLabelText("Reason (required)"), "Checked in the wrong patient");
    await userEvent.click(d.getByRole("button", { name: "Undo check-in" }));
    expect(await d.findByText(/^Check-in undone\./)).toBeInTheDocument();
    expect(a.flowState).toBe("Confirmed");
  });
});

describe("blocked actions, reasons and refusals", () => {
  it("shows check-in on blocked time disabled with the server's reason, and keeping it with a reason makes check-in available", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "Confirmed" });
    a.blockFlagged = true;
    const { d } = await openAppointment(a.id);
    const checkIn = await d.findByRole("button", { name: "Check in" });
    expect(checkIn).toBeDisabled();
    expect(checkIn).toHaveAccessibleDescription("The provider has blocked this time. Reschedule, reassign, cancel or keep the appointment first.");
    expect(d.getByTestId("block-flag")).toHaveTextContent("Blocked time");
    expect(block(a.id)).toHaveTextContent("· Blocked time");

    await userEvent.click(d.getByRole("button", { name: "Keep despite blocked time" }));
    await userEvent.type(d.getByLabelText("Reason (required)"), "Seen before the course");
    await userEvent.click(d.getByRole("button", { name: "Keep appointment" }));
    expect(await d.findByText(/^Appointment kept despite the blocked time\./)).toBeInTheDocument();
    await waitFor(() => expect(d.getByRole("button", { name: "Check in" })).toBeEnabled());
  });

  it("shows Complete disabled with the checkout sentence while checkout items are open", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedOut" });
    server.checkoutOpen.add(a.id);
    const { d } = await openAppointment(a.id);
    const complete = await d.findByRole("button", { name: "Complete visit" });
    expect(complete).toBeDisabled();
    expect(complete).toHaveAccessibleDescription("Ready for checkout — the patient has not been marked as left.");
  });

  it("changes the provider from the drawer only with a reason, and the booking moves", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    await userEvent.click(await d.findByRole("button", { name: "Change provider or room" }));
    await userEvent.selectOptions(d.getByLabelText("Provider"), "Dr. Patel");
    await userEvent.type(d.getByLabelText("Reason for changing the provider (required)"), "Dr. Rivera is off sick");
    await userEvent.click(d.getByRole("button", { name: "Save" }));

    expect(await d.findByText(/^Provider changed\./)).toBeInTheDocument();
    expect(a.providerName).toBe("Dr. Patel");
    expect(d.getByText("Dr. Patel", { selector: "dd" })).toBeInTheDocument();
  });

  it("reports a change someone else made first as a conflict, applies nothing on top, and Reload shows the current appointment", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedIn" });
    const { d } = await openAppointment(a.id);
    await d.findByRole("button", { name: "Mark ready" });
    server.moveBehindTheScenes(a.id, "Ready");

    await userEvent.click(d.getByRole("button", { name: "Undo check-in" }));
    await userEvent.type(d.getByLabelText("Reason (required)"), "Wrong patient");
    await userEvent.click(d.getByRole("button", { name: "Undo check-in" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(a.flowState).toBe("Ready");
    await userEvent.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(d.getByTestId("flow-badge")).toHaveTextContent("Ready"));
  });

  it("explains a refused move in the server's words and leaves the appointment as it was", async () => {
    server.seed({ start: `${DAY}T08:00`, patientName: "Bo Kim", flowState: "Seated" });
    const a = server.seed({ start: `${DAY}T11:00`, patientName: "Ann Lee", flowState: "Ready" });
    const { d } = await openAppointment(a.id);
    await userEvent.click(await d.findByRole("button", { name: "Seat patient" }));

    expect(await d.findByRole("alert")).toHaveTextContent("That operatory is occupied by a patient who is seated or in treatment.Nothing was changed.");
    expect(a.flowState).toBe("Ready");
  });
});

describe("who can do what", () => {
  it("a role that can only view the schedule sees where the patient is and who is next, but has no buttons", async () => {
    server.permissions = ["ViewSchedule"];
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedIn" });
    const { d } = await openAppointment(a.id);
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Checked in");
    expect(await d.findByText("Waiting for chairside staff to mark the patient ready.")).toBeInTheDocument();
    expect(d.queryByRole("group", { name: "Patient flow" })).not.toBeInTheDocument();
    expect(d.queryByRole("group", { name: "Booking" })).not.toBeInTheDocument();
  });

  it("a cancelled appointment offers no flow and no booking changes", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", status: "Cancelled", cancelReason: "Illness" });
    const { d } = await openAppointment(a.id);
    expect(d.queryByRole("group", { name: "Patient flow" })).not.toBeInTheDocument();
    expect(d.queryByRole("group", { name: "Booking" })).not.toBeInTheDocument();
    expect(d.queryByTestId("flow-badge")).not.toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("the drawer with its action groups, a disabled action and a reason form has no axe violations, and focus survives a move", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "Confirmed" });
    a.blockFlagged = true;
    const { view, d } = await openAppointment(a.id);
    expect(await d.findByRole("group", { name: "Patient flow" })).toBeInTheDocument();
    expect(d.getByRole("group", { name: "Booking" })).toBeInTheDocument();
    expect(await axe(view.container)).toHaveNoViolations();

    await userEvent.click(d.getByRole("button", { name: "Keep despite blocked time" }));
    expect(await axe(view.container)).toHaveNoViolations();
    await userEvent.type(d.getByLabelText("Reason (required)"), "Seen before the course");
    await userEvent.click(d.getByRole("button", { name: "Keep appointment" }));
    await d.findByText(/^Appointment kept/);
    expect(document.activeElement).toBe(document.getElementById("cal-drawer-title"));
  });
});
