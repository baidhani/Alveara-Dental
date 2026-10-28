import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { ImportBatchPage } from "./ImportBatchPage";
import { FakeImportServer, dryRunBatch, json, makeBatch, makeRow, reconciliation, refusal, suggestion } from "./importTestServer";
import { POLL_LIMIT_MS, POLL_MS } from "./useImportBatch";

// ALV-018-C02 slice 7: one batch as steps (file, mapping, dry-run, likely duplicates, commit, what happened), against a small in-memory server that answers like the real API.

let server: FakeImportServer;

beforeEach(() => { server = new FakeImportServer(); server.install(); });
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/admin/data-import/b1"]}>
      <AuthProvider>
        <Routes><Route path="/admin/data-import/:batchId" element={<ImportBatchPage />} /></Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

const commitButton = (name: RegExp | string) => screen.findByRole("button", { name });

describe("the file and the mapping", () => {
  it("shows what was sent and the column mapping prefilled from the suggestion, with the dry-run waiting for a saved mapping", async () => {
    renderPage();

    expect(await screen.findByRole("heading", { name: "Patients: Old system" })).toBeInTheDocument();
    expect(screen.getByText("Needs its columns mapped")).toBeInTheDocument();
    expect(screen.getByText(/10 rows, 3 columns, 2\.0 KB/)).toBeInTheDocument();
    expect(await screen.findByLabelText("Field for column First Name")).toHaveValue("firstName");
    expect(screen.getByLabelText("Field for column DOB")).toHaveValue("dateOfBirth");
    expect(screen.getByRole("button", { name: "Run the dry-run" })).toBeDisabled();
    expect(screen.getByText("Save the column mapping first.")).toBeInTheDocument();
  });

  it("saves the mapping (an ignored column as null), says so, and then offers the dry-run", async () => {
    renderPage();
    const user = userEvent.setup();

    await user.selectOptions(await screen.findByLabelText("Field for column DOB"), "");
    expect(screen.getByText(/Not filled yet and required: Date of birth/)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Field for column DOB"), "dateOfBirth");
    await user.click(screen.getByRole("button", { name: "Save the mapping" }));

    expect(await screen.findByText("Mapping saved.")).toBeInTheDocument();
    const put = server.of("PUT", /\/mapping$/)[0];
    expect(server.bodyOf(put)).toEqual({ mapping: { "First Name": "firstName", "Last Name": "lastName", DOB: "dateOfBirth" } });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run the dry-run" })).toBeEnabled());
  });

  it("lists the problems of a mapping the server refuses, and saves nothing", async () => {
    server.reply = (c) => (c.method === "PUT" ? json({ error: "mapping_invalid", message: "no", problems: [{ code: "required_field_unmapped", row: null, column: null, message: "Date of birth is required but no column fills it." }] }, 400) : null);
    renderPage();

    await userEvent.setup().click(await screen.findByRole("button", { name: "Save the mapping" }));

    expect(await screen.findByText("The mapping was not saved. Correct the problems listed and save it again.")).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Problems with the mapping" })).getByText("Date of birth is required but no column fills it.")).toBeInTheDocument();
    expect(screen.queryByText("Mapping saved.")).not.toBeInTheDocument();
  });
});

describe("the dry-run", () => {
  it("runs, says it writes nothing, and reports the counts and the row results", async () => {
    server.current = makeBatch({ mappingConfirmed: true });
    server.rows = [makeRow({ rowNumber: 2 }), makeRow({ rowNumber: 3, status: "Rejected", reason: "invalid_fields", errors: { firstName: "First name is required." } }), makeRow({ rowNumber: 4, status: "Skipped", reason: "already_registered", matchedPatientIds: ["p1"] })];
    renderPage();
    const user = userEvent.setup();

    expect(await screen.findByText(/It writes nothing/)).toBeInTheDocument();
    await user.click(await screen.findByRole("button", { name: "Run the dry-run" }));

    expect(await screen.findByText((_, el) => el?.tagName === "P" && /10 rows: 6 valid, 2 rejected, 1 skipped, 1 waiting for a decision\./.test(el.textContent ?? ""))).toBeInTheDocument();
    const table = await screen.findByRole("table", { name: "Row results" });
    expect(within(table).getByText("firstName: First name is required.")).toBeInTheDocument();
    expect(within(table).getByText("Already registered")).toBeInTheDocument();
    expect(within(table).getByText("Matches 1 registered patient.")).toBeInTheDocument();
    expect(server.of("POST", /\/dry-run$/)).toHaveLength(1);
  });

  it("filters the row results by status", async () => {
    server.current = dryRunBatch();
    server.rows = [makeRow({ rowNumber: 2 }), makeRow({ rowNumber: 3, status: "Rejected", reason: "invalid_fields", errors: { firstName: "First name is required." } })];
    renderPage();

    await userEvent.setup().selectOptions(await screen.findByLabelText("Show"), "Rejected");

    await waitFor(() => expect(server.calls.some((c) => c.url.includes("/rows?status=Rejected"))).toBe(true));
    await waitFor(() => expect(within(screen.getByRole("table", { name: "Row results" })).queryAllByRole("row")).toHaveLength(2));   // header + the one rejected row
  });

  it("shows the total of the valid balances to check against the old system", async () => {
    server.current = dryRunBatch({ sourceKind: "OpeningBalances", validAmount: 165.5, rejectedRows: 0, skippedRows: 0, unresolvedRows: 0 });
    renderPage();

    const step = within(await screen.findByRole("region", { name: "Step 3: dry-run" }));
    expect(step.getByText(/\$165\.50/, { selector: "strong" })).toBeInTheDocument();
    expect(step.getByText(/Check this against the old system before committing/)).toBeInTheDocument();
  });

  it("downloads the error report with the server's file name", async () => {
    server.current = dryRunBatch();
    renderPage();

    await userEvent.setup().click(await screen.findByRole("button", { name: "Download the error report (CSV)" }));

    await waitFor(() => expect(server.downloads).toEqual(["import-b1-errors.csv"]));
  });
});

describe("likely duplicates", () => {
  const waiting = () => makeRow({ rowNumber: 5, status: "Unresolved", reason: "possible_duplicate", matchedPatientIds: ["p1", "p2"] });

  it("lists each row with links to the matching records and records a decision, then the row leaves the queue", async () => {
    server.current = dryRunBatch();
    server.rows = [waiting()];
    renderPage();
    const user = userEvent.setup();

    const link = await screen.findByRole("link", { name: "Open registered patient 1 (new tab)" });
    expect(link).toHaveAttribute("href", "/patients/p1");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    await user.click(screen.getByRole("button", { name: "This is a different person: register them" }));

    expect(await screen.findByText("No rows are waiting for a decision.")).toBeInTheDocument();
    expect(screen.getByText(/Row 5: recorded as a different person, it will be registered\./)).toBeInTheDocument();
    expect(server.bodyOf(server.of("POST", /\/decision$/)[0])).toEqual({ decision: "RegisterAsNew" });
  });

  it("records the same person as a skip", async () => {
    server.current = dryRunBatch();
    server.rows = [waiting()];
    renderPage();

    await userEvent.setup().click(await screen.findByRole("button", { name: "This is the same person: skip the row" }));

    await waitFor(() => expect(server.bodyOf(server.of("POST", /\/decision$/)[0])).toEqual({ decision: "Skip" }));
  });

  it("is read-only for someone who cannot open patient records, and says why", async () => {
    server.permissions = ["BulkImportData"];
    server.current = dryRunBatch();
    server.rows = [waiting()];
    renderPage();

    expect(await screen.findByText(/You cannot open patient records/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Open registered patient/ })).not.toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "This is the same person: skip the row" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "This is a different person: register them" })).toBeDisabled();
  });

  it("is not offered for appointment or balance files", async () => {
    server.current = dryRunBatch({ sourceKind: "Appointments" });
    renderPage();

    await screen.findByRole("heading", { name: "Step 4: commit" });
    expect(screen.queryByRole("heading", { name: "Likely duplicates" })).not.toBeInTheDocument();
  });
});

