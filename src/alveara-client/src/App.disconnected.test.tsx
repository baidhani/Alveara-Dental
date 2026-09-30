import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "./App";

describe("Application shell — disconnected/local-server-unavailable state", () => {
  it("shows a visible disconnected banner when the health check fails, never a silent offline mode", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));

    render(<App />);

    // ALV-N009: the same outage that fails the health check also fails the permissions check,
    // which (correctly) cannot distinguish "genuinely signed out" from "server unreachable" and
    // lands on the login screen's own session-expired alert too - both alerts are expected
    // together here; the disconnected banner specifically must be one of them.
    await waitFor(() => {
      const alerts = screen.getAllByRole("alert");
      expect(alerts.some((el) => /local server unavailable/i.test(el.textContent ?? ""))).toBe(true);
    });
  });
});
