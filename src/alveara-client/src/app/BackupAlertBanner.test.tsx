import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { BackupAlertBanner } from "./BackupAlertBanner";

/** ALV-N026 (PD-25): the administrators' banner for unacknowledged backup and restore failures, against a scripted API. */
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const alert = (id: string, kind = "BackupFailed") => ({ id, kind, createdAtUtc: "2030-03-10T10:00:00Z", delivery: "Delivered", deliveryFailureCode: null, acknowledged: false, acknowledgedAtUtc: null });

afterEach(() => vi.unstubAllGlobals());

function stub(alerts: unknown, ackStatus = 200) {
  const calls: { url: string; method: string; headers: Record<string, string> }[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    calls.push({ url: String(url), method: init?.method ?? "GET", headers: (init?.headers ?? {}) as Record<string, string> });
    if (String(url).includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (String(url).includes("/acknowledge")) return Promise.resolve(ackStatus === 200 ? json({ ...alert("a1"), acknowledged: true }) : json({ error: "alert_not_found", message: "That backup alert was not found." }, ackStatus));
    return Promise.resolve(json(alerts));
  }));
  return calls;
}

describe("BackupAlertBanner", () => {
  it("shows nothing when there is nothing to acknowledge, or when the answer is not a list", async () => {
    const calls = stub([]);
    const { unmount } = render(<MemoryRouter><BackupAlertBanner /></MemoryRouter>);
    await waitFor(() => expect(calls.length).toBeGreaterThan(0));
    expect(screen.queryByRole("alert", { name: "Backup alerts" })).toBeNull();
    unmount();
    const again = stub({ status: "ok" });
    render(<MemoryRouter><BackupAlertBanner /></MemoryRouter>);
    await waitFor(() => expect(again.length).toBeGreaterThan(0));
    expect(screen.queryByRole("alert", { name: "Backup alerts" })).toBeNull();
  });

  it("lists each failure, says acknowledging does not clear System Status, and acknowledges one with a CSRF token", async () => {
    const calls = stub([alert("a1"), alert("a2", "RestoreDrillFailed")]);
    render(<MemoryRouter><BackupAlertBanner /></MemoryRouter>);
    const banner = await screen.findByRole("alert", { name: "Backup alerts" });
    expect(banner).toHaveTextContent("2 backup or restore problems need attention.");
    expect(banner).toHaveTextContent(/System Status keeps warning until a later backup or restore check succeeds/);
    expect(banner).toHaveTextContent("A restore drill failed");
    await userEvent.setup().click(screen.getAllByRole("button", { name: /^Acknowledge:/ })[0]);
    await waitFor(() => expect(screen.getAllByRole("button", { name: /^Acknowledge:/ })).toHaveLength(1));
    const post = calls.find((c) => c.method === "POST" && c.url.endsWith("/api/backup/alerts/a1/acknowledge"))!;
    expect(post.headers["X-CSRF-Token"]).toBe("csrf");
  });

  it("keeps the alert and says so when acknowledging fails", async () => {
    stub([alert("a1")], 404);
    render(<MemoryRouter><BackupAlertBanner /></MemoryRouter>);
    await userEvent.setup().click(await screen.findByRole("button", { name: /^Acknowledge:/ }));
    expect(await screen.findByText("That backup alert was not found.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Acknowledge:/ })).toBeInTheDocument();
  });
});
