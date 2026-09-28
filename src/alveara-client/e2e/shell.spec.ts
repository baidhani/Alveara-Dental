import { test, expect } from "@playwright/test";
import type { Page, Route } from "@playwright/test";

// Real Chromium browser walkthrough (N001-R01-02) — replaces the claim that
// jsdom/Testing-Library structural checks alone prove "usable at agreed
// desktop and tablet widths." Every test in this file runs twice: once at
// the desktop viewport (1280x800) and once at the tablet viewport
// (768x1024), via the two Playwright projects in playwright.config.ts.

async function mockHealthy(page: Page) {
  await page.route("**/api/health", (route: Route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) })
  );
}

async function mockDown(page: Page) {
  await page.route("**/api/health", (route: Route) => route.abort("failed"));
}

test.describe("Application shell — real browser walkthrough", () => {
  test("dashboard loads with an honest empty state, no horizontal overflow", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/");

    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await expect(page.getByText("Nothing to show yet")).toBeVisible();

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
    expect(overflow).toBe(false);
  });

  test("navigation reaches the showcase page and it renders every control", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/");

    await page.getByRole("link", { name: "Component Showcase" }).click();
    await expect(page.getByRole("heading", { name: "Component Showcase" })).toBeVisible();

    const buttonsSection = page.locator("section", { has: page.getByRole("heading", { name: "Buttons" }) });
    await expect(buttonsSection.getByRole("button", { name: "Primary" })).toBeVisible();
    await expect(buttonsSection.getByRole("button", { name: "Secondary" })).toBeVisible();
    await expect(buttonsSection.getByRole("button", { name: "Danger", exact: true })).toBeVisible();
    await expect(page.getByLabel("Email address")).toBeVisible();

    // Long-text wrapping check (proxy for "long labels" requirement): the
    // showcase page's description must wrap within its container rather
    // than overflowing it, at both the desktop and tablet viewport.
    const desc = page.getByText("The reusable controls built for the shell so far.", { exact: false });
    const box = await desc.boundingBox();
    const viewport = page.viewportSize();
    expect(box).not.toBeNull();
    expect(box!.width).toBeLessThanOrEqual(viewport!.width);
  });

  test("keyboard-only path: skip link, tab into navigation, activate with Enter", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/");

    await page.keyboard.press("Tab");
    await expect(page.getByText("Skip to main content")).toBeFocused();

    // Keep tabbing until the Component Showcase link is focused.
    const showcaseLink = page.getByRole("link", { name: "Component Showcase" });
    for (let i = 0; i < 20; i++) {
      const isFocused = await showcaseLink.evaluate((el) => el === document.activeElement);
      if (isFocused) break;
      await page.keyboard.press("Tab");
    }
    await expect(showcaseLink).toBeFocused();

    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Component Showcase" })).toBeVisible();
  });

  test("unknown route shows a truthful not-found page with a working, single-control return action", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/this-route-does-not-exist");

    await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
    const backControl = page.getByRole("link", { name: "Back to Dashboard" });
    await expect(backControl).toBeVisible();
    await backControl.click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  });

  test("form validation appears on blur and clears when corrected", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/showcase");

    const emailInput = page.getByLabel("Email address");
    await emailInput.click();
    await emailInput.fill("not-an-email");
    await emailInput.blur();
    await expect(page.getByRole("alert").filter({ hasText: "valid email" })).toBeVisible();

    await emailInput.fill("person@example.com");
    await emailInput.blur();
    await expect(page.getByText("Enter a valid email address.")).toHaveCount(0);
  });

  test("notification toasts appear for each tone", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/showcase");

    await page.getByRole("button", { name: "Success" }).click();
    await expect(page.getByText("Saved successfully.")).toBeVisible();

    await page.getByRole("button", { name: "Warning" }).click();
    await expect(page.getByText("Double-check this before continuing.")).toBeVisible();
  });

  test("shows the disconnected banner when the API is unreachable, and clears it once healthy again", async ({ page }) => {
    await mockDown(page);
    await page.goto("/");
    await expect(page.getByRole("alert").filter({ hasText: "Local server unavailable" })).toBeVisible();

    // Simulate recovery: a fresh check (via reload) now finds a healthy API.
    await page.unroute("**/api/health");
    await mockHealthy(page);
    await page.reload();
    await expect(page.getByRole("alert").filter({ hasText: "Local server unavailable" })).toHaveCount(0);
  });

  test("does not show the disconnected banner while the API is healthy", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/");
    await expect(page.getByRole("alert").filter({ hasText: "Local server unavailable" })).toHaveCount(0);
  });

  test("theme toggle switches and persists between light and dark", async ({ page }) => {
    await mockHealthy(page);
    await page.goto("/");

    await expect(page.locator("html")).toHaveAttribute("data-theme", /light|dark/);
    const initial = await page.locator("html").getAttribute("data-theme");

    const toggle = page.getByRole("button", { name: /Light|Dark/ });
    await toggle.click();
    const toggled = await page.locator("html").getAttribute("data-theme");
    expect(toggled).not.toBe(initial);

    await page.reload();
    await expect(page.locator("html")).toHaveAttribute("data-theme", toggled!);
  });

  test("layout has no unintended horizontal overflow at this viewport", async ({ page }, testInfo) => {
    await mockHealthy(page);
    await page.goto("/showcase");

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
    expect(overflow).toBe(false);

    // Sidebar layout differs structurally between desktop and tablet per
    // AppShell.css's 820px breakpoint; assert the concrete difference.
    const sidebarBox = await page.locator(".alv-shell__sidebar").boundingBox();
    const viewport = page.viewportSize()!;
    expect(sidebarBox).not.toBeNull();

    if (testInfo.project.name === "tablet") {
      // Collapsed to a top bar: sidebar spans (approximately) the full width.
      expect(sidebarBox!.width).toBeGreaterThan(viewport.width * 0.9);
    } else {
      // Desktop: fixed narrow sidebar column, not full width.
      expect(sidebarBox!.width).toBeLessThan(viewport.width * 0.5);
    }
  });
});
