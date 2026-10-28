import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "../../App";
import { FakeImportServer, dryRunBatch } from "./importTestServer";

// ALV-018-C02 slice 7: the data import screens' place in the application: a menu entry and the two routes for anyone who holds BulkImportData, and a permission-denied page (not a screen) for everyone else.

let server: FakeImportServer;

beforeEach(() => {
  server = new FakeImportServer();
  server.install();
  server.batches = [dryRunBatch()];
  server.current = dryRunBatch();
});
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("the data import screens in the application", () => {
  it("are in the menu, and the list and a batch open, for someone who holds BulkImportData", async () => {
    window.history.pushState({}, "", "/admin/data-import");
    render(<App />);

    expect(await screen.findByRole("heading", { name: "Data import" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Data import" })).toHaveAttribute("href", "/admin/data-import");
  });

  it("open a batch by its address", async () => {
    window.history.pushState({}, "", "/admin/data-import/b1");
    render(<App />);

    expect(await screen.findByRole("heading", { name: "Patients: Old system" })).toBeInTheDocument();
  });

  it("are neither in the menu nor reachable by their addresses for someone without the permission", async () => {
    server.permissions = ["ViewPatientRecords"];
    for (const path of ["/admin/data-import", "/admin/data-import/b1"]) {
      window.history.pushState({}, "", path);
      const { unmount } = render(<App />);

      await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
      expect(screen.queryByRole("heading", { name: "Data import" })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Data import" })).not.toBeInTheDocument();
      unmount();
    }
  });
});
