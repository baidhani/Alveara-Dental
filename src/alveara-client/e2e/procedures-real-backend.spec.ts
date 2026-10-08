import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N005 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.procedures.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the acceptance items as a billing person would: add a procedure with a code, description, category, applicability and fee; see a wrong entry refused beside its field with what was typed kept;
// change a fee (a new version - the old fee stays readable); inactivate with a reason and reactivate; see who did what in the history. A dentist can read but not change, a hygienist cannot see the catalog,
// and the API refuses what the screen does not offer (role matrix, CSRF, a stale version).

test.describe.configure({ mode: "serial" });

const OUT = process.env.PROCEDURES_E2E_OUT ?? path.join(process.cwd(), "procedures-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "proc-pass-1!";
const sessions: Record<string, { context: BrowserContext; page: Page }> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
let billingName = "";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, withCsrf = true) {
  const headers = method === "get" || !withCsrf ? {} : { "X-CSRF-Token": await csrf(page) };
  const response = await page.request[method](url, method === "get" ? {} : { headers, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

async function createUser(role: string, username: string): Promise<string> {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  return id as string;
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

async function setTheme(page: Page, theme: "light" | "dark") {
  await page.getByRole("button", { name: /Light|Dark/ }).waitFor();
  if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
  await page.waitForTimeout(300);
}

async function scanBothThemes(page: Page, label: string) {
  for (const theme of ["light", "dark"] as const) {
    await setTheme(page, theme);
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
    const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    (evidence.axe as Record<string, unknown>)[`${label}-${theme}`] = { blocking: blocking.length, violations: results.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length })) };
    expect(blocking.map((v) => `${v.id}:${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`), `${label} (${theme}): critical/serious axe violations`).toEqual([]);
  }
  await setTheme(page, "light");
}

const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const row = (page: Page, code: string) => page.getByRole("listitem").filter({ has: page.getByText(code, { exact: true }) });
const addForm = (page: Page) => page.getByRole("form", { name: "Add a procedure" });

async function gotoCatalog(page: Page) {
  await page.goto("/procedures");
  await expect(page.getByRole("heading", { name: "Procedures and fees" })).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Billing", "Dentist", "Hygienist"]) {
    const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    if (role === "Billing") {
      billingName = `Beth Billing ${stamp}`;
      expect((await api(admin, "post", "/api/config/staff", { displayName: billingName, userAccountId: userId })).status).toBe(201);
    }
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "procedures-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("billing finds the catalog in the navigation and adds a procedure with its fee", async () => {
  const page = sessions.Billing.page;
  await page.getByRole("link", { name: "Procedures & fees" }).click();
  await expect(page.getByRole("heading", { name: "Procedures and fees" })).toBeVisible();
  await expect(page.getByText("No procedures found")).toBeVisible();

  await page.getByRole("button", { name: "Add a procedure" }).click();
  await addForm(page).getByRole("button", { name: "Add procedure" }).click();
  await expect(addForm(page).getByText("A code is required.")).toBeVisible();
  await expect(addForm(page).getByText("A fee is required (enter 0 for no charge).")).toBeVisible();

  await addForm(page).getByLabel("Code", { exact: true }).fill("D1110");
  await addForm(page).getByLabel("Description").fill("Looks like a CDT code");
  await addForm(page).getByLabel("Category").selectOption("Preventive");
  await addForm(page).getByLabel("Applies to").selectOption("WholeMouth");
  await addForm(page).getByLabel("Fee (US dollars)").fill("95");
  await addForm(page).getByRole("button", { name: "Add procedure" }).click();
  await expect(addForm(page).getByText(/must not look like a CDT code/)).toBeVisible();      // the server's refusal, beside the field
  await expect(addForm(page).getByLabel("Description")).toHaveValue("Looks like a CDT code");   // what was typed is kept
  await shot(page, "01-wrong-entry-refused.png");

  await addForm(page).getByLabel("Code", { exact: true }).fill("CLEAN-1");
  await addForm(page).getByLabel("Description").fill("Adult cleaning");
  await addForm(page).getByRole("button", { name: "Add procedure" }).click();
  await expect(statusLine(page)).toContainText("CLEAN-1 added to the catalog.");
  await expect(row(page, "CLEAN-1")).toContainText("$95.00");
  await expect(row(page, "CLEAN-1")).toContainText("Active");
  await shot(page, "02-added.png");
  await scanBothThemes(page, "catalog");
});

