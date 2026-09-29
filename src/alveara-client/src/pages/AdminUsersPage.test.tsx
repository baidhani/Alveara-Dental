import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { AdminUsersPage } from "./AdminUsersPage";
import { AuthProvider } from "../contexts/AuthContext";
import { NotificationProvider } from "../components/Notification";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderPage() {
  return render(
    <MemoryRouter>
      <NotificationProvider>
        <AuthProvider>
          <AdminUsersPage />
        </AuthProvider>
      </NotificationProvider>
    </MemoryRouter>
  );
}

describe("AdminUsersPage", () => {
  it("shows a clear permission-denied state when the caller lacks ManageUsers, not a generic error", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/auth/permissions")) {
          return Promise.resolve(jsonResponse({ role: "Billing", permissions: ["ManageBilling"] }));
        }
        if (String(url).includes("/api/auth/users")) {
          return Promise.resolve(jsonResponse({ error: "permission_denied", required: "ManageUsers" }, 403));
        }
        return Promise.resolve(jsonResponse({}));
      })
    );

    renderPage();

    await waitFor(() => expect(screen.getByText(/don't have permission/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("lists users with role/status/MFA state when the caller has ManageUsers", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/auth/permissions")) {
          return Promise.resolve(jsonResponse({ role: "Admin", permissions: ["ManageUsers", "ManageRoles", "ManageAccountStatus"] }));
        }
        if (String(url).includes("/api/auth/users")) {
          return Promise.resolve(
            jsonResponse([
              {
                id: "11111111-1111-1111-1111-111111111111",
                username: "front-desk-1",
                role: "FrontDesk",
                isDisabled: false,
                mfaEnabled: false,
                createdAtUtc: new Date().toISOString(),
              },
            ])
          );
        }
        return Promise.resolve(jsonResponse({}));
      })
    );

    renderPage();

    await waitFor(() => expect(screen.getByText("front-desk-1")).toBeInTheDocument());
    expect(screen.getByText("Enabled")).toBeInTheDocument();
    expect(screen.getByText("Not enabled")).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("shows an empty state, not a blank table, when there are no users", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/auth/permissions")) {
          return Promise.resolve(jsonResponse({ role: "Admin", permissions: ["ManageUsers"] }));
        }
        if (String(url).includes("/api/auth/users")) {
          return Promise.resolve(jsonResponse([]));
        }
        return Promise.resolve(jsonResponse({}));
      })
    );

    renderPage();

    await waitFor(() => expect(screen.getByText("No users yet")).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
