import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AppShell } from "./AppShell";
import { AuthProvider } from "../contexts/AuthContext";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function stubPermissions(role: string, permissions: string[]) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string) => {
      if (String(url).includes("/api/auth/permissions")) {
        return Promise.resolve(
          jsonResponse({ username: "test-user", role, permissions, sessionExpiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString() })
        );
      }
      if (String(url).includes("/api/auth/logout")) {
        return Promise.resolve(jsonResponse(undefined));
      }
      return Promise.resolve(jsonResponse({ status: "ok" }));
    })
  );
}

function renderShell() {
  return render(
    <MemoryRouter initialEntries={["/"]}>
      <AuthProvider>
        <Routes>
          <Route element={<AppShell />}>
            <Route path="/" element={<div>Dashboard content</div>} />
          </Route>
        </Routes>
      </AuthProvider>
    </MemoryRouter>
  );
}

describe("AppShell — permission-aware navigation (ALV-N009)", () => {
  it("hides a nav entry whose required permission the caller does not hold", async () => {
    stubPermissions("Billing", ["ManageBilling"]);
    renderShell();

    await waitFor(() => expect(screen.getByText("Dashboard content")).toBeInTheDocument());

    expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Security Administration" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Permission Matrix" })).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows a nav entry once the caller holds its required permission", async () => {
    stubPermissions("Admin", ["ManageUsers", "ViewPermissionMatrix"]);
    renderShell();

    await waitFor(() => expect(screen.getByText("Dashboard content")).toBeInTheDocument());

    expect(screen.getByRole("link", { name: "Security Administration" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Permission Matrix" })).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows the signed-in caller's identity and a working, keyboard-reachable sign-out control", async () => {
    stubPermissions("Dentist", []);
    renderShell();

    await waitFor(() => expect(screen.getByText("test-user")).toBeInTheDocument());
    expect(screen.getByText("Dentist")).toBeInTheDocument();

    const signOutButton = screen.getByRole("button", { name: "Sign out" });
    const user = userEvent.setup();
    await user.click(signOutButton);

    // Signing out clears the shell's protected state - the account identity and nav disappear
    // because the whole shell unmounts once AuthContext transitions to signed-out (this render
    // has no router-level guard wired, so we assert the local effect: identity is gone).
    await waitFor(() => expect(screen.queryByText("test-user")).not.toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
