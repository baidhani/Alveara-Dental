import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-015 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.treatment-plans.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the three acceptance items as a dentist would: from a patient's diagnosis, create a treatment plan that links to a catalog procedure and carries its fee (1); see a wrong entry refused beside its
// field with what was typed kept (2); and find every change in the history and the audit log with who and when (3). Then the fee is copied (a later catalog fee change does not rewrite the plan), a
// procedure and the plan are withdrawn with reasons, a hygienist can read but not change (and never touches the catalog), billing has no access, and the API refuses what the screen does not offer.

test.describe.configure({ mode: "serial" });

const OUT = process.env.TREATMENT_PLANS_E2E_OUT ?? path.join(process.cwd(), "treatment-plans-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
let patientId = "";
let encounterId = "";
let diagnosisId = "";
let perio: { id: string; rowVersion: string };
let filling: { id: string };
let planId = "";
const NAMES: Record<string, string> = { Dentist: "Dr. Dana Dentist" };

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
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
const createForm = (page: Page) => page.getByRole("form", { name: "Create a treatment plan" });
const card = (page: Page, title: string) => page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: new RegExp(title) }) });

async function gotoTab(page: Page) {
  await page.goto(`/patients/${patientId}/treatment-plan`);
  await expect(page.getByRole("heading", { name: "Treatment plan", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Plans on record" })).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "Billing", "FrontDesk"]) {
    const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    if (NAMES[role]) {
      expect((await api(admin, "post", "/api/config/staff", { displayName: `${NAMES[role]} ${stamp}`, userAccountId: userId })).status, `staff profile for ${role}`).toBe(201);
      NAMES[role] = `${NAMES[role]} ${stamp}`;
    }
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "treatment-plans-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a patient with an encounter and a current diagnosis, and two catalog procedures", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const p = await api(front, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `plan-ann-${stamp}` });
  expect(p.status).toBe(201);
  patientId = p.body.id;
  const e = await api(dentist, "post", `/api/patients/${patientId}/encounters`, {}, { "Idempotency-Key": `plan-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  encounterId = e.body.id;
  const d = await api(dentist, "post", `/api/patients/${patientId}/diagnoses`, { idempotencyKey: `plan-dx-${stamp}`, encounterId, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(d.status).toBe(200);
  diagnosisId = d.body.id;
  const a = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", fee: 120 });
  const b = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "FILL-1", description: "Surface filling", category: "Restorative", scope: "ToothSurface", fee: 80 });
  expect([a.status, b.status]).toEqual([200, 200]);
  perio = { id: a.body.summary.id, rowVersion: a.body.summary.rowVersion };
  filling = { id: b.body.summary.id };
});

test("ACCEPTANCE 1: from the patient's diagnosis a plan is created that links to a catalog procedure and carries its fee estimate", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await expect(page.getByText("No treatment plan has been created for this patient.")).toBeVisible();
  const f = createForm(page);
  await f.getByLabel("Plan title").fill("Gum health plan");
  await f.getByLabel(/^Diagnosis this procedure/).selectOption({ label: "Chronic periodontitis" });
  await f.getByLabel(/^Procedure/).selectOption({ label: "PERIO-1 - Periodontal maintenance ($120.00)" });
  await f.getByRole("button", { name: "Create treatment plan" }).click();
  await expect(statusLine(page)).toContainText('Treatment plan "Gum health plan" created.');
  const c = card(page, "Gum health plan");
  await expect(c).toContainText("PERIO-1");
  await expect(c).toContainText("For: Chronic periodontitis");
  await expect(c).toContainText("Fee $120.00");
  await expect(c).toContainText("Estimate total: $120.00");
  await expect(c).toContainText("not an insurance estimate");
  await shot(page, "01-plan-created.png");

  const plans = (await api(page, "get", `/api/patients/${patientId}/treatment-plans`)).body.plans as any[];
  expect(plans).toHaveLength(1);
  planId = plans[0].id;
  const item = plans[0].items[0];
  expect([item.diagnosisId, item.procedureId, item.fee, plans[0].estimateTotal, plans[0].status]).toEqual([diagnosisId, perio.id, 120, 120, "Proposed"]);
  evidence.acceptance1 = { diagnosisId: item.diagnosisId, procedureId: item.procedureId, fee: item.fee, total: plans[0].estimateTotal };
  await scanBothThemes(page, "author");
});

test("ACCEPTANCE 2: a wrong entry prompts for correction beside its field, keeps what was typed, and saves nothing", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const f = createForm(page);
  await f.getByRole("button", { name: "Create treatment plan" }).click();
  await expect(f.locator("span.alv-tp__error", { hasText: "Give the plan a title." })).toBeVisible();
  await expect(f.locator("span.alv-tp__error", { hasText: "Choose the diagnosis this procedure is for." })).toBeVisible();
  await shot(page, "02-gaps-named.png");

  // an incisal surface does not exist on a molar: only the server knows, and it says so beside the field
  await page.getByRole("button", { name: "Add procedure" }).click();
  const add = page.getByRole("form", { name: "Add a procedure to this treatment plan" });
  await add.getByLabel(/^Diagnosis this procedure/).selectOption({ label: "Chronic periodontitis" });
  await add.getByLabel(/^Procedure/).selectOption({ label: "FILL-1 - Surface filling ($80.00)" });
  await add.getByLabel(/^Tooth/).selectOption("16");
  await add.getByLabel(/^Surface/).selectOption("I");
  await add.getByRole("button", { name: "Add procedure" }).click();
  await expect(add.locator("span.alv-tp__error", { hasText: "That surface does not exist on that tooth." })).toBeVisible();
  await expect(add.getByLabel(/^Tooth/)).toHaveValue("16");
  await expect(add.getByLabel(/^Surface/)).toHaveValue("I");
  await shot(page, "03-server-refusal.png");
  const plans = (await api(page, "get", `/api/patients/${patientId}/treatment-plans`)).body.plans as any[];
  expect([plans.length, plans[0].items.length]).toEqual([1, 1]);                                                    // nothing was saved
  evidence.acceptance2 = { refusedBesideField: "That surface does not exist on that tooth.", plansAfter: plans.length, itemsAfter: plans[0].items.length };

  await add.getByLabel(/^Surface/).selectOption("O");
  await add.getByRole("button", { name: "Add procedure" }).click();
  await expect(statusLine(page)).toContainText("Procedure added to the plan.");
  const c = card(page, "Gum health plan");
  await expect(c).toContainText("FILL-1");
  await expect(c).toContainText("Tooth 3, surface O");     // stored as FDI 16, shown in the default Universal numbering
  await expect(c).toContainText("Estimate total: $200.00");
  await shot(page, "04-procedure-added.png");
});

test("the fee is copied: a later catalog fee change does not rewrite the plan", async () => {
  const page = sessions.Dentist.page;
  const revised = await api(admin, "post", `/api/procedures/${perio.id}/revise`, { codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", fee: 150, reason: "Annual fee review", rowVersion: perio.rowVersion });
  expect(revised.status).toBe(200);
  await gotoTab(page);
  const c = card(page, "Gum health plan");
  await expect(c).toContainText("Fee $120.00 (catalog version 1)");
  await expect(c).toContainText("Estimate total: $200.00");
  evidence.feeCopied = { catalogFeeNow: 150, planFeeStill: 120 };
});

test("a procedure is withdrawn only with a reason and leaves the estimate total", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await page.getByRole("button", { name: "Withdraw FILL-1" }).click();
  const f = page.getByRole("form", { name: "Withdraw FILL-1 from this plan" });
  await expect(f.getByRole("button", { name: "Withdraw procedure" })).toBeDisabled();
  await f.getByLabel("Why is this procedure being withdrawn?").fill("Patient declined");
  await f.getByRole("button", { name: "Withdraw procedure" }).click();
  await expect(statusLine(page)).toContainText("FILL-1 withdrawn from the plan.");
  const c = card(page, "Gum health plan");
  await expect(c).toContainText("Patient declined");
  await expect(c).toContainText("Estimate total: $120.00");
  await shot(page, "05-procedure-withdrawn.png");
});

test("a plan can be renamed", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await page.getByRole("button", { name: "Rename plan" }).click();
  const f = page.getByRole("form", { name: "Rename this treatment plan" });
  await f.getByLabel("New title").fill("Periodontal care plan");
  await f.getByRole("button", { name: "Save title" }).click();
  await expect(statusLine(page)).toContainText("Treatment plan renamed.");
  await expect(card(page, "Periodontal care plan")).toBeVisible();
});

test("ACCEPTANCE 3: every plan change is in the history and the audit log with who and when", async () => {
  const page = sessions.Dentist.page;
  const history = (await api(page, "get", `/api/treatment-plans/${planId}/history`)).body as any[];
  expect(history.map((h) => h.changeType)).toEqual(["Created", "ItemAdded", "ItemAdded", "ItemWithdrawn", "Renamed"]);      // the refused add left no event
  for (const h of history) {
    expect(h.actorName, "who").toBe(NAMES.Dentist);
    expect(Date.parse(h.occurredAtUtc), "when").toBeGreaterThan(0);
  }
  const audit = JSON.stringify((await api(admin, "get", "/api/auth/audit-log")).body);
  expect(audit).toContain("TreatmentPlan");
  evidence.acceptance3 = { events: history.map((h) => ({ n: h.eventNumber, type: h.changeType, actor: h.actorName, at: h.occurredAtUtc })), auditContainsTreatmentPlan: true };
});

test("a hygienist can read the plan but is offered no way to change it, and never touches the catalog", async () => {
  const page = sessions.Hygienist.page;
  const catalogCalls: string[] = [];
  page.on("request", (r) => { if (r.url().includes("/api/procedures")) catalogCalls.push(r.url()); });
  await gotoTab(page);
  const c = card(page, "Periodontal care plan");
  await expect(c).toContainText("Estimate total: $120.00");
  await expect(c).toContainText("Patient declined");
  await expect(page.getByRole("form")).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Add procedure|Rename plan|Withdraw/ })).toHaveCount(0);
  await expect(statusLine(page)).toContainText("your role cannot change it");
  expect(catalogCalls).toEqual([]);
  await shot(page, "06-hygienist-read-only.png");
  await scanBothThemes(page, "read-only");
  expect((await api(page, "post", `/api/treatment-plans/${planId}/rename`, { title: "x", rowVersion: "AAAAAAAAAAA=" })).status).toBe(403);
});

test("billing has no treatment plan tab and is refused at the address and the API", async () => {
  const page = sessions.Billing.page;
  await page.goto(`/patients/${patientId}/details`);
  await expect(page.getByRole("link", { name: "Treatment plan" })).toHaveCount(0);
  await page.goto(`/patients/${patientId}/treatment-plan`);
  await expect(page.getByText(/permission/i).first()).toBeVisible();
  expect((await api(page, "get", `/api/patients/${patientId}/treatment-plans`)).status).toBe(403);
  await shot(page, "07-billing-denied.png");
});

test("the API refuses what the screen does not offer", async () => {
  const dentist = sessions.Dentist.page;
  const plan = (await api(dentist, "get", `/api/treatment-plans/${planId}`)).body;
  expect((await api(dentist, "post", `/api/treatment-plans/${planId}/rename`, { title: "No CSRF", rowVersion: plan.rowVersion }, {}, false)).status).toBe(400);
  expect((await api(dentist, "post", `/api/treatment-plans/${planId}/rename`, { title: "Fresh", rowVersion: plan.rowVersion })).status).toBe(200);
  const stale = await api(dentist, "post", `/api/treatment-plans/${planId}/rename`, { title: "Stale", rowVersion: plan.rowVersion });
  expect([stale.status, stale.body.error]).toEqual([409, "concurrency_conflict"]);
  const none = await api(dentist, "post", `/api/patients/${patientId}/treatment-plans`, { idempotencyKey: `empty-${stamp}`, title: "Empty", items: [] });
  expect([none.status, none.body.error]).toEqual([400, "validation_failed"]);
  expect((await api(dentist, "get", `/api/treatment-plans/00000000-0000-0000-0000-00000000dead`)).status).toBe(404);
});

test("a plan is withdrawn only with a reason, stays on record, and can no longer be changed", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await page.getByRole("button", { name: "Withdraw plan" }).click();
  const f = page.getByRole("form", { name: "Withdraw this treatment plan" });
  await expect(f.getByRole("button", { name: "Withdraw plan" })).toBeDisabled();
  await f.getByLabel("Why is this plan being withdrawn?").fill("Entered on the wrong patient");
  await f.getByRole("button", { name: "Withdraw plan" }).click();
  await expect(statusLine(page)).toContainText("Treatment plan withdrawn.");
  await expect(page.getByText("No treatment plan has been created for this patient.")).toBeVisible();          // hidden by default
  await page.getByLabel("Show withdrawn plans").check();
  const c = card(page, "Fresh");
  await expect(c).toContainText("Withdrawn");
  await expect(c).toContainText("Entered on the wrong patient");
  await expect(c.getByRole("button", { name: /Add procedure|Rename plan|Withdraw/ })).toHaveCount(0);
  await shot(page, "08-plan-withdrawn.png");
  const plan = (await api(page, "get", `/api/treatment-plans/${planId}`)).body;
  const late = await api(page, "post", `/api/treatment-plans/${planId}/rename`, { title: "Late", rowVersion: plan.rowVersion });
  expect([late.status, late.body.error]).toEqual([409, "plan_withdrawn"]);
});
