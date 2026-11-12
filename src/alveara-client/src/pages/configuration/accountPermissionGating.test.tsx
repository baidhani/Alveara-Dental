import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { ConfigurationHubPage } from "../ConfigurationHubPage";
import { NotificationProvider } from "../../components/Notification";
import { AuthProvider, useAuth } from "../../contexts/AuthContext";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

function stub(accountPermissions: string[] | undefined) {
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => {
    const u = String(url);
    if (u.includes("/api/auth/permissions"))
      return Promise.resolve(json({ username: "owner", role: "Admin", permissions: ["ManagePracticeConfiguration"], accountPermissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
    if (u.includes("/api/config/practice")) return Promise.resolve(json({ id: null, name: null, phone: null, addressLine: null, configured: false, rowVersion: null }));
    return Promise.resolve(json([]));
  }));
}

function Probe() {
  const { hasPermission, state } = useAuth();
  return <span data-testid="probe">{state.kind}:{String(hasPermission("ManageLoginPresentation"))}</span>;
}

describe("account-scoped permissions on the client (ALV-N020-C01)", () => {
  it("an account permission counts for hasPermission; an older server without the field still works", async () => {
    stub(["ManageLoginPresentation"]);
    const { unmount } = render(<AuthProvider><Probe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("probe").textContent).toBe("signed-in:true"));
    unmount();

    stub(undefined);
    render(<AuthProvider><Probe /></AuthProvider>);
    await waitFor(() => expect(screen.getByTestId("probe").textContent).toBe("signed-in:false"));
  });

  it.each([
    [["ManageLoginPresentation"], true],
    [[], false],
  ])("the Sign-in screen tab is offered only to the holder (%j -> %s)", async (accountPermissions, shown) => {
    stub(accountPermissions);
    render(<AuthProvider><NotificationProvider><ConfigurationHubPage /></NotificationProvider></AuthProvider>);
    await screen.findByRole("tab", { name: "Practice" });
    await waitFor(() => expect(screen.queryByRole("tab", { name: "Sign-in screen" }) !== null).toBe(shown));
  });
});
