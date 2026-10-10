import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { PlanReference } from "./DiagnosisItem";

/**
 * ALV-015-C01: how a diagnosis shows its treatment-plan reference. Until it is tied to a plan it is exactly what STORY-013 showed (unresolved, proving nothing); once tied it names the plan with a link to the plan tab, how
 * it was tied and by whom, and says the original text is kept.
 */
const show = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

describe("PlanReference", () => {
  it("says there is none when there is none", () => {
    show(<PlanReference value={null} />);
    expect(screen.getByText("No treatment plan reference.")).toBeInTheDocument();
  });

  it("keeps saying an untied reference is unresolved and proves nothing", () => {
    show(<PlanReference value="plan-7" />);
    expect(screen.getByText(/Treatment plan reference \(unresolved\):/)).toBeInTheDocument();
    expect(screen.getByText(/does not mean a treatment plan with this reference exists/)).toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("names the plan it was tied to, links to the plan tab, and says how, by whom and that the original text is kept", () => {
    show(<PlanReference value="plan-7" resolved={{ patientId: "p1", title: "Gum health plan", method: "ReferenceCode", byName: "Dr. Okafor", atUtc: "2030-01-03T10:00:00Z" }} />);
    expect(screen.getByText(/Treatment plan reference \(tied to a plan\):/)).toBeInTheDocument();
    expect(screen.getByText("plan-7")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Gum health plan" })).toHaveAttribute("href", "/patients/p1/treatment-plan");
    expect(screen.getByText(/Matched to the plan's reference code by Dr\. Okafor/)).toBeInTheDocument();
    expect(screen.getByText(/The original text is kept as it was written\./)).toBeInTheDocument();
    expect(screen.queryByText(/unresolved/)).not.toBeInTheDocument();
  });

  it.each([["PlanId", "Matched to the plan's id"], ["Chosen", "Chosen by a person"], [null, "Tied to the plan"]])("words the way it was tied (%s)", (method, words) => {
    show(<PlanReference value="x" resolved={{ patientId: "p1", title: null, method, byName: null, atUtc: null }} />);
    expect(screen.getByText(new RegExp(`^${words}`))).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "this patient's plan" })).toBeInTheDocument();
  });
});
