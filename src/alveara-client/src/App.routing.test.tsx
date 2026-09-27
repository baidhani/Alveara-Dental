import { describe, it, expect } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "./App";

describe("Application shell — unknown route", () => {
  it("shows a truthful not-found page instead of a blank screen or crash", async () => {
    window.history.pushState({}, "", "/this-route-does-not-exist");
    render(<App />);

    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Page not found" })).toBeInTheDocument()
    );
    expect(screen.getByRole("link", { name: "Back to Dashboard" })).toBeInTheDocument();
  });
});
