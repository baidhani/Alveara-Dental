import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "./App";

describe("Application shell — disconnected/local-server-unavailable state", () => {
  it("shows a visible disconnected banner when the health check fails, never a silent offline mode", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));

    render(<App />);

    // ALV-N020 (F-007, F-012, F-013): the outage leaves a first visit on the plain sign-in page - no "session expired" notice - and the banner there is the
    // sign-in variant, without "Changes cannot be saved".
    await waitFor(() => {
      const alerts = screen.getAllByRole("alert");
      expect(alerts.some((el) => /local server unavailable/i.test(el.textContent ?? ""))).toBe(true);
    });
    await waitFor(() => expect(screen.getByRole("heading", { name: "Sign in" })).toBeInTheDocument());   // the failed session check has settled on the sign-in page
    await waitFor(() => expect(screen.queryByText(/changes cannot be saved/i)).not.toBeInTheDocument());
    expect(screen.queryByText(/your session expired/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/changes cannot be saved/i)).not.toBeInTheDocument();
  });
});