describe("the commit", () => {
  it("offers all-or-nothing only when every row is valid or skipped, and defaults to it then", async () => {
    server.current = dryRunBatch({ rejectedRows: 0, unresolvedRows: 0 });
    renderPage();

    expect(await screen.findByRole("radio", { name: /All or nothing/ })).toBeChecked();
    expect(screen.getByRole("button", { name: "Commit 9 rows" })).toBeDisabled();
  });

  it("explains why all-or-nothing is blocked when rows are rejected or waiting, and defaults to the valid rows", async () => {
    server.current = dryRunBatch();
    renderPage();

    expect(await screen.findByRole("radio", { name: /Commit the valid rows only/ })).toBeChecked();
    expect(screen.getByRole("radio", { name: /All or nothing/ })).toBeDisabled();
    expect(screen.getByText(/Every row must be valid or skipped/)).toBeInTheDocument();
  });

  it("offers only the valid rows for appointments and balances, and says why", async () => {
    server.current = dryRunBatch({ sourceKind: "Appointments", rejectedRows: 0, unresolvedRows: 0 });
    renderPage();

    expect(await screen.findByRole("radio", { name: /All or nothing/ })).toBeDisabled();
    expect(screen.getByText(/Appointments are recorded one at a time, each in its own transaction/)).toBeInTheDocument();
  });

  it("needs the confirmation, then sends the policy and the number of valid rows it showed, and follows the commit", async () => {
    server.current = dryRunBatch();
    renderPage();
    const user = userEvent.setup();

    const button = await commitButton("Commit 6 rows");
    expect(button).toBeDisabled();
    expect(screen.getByText(/This will write/)).toHaveTextContent("This will write 6 rows to the patient records. It cannot be undone as a batch.");
    await user.click(screen.getByRole("checkbox", { name: /I have checked the results/ }));
    await user.click(button);

    expect(await screen.findByText(/The commit is running/)).toBeInTheDocument();
    expect(server.bodyOf(server.of("POST", /\/commit$/)[0])).toEqual({ policy: "CommitValidRows", expectedValidRows: 6 });
  });

  it("reloads the batch and explains when the results changed since they were looked at, committing nothing", async () => {
    server.current = dryRunBatch();
    server.reply = (c) => {
      if (c.method !== "POST" || !c.url.endsWith("/commit")) return null;
      server.current = dryRunBatch({ rejectedRows: 3 });                               // someone changed the results meanwhile
      return refusal(409, "results_changed");
    };
    renderPage();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("checkbox", { name: /I have checked the results/ }));
    await user.click(screen.getByRole("button", { name: "Commit 6 rows" }));

    expect(await screen.findByText(/The results changed since you looked at them/)).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Commit 5 rows" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: /I have checked the results/ })).not.toBeChecked();
  });

  it("will not commit opening balances for someone without Manage billing, and shows the total that would be written", async () => {
    server.permissions = ["BulkImportData", "ViewPatientRecords"];
    server.current = dryRunBatch({ sourceKind: "OpeningBalances", validAmount: 165.5, rejectedRows: 0, skippedRows: 0, unresolvedRows: 0 });
    renderPage();

    expect(await screen.findByText(/a total of/)).toHaveTextContent("a total of $165.50");
    expect(screen.getAllByText(/also needs the Manage billing permission/).length).toBeGreaterThan(0);
    expect(screen.getByRole("checkbox", { name: /I have checked the results and the total/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Commit 10 rows" })).toBeDisabled();
  });

  it("says there is nothing to commit when no row is valid", async () => {
    server.current = dryRunBatch({ rejectedRows: 10, skippedRows: 0, unresolvedRows: 0 });
    renderPage();

    expect(await screen.findByText("No row is valid, so there is nothing to commit.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Commit 0 rows" })).toBeDisabled();
  });
});

describe("while it commits", () => {
  const committing = (over = {}) => makeBatch({ status: "Committing", mappingConfirmed: true, dryRunAtUtc: "2026-10-28T12:05:00+00:00", commitRound: 1, rowCount: 10, attemptedRows: 10, ...over });

  it("follows the batch until it has committed, shows what happened, and then stops asking", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval", "Date"] });
    server.current = committing();
    renderPage();
    expect(await screen.findByText(/The commit is running/)).toBeInTheDocument();

    server.current = makeBatch({ status: "Committed", commitOutcome: "CompletedWithExceptions", acceptedRows: 6, rejectedRows: 2, skippedRows: 1, unresolvedRows: 1, mappingConfirmed: true, dryRunAtUtc: "2026-10-28T12:05:00+00:00" });
    await act(async () => { await vi.advanceTimersByTimeAsync(POLL_MS); });

    expect(await screen.findByText("Reconciled")).toBeInTheDocument();
    const asked = server.of("GET", /\/api\/import-batches\/b1$/).length;
    await act(async () => { await vi.advanceTimersByTimeAsync(POLL_MS * 5); });
    expect(server.of("GET", /\/api\/import-batches\/b1$/)).toHaveLength(asked);
  });

  it("stops asking after ten minutes and offers to check again", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval", "Date"] });
    server.current = committing();
    renderPage();
    expect(await screen.findByText(/The commit is running/)).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(POLL_LIMIT_MS + POLL_MS); });

    expect(await screen.findByText("It is still running after ten minutes.")).toBeInTheDocument();
    const asked = server.of("GET", /\/api\/import-batches\/b1$/).length;
    await act(async () => { await vi.advanceTimersByTimeAsync(POLL_MS * 5); });
    expect(server.of("GET", /\/api\/import-batches\/b1$/)).toHaveLength(asked);
    await act(async () => { screen.getByRole("button", { name: "Check again" }).click(); });
    expect(server.of("GET", /\/api\/import-batches\/b1$/).length).toBeGreaterThan(asked);
  });

  it("stops asking when the person leaves the page", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval", "Date"] });
    server.current = committing();
    const { unmount } = renderPage();
    expect(await screen.findByText(/The commit is running/)).toBeInTheDocument();
    unmount();
    const asked = server.of("GET", /\/api\/import-batches\/b1$/).length;

    await act(async () => { await vi.advanceTimersByTimeAsync(POLL_MS * 5); });

    expect(server.of("GET", /\/api\/import-batches\/b1$/)).toHaveLength(asked);
  });

  it("says a stopped commit stopped and retries it", async () => {
    server.current = committing({ canRetry: true });
    renderPage();
    const user = userEvent.setup();

    expect(await screen.findByText(/The commit stopped before it finished/)).toBeInTheDocument();
    expect(screen.getByText("Commit stopped")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Retry the commit" }));

    await waitFor(() => expect(server.of("POST", /\/retry$/)).toHaveLength(1));
    expect(await screen.findByText(/The commit is running/)).toBeInTheDocument();
  });

  it("will not retry a balance commit for someone without Manage billing", async () => {
    server.permissions = ["BulkImportData"];
    server.current = committing({ canRetry: true, sourceKind: "OpeningBalances" });
    renderPage();

    expect(await screen.findByRole("button", { name: "Retry the commit" })).toBeDisabled();
    expect(screen.getByText(/Retrying a balance commit also needs the Manage billing permission/)).toBeInTheDocument();
  });

  it("explains a refused retry in words", async () => {
    server.current = committing({ canRetry: true });
    server.reply = (c) => (c.method === "POST" && c.url.endsWith("/retry") ? refusal(409, "not_stuck") : null);
    renderPage();

    await userEvent.setup().click(await screen.findByRole("button", { name: "Retry the commit" }));

    expect(await screen.findByText(/not stuck, so it cannot be retried/)).toBeInTheDocument();
  });
});

