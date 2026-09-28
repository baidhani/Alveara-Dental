import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// Real-browser accessibility smoke coverage (N001-R01-03 asked for this to
// extend beyond the dashboard-only jsdom check, and to cover both themes).

async function mockHealthy(page: Page) {
  await page.route("**/api/health", (route: Route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "ok" }) })
  );
}

async function setTheme(page: Page, theme: "light" | "dark") {
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
