import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { MfaSettingsPage } from "./MfaSettingsPage";
import { AuthProvider } from "../contexts/AuthContext";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function renderPage() {
  return render(
    <MemoryRouter>
      <AuthProvider>
        <MfaSettingsPage />
      </AuthProvider>
    </MemoryRouter>
  );
}

describe("MfaSettingsPage", () => {
  it("shows the secret and recovery codes after starting enrollment, inert until confirmed", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string, init?: RequestInit) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions")) return Promise.resolve(jsonResponse({ role: "Dentist", permissions: [] }));
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "tok" }));
        if (u.includes("/api/auth/mfa/enroll") && init?.method === "POST") {
          return Promise.resolve(jsonResponse({ base32Secret: "ABCDEFGHIJKLMNOP", recoveryCodes: ["AAAA1111", "BBBB2222"] }));
        }
        return Promise.resolve(jsonResponse({}));
      })
    );

    renderPage();
    const user = userEvent.setup();
    await waitFor(() => expect(screen.getByRole("button", { name: "Set up an authenticator app" })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Set up an authenticator app" }));

    await waitFor(() => expect(screen.getByTestId("mfa-secret")).toHaveTextContent("ABCDEFGHIJKLMNOP"));
    expect(screen.getByTestId("mfa-recovery-codes")).toHaveTextContent("AAAA1111");
    vi.unstubAllGlobals();
  });

  it("asks for the current password before replacing an already-active factor", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string, init?: RequestInit) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions")) return Promise.resolve(jsonResponse({ role: "Dentist", permissions: [] }));
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "tok" }));
        if (u.includes("/api/auth/mfa/enroll") && init?.method === "POST") {
          const body = init?.body ? JSON.parse(String(init.body)) : {};
          if (!body.currentPassword) {
            return Promise.resolve(jsonResponse({ error: "current_password_required" }, 400));
          }
          return Promise.resolve(jsonResponse({ base32Secret: "SECRET", recoveryCodes: [] }));
        }
        return Promise.resolve(jsonResponse({}));
      })
    );

    renderPage();
    const user = userEvent.setup();
    await waitFor(() => expect(screen.getByRole("button", { name: "Set up an authenticator app" })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Set up an authenticator app" }));

    await waitFor(() => expect(screen.getByLabelText("Current password")).toBeInTheDocument());
    await user.type(screen.getByLabelText("Current password"), "my-password");
    await user.click(screen.getByRole("button", { name: "Continue" }));

    await waitFor(() => expect(screen.getByTestId("mfa-secret")).toHaveTextContent("SECRET"));
    vi.unstubAllGlobals();
  });
});
