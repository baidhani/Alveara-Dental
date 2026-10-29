import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { saveFile } from "../../services/saveFile";
import { ReportsHubPage } from "./ReportsHubPage";
import { FakeReportServer, baselineRecord, refusal } from "./reportTestServer";

// ALV-N006 slice 7: taking a report away as a CSV file or on paper.

vi.mock("../../services/saveFile", () => ({ saveFile: vi.fn() }));

let server: FakeReportServer;

const csvFor = (name: string) => new Response("﻿Report,x\r\n", { status: 200, headers: { "Content-Type": "text/csv", "Content-Disposition": `attachment; filename="${name}"` } });
const isExport = (path: string) => path.endsWith("/export");

beforeEach(() => { vi.mocked(saveFile).mockClear(); server = new FakeReportServer(); server.permissions = ["ViewFinancialReports", "ViewMeasurementReports"]; server.install(); });
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <AuthProvider>
        <Routes><Route path="/reports/:reportId?" element={<ReportsHubPage />} /></Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe("downloading a report", () => {
  it("asks for the export with the filter the screen was asked with, saves the file under the name the server gave it and says so", async () => {
    const user = userEvent.setup();
    server.reply = (c) => (isExport(c.path) ? csvFor("production_2025-04-01_to_2025-04-30.csv") : null);
    renderAt("/reports/production?from=2025-04-01&to=2025-04-30&providerIds=p1");

    await user.click(await screen.findByRole("button", { name: "Download CSV" }));

    expect(await screen.findByText("Downloaded production_2025-04-01_to_2025-04-30.csv.")).toBeInTheDocument();
    const [asked] = server.reports("/api/reports/production/export");
    expect(asked.params.get("from")).toBe("2025-04-01");
    expect(asked.params.get("to")).toBe("2025-04-30");
    expect(asked.params.getAll("providerIds")).toEqual(["p1"]);
    expect(vi.mocked(saveFile)).toHaveBeenCalledTimes(1);
    expect(vi.mocked(saveFile).mock.calls[0][0]).toBe("production_2025-04-01_to_2025-04-30.csv");
  });

  it("does not send a filter the report cannot use, exactly as the screen does not", async () => {
    const user = userEvent.setup();
    server.reply = (c) => (isExport(c.path) ? csvFor("collections.csv") : null);
    renderAt("/reports/collections?from=2025-04-01&to=2025-04-30&providerIds=p1&locationId=l1");

    await user.click(await screen.findByRole("button", { name: "Download CSV" }));
    await screen.findByText("Downloaded collections.csv.");

    const [asked] = server.reports("/api/reports/collections/export");
    expect(asked.params.getAll("providerIds")).toEqual([]);
    expect(asked.params.get("locationId")).toBeNull();
  });

  it("says why in words when the server refuses, and saves no file", async () => {
    const user = userEvent.setup();
    server.reply = (c) => (isExport(c.path) ? refusal(413, "export_too_large") : null);
    renderAt("/reports/production?from=2025-04-01&to=2025-04-30");

    await user.click(await screen.findByRole("button", { name: "Download CSV" }));

    expect(await screen.findByText("That report has too many lines for one file. Choose a shorter range.")).toBeInTheDocument();
    expect(vi.mocked(saveFile)).not.toHaveBeenCalled();
    expect(screen.queryByText(/^Downloaded/)).not.toBeInTheDocument();
  });

  it.each([
    [503, "audit_unavailable", "The report could not be recorded, so it was not produced or exported. Try again in a moment."],
    [403, "forbidden", "You do not have access to this report."],
  ])("says a %s refusal (%s) in words", async (status, code, words) => {
    const user = userEvent.setup();
    server.reply = (c) => (isExport(c.path) ? refusal(status, code) : null);
    renderAt("/reports/production?from=2025-04-01&to=2025-04-30");

    await user.click(await screen.findByRole("button", { name: "Download CSV" }));

    expect(await screen.findByText(words)).toBeInTheDocument();
    expect(vi.mocked(saveFile)).not.toHaveBeenCalled();
  });

  it("makes one request however many times the button is pressed while the file is being prepared", async () => {
    const user = userEvent.setup();
    let release: (r: Response) => void = () => undefined;
    server.reply = (c) => (isExport(c.path) ? new Promise<Response>((resolve) => { release = resolve; }) : null);
    renderAt("/reports/production?from=2025-04-01&to=2025-04-30");

    await user.click(await screen.findByRole("button", { name: "Download CSV" }));
    const busy = await screen.findByRole("button", { name: "Preparing the file…" });
    expect(busy).toBeDisabled();
    await user.click(busy);
    release(csvFor("production.csv"));

    await screen.findByText("Downloaded production.csv.");
    expect(server.reports("/api/reports/production/export")).toHaveLength(1);
    expect(vi.mocked(saveFile)).toHaveBeenCalledTimes(1);
  });
});

