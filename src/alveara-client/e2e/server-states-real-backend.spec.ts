import { test, expect } from "@playwright/test";

// ALV-N030 S21 (brief 16.4): the screens a browser shows when the server is not ready for use, rendered by the real client against the real application
// (ChainHost under Test) started by scripts/Invoke-RealBackendWalkthrough.ps1 with -DatabaseState NotInstalled or UpgradeRequired. The third state - setup,
// "restarting", then sign-in - is proven by account-bootstrap-real-backend.spec.ts with -DatabaseState Fresh.

const state = process.env.E2E_DB_STATE ?? "";

test("S21 not installed: only the installer message, no sign-in", async ({ page, request }) => {
  test.skip(state !== "NotInstalled", "runs with -DatabaseState NotInstalled");
  const status = await request.get("/api/setup/status");
  expect([status.status(), await status.json()]).toEqual([503, { mode: "installation_incomplete" }]);
  await page.goto("/login");
  await expect(page.getByText("DentalKeep is not installed yet. Run the installer.")).toBeVisible();
  await expect(page.getByLabel("Username")).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath("s21-not-installed.png"), fullPage: true });
  expect((await request.get("/api/patients")).status()).toBe(503);
});

test("S21 upgrade required: only the upgrade message, no sign-in", async ({ page, request }) => {
  test.skip(state !== "UpgradeRequired", "runs with -DatabaseState UpgradeRequired");
  const status = await request.get("/api/setup/status");
  expect([status.status(), await status.json()]).toEqual([503, { mode: "upgrade_required" }]);
  await page.goto("/");
  await expect(page.getByText("An upgrade is waiting. An administrator must run the upgrade tool.")).toBeVisible();
  await expect(page.getByLabel("Username")).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath("s21-upgrade-required.png"), fullPage: true });
  expect((await request.get("/api/patients")).status()).toBe(503);
});
