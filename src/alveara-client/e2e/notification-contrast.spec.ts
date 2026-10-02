import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-N001 R04 (finding F1 and its siblings): every notification variant, rendered by the real component in a real browser, must
// pass axe's colour-contrast rule in both themes. Before R04 the light success (4.45:1) and warning (3.47:1) and the dark success
// and info notifications failed it. The tests assert the toasts really rendered, so an empty scan cannot pass.

async function mockAuthenticated(page: Page) {
  await page.route("**/api/auth/permissions", (route: Route) =>
    route.fulfill({
      status: 200, contentType: "application/json",
      body: JSON.stringify({ username: "e2e-admin", role: "Admin", permissions: ["ManageUsers", "ViewAuditLog"], sessionExpiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString() }),
    })
  );
  await page.route("**/api/auth/csrf-token", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ token: "e2e-csrf" }) }));
  await page.route("**/api/health", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
}

for (const theme of ["light", "dark"] as const) {
  test(`all four notification variants pass the colour-contrast rule in ${theme} mode`, async ({ page }) => {
    await mockAuthenticated(page);
    await page.goto("/showcase");
    await page.getByRole("button", { name: /Light|Dark/ }).waitFor();
    if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);

    for (const name of ["Info", "Success", "Warning", "Danger"]) await page.getByRole("button", { name, exact: true }).last().click(); // "Danger" also exists as a button-variant sample earlier on the page
    for (const variant of ["info", "success", "warning", "danger"]) await expect(page.locator(`.alv-notification--${variant}`)).toBeVisible();
    await page.waitForTimeout(600); // let any entrance transition finish so the measured colours are the settled ones

    const results = await new AxeBuilder({ page }).include(".alv-notification-region").withRules(["color-contrast"]).analyze();
    expect(results.violations, JSON.stringify(results.violations.map((v) => v.nodes.map((n) => n.any.map((a) => a.message))), null, 2)).toEqual([]);
  });
}