describe("the page around the actions", () => {
  it("renders without React warning about repeated keys or anything else", async () => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => undefined);
    renderAt("/reports/production?from=2025-04-01&to=2025-04-30");
    await screen.findByRole("button", { name: "Download CSV" });
    await screen.findByRole("list", { name: "Summary" });
    expect(errors.mock.calls.map((c) => String(c[0]))).toEqual([]);
    expect(screen.queryByRole("main")).toBeNull();                      // the app shell owns the one main landmark; the hub must not nest a second
  });
});

describe("printing a report", () => {
  it("opens the browser's print dialog and asks the server for nothing", async () => {
    const user = userEvent.setup();
    const print = vi.spyOn(window, "print").mockImplementation(() => undefined);
    renderAt("/reports/production?from=2025-04-01&to=2025-04-30");
    await screen.findByRole("heading", { name: "Production and charges" });
    const before = server.calls.length;

    await user.click(await screen.findByRole("button", { name: "Print" }));

    expect(print).toHaveBeenCalledTimes(1);
    expect(server.calls.slice(before).filter((c) => isExport(c.path))).toHaveLength(0);
  });
});

describe("exporting the measurement panel", () => {
  it("exports the metrics from the page and the comparison for the baseline that was chosen", async () => {
    const user = userEvent.setup();
    server.baselines = [baselineRecord(2, { name: "Autumn" }), baselineRecord(1, { name: "Spring" })];
    server.reply = (c) => (isExport(c.path) ? csvFor("measurement.csv") : null);
    renderAt("/reports/measurement?from=2025-04-01&to=2025-04-30");

    await user.click(await screen.findByRole("button", { name: "Download metrics CSV" }));
    await screen.findByText("Downloaded measurement.csv.");
    expect(server.reports("/api/reports/measurement/metrics/export")).toHaveLength(1);

    await user.selectOptions(await screen.findByRole("combobox", { name: "Compare with" }), "b1");
    await waitFor(() => expect(server.reports("/api/reports/measurement/comparison").some((c) => c.params.get("baselineId") === "b1")).toBe(true));
    await user.click(await screen.findByRole("button", { name: "Download comparison CSV" }));

    await waitFor(() => expect(server.reports("/api/reports/measurement/comparison/export")).toHaveLength(1));
    expect(server.reports("/api/reports/measurement/comparison/export")[0].params.get("baselineId")).toBe("b1");
    expect(screen.getAllByRole("button", { name: "Print" })).toHaveLength(1);        // the page prints once; the comparison does not add a second button
  });
});

describe("accessibility of the actions", () => {
  it("has no detectable violations with an error showing", async () => {
    const user = userEvent.setup();
    server.reply = (c) => (isExport(c.path) ? refusal(413, "export_too_large") : null);
    const { container } = renderAt("/reports/production?from=2025-04-01&to=2025-04-30");
    await user.click(await screen.findByRole("button", { name: "Download CSV" }));
    await screen.findByRole("alert");
    expect(await axe(container)).toHaveNoViolations();
  });
});
