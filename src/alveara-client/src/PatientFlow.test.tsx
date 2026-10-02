import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeCalendarServer } from "./test/fakeCalendarServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * STORY-011: a patient's flow through the visit (check in, start treatment, complete) from the calendar drawer, through the real <App /> against an
 * in-memory fake of the scheduling API. The flow rules, the audit trail and the races are proven by the backend tests and the real-backend
 * walkthrough; what is proven here is that the drawer offers the right next step, asks correctly (with the version it loaded), explains every outcome
 * in words, shows the flow on the calendar and in the history, withholds reschedule/cancel/no-show once the patient has arrived, and gives people
 * without the permission no way to move a patient.
 */
const ANN = "aaaaaaaa-0000-0000-0000-000000000001";
const DAY = "2030-01-14";

let server: FakeCalendarServer;

beforeEach(() => {
  server = new FakeCalendarServer();
  server.add(makePatient({ id: ANN, firstName: "Ann", lastName: "Lee" }));
  for (const p of server.providers) p.weeklyAvailability.push({ dayOfWeek: 1, startLocal: "08:00", endLocal: "17:00" } as never);
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const block = (id: string) => document.querySelector(`[data-appointment="${id}"]`) as HTMLElement;
const calls = (method: string, suffix: string) => server.callsToSchedule(method, "/api/appointments").filter((c) => c.url.includes(suffix));

async function openAppointment(id: string) {
  window.history.pushState({}, "", "/calendar");
  const view = render(<App />);
  fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
  await screen.findByRole("heading", { name: /\d{4}-\d{2}-\d{2}/ });
  await userEvent.click(await waitFor(() => { const b = block(id); expect(b).toBeInTheDocument(); return b; }));
  return { view, d: within(screen.getByRole("dialog")) };
}

const FLOW_BUTTONS = ["Check in", "Start treatment", "Complete treatment"];
const CHANGE_BUTTONS = ["Reschedule", "Cancel appointment", "Mark no-show"];

describe("moving a patient through the visit", () => {
  it("offers Check in for a scheduled patient, and checking in marks them in words in the drawer and on the calendar, asking with the version it loaded", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    expect(d.getByRole("button", { name: "Check in" })).toBeEnabled();
    expect(d.queryByRole("button", { name: "Start treatment" })).not.toBeInTheDocument();
    expect(d.queryByRole("button", { name: "Complete treatment" })).not.toBeInTheDocument();
    expect(d.queryByTestId("flow-badge")).not.toBeInTheDocument(); // not arrived yet: nothing to say
    expect(block(a.id)).not.toHaveTextContent("Checked in");

    await userEvent.click(d.getByRole("button", { name: "Check in" }));

    expect(await d.findByText("Patient checked in.")).toBeInTheDocument();
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Checked in");
    await waitFor(() => expect(block(a.id)).toHaveTextContent("09:00–10:00 · Checked in"));
    expect(block(a.id)).toHaveAccessibleName(/Checked in$/);
    expect(block(a.id).dataset.flow).toBe("CheckedIn");
    expect(calls("POST", "/check-in").at(-1)!.body).toEqual({ rowVersion: "a1" });
    expect(a.flowState).toBe("CheckedIn");
  });

  it("once checked in the next steps are offered and Reschedule, Cancel appointment and Mark no-show are not", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    for (const name of CHANGE_BUTTONS) expect(d.getByRole("button", { name })).toBeInTheDocument(); // before arrival

    await userEvent.click(d.getByRole("button", { name: "Check in" }));
    await d.findByText("Patient checked in.");

    expect(d.queryByRole("button", { name: "Check in" })).not.toBeInTheDocument();
    expect(d.getByRole("button", { name: "Start treatment" })).toBeEnabled();
    expect(d.getByRole("button", { name: "Complete treatment" })).toBeEnabled(); // InTreatment may be skipped
    for (const name of CHANGE_BUTTONS) expect(d.queryByRole("button", { name })).not.toBeInTheDocument();
    expect(d.queryByText("A no-show can be recorded once the start time has passed.")).not.toBeInTheDocument();
  });

  it("a checked-in patient whose treatment is completed becomes Completed, no further flow step is offered, and the history shows who-did-what in words", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedIn" });
    const { d } = await openAppointment(a.id);

    await userEvent.click(d.getByRole("button", { name: "Complete treatment" }));

    expect(await d.findByText("Treatment completed.", { selector: "p" })).toBeInTheDocument();
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Completed");
    for (const name of FLOW_BUTTONS) expect(d.queryByRole("button", { name })).not.toBeInTheDocument();
    for (const name of CHANGE_BUTTONS) expect(d.queryByRole("button", { name })).not.toBeInTheDocument();
    await waitFor(() => expect(block(a.id)).toHaveTextContent("· Completed"));
    expect(calls("POST", "/complete").at(-1)!.body).toEqual({ rowVersion: "a1" });
    const history = within(d.getByRole("table", { name: /what happened/i }));
    expect(history.getByText("Treatment completed")).toBeInTheDocument();
    expect(history.getByText("CheckedIn -> Completed")).toBeInTheDocument();
  });

  it("the full path Scheduled to Checked in to In treatment to Completed works step by step and every step is in the history", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);

    await userEvent.click(d.getByRole("button", { name: "Check in" }));
    await d.findByText("Patient checked in.", { selector: "p" });
    await userEvent.click(d.getByRole("button", { name: "Start treatment" }));
    await d.findByText("Treatment started.", { selector: "p" });
    expect(d.getByTestId("flow-badge")).toHaveTextContent("In treatment");
    expect(d.queryByRole("button", { name: "Start treatment" })).not.toBeInTheDocument();
    await userEvent.click(d.getByRole("button", { name: "Complete treatment" }));
    await d.findByText("Treatment completed.", { selector: "p" });

    expect(a.flowState).toBe("Completed");
    const rows = within(d.getByRole("table", { name: /what happened/i })).getAllByRole("row").slice(1).map((r) => within(r).getAllByRole("cell")[1].textContent);
    expect(rows).toEqual(["Scheduled", "Patient checked in", "Treatment started", "Treatment completed"]);
    // each request carried the version returned by the one before it
    expect([calls("POST", "/check-in"), calls("POST", "/start-treatment"), calls("POST", "/complete")].map((c) => c.at(-1)!.body)).toEqual([
      { rowVersion: "a1" }, { rowVersion: "a2" }, { rowVersion: "a3" },
    ]);
  });
});

