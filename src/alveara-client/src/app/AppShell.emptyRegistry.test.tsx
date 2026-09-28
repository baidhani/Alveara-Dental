import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";

// Failure path required by the ALV-N001 story prompt: "Empty module
// registry" must not crash the shell. Mocked here rather than temporarily
// emptying the real registry, so this test is independent of how many real
// modules exist at any given time.
vi.mock("./moduleRegistry", () => ({ moduleRegistry: [] }));

describe("AppShell — empty module registry failure path", () => {
  it("renders without crashing and keeps the main content region reachable when there are no modules", async () => {
    const { AppShell } = await import("./AppShell");

    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route element={<AppShell />}>
            <Route path="/" element={<div>Placeholder content</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    );

    expect(screen.getByText("Placeholder content")).toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: "Primary navigation" });
    expect(nav.children.length).toBe(0);
  });
});
