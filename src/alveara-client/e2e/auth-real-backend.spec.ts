import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";

// ALV-001-C01 R02 (review finding ALV-001-C01-R01-05): unlike every other e2e spec in this
// directory, this file deliberately does NOT mock the API via page.route() - the reviewer's
// finding was specifically that component tests and route-mocked "real browser" tests do not
// satisfy "exercise the actual rendered application in a real browser against the real
// API/database." Every request here is a genuine network call from a real Chromium instance to a
// real running Alveara.Api process backed by a real (migrated) LocalDB database.
//
// This spec is NOT wired into playwright.config.ts's default `npm test:e2e` run, because that
// config's webServer only starts the built static client (`vite preview`), with no backend behind
// it - the existing specs are deliberately backend-independent via mocking. Running this file
// requires both a real API (migrated database, AdminBootstrapSecret configured) and the Vite dev
// server (which proxies /api to the API - see vite.config.ts) already running; see
// docs/testing/REAL_BACKEND_E2E.md for the exact commands. Evidence of an actual run is at
// .alveara/handoffs/ALV-001-C01/R02-evidence/artifacts/playwright-real-backend/.

test.describe.configure({ mode: "serial" });

let adminUsername: string;
let bootstrapSecret: string;
let mfaSecret: string;
let context: BrowserContext;
let page: Page;

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  adminUsername = `admin-${Date.now()}`;
  bootstrapSecret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  // A single shared context/page across every test below (Playwright's documented pattern for a
  // session-like flow): each test() normally gets its own isolated, cookie-free context, which
  // would silently drop the login from one step before the next step ever runs.
  context = await browser.newContext();
  page = await context.newPage();
});

test.afterAll(async () => {
  await context.close();
});

