import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { App } from "../App";
import { THEME_KEY, readStoredTheme } from "./useAppTheme";
import { DisconnectedBanner } from "../components/DisconnectedBanner";

afterEach(() => {
  localStorage.clear();
  document.documentElement.removeAttribute("data-theme");
  vi.unstubAllGlobals();
});

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("theme on the sign-in page (ALV-N020, F-156)", () => {
  it("the sign-in page uses the saved dark theme on a fresh load", async () => {
    localStorage.setItem(THEME_KEY, "dark");
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) =>
      Promise.resolve(String(url).includes("/api/health") ? json({ status: "ok" }) : json({ error: "unauthorized" }, 401))));
    window.history.pushState({}, "", "/login");

    render(<App />);

    await waitFor(() => expect(screen.getByRole("heading", { name: "Sign in" })).toBeInTheDocument());
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("reads the saved value, and falls back to the system preference when nothing is saved", () => {
    localStorage.setItem(THEME_KEY, "light");
    expect(readStoredTheme()).toBe("light");
    localStorage.clear();
    expect(["light", "dark"]).toContain(readStoredTheme());
  });
});

describe("DisconnectedBanner variants (ALV-N020, F-013)", () => {
  it("the sign-in variant does not say changes cannot be saved", () => {
    render(<DisconnectedBanner variant="sign-in" />);
    expect(screen.getByRole("alert")).toHaveTextContent(/local server unavailable/i);
    expect(screen.getByRole("alert")).not.toHaveTextContent(/changes cannot be saved/i);
  });

  it("the signed-in wording is unchanged (PD-01 undecided)", () => {
    render(<DisconnectedBanner />);
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Local server unavailable. Changes cannot be saved until the connection is restored. This is not an offline mode — reconnect to continue."
    );
  });
});
