import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import { AuthProvider, useAuth } from "./AuthContext";
import { usePracticeZone } from "./authContextObject";
import { signedInSession } from "../test/signedInSession";

// ALV-N021 A3 (briefing 4.1-1, 4.1-4, 4.1-5, BR-3, BR-5): the practice zone comes only from the newest authenticated session response. A missing or
// unusable zone is null (screens show "unavailable", never the browser zone); a newer response with a different usable zone replaces it and is flagged
// once as an observed change; signing out forgets it.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

function Probe() {
  const { state, practiceZoneChangedTo, dismissPracticeZoneChange, refresh, logout } = useAuth();
  const zone = usePracticeZone();
  return (
    <div>
      <span data-testid="kind">{state.kind}</span>
      <span data-testid="zone">{zone ?? "none"}</span>
      <span data-testid="changed">{practiceZoneChangedTo ?? "no"}</span>
      <button onClick={() => void refresh()}>refresh</button>
      <button onClick={dismissPracticeZoneChange}>dismiss</button>
      <button onClick={() => void logout()}>logout</button>
    </div>
  );
}

/** A fetch that answers the session check with the given responses in turn (the last one repeats), and logout with 204. */
function stubSessions(...bodies: object[]) {
  let i = 0;
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL) => {
    const u = String(url);
    if (u.includes("/api/auth/permissions")) return Promise.resolve(json(bodies[Math.min(i++, bodies.length - 1)]));
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "t" }));
    if (u.includes("/api/auth/logout")) return Promise.resolve(new Response(null, { status: 204 }));
    return Promise.resolve(json({}));
  }));
}

const show = () => render(<AuthProvider><Probe /></AuthProvider>);

afterEach(() => vi.unstubAllGlobals());

describe("the practice zone in the session (ALV-N021 A3)", () => {
  it("is taken from the authenticated response", async () => {
    stubSessions(signedInSession({ username: "u", role: "Billing", permissions: [] }));
    show();
    await waitFor(() => expect(screen.getByTestId("zone")).toHaveTextContent("America/Chicago"));
    expect(screen.getByTestId("changed")).toHaveTextContent("no");
  });

  it("is null when the response has none, or one this browser cannot use", async () => {
    stubSessions(signedInSession({ username: "u", role: "Billing", permissions: [], practiceTimeZoneId: null }));
    show();
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-in"));
    expect(screen.getByTestId("zone")).toHaveTextContent("none");
    vi.unstubAllGlobals();

    stubSessions(signedInSession({ username: "u2", role: "Billing", permissions: [], practiceTimeZoneId: "Central Standard Time" }));
    show();
    await waitFor(() => expect(screen.getAllByTestId("kind")[1]).toHaveTextContent("signed-in"));
    expect(screen.getAllByTestId("zone")[1]).toHaveTextContent("none");
  });

  it("is replaced by a newer response; a different usable zone is flagged once, an identical one is not", async () => {
    stubSessions(
      signedInSession({ username: "u", role: "Billing", permissions: [] }),
      signedInSession({ username: "u", role: "Billing", permissions: [] }),
      signedInSession({ username: "u", role: "Billing", permissions: [], practiceTimeZoneId: "America/Denver" }),
    );
    show();
    await waitFor(() => expect(screen.getByTestId("zone")).toHaveTextContent("America/Chicago"));
    await act(async () => { screen.getByText("refresh").click(); });
    await waitFor(() => expect(screen.getByTestId("zone")).toHaveTextContent("America/Chicago"));
    expect(screen.getByTestId("changed")).toHaveTextContent("no");   // the same zone again: nothing to say

    await act(async () => { screen.getByText("refresh").click(); });
    await waitFor(() => expect(screen.getByTestId("zone")).toHaveTextContent("America/Denver"));
    expect(screen.getByTestId("changed")).toHaveTextContent("America/Denver");
    await act(async () => { screen.getByText("dismiss").click(); });
    expect(screen.getByTestId("changed")).toHaveTextContent("no");
  });

  it("is forgotten when the session ends, so the next sign-in is not reported as a change", async () => {
    stubSessions(signedInSession({ username: "u", role: "Billing", permissions: [] }));
    show();
    await waitFor(() => expect(screen.getByTestId("zone")).toHaveTextContent("America/Chicago"));
    await act(async () => { screen.getByText("logout").click(); });
    await waitFor(() => expect(screen.getByTestId("kind")).toHaveTextContent("signed-out"));
    expect(screen.getByTestId("zone")).toHaveTextContent("none");
    expect(screen.getByTestId("changed")).toHaveTextContent("no");
  });
});
