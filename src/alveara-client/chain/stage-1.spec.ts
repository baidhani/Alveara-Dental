import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";

// ALV-N020 stage 1 (sign-in, accounts and session) and the minimal part of stage 2 (two patients) of the accumulated chain (R05 PKG-02/PKG-03, cases
// 1.01-1.06, 1.10-1.12, F-021, F-037, and the browser-level outage case 13.05). Runs only through scripts/Invoke-AccumulatedChain.ps1, after stage 0 created
// the administrator `stage0-admin` on the run's own throw-away database. Step titles start with their source id; the evidence builder reads it.
// Cost: case 1.12 waits for a real 5-minute session (the shortest per-user timeout; PD-32: the injected clock never controls sign-in expiry), so this
// stage takes about 6-7 minutes.

test.describe.configure({ mode: "serial" });

const ADMIN = "stage0-admin";
const ADMIN_PASSWORD = "stage0-admin-password-1!";
const PASSWORD = "stage1-user-password-1!";

let browserRef: Browser;
let adminContext: BrowserContext;
let admin: Page;
const ids: Record<string, string> = {};
const patients: Record<string, string> = {};

async function csrf(page: Page): Promise<string> {
  return (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
}

async function call(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, unknown> };
}

async function signIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

async function newSession(username: string, password = PASSWORD): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browserRef.newContext();
  const page = await context.newPage();
  await signIn(page, username, password);
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  return { context, page };
}

/** Registers an account, then (unless `keepDisabled`) gives it a role and enables it, as the administrator. */
async function createUser(username: string, role: string, keepDisabled = false): Promise<string> {
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.status(), `register ${username}`).toBe(201);
  const id = (await reg.json()).id as string;
  if (!keepDisabled) {
    expect((await call(admin, "put", `/api/auth/${id}/role`, { role })).status).toBe(200);
    expect((await call(admin, "put", `/api/auth/${id}/enabled`, { enabled: true })).status).toBe(200);
  }
  return id;
}

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
});

test.afterAll(async () => {
  await adminContext?.close();
});

test("1.01 a first visit to a protected page shows the plain sign-in page, never 'session expired' (F-007)", async ({ page }) => {
  await page.goto("/admin/users");
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByText(/your session expired/i)).toHaveCount(0);
});

test("1.02 a wrong password and an unknown name get the same plain refusal", async ({ page }) => {
  for (const name of [ADMIN, "no-such-user"]) {
    await signIn(page, name, "wrong-password");
    await expect(page.getByText("Invalid username or password.")).toBeVisible();
  }
});

test("1.03 the administrator signs in (with spaces around the name, F-151) and sees the admin menu", async () => {
  await signIn(admin, `  ${ADMIN} `, ADMIN_PASSWORD);
  await expect(admin.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(admin.getByRole("link", { name: "Security Administration" })).toBeVisible();
  ids.dentist = await createUser("chain-dentist", "Dentist");
  ids.front = await createUser("chain-front", "FrontDesk");
  ids.billing = await createUser("chain-billing", "Billing");
  ids.pending = await createUser("chain-pending", "Dentist", true);
});

test("1.04 an account that is still disabled cannot sign in", async ({ page }) => {
  await signIn(page, "chain-pending", PASSWORD);
  await expect(page.getByText(/this account has been disabled/i)).toBeVisible();
});

test("1.05 disabling billing1 refuses its sign-in; enabling it again lets it in", async ({ page }) => {
  expect((await call(admin, "put", `/api/auth/${ids.billing}/enabled`, { enabled: false })).status).toBe(200);
  await signIn(page, "chain-billing", PASSWORD);
  await expect(page.getByText(/this account has been disabled/i)).toBeVisible();
  expect((await call(admin, "put", `/api/auth/${ids.billing}/enabled`, { enabled: true })).status).toBe(200);
  await signIn(page, "chain-billing", PASSWORD);
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
});

test("1.06 revoking a signed-in user's sessions signs that browser out on its next action, with the expiry notice", async () => {
  const front = await newSession("chain-front");
  expect((await call(admin, "post", `/api/auth/${ids.front}/revoke-sessions`)).status).toBe(200);
  await front.page.getByRole("link", { name: "Patients" }).click();
  await expect(front.page.getByText(/your session expired/i)).toBeVisible();
  await front.context.close();
});

test("2.01 two synthetic patients are registered by the front desk (minimal stage 2)", async () => {
  const front = await newSession("chain-front");
  for (const [key, firstName, lastName, dob] of [["brian", "Brian", "Baker", "1979-11-02"], ["alice", "Alice", "Anderson", "1985-03-14"]] as const) {
    const r = await call(front.page, "post", "/api/patients",
      { firstName, lastName, dateOfBirth: dob, sex: "Male", phone: "555-010-0102", addressLine1: "1 Main St", city: "Springfield", state: "IL", postalCode: "62701" },
      { "Idempotency-Key": `chain-stage1-${key}` });
    expect(r.status, `register ${firstName}`).toBe(201);
    patients[key] = r.body.id as string;
  }
  await front.context.close();
});

test("F-037 after the front desk signs out on a patient page, the next person lands on the dashboard with no patient shown", async () => {
  const shared = await browserRef.newContext();                                                           // one browser window used by two people
  const page = await shared.newPage();
  await signIn(page, "chain-front", PASSWORD);
  await page.goto(`/patients/${patients.brian}`);
  await expect(page.getByText("Brian Baker").first()).toBeVisible();
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();

  await signIn(page, ADMIN, ADMIN_PASSWORD);
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByText("Brian Baker")).toHaveCount(0);
  await shared.close();
});

