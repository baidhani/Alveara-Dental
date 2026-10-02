import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-N002 R09: the System Status page's stale-data banner (shown when a poll fails after a successful one) must pass axe's colour-contrast
// rule in both themes. Before R09 its warning text was 3.47:1 on its tint. The page polls every 15 s, so the test drives the page clock.

async function mockAuthenticated(page: Page) {
  await page.route("**/api/auth/permissions", (route: Route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ username: "e2e-admin", role: "Admin", permissions: ["ManageUsers", "ViewAuditLog"], sessionExpiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString() }) })
  );
  await page.route("**/api/auth/csrf-token", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ token: "e2e-csrf" }) }));
  await page.route("**/api/health", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
}

for (const theme of ["light", "dark"] as const) {
  test(`the stale-data banner passes the colour-contrast rule in ${theme} mode`, async ({ page }) => {
    await mockAuthenticated(page);
    let healthy = true;
    await page.route("**/api/systemstatus", (route: Route) =>
      healthy
        ? route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ appVersion: "1.0.0.0", localServerReachable: true, database: { reachable: true }, backgroundRunner: { status: "healthy", lastPollUtc: new Date().toISOString() } }) })
        : route.fulfill({ status: 500, contentType: "application/json", body: "{}" })
    );
    await page.clock.install();
    await page.goto("/system-status");
    await page.getByRole("button", { name: /Light|Dark/ }).waitFor();
    if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await expect(page.getByText("Healthy")).toBeVisible();

    healthy = false;
    await page.clock.fastForward(16_000); // one 15 s poll interval: the next check fails
    await expect(page.locator(".status-stale-banner")).toBeVisible();

    const results = await new AxeBuilder({ page }).include(".status-stale-banner").withRules(["color-contrast"]).analyze();
    expect(results.violations, JSON.stringify(results.violations.map((v) => v.nodes.map((n) => n.any.map((a) => a.message))), null, 2)).toEqual([]);
  });
}
