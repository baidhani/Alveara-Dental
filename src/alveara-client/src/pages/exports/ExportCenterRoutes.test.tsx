import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "../../App";

// ALV-018-C01: the Export screen's place in the application: a menu entry and a route for anyone who holds one of the three export permissions, and a permission-denied page (not the screen) for everyone else.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

function stubFetch(permissions: string[]) {
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => {
    const u = String(url);
    if (u.includes("/api/auth/permissions")) return Promise.resolve(json({ username: "u", role: "Admin", permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
    if (u.includes("/api/patient-exports/overview")) return Promise.resolve(json({ domains: [], excluded: [], maxPatients: 5000, maxPackageBytes: 1, defaultFolderNaming: "IdOnly", allPatientsReadMode: "TableByTable", patientCount: 0, documentBytes: 0, allPatientsWithinLimits: true, practiceTimeZone: "UTC" }));
    if (u.includes("/api/patient-exports/history")) return Promise.resolve(json([]));
    if (u.includes("/api/health")) return Promise.resolve(json({ status: "ok" }));
    return Promise.resolve(json({}));
  }));
}

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("the export screen in the application", () => {
  it.each(["ExportPatientData", "ExportAllPatientData", "ExportPatientReadableRecord"])("is in the menu and opens for someone who holds only %s", async (permission) => {
    stubFetch([permission]);
    window.history.pushState({}, "", "/admin/exports");

    render(<App />);

    expect(await screen.findByRole("heading", { name: "Export patient data" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Export patient data" })).toHaveAttribute("href", "/admin/exports");
  });

  it("is neither in the menu nor reachable by its address for someone who holds no export permission", async () => {
    stubFetch(["ViewPatientRecords"]);
    window.history.pushState({}, "", "/admin/exports");

    render(<App />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    expect(screen.queryByRole("heading", { name: "Export patient data" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Export patient data" })).not.toBeInTheDocument();
  });
});
