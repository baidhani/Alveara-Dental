import { test, expect } from "@playwright/test";
import type { Page, Route } from "@playwright/test";

/**
 * ALV-N021 A2 (F-064): the production build (what the mocked suite serves) has no Component Showcase - not in the navigation and not
 * as a page. The showcase specs (shell.spec.ts, notification-contrast.spec.ts) run against the development server instead (playwright.config.ts).
 */
async function signedInAdmin(page: Page) {
  await page.route("**/api/auth/permissions", (route: Route) =>
    route.fulfill({
      status: 200, contentType: "application/json",
      body: JSON.stringify({ username: "e2e-admin", role: "Admin", permissions: ["ManageUsers", "ViewAuditLog", "ViewPermissionMatrix", "ManagePracticeConfiguration", "ManageBackups", "ViewBackupStatus"], sessionExpiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString() }),
    })
  );
  await page.route("**/api/auth/csrf-token", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ token: "e2e-csrf" }) }));
  await page.route("**/api/health", (route: Route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) }));
}

test("the production build has no Component Showcase in the navigation or as a page", async ({ page }) => {
  await signedInAdmin(page);
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Component Showcase" })).toHaveCount(0);

  await page.goto("/showcase");
  await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Component Showcase" })).toHaveCount(0);
});
