import { execFileSync } from "node:child_process";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { createAccount } from "./support/testAccounts";

// ALV-N030 S14 (brief section 11; client slice 6): the account bootstrap in a real browser against the real application (ChainHost under Test, started by
// scripts/Invoke-RealBackendWalkthrough.ps1 on a fresh AlveraWalk_* database). The setup token comes from the product's own installer command
// (`Alveara.Api setup issue-token`) run against this walkthrough's database - never from a fixture - and is typed into the setup page; it never appears in a URL.

test.describe.configure({ mode: "serial" });

const PASSWORD = "Walkthrough-Pass-1!";
const DEPLOY = { username: "deploy-admin", password: "Deployment-Admin-Pass-1" };
const stamp = Date.now();
const upgradeAdmin = `upgrade-admin-${stamp}`;
const owner = { displayName: `Dr Owner ${stamp}`, username: `dr-owner-${stamp}` };

let browserRef: Browser;
let deployContext: BrowserContext;
let deploy: Page;
let adminContext: BrowserContext;
let admin: Page;
let temporaryPassword = "";

/** The installer's command, run exactly as the installer would, against this walkthrough's database. Prints the token on its own line. */
function issueSetupToken(): string {
  const database = process.env.E2E_DB_NAME ?? "";
  expect(database, "E2E_DB_NAME must be set by the runner").toMatch(/^AlveraWalk_[A-Za-z0-9_]+$/);
  const server = process.env.ALVERA_SQL_SERVER ?? ".\\SQLEXPRESS";
  const dll = path.resolve(process.cwd(), "..", "Alveara.Api", "bin", "Debug", "net10.0", "Alveara.Api.dll");
  const output = execFileSync("dotnet", [dll, "setup", "issue-token"], {
    encoding: "utf8",
    timeout: 60_000,
    env: { ...process.env, ConnectionStrings__Alveara: `Server=${server};Database=${database};Trusted_Connection=True;TrustServerCertificate=True`, ASPNETCORE_ENVIRONMENT: "Production" },
  });
  const token = output.split(/\r?\n/).map((l) => l.trim()).find((l) => /^[0-9a-f]{64}$/.test(l));
  expect(token, "the setup command printed a token").toBeTruthy();
  return token!;
}

function shot(page: Page, name: string) {
  const dir = process.env.ACCOUNT_BOOTSTRAP_E2E_OUT ?? test.info().outputDir;
  mkdirSync(dir, { recursive: true });
  return page.screenshot({ path: path.join(dir, name), fullPage: true });
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  deployContext = await browser.newContext();
  deploy = await deployContext.newPage();
});

test.afterAll(async () => {
  await adminContext.close();
  await deployContext.close();
});

