import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "./App";

describe("Application shell — disconnected/local-server-unavailable state", () => {
  it("shows a visible disconnected banner when the health check fails, never a silent offline mode", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));

    render(<App />);

    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(/local server unavailable/i)
    );
  });
});
