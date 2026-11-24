import { execFileSync } from "node:child_process";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import type { Page } from "@playwright/test";
import { sqlServer } from "../e2e/support/sqlServer";
import { createAccount } from "../e2e/support/testAccounts";

// ALV-N019 stage 0 of the accumulated chain (R05 PKG-01, raw-log step 109 / S-09, owner decision "option B").
// Runs only through scripts/Invoke-AccumulatedChain.ps1, which gives it a fresh AlveraChain_<run id> database and ChainHost started under the
// Test environment with test support on a 127.0.0.1 listener (ALV-N030, brief section 17). ALV-N030 removed sign-up and the static-secret
// first-administrator bootstrap; S-09 now proves both are gone and creates the administrator through the test-only fixture route. Step titles start with their source id; the evidence builder reads it.

test.describe.configure({ mode: "serial" });

const database = process.env.E2E_DB_NAME ?? "";
const password = "stage0-admin-password-1!";
const username = "stage0-admin";

function userCount(): number {
  if (!/^AlveraChain_[0-9a-f]{32}$/.test(database)) throw new Error(`Refusing to query '${database}': not a chain database.`);
  const out = execFileSync("sqlcmd", ["-S", sqlServer, "-d", database, "-E", "-b", "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; SELECT COUNT(*) FROM UserAccounts"], { encoding: "utf8" });
  return Number(out.trim().split(/\r?\n/)[0]);
}

test.beforeAll(() => {
  expect(database, "E2E_DB_NAME must be the chain database").toMatch(/^AlveraChain_[0-9a-f]{32}$/);
});

test("S-09a sign-up and the old first-administrator bootstrap are gone (404 or 405) and create nothing", async ({ request }) => {
  for (const url of ["/api/auth/register", "/api/auth/bootstrap-admin"]) {
    const response = await request.post(url, { data: { username, password, secret: "anything" } });
    expect([404, 405], `${url} answered ${response.status()}`).toContain(response.status());
  }
  expect(userCount()).toBe(0);
});

test("S-09b the test-only fixture route creates exactly one administrator (201)", async ({ request }) => {
  const response = await createAccount(request, { username, password, role: "Admin" });
  expect(response.status()).toBe(201);
  expect(userCount()).toBe(1);
});

test("S-09c a second fixture account with the same username is refused with 409 and the user count stays 1", async ({ request }) => {
  const response = await createAccount(request, { username, password, role: "Admin" });
  expect(response.status()).toBe(409);
  expect((await response.json()).error).toBe("account_conflict");
  expect(userCount()).toBe(1);
});

// PD-32 (ALV-N019 R02): the host reports the clock it really uses. The runner passes the mode it measured in CHAIN_CLOCK_MODE.
test("C-01 the host reports the clock it really uses: an injected test clock, or the product's own clock in real-clock mode", async ({ request }) => {
  const mode = process.env.CHAIN_CLOCK_MODE;
  expect(["injected", "real"], "CHAIN_CLOCK_MODE must be set by the runner").toContain(mode);
  const response = await request.get("/api/chain/clock-mode");
  if (mode === "injected") {
    expect(response.status()).toBe(200);
    const report = await response.json();
    expect(report.mode).toBe("injected");
    expect(report.clockType).toBe("ChainPracticeClock");
    expect(report.utcNow).toMatch(/^2026-01-15T15:00:00/);       // the fixed instant the chain host was started with
  } else {
    // ALV-N030 (brief 17.3): real-clock runs use ChainHost too (CHAIN_CLOCK_MODE=real); it swaps nothing and reports the product's own clock
    expect(response.status()).toBe(200);
    const report = await response.json();
    expect(report.mode).toBe("real");
    expect(report.clockType).not.toBe("ChainPracticeClock");
  }
});

async function signIn(page: Page): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

/** Where the captured screenshots go: the run's evidence folder (CHAIN_OUT), or the test's own output folder when run by hand. */
function capturePath(name: string): string {
  const dir = process.env.CHAIN_OUT ? path.join(process.env.CHAIN_OUT, "brand") : test.info().outputDir;
  mkdirSync(dir, { recursive: true });
  return path.join(dir, name);
}

test("S-09d the administrator signs in through the browser and reaches the dashboard", async ({ page }) => {
  await signIn(page);
});

// Review correction 06 (ALV-N019 R02): the DentalKeep identity is asserted in the real product, in a real browser, with captures.
test("B-01 the sign-in page says DentalKeep in the document title and in its own text (real browser, captured)", async ({ page }) => {
  await page.goto("/login");
  await expect(page).toHaveTitle("DentalKeep");
  await expect(page.getByText("Enter your username and password to access DentalKeep.")).toBeVisible();
  await expect(page.getByText("Alveara")).toHaveCount(0);
  await page.screenshot({ path: capturePath("brand-sign-in.png"), fullPage: true });
});

test("B-02 the signed-in page shows DentalKeep in the header brand and in the document title (real browser, captured)", async ({ page }) => {
  await signIn(page);
  await expect(page).toHaveTitle("DentalKeep");
  await expect(page.locator(".alv-shell__brand-text")).toContainText("DentalKeep");
  await expect(page.locator(".alv-shell__brand-text")).not.toContainText("Alveara");
  await page.screenshot({ path: capturePath("brand-signed-in.png"), fullPage: true });
});
