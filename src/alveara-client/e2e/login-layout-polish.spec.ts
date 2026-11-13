import fs from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-N020-C02 (login layout visual polish), mocked API, real Chromium, desktop project only (it sets its own viewports). Proves, at 1280x800, 375x812 and the
// two 200%-equivalent viewports (640x400 of 1280x800, 960x540 of 1920x1080):
// - Username, Password and Sign in are one equal-width column;
// - the logo has room above it and between it and the form (desktop values; smaller, still positive, minimums where space is short) and nothing scrolls sideways
//   or is covered;
// - the footer notice is 12 px and smaller than the field labels;
// - the logo is the bundled local SVG, loaded, named "DentalKeep", rendered at its 1774x820 ratio without distortion or clipping, with no request leaving the origin.
// A separate capture test (POLISH_SHOTS_OUT + POLISH_SHOTS_LABEL) writes before/after screenshots of all five bundled backgrounds; it asserts only that the page rendered.

const NOTICE = "Please ensure you have taken appropriate precautions.\n\nAll access to this system will be tracked and monitored.";
const KEYS = ["teal-doodle", "blue-doodle", "blue-operatory", "violet-operatory", "neon-doodle"];
const SVG_RATIO = 1774 / 820;

interface Layout { width: number; height: number; label: string; topGap: number; belowGap: number }
// topGap: minimum px from the panel top to the logo; belowGap: minimum px from the logo to the "Sign in" heading.
const LAYOUTS: Layout[] = [
  { width: 1280, height: 800, label: "desktop", topGap: 72, belowGap: 40 },
  { width: 960, height: 540, label: "200% of 1920x1080", topGap: 32, belowGap: 24 },
  { width: 640, height: 400, label: "200% of 1280x800", topGap: 32, belowGap: 24 },
  { width: 375, height: 812, label: "narrow", topGap: 32, belowGap: 24 },
];

async function signedOut(page: Page, key = "blue-operatory", theme: "light" | "dark" = "light") {
  await page.route("**/api/auth/permissions", (r: Route) => r.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ error: "unauthenticated" }) }));
  await page.route("**/api/health", (r: Route) => r.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
  await page.route("**/api/public/login-presentation", (r: Route) =>
    r.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ background: { kind: "bundled", key }, displayMode: "Tile", footerNotice: NOTICE }) }));
  await page.addInitScript((t) => {
    localStorage.setItem("alveara-theme", t);
    document.addEventListener("DOMContentLoaded", () => document.documentElement.setAttribute("data-theme", t));
  }, theme);
}

const noHorizontalScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth && document.body.scrollWidth <= window.innerWidth);

async function notObscured(page: Page, locator: ReturnType<Page["locator"]>) {
  await locator.scrollIntoViewIfNeeded();
  const hit = await locator.evaluate((el) => {
    const r = el.getBoundingClientRect();
    const top = document.elementFromPoint(r.left + r.width / 2, r.top + Math.min(r.height / 2, 8));
    return !!top && (el === top || el.contains(top));
  });
  expect(hit, "nothing covers the element").toBe(true);
}

test.beforeEach(({ browserName }, testInfo) => test.skip(browserName !== "chromium" || testInfo.project.name !== "desktop", "sets its own viewports"));

