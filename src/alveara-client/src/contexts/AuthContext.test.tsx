import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { AuthProvider, CONNECTION_RETRY_INTERVAL_MS, useAuth } from "./AuthContext";
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

  it("R03: an older in-flight refresh cannot restore signed-in state after a newer 401 already cleared it", async () => {
    let resolveOlder!: (r: Response) => void;
    const olderResponse = new Promise<Response>((resolve) => {
      resolveOlder = resolve;
    });
    let callCount = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() => {
        callCount += 1;
        // Call 1 (mount's refresh()) is deliberately left pending - it represents an older request
        // that resolves LAST, after a newer one has already superseded its decision. Every call
        // after that resolves immediately with a 401, simulating the session having been revoked
        // (e.g. a role change elsewhere) by the time the newer request lands.
        if (callCount === 1) return olderResponse;
        return Promise.resolve(jsonResponse({ error: "unauthorized" }, 401));
      })
    );

    function ProbeWithRefresh() {
      const { refresh } = useAuth();
      return (
        <div>
          <Probe />
          <button onClick={() => refresh()}>refresh</button>
        </div>
      );
    }

    render(
      <AuthProvider>
        <ProbeWithRefresh />
      </AuthProvider>
    );

    // Mount's own refresh() (call 1) is still pending - nothing has resolved yet.
    expect(screen.getByTestId("kind")).toHaveTextContent("loading");

    // A newer refresh() (call 2) resolves immediately with a 401 and clears to signed-out.
    await act(async () => {
      screen.getByText("refresh").click();
      await Promise.resolve();
      await Promise.resolve();
    });
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));

    // NOW the older call (1) finally resolves with a successful signed-in body. It must be
    // ignored - a newer decision (the 401 above) already superseded it - or this would silently
    // resurrect a session that was just revoked.
    await act(async () => {
      resolveOlder(
        jsonResponse({
          username: "u",
          role: "Admin",
          permissions: ["ManageUsers"],
          sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString(),
        })
      );
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(screen.getByTestId("kind")).toHaveTextContent("signed-out");
  });

  it("R03: logout() invalidates an older in-flight refresh so it cannot restore state after sign-out", async () => {
    let resolveOlder!: (r: Response) => void;
    const olderResponse = new Promise<Response>((resolve) => {
      resolveOlder = resolve;
    });
    let permissionsCallCount = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL) => {
        const u = String(url);
        if (u.includes("/api/auth/logout")) return Promise.resolve(jsonResponse(undefined));
        if (u.includes("/api/auth/permissions")) {
          permissionsCallCount += 1;
          // Mount's own refresh() (call 1) resolves immediately, signed in; a manually-triggered
          // second refresh() (call 2) is left pending until after logout() completes.
          if (permissionsCallCount === 1) {
            return Promise.resolve(
              jsonResponse({ username: "u", role: "Admin", permissions: ["ManageUsers"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() })
            );
          }
          return olderResponse;
        }
        return Promise.resolve(jsonResponse({}));
      })
    );

    function ProbeWithControls() {
      const { refresh, logout } = useAuth();
      return (
        <div>
          <Probe />
          <button onClick={() => refresh()}>refresh</button>
          <button onClick={() => logout()}>logout</button>
        </div>
      );
    }

    render(
      <AuthProvider>
        <ProbeWithControls />
      </AuthProvider>
    );

    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));

    // Kick off a second refresh() that will stay pending through the logout below.
    act(() => {
      screen.getByText("refresh").click();
    });

    await act(async () => {
      screen.getByText("logout").click();
      await Promise.resolve();
      await Promise.resolve();
    });
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));

    // The refresh() started before logout() now resolves with an old, still-signed-in body - it
    // must not undo the explicit sign-out.
    await act(async () => {
      resolveOlder(
        jsonResponse({ username: "u", role: "Admin", permissions: ["ManageUsers"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() })
      );
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(screen.getByTestId("kind")).toHaveTextContent("signed-out");
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

// ---------- ALV-N020: only an authentication decision ends a session (F-155), and why it ended is known (F-007, F-037) ----------

function SessionProbe() {
  const { state, connectionLost, endedReason, expiredUsername, identityEpoch, logout } = useAuth();
  return (
    <div>
      <span data-testid="kind">{state.kind}</span>
      <span data-testid="user">{state.kind === "signed-in" ? state.username : ""}</span>
      <span data-testid="lost">{String(connectionLost)}</span>
      <span data-testid="ended">{String(endedReason)}</span>
      <span data-testid="expired-user">{String(expiredUsername)}</span>
      <span data-testid="epoch">{identityEpoch}</span>
      <button onClick={() => void logout()}>out</button>
    </div>
  );
}

const signedInAs = (username: string) =>
  jsonResponse({ username, role: "Admin", permissions: [], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() });

/** Answers the permissions check from `next()`; everything else (csrf, logout) succeeds. */
function stubPermissions(next: () => Response | Promise<Response>) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: RequestInfo | URL) => {
      const u = String(url);
      if (u.includes("/api/auth/permissions")) return Promise.resolve(next());
      if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "t" }));
      return Promise.resolve(jsonResponse({}));
    })
  );
}

