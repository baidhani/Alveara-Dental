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
});
