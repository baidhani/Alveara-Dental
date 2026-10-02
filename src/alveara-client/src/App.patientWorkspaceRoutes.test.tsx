import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "./App";
import { patientWorkspaceTabs } from "./app/patientWorkspaceTabs";
import { moduleRegistry } from "./app/moduleRegistry";

const jsonResponse = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });

function signInAs(role: string, permissions: string[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string) =>
      Promise.resolve(
        String(url).includes("/api/auth/permissions")
          ? jsonResponse({ username: "user-1", role, permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() })
          : String(url).startsWith("/api/patients") ? jsonResponse([]) : jsonResponse({ status: "ok" })
      )
    )
  );
}

afterEach(() => vi.unstubAllGlobals());

/**
 * ALV-003-C01: the patient pages and their nav links follow the permissions (the server re-checks them on every call).
 * STORY-003's own route tests stay in App.patientRoute.test.tsx, unchanged.
 */
describe("Patients routes", () => {
  it("a clinical user can open Patients (search) but not Register Patient", async () => {
    signInAs("Dentist", ["ViewPatientRecords"]);
    window.history.pushState({}, "", "/patients");

    render(<App />);

    await waitFor(() => expect(screen.getByRole("heading", { name: "Patients" })).toBeInTheDocument());
    expect(screen.getByRole("link", { name: "Patients" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Register Patient" })).not.toBeInTheDocument();
  });

  it("a user without ViewPatientRecords gets neither the Patients link nor the page nor a patient workspace URL", async () => {
    signInAs("Unassigned", []);
    window.history.pushState({}, "", "/patients");

    render(<App />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    expect(screen.queryByRole("link", { name: "Patients" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Patient context" })).not.toHaveTextContent("Find a patient");
  });

  it("a patient workspace URL is denied without ViewPatientRecords", async () => {
    signInAs("Unassigned", []);
    window.history.pushState({}, "", "/patients/aaaaaaaa-0000-0000-0000-000000000001/household");

    render(<App />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    expect(screen.queryByRole("navigation", { name: "Patient workspace sections" })).not.toBeInTheDocument();
  });

  it("only one of Patients / Register Patient is highlighted at a time", async () => {
    signInAs("FrontDesk", ["RegisterPatients", "ViewPatientRecords"]);
    window.history.pushState({}, "", "/patients/register");

    render(<App />);

    await waitFor(() => expect(screen.getByRole("heading", { name: "Register patient" })).toBeInTheDocument());
    expect(screen.getByRole("link", { name: "Register Patient" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Patients" })).not.toHaveAttribute("aria-current");
  });
});

describe("module registries", () => {
  it("the patient workspace lists only tabs that exist (no placeholders for later modules)", () => {
    // ALV-N010 added the Forms tab through this extension point; it is the only addition and is permission-gated.
    expect(patientWorkspaceTabs.map((t) => t.id)).toEqual(["details", "household", "history", "forms"]);
    expect(patientWorkspaceTabs.find((t) => t.id === "forms")?.requiredPermission).toBe("ViewSignedForms");
    expect(new Set(patientWorkspaceTabs.map((t) => t.path)).size).toBe(patientWorkspaceTabs.length);
  });

  it("the Patients and Register Patient nav entries are gated by the right permissions", () => {
    expect(moduleRegistry.find((m) => m.id === "patients")?.requiredPermission).toBe("ViewPatientRecords");
    expect(moduleRegistry.find((m) => m.id === "register-patient")?.requiredPermission).toBe("RegisterPatients");
  });
});
