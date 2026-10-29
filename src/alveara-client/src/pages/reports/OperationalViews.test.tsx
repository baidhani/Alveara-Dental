import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { ReportsHubPage } from "./ReportsHubPage";
import { FakeReportServer, appointment, cancellationsData, flowData, followUpsData, task } from "./reportTestServer";

// ALV-N006 slice 6a: the patient-flow, recall and follow-up, and cancellations views, against the small in-memory server.

let server: FakeReportServer;

beforeEach(() => { server = new FakeReportServer(); server.install(); });
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function renderReport(id: string) {
  return render(
    <MemoryRouter initialEntries={[`/reports/${id}?from=2030-01-14&to=2030-01-16`]}>
      <AuthProvider>
        <Routes><Route path="/reports/:reportId?" element={<ReportsHubPage />} /></Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe("patient flow", () => {
  it("shows every visit state as a row, in order, with the moves recorded, and says the states add up", async () => {
    renderReport("patient-flow");
    const states = await screen.findByRole("table", { name: "Visits by state" });
    expect(within(states).getAllByRole("rowheader").map((h) => h.textContent)).toEqual(["Scheduled", "Confirmed", "Checked in", "Ready", "Seated", "In treatment", "Checked out", "Completed"]);
    expect(screen.getByText("The visit states add up to the scheduled appointments.")).toBeInTheDocument();
    const moves = screen.getByRole("table", { name: "Moves recorded on these appointments" });
    expect(within(moves).getByRole("row", { name: "Checked in 2" })).toBeInTheDocument();
    expect(within(moves).getByRole("row", { name: "Treatment started 1" })).toBeInTheDocument();
  });

  it("asks for the appointments in a state with the state and the same range", async () => {
    const user = userEvent.setup();
    server.appointments = [appointment(1, { flowState: "CheckedIn" })];
    renderReport("patient-flow");
    const states = await screen.findByRole("table", { name: "Visits by state" });
    await user.click(within(states).getByRole("button", { name: "1, show the records: visits checked in" }));

    const records = await screen.findByRole("region", { name: "Records: Visits: checked in" });
    expect(await within(records).findByText("1 record; showing 1.")).toBeInTheDocument();
    expect(within(records).getByText("Checked in")).toBeInTheDocument();
    const call = server.reports("/api/reports/patient-flow/appointments")[0];
    expect([call.params.get("flowState"), call.params.get("from"), call.params.get("to")]).toEqual(["CheckedIn", "2030-01-14", "2030-01-16"]);
  });

  it("does not let the numbers pass as right when the server says they do not add up", async () => {
    server.flow = flowData({ reconciled: false });
    renderReport("patient-flow");
    expect(await screen.findByRole("alert")).toHaveTextContent("The visit states do not add up to the scheduled appointments. Do not rely on these numbers; tell your administrator.");
  });
});

describe("recall and follow-up", () => {
  it("shows the moment it was judged at and a row for each kind that has tasks, with a total row", async () => {
    renderReport("follow-ups");
    const table = await screen.findByRole("table", { name: "Follow-up tasks due in the period, by kind and outcome" });
    expect(screen.getByText(/Judged at .*A task is overdue when it is still open and was due before that moment\./)).toBeInTheDocument();
    expect(within(table).getAllByRole("rowheader").map((h) => h.textContent)).toEqual(["Manual", "Recall", "All kinds"]);        // the kind with no task is not listed
    const total = within(table).getByRole("row", { name: /All kinds/ });
    expect(within(total).getByRole("button", { name: "7, show the records: All kinds, due" })).toBeInTheDocument();
    expect(screen.getByText("The outcomes add up to the tasks due.")).toBeInTheDocument();
  });

  it("asks for the tasks in a cell at the SAME moment the report was judged at, so the list matches the number", async () => {
    const user = userEvent.setup();
    server.tasks = [task(1, { status: "Open" })];
    renderReport("follow-ups");
    const table = await screen.findByRole("table", { name: "Follow-up tasks due in the period, by kind and outcome" });
    await user.click(within(table).getByRole("button", { name: "1, show the records: Manual, open and overdue" }));

    const records = await screen.findByRole("region", { name: "Records: Manual: open and overdue" });
    expect(await within(records).findByText("1 record; showing 1.")).toBeInTheDocument();
    const call = server.reports("/api/reports/follow-ups/tasks")[0];
    expect([call.params.get("kind"), call.params.get("outcome"), call.params.get("asOf")]).toEqual(["Manual", "OpenOverdue", "2030-01-15T18:00:00+00:00"]);
    expect(within(records).getByText("Assigned")).toBeInTheDocument();
    expect(within(records).queryByText(/Call/)).not.toBeInTheDocument();                  // a task's title is never shown
  });

  it("says a task closed by the system is closed by the system", async () => {
    const user = userEvent.setup();
    server.tasks = [task(1, { status: "Completed", closedAtUtc: "2030-01-15T13:00:00+00:00", closedBySystem: true, assigneeUserId: null, kind: "Recall" })];
    renderReport("follow-ups");
    const table = await screen.findByRole("table", { name: "Follow-up tasks due in the period, by kind and outcome" });
    await user.click(within(table).getByRole("button", { name: "1, show the records: Recall, closed by the system" }));

    const records = await screen.findByRole("region", { name: "Records: Recall: closed by the system" });
    expect(await within(records).findByText(/\(by the system\)/)).toBeInTheDocument();
    expect(within(records).getByText("Unassigned")).toBeInTheDocument();
  });
});

describe("cancellations and no-shows", () => {
  it("shows the counts with their rates and the lead-time cuts in words", async () => {
    renderReport("cancellations");
    const lead = await screen.findByRole("table", { name: "Cancelled appointments by how early they were cancelled" });
    expect(within(lead).getAllByRole("rowheader").map((h) => h.textContent)).toEqual(["Less than 24 hours before", "1 to 7 days before", "More than 7 days before", "No time recorded"]);
    expect(screen.getByText("57.1% of booked")).toBeInTheDocument();
    expect(screen.getByText("14.3% of booked")).toBeInTheDocument();
    expect(screen.getByText("The providers, the types and the lead times add up to the totals.")).toBeInTheDocument();
  });

  it("says a rate is not available, never zero, when nothing was booked", async () => {
    server.cancellations = cancellationsData({
      totals: { booked: 0, cancelled: 0, noShow: 0, cancellationRate: null, noShowRate: null }, leadTimes: [], providers: [{ providerProfileId: "pa", providerName: "Dr. Rivera", counts: { booked: 0, cancelled: 0, noShow: 0, cancellationRate: null, noShowRate: null } }], types: [],
    });
    renderReport("cancellations");
    const providers = await screen.findByRole("table", { name: "Cancellations and no-shows by booked provider" });
    expect(within(providers).getAllByText("Not available")).toHaveLength(2);
    expect(within(providers).queryByText("0.0%")).not.toBeInTheDocument();
  });

  it("asks for the appointments behind a lead-time bucket and a type with the right criteria", async () => {
    const user = userEvent.setup();
    server.appointments = [appointment(1, { status: "Cancelled" })];
    renderReport("cancellations");
    const lead = await screen.findByRole("table", { name: "Cancelled appointments by how early they were cancelled" });
    await user.click(within(lead).getByRole("button", { name: "1, show the records: cancelled Less than 24 hours before" }));
    const first = server.reports("/api/reports/cancellations/appointments")[0];
    expect([first.params.get("measure"), first.params.get("leadTime")]).toEqual(["Cancelled", "LessThan24h"]);

    const types = screen.getByRole("table", { name: "Cancellations and no-shows by appointment type" });
    await user.click(within(types).getByRole("button", { name: "1, show the records: Quick check, no-shows" }));
    const second = server.reports("/api/reports/cancellations/appointments")[1];
    expect([second.params.get("measure"), second.params.get("appointmentTypeId")]).toEqual(["NoShow", "t2"]);
  });
});

describe("accessibility of the other views", () => {
  it.each(["patient-flow", "follow-ups", "cancellations"])("has no detectable violations for %s", async (id) => {
    const { container } = renderReport(id);
    await screen.findAllByRole("table");
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("what the follow-up screen does with a server that disagrees with itself", () => {
  it("does not let the numbers pass as right when the outcomes do not add up", async () => {
    server.followUps = followUpsData({ reconciled: false });
    renderReport("follow-ups");
    expect(await screen.findByRole("alert")).toHaveTextContent("The outcomes do not add up to the tasks due. Do not rely on these numbers; tell your administrator.");
  });
});
