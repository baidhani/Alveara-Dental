import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { ReportsHubPage } from "./ReportsHubPage";
import { FakeReportServer, baselineRecord, comparisonData, refusal } from "./reportTestServer";

// ALV-N006 slice 6c: the measurement panel (metrics, baselines, comparison), against the small in-memory server.

let server: FakeReportServer;

beforeEach(() => { server = new FakeReportServer(); server.permissions = ["ViewMeasurementReports", "ManageMeasurementBaselines"]; server.install(); });
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function renderPanel() {
  return render(
    <MemoryRouter initialEntries={["/reports/measurement?from=2025-04-01&to=2025-04-30"]}>
      <AuthProvider>
        <Routes><Route path="/reports/:reportId?" element={<ReportsHubPage />} /></Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe("the measured metrics", () => {
  it("shows what was counted out of what for each metric, with the definition version, and Not available when nothing was counted", async () => {
    renderPanel();
    const table = await screen.findByRole("table", { name: "Measured metrics for the period" });
    expect(within(table).getByRole("row", { name: "Scheduling conflicts 4 12 33.3% Version 1" })).toBeInTheDocument();
    expect(within(table).getByRole("row", { name: "No-shows 0 0 Not available Version 1" })).toBeInTheDocument();
    expect(within(table).queryByText("0.0%")).not.toBeInTheDocument();
    expect(screen.getByText(/Follow-up and note metrics are judged at/)).toBeInTheDocument();
  });

  it("explains how each metric is defined and links to the records of a metric only for a person who may see that report", async () => {
    renderPanel();
    const details = (await screen.findByText("How each metric is defined")).closest("details")!;
    expect(within(details).getByText(/things counted for Scheduling conflicts\. Out of: all things for Scheduling conflicts\. Source: the records\./)).toBeInTheDocument();
    expect(within(details).queryByRole("link")).not.toBeInTheDocument();                    // this person may not see the operational reports
  });

  it("offers the link to the records for a person who may see the operational reports", async () => {
    server.permissions = ["ViewMeasurementReports", "ViewOperationalReports"];
    renderPanel();
    const details = (await screen.findByText("How each metric is defined")).closest("details")!;
    const links = within(details).getAllByRole("link", { name: /See the records in the/ });
    expect(links.map((l) => l.textContent)).toEqual(["See the records in the Recall and follow-up report", "See the records in the Cancellations and no-shows report", "See the records in the Cancellations and no-shows report"]);
    expect(links[0]).toHaveAttribute("href", "/reports/follow-ups?from=2025-04-01&to=2025-04-30");
  });

  it("says nothing was counted but keeps the baselines and the comparison on the page", async () => {
    server.noData = true;
    renderPanel();
    expect(await screen.findByText("Nothing was counted in this period, so no metric has a value. A metric with nothing counted shows Not available, never zero.")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Baselines" })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Comparison with a baseline" })).toBeInTheDocument();
  });
});

describe("the baselines", () => {
  it("says no baseline has been set, and lists baselines newest first with the period and what was stored", async () => {
    renderPanel();
    expect(await screen.findByText("No baseline has been set")).toBeInTheDocument();
    server.baselines = [baselineRecord(2, { name: "Autumn" }), baselineRecord(1, { name: "Spring" })];
    const { unmount } = renderPanel();
    const table = (await screen.findAllByRole("table", { name: "Baselines, newest first" }))[0];
    expect(within(table).getAllByRole("rowheader").map((h) => h.textContent)).toEqual(["Autumn", "Spring"]);
    expect(within(table).getAllByText("2025-03-01 to 2025-03-31")).toHaveLength(2);
    unmount();
  });

  it("shows no form to a person who may not set a baseline", async () => {
    server.permissions = ["ViewMeasurementReports"];
    renderPanel();
    await screen.findByRole("heading", { name: "Baselines" });
    expect(screen.queryByRole("form", { name: "Set a baseline" })).not.toBeInTheDocument();
  });
});

