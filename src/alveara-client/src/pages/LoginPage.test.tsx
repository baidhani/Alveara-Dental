import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { LoginPage } from "./LoginPage";
import { AuthProvider } from "../contexts/AuthContext";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderLoginPage(initialEntries = ["/login"]) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <AuthProvider>
        <LoginPage />
      </AuthProvider>
    </MemoryRouter>
  );
}

describe("LoginPage", () => {
  it("shows an honest invalid-credentials message on a wrong password, not a generic error", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/auth/login")) {
          return Promise.resolve(jsonResponse({ error: "invalid_credentials", message: "Invalid username or password." }, 401));
        }
        return Promise.resolve(jsonResponse({ role: null }, 401)); // /permissions: signed out
      })
    );

    renderLoginPage();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Username"), "someone");
    await user.type(screen.getByLabelText("Password"), "wrong-password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() => expect(screen.getByText("Invalid username or password.")).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("shows a distinct locked-out message, not the generic invalid-credentials one", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/auth/login")) {
          return Promise.resolve(jsonResponse({ error: "account_locked", lockedUntilUtc: "2026-01-01T00:00:00Z" }, 423));
        }
        return Promise.resolve(jsonResponse({}, 401));
      })
    );

    renderLoginPage();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Username"), "someone");
    await user.type(screen.getByLabelText("Password"), "password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() => expect(screen.getByText(/temporarily locked/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("shows the session-expired banner when reached via ?reason=expired", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({}, 401)));

    renderLoginPage(["/login?reason=expired"]);

    await waitFor(() => expect(screen.getByText(/your session expired/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
