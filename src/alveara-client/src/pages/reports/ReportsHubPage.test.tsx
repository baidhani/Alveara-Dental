import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { ReportsHubPage } from "./ReportsHubPage";
import { FakeReportServer, appointment, refusal } from "./reportTestServer";

// ALV-N006 slice 6a: the reporting hub, its filter bar and its states, against a small in-memory server that answers like the real API.

let server: FakeReportServer;

beforeEach(() => { server = new FakeReportServer(); server.install(); });
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function Where() {
  const l = useLocation();
  return <p data-testid="where">{l.pathname}{l.search}</p>;
}

function renderHub(path = "/reports/schedule?from=2030-01-14&to=2030-01-17") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <Where />
        <Routes>
          <Route path="/reports/:reportId?" element={<ReportsHubPage />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe("what the hub offers", () => {
  it("lists only the report groups the person's permissions allow", async () => {
    renderHub();
    const nav = await screen.findByRole("navigation", { name: "Reports" });
    expect(within(nav).getByRole("heading", { name: "Operational" })).toBeInTheDocument();
    for (const name of ["Schedule", "Patient flow", "Recall and follow-up", "Cancellations and no-shows"]) expect(within(nav).getByRole("link", { name })).toBeInTheDocument();
    expect(within(nav).queryByRole("heading", { name: "Financial" })).not.toBeInTheDocument();
    expect(within(nav).queryByRole("heading", { name: "Clinical" })).not.toBeInTheDocument();
  });

  it("says so, and calls no report, when the person may see none", async () => {
    server.permissions = ["ViewPatientRecords"];
    renderHub();
    expect(await screen.findByText("No reports are available to you")).toBeInTheDocument();
    expect(server.calls.filter((c) => c.path.startsWith("/api/reports"))).toHaveLength(0);
  });

  it("opens the first available report when none is named and keeps the date range in the address", async () => {
    renderHub("/reports");
    await screen.findByRole("heading", { name: "Schedule", level: 2 });
    const where = screen.getByTestId("where").textContent!;
    expect(where).toMatch(/^\/reports\/schedule\?from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}$/);
  });

  it("says a named report is not available to the person instead of showing it or something else", async () => {
    renderHub("/reports/production?from=2030-01-14&to=2030-01-17");
    expect(await screen.findByText("That report is not available to you")).toBeInTheDocument();
    expect(server.reports("/api/reports/production")).toHaveLength(0);
  });
});

describe("the filter bar", () => {
  it("asks the server nothing new until Show report is pressed, then asks for exactly the new range and puts it in the address", async () => {
    const user = userEvent.setup();
    renderHub();
    await screen.findByRole("table", { name: "Appointments by day" });
    expect(server.reports("/api/reports/schedule")).toHaveLength(1);

    const from = screen.getByLabelText("First day");
    await user.clear(from);
    await user.type(from, "2030-01-10");
    expect(server.reports("/api/reports/schedule")).toHaveLength(1);                         // typing a date asks nothing
    await user.click(screen.getByRole("button", { name: "Show report" }));

    await waitFor(() => expect(server.reports("/api/reports/schedule")).toHaveLength(2));
    expect(server.reports("/api/reports/schedule")[1].params.get("from")).toBe("2030-01-10");
    expect(screen.getByTestId("where").textContent).toContain("from=2030-01-10");
  });

  it("refuses a range whose first day is after its last, with a message and a disabled button", async () => {
    const user = userEvent.setup();
    renderHub();
    await screen.findByRole("table", { name: "Appointments by day" });
    const from = screen.getByLabelText("First day");
    await user.clear(from);
    await user.type(from, "2030-02-01");

    expect(screen.getByRole("alert")).toHaveTextContent("The first date must not be after the last date.");
    expect(screen.getByRole("button", { name: "Show report" })).toBeDisabled();
  });

  it("sends the chosen providers, and offers none for a report the server cannot filter by provider", async () => {
    const user = userEvent.setup();
    renderHub();
    await user.click(await screen.findByRole("checkbox", { name: "Dr. Patel" }));
    await user.click(screen.getByRole("button", { name: "Show report" }));
    await waitFor(() => expect(server.reports("/api/reports/schedule")).toHaveLength(2));
    expect(server.reports("/api/reports/schedule")[1].params.getAll("providerIds")).toEqual(["pb"]);

    await user.click(screen.getByRole("link", { name: "Recall and follow-up" }));
    await screen.findByText("This report covers the whole practice. It cannot be narrowed by provider or location.");
    expect(screen.queryByRole("checkbox", { name: "Dr. Patel" })).not.toBeInTheDocument();
    await waitFor(() => expect(server.reports("/api/reports/follow-ups")).toHaveLength(1));
    expect(server.reports("/api/reports/follow-ups")[0].params.getAll("providerIds")).toEqual([]);          // the filter is not even sent
  });

  it("says when the list of providers could not be loaded and still shows the report", async () => {
    server.reply = (c) => (c.path === "/api/config/providers" ? refusal(403, "forbidden") : null);
    renderHub();
    expect(await screen.findByText("The list of providers could not be loaded, so this report cannot be narrowed by provider.")).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: "Appointments by day" })).toBeInTheDocument();
  });
});