test("S14-a an administrator of an upgraded practice sees the Practice Owner transition notice", async () => {
  // an existing administrator with no Deployment Administrator and no owner: the state an upgraded installation is in
  expect((await createAccount(admin.request, { username: upgradeAdmin, password: PASSWORD, role: "Admin", withStaffProfile: true })).ok()).toBeTruthy();
  await signIn(admin, upgradeAdmin, PASSWORD);
  await expect(admin.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(admin.getByRole("button", { name: "Designate the Practice Owner" })).toBeVisible();
  await shot(admin, "01-upgrade-notice.png");
});

test("S14-b first-run setup: the installer's token on the setup page creates the Deployment Administrator, then sign-in", async () => {
  const token = issueSetupToken();
  await deploy.goto("/setup");
  await expect(deploy.getByRole("heading", { name: "First-run setup" })).toBeVisible();
  await deploy.getByLabel("Setup token").fill(token);
  await deploy.getByLabel("Username").fill(DEPLOY.username);
  await deploy.getByLabel(/^Password/).fill(DEPLOY.password);
  await deploy.getByLabel("Confirm the password").fill(DEPLOY.password);
  await shot(deploy, "02-setup-page.png");
  await deploy.getByRole("button", { name: "Create the Deployment Administrator" }).click();
  await expect(deploy).toHaveURL(/\/login\?setup=complete$/);
  expect(deploy.url()).not.toContain(token);
  await expect(deploy.getByText("Setup is complete.")).toBeVisible();

  await signIn(deploy, DEPLOY.username, DEPLOY.password);
  await expect(deploy.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(deploy.getByText(/No Practice Owner has been created yet\. We recommend creating one before you configure the practice/)).toBeVisible();
  await shot(deploy, "03-deployment-administrator-notice.png");
});

test("S14-c the setup page refuses once setup is complete", async () => {
  const page = await (await browserRef.newContext()).newPage();
  await page.goto("/setup");
  await expect(page.getByText("This installation has already been set up.")).toBeVisible();
  await expect(page.getByLabel("Setup token")).toHaveCount(0);
  await page.context().close();
});

test("S14-d the Deployment Administrator adds the Practice Owner as a staff member and gets a temporary password once", async () => {
  await deploy.goto("/admin/configuration");
  await deploy.getByRole("tab", { name: "Staff" }).click();
  await deploy.getByRole("button", { name: "Add staff member" }).click();
  await expect(deploy.getByLabel(/Login account/)).toHaveCount(0);                               // no "link an existing account" picker
  await deploy.getByLabel("Display name").fill(owner.displayName);
  await deploy.getByLabel("Job title").fill("Dentist");
  await deploy.getByLabel("Username").fill(owner.username);
  await deploy.getByLabel("Role").selectOption("Dentist");
  await deploy.getByLabel("Make this person the Practice Owner").check();
  await deploy.getByRole("button", { name: "Add staff member" }).last().click();
  const shown = deploy.getByTestId("temporary-password");
  await expect(shown).toHaveText(/^.{16}$/);
  temporaryPassword = (await shown.textContent())!.trim();
  await expect(deploy.getByText("Give this password to the person; they must change it when they first sign in.")).toBeVisible();
  await shot(deploy, "04-temporary-password.png");
  await deploy.getByRole("button", { name: "Done, hide the password" }).click();
  await expect(deploy.getByTestId("temporary-password")).toHaveCount(0);

  await deploy.goto("/");
  await expect(deploy.getByText(/No Practice Owner has been created yet/)).toHaveCount(0);   // an owner exists now

  await deploy.goto("/admin/users");
  const deployRow = deploy.getByRole("row").filter({ hasText: DEPLOY.username });
  await expect(deployRow).toContainText("Deployment Administrator");
  const ownerRow = deploy.getByRole("row").filter({ hasText: owner.username });
  await expect(ownerRow).toContainText("Practice Owner");
  await expect(ownerRow).toContainText("Must change password");
  await shot(deploy, "05-security-administration-labels.png");
});

test("S14-e the new staff member's first sign-in requires choosing a password, then the app opens", async () => {
  const context = await browserRef.newContext();
  const page = await context.newPage();
  try {
    await signIn(page, owner.username, temporaryPassword);
    await expect(page).toHaveURL(/\/change-password$/);
    await expect(page.getByRole("heading", { name: "Choose a new password" })).toBeVisible();
    await page.goto("/patients");                                                                  // every other page sends it back
    await expect(page).toHaveURL(/\/change-password$/);
    await shot(page, "06-choose-a-new-password.png");
    await page.getByLabel("Temporary password").fill(temporaryPassword);
    await page.getByLabel(/^New password/).fill("Owner-Chosen-Pass-1");
    await page.getByLabel("Confirm the new password").fill("Owner-Chosen-Pass-1");
    await page.getByRole("button", { name: "Save my new password" }).click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await shot(page, "07-dashboard-after-change.png");
  } finally {
    await context.close();
  }
});

test("S14-f the transition notice is gone for the upgraded practice's administrator once an owner exists", async () => {
  await admin.goto("/");
  await expect(admin.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(admin.getByRole("button", { name: "Designate the Practice Owner" })).toHaveCount(0);
});
