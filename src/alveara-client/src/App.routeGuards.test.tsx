import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function stubFetch(handlers: { permissions?: () => Response; users?: () => Response }) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: string) => {
      const u = String(url);
      if (u.includes("/api/auth/permissions") && handlers.permissions) return Promise.resolve(handlers.permissions());
      if (u.includes("/api/auth/users") && handlers.users) return Promise.resolve(handlers.users());
      if (u.includes("/api/health")) return Promise.resolve(jsonResponse({ status: "ok" }));
      return Promise.resolve(jsonResponse({}));
    })
  );
}

describe("Direct-URL/deep-link route guards (ALV-N009)", () => {
  it("denies a direct URL to a protected route when signed out, redirecting to the session-expired login screen", async () => {
    stubFetch({ permissions: () => jsonResponse({ error: "unauthorized" }, 401) });
    window.history.pushState({}, "", "/admin/users");

    render(<App />);

    await waitFor(() => expect(screen.getByText(/your session expired/i)).toBeInTheDocument());
    // The protected page's own content must never have mounted - proves the guard denies before
    // any protected data fetch starts, not merely after one fails.
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows permission-denied (not the protected page) for a signed-in caller lacking the required permission, with the shell nav still visible", async () => {
    stubFetch({
      permissions: () =>
        jsonResponse({ username: "billing-1", role: "Billing", permissions: ["ManageBilling"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }),
    });
    window.history.pushState({}, "", "/admin/users");

    render(<App />);

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    // Still inside the shell - nav is visible, just without the forbidden entry - so the caller
    // isn't stranded on a bare error screen with no way back into the app.
    expect(screen.getByRole("navigation", { name: "Primary navigation" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Security Administration" })).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("renders the protected page for a signed-in caller who holds the required permission", async () => {
    stubFetch({
      permissions: () =>
        jsonResponse({ username: "admin-1", role: "Admin", permissions: ["ManageUsers"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }),
      users: () => jsonResponse([]),
    });
    window.history.pushState({}, "", "/admin/users");

    render(<App />);

    await waitFor(() => expect(screen.getByRole("heading", { name: "Security administration" })).toBeInTheDocument());
    expect(screen.queryByText(/don't have permission/i)).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("returns a caller to the page they were denied once they sign in", async () => {
    let permissionsCallCount = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions")) {
          permissionsCallCount += 1;
          // First check (before sign-in): no session. After a successful login, refresh() asks
          // again and this time gets a real signed-in identity with the matrix permission.
          return Promise.resolve(
            permissionsCallCount === 1
              ? jsonResponse({ error: "unauthorized" }, 401)
              : jsonResponse({
                  username: "admin-1",
                  role: "Admin",
                  permissions: ["ViewPermissionMatrix"],
                  sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString(),
                })
          );
        }
        if (u.includes("/api/auth/login")) {
          return Promise.resolve(jsonResponse({ id: "11111111-1111-1111-1111-111111111111", username: "admin-1", role: "Admin" }));
        }
        if (u.includes("/api/auth/permission-matrix")) return Promise.resolve(jsonResponse([]));
        if (u.includes("/api/health")) return Promise.resolve(jsonResponse({ status: "ok" }));
        return Promise.resolve(jsonResponse({}));
      })
    );
    window.history.pushState({}, "", "/admin/permissions");

    render(<App />);

    await waitFor(() => expect(screen.getByRole("heading", { name: "Sign in" })).toBeInTheDocument());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Username"), "admin-1");
    await user.type(screen.getByLabelText("Password"), "correct-password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    // Landed back on the ORIGINALLY requested page (/admin/permissions), not the default
    // dashboard - proving RequireAuth's captured `from` location survived the round trip.
    await waitFor(() => expect(screen.getByRole("heading", { name: "Permission matrix" })).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
