import { describe, it, expect, vi, afterEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";
import { DISCARD_PROMPT } from "./hooks/useUnsavedChangesWarning";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const practice = { id: null, name: null, phone: null, addressLine: null, timeZoneId: "America/Chicago", currency: "USD", configured: false, rowVersion: null };

function stubFetch(overrides: Record<string, (init?: RequestInit) => Response> = {}) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
      const u = String(url);
      for (const [fragment, handler] of Object.entries(overrides)) if (u.includes(fragment)) return Promise.resolve(handler(init));
      if (u.includes("/api/auth/permissions"))
        return Promise.resolve(jsonResponse({ username: "office-1", role: "OfficeManager", permissions: ["ManagePracticeConfiguration"], sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
      if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
      if (u.includes("/api/config/practice")) return Promise.resolve(jsonResponse(practice));
      if (u.includes("/api/health")) return Promise.resolve(jsonResponse({ status: "ok" }));
      return Promise.resolve(jsonResponse([]));
    })
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openConfigurationWithDraft() {
  window.history.pushState({}, "", "/");
  render(<App />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole("link", { name: "Practice Configuration" }));
  await user.type(await screen.findByLabelText("Practice name"), "Half typed");
  return user;
}

describe("Unsaved-change protection survives main navigation (ALV-N003 R02)", () => {
  it("asks before a shell link discards a dirty draft, and declining keeps the page and the draft", async () => {
    stubFetch();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const user = await openConfigurationWithDraft();

    await user.click(screen.getByRole("link", { name: "Dashboard" }));

    await waitFor(() => expect(confirm).toHaveBeenCalledWith(DISCARD_PROMPT));
    expect(screen.getByRole("heading", { name: "Practice configuration" })).toBeInTheDocument();
    expect(screen.getByLabelText("Practice name")).toHaveValue("Half typed");

    confirm.mockReturnValue(true);
    await user.click(screen.getByRole("link", { name: "Dashboard" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument());
  });

  it("does not prompt when nothing is dirty", async () => {
    stubFetch();
    const confirm = vi.spyOn(window, "confirm");
    window.history.pushState({}, "", "/");
    render(<App />);
    const user = userEvent.setup();
    await user.click(await screen.findByRole("link", { name: "Practice Configuration" }));
    await screen.findByLabelText("Practice name");
    await user.click(screen.getByRole("link", { name: "Dashboard" }));
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument());
    expect(confirm).not.toHaveBeenCalled();
  });

  it("also guards browser back: declining stays on the configuration page with the draft intact", async () => {
    stubFetch();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await openConfigurationWithDraft();

    act(() => window.history.back());

    await waitFor(() => expect(confirm).toHaveBeenCalledWith(DISCARD_PROMPT));
    await waitFor(() => expect(window.location.pathname).toBe("/admin/configuration"));
    expect(screen.getByLabelText("Practice name")).toHaveValue("Half typed");
  });

  it("asks before sign-out discards a draft, and declining keeps the user signed in", async () => {
    stubFetch();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const user = await openConfigurationWithDraft();

    await user.click(screen.getByRole("button", { name: /sign out/i }));

    expect(confirm).toHaveBeenCalledWith(DISCARD_PROMPT);
    expect(screen.getByLabelText("Practice name")).toHaveValue("Half typed");
  });

  it("never holds a revoked session's draft hostage: a 401 clears the protected UI and redirects without a prompt", async () => {
    stubFetch({ "/api/config/practice": (init) => (init?.method === "PUT" ? jsonResponse({ error: "unauthorized" }, 401) : jsonResponse(practice)) });
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); // would refuse - must not even be asked
    const user = await openConfigurationWithDraft();

    await user.click(screen.getByRole("button", { name: "Save practice information" }));

    await waitFor(() => expect(screen.getByText(/your session expired/i)).toBeInTheDocument());
    expect(screen.queryByLabelText("Practice name")).not.toBeInTheDocument();
    expect(confirm).not.toHaveBeenCalled();
  });
});