describe("when a move does not go through", () => {
  it("a change made by someone else first is reported as a conflict, nothing is applied on top of it, and Reload shows the current appointment", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    server.changeBehindTheScenes(a.id, { notes: "Moved to the front of the queue" });

    await userEvent.click(d.getByRole("button", { name: "Check in" }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(a.flowState).toBe("Scheduled");
    expect(d.queryByTestId("flow-badge")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reload/i }));
    await waitFor(() => expect(d.getByLabelText("Note")).toHaveValue("Moved to the front of the queue"));
    await userEvent.click(d.getByRole("button", { name: "Check in" })); // now carrying the current version
    expect(await d.findByText("Patient checked in.")).toBeInTheDocument();
  });

  it("a dropped connection after the server applied the check-in says it cannot tell, and pressing Check in again does not check in twice", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { d } = await openAppointment(a.id);
    server.dropNextResponseFor = "check-in";

    await userEvent.click(d.getByRole("button", { name: "Check in" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not confirm whether that was saved/i);
    expect(a.flowState).toBe("CheckedIn"); // it was applied

    await userEvent.click(d.getByRole("button", { name: "Check in" })); // retry with the old version: the server sees the patient is already in
    expect(await d.findByText("Patient checked in.")).toBeInTheDocument();
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Checked in");
    expect(await d.findAllByText("Patient checked in")).toHaveLength(1); // one history entry, not two
  });

  it("a move the server refuses is explained in its words and the appointment is left as it was", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedIn" });
    const { d } = await openAppointment(a.id);
    server.failSchedule(`POST /api/appointments/${a.id}/start-treatment`, json(409, { error: "invalid_flow_transition", message: "A visit cannot go from checked in to in treatment." }));

    await userEvent.click(d.getByRole("button", { name: "Start treatment" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A visit cannot go from checked in to in treatment.");
    expect(d.getByTestId("flow-badge")).toHaveTextContent("Checked in");
    expect(d.getByRole("button", { name: "Start treatment" })).toBeEnabled(); // nothing changed; the user can try again
    expect(a.flowState).toBe("CheckedIn");
  });
});

describe("who can move a patient", () => {
  it("a role that can only view the schedule sees where the patient is but has no flow buttons", async () => {
    server.permissions = ["ViewSchedule"];
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedIn" });
    const { d } = await openAppointment(a.id);

    expect(d.getByTestId("flow-badge")).toHaveTextContent("Checked in");
    for (const name of [...FLOW_BUTTONS, ...CHANGE_BUTTONS]) expect(d.queryByRole("button", { name })).not.toBeInTheDocument();
  });

  it("a cancelled or no-show appointment has no flow and no flow buttons", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", status: "Cancelled", cancelReason: "Illness" });
    const { d } = await openAppointment(a.id);

    for (const name of FLOW_BUTTONS) expect(d.queryByRole("button", { name })).not.toBeInTheDocument();
    expect(d.queryByTestId("flow-badge")).not.toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("the drawer in each flow state has no axe violations and the flow buttons are a labelled group", async () => {
    const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee" });
    const { view, d } = await openAppointment(a.id);
    expect(d.getByRole("group", { name: "Patient flow" })).toBeInTheDocument();
    expect(await axe(view.container)).toHaveNoViolations();

    await userEvent.click(d.getByRole("button", { name: "Check in" }));
    await d.findByText("Patient checked in.");
    expect(await axe(view.container)).toHaveNoViolations();
    expect(document.activeElement).toBe(document.getElementById("cal-drawer-title")); // focus is not lost when the clicked button disappears

    await userEvent.click(d.getByRole("button", { name: "Complete treatment" }));
    await d.findByText("Treatment completed.", { selector: "p" });
    expect(await axe(view.container)).toHaveNoViolations();
  });
});