async function revalidate() {
  await act(async () => {
    window.dispatchEvent(new Event("focus"));
  });
}

describe("AuthContext session and outage states (ALV-N020)", () => {
  it.each([
    ["a 502 from the dev proxy", () => new Response("Bad gateway", { status: 502 })],
    ["a 503", () => jsonResponse({ error: "unavailable" }, 503)],
    ["a 500 from the API itself", () => jsonResponse({ error: "internal_error" }, 500)],
    ["no connection at all", () => Promise.reject(new TypeError("Failed to fetch"))],
  ])("keeps a signed-in session through %s, flags the lost connection, and clears the flag when the server answers again", async (_label, failure) => {
    let mode: "ok" | "fail" = "ok";
    stubPermissions(() => (mode === "ok" ? signedInAs("dentist1") : (failure() as Response)));
    render(<AuthProvider><SessionProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));

    mode = "fail";
    await revalidate();
    await waitFor(() => expect(screen.getByTestId("lost")).toHaveTextContent("true"));
    expect(screen.getByTestId("kind")).toHaveTextContent("signed-in");                                // the shell (and any unsaved form) stays
    expect(screen.getByTestId("ended")).toHaveTextContent("null");

    mode = "ok";
    await revalidate();
    await waitFor(() => expect(screen.getByTestId("lost")).toHaveTextContent("false"));
    expect(screen.getByTestId("user")).toHaveTextContent("dentist1");
  });

  it("a 401 on revalidation ends the session as expired and remembers whose it was", async () => {
    let session = true;
    stubPermissions(() => (session ? signedInAs("dentist1") : jsonResponse({ error: "unauthorized" }, 401)));
    render(<AuthProvider><SessionProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    const epoch = Number(screen.getByTestId("epoch").textContent);

    session = false;
    await revalidate();
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(screen.getByTestId("ended")).toHaveTextContent("expired");
    expect(screen.getByTestId("expired-user")).toHaveTextContent("dentist1");
    expect(Number(screen.getByTestId("epoch").textContent)).toBe(epoch + 1);
  });

  it("an explicit sign-out ends the session as signed-out and keeps no identity to return to", async () => {
    stubPermissions(() => signedInAs("frontdesk1"));
    render(<AuthProvider><SessionProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));

    await act(async () => {
      screen.getByText("out").click();
    });
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(screen.getByTestId("ended")).toHaveTextContent("signed-out");
    expect(screen.getByTestId("expired-user")).toHaveTextContent("null");
  });

  it("a first visit with no session is neutral: no ended reason", async () => {
    stubPermissions(() => jsonResponse({ error: "unauthorized" }, 401));
    render(<AuthProvider><SessionProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(screen.getByTestId("ended")).toHaveTextContent("null");
  });

  it("a first load during an outage is neutral and signs straight in when the server returns with a still-valid session", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let up = false;
    stubPermissions(() => (up ? signedInAs("admin") : new Response("Bad gateway", { status: 502 })));
    render(<AuthProvider><SessionProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(screen.getByTestId("lost")).toHaveTextContent("true");
    expect(screen.getByTestId("ended")).toHaveTextContent("null");

    up = true;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(CONNECTION_RETRY_INTERVAL_MS);
    });
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    expect(screen.getByTestId("lost")).toHaveTextContent("false");
  });

  it("a different person found by revalidation (a sign-in in another tab) advances the identity counter", async () => {
    let who = "frontdesk1";
    stubPermissions(() => signedInAs(who));
    render(<AuthProvider><SessionProbe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("user")).toHaveTextContent("frontdesk1"));
    const epoch = Number(screen.getByTestId("epoch").textContent);

    who = "admin";
    await revalidate();
    await waitFor(() => expect(screen.getByTestId("user")).toHaveTextContent("admin"));
    expect(Number(screen.getByTestId("epoch").textContent)).toBe(epoch + 1);
  });
});
