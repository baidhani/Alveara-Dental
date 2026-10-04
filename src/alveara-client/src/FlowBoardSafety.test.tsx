import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeFlowServer } from "./test/fakeFlowServer";
import type { PatientFlowState } from "./services/schedulingApi";

/**
 * ALV-N011: the live visit board's MINIMAL patient-safety indicator. The board is shared by front-office and clinical staff, so it may show only that a patient has something active or a
 * clearance open - in words - and never a diagnosis, allergy or medication. A caller without the indicator permission gets nothing at all, and a patient with nothing on file gets nothing
 * (no invented "all clear"). The authorization and minimization are proven at the HTTP boundary by the backend tests; here is what the screen does with what the server sends.
 */
const DAY = "2030-01-14";
const ANN = "pat-ann", BO = "pat-bo", CY = "pat-cy";
const BASE = ["ViewPatientRecords", "ViewSchedule", "UpdateVisitFlow", "UpdateChairsideFlow"];
let server: FakeFlowServer;

beforeEach(() => {
  server = new FakeFlowServer();
  server.serverNow = "2030-01-14T14:59:30.000Z";
  server.permissions = [...BASE, "ViewSafetyIndicator"];
  server.install();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function visit(patientId: string, name: string, start: string, flow: PatientFlowState = "Scheduled") {
  const a = server.seed({ start, patientName: name, flowState: flow });
  a.patientId = patientId;
  return a;
}
async function openBoard() {
  window.history.pushState({}, "", "/flow");
  const view = render(<App />);
  fireEvent.change(await screen.findByLabelText("Date"), { target: { value: DAY } });
  await waitFor(() => expect(screen.getByRole("region", { name: /^Scheduled \(/ })).toBeInTheDocument());
  return view;
}
const card = (name: string) => screen.getByRole("heading", { name }).closest("article")!;

describe("the visit board's safety indicator", () => {
  it("says only 'Safety alert on file' and 'Clearance open' on the cards that have them, and nothing on the others", async () => {
    visit(ANN, "Ann Lee", `${DAY}T09:00`);
    visit(BO, "Bo Kim", `${DAY}T10:00`);
    visit(CY, "Cy Poe", `${DAY}T11:00`);
    server.safety[ANN] = { alert: true, clearance: true };
    server.safety[BO] = { alert: false, clearance: true };
    await openBoard();

    expect(within(card("Ann Lee")).getByText("Safety alert on file")).toBeInTheDocument();
    expect(within(card("Ann Lee")).getByText("Clearance open")).toBeInTheDocument();
    expect(within(card("Bo Kim")).queryByText("Safety alert on file")).not.toBeInTheDocument();
    expect(within(card("Bo Kim")).getByText("Clearance open")).toBeInTheDocument();
    expect(card("Cy Poe").querySelector("[data-safety]")).toBeNull();                  // nothing on file: nothing drawn, no reassuring "all clear"
  });

  it("never draws a diagnosis, allergy, medication, category or severity - the card has no place to put one", async () => {
    visit(ANN, "Ann Lee", `${DAY}T09:00`);
    server.safety[ANN] = { alert: true, clearance: false };
    await openBoard();
    const text = card("Ann Lee").textContent ?? "";
    expect(text).toContain("Safety alert on file");
    for (const word of ["Critical", "High", "Allergy", "Penicillin", "Anticoagulant", "Condition", "Medication"]) expect(text).not.toContain(word);
    expect(Object.keys(server.safety[ANN]).sort()).toEqual(["alert", "clearance"]);   // the whole contract is two booleans
  });

  it("draws nothing for a caller who does not hold the indicator permission, even when the patient has alerts", async () => {
    server.permissions = BASE;
    visit(ANN, "Ann Lee", `${DAY}T09:00`);
    server.safety[ANN] = { alert: true, clearance: true };
    await openBoard();
    expect(card("Ann Lee").querySelector("[data-safety]")).toBeNull();
    expect(screen.queryByText("Safety alert on file")).not.toBeInTheDocument();
    expect(screen.queryByText("Clearance open")).not.toBeInTheDocument();
  });

  it("has no accessibility violations with the indicator on the board", async () => {
    visit(ANN, "Ann Lee", `${DAY}T09:00`);
    server.safety[ANN] = { alert: true, clearance: true };
    const { container } = await openBoard();
    expect(await axe(container)).toHaveNoViolations();
  });
});
