import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { SystemStatusPage } from "./SystemStatusPage";
import { AuthContext } from "../contexts/authContextObject";
import type { AuthContextValue } from "../contexts/authContextObject";

// ALV-N026 (D-06): the page is for administrators; these tests sign in as one unless they say otherwise
const as = (role: string) => ({ state: { kind: "signed-in", role, username: "u", permissions: [], sessionExpiresAtUtc: null }, hasPermission: () => false }) as unknown as AuthContextValue;
const asAdmin = (ui: React.ReactElement, role = "Admin") => <AuthContext.Provider value={as(role)}>{ui}</AuthContext.Provider>;

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

describe("SystemStatusPage", () => {
  it("shows every field truthfully when the system is healthy", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(async () =>   // ALV-N026: a fresh response per call (the page now also reads the administrator sections)
        jsonResponse({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: true },
          backgroundRunner: { status: "healthy", lastPollUtc: new Date().toISOString() },
        })
      )
    );

    render(asAdmin(<SystemStatusPage />));

    await waitFor(() => expect(screen.getByText("1.0.0.0")).toBeInTheDocument());
    expect(screen.getAllByText("Reachable")).toHaveLength(2); // local server + database
    expect(screen.getByText("Healthy")).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  it("shows the database as unreachable without hiding the rest of the page", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(async () =>   // ALV-N026: a fresh response per call
        jsonResponse({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: false },
          backgroundRunner: { status: "not_yet_polled", lastPollUtc: null },
        })
      )
    );

    render(asAdmin(<SystemStatusPage />));

    await waitFor(() => expect(screen.getByText("Unreachable")).toBeInTheDocument());
    expect(screen.getByText("Not yet polled")).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  it("distinguishes 'local server unavailable' from a public-internet problem when the fetch itself fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));

    render(asAdmin(<SystemStatusPage />));

    await waitFor(() => expect(screen.getByText("Local server unavailable")).toBeInTheDocument());
    expect(screen.getByText(/different from a public-internet outage/i)).toBeInTheDocument();

    vi.unstubAllGlobals();
  });
});

describe("SystemStatusPage - ALV-N026 administrator sections", () => {
  const health = { appVersion: "1.0.0.0", localServerReachable: true, database: { reachable: true }, backgroundRunner: { status: "healthy", lastPollUtc: null } };

  it("is for administrators only", () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({})));
    render(asAdmin(<SystemStatusPage />, "OfficeManager"));
    expect(screen.getByText("System Status is for administrators")).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it("shows backup, storage and account counts, and says a failure stays until a later success", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => Promise.resolve(jsonResponse(String(url).includes("/admin")
      ? { backup: { lastGoodBackupUtc: null, lastRestoreDrillUtc: null, lastRestoreDrillOutcome: null, recoveryKeyConfigured: true, scheduleEnabled: true, overdue: true, unresolvedFailures: 1, unacknowledgedAlerts: 0, warning: true },
          storage: { documentFolderReachable: true, freeBytes: 5 * 1024 * 1024 * 1024, missingDocuments: 2, damagedDocuments: 1 },
          accounts: { enabled: 7, disabled: 2, locked: 1, enabledWithoutMfa: 3 },
          pdfEngine: { ready: false, code: "pdf_engine_unavailable", errorClass: "EngineMissing", reason: "The PDF engine is not installed.", bundleId: "typst-0.15.1+noto-sans-2.015+vcrt-missing", checkedAtUtc: "2030-01-01T00:00:00Z" } }
      : health))));
    render(asAdmin(<SystemStatusPage />));
    expect(await screen.findByText("Needs attention")).toBeInTheDocument();
    expect(screen.getByText("Scheduled backups: Overdue")).toBeInTheDocument();
    expect(screen.getByText("Unresolved failures: 1 (not yet acknowledged: 0)")).toBeInTheDocument();
    expect(screen.getByText(/stays here until a later backup, verification or restore drill succeeds, even if it was acknowledged/)).toBeInTheDocument();
    expect(screen.getByText("Free space: 5.0 GB free")).toBeInTheDocument();
    expect(screen.getByText("Missing documents: 2")).toBeInTheDocument();
    expect(screen.getByText("Enabled without MFA: 3")).toBeInTheDocument();
    expect(screen.getByText("Unavailable")).toBeInTheDocument();                                   // ALV-N026 S4: the PDF engine line
    expect(screen.getByText(/The PDF engine is not installed\. Other work is unaffected/)).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("says a section could not be read rather than calling it healthy", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => Promise.resolve(jsonResponse(String(url).includes("/admin")
      ? { backup: null, storage: null, accounts: { enabled: 1, disabled: 0, locked: 0, enabledWithoutMfa: 0 } }
      : health))));
    render(asAdmin(<SystemStatusPage />));
    expect(await screen.findAllByText(/Could not be read right now; this does not mean it is healthy\./)).toHaveLength(3);   // backup, storage and (ALV-N026 S4) the PDF engine, which this answer leaves out
    vi.unstubAllGlobals();
  });
});
