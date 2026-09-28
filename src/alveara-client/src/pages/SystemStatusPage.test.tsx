import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { SystemStatusPage } from "./SystemStatusPage";

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
      vi.fn().mockResolvedValue(
        jsonResponse({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: true },
          backgroundRunner: { status: "healthy", lastPollUtc: new Date().toISOString() },
        })
      )
    );

    render(<SystemStatusPage />);

    await waitFor(() => expect(screen.getByText("1.0.0.0")).toBeInTheDocument());
    expect(screen.getAllByText("Reachable")).toHaveLength(2); // local server + database
    expect(screen.getByText("Healthy")).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  it("shows the database as unreachable without hiding the rest of the page", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        jsonResponse({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: false },
          backgroundRunner: { status: "not_yet_polled", lastPollUtc: null },
        })
      )
    );

    render(<SystemStatusPage />);

    await waitFor(() => expect(screen.getByText("Unreachable")).toBeInTheDocument());
    expect(screen.getByText("Not yet polled")).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  it("distinguishes 'local server unavailable' from a public-internet problem when the fetch itself fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));

    render(<SystemStatusPage />);

    await waitFor(() => expect(screen.getByText("Local server unavailable")).toBeInTheDocument());
    expect(screen.getByText(/different from a public-internet outage/i)).toBeInTheDocument();

    vi.unstubAllGlobals();
  });
});
