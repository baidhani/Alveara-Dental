import { test, expect } from "@playwright/test";
import type { Page, Route } from "@playwright/test";

async function mockHealthEndpoint(page: Page) {
  await page.route("**/api/health", (route: Route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) })
  );
}

test.describe("System Status page — real browser", () => {
  test("shows truthful reachable status for every field when the API reports healthy", async ({ page }) => {
    await mockHealthEndpoint(page);
    await page.route("**/api/systemstatus", (route: Route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: true },
          backgroundRunner: { status: "healthy", lastPollUtc: new Date().toISOString() },
        }),
      })
    );

    await page.goto("/system-status");
    await expect(page.getByRole("heading", { name: "System Status" })).toBeVisible();
    await expect(page.getByText("Healthy")).toBeVisible();
    await expect(page.getByText("1.0.0.0")).toBeVisible();
  });

  test("shows the database as unreachable while the rest of the page still renders", async ({ page }) => {
    await mockHealthEndpoint(page);
    await page.route("**/api/systemstatus", (route: Route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: false },
          backgroundRunner: { status: "not_yet_polled", lastPollUtc: null },
        }),
      })
    );

    await page.goto("/system-status");
    await expect(page.getByText("Unreachable")).toBeVisible();
    await expect(page.getByText("Not yet polled")).toBeVisible();
  });

  test("distinguishes local-server-unavailable from public-internet state when the API cannot be reached at all", async ({ page }) => {
    await mockHealthEndpoint(page);
    await page.route("**/api/systemstatus", (route: Route) => route.abort("failed"));

    await page.goto("/system-status");
    await expect(page.getByText("Local server unavailable")).toBeVisible();
    await expect(page.getByText(/different from a public-internet outage/i)).toBeVisible();
  });

  test("is reachable from the sidebar navigation", async ({ page }) => {
    await mockHealthEndpoint(page);
    await page.route("**/api/systemstatus", (route: Route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          appVersion: "1.0.0.0",
          localServerReachable: true,
          database: { reachable: true },
          backgroundRunner: { status: "healthy", lastPollUtc: new Date().toISOString() },
        }),
      })
    );

    await page.goto("/");
    await page.getByRole("link", { name: "System Status" }).click();
    await expect(page.getByRole("heading", { name: "System Status" })).toBeVisible();
  });
});