describe("the comparison", () => {
  it("says there is no baseline and shows no change when none has been set", async () => {
    server.comparison = comparisonData({
      baseline: null,
      comparisons: [{ key: "scheduling_conflicts", name: "Scheduling conflicts", lowerIsBetter: true, status: "no_baseline", message: "There is no baseline for this metric, so there is nothing to compare with.", baseline: null, current: { definitionVersion: 1, numeratorCount: 4, denominatorCount: 12, rate: 0.3333 }, absoluteChange: null, relativeChange: null, movement: null }],
    });
    renderPanel();
    expect(await screen.findByText("There is no baseline yet, so nothing can be compared and no change is shown.")).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Each metric in this period against the baseline" });
    expect(within(table).getByRole("row", { name: /Scheduling conflicts None 33.3% \(4 of 12\) No change shown There is no baseline/ })).toBeInTheDocument();
  });

  it("states a valid change as lower or higher with the points and the share, and never calls it an improvement", async () => {
    server.baselines = [baselineRecord(1, { name: "Spring" })];
    renderPanel();
    const table = await screen.findByRole("table", { name: "Each metric in this period against the baseline" });
    expect(screen.getByText(/Compared with/)).toHaveTextContent("Compared with Spring 1 (2025-03-01 to 2025-03-31). A metric is compared only when that period ended before this one started, the metric is defined the same way, and both periods have at least 30 records.");
    expect(within(table).getByRole("row", { name: /Scheduling conflicts 5.0% \(2 of 40\) 10.0% \(4 of 40\) Higher by 5.0 percentage points \(100% of the baseline rate\)/ })).toBeInTheDocument();
    expect(within(table).getByRole("row", { name: /Follow-ups not done on time 25.0% \(1 of 4\) 75.0% \(3 of 4\) Higher by 50.0 percentage points \(200% of the baseline rate\)/ })).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/improv|better|worse/i);
    expect(screen.getByText("A difference between two periods shows that the numbers differ. It does not show what caused the difference.")).toBeInTheDocument();
  });

  it("shows no change and gives the reason for a metric that cannot honestly be compared", async () => {
    renderPanel();
    const table = await screen.findByRole("table", { name: "Each metric in this period against the baseline" });
    const small = within(table).getByRole("row", { name: /Billing corrections/ });
    expect(small).toHaveTextContent("No change shown");
    expect(small).toHaveTextContent("The baseline has too few records to compare with.");
    const changed = within(table).getByRole("row", { name: /Duplicate patient merges/ });
    expect(changed).toHaveTextContent("The metric is defined differently now than when the baseline was taken, so the two numbers do not measure the same thing.");
    expect(changed).not.toHaveTextContent(/Higher|Lower/);
  });

  it("lists what cannot be measured yet, with the reason, and presents the overhead reduction as a target", async () => {
    renderPanel();
    const heading = await screen.findByRole("heading", { name: "What cannot be measured yet" });
    const list = heading.nextElementSibling as HTMLElement;
    expect(within(list).getAllByRole("listitem")).toHaveLength(3);
    expect(within(list).getByText(/This is a product target \(a 20 to 30 percent reduction\), not a measurement\./)).toBeInTheDocument();
  });

  it("compares with the baseline that was chosen, not only the latest", async () => {
    const user = userEvent.setup();
    server.baselines = [baselineRecord(2, { name: "Autumn" }), baselineRecord(1, { name: "Spring" })];
    renderPanel();
    await user.selectOptions(await screen.findByRole("combobox", { name: "Compare with" }), "b1");

    await waitFor(() => expect(server.reports("/api/reports/measurement/comparison").some((c) => c.params.get("baselineId") === "b1")).toBe(true));
    expect(await screen.findByText(/Compared with/)).toHaveTextContent("Compared with Spring");
  });
});