describe("what happened", () => {
  const committed = (over = {}) => makeBatch({ status: "Committed", commitOutcome: "CompletedWithExceptions", acceptedRows: 6, rejectedRows: 2, skippedRows: 1, unresolvedRows: 1, mappingConfirmed: true, dryRunAtUtc: "2026-10-28T12:05:00+00:00", ...over });

  it("shows a reconciled commit in words as well as colour, with the counts and the records found", async () => {
    server.current = committed();
    renderPage();

    expect(await screen.findByText("Reconciled")).toBeInTheDocument();
    expect(screen.getByText("Every row and every record is accounted for.")).toBeInTheDocument();
    expect(screen.getByText(/6 rows were accepted; 6 of them point at a record that exists, and 6 records carry this batch's mark\./)).toBeInTheDocument();
    expect(screen.getByText("4 audit entries were recorded for this batch.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download the error report (CSV)" })).toBeInTheDocument();
  });

  it("lists every difference when the batch does not reconcile", async () => {
    server.current = committed();
    server.report = reconciliation({ reconciled: false, problems: [{ code: "accepted_result_missing", message: "6 row(s) are Accepted but only 5 point at a record that exists." }, { code: "amount_mismatch", message: "The batch recorded 10.00 but the ledger holds 9.00 for it." }] });
    renderPage();

    expect(await screen.findByText("Does not reconcile")).toBeInTheDocument();
    expect(screen.getByText("2 differences need explaining.")).toBeInTheDocument();
    const list = screen.getByRole("list", { name: "Differences" });
    expect(within(list).getByText(/only 5 point at a record/)).toBeInTheDocument();
    expect(within(list).getByText(/ledger holds 9\.00/)).toBeInTheDocument();
  });

  it("compares the balances the dry-run found, the batch recorded and the ledger holds", async () => {
    server.current = committed({ sourceKind: "OpeningBalances" });
    server.report = reconciliation({ sourceKind: "OpeningBalances", validAmount: 165.5, acceptedAmount: 165.5, ledgerTotal: 165.5 });
    renderPage();

    expect(await screen.findByText(/Balances: valid at the dry-run/)).toHaveTextContent("valid at the dry-run $165.50, loaded $165.50, held on the ledgers for this batch $165.50");
  });

  it("explains the reasons rows were not accepted", async () => {
    server.current = committed();
    renderPage();

    await userEvent.setup().click(await screen.findByText("Why rows were not accepted"));

    expect(screen.getByText("2 × Rejected: A value in the row is not valid")).toBeInTheDocument();
  });
});

describe("when the batch cannot be loaded", () => {
  it("says so and offers a retry, rather than showing an empty batch", async () => {
    server.reply = (c) => (c.method === "GET" && /\/api\/import-batches\/b1$/.test(c.url) ? refusal(404, "batch_not_found") : null);
    renderPage();

    expect(await screen.findByText("Could not load this batch")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no violations the automated check can find on the dry-run and commit steps", async () => {
    server.current = dryRunBatch();
    server.rows = [makeRow({ rowNumber: 3, status: "Rejected", reason: "invalid_fields", errors: { firstName: "First name is required." } }), makeRow({ rowNumber: 5, status: "Unresolved", reason: "possible_duplicate", matchedPatientIds: ["p1"] })];
    server.suggested = suggestion();
    const { container } = renderPage();
    await screen.findByRole("heading", { name: "Likely duplicates" });
    await screen.findByRole("table", { name: "Row results" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
