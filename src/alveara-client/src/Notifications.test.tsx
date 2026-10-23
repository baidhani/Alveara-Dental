import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { NotificationBell, NOTIFICATION_POLL_MS } from "./components/NotificationBell";
import { ComposeMessage } from "./pages/notifications/ComposeMessage";
import { NotificationsPage } from "./pages/notifications/NotificationsPage";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const summary = (unread: number, important = 0, urgent = 0) => ({ unread, unreadImportant: important, unreadUrgent: urgent, newestBannerAtUtc: null });

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

describe("ALV-N015 notification bell", () => {
  it("shows the unread count and no banner for ordinary notices", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(summary(3))));
    const { container } = render(<MemoryRouter><NotificationBell /></MemoryRouter>);
    expect(await screen.findByRole("link", { name: "Notifications, 3 unread" })).toBeTruthy();
    expect(screen.queryByText(/important notification|urgent notification/)).toBeNull();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("shows a generic banner for important and an alert for urgent", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(summary(2, 1, 1))));
    render(<MemoryRouter><NotificationBell /></MemoryRouter>);
    expect((await screen.findByRole("alert")).textContent).toContain("urgent notification");
  });

  it("can be dismissed, and comes back when a newer important notice arrives", async () => {
    const answers = [{ ...summary(1, 1), newestBannerAtUtc: "2026-10-01T10:00:00Z" }, { ...summary(2, 2), newestBannerAtUtc: "2026-10-01T10:00:00Z" }, { ...summary(3, 3), newestBannerAtUtc: "2026-10-01T11:00:00Z" }];
    let i = 0;
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(json(answers[Math.min(i++, 2)]))));
    render(<MemoryRouter><NotificationBell /></MemoryRouter>);
    await userEvent.click(await screen.findByRole("button", { name: "Dismiss" }));
    expect(screen.queryByText(/important notification/)).toBeNull();
    await act(async () => { window.dispatchEvent(new Event("focus")); });
    await waitFor(() => expect(i).toBe(2));
    expect(screen.queryByText(/important notification/)).toBeNull();            // the same newest notice stays dismissed
    await act(async () => { window.dispatchEvent(new Event("focus")); });
    expect(await screen.findByText(/important notification/)).toBeTruthy();       // a newer one shows again
  });

  it("says it could not check instead of showing zero when the server is unreachable", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("offline")));
    render(<MemoryRouter><NotificationBell /></MemoryRouter>);
    expect(await screen.findByRole("link", { name: "Notifications, could not check" })).toBeTruthy();
    expect(screen.queryByText("0")).toBeNull();
  });

  it("checks again every minute and when the window regains focus", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fetchMock = vi.fn().mockResolvedValue(json(summary(0)));
    vi.stubGlobal("fetch", fetchMock);
    render(<MemoryRouter><NotificationBell /></MemoryRouter>);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    await act(async () => { vi.advanceTimersByTime(NOTIFICATION_POLL_MS); });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    await act(async () => { window.dispatchEvent(new Event("focus")); });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
  });
});

describe("ALV-N015 compose", () => {
  function stub(sendStatuses: number[]) {
    const keys: string[] = [];
    let n = 0;
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      const u = String(url);
      if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
      if (u.includes("/api/messages/recipients")) return Promise.resolve(json([{ userId: "u1", name: "Pat Desk", role: "FrontDesk" }]));
      if (u === "/api/messages") {
        keys.push((init?.headers as Record<string, string>)["Idempotency-Key"]);
        const status = sendStatuses[n++] ?? 201;
        return Promise.resolve(status === 201
          ? json({ message: { id: "m1" }, replayed: false }, 201)
          : json({ error: "message_invalid", message: "Fix the message.", problems: [{ field: "body", code: "body_required", message: "Write a message." }] }, status));
      }
      return Promise.resolve(json({}));
    }));
    return keys;
  }

  it("names the wrong field and reuses the same idempotency key for a retry of the same attempt", async () => {
    const keys = stub([400, 201]);
    const onSent = vi.fn();
    render(<ComposeMessage onSent={onSent} />);
    await userEvent.click(await screen.findByLabelText(/Pat Desk/));
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(await screen.findByText("Write a message.")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(onSent).toHaveBeenCalledTimes(1));
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
  });

  it("starts a new attempt (a new key) once the text is changed", async () => {
    const keys = stub([400, 201]);
    render(<ComposeMessage onSent={vi.fn()} />);
    await userEvent.click(await screen.findByLabelText(/Pat Desk/));
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    await screen.findByText("Write a message.");
    await userEvent.type(screen.getByLabelText(/Message \(plain text/), "Hello");
    await userEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(keys).toHaveLength(2));
    expect(keys[0]).not.toBe(keys[1]);
  });
});

describe("ALV-N015 notifications list", () => {
  it("never lets a slow answer for the Unread tab replace the History tab the person has since opened", async () => {
    const item = (id: string, sender: string, readAtUtc: string | null) => ({ id, kind: "message.received", priority: "Normal", createdAtUtc: "2026-10-01T10:00:00Z", readAtUtc, messageId: "m", senderName: sender, hasPatientLink: false });
    let releaseUnread: (r: Response) => void = () => {};
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => {
      if (String(url).includes("status=unread")) return new Promise<Response>((resolve) => { releaseUnread = resolve; });
      return Promise.resolve(json({ items: [item("2", "Reader Rita", "2026-10-01T11:00:00Z")], total: 1, page: 1, pageSize: 25 }));
    }));
    render(<MemoryRouter><NotificationsPage /></MemoryRouter>);
    await userEvent.click(screen.getByRole("tab", { name: "History" }));
    expect(await screen.findByText(/Reader Rita/)).toBeTruthy();
    await act(async () => { releaseUnread(json({ items: [item("1", "Unread Ula", null)], total: 1, page: 1, pageSize: 25 })); });
    expect(screen.getByText(/Reader Rita/)).toBeTruthy();
    expect(screen.queryByText(/Unread Ula/)).toBeNull();
  });
});