describe("setting a baseline", () => {
  const fill = async (user: ReturnType<typeof userEvent.setup>, name = "Spring 2025", from = "2025-03-01", to = "2025-03-31") => {
    const form = await screen.findByRole("form", { name: "Set a baseline" });
    await user.type(within(form).getByLabelText("Name"), name);
    await user.type(within(form).getByLabelText("First day"), from);
    await user.type(within(form).getByLabelText("Last day"), to);
    return form;
  };

  it("saves the baseline with a key and the token, says so, clears the form and refreshes the list and the comparison", async () => {
    const user = userEvent.setup();
    renderPanel();
    const form = await fill(user);
    await user.click(within(form).getByRole("button", { name: "Save the baseline" }));

    expect(await screen.findByText('Baseline "Spring 2025" saved for 2025-03-01 to 2025-03-31.')).toBeInTheDocument();
    const post = server.posts("/api/reports/measurement/baselines")[0];
    expect(post.body).toEqual({ name: "Spring 2025", from: "2025-03-01", to: "2025-03-31" });
    expect(post.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
    expect(post.headers.get("X-CSRF-Token")).toBe("t");
    expect(within(form).getByLabelText("Name")).toHaveValue("");
    expect(await screen.findByRole("table", { name: "Baselines, newest first" })).toBeInTheDocument();
    await waitFor(() => expect(server.reports("/api/reports/measurement/baselines").length).toBeGreaterThanOrEqual(2));
  });

  it("sends the SAME key when the unchanged baseline is saved again after a failure, and a new key once anything is changed", async () => {
    const user = userEvent.setup();
    let fail = true;
    server.reply = (c) => (fail && c.method === "POST" && c.path === "/api/reports/measurement/baselines" ? refusal(503, "save_failed") : null);
    renderPanel();
    const form = await fill(user);
    await user.click(within(form).getByRole("button", { name: "Save the baseline" }));
    expect(await screen.findByText("The baseline could not be saved. Nothing was stored. Try again.")).toBeInTheDocument();

    fail = false;
    await user.click(within(form).getByRole("button", { name: "Save the baseline" }));
    await screen.findByText(/saved for 2025-03-01 to 2025-03-31/);
    const [first, second] = server.posts("/api/reports/measurement/baselines");
    expect(second.headers.get("Idempotency-Key")).toBe(first.headers.get("Idempotency-Key"));

    await user.type(within(form).getByLabelText("Name"), "Another");
    await user.type(within(form).getByLabelText("First day"), "2025-02-01");
    await user.type(within(form).getByLabelText("Last day"), "2025-02-28");
    await user.click(within(form).getByRole("button", { name: "Save the baseline" }));
    await waitFor(() => expect(server.posts("/api/reports/measurement/baselines")).toHaveLength(3));
    expect(server.posts("/api/reports/measurement/baselines")[2].headers.get("Idempotency-Key")).not.toBe(first.headers.get("Idempotency-Key"));
  });

  it("refuses, before asking the server, a period that is not over, an empty name and an inverted range", async () => {
    const user = userEvent.setup();
    renderPanel();
    const form = await fill(user, "Now", "2025-03-01", "2999-12-31");
    expect(within(form).getByRole("alert")).toHaveTextContent("A baseline must be a period that is already over. Choose days before today.");
    expect(within(form).getByRole("button", { name: "Save the baseline" })).toBeDisabled();

    await user.clear(within(form).getByLabelText("Last day"));
    await user.type(within(form).getByLabelText("Last day"), "2025-02-01");
    expect(within(form).getByRole("alert")).toHaveTextContent("The first day must not be after the last day.");
    await user.clear(within(form).getByLabelText("Name"));
    expect(within(form).getByRole("alert")).toHaveTextContent("Give the baseline a name.");
    expect(server.posts("/api/reports/measurement/baselines")).toHaveLength(0);
  });

  it("says a refusal from the server in words", async () => {
    const user = userEvent.setup();
    server.reply = (c) => (c.method === "POST" ? refusal(400, "period_not_complete") : null);
    renderPanel();
    const form = await fill(user);
    await user.click(within(form).getByRole("button", { name: "Save the baseline" }));
    expect(await within(form).findByText("A baseline must be a period that is already over. Choose days before today.")).toBeInTheDocument();
  });
});

describe("accessibility of the measurement panel", () => {
  it("has no detectable violations with baselines, the form and a comparison on the page", async () => {
    server.baselines = [baselineRecord(1)];
    const { container } = renderPanel();
    await screen.findByRole("table", { name: "Each metric in this period against the baseline" });
    await screen.findByRole("form", { name: "Set a baseline" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
