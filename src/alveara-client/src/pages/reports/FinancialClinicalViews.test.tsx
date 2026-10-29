import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { ReportsHubPage } from "./ReportsHubPage";
import { FakeReportServer, account, balancesData, ledgerProof, planItem, procedureRecord, productionData } from "./reportTestServer";

// ALV-N006 slice 6b: the financial and clinical views, against the small in-memory server.

let server: FakeReportServer;

beforeEach(() => { server = new FakeReportServer(); server.install(); });
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function renderReport(id: string, search = "from=2030-01-14&to=2030-01-16") {
  return render(
    <MemoryRouter initialEntries={[`/reports/${id}?${search}`]}>
      <AuthProvider>
        <Routes><Route path="/reports/:reportId?" element={<ReportsHubPage />} /></Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

const financial = () => { server.permissions = ["ViewFinancialReports", "ViewPatientRecords"]; };
const clinical = () => { server.permissions = ["ViewClinicalReports", "ViewPatientRecords"]; };

describe("which groups a person sees", () => {
  it("shows the financial group only to a person with the financial permission, and the clinical group only to one with the clinical permission", async () => {
    financial();
    const { unmount } = renderReport("production");
    const nav = await screen.findByRole("navigation", { name: "Reports" });
    expect(within(nav).getByRole("heading", { name: "Financial" })).toBeInTheDocument();
    for (const name of ["Production and charges", "Collections and payments", "Balances"]) expect(within(nav).getByRole("link", { name })).toBeInTheDocument();
    expect(within(nav).queryByRole("heading", { name: "Clinical" })).not.toBeInTheDocument();
    expect(within(nav).queryByRole("heading", { name: "Operational" })).not.toBeInTheDocument();
    unmount();

    clinical();
    renderReport("treatment-plans");
    const clinicalNav = await screen.findByRole("navigation", { name: "Reports" });
    expect(within(clinicalNav).getByRole("heading", { name: "Clinical" })).toBeInTheDocument();
    for (const name of ["Treatment-plan status", "Completed procedures", "Provider activity"]) expect(within(clinicalNav).getByRole("link", { name })).toBeInTheDocument();
    expect(within(clinicalNav).queryByRole("heading", { name: "Financial" })).not.toBeInTheDocument();
  });
});

describe("production and charges", () => {
  beforeEach(financial);

  it("shows the money as money, the share of each provider and code, and the proof that the ledger adds up", async () => {
    renderReport("production");
    const cards = await screen.findByRole("list", { name: "Summary" });
    expect(within(cards).getByText("$300.00")).toBeInTheDocument();
    expect(within(cards).getByText("$100.00")).toBeInTheDocument();
    expect(within(cards).getByText("$200.00")).toBeInTheDocument();
    const providers = screen.getByRole("table", { name: "Production by the provider credited with the procedure" });
    expect(within(providers).getByRole("row", { name: /Dr. One 2 \$200.00 \$100.00 \$100.00/ })).toBeInTheDocument();
    expect(screen.getByText("Dated by when each entry was recorded, not by the date of the procedure.")).toBeInTheDocument();

    const ledger = screen.getByRole("table", { name: "Ledger entries of the period by line, with their effect on what patients owe" });
    expect(within(ledger).getByRole("row", { name: "Payments received 2 -$190.00" })).toBeInTheDocument();                       // a payment takes from what is owed
    expect(within(ledger).getByRole("row", { name: "Opening balances loaded by an import 1 $75.00" })).toBeInTheDocument();       // shown apart, neither production nor collections
    expect(within(ledger).getByRole("row", { name: "All lines 10 $155.00" })).toBeInTheDocument();
    expect(screen.getByText("The lines add up to the ledger: $155.00 across 10 entries, checked against an independent sum of the same entries.")).toBeInTheDocument();
  });

  it("opens the charges of one provider with the provider and the Charges line, and the entries as money", async () => {
    const user = userEvent.setup();
    renderReport("production");
    const providers = await screen.findByRole("table", { name: "Production by the provider credited with the procedure" });
    await user.click(within(providers).getByRole("button", { name: "2, show the records: Dr. One, charges" }));

    const records = await screen.findByRole("region", { name: "Records: Dr. One: charges" });
    expect(await within(records).findByText("2 records; showing 2.")).toBeInTheDocument();
    const call = server.reports("/api/reports/production/entries")[0];
    expect([call.params.get("providerId"), call.params.get("line")]).toEqual(["pa", "Charges"]);
    expect(within(records).getAllByText("$100.00")).toHaveLength(2);
    expect(within(records).getAllByRole("link", { name: "Open patient record" })).toHaveLength(2);
  });

  it("asks for the charges that no completed procedure can be traced to with the server's 'not attributed' provider", async () => {
    const user = userEvent.setup();
    server.production = productionData({ providers: [{ providerProfileId: null, providerName: "Not attributed", charges: 3, gross: 300, reversed: 100, net: 200 }] });
    renderReport("production");
    const providers = await screen.findByRole("table", { name: "Production by the provider credited with the procedure" });
    await user.click(within(providers).getByRole("button", { name: "3, show the records: Not attributed, charges" }));

    expect(server.reports("/api/reports/production/entries")[0].params.get("providerId")).toBe("00000000-0000-0000-0000-000000000000");
  });

  it("lets only the lines of the report open their entries and leaves the others as plain numbers", async () => {
    const user = userEvent.setup();
    renderReport("production");
    const ledger = await screen.findByRole("table", { name: "Ledger entries of the period by line, with their effect on what patients owe" });
    expect(within(ledger).queryByRole("button", { name: /Payments received/ })).not.toBeInTheDocument();
    expect(within(ledger).queryByRole("button", { name: /Opening balances/ })).not.toBeInTheDocument();
    expect(within(ledger).queryByRole("button", { name: /Adjustments/ })).not.toBeInTheDocument();
    await user.click(within(ledger).getByRole("button", { name: "1, show the records: Charges reversed" }));

    expect(server.reports("/api/reports/production/entries")[0].params.get("line")).toBe("ChargeReversals");
  });

  it("warns, and does not let the numbers pass as right, when the lines do not agree with the ledger", async () => {
    server.production = productionData({ ledger: ledgerProof({ ledgerTotal: 160, ledgerEntries: 11, reconciled: false }) });
    renderReport("production");
    expect(await screen.findByRole("alert")).toHaveTextContent("The lines do not agree with the ledger ($155.00 in 10 entries against $160.00 in 11). Do not rely on these numbers; tell your administrator.");
  });

  it("says when only the chosen providers' production is covered", async () => {
    server.production = productionData({ ledger: ledgerProof({ scope: "ProviderProduction" }) });
    renderReport("production", "from=2030-01-14&to=2030-01-16&providerIds=pa");
    expect(await screen.findByText("Only the production of the providers chosen is covered here, so only the charge lines are shown.")).toBeInTheDocument();
    expect(server.reports("/api/reports/production")[0].params.getAll("providerIds")).toEqual(["pa"]);
  });
});

describe("collections and payments", () => {
  beforeEach(financial);

  it("shows money received less reversals and refunds by method, and never offers a provider", async () => {
    renderReport("collections", "from=2030-01-14&to=2030-01-16&providerIds=pa");
    const cards = await screen.findByRole("list", { name: "Summary" });
    for (const amount of ["$190.00", "$40.00", "$20.00", "$130.00"]) expect(within(cards).getByText(amount)).toBeInTheDocument();
    const methods = screen.getByRole("table", { name: "Collections by payment method" });
    expect(within(methods).getByRole("row", { name: /Cash 1 \$150.00 \$0.00 \$20.00 \$130.00/ })).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Dr. Rivera" })).not.toBeInTheDocument();
    expect(server.reports("/api/reports/collections")[0].params.getAll("providerIds")).toEqual([]);            // a chosen provider is not sent: the server would refuse it
    expect(screen.getByText(/Collections cannot be narrowed by provider or location/)).toBeInTheDocument();
  });

  it("opens the payments of one method, and lets only the four collection lines open entries", async () => {
    const user = userEvent.setup();
    renderReport("collections");
    const methods = await screen.findByRole("table", { name: "Collections by payment method" });
    await user.click(within(methods).getByRole("button", { name: "1, show the records: Cash, payments" }));
    const first = server.reports("/api/reports/collections/entries")[0];
    expect([first.params.get("method"), first.params.get("line")]).toEqual(["Cash", "Payments"]);

    const ledger = screen.getByRole("table", { name: "Ledger entries of the period by line, with their effect on what patients owe" });
    expect(within(ledger).queryByRole("button", { name: /Charges/ })).not.toBeInTheDocument();
    await user.click(within(ledger).getByRole("button", { name: "1, show the records: Refunds" }));
    expect(server.reports("/api/reports/collections/entries")[1].params.get("line")).toBe("Refunds");
  });
});

describe("balances", () => {
  beforeEach(financial);

  it("shows what was owed at the start and at the end and the change, and says the change equals the ledger", async () => {
    renderReport("balances");
    const table = await screen.findByRole("table", { name: "Balances at the start and at the end of the period" });
    expect(within(table).getByRole("row", { name: "Total owed $160.00 $330.00" })).toBeInTheDocument();
    expect(within(table).getByRole("row", { name: "Accounts with any entry 2 3" })).toBeInTheDocument();
    expect(screen.getByText("The change ($170.00) equals an independent sum of the 4 entries recorded in the period ($170.00).")).toBeInTheDocument();
    expect(screen.getByText(/No account can be in credit while the ledger refuses a negative balance/)).toBeInTheDocument();
  });

  it("lists the accounts that owe at the end, largest first, as money, and shows more without repeating", async () => {
    const user = userEvent.setup();
    server.accounts = [account(1, 200), account(2, 130), account(3, 10)];
    renderReport("balances");
    const table = await screen.findByRole("table", { name: "Balances at the start and at the end of the period" });
    expect(within(table).queryByRole("button", { name: /at the start/ })).not.toBeInTheDocument();             // only the end opens a list
    await user.click(within(table).getByRole("button", { name: "2, show the records: accounts that owe at the end" }));

    const records = await screen.findByRole("region", { name: "Records: Accounts that owe at the end" });
    expect(await within(records).findByText("3 records; showing 2.")).toBeInTheDocument();
    expect(within(records).getByText("$200.00")).toBeInTheDocument();
    await user.click(within(records).getByRole("button", { name: "Show more" }));
    expect(await within(records).findByText("$10.00")).toBeInTheDocument();
    const calls = server.reports("/api/reports/balances/accounts");
    expect([calls[0].params.get("side"), calls[1].params.get("afterAccountId")]).toEqual(["Owing", "acc2"]);
  });

  it("warns when the change does not equal the entries recorded", async () => {
    server.balances = balancesData({ ledgerChange: 171, reconciled: false });
    renderReport("balances");
    expect(await screen.findByRole("alert")).toHaveTextContent("The change ($170.00) does not equal the entries recorded in the period ($171.00). Do not rely on these numbers; tell your administrator.");
  });

  it("says there is no data when no account holds an entry", async () => {
    server.noData = true;
    renderReport("balances");
    expect(await screen.findByText("No account holds any ledger entry yet.")).toBeInTheDocument();
  });
});

describe("treatment-plan status", () => {
  beforeEach(clinical);

  it("shows where every proposed procedure stands, with the cuts of the accepted ones below it, and the fees as estimates", async () => {
    renderReport("treatment-plans");
    const table = await screen.findByRole("table", { name: "Proposed procedures by where they stand at the end of the period" });
    expect(within(table).getAllByRole("rowheader").map((h) => h.textContent)).toEqual([
      "Waiting for a decision", "Accepted", "Declined", "Withdrawn", "of which: accepted and scheduled", "of which: accepted, not scheduled", "of which: accepted and done",
    ]);
    expect(within(table).getByRole("row", { name: /Accepted 4 \$400.00/ })).toBeInTheDocument();
    expect(screen.getByText("The groups add up to the procedures proposed.")).toBeInTheDocument();
    expect(screen.getByText("The fees are the practice's estimates copied into each plan. They are not amounts billed.")).toBeInTheDocument();
  });

  it("opens the items of a cell with the cell's own name and shows the withdrawn ones as withdrawn", async () => {
    const user = userEvent.setup();
    server.planItems = [planItem(1, { withdrawn: true, acceptance: "Pending" })];
    renderReport("treatment-plans");
    const table = await screen.findByRole("table", { name: "Proposed procedures by where they stand at the end of the period" });
    await user.click(within(table).getByRole("button", { name: "1, show the records: Withdrawn" }));

    const records = await screen.findByRole("region", { name: "Records: Withdrawn" });
    expect(await within(records).findByText("Withdrawn", { selector: "td" })).toBeInTheDocument();
    expect(server.reports("/api/reports/treatment-plans/items")[0].params.get("cell")).toBe("Withdrawn");
  });
});

describe("completed procedures", () => {
  beforeEach(clinical);

  it("shows the days, the providers and the codes, each adding up, and sends the chosen provider", async () => {
    renderReport("completed-procedures", "from=2030-01-14&to=2030-01-16&providerIds=pb");
    const providers = await screen.findByRole("table", { name: "Procedures by the provider credited" });
    expect(within(providers).getByRole("row", { name: "Dr. One 1 $100.00" })).toBeInTheDocument();
    expect(screen.getByText("The days, the providers and the codes each add up to the procedures performed.")).toBeInTheDocument();
    expect(server.reports("/api/reports/completed-procedures")[0].params.getAll("providerIds")).toEqual(["pb"]);
  });

  it("opens the procedures of a day, of a provider and of a code with those criteria", async () => {
    const user = userEvent.setup();
    server.procedureRecords = [procedureRecord(1), procedureRecord(2)];
    renderReport("completed-procedures");
    await user.click(await screen.findByRole("button", { name: "2, show the records: 2030-01-15" }));
    await screen.findByText("2 records; showing 2.");
    await user.click(screen.getByRole("button", { name: "1, show the records: Dr. One" }));
    await user.click(screen.getByRole("button", { name: "1, show the records: LOCAL LOCAL-501" }));

    const calls = server.reports("/api/reports/completed-procedures/procedures");
    await waitFor(() => expect(calls.length).toBeGreaterThanOrEqual(3));
    expect(calls[0].params.get("date")).toBe("2030-01-15");
    expect(calls[1].params.get("providerId")).toBe("pa");
    expect([calls[2].params.get("codeSystem"), calls[2].params.get("code")]).toEqual(["LOCAL", "LOCAL-501"]);
  });
});

describe("provider activity", () => {
  beforeEach(clinical);

  it("shows the three different measures for each provider and an all-providers row, and explains whose visit counts", async () => {
    renderReport("provider-activity");
    const table = await screen.findByRole("table", { name: "Activity of each provider" });
    expect(within(table).getAllByRole("rowheader").map((h) => h.textContent)).toEqual(["Dr. Patel", "Dr. Rivera", "All providers"]);
    expect(screen.getByText("Every column adds up to its total.")).toBeInTheDocument();
    expect(screen.getByText("A visit finished is credited to the provider who saw the patient, which may not be the one it was booked with.")).toBeInTheDocument();
  });

  it("opens the appointments of a measure and provider, and the procedures of a provider, each from its own route", async () => {
    const user = userEvent.setup();
    renderReport("provider-activity");
    const table = await screen.findByRole("table", { name: "Activity of each provider" });
    await user.click(within(table).getByRole("button", { name: "1, show the records: Dr. Patel, visits finished" }));
    const visits = server.reports("/api/reports/provider-activity/appointments")[0];
    expect([visits.params.get("measure"), visits.params.get("providerId")]).toEqual(["VisitsCompleted", "pb"]);

    await user.click(within(table).getByRole("button", { name: "2, show the records: Dr. Rivera, procedures done" }));
    const procedures = server.reports("/api/reports/provider-activity/procedures")[0];
    expect(procedures.params.get("providerId")).toBe("pa");

    await user.click(within(table).getByRole("button", { name: "5, show the records: All providers, booked" }));
    const booked = server.reports("/api/reports/provider-activity/appointments")[1];
    expect([booked.params.get("measure"), booked.params.has("providerId")]).toEqual(["Booked", false]);
  });
});

describe("accessibility of the financial and clinical views", () => {
  it.each(["production", "collections", "balances"])("has no detectable violations for %s", async (id) => {
    financial();
    const { container } = renderReport(id);
    await screen.findAllByRole("table");
    expect(await axe(container)).toHaveNoViolations();
  });

  it.each(["treatment-plans", "completed-procedures", "provider-activity"])("has no detectable violations for %s", async (id) => {
    clinical();
    const { container } = renderReport(id);
    await screen.findAllByRole("table");
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("a person without the permission", () => {
  it("is told a financial report is not available instead of being shown it", async () => {
    clinical();
    renderReport("production");
    expect(await screen.findByText("That report is not available to you")).toBeInTheDocument();
    expect(server.reports("/api/reports/production")).toHaveLength(0);
  });
});

