import { describe, it, expect } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { App } from "./App";

describe("Application shell — accessibility smoke test", () => {
  it("has no detectable axe violations on the dashboard", async () => {
    const { container } = render(<App />);
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument());

    const results = await axe(container);
    expect(results).toHaveNoViolations();
  });
});