test("a fee change is a new version: the old fee stays readable, and the history says who and why", async () => {
  const page = sessions.Billing.page;
  await gotoCatalog(page);
  await page.getByRole("button", { name: "Change procedure CLEAN-1" }).click();
  const form = page.getByRole("form", { name: "Change procedure CLEAN-1" });
  await form.getByLabel("Fee (US dollars)").fill("105.50");
  await form.getByRole("button", { name: "Save change" }).click();
  await expect(form.getByText("Say why.")).toBeVisible();
  await form.getByLabel("Why is this being changed?").fill("Annual fee review");
  await form.getByRole("button", { name: "Save change" }).click();
  await expect(statusLine(page)).toContainText("CLEAN-1 changed.");
  await expect(row(page, "CLEAN-1")).toContainText("$105.50");

  await row(page, "CLEAN-1").getByText("Fee and change history").click();
  const versions = page.getByRole("list", { name: "Versions of CLEAN-1" });
  await expect(versions).toContainText("Version 1: $95.00");
  await expect(versions).toContainText("Version 2: $105.50");
  await expect(versions).toContainText("Reason: Annual fee review");
  await expect(page.getByRole("list", { name: "Changes to CLEAN-1" })).toContainText(billingName);
  await shot(page, "03-fee-history.png");
  await scanBothThemes(page, "history");
});

test("inactivating needs a reason, removes it from planning, and reactivating restores it", async () => {
  const page = sessions.Billing.page;
  await gotoCatalog(page);
  await page.getByRole("button", { name: "Inactivate procedure CLEAN-1" }).click();
  const form = page.getByRole("form", { name: "Inactivate procedure CLEAN-1" });
  await expect(form.getByRole("button", { name: "Inactivate procedure" })).toBeEnabled();
  await form.getByRole("button", { name: "Inactivate procedure" }).click();
  await expect(form.getByText("Say why.")).toBeVisible();
  await form.getByLabel("Why is CLEAN-1 being inactivated?").fill("No longer offered");
  await form.getByRole("button", { name: "Inactivate procedure" }).click();
  await expect(statusLine(page)).toContainText("CLEAN-1 inactivated.");
  await expect(row(page, "CLEAN-1")).toContainText("Inactive");
  expect((await api(page, "get", "/api/procedures/active")).body).toEqual([]);

  await page.getByRole("button", { name: "Reactivate procedure CLEAN-1" }).click();
  await page.getByRole("form", { name: "Reactivate procedure CLEAN-1" }).getByRole("button", { name: "Reactivate procedure" }).click();
  await expect(statusLine(page)).toContainText("CLEAN-1 reactivated.");
  expect(((await api(page, "get", "/api/procedures/active")).body as any[]).map((p) => p.code)).toEqual(["CLEAN-1"]);
  await shot(page, "04-reactivated.png");
});

test("a dentist can read the catalog but is offered no way to change it", async () => {
  const page = sessions.Dentist.page;
  await gotoCatalog(page);
  await expect(row(page, "CLEAN-1")).toContainText("$105.50");
  await expect(page.getByRole("button", { name: "Add a procedure" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Change procedure|Inactivate procedure/ })).toHaveCount(0);
  await expect(statusLine(page)).toContainText("your role cannot change it");
  await shot(page, "05-dentist-read-only.png");
  await scanBothThemes(page, "read-only");
});

test("a hygienist has no catalog link and is told permission is needed at the address", async () => {
  const page = sessions.Hygienist.page;
  await expect(page.getByRole("link", { name: "Procedures & fees" })).toHaveCount(0);
  await page.goto("/procedures");
  await expect(page.getByText(/permission/i).first()).toBeVisible();
  await shot(page, "06-hygienist-denied.png");
});

test("the API refuses what the screen does not offer", async () => {
  const dentist = sessions.Dentist.page;
  const hygienist = sessions.Hygienist.page;
  const billing = sessions.Billing.page;
  const list = (await api(billing, "get", "/api/procedures")).body as any[];
  const clean = list.find((p) => p.code === "CLEAN-1");

  expect((await api(dentist, "post", "/api/procedures", { codeSystem: "Local", code: "NOPE-1", description: "x", category: "Other", scope: "WholeMouth", fee: 1 })).status).toBe(403);
  expect((await api(hygienist, "get", "/api/procedures")).status).toBe(403);
  expect((await api(billing, "post", "/api/procedures", { codeSystem: "Local", code: "NOCSRF-1", description: "x", category: "Other", scope: "WholeMouth", fee: 1 }, false)).status).toBe(400);

  const revise = (fee: number, rowVersion: string) => api(billing, "post", `/api/procedures/${clean.id}/revise`, {
    codeSystem: "Local", code: "CLEAN-1", description: "Adult cleaning", category: "Preventive", scope: "WholeMouth", fee, reason: "Check", rowVersion });
  expect((await revise(110, clean.rowVersion)).status).toBe(200);
  expect((await revise(120, clean.rowVersion)).status).toBe(409);                      // the copy read before the change is stale
  const dup = await api(billing, "post", "/api/procedures", { codeSystem: "Local", code: "CLEAN-1", description: "Different", category: "Other", scope: "WholeMouth", fee: 1 });
  expect([dup.status, dup.body.error]).toEqual([409, "procedure_exists"]);
  const audit = (await api(admin, "get", "/api/auth/audit-log")).body as any;
  expect(JSON.stringify(audit)).toContain("ProcedureDefinition");
});
