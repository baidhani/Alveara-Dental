import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { AuthProvider, useAuth } from "./AuthContext";
import { ApiError, listUsers } from "../services/authApi";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function Probe() {
  const { state, hasPermission, sessionExpiringSoon } = useAuth();
  return (
    <div>
      <span data-testid="kind">{state.kind}</span>
      <span data-testid="expiring-soon">{String(sessionExpiringSoon)}</span>
      <span data-testid="has-manage-users">{String(hasPermission("ManageUsers"))}</span>
    </div>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("AuthContext (ALV-N009)", () => {
  it("hasPermission never throws when the server response omits permissions (malformed/unexpected shape)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ status: "ok" })));

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    // Must resolve to a boolean, not throw - even though `permissions` is missing from the body.
    expect(screen.getByTestId("has-manage-users")).toHaveTextContent("false");
  });

  it("flags sessionExpiringSoon once the real server-issued expiry is within the warning window", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const soon = new Date(Date.now() + 60 * 1000).toISOString(); // 1 minute out - inside the 2-minute warning window
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ username: "u", role: "Admin", permissions: [], sessionExpiresAtUtc: soon }))
    );

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByTestId("kind")).toHaveTextContent("signed-in");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(15_000);
    });
    expect(screen.getByTestId("expiring-soon")).toHaveTextContent("true");
  });

  it("does not flag sessionExpiringSoon when the expiry is comfortably in the future", async () => {
    const later = new Date(Date.now() + 30 * 60 * 1000).toISOString();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ username: "u", role: "Admin", permissions: [], sessionExpiresAtUtc: later }))
    );

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    expect(screen.getByTestId("expiring-soon")).toHaveTextContent("false");
  });

  it("transitions to signed-out as soon as any authenticated call receives a 401, without waiting for the next permissions poll", async () => {
    let permissionsCallCount = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions")) {
          permissionsCallCount += 1;
          return Promise.resolve(
            jsonResponse({ username: "u", role: "Admin", permissions: ["ManageUsers"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() })
          );
        }
        if (u.includes("/api/auth/users")) return Promise.resolve(jsonResponse({ error: "unauthorized" }, 401));
        return Promise.resolve(jsonResponse({}));
      })
    );

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    expect(permissionsCallCount).toBe(1);

    // Simulate some OTHER authenticated call (not the permissions check itself) getting a 401 -
    // e.g. the cookie expired between page load and this action. Goes through authApi.ts's own
    // request() wrapper (not a bare fetch) since that's what actually fires the onUnauthorized
    // hook this test is exercising.
    await act(async () => {
      await listUsers().catch(() => undefined);
    });

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    // Proves this was the reactive 401 handler, not a fresh refresh() re-deriving state from a
    // second permissions call.
    expect(permissionsCallCount).toBe(1);
  });

  it("logout() clears signed-in state (UI-state-clearing: no permission survives sign-out)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL) => {
        const u = String(url);
        if (u.includes("/api/auth/permissions"))
          return Promise.resolve(
            jsonResponse({ username: "u", role: "Admin", permissions: ["ManageUsers"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() })
          );
        if (u.includes("/api/auth/logout")) return Promise.resolve(jsonResponse(undefined));
        return Promise.resolve(jsonResponse({}));
      })
    );

    function ProbeWithLogout() {
      const { logout } = useAuth();
      return (
        <div>
          <Probe />
          <button onClick={() => logout()}>logout</button>
        </div>
      );
    }

    render(
      <AuthProvider>
        <ProbeWithLogout />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    expect(screen.getByTestId("has-manage-users")).toHaveTextContent("true");

    screen.getByText("logout").click();

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(screen.getByTestId("has-manage-users")).toHaveTextContent("false");
  });

  it("R02: crosses the authoritative session expiry and unmounts protected state (was: only cleared the warning flag)", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const soon = new Date(Date.now() + 30 * 1000).toISOString(); // 30s out
    let permissionsCallCount = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() => {
        permissionsCallCount += 1;
        // First call (mount) returns the still-valid session with a near expiry; once the
        // boundary is crossed, refresh() re-asks the server and the cookie is genuinely expired.
        if (permissionsCallCount === 1) {
          return Promise.resolve(jsonResponse({ username: "u", role: "Admin", permissions: [], sessionExpiresAtUtc: soon }));
        }
        return Promise.resolve(jsonResponse({ error: "unauthorized" }, 401));
      })
    );

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByTestId("kind")).toHaveTextContent("signed-in");

    // Cross the 30s expiry boundary without any other API call happening in between.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(45_000);
    });

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(permissionsCallCount).toBeGreaterThan(1);
  });

  it("R02: revalidates a signed-in session on a bounded poll and clears state when access was revoked elsewhere", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let permissionsCallCount = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() => {
        permissionsCallCount += 1;
        const farFuture = new Date(Date.now() + 30 * 60 * 1000).toISOString();
        // First call (mount) is signed in with ManageUsers; every call after that simulates the
        // role having been changed by another admin session mid-visit (no permission survives).
        if (permissionsCallCount === 1) {
          return Promise.resolve(
            jsonResponse({ username: "u", role: "Admin", permissions: ["ManageUsers"], sessionExpiresAtUtc: farFuture })
          );
        }
        return Promise.resolve(jsonResponse({ error: "unauthorized" }, 401));
      })
    );

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    expect(screen.getByTestId("has-manage-users")).toHaveTextContent("true");
    expect(permissionsCallCount).toBe(1);

    // No navigation, no focus event, no unrelated protected call - just time passing, which is
    // exactly the "moving among routes that don't fetch protected data" scenario the review flagged.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30_000);
    });

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(permissionsCallCount).toBeGreaterThan(1);
  });

  it("ApiError with status 401 from getMyPermissions itself resolves to signed-out, not a thrown crash", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ error: "unauthorized" }, 401)));

    render(
      <AuthProvider>
        <Probe />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
  });
});

// Sanity: ApiError is exported with the shape AuthContext/authApi rely on.
describe("ApiError", () => {
  it("carries status/code/body", () => {
    const err = new ApiError(401, "unauthorized", "nope", { foo: "bar" });
    expect(err.status).toBe(401);
    expect(err.code).toBe("unauthorized");
    expect(err.body).toEqual({ foo: "bar" });
  });
});
