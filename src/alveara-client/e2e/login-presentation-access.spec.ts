import fs from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-N020-C01 R02 (review item C01-1), mocked API, real browser. The briefing's two accessibility modes, proven in a real Chromium:
// - 200% zoom. A browser zoomed to 200% on a 1280x800 (or 1920x1080) window lays the page out in a 640x400 (or 960x540) CSS-pixel viewport; that is the
//   viewport used here (WCAG 1.4.4 / 1.4.10 reflow equivalence). Checks: no horizontal scrolling, keyboard order Username -> Password -> Sign in, Sign in and the
//   footer notice reachable and not obscured, the sign-in failure message visible, axe clean (all rules).
// - Forced colours / high contrast (Playwright forcedColors: "active" = Chromium's real forced-colours mode). Checks: the decorative background image is suppressed,
//   the solid panel keeps a visible edge, labels, fields, buttons, notice and the status/error message are readable in system colours, focus stays visible.
// Screenshots are kept in A11Y_OUT (or the test output folder). Runs in the desktop project only (it sets its own viewports).

const NOTICE = "Please ensure you have taken appropriate precautions.\n\nAll access to this system will be tracked and monitored.";
const ZOOM_VIEWPORTS = [{ width: 640, height: 400, label: "200% of 1280x800" }, { width: 960, height: 540, label: "200% of 1920x1080" }];

async function signedOut(page: Page, theme: "light" | "dark" = "light") {
  await page.route("**/api/auth/permissions", (r: Route) => r.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ error: "unauthenticated" }) }));
  await page.route("**/api/health", (r: Route) => r.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
  await page.route("**/api/public/login-presentation", (r: Route) =>
    r.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ background: { kind: "bundled", key: "blue-operatory" }, displayMode: "Tile", footerNotice: NOTICE }) }));
  await page.route("**/api/auth/login", (r: Route) => r.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ error: "invalid_credentials" }) }));
  await page.addInitScript((t) => {
    localStorage.setItem("alveara-theme", t);
    document.addEventListener("DOMContentLoaded", () => document.documentElement.setAttribute("data-theme", t));
  }, theme);
}

async function failSignIn(page: Page) {
  await page.getByLabel("Username").fill("nobody");
  await page.getByLabel("Password").fill("wrong-password-1");
  await page.getByRole("button", { name: /sign in/i }).click();
  await expect(page.locator(".alv-login__banner--danger")).toBeVisible();
}

const noHorizontalScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth && document.body.scrollWidth <= window.innerWidth);

/** The element at the centre of `locator` is the element itself or inside it: nothing is painted over it. */
async function notObscured(page: Page, locator: ReturnType<Page["locator"]>) {
  await locator.scrollIntoViewIfNeeded();
  const box = await locator.boundingBox();
  expect(box, "the element has a box").not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(page.viewportSize()!.width + 1);
  const hit = await locator.evaluate((el) => {
    const r = el.getBoundingClientRect();
    const top = document.elementFromPoint(r.left + r.width / 2, r.top + Math.min(r.height / 2, 8));
    return !!top && (el === top || el.contains(top));
  });
  expect(hit, "nothing covers the element").toBe(true);
}

test.beforeEach(({ browserName }, testInfo) => test.skip(browserName !== "chromium" || testInfo.project.name !== "desktop", "sets its own viewports; forced-colours emulation is Chromium"));

for (const vp of ZOOM_VIEWPORTS) {
  for (const theme of ["light", "dark"] as const) {
    test(`200% zoom (${vp.label}, ${theme}): no horizontal scroll, keyboard order, Sign in and the notice reachable, failure message visible, axe clean`, async ({ page }, testInfo) => {
      const out = process.env.A11Y_OUT ?? testInfo.outputDir;
      fs.mkdirSync(out, { recursive: true });
      await signedOut(page, theme);
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto("/login");
      await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
      expect(await noHorizontalScroll(page), "no horizontal scrolling").toBe(true);

      const order: string[] = [];                                                                                          // keyboard-only, in order
      for (let i = 0; i < 6; i++) {
        await page.keyboard.press("Tab");
        order.push(await page.evaluate(() => { const el = document.activeElement as HTMLElement | null; return el?.getAttribute("name") ?? el?.textContent?.trim() ?? ""; }));
      }
      expect(order.filter((o) => ["username", "password", "Sign in"].includes(o)).slice(0, 3)).toEqual(["username", "password", "Sign in"]);

      await notObscured(page, page.getByRole("button", { name: "Sign in" }));
      await notObscured(page, page.getByRole("contentinfo"));
      await expect(page.getByRole("contentinfo")).toContainText("All access to this system will be tracked and monitored.");

      await failSignIn(page);                                                                                              // the status/error state at this zoom
      await notObscured(page, page.locator(".alv-login__banner--danger"));
      await notObscured(page, page.getByRole("button", { name: "Sign in" }));
      expect(await noHorizontalScroll(page)).toBe(true);
      await page.screenshot({ path: path.join(out, `zoom200-${vp.width}x${vp.height}-${theme}.png`), fullPage: true });

      const results = await new AxeBuilder({ page }).analyze();
      expect(results.violations, JSON.stringify(results.violations.map((v) => [v.id, v.nodes.map((n) => n.target)]))).toEqual([]);
    });
  }
}

