import { describe, it, expect, vi, afterEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AppShell } from "./AppShell";
import { AuthProvider, useAuth } from "../contexts/AuthContext";
import { signedInSession } from "../test/signedInSession";
import { showInstant } from "../time/practiceTime";

// ALV-N021 A3 (briefing 4.1-4, 4.1-5): the shell says when no time can be shown, and when a newer session answer changed the practice zone; screens render
// every instant with the session zone and show "Time unavailable" without one - never a browser-zone time.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const INSTANT = "2030-01-15T03:30:00Z";   // 9:30 PM on the 14th in Chicago; 8:30 PM in Denver

function stubSessions(...bodies: object[]) {
  let i = 0;
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL) => {
    if (String(url).includes("/api/auth/permissions")) return Promise.resolve(json(bodies[Math.min(i++, bodies.length - 1)]));
    return Promise.resolve(json({ status: "ok" }));
  }));
}

function Page() {
  const { refresh } = useAuth();
  return (
    <div>
      <p data-testid="when">{showInstant(INSTANT)}</p>
      <button onClick={() => void refresh()}>refresh</button>
    </div>
  );
}

const renderShell = () => render(
  <MemoryRouter initialEntries={["/"]}>
    <AuthProvider>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<Page />} />
        </Route>
      </Routes>
    </AuthProvider>
  </MemoryRouter>
);
const plain = (s: string | null) => s?.replace(/ /g, " ") ?? "";

afterEach(() => vi.unstubAllGlobals());

describe("the practice zone in the shell (ALV-N021 A3)", () => {
  it("renders a screen's instants in the session zone, with no banner", async () => {
    stubSessions(signedInSession({ username: "u", role: "Billing", permissions: [] }));
    renderShell();
    await waitFor(() => expect(plain(screen.getByTestId("when").textContent)).toBe("1/14/2030, 9:30:00 PM"));
    expect(screen.queryByText(/Times cannot be shown/)).not.toBeInTheDocument();
  });

  it("without a usable zone shows 'Time unavailable' and says why, never a browser-zone time", async () => {
    stubSessions(signedInSession({ username: "u", role: "Billing", permissions: [], practiceTimeZoneId: null }));
    renderShell();
    expect(await screen.findByText("Times cannot be shown: the practice time zone is not available.")).toBeInTheDocument();
    expect(screen.getByTestId("when")).toHaveTextContent("Time unavailable");
  });

  it("re-renders in a different zone from a newer session answer and says so once, dismissably", async () => {
    stubSessions(
      signedInSession({ username: "u", role: "Billing", permissions: [] }),
      signedInSession({ username: "u", role: "Billing", permissions: [], practiceTimeZoneId: "America/Denver" }),
    );
    renderShell();
    await waitFor(() => expect(plain(screen.getByTestId("when").textContent)).toBe("1/14/2030, 9:30:00 PM"));
    await act(async () => { screen.getByText("refresh").click(); });
    expect(await screen.findByText(/Your session was refreshed; times are shown in America\/Denver\./)).toBeInTheDocument();
    expect(plain(screen.getByTestId("when").textContent)).toBe("1/14/2030, 8:30:00 PM");
    await act(async () => { screen.getByRole("button", { name: "Dismiss" }).click(); });
    expect(screen.queryByText(/Your session was refreshed/)).not.toBeInTheDocument();
  });
});
