import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { FormField } from "./FormField";

describe("FormField — validation/error failure path", () => {
  it("marks the input invalid and announces the error via aria-describedby + role=alert", () => {
    render(<FormField label="Email address" error="Enter a valid email address." />);

    const input = screen.getByLabelText("Email address");
    expect(input).toHaveAttribute("aria-invalid", "true");

    const error = screen.getByRole("alert");
    expect(error).toHaveTextContent("Enter a valid email address.");
    expect(input.getAttribute("aria-describedby")).toContain(error.id);
  });

  it("shows the hint instead of the error when there is no error", () => {
    render(<FormField label="Email address" hint="We'll never share this." />);
    expect(screen.getByText("We'll never share this.")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("simultaneous hint + error: hint is hidden and aria-describedby only references the rendered error (no dangling id)", () => {
    render(
      <FormField
        label="Email address"
        hint="We'll never share this."
        error="Enter a valid email address."
      />
    );

    expect(screen.queryByText("We'll never share this.")).not.toBeInTheDocument();

    const input = screen.getByLabelText("Email address");
    const error = screen.getByRole("alert");
    const describedBy = input.getAttribute("aria-describedby") ?? "";

    expect(describedBy).toBe(error.id); // exactly the error id, nothing else
    for (const id of describedBy.split(/\s+/).filter(Boolean)) {
      expect(document.getElementById(id)).not.toBeNull(); // every referenced id must exist in the DOM
    }
  });
});
