import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// Real-browser accessibility smoke coverage (N001-R01-03 asked for this to
// extend beyond the dashboard-only jsdom check, and to cover both themes).

/** The shell is behind the sign-in gate since ALV-001-C01/ALV-N009: these mocked specs present an authenticated administrator
 *  (same response shape as GET /api/auth/permissions) so they reach the real shell pages again. The authenticated, real-API
 *  accessibility check is src/alveara-client/gate-a/gate-a-browser.spec.ts. */
async function mockAuthenticated(page: Page) {
  await page.route("**/api/auth/permissions", (route: Route) =>
    route.fulfill({
      status: 200, contentType: "application/json",
      body: JSON.stringify({ username: "e2e-admin", role: "Admin", permissions: ["ManageUsers", "ViewAuditLog", "ViewPermissionMatrix", "ManagePracticeConfiguration", "ManageBackups", "ViewBackupStatus"], sessionExpiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString() }),
    })
  );
  await page.route("**/api/auth/csrf-token", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ token: "e2e-csrf" }) }));
}

async function mockHealthy(page: Page) {
  await mockAuthenticated(page);
  await page.route("**/api/health", (route: Route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) })
  );
}

async function setTheme(page: Page, theme: "light" | "dark") {
  await page.getByRole("button", { name: /Light|Dark/ }).waitFor(); // the shell (and its toggle) mounts only after the authenticated session resolves
  const current = await page.locator("html").getAttribute("data-theme");
  if (current !== theme) {
    await page.getByRole("button", { name: /Light|Dark/ }).click();
  }
  await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
}

const pages: Array<{ label: string; path: string }> = [
  { label: "Dashboard", path: "/" },
  { label: "Component Showcase", path: "/showcase" },
  { label: "Not Found", path: "/this-route-does-not-exist" },
];

for (const { label, path } of pages) {
  for (const theme of ["light", "dark"] as const) {
    test(`${label} has no critical/serious axe violations in ${theme} mode`, async ({ page }) => {
      await mockHealthy(page);
      await page.goto(path);
      await setTheme(page, theme);

      const results = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa"])
        .analyze();

      const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
    });
  }
}
