import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-001-C01 R08: the login page's expired-session (warning) banner and its sign-in-failure (danger) banner must pass axe's colour-contrast
// rule in both themes. Before R08 the warning banner's text was 3.16:1 on its tint over the page background (light theme). The tests assert
// the banners really rendered, so an empty scan cannot pass. The signed-out shell has no theme toggle, so the theme is set the way the app stores it.

async function signedOut(page: Page, theme: "light" | "dark") {
  await page.route("**/api/auth/permissions", (route: Route) => route.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ error: "unauthenticated" }) }));
  await page.route("**/api/health", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
  await page.addInitScript((t) => {
    localStorage.setItem("alveara-theme", t); // the key the app persists the theme under
    document.addEventListener("DOMContentLoaded", () => document.documentElement.setAttribute("data-theme", t));
  }, theme);
}

async function scan(page: Page, selector: string) {
  const results = await new AxeBuilder({ page }).include(selector).withRules(["color-contrast"]).analyze();
  expect(results.violations, JSON.stringify(results.violations.map((v) => v.nodes.map((n) => n.any.map((a) => a.message))), null, 2)).toEqual([]);
}

for (const theme of ["light", "dark"] as const) {
  test(`the expired-session banner passes the colour-contrast rule in ${theme} mode`, async ({ page }) => {
    await signedOut(page, theme);
    await page.goto("/login?reason=expired");
    await expect(page.locator(".alv-login__banner--warning")).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await scan(page, ".alv-login__banner--warning");
  });

  test(`the sign-in-failure banner passes the colour-contrast rule in ${theme} mode`, async ({ page }) => {
    await signedOut(page, theme);
    await page.route("**/api/auth/login", (route: Route) => route.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ error: "invalid_credentials" }) }));
    await page.goto("/login");
    await page.getByLabel("Username").fill("nobody");
    await page.getByLabel("Password").fill("wrong-password-1");
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page.locator(".alv-login__banner--danger")).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await scan(page, ".alv-login__banner--danger");
  });
}