for (const lay of LAYOUTS) {
  test(`${lay.label} (${lay.width}x${lay.height}): equal-width column, logo spacing, 12 px notice, SVG logo ratio, nothing scrolls or is covered`, async ({ page }) => {
    const foreign: string[] = [];
    page.on("request", (req) => { const u = new URL(req.url()); if (u.origin !== new URL(page.url() === "about:blank" ? req.url() : page.url()).origin && !u.protocol.startsWith("data")) foreign.push(req.url()); });
    await signedOut(page);
    await page.setViewportSize({ width: lay.width, height: lay.height });
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    await expect(page.getByRole("contentinfo")).toContainText("All access to this system will be tracked and monitored.");

    // 1. one equal-width column
    const w = async (l: ReturnType<Page["locator"]>) => (await l.boundingBox())!.width;
    const [user, pass, btn] = [await w(page.getByLabel("Username")), await w(page.getByLabel("Password")), await w(page.getByRole("button", { name: "Sign in" }))];
    expect(Math.abs(user - btn), `Username ${user} vs Sign in ${btn}`).toBeLessThanOrEqual(1);
    expect(Math.abs(pass - btn), `Password ${pass} vs Sign in ${btn}`).toBeLessThanOrEqual(1);

    // 2. logo spacing (page scrolled to the top)
    const logo = page.getByRole("img", { name: "DentalKeep" });
    const panelBox = (await page.locator(".alv-signin__panel").boundingBox())!;
    const logoBox = (await logo.boundingBox())!;
    const headingBox = (await page.getByRole("heading", { name: "Sign in" }).boundingBox())!;
    expect(logoBox.y - panelBox.y, "space above the logo").toBeGreaterThanOrEqual(lay.topGap);
    expect(headingBox.y - (logoBox.y + logoBox.height), "space between the logo and the Sign in heading").toBeGreaterThanOrEqual(lay.belowGap);

    // 3. the notice is 12 px and smaller than the field labels
    const notice = await page.getByRole("contentinfo").evaluate((el) => parseFloat(getComputedStyle(el).fontSize));
    const label = await page.locator(".alv-form-field__label").first().evaluate((el) => parseFloat(getComputedStyle(el).fontSize));
    expect(notice, "notice font size").toBe(12);
    expect(notice, "notice smaller than the labels").toBeLessThan(label);
    const centred = await page.getByRole("contentinfo").evaluate((el) => {                                  // owner request: the notice text is centred in the panel
      const r = document.createRange(); r.selectNodeContents(el.querySelector("p")!);
      const lines = Array.from(r.getClientRects()).filter((q) => q.width > 0);
      const panel = document.querySelector(".alv-signin__panel")!.getBoundingClientRect();
      return { align: getComputedStyle(el).textAlign, offsets: lines.map((q) => Math.abs((q.left + q.right) / 2 - (panel.left + panel.right) / 2)) };
    });
    expect(centred.align, "notice text-align").toBe("center");
    for (const off of centred.offsets) expect(off, "each notice line is centred on the panel").toBeLessThanOrEqual(2);

    // 4. the logo: bundled local SVG, loaded, named, undistorted, not clipped
    const info = await logo.evaluate((el) => { const i = el as HTMLImageElement; return { src: i.currentSrc, loaded: i.complete && i.naturalWidth > 0, natW: i.naturalWidth, natH: i.naturalHeight, alt: i.alt }; });
    expect(new URL(info.src).pathname, "a bundled .svg").toMatch(/\.svg$/);
    expect(info.loaded, "the logo loaded").toBe(true);
    expect(info.alt).toBe("DentalKeep");
    expect(Math.abs(logoBox.width / logoBox.height / SVG_RATIO - 1), `rendered ratio ${logoBox.width}x${logoBox.height}`).toBeLessThan(0.005);
    expect(logoBox.x).toBeGreaterThanOrEqual(panelBox.x);
    expect(logoBox.x + logoBox.width).toBeLessThanOrEqual(panelBox.x + panelBox.width);
    const fit = await logo.evaluate((el) => getComputedStyle(el).objectFit);
    expect(fit, "scaled to fit, never cropped or stretched").toBe("contain");

    // nothing scrolls sideways, Sign in and the footer are reachable and not covered, no request left the origin
    expect(await noHorizontalScroll(page), "no horizontal scrolling").toBe(true);
    await notObscured(page, page.getByRole("button", { name: "Sign in" }));
    await notObscured(page, page.getByRole("contentinfo"));
    expect(foreign, "no request left the origin").toEqual([]);
  });
}

test("the logo keeps its ratio and the layout stays clean in forced colours (1280 and 375)", async ({ page }) => {
  await signedOut(page);
  await page.emulateMedia({ forcedColors: "active" });
  for (const vp of [{ width: 1280, height: 800 }, { width: 375, height: 812 }]) {
    await page.setViewportSize(vp);
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    const box = (await page.getByRole("img", { name: "DentalKeep" }).boundingBox())!;
    expect(Math.abs(box.width / box.height / SVG_RATIO - 1)).toBeLessThan(0.005);
    const w = async (l: ReturnType<Page["locator"]>) => (await l.boundingBox())!.width;
    expect(Math.abs((await w(page.getByLabel("Username"))) - (await w(page.getByRole("button", { name: "Sign in" }))))).toBeLessThanOrEqual(1);
    expect(await page.getByRole("contentinfo").evaluate((el) => parseFloat(getComputedStyle(el).fontSize))).toBe(12);
    expect(await noHorizontalScroll(page)).toBe(true);
  }
});

for (const theme of ["light", "dark"] as const) {
  test(`axe is clean on the polished sign-in screen (${theme}, 1280x800)`, async ({ page }) => {
    await signedOut(page, "blue-operatory", theme);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations.map((v) => [v.id, v.nodes.map((n) => n.target)]))).toEqual([]);
  });
}

// Before/after capture for the owner and the reviewer: skipped unless POLISH_SHOTS_OUT is set.
test("capture: every bundled background, 1280x800 (plus narrow and 200% for the default), full page", async ({ page }) => {
  test.skip(!process.env.POLISH_SHOTS_OUT, "capture only when POLISH_SHOTS_OUT is set");
  const out = path.join(process.env.POLISH_SHOTS_OUT!, process.env.POLISH_SHOTS_LABEL ?? "shots");
  fs.mkdirSync(out, { recursive: true });
  const shoot = async (key: string, vp: { width: number; height: number }, name: string) => {
    await page.unrouteAll({ behavior: "ignoreErrors" });
    await signedOut(page, key);
    await page.setViewportSize(vp);
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    await expect(page.getByTestId("signin-backdrop")).toHaveAttribute("style", new RegExp(`tile-${key}`));
    await page.evaluate(async () => {
      const el = document.querySelector("[data-testid=signin-backdrop]") as HTMLElement;
      const img = new Image();
      img.src = getComputedStyle(el).backgroundImage.replace(/^url\("?|"?\)$/g, "");
      await img.decode();
      await Promise.all(Array.from(document.images).map((i) => i.decode().catch(() => undefined)));
    });
    await page.screenshot({ path: path.join(out, name), fullPage: true });
  };
  for (const key of KEYS) await shoot(key, { width: 1280, height: 800 }, `${key}-1280x800.png`);
  await shoot("teal-doodle", { width: 375, height: 812 }, "teal-doodle-375x812.png");
  await shoot("teal-doodle", { width: 640, height: 400 }, "teal-doodle-640x400-zoom200.png");
  await shoot("teal-doodle", { width: 960, height: 540 }, "teal-doodle-960x540-zoom200.png");
});
