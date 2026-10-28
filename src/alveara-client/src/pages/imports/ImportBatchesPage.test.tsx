import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import { ImportBatchesPage } from "./ImportBatchesPage";
import { FakeImportServer, dryRunBatch, json, makeBatch } from "./importTestServer";

// ALV-018-C02 slice 7: the batch list and the upload form, against a small in-memory server that answers like the real API.

let server: FakeImportServer;

beforeEach(() => { server = new FakeImportServer(); server.install(); });
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/admin/data-import"]}>
      <AuthProvider>
        <Routes>
          <Route path="/admin/data-import" element={<ImportBatchesPage />} />
          <Route path="/admin/data-import/:batchId" element={<p>Batch page opened</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

const csv = () => new File(["First Name,Last Name\r\nAnn,Rivera\r\n"], "patients.csv", { type: "text/csv" });

describe("the batch list", () => {
  it("lists every batch with its state in words and a link to open it", async () => {
    server.batches = [dryRunBatch({ id: "b2", sourceLabel: "October export" }), makeBatch({ id: "b3", sourceLabel: "Balances", sourceKind: "OpeningBalances", status: "Committed", commitOutcome: "CompletedWithExceptions" })];
    renderPage();

    const table = await screen.findByRole("table", { name: "Import batches, newest first" });
    expect(within(table).getByRole("link", { name: "October export" })).toHaveAttribute("href", "/admin/data-import/b2");
    expect(within(table).getByText("Dry-run done - ready to commit")).toBeInTheDocument();
    expect(within(table).getByText("Committed with exceptions")).toBeInTheDocument();
    expect(within(table).getByText("Opening balances")).toBeInTheDocument();
  });

  it("says so when there are no batches, and shows a retry (not an empty list) when the read fails", async () => {
    renderPage();
    expect(await screen.findByText("No files have been sent yet.")).toBeInTheDocument();

    server.reply = (c) => (c.method === "GET" && c.url.startsWith("/api/import-batches?") ? json({ error: "x" }, 500) : null);
    await userEvent.setup().click(screen.getByRole("button", { name: "Refresh" }));
    expect(await screen.findByText("Could not load the batches")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });
});

describe("sending a file", () => {
  it("needs a file and a source name before it can be sent", async () => {
    renderPage();
    const send = await screen.findByRole("button", { name: "Check and upload the file" });
    expect(send).toBeDisabled();

    const user = userEvent.setup();
    await user.upload(screen.getByLabelText(/CSV file/), csv());
    expect(send).toBeDisabled();
    await user.type(screen.getByLabelText(/Where the file came from/), "Old system");
    expect(send).toBeEnabled();
  });

  it("posts the chosen kind, the label and the file, then opens the new batch", async () => {
    server.reply = (c) => (c.method === "POST" && c.url.startsWith("/api/import-batches?") ? json({ batch: makeBatch({ id: "b9" }), created: true }, 201) : null);
    renderPage();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("radio", { name: "Appointments" }));
    await user.type(screen.getByLabelText(/Where the file came from/), "Old system");
    await user.upload(screen.getByLabelText(/CSV file/), csv());
    await user.click(screen.getByRole("button", { name: "Check and upload the file" }));

    expect(await screen.findByText("Batch page opened")).toBeInTheDocument();
    const post = server.of("POST", /\/api\/import-batches\?/)[0];
    expect(post.url).toBe("/api/import-batches?sourceKind=Appointments&label=Old+system");
    expect(new Headers(post.init?.headers).get("X-CSRF-Token")).toBe("t");
  });

  it("lists every problem of a refused file with its place and opens nothing", async () => {
    server.reply = (c) => (c.method === "POST" && c.url.startsWith("/api/import-batches?")
      ? json({ error: "import_file_rejected", message: "no", problems: [{ code: "bad", row: 5, column: "DOB", message: "Not a real date." }, { code: "empty", row: null, column: null, message: "The file is empty." }] }, 400) : null);
    renderPage();
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText(/Where the file came from/), "Old system");
    await user.upload(screen.getByLabelText(/CSV file/), csv());
    await user.click(screen.getByRole("button", { name: "Check and upload the file" }));

    expect(await screen.findByText("The file was not accepted. Correct the problems listed and choose it again.")).toBeInTheDocument();
    const list = screen.getByRole("list", { name: "Problems in the file" });
    expect(within(list).getByText(/Row 5, column “DOB”: Not a real date\./)).toBeInTheDocument();
    expect(within(list).getByText(/The file: The file is empty\./)).toBeInTheDocument();
    expect(screen.queryByText("Batch page opened")).not.toBeInTheDocument();
  });

  it("explains what each kind of file does before it is sent", async () => {
    renderPage();
    const user = userEvent.setup();

    expect(await screen.findByText(/Load these first/)).toBeInTheDocument();
    await user.click(screen.getByRole("radio", { name: "Opening balances" }));
    expect(screen.getByText(/first entry on their billing ledger/)).toBeInTheDocument();
  });
});

describe("stray files", () => {
  it("asks before it removes anything, can be cancelled, and reports what the sweep did", async () => {
    renderPage();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Remove stray files" }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(server.of("POST", /orphan-sweep/)).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Remove stray files" }));
    await user.click(screen.getByRole("button", { name: "Yes, remove stray files" }));

    expect(await screen.findByText("Looked at 3 file(s): removed 1, kept 2.")).toBeInTheDocument();
    expect(server.of("POST", /orphan-sweep/)).toHaveLength(1);
  });
});

describe("accessibility", () => {
  it("has no violations the automated check can find", async () => {
    server.batches = [dryRunBatch({ id: "b2" })];
    const { container } = renderPage();
    await screen.findByRole("table", { name: "Import batches, newest first" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
