import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
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

describe("LoginPage server failures and session states (ALV-N020)", () => {
  function stubLogin(loginResponse: () => Response | Promise<Response>, permissions: () => Response = () => jsonResponse({}, 401)) {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/auth/login")) return Promise.resolve(loginResponse());
        return Promise.resolve(permissions());
      })
    );
  }

  async function signIn() {
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Username"), "admin");
    await user.type(screen.getByLabelText("Password"), "password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
  }

  it("F-014: with the API down (proxy 502) it says the server cannot be reached, never 'Invalid username or password'", async () => {
    stubLogin(() => new Response("Bad gateway", { status: 502 }));
    renderLoginPage();
    await signIn();
    await waitFor(() => expect(screen.getByText(/server cannot be reached/i)).toBeInTheDocument());
    expect(screen.queryByText("Invalid username or password.")).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("F-014: with no connection at all it says the server cannot be reached", async () => {
    stubLogin(() => Promise.reject(new TypeError("Failed to fetch")));
    renderLoginPage();
    await signIn();
    await waitFor(() => expect(screen.getByText(/server cannot be reached/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("F-014: a 500 from the live API is a server problem, not wrong credentials", async () => {
    stubLogin(() => jsonResponse({ error: "internal_error" }, 500));
    renderLoginPage();
    await signIn();
    await waitFor(() => expect(screen.getByText(/server had a problem/i)).toBeInTheDocument());
    expect(screen.queryByText("Invalid username or password.")).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("F-151: sends the username without surrounding spaces", async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) =>
      Promise.resolve(String(url).includes("/api/auth/login") ? jsonResponse({ error: "invalid_credentials" }, 401) : jsonResponse({}, 401))
    );
    vi.stubGlobal("fetch", fetchMock);
    renderLoginPage();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Username"), "  dentist1 ");
    await user.type(screen.getByLabelText("Password"), "password");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    await waitFor(() => expect(screen.getByText("Invalid username or password.")).toBeInTheDocument());
    const loginCall = fetchMock.mock.calls.find(([u]) => String(u).includes("/api/auth/login"))!;
    expect(JSON.parse(String((loginCall[1] as RequestInit).body)).username).toBe("dentist1");
    vi.unstubAllGlobals();
  });

  it("F-012: hides the expiry notice while the server cannot be reached", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(new Response("Bad gateway", { status: 502 }))));
    renderLoginPage(["/login?reason=expired"]);
    await waitFor(() => expect(screen.getByRole("heading", { name: "Sign in" })).toBeInTheDocument());
    await new Promise((r) => setTimeout(r, 50));                                                       // let the failed session check settle
    expect(screen.queryByText(/your session expired/i)).not.toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("a signed-in person who opens the sign-in page goes to the dashboard", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() =>
        Promise.resolve(jsonResponse({ username: "admin", role: "Admin", permissions: [], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }))
      )
    );
    render(
      <MemoryRouter initialEntries={["/login"]}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<h1>Dashboard</h1>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>
    );
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
