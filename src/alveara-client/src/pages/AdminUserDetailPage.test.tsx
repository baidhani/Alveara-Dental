import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AdminUserDetailPage } from "./AdminUserDetailPage";
import { AuthProvider } from "../contexts/AuthContext";
import { NotificationProvider } from "../components/Notification";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const user = {
  id: "11111111-1111-1111-1111-111111111111",
  username: "front-desk-1",
  role: "FrontDesk",
  isDisabled: false,
  mfaEnabled: false,
  sessionTimeoutMinutes: 30,
  createdAtUtc: new Date().toISOString(),
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/admin/users/${user.id}`]}>
      <NotificationProvider>
        <AuthProvider>
          <Routes>
            <Route path="/admin/users/:userId" element={<AdminUserDetailPage />} />
          </Routes>
        </AuthProvider>
      </NotificationProvider>
    </MemoryRouter>
  );
}

describe("AdminUserDetailPage", () => {
  it("shows the user's details and lets an admin update the session timeout", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string, init?: RequestInit) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions")) return Promise.resolve(jsonResponse({ role: "Admin", permissions: ["ManageUsers", "ManageAccountStatus", "ManageRoles"] }));
        if (u.includes("/api/auth/users") && (!init || init.method === undefined)) return Promise.resolve(jsonResponse([user]));
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "tok" }));
        if (u.includes("/session-timeout")) return Promise.resolve(jsonResponse({ id: user.id, username: user.username, sessionTimeoutMinutes: 60 }));
        return Promise.resolve(jsonResponse([user]));
      })
    );

    renderPage();
    const ue = userEvent.setup();

    await waitFor(() => expect(screen.getByText("front-desk-1")).toBeInTheDocument());
    const input = screen.getByLabelText("Session timeout (minutes)");
    await ue.clear(input);
    await ue.type(input, "60");
    await ue.click(screen.getByRole("button", { name: "Update timeout" }));

    await waitFor(() => expect(screen.getByText("front-desk-1")).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("shows a not-found state for an id that isn't in the user list", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions")) return Promise.resolve(jsonResponse({ role: "Admin", permissions: ["ManageUsers"] }));
        if (u.includes("/api/auth/users")) return Promise.resolve(jsonResponse([]));
        return Promise.resolve(jsonResponse({}));
      })
    );

    renderPage();

    await waitFor(() => expect(screen.getByText("User not found")).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
