import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { AuditLogPage } from "./AuditLogPage";
import { AuthContext } from "../contexts/authContextObject";
import type { AuthContextValue } from "../contexts/authContextObject";
import type { AuditLogEntry } from "../services/authApi";

/**
 * ALV-N025 slice 1 (F-149, F-150, F-041, BR-02): the audit page names people (ids in a tooltip), says what the log holds, filters by event type and date,
 * and links a patient edit to the patient's History only for a viewer who may see patients - otherwise it says so and offers no link.
 */
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const asUser = (permissions: string[]) => ({ hasPermission: (p: string) => permissions.includes(p) }) as unknown as AuthContextValue;
const PATIENT = "aaaaaaaa-0000-0000-0000-000000000001";
const ADMIN = "bbbbbbbb-0000-0000-0000-000000000002";
const edit = (): AuditLogEntry => ({
  id: "e1", eventType: "PatientUpdated", targetUserAccountId: PATIENT, performedByUserAccountId: ADMIN, details: "Patient updated: phone.", timestampUtc: "2030-01-01T10:00:00Z",
  entityType: "Patient", reason: null, correlationId: "h1", performedByName: "Dana Admin", targetName: null, historyEntryId: "h1",
});

function stub(entries: AuditLogEntry[]) {
  const fetchMock = vi.fn().mockImplementation((url: RequestInfo | URL) =>
    Promise.resolve(String(url).includes("/event-types") ? json(["PatientRegistered", "PatientUpdated"]) : json(entries)));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

const renderAs = (permissions: string[]) => render(
  <MemoryRouter>
    <AuthContext.Provider value={asUser(permissions)}><AuditLogPage /></AuthContext.Provider>
  </MemoryRouter>,
);

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("ALV-N025 audit page", () => {
  it("names who acted, keeps the id in a tooltip, and says the log holds every kind of action (F-149, F-150)", async () => {
    stub([edit()]);
    renderAs(["ViewAuditLog", "ViewPatientRecords"]);
    const who = await screen.findByText("Dana Admin");
    expect(who).toHaveAttribute("title", ADMIN);
    expect(screen.queryByText(ADMIN)).not.toBeInTheDocument();
    expect(screen.getByText(/recorded actions of every kind/)).toBeInTheDocument();
    expect(screen.queryByText(/account, role, and session events/)).not.toBeInTheDocument();
  });

  it("links a patient edit to the patient's History for a viewer who may see patients (F-041)", async () => {
    stub([edit()]);
    renderAs(["ViewAuditLog", "ViewPatientRecords"]);
    const link = await screen.findByRole("link", { name: "View in patient history" });
    expect(link).toHaveAttribute("href", `/patients/${PATIENT}/history`);
  });

  it("offers no History link to a viewer without patient access, and says why (BR-02)", async () => {
    stub([edit()]);
    renderAs(["ViewAuditLog"]);
    expect(await screen.findByText(/History is available to staff who may see this patient/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "View in patient history" })).not.toBeInTheDocument();
  });

  it("filters by event type and date range (F-150)", async () => {
    const user = userEvent.setup();
    const fetchMock = stub([edit()]);
    renderAs(["ViewAuditLog"]);
    const form = await screen.findByRole("form", { name: "Filter the audit log" });
    await waitFor(() => expect(within(form).getAllByRole("option").map((o) => o.textContent)).toEqual(["All events", "PatientRegistered", "PatientUpdated"]));
    await user.selectOptions(within(form).getByLabelText("Event"), "PatientUpdated");
    await user.type(within(form).getByLabelText("From"), "2030-01-01");
    await user.click(within(form).getByRole("button", { name: "Show" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([u]) => String(u).includes("eventType=PatientUpdated") && String(u).includes("fromUtc="))).toBe(true));
  });
});
