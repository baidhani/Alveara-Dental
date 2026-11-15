import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeFlowServer } from "./test/fakeFlowServer";
import { makePatient } from "./test/fakePatientServer";

/**
 * ALV-N021 A2 (briefing 6.3): the checkout panel on the Visit board and in the calendar drawer, through the real <App /> against the in-memory visit fake.
 * The gate itself (applicability, ledger verification, who may record what, append-only history) is proven by the backend tests; what is proven here is that the
 * panel shows the server's items and summary in words, records each decision with an Idempotency-Key that a retry reuses, asks for a reason or a ledger entry
 * where the server needs one, explains each refusal without recording anything, and that Complete becomes available once every item is resolved.
 */
const DAY = "2030-01-14";
const ANN = "aaaaaaaa-0000-0000-0000-000000000001";
const ALL = ["ViewPatientRecords", "ViewSchedule", "ManageAppointments", "UpdateVisitFlow", "UpdateChairsideFlow"];

let server: FakeFlowServer;

beforeEach(() => {
  server = new FakeFlowServer();
  server.serverNow = "2030-01-14T14:59:30.000Z";
  server.permissions = ALL;
  server.add(makePatient({ id: ANN, firstName: "Ann", lastName: "Lee" }));
  for (const p of server.providers) p.weeklyAvailability.push({ dayOfWeek: 1, startLocal: "08:00", endLocal: "17:00" } as never);
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const card = (id: string) => document.querySelector(`[data-visit="${id}"]`) as HTMLElement;
const checkoutPosts = () => server.callsToSchedule("POST", "/api/appointments").filter((c) => /\/checkout\//.test(c.url));

function readyForCheckout(financial = false) {
  const a = server.seed({ start: `${DAY}T09:00`, patientName: "Ann Lee", flowState: "CheckedOut" });
  a.patientId = ANN;
  server.checkoutOpen.add(a.id);
  if (financial) server.financialDue.add(a.id);
  return a;
}

async function openBoardCheckout(id: string) {
  window.history.pushState({}, "", "/flow");
  const view = render(<App />);
  fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
  await waitFor(() => expect(card(id)).toBeInTheDocument());
  await userEvent.click(within(card(id)).getByRole("button", { name: "Show checkout for Ann Lee" }));
  const panel = within(await within(card(id)).findByRole("region", { name: "Checkout for Ann Lee" }));
  return { view, panel };
}

async function record(panel: ReturnType<typeof within>, item: RegExp, outcome: string, extra: { reason?: string; entry?: string } = {}) {
  const form = within(panel.getByRole("form", { name: item }));
  await userEvent.selectOptions(form.getByRole("combobox"), outcome);
  if (extra.entry !== undefined) await userEvent.type(form.getByLabelText("Ledger entry number (required)"), extra.entry);
  if (extra.reason !== undefined) await userEvent.type(form.getByLabelText(/^Reason/), extra.reason);
  await userEvent.click(form.getByRole("button", { name: "Record" }));
  return form;
}

describe("the checkout panel on the Visit board", () => {
  it("shows each item in words, records the decisions, and Complete becomes available once all are resolved", async () => {
    const a = readyForCheckout(true);
    server.ledgerEntries.add(7);   // a payment posted to Ann's account today
    const { panel } = await openBoardCheckout(a.id);
    expect(panel.getByText("Ready for checkout — the patient has not been marked as left.")).toBeInTheDocument();
    expect(card(a.id).querySelector('[data-item="treatment_ended"]')).toHaveTextContent("Treatment ended: Done");
    expect(within(card(a.id)).getByRole("button", { name: "Complete visit Ann Lee" })).toBeDisabled();

    await record(panel, /patient has left/i, "Patient has left");
    expect(await panel.findByText(/^Patient has left recorded\. Ready for checkout — follow-up decision required\./)).toBeInTheDocument();

    await record(panel, /follow-up/i, "Patient declined");
    expect(await panel.findByText(/^Follow-up recorded\. Ready for checkout — financial decision required\./)).toBeInTheDocument();

    await record(panel, /payment/i, "Payment recorded", { entry: "7" });
    expect(await panel.findByText("Ready for checkout — every item is resolved; the visit can be completed.", { selector: "strong" })).toBeInTheDocument();
    expect(panel.getByText(/Payment recorded \(ledger entry 7, by Desk One\)/)).toBeInTheDocument();
    await waitFor(() => expect(within(card(a.id)).getByRole("button", { name: "Complete visit Ann Lee" })).toBeEnabled());

    expect(checkoutPosts().map((c) => [c.url.split("/").at(-1), c.body])).toEqual([
      ["departure", { outcome: "patient_left", reason: null, ledgerEntryNumber: null }],
      ["follow_up", { outcome: "patient_declined", reason: null, ledgerEntryNumber: null }],
      ["financial", { outcome: "payment_recorded", reason: null, ledgerEntryNumber: 7 }],
    ]);
    const keys = checkoutPosts().map((c) => c.headers["Idempotency-Key"]);
    expect(new Set(keys).size).toBe(3);   // one key per decision
  });

  it("asks for a reason and a ledger entry where the server needs one, without sending anything", async () => {
    const a = readyForCheckout(true);
    const { panel } = await openBoardCheckout(a.id);
    const followUp = await record(panel, /follow-up/i, "Deferred");
    expect(followUp.getByRole("alert")).toHaveTextContent("Give a reason for this outcome.");
    const money = await record(panel, /payment/i, "Waived");
    expect(money.getByRole("alert")).toHaveTextContent("Enter the ledger entry number of the payment or waiver.");
    expect(checkoutPosts()).toHaveLength(0);
  });

  it("explains a payment the server cannot verify and records nothing", async () => {
    const a = readyForCheckout(true);
    const { panel } = await openBoardCheckout(a.id);
    const form = await record(panel, /payment/i, "Payment recorded", { entry: "99" });

    expect(await form.findByRole("alert")).toHaveTextContent("No payment with that number was posted to this patient's account on the day of the visit (or it was reversed). Nothing was recorded.");
    expect(server.checkoutWrites).toBe(0);
  });

  it("after a dropped connection, pressing Record again reuses the same key and records once", async () => {
    const a = readyForCheckout();
    server.dropNextCheckout = true;
    const { panel } = await openBoardCheckout(a.id);
    const form = await record(panel, /patient has left/i, "Patient has left");
    expect(await form.findByRole("alert")).toHaveTextContent("could not confirm whether that was recorded");
    expect(server.checkoutWrites).toBe(1);   // it was recorded

    await userEvent.click(form.getByRole("button", { name: "Record" }));
    expect(await panel.findByText(/^Patient has left recorded\./)).toBeInTheDocument();
    const keys = checkoutPosts().map((c) => c.headers["Idempotency-Key"]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(server.checkoutWrites).toBe(1);   // not twice
  });

  it("explains a role the server refuses, and records nothing", async () => {
    server.permissions = ["ViewSchedule", "UpdateChairsideFlow"];
    const a = readyForCheckout();
    const { panel } = await openBoardCheckout(a.id);
    const form = await record(panel, /patient has left/i, "Patient has left");
    expect(await form.findByRole("alert")).toHaveTextContent("Your role may not record this outcome. Nothing was recorded.");
    expect(server.checkoutWrites).toBe(0);
  });

  it("is offered only on a visit that is ready for checkout, and has no axe violations", async () => {
    const a = readyForCheckout(true);
    const other = server.seed({ start: `${DAY}T11:00`, patientName: "Bo Kim", flowState: "InTreatment", provider: "prov-b", operatory: "op-2" });
    const { view } = await openBoardCheckout(a.id);
    expect(within(card(other.id)).queryByRole("button", { name: /Show checkout/ })).not.toBeInTheDocument();
    expect(await axe(view.container)).toHaveNoViolations();
  });
});

describe("the checkout panel in the calendar drawer", () => {
  it("shows the same panel for a visit ready for checkout, and recording the last item enables Complete", async () => {
    const a = readyForCheckout();
    window.history.pushState({}, "", "/calendar");
    render(<App />);
    fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
    await userEvent.click(await waitFor(() => { const b = document.querySelector(`[data-appointment="${a.id}"]`) as HTMLElement; expect(b).toBeInTheDocument(); return b; }));
    const d = within(screen.getByRole("dialog"));
    const panel = within(await d.findByRole("region", { name: "Checkout for Ann Lee" }));
    expect(await d.findByRole("button", { name: "Complete visit" })).toBeDisabled();

    await record(panel, /patient has left/i, "Patient has left");
    await panel.findByText(/^Patient has left recorded\./);
    await record(panel, /follow-up/i, "Not clinically required");
    await panel.findByText(/^Follow-up recorded\./);
    await waitFor(() => expect(d.getByRole("button", { name: "Complete visit" })).toBeEnabled());
  });
});
