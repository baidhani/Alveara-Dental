import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "./App";

const jsonResponse = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });

function signInAs(role: string, permissions: string[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string) =>
      Promise.resolve(
        String(url).includes("/api/auth/permissions")
          ? jsonResponse({ username: "user-1", role, permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() })
          : jsonResponse({ status: "ok" })
      )
    )
  );
}

afterEach(() => vi.unstubAllGlobals());

/** STORY-003: the registration page and its nav link follow the RegisterPatients permission (the server re-checks it on every call). */
describe("Register Patient route", () => {
  it("renders for a front-desk user, with its nav link", async () => {
    signInAs("FrontDesk", ["RegisterPatients", "ViewSchedule"]);
    window.history.pushState({}, "", "/patients/register");

    render(<App />);

    await waitFor(() => expect(screen.getByRole("heading", { name: "Register patient" })).toBeInTheDocument());
    expect(screen.getByRole("link", { name: "Register Patient" })).toBeInTheDocument();
    expect(screen.queryByText(/don't have permission/i)).not.toBeInTheDocument();
  });

  it("is denied to a clinical user without the permission, and the nav link is hidden", async () => {
    signInAs("Dentist", ["ViewPatientRecords"]);
    window.history.pushState({}, "", "/patients/register");

    render(<App />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    expect(screen.queryByRole("heading", { name: "Register patient" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Register Patient" })).not.toBeInTheDocument();
  });
});
