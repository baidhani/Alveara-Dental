import { execFileSync } from "node:child_process";
import { mkdirSync } from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";
import type { Browser, Page } from "@playwright/test";
import { createAccount } from "./support/testAccounts";

// ALV-N030-C01 (brief 18.11, R08-4; review R10): the targeted transfer and recovery walkthrough in a real browser against the real application (ChainHost under
// Test on a fresh, set-up AlveraWalk_* database with no Deployment Administrator - an upgraded installation). The recovery token comes from the product's own
// installer command (`Alveara.Api recovery issue-token`), never from a fixture, and is typed into the recovery page; it never appears in a URL.
// 1. Recovery creates the Deployment Administrator (R1). 2. The Deployment Administrator transfers ownership A -> B; A's open session ends.
// 3. A disables the Deployment Administrator; a new recovery token and the recovery page reset it (R2), and it signs in with the new password.

test.describe.configure({ mode: "serial" });

const PASSWORD = "Walkthrough-Pass-1!";
const stamp = Date.now();
const DEPLOY = { username: `deploy-${stamp}`, first: "Recovered-Deploy-Pass-1", second: "Recovered-Deploy-Pass-2" };
const ownerA = `owner-a-${stamp}`;
const ownerB = `owner-b-${stamp}`;

let browserRef: Browser;
let deploy: Page;
let ownerPage: Page;

/** The installer's command, run exactly as the installer would, against this walkthrough's database. Prints the token on its own line. */
function issueRecoveryToken(): string {
  const database = process.env.E2E_DB_NAME ?? "";
  expect(database, "E2E_DB_NAME must be set by the runner").toMatch(/^AlveraWalk_[A-Za-z0-9_]+$/);
  const server = process.env.ALVERA_SQL_SERVER ?? ".\\SQLEXPRESS";
  const dll = path.resolve(process.cwd(), "..", "Alveara.Api", "bin", "Debug", "net10.0", "Alveara.Api.dll");
  const output = execFileSync("dotnet", [dll, "recovery", "issue-token"], {
    encoding: "utf8",
    timeout: 60_000,
    env: { ...process.env, ConnectionStrings__Alveara: `Server=${server};Database=${database};Trusted_Connection=True;TrustServerCertificate=True`, ASPNETCORE_ENVIRONMENT: "Production" },
  });
  const token = output.split(/\r?\n/).map((l) => l.trim()).find((l) => /^[0-9a-f]{64}$/.test(l));
  expect(token, "the recovery command printed a token").toBeTruthy();
  return token!;
}

function shot(page: Page, name: string) {
  const dir = process.env.OWNER_TRANSFER_E2E_OUT ?? test.info().outputDir;
  mkdirSync(dir, { recursive: true });
  return page.screenshot({ path: path.join(dir, name), fullPage: true });
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

async function recover(page: Page, token: string, username: string, password: string) {
  await page.goto("/recovery");
  await page.getByLabel("Recovery token").fill(token);
  await page.getByLabel("Username").fill(username);
  await page.getByLabel(/^New password/).fill(password);
  await page.getByLabel("Confirm the new password").fill(password);
  await page.getByRole("button", { name: "Recover the Deployment Administrator" }).click();
}

/** A JSON call from the page's own signed-in session (cookies and the CSRF token). */
async function call(page: Page, method: string, url: string, body?: unknown) {
  const csrf = (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
  return page.request.fetch(url, { method, headers: { "X-CSRF-Token": csrf, "Content-Type": "application/json" }, data: body === undefined ? undefined : JSON.stringify(body) });
}

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  deploy = await (await browser.newContext()).newPage();
  ownerPage = await (await browser.newContext()).newPage();
});

test.afterAll(async () => {
  await deploy.context().close();
  await ownerPage.context().close();
});

test("C01-a recovery creates the Deployment Administrator on an installation that has none", async () => {
  const token = issueRecoveryToken();
  await recover(deploy, token, DEPLOY.username, DEPLOY.first);
  await expect(deploy.getByText(new RegExp(`The Deployment Administrator "${DEPLOY.username}" was created`))).toBeVisible();
  expect(deploy.url()).not.toContain(token);
  await shot(deploy, "01-recovery-created.png");
  await signIn(deploy, DEPLOY.username, DEPLOY.first);
});

test("C01-b the Deployment Administrator transfers ownership; the former owner's session ends", async () => {
  for (const username of [ownerA, ownerB])
    expect((await createAccount(deploy.request, { username, password: PASSWORD, role: "Admin", withStaffProfile: true })).ok()).toBeTruthy();
  const staff = await (await deploy.request.get("/api/config/staff")).json() as { id: string; displayName: string }[];
  const staffA = staff.find((s) => s.displayName === ownerA)!;
  expect((await call(deploy, "POST", "/api/practice-owner/initial", { staffProfileId: staffA.id, confirm: true })).status()).toBe(201);

  await signIn(ownerPage, ownerA, PASSWORD);

  await deploy.goto("/admin/users");
  await expect(deploy.getByRole("row").filter({ hasText: ownerA })).toContainText("Practice Owner");
  await expect(deploy.getByLabel(`Role for ${ownerA}`)).toBeDisabled();                        // the owner's role is locked until a transfer
  await deploy.getByRole("button", { name: "Transfer Practice Owner" }).click();
  const dialog = deploy.getByRole("dialog", { name: "Transfer Practice Owner" });
  await expect(dialog.getByText(ownerA)).toBeVisible();
  await dialog.getByLabel("New Practice Owner").selectOption({ label: ownerB });
  await dialog.getByLabel(/^Reason/).fill("Walkthrough: the practice changed hands.");
  await dialog.getByLabel(/^Your password/).fill(DEPLOY.first);
  await dialog.getByLabel(/I am transferring the Practice Owner designation/).check();
  await shot(deploy, "02-transfer-dialog.png");
  await dialog.getByRole("button", { name: "Transfer" }).click();
  await expect(deploy.getByText(`${ownerB} is now the Practice Owner.`)).toBeVisible();
  await expect(deploy.getByRole("row").filter({ hasText: ownerB })).toContainText("Practice Owner");
  await shot(deploy, "03-transferred.png");

  // the former owner's open session ended: the next page load goes to sign-in
  await ownerPage.goto("/patients");
  await expect(ownerPage).toHaveURL(/\/login/);
});

test("C01-c a console token resets a disabled Deployment Administrator, who then signs in with the new password", async () => {
  const deployId = ((await (await deploy.request.get("/api/auth/users")).json()) as { id: string; username: string }[]).find((u) => u.username === DEPLOY.username)!.id;
  await signIn(ownerPage, ownerA, PASSWORD);                                                      // an ordinary Administrator now
  expect((await call(ownerPage, "PUT", `/api/auth/${deployId}/enabled`, { enabled: false })).ok()).toBeTruthy();

  const page = await (await browserRef.newContext()).newPage();
  try {
    const token = issueRecoveryToken();
    await recover(page, token, DEPLOY.username, DEPLOY.second);
    await expect(page.getByText(/was reset: the new password is set, the account is enabled, and two-factor authentication is off/)).toBeVisible();
    await shot(page, "04-recovery-reset.png");
    await signIn(page, DEPLOY.username, DEPLOY.second);
  } finally {
    await page.context().close();
  }
});
