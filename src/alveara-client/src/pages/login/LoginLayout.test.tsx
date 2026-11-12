import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { LoginLayout } from "./LoginLayout";
import { backgroundCss, BUNDLED_BACKGROUNDS } from "./loginBackgrounds";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const presentation = (over: Record<string, unknown> = {}) => ({ background: { kind: "bundled", key: "teal-doodle" }, displayMode: "Tile", footerNotice: null, ...over });

afterEach(() => vi.unstubAllGlobals());

function renderLayout() {
  return render(<LoginLayout><h1>Sign in</h1><button type="submit">Sign in</button></LoginLayout>);
}
const backdrop = () => screen.getByTestId("signin-backdrop");

describe("LoginLayout (ALV-N020-C01)", () => {
  it("renders the sign-in content at once with the bundled teal tile and the DentalKeep logo, before the presentation answers", () => {
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise(() => {})));                              // never answers
    renderLayout();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeTruthy();
    expect(screen.getByRole("img", { name: "DentalKeep" })).toBeTruthy();
    expect(backdrop().getAttribute("aria-hidden")).toBe("true");
    expect(backdrop().dataset.displayMode).toBe("Tile");
    expect(backdrop().style.backgroundImage).toContain("tile-teal-doodle");
    expect(screen.queryByRole("contentinfo")).toBeNull();
  });

  it("applies a configured bundled background and display mode", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(presentation({ background: { kind: "bundled", key: "blue-operatory" }, displayMode: "Fill" }))));
    renderLayout();
    await waitFor(() => expect(backdrop().style.backgroundImage).toContain("tile-blue-operatory"));
    expect(backdrop().dataset.displayMode).toBe("Fill");
    expect(backdrop().style.backgroundSize).toBe("cover");
  });

  it("uses an added background from our own image route in the configured mode", async () => {
    const url = `/api/public/login-background/${"a".repeat(64)}.jpg`;
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(presentation({ background: { kind: "added", url }, displayMode: "Stretch" }))));
    renderLayout();
    await waitFor(() => expect(backdrop().style.backgroundImage).toContain(url));
    expect(backdrop().style.backgroundSize).toBe("100% 100%");
  });

  it.each([
    ["an unknown bundled key", presentation({ background: { kind: "bundled", key: "not-in-the-catalog" }, displayMode: "Fill" })],
    ["an added URL that is not our image route", presentation({ background: { kind: "added", url: "https://evil.example/x.png" }, displayMode: "Fill" })],
    ["an unknown display mode", presentation({ displayMode: "Centered" })],
  ])("keeps the teal tile default for %s", async (_label, body) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(body)));
    renderLayout();
    await new Promise((r) => setTimeout(r, 20));
    expect(backdrop().style.backgroundImage).toContain("tile-teal-doodle");
    expect(backdrop().dataset.displayMode).toBe("Tile");
  });

  it("keeps the default and shows no footer when the server cannot be reached", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
    renderLayout();
    await new Promise((r) => setTimeout(r, 20));
    expect(backdrop().style.backgroundImage).toContain("tile-teal-doodle");
    expect(screen.queryByRole("contentinfo")).toBeNull();
    expect(screen.getByRole("button", { name: "Sign in" })).toBeTruthy();
  });

  it("renders the footer notice as plain text, markup and all, keeping its line breaks", async () => {
    const notice = "Authorized use only.\n<img src=x onerror=alert(1)><script>alert(1)</script>";
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(presentation({ footerNotice: notice }))));
    const { container } = renderLayout();
    const footer = await screen.findByRole("contentinfo");
    expect(footer.textContent).toBe(notice);
    expect(container.querySelector("script")).toBeNull();
    expect(footer.querySelector("img")).toBeNull();
  });

  it("has no footer at all when the notice is turned off", async () => {
    const fetchMock = vi.fn().mockResolvedValue(json(presentation({ footerNotice: null, displayMode: "Fill" })));
    vi.stubGlobal("fetch", fetchMock);
    const { container } = renderLayout();
    await waitFor(() => expect(backdrop().dataset.displayMode).toBe("Fill"));
    expect(container.querySelector("footer")).toBeNull();
  });

  it("passes the automated accessibility check with a notice shown", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(presentation({ footerNotice: "Authorized use only." }))));
    const { container } = renderLayout();
    await screen.findByRole("contentinfo");
    expect((await axe(container)).violations).toEqual([]);
  });
});

describe("backgroundCss (ALV-N020-C01 display modes)", () => {
  it("Tile repeats at natural size, Stretch fills exactly, Fill covers and centres; each keeps its fallback colour", () => {
    expect(backgroundCss("/x.jpg", "Tile", "#000")).toMatchObject({ backgroundRepeat: "repeat", backgroundSize: "auto", backgroundColor: "#000" });
    expect(backgroundCss("/x.jpg", "Stretch", "#000")).toMatchObject({ backgroundRepeat: "no-repeat", backgroundSize: "100% 100%" });
    expect(backgroundCss("/x.jpg", "Fill", "#000")).toMatchObject({ backgroundRepeat: "no-repeat", backgroundSize: "cover", backgroundPosition: "center" });
  });

  it("bundles exactly the five owner backgrounds, teal doodle first (the deployment default)", () => {
    expect(BUNDLED_BACKGROUNDS.map((b) => b.key)).toEqual(["teal-doodle", "blue-doodle", "blue-operatory", "violet-operatory", "neon-doodle"]);
  });
});
