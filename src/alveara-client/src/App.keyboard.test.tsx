import { describe, it, expect } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";

describe("Application shell — keyboard-only path", () => {
  it("lets a keyboard-only user reach and activate the skip link, then tab into navigation and activate a module link", async () => {
    const user = userEvent.setup();
    render(<App />);
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument());

    // First tab stop must be the skip link (keyboard users should never have
    // to tab through the entire sidebar just to reach the content region).
    await user.tab();
    expect(screen.getByText("Skip to main content")).toHaveFocus();

    // Continue tabbing until the "Component Showcase" nav link is focused,
    // then activate it with the keyboard alone.
    const showcaseLink = screen.getByRole("link", { name: "Component Showcase" });
    let guard = 0;
    while (document.activeElement !== showcaseLink && guard < 20) {
      await user.tab();
      guard += 1;
    }
    expect(showcaseLink).toHaveFocus();

    await user.keyboard("{Enter}");
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Component Showcase" })).toBeInTheDocument()
    );
  });
});