test("1.10 after sign-out the browser Back button shows the sign-in page and no patient information", async () => {
  const { context, page } = await newSession("chain-front");
  await page.goto(`/patients/${patients.alice}`);
  await expect(page.getByText("Alice Anderson").first()).toBeVisible();
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page.getByText("Alice Anderson")).toHaveCount(0);
  await expect(page.getByText("1985-03-14")).toHaveCount(0);
  await context.close();
});

test("F-021 inactivating a staff profile disables its login and ends its session; reactivating the profile does not re-enable it", async () => {
  const created = await call(admin, "post", "/api/config/staff", { displayName: "Dr. Chain Dentist", jobTitle: "Dentist", userAccountId: ids.dentist });
  expect(created.status).toBe(201);
  const dentist = await newSession("chain-dentist");

  const inactivated = await call(admin, "put", `/api/config/staff/${created.body.id}/active`, { isActive: false, rowVersion: created.body.rowVersion });
  expect(inactivated.status).toBe(200);

  await dentist.page.getByRole("link", { name: "Patients" }).click();                                   // the open session ends on its next action
  await expect(dentist.page.getByText(/your session expired/i)).toBeVisible();
  await signIn(dentist.page, "chain-dentist", PASSWORD);
  await expect(dentist.page.getByText(/this account has been disabled/i)).toBeVisible();

  const reactivated = await call(admin, "put", `/api/config/staff/${created.body.id}/active`, { isActive: true, rowVersion: inactivated.body.rowVersion });
  expect(reactivated.status).toBe(200);
  await signIn(dentist.page, "chain-dentist", PASSWORD);
  await expect(dentist.page.getByText(/this account has been disabled/i)).toBeVisible();          // still disabled: only an administrator re-enables it
  await dentist.context.close();
});

test("1.11 starting MFA enrolment without finishing leaves password sign-in unchanged", async () => {
  const { context, page } = await newSession("chain-billing");
  expect((await call(page, "post", "/api/auth/mfa/enroll", { currentPassword: PASSWORD })).status).toBe(200);
  await page.getByRole("button", { name: "Sign out" }).click();
  await signIn(page, "chain-billing", PASSWORD);
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();                         // no MFA prompt: enrolment was never confirmed
  await context.close();
});

test("13.05 a server outage keeps the signed-in screen and the typed form, and it recovers without signing in again (F-155, browser-level)", async () => {
  const { context, page } = await newSession("chain-front");
  await page.goto("/patients/register");
  await page.getByLabel("First name").fill("Typed During Outage");

  await page.route("**/api/**", (route) => route.fulfill({ status: 502, body: "Bad gateway" }));        // the API stops answering
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));                                  // the session check runs and fails
  await page.waitForTimeout(1_000);
  await expect(page.getByRole("heading", { name: "Sign in" })).toHaveCount(0);
  await expect(page.getByLabel("First name")).toHaveValue("Typed During Outage");

  await page.unroute("**/api/**");                                                                       // the API is back
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(page.getByLabel("First name")).toHaveValue("Typed During Outage");
  await expect(page.getByText(/your session expired/i)).toHaveCount(0);
  await context.close();
});

test("1.12 a short per-user timeout warns first, then signs out with the expiry message (fixed expiry unchanged)", async () => {
  test.setTimeout(9 * 60_000);
  expect((await call(admin, "put", `/api/auth/${ids.front}/session-timeout`, { sessionTimeoutMinutes: 5 })).status).toBe(200);
  const { context, page } = await newSession("chain-front");
  await page.goto("/patients/register");
  await page.getByLabel("First name").fill("Unsaved");

  await expect(page.getByText(/your session will expire soon/i)).toBeVisible({ timeout: 4 * 60_000 });   // about 3 minutes in
  await expect(page.getByText(/your session expired/i)).toBeVisible({ timeout: 3 * 60_000 });           // at 5 minutes (checked every 15 s)
  await expect(page).toHaveURL(/\/login\?reason=expired/);
  await context.close();
});
