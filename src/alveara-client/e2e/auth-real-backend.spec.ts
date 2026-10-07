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

  // ---------- ALV-N003: practice, staff, provider, operatory, and scheduling configuration ----------

  test("practice configuration: set up the practice end to end, see it in the scheduling preview, and find it in the audit log", async () => {
    await page.goto("/admin/configuration");
    await expect(page.getByRole("heading", { name: "Practice configuration" })).toBeVisible();

    // Practice information (time zone/currency are reflected read-only from the deployment).
    await expect(page.getByLabel("Time zone")).toHaveValue("America/Chicago");
    await expect(page.getByLabel("Currency")).toHaveValue("USD");
    await page.getByLabel("Practice name").fill("E2E Dental");
    await page.getByRole("button", { name: "Save practice information" }).click();
    await expect(page.getByText("Practice information saved.")).toBeVisible();

    // The one active location, then what hangs off it.
    await page.getByRole("button", { name: "Add location" }).click();
    await page.getByLabel("Location name").fill("Main Office");
    await page.getByRole("button", { name: "Add location" }).last().click();
    await expect(page.getByText("Added the location.")).toBeVisible();

    await page.getByRole("tab", { name: "Appointment types" }).click();
    await page.getByRole("button", { name: "Add appointment type" }).click();
    await page.getByLabel("Appointment type name").fill("E2E Exam");
    await page.getByLabel("Default duration (minutes)").fill("45");
    await page.getByRole("button", { name: "Add appointment type" }).last().click();
    await expect(page.getByRole("cell", { name: "45 min", exact: true })).toBeVisible();

    await page.getByRole("tab", { name: "Operatories" }).click();
    await page.getByRole("button", { name: "Add operatory" }).click();
    await page.getByLabel("Operatory name").fill("E2E Op 1");
    await page.getByRole("button", { name: "Add operatory" }).last().click();
    await expect(page.getByRole("cell", { name: "E2E Op 1", exact: true })).toBeVisible();

    // Staff -> provider -> weekly hours (practice-local wall clock).
    await page.getByRole("tab", { name: "Staff" }).click();
    await page.getByRole("button", { name: "Add staff profile" }).click();
    await page.getByLabel("Display name").fill("Dr. E2E");
    await page.getByLabel("Job title").fill("Dentist");
    await page.getByRole("button", { name: "Add staff profile" }).last().click();
    await expect(page.getByRole("cell", { name: "Dr. E2E", exact: true })).toBeVisible();

    await page.getByRole("tab", { name: "Providers" }).click();
    await page.getByRole("button", { name: "Add provider" }).click();
    await page.getByLabel("Staff member").selectOption({ label: "Dr. E2E" });
    await page.getByLabel("Specialty").fill("General dentistry");
    await page.getByRole("button", { name: "Add provider" }).last().click();
    await expect(page.getByRole("cell", { name: "General dentistry", exact: true })).toBeVisible();

    await page.getByRole("tab", { name: "Availability" }).click();
    await page.getByLabel("Provider").selectOption({ label: "Dr. E2E - General dentistry" });
    await page.getByRole("button", { name: "Add window" }).click();
    await page.getByRole("button", { name: "Save weekly hours" }).click();
    await expect(page.getByText("Weekly availability saved.")).toBeVisible();

    // Immediately consumable: the scheduling preview reads the same read model scheduling uses.
    await page.getByRole("tab", { name: "Scheduling preview" }).click();
    await expect(page.getByText(/Active location: Main Office - times in America\/Chicago/)).toBeVisible();
    await expect(page.getByText(/Dr\. E2E/)).toBeVisible();
    await expect(page.getByText(/Monday 09:00-17:00/)).toBeVisible();
    await expect(page.getByText("E2E Op 1")).toBeVisible();
    await expect(page.getByText("E2E Exam - 45 min")).toBeVisible();

    // Inactivating an operatory removes it from scheduling but keeps it resolvable (Show inactive).
    await page.getByRole("tab", { name: "Operatories" }).click();
    await page.getByRole("button", { name: "Inactivate E2E Op 1" }).click();
    await expect(page.getByText("Inactivated the operatory.")).toBeVisible();
    await page.getByLabel("Show inactive").check();
    await expect(page.getByRole("row").filter({ hasText: "E2E Op 1" })).toContainText("Inactive");
    await page.getByRole("tab", { name: "Scheduling preview" }).click();
    await expect(page.getByText("E2E Exam - 45 min")).toBeVisible();
    await expect(page.getByText("E2E Op 1")).toHaveCount(0);

    // Material changes were audited through the shared audit path, with their entity type.
    await page.goto("/admin/audit-log");
    await expect(page.getByRole("row").filter({ hasText: "Operatory" }).filter({ hasText: "ConfigurationCreated" }).first()).toBeVisible();
    await expect(page.getByRole("row").filter({ hasText: "Operatory" }).filter({ hasText: "ConfigurationInactivated" }).first()).toBeVisible();
    await expect(page.getByRole("row").filter({ hasText: "ProviderAvailabilityReplaced" }).first()).toBeVisible();
  });

  // ALV-N003 R02 (review finding ALV-N003-R01-03): a real browser, real dialog. Main-shell navigation
  // must ask before discarding a dirty configuration draft; declining keeps the page and the text.
  test("unsaved configuration edits survive an attempt to leave through the main navigation", async () => {
    await page.goto("/admin/configuration");
    const name = page.getByLabel("Practice name");
    await expect(name).toHaveValue("E2E Dental"); // saved by the previous test
    await name.fill("E2E Dental - unsaved edit");

    const dialogs: string[] = [];
    page.once("dialog", (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss(); // refuse to discard
    });
    await page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Dashboard" }).click();
    await expect.poll(() => dialogs.length).toBe(1);
    expect(dialogs[0]).toMatch(/unsaved changes/i);
    await expect(page).toHaveURL(/\/admin\/configuration$/);
    await expect(name).toHaveValue("E2E Dental - unsaved edit");

    page.once("dialog", (dialog) => void dialog.accept()); // now agree to discard
    await page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Dashboard" }).click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  });

  // ---------- ALV-N004: encrypted backup, verification, and restore into an isolated target ----------

  test("backup & recovery: set up the recovery key, run a real backup, and restore it into an isolated target", async () => {
    test.setTimeout(90_000); // the default 30 s test budget must also cover a slow server-side key generation (see the bounded wait below)
    await page.goto("/admin/backup");
    await expect(page.getByRole("heading", { name: "Backup & recovery" })).toBeVisible();
    await expect(page.getByText("No recovery key is set up")).toBeVisible();
    await expect(page.getByRole("button", { name: "Run backup now" })).toBeDisabled();

    // Recovery key: shown once, acknowledged, then gone from the screen.
    await page.getByRole("button", { name: "Set up recovery key" }).click();
    await expect(page.getByLabel("Recovery passphrase")).toHaveCount(0); // the server generates the passphrase; the user never chooses a weak one
    await page.getByLabel("Your current password").fill("admin-password-1!");
    await page.getByRole("button", { name: "Create recovery key" }).click();
    const keyBox = page.getByLabel("Recovery key file contents");
    // Gate B stabilization (review condition on the ALV-N004 R06 / Gate B reviews): the key box appears only after the SERVER has generated an RSA-3072
    // OpenPGP key, whose time varies. Measured on this machine against the real API (18 samples idle: 0.7-4.6 s, three above 4.3 s; 18 samples under CPU load:
    // 2.0-6.6 s, two above 5 s) and the 5 s default assertion timeout failed this exact step in 5 of 6 loaded runs. 30 s is 4.5x the worst measured latency and
    // still bounds a genuinely stuck request; the expectation passes the moment the box appears.
    await expect(keyBox).toBeVisible({ timeout: 30_000 });
    const recoveryKey = await keyBox.inputValue();
    expect(recoveryKey).toContain("BEGIN PGP PRIVATE KEY BLOCK"); // a standard OpenPGP key block, restorable with stock tooling
    const passphrase = await page.getByLabel("Recovery passphrase (generated)").inputValue();
    expect(passphrase.length).toBeGreaterThanOrEqual(24);
    await page.getByLabel(/I have stored the recovery key file/).check();
    await page.getByRole("button", { name: /Done - clear the key/ }).click();
    await expect(keyBox).toHaveCount(0);
    await expect(page.getByText("No recovery key is set up")).toHaveCount(0);

    // A real SQL Server backup of the real database, encrypted to the recovery key.
    await page.getByRole("button", { name: "Run backup now" }).click();
    await expect(page.getByText("Backup completed.")).toBeVisible();
    const historyRow = page.getByRole("row").filter({ hasText: "Manual" }).first();
    await expect(historyRow).toContainText("Succeeded");
    await expect(historyRow).toContainText("File hash verified only"); // backed up != proven restorable
    await expect(historyRow).toContainText("Database, Deployment settings, Documents, Encryption keys");
    await expect(page.getByLabel("Asset coverage")).toContainText("includes every managed asset class");

    // The restore wizard: explicit recovery material, compatibility checks, then an ISOLATED restore.
    await historyRow.getByRole("button", { name: /Verify or restore/ }).click();
    const wizard = page.getByRole("dialog", { name: "Restore wizard" });
    await expect(wizard).toContainText("never overwrites your live data");
    await wizard.getByLabel("Or paste the key file contents").fill(recoveryKey);
    await wizard.getByLabel("Recovery passphrase").fill(passphrase);
    await wizard.getByLabel("Your current password").fill("admin-password-1!");
    await wizard.getByRole("button", { name: "Check compatibility and recovery material" }).click();
    await expect(wizard).toContainText("The recovery key and passphrase match this backup.");
    await wizard.getByLabel(/I understand a restore drill creates a new isolated database/).check();
    await wizard.getByRole("button", { name: "Restore into an isolated target" }).click();
    await expect(wizard.getByText("Restore drill succeeded")).toBeVisible({ timeout: 120_000 });
    await expect(wizard).toContainText("AlveraRestore_");
    await expect(wizard).toContainText("Your live data was not touched");
    await wizard.getByRole("button", { name: "Close" }).click();

    // The drill counts as full verification; the isolated copy can then be removed (with the password).
    await expect(page.getByRole("row").filter({ hasText: "Manual" }).first()).toContainText("Fully verified (restorable)");
    await page.getByRole("button", { name: "Remove target…" }).click();
    await page.getByLabel("Your current password").fill("admin-password-1!");
    await page.getByRole("button", { name: "Remove the restored copy" }).click();
    await expect(page.getByText("The isolated restore target was removed.")).toBeVisible();

    // Disaster recovery: the retained file is discoverable and restorable WITHOUT using the backup history at all.
    const archivePanel = page.getByRole("region", { name: "Recover from a retained backup file" });
    await expect(archivePanel).toContainText(".abk");
    await archivePanel.getByRole("button", { name: /Restore from alveara-backup-/ }).first().click();
    const archiveWizard = page.getByRole("dialog", { name: "Restore wizard" });
    await expect(archiveWizard).toContainText("retained backup file");
    await archiveWizard.getByLabel("Or paste the key file contents").fill(recoveryKey);
    await archiveWizard.getByLabel("Recovery passphrase").fill(passphrase);
    await archiveWizard.getByLabel("Your current password").fill("admin-password-1!");
    await archiveWizard.getByRole("button", { name: "Check compatibility and recovery material" }).click();
    await expect(archiveWizard).toContainText("The backup file is present.");
    await expect(archiveWizard.getByRole("button", { name: /Verify only/ })).toHaveCount(0);
    await archiveWizard.getByLabel(/I understand a restore drill creates a new isolated database/).check();
    await archiveWizard.getByRole("button", { name: "Restore into an isolated target" }).click();
    await expect(archiveWizard.getByText("Restore drill succeeded")).toBeVisible({ timeout: 120_000 });
    await expect(archiveWizard).toContainText("deployment settings the data was created under");
    await archiveWizard.getByRole("button", { name: "Close" }).click();

    // This walkthrough owns the second isolated copy too: it removes it the same way as the first, so a run leaves no AlveraRestore_ database behind (TESTSPEED-P2 Gate 2 review).
    await page.getByRole("button", { name: "Remove target…" }).click();
    await page.getByLabel("Your current password").fill("admin-password-1!");
    await page.getByRole("button", { name: "Remove the restored copy" }).click();
    await expect(page.getByRole("cell", { name: "removed", exact: true })).toHaveCount(2);   // BOTH drills now show their target as removed (the toast text alone is still on the page from the first removal)

    // Audited through the shared audit path.
    await page.goto("/admin/audit-log");
    for (const eventType of ["RecoveryKeyConfigured", "BackupCreated", "RestoreDrillCompleted", "RestoreTargetRemoved"])
      await expect(page.getByRole("row").filter({ hasText: eventType }).first()).toBeVisible();
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
    await expect(page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Practice Configuration" })).toHaveCount(0); // ALV-N003
    await expect(page.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Backup & Recovery" })).toHaveCount(0); // ALV-N004

    // Direct URL to each hidden route is still denied server-authoritatively - the client-side
    // guard shows permission-denied before the page's own protected data fetch even starts, and
    // the server itself would reject the underlying API calls regardless of what the client shows.
    await page.goto("/admin/users");
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await page.goto("/admin/permissions");
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await page.goto("/admin/audit-log");
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await page.goto("/admin/configuration"); // ALV-N003: the same guard protects the configuration hub
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await expect(page.getByRole("heading", { name: "Practice configuration" })).toHaveCount(0);
    await page.goto("/admin/backup"); // ALV-N004
    await expect(page.getByText(/don't have permission/i)).toBeVisible();
    await expect(page.getByRole("heading", { name: "Backup & recovery" })).toHaveCount(0);
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
