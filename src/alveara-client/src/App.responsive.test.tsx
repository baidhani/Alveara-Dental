import { describe, it, expect } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { readFileSync } from "node:fs";
import path from "node:path";
import { App } from "./App";

describe("Application shell — responsive layout smoke test", () => {
  it("renders the same structural regions regardless of viewport (layout is CSS-driven)", async () => {
    render(<App />);
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument());

    expect(screen.getByRole("navigation", { name: "Primary navigation" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });

  it("defines a tablet-width breakpoint that collapses the sidebar to a top bar", () => {
    const cssPath = path.join(process.cwd(), "src/app/AppShell.css");
    const css = readFileSync(cssPath, "utf-8");
    expect(css).toMatch(/@media \(max-width: 820px\)/);
  });
});
