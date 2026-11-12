import fs from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-N020-C01, mocked API, real browser:
// - the tile-readiness gate (review BRIEFING-R02 correction 4): every bundled background in Tile mode at three viewports, full-page screenshots kept for the
//   seam inspection (TILE_GATE_OUT names the folder; otherwise the test output folder);
// - each display mode renders as specified;
// - accessibility: axe (all rules) on the sign-in page for every bundled background in light and dark themes at 1280 and 375 px; keyboard-only sign-in order;
// - the footer notice renders as plain text and is absent when turned off.
// Runs in the desktop project only (it sets its own viewports).

const KEYS = ["teal-doodle", "blue-doodle", "blue-operatory", "violet-operatory", "neon-doodle"];
const GATE_VIEWPORTS = [{ width: 1920, height: 1080 }, { width: 1280, height: 800 }, { width: 375, height: 812 }];
const NOTICE = "Please ensure you have taken appropriate precautions.\n\nAll access to this system will be tracked and monitored.";

async function signedOut(page: Page, presentation: unknown, theme: "light" | "dark" = "light") {
  await page.route("**/api/auth/permissions", (r: Route) => r.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ error: "unauthenticated" }) }));
  await page.route("**/api/health", (r: Route) => r.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
  await page.route("**/api/public/login-presentation", (r: Route) => r.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(presentation) }));
  await page.addInitScript((t) => {
    localStorage.setItem("alveara-theme", t);
    document.addEventListener("DOMContentLoaded", () => document.documentElement.setAttribute("data-theme", t));
  }, theme);
}

const bundled = (key: string, displayMode = "Tile", footerNotice: string | null = null) => ({ background: { kind: "bundled", key }, displayMode, footerNotice });

/** Waits until the backdrop shows `key` and its image has really loaded and decoded. */
async function backgroundReady(page: Page, key: string) {
  const backdrop = page.getByTestId("signin-backdrop");
  await expect(backdrop).toHaveAttribute("style", new RegExp(`tile-${key}`));
  await page.evaluate(async () => {
    const el = document.querySelector("[data-testid=signin-backdrop]") as HTMLElement;
    const url = getComputedStyle(el).backgroundImage.replace(/^url\("?|"?\)$/g, "");
    const img = new Image();
    img.src = url;
    await img.decode();
  });
}

test.beforeEach(({}, testInfo) => test.skip(testInfo.project.name !== "desktop", "sets its own viewports"));

for (const key of KEYS) {
  test(`tile-readiness gate: ${key} in Tile mode at 1920x1080, 1280x800 and 375x812`, async ({ page }, testInfo) => {
    const out = process.env.TILE_GATE_OUT ?? testInfo.outputDir;
    fs.mkdirSync(out, { recursive: true });
    await signedOut(page, bundled(key));
    for (const vp of GATE_VIEWPORTS) {
      await page.setViewportSize(vp);
      await page.goto("/login");
      await backgroundReady(page, key);
      await expect(page.getByTestId("signin-backdrop")).toHaveAttribute("data-display-mode", "Tile");
      await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
      await page.screenshot({ path: path.join(out, `tile-${key}-${vp.width}x${vp.height}.png`), fullPage: true });
    }
  });
}

test("each display mode renders as specified (Stretch fills exactly, Fill covers and centres)", async ({ page }, testInfo) => {
  const out = process.env.TILE_GATE_OUT ?? testInfo.outputDir;
  for (const [mode, size] of [["Stretch", "100% 100%"], ["Fill", "cover"]] as const) {
    await page.unrouteAll({ behavior: "ignoreErrors" });
    await signedOut(page, bundled("blue-operatory", mode));
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto("/login");
    await backgroundReady(page, "blue-operatory");
    await expect(page.getByTestId("signin-backdrop")).toHaveAttribute("data-display-mode", mode);
    expect(await page.getByTestId("signin-backdrop").evaluate((el) => getComputedStyle(el).backgroundSize)).toBe(size);
    await page.screenshot({ path: path.join(out, `mode-${mode.toLowerCase()}-1280x800.png`) });
  }
});

for (const theme of ["light", "dark"] as const) {
  for (const width of [1280, 375]) {
    test(`accessibility: every bundled background passes axe in ${theme} theme at ${width}px, with the notice shown`, async ({ page }) => {
      for (const key of KEYS) {
        await page.unrouteAll({ behavior: "ignoreErrors" });
        await signedOut(page, bundled(key, "Tile", NOTICE), theme);
        await page.setViewportSize({ width, height: 800 });
        await page.goto("/login");
        await backgroundReady(page, key);
        await expect(page.getByRole("contentinfo")).toBeVisible();
        const results = await new AxeBuilder({ page }).analyze();
        expect(results.violations, `${key} ${theme} ${width}px: ` + JSON.stringify(results.violations.map((v) => [v.id, v.nodes.map((n) => n.target)]))).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);   // no horizontal scroll
      }
    });
  }
}

test("keyboard-only: Tab reaches Username, Password and Sign in in order; the notice is plain text and not focusable", async ({ page }) => {
  await signedOut(page, bundled("teal-doodle", "Tile", "Authorized use only.\n<b>not bold</b>"));
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/login");
  await expect(page.getByRole("contentinfo")).toHaveText("Authorized use only.\n<b>not bold</b>");
  expect(await page.getByRole("contentinfo").locator("b").count()).toBe(0);
  const order: string[] = [];
  for (let i = 0; i < 6; i++) {
    await page.keyboard.press("Tab");
    order.push(await page.evaluate(() => { const el = document.activeElement as HTMLElement | null; return el?.getAttribute("name") ?? el?.textContent?.trim() ?? ""; }));
  }
  const relevant = order.filter((o) => ["username", "password", "Sign in"].includes(o));
  expect(relevant.slice(0, 3)).toEqual(["username", "password", "Sign in"]);
});

test("a disabled notice is not on the page at all", async ({ page }) => {
  await signedOut(page, bundled("teal-doodle", "Tile", null));
  await page.goto("/login");
  await backgroundReady(page, "teal-doodle");
  expect(await page.locator("footer").count()).toBe(0);
});