for (const vp of [{ width: 1280, height: 800 }, { width: 375, height: 812 }]) {
  test(`forced colours (${vp.width}px): the background is suppressed and the panel, labels, fields, buttons, notice, error message and focus stay readable`, async ({ page }, testInfo) => {
    const out = process.env.A11Y_OUT ?? testInfo.outputDir;
    fs.mkdirSync(out, { recursive: true });
    await signedOut(page);
    await page.emulateMedia({ forcedColors: "active" });
    await page.setViewportSize({ width: vp.width, height: vp.height });
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    expect(await page.evaluate(() => matchMedia("(forced-colors: active)").matches), "the browser really is in forced-colours mode").toBe(true);

    const backdrop = page.getByTestId("signin-backdrop");                                                                  // the decorative image is not painted
    expect(await backdrop.evaluate((el) => getComputedStyle(el).backgroundImage)).toBe("none");

    const panel = page.locator(".alv-signin__panel");                                                                      // a solid system-colour panel with a visible edge
    const p = await panel.evaluate((el) => { const s = getComputedStyle(el); return { bg: s.backgroundColor, color: s.color, edge: s.borderInlineStartStyle !== "none" && parseFloat(s.borderInlineStartWidth) > 0 }; });
    expect(p.bg).not.toBe(p.color);
    expect(p.edge, "the panel keeps a visible edge without its shadow").toBe(true);

    await expect(page.getByLabel("Username")).toBeVisible();                                                                // labels and fields
    await expect(page.getByLabel("Password")).toBeVisible();
    for (const field of [page.getByLabel("Username"), page.getByLabel("Password")]) {
      const b = await field.evaluate((el) => { const s = getComputedStyle(el); return { style: s.borderTopStyle, width: parseFloat(s.borderTopWidth), color: s.borderTopColor, bg: s.backgroundColor }; });
      expect(b.style).not.toBe("none");
      expect(b.width).toBeGreaterThan(0);
      expect(b.color, "the field edge differs from its own background").not.toBe(b.bg);
    }
    const button = await page.getByRole("button", { name: "Sign in" }).evaluate((el) => { const s = getComputedStyle(el); return { style: s.borderTopStyle, width: parseFloat(s.borderTopWidth), color: s.color, bg: s.backgroundColor }; });
    expect(button.style).not.toBe("none");
    expect(button.width).toBeGreaterThan(0);
    expect(button.color).not.toBe(button.bg);

    const notice = await page.getByRole("contentinfo").evaluate((el) => { const s = getComputedStyle(el); const panelBg = getComputedStyle(document.querySelector(".alv-signin__panel")!).backgroundColor; return { color: s.color, panelBg }; });
    expect(notice.color, "the notice text differs from the panel").not.toBe(notice.panelBg);
    await expect(page.getByRole("contentinfo")).toBeVisible();

    for (const target of ["username", "password"]) {                                                                       // focus stays visible
      await page.getByLabel(target === "username" ? "Username" : "Password").focus();
      const f = await page.evaluate(() => { const s = getComputedStyle(document.activeElement!); return { style: s.outlineStyle, width: parseFloat(s.outlineWidth) }; });
      expect(f.style, `${target} focus outline`).not.toBe("none");
      expect(f.width, `${target} focus outline width`).toBeGreaterThan(0);
    }
    await page.getByRole("button", { name: "Sign in" }).focus();
    const fb = await page.evaluate(() => { const s = getComputedStyle(document.activeElement!); return { style: s.outlineStyle, width: parseFloat(s.outlineWidth) }; });
    expect(fb.style).not.toBe("none");
    expect(fb.width).toBeGreaterThan(0);

    await failSignIn(page);                                                                                                // the status/error message
    const banner = await page.locator(".alv-login__banner--danger").evaluate((el) => { const s = getComputedStyle(el); return { color: s.color, bg: s.backgroundColor, border: s.borderTopStyle }; });
    expect(banner.color).not.toBe(banner.bg);
    expect(await noHorizontalScroll(page)).toBe(true);
    await page.screenshot({ path: path.join(out, `forced-colors-${vp.width}x${vp.height}.png`), fullPage: true });
  });
}