describe("the states of a report", () => {
  it("shows the time it was made and how the numbers were read", async () => {
    renderHub();
    const meta = await screen.findByTestId("report-meta");
    expect(meta).toHaveTextContent("2030-01-14 to 2030-01-17 (4 days, America/Chicago). Made");
    expect(meta).toHaveTextContent("Every number was read at the same moment.");
  });

  it("says plainly that there is nothing to report instead of showing a screen of zeros", async () => {
    server.noData = true;
    renderHub();
    expect(await screen.findByText("Nothing to report")).toBeInTheDocument();
    expect(screen.getByText("No appointments start in this period for the chosen filters.")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Appointments by day" })).not.toBeInTheDocument();
  });

  it("explains a range that is too large in words and asks again when told to try again", async () => {
    const user = userEvent.setup();
    let refuse = true;
    server.reply = (c) => (refuse && c.path === "/api/reports/schedule" ? refusal(400, "range_too_large") : null);
    renderHub();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("That range is longer than a report can cover. Choose a shorter one.");
    expect(screen.queryByRole("table", { name: "Appointments by day" })).not.toBeInTheDocument();

    refuse = false;
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("table", { name: "Appointments by day" })).toBeInTheDocument();
  });

  it("says a refused permission in words and a lost connection in words", async () => {
    server.reply = (c) => (c.path === "/api/reports/schedule" ? refusal(403, "forbidden") : null);
    renderHub();
    expect(await screen.findByText("You do not have access to this report.")).toBeInTheDocument();
  });

  it("shows loading, not the previous answer, while a new filter is being answered", async () => {
    const user = userEvent.setup();
    renderHub();
    await screen.findByRole("table", { name: "Appointments by day" });
    let release: (r: Response) => void = () => {};
    server.reply = (c) => (c.path === "/api/reports/schedule" ? new Promise<Response>((resolve) => { release = resolve; }) : null);
    await user.click(screen.getByRole("button", { name: "Last 7 days" }));
    await user.click(screen.getByRole("button", { name: "Show report" }));

    expect(await screen.findByText("Making the report…")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Appointments by day" })).not.toBeInTheDocument();
    release(refusal(400, "report_too_large"));
    expect(await screen.findByText("That range holds too many records for one report. Choose a shorter range or fewer providers.")).toBeInTheDocument();
  });
});

describe("opening the records behind a number", () => {
  it("lists the appointments behind a count, with the total, and shows more only on request without repeating any", async () => {
    const user = userEvent.setup();
    server.appointments = [appointment(1), appointment(2), appointment(3)];
    renderHub();
    const table = await screen.findByRole("table", { name: "Appointments by booked provider" });
    await user.click(within(table).getByRole("button", { name: "4, show the records: Dr. Rivera, all appointments" }));

    const records = await screen.findByRole("region", { name: "Records: Dr. Rivera: all appointments" });
    expect(await within(records).findByText("3 records; showing 2.")).toBeInTheDocument();
    const call = server.reports("/api/reports/schedule/appointments")[0];
    expect(call.params.get("providerId")).toBe("pa");
    expect(call.params.get("from")).toBe("2030-01-14");
    await user.click(within(records).getByRole("button", { name: "Show more" }));

    expect(await within(records).findByText("3 records; showing 3.")).toBeInTheDocument();
    expect(within(records).queryByRole("button", { name: "Show more" })).not.toBeInTheDocument();
    expect(server.reports("/api/reports/schedule/appointments")[1].params.get("afterId")).toBe("a2");
    expect(within(records).getAllByRole("link", { name: "Open patient record" })).toHaveLength(3);
    expect(within(records).getAllByRole("link", { name: "Open patient record" })[0]).toHaveAttribute("href", "/patients/p1");
  });

  it("shows no link to a patient record to a person who may not open one", async () => {
    const user = userEvent.setup();
    server.permissions = ["ViewOperationalReports"];
    renderHub();
    const table = await screen.findByRole("table", { name: "Appointments by day" });
    await user.click(within(table).getAllByRole("button", { name: /show the records: 2030-01-14, all appointments/ })[0]);

    const records = await screen.findByRole("region", { name: /Records:/ });
    expect(await within(records).findAllByText("Patient record (you cannot open it)")).toHaveLength(2);
    expect(within(records).queryByRole("link")).not.toBeInTheDocument();
  });

  it("says a failure to load the records in words and can close the list", async () => {
    const user = userEvent.setup();
    server.reply = (c) => (c.path.endsWith("/appointments") ? refusal(400, "report_too_large") : null);
    renderHub();
    const table = await screen.findByRole("table", { name: "Appointments by day" });
    await user.click(within(table).getAllByRole("button", { name: /show the records: 2030-01-14, all appointments/ })[0]);

    expect(await screen.findByText("The records could not be shown")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Close the list" }));
    expect(screen.queryByRole("region", { name: /Records:/ })).not.toBeInTheDocument();
  });

  it("makes a zero plain text, because there is nothing behind it to list", async () => {
    renderHub();
    const table = await screen.findByRole("table", { name: "Appointments by day" });
    const emptyDay = within(table).getByRole("row", { name: /2030-01-17/ });
    expect(within(emptyDay).queryByRole("button")).not.toBeInTheDocument();
    expect(within(emptyDay).getAllByText("0").length).toBeGreaterThanOrEqual(4);
  });
});

describe("accessibility", () => {
  it("has no detectable violations with a report and its records open", async () => {
    const user = userEvent.setup();
    const { container } = renderHub();
    const table = await screen.findByRole("table", { name: "Appointments by booked provider" });
    await user.click(within(table).getByRole("button", { name: "4, show the records: Dr. Rivera, all appointments" }));
    await screen.findByText("3 records; showing 2.");

    expect(await axe(container)).toHaveNoViolations();
  });
});