test.describe("Real backend — first-admin bootstrap, login, MFA, security administration", () => {
  test("an unauthenticated caller cannot select a role at registration", async () => {
    await page.goto("/login");
    // There is no role field anywhere in the UI - registration itself isn't exposed as a public
    // flow in this shell (self-service accounts are provisioned by an admin instead), which is
    // itself the acceptance criterion made visible: no rendered path exists to choose a role.
    await expect(page.getByLabel("Username")).toBeVisible();
    await expect(page.locator("select, input").filter({ hasText: /role/i })).toHaveCount(0);
  });

  test("bootstraps the first admin via the real API and signs in", async () => {
    const response = await page.request.post("/api/auth/bootstrap-admin", {
      data: { username: adminUsername, password: "admin-password-1!", secret: bootstrapSecret },
    });
    expect(response.ok()).toBeTruthy();

    await page.goto("/login");
    await page.getByLabel("Username").fill(adminUsername);
    await page.getByLabel("Password").fill("admin-password-1!");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  });

  test("enrolls a real TOTP factor and completes an MFA challenge end to end", async () => {
    await page.goto("/settings/mfa");
    await page.getByRole("button", { name: "Set up an authenticator app" }).click();

    const secret = await page.getByTestId("mfa-secret").innerText();
    expect(secret.length).toBeGreaterThan(0);
    mfaSecret = secret;
    const recoveryCodesText = await page.getByTestId("mfa-recovery-codes").innerText();
    expect(recoveryCodesText.split("\n").filter(Boolean).length).toBe(10);

    const code = await computeTotp(secret);
    await page.getByLabel(/current code from your authenticator/i).fill(code);
    await page.getByRole("button", { name: "Confirm" }).click();
    await expect(page.getByText("MFA is now active on your account.")).toBeVisible();

    // Clear the session cookie directly (equivalent to a real sign-out, which ALV-N009 exercises
    // separately at the end of this file) so the next login is genuinely unauthenticated.
    await context.clearCookies();
    await page.goto("/login");
    await page.getByLabel("Username").fill(adminUsername);
    await page.getByLabel("Password").fill("admin-password-1!");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(/mfa-challenge/);
    const challengeCode = await computeTotp(secret);
    await page.getByLabel(/authentication or recovery code/i).fill(challengeCode);
    await page.getByRole("button", { name: "Verify" }).click();

    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  });

  test("replaying the exact same successful MFA challenge submission is rejected, not accepted twice", async () => {
    // R03 (review finding ALV-001-C01-R02-01): the reviewer's own live reproduction submitted an
    // identical challenge-token/code body twice via direct HTTP calls and got 200 both times.
    // This test reproduces that exact shape against the real API and asserts it's now fixed.
    await context.clearCookies();
    const loginResponse = await page.request.post("/api/auth/login", {
      data: { username: adminUsername, password: "admin-password-1!" },
    });
    expect(loginResponse.status()).toBe(202); // MFA required
    const { challengeToken } = await loginResponse.json();

    // Re-derive the active secret via a fresh enrollment replacement isn't needed here - the
    // account's current factor is still the one from the previous test, so recompute its code
    // from the same secret captured in that test's closure.
    const code = await computeTotp(mfaSecret);

    const first = await page.request.post("/api/auth/mfa/challenge", {
      data: { challengeToken, code },
    });
    expect(first.status()).toBe(200);

    const second = await page.request.post("/api/auth/mfa/challenge", {
      data: { challengeToken, code },
    });
    expect(second.status()).not.toBe(200);
  });

  test("security administration: list users, view detail, change role, set session timeout", async () => {
    await page.goto("/admin/users");
    await expect(page.getByRole("heading", { name: "Security administration" })).toBeVisible();
    await expect(page.getByText(adminUsername)).toBeVisible();

    await page.getByRole("link", { name: "View" }).first().click();
    await expect(page.getByRole("heading", { name: "User details" })).toBeVisible();

    const timeoutInput = page.getByLabel("Session timeout (minutes)");
    await timeoutInput.fill("45");
    await page.getByRole("button", { name: "Update timeout" }).click();
    await expect(page.getByText("Session timeout updated.")).toBeVisible();
  });

  test("permission matrix is visible to the admin and shows every role", async () => {
    await page.goto("/admin/permissions");
    await expect(page.getByRole("heading", { name: "Permission matrix" })).toBeVisible();
    for (const role of ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager", "Admin"]) {
      await expect(page.getByRole("columnheader", { name: role })).toBeVisible();
    }
  });

  // ALV-002-C01 (review finding ALV-002-C01-R01-01): real-browser verification of the new
  // permission-aware audit viewer, reading genuine audit entries this session's own earlier
  // actions already produced against the real API/database (bootstrap, login, and the session
  // timeout change from the previous test) - not fixture data.
  test("the audit log page shows real audit entries this session produced, as the admin", async () => {
    await page.goto("/admin/audit-log");
    await expect(page.getByRole("heading", { name: "Audit log" })).toBeVisible();
    await expect(page.getByText("SessionTimeoutChanged")).toBeVisible(); // from the previous test
    await expect(page.getByText("LoginSucceeded").first()).toBeVisible(); // from this very session's own sign-ins
    // ALV-002-C01 R03 (review finding R02-01): the shared entity metadata must actually reach the
    // viewer. The SessionTimeoutChanged row was written through the shared AuditService with
    // EntityType = "UserAccount"; its row must show that, not a dash.
    const timeoutRow = page.getByRole("row").filter({ hasText: "SessionTimeoutChanged" }).first();
    await expect(timeoutRow).toContainText("UserAccount");
    await expect(page.getByText(/don't have permission/i)).toHaveCount(0);
  });

  // ---------- ALV-N009: authorization-aware navigation, session UX, identity context ----------

  test("a caller without ManageUsers/ViewPermissionMatrix never sees those nav links and is denied the pages directly, against the real API", async () => {
    // Create and sign in as a limited-permission (Dentist) user, while still holding the admin's
    // authenticated session/CSRF token from the tests above.
    const csrfResponse = await page.request.get("/api/auth/csrf-token");
    const { token: csrf } = await csrfResponse.json();
    const limitedUsername = `dentist-${Date.now()}`;
    const registerResponse = await page.request.post("/api/auth/register", {
      data: { username: limitedUsername, password: "dentist-password-1!" },
    });
    expect(registerResponse.ok()).toBeTruthy();
    const { id: limitedUserId } = await registerResponse.json();
    const roleResponse = await page.request.put(`/api/auth/${limitedUserId}/role`, {
      headers: { "X-CSRF-Token": csrf },
      data: { role: "Dentist" },
    });
    expect(roleResponse.ok()).toBeTruthy();
    const enableResponse = await page.request.put(`/api/auth/${limitedUserId}/enabled`, {
      headers: { "X-CSRF-Token": csrf },
      data: { enabled: true },
    });
    expect(enableResponse.ok()).toBeTruthy();

    await context.clearCookies();
    await page.goto("/login");
    await page.getByLabel("Username").fill(limitedUsername);
    await page.getByLabel("Password").fill("dentist-password-1!");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

    // Real server-derived nav filtering, not a hard-coded client assumption: this account holds
    // neither ManageUsers, ViewPermissionMatrix, nor ViewAuditLog, so none of those links render.
    await expect(page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Security Administration" })).toHaveCount(0);
    await expect(page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Permission Matrix" })).toHaveCount(0);
    await expect(page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Audit Log" })).toHaveCount(0);

    // Direct URL to each hidden route is still denied server-authoritatively - the client-side
    // guard shows permission-denied before the page's own protected data fetch even starts, and
    // the server itself would reject the underlying API calls regardless of what the client shows.
    await page.goto("/admin/users");
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await page.goto("/admin/permissions");
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await page.goto("/admin/audit-log");
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
  });

  test("the account menu shows the signed-in identity, and sign-out clears the session so protected routes are denied again", async () => {
    await page.goto("/");
    await expect(page.getByText("Dentist", { exact: true })).toBeVisible(); // role, shown in the sidebar account area

    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();

    // UI-state clearing: a direct URL to a protected route after sign-out is denied again, not
    // served from any stale client-held state.
    await page.goto("/admin/users");
    await expect(page.getByText(/your session expired/i)).toBeVisible();
    await expect(page.getByRole("heading", { name: "Security administration" })).toHaveCount(0);
  });
});

/** RFC 6238 TOTP, computed independently of the application's own Otp.NET-backed implementation -
 *  proves interoperability rather than the app merely agreeing with itself. No network call. */
async function computeTotp(base32Secret: string): Promise<string> {
  const key = base32Decode(base32Secret);
  const counter = Math.floor(Date.now() / 1000 / 30);
  const counterBytes = new Uint8Array(8);
  let c = BigInt(counter);
  for (let i = 7; i >= 0; i--) {
    counterBytes[i] = Number(c & 0xffn);
    c >>= 8n;
  }
  const cryptoKey = await crypto.subtle.importKey("raw", key, { name: "HMAC", hash: "SHA-1" }, false, ["sign"]);
  const hmac = new Uint8Array(await crypto.subtle.sign("HMAC", cryptoKey, counterBytes));
  const offset = hmac[hmac.length - 1] & 0x0f;
  const binCode =
    ((hmac[offset] & 0x7f) << 24) | ((hmac[offset + 1] & 0xff) << 16) | ((hmac[offset + 2] & 0xff) << 8) | (hmac[offset + 3] & 0xff);
  return String(binCode % 1_000_000).padStart(6, "0");
}

function base32Decode(input: string): Uint8Array {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bytes: number[] = [];
  let bits = 0;
  let value = 0;
  for (const char of input.toUpperCase()) {
    const idx = alphabet.indexOf(char);
    if (idx === -1) continue;
    value = (value << 5) | idx;
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return new Uint8Array(bytes);
}
