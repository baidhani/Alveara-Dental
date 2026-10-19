import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { seedLegacyCompletion } from "./support/legacyCompletion";

// STORY-007 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.billing.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the three acceptance items as a billing person would: a completed procedure is billed and the charge carries the plan's fee, with no amount to type (1); a payment lowers the balance and the
// history keeps every entry as it was, with an over-payment refused beside its field (2); and every billing action is in the audit log with who and when (3). Then an adjustment with a reason, a payment from
// an out-of-date second screen (a conflict that changes nothing), the failure path (when the log cannot be written nothing is recorded and billing is told so, then the same save works), a dentist who can
// read but not change, a hygienist who cannot see billing, and the API refusing what the screen does not offer.

test.describe.configure({ mode: "serial" });

const OUT = process.env.BILLING_E2E_OUT ?? path.join(process.cwd(), "billing-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "bill-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
let patientId = "";
let planId = "";
let performed = "";      // the completed plan procedure
let accepted = "";       // accepted, not completed
const NAMES: Record<string, string> = { Dentist: "Dr. Dana Dentist", Billing: "Beth Billing" };

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

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log demonstration needs it)");
  execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
}

const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const paymentForm = (page: Page) => page.getByRole("form", { name: "Record a payment" });
const adjustmentForm = (page: Page) => page.getByRole("form", { name: "Record an adjustment" });
const ledger = (page: Page) => page.getByRole("table", { name: "Account history" });
const account = async (page: Page) => (await api(page, "get", `/api/patients/${patientId}/billing`)).body as any;

async function gotoBilling(page: Page) {
  await page.goto(`/patients/${patientId}/billing`);
  await expect(page.getByRole("heading", { name: "Billing", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Account history" })).toBeVisible();
}

async function pay(page: Page, amount: string, method = "Cash", note = "") {
  await paymentForm(page).getByLabel(/^Amount/).fill(amount);
  await paymentForm(page).getByLabel("How it was received").selectOption(method);
  if (note) await paymentForm(page).getByLabel("Note (optional)").fill(note);
  await paymentForm(page).getByRole("button", { name: "Record payment" }).click();
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
  writeFileSync(path.join(OUT, "billing-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a patient whose dentist has completed one procedure of a plan and accepted another", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const p = await api(front, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `bill-ann-${stamp}` });
  expect(p.status).toBe(201);
  patientId = p.body.id;
  const e = await api(dentist, "post", `/api/patients/${patientId}/encounters`, {}, { "Idempotency-Key": `bill-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  const d = await api(dentist, "post", `/api/patients/${patientId}/diagnoses`, { idempotencyKey: `bill-dx-${stamp}`, encounterId: e.body.id, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(d.status).toBe(200);
  const a = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", fee: 120 });
  const b = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "SCALE-1", description: "Scaling", category: "Periodontic", scope: "WholeMouth", fee: 70 });
  expect([a.status, b.status]).toEqual([200, 200]);
  const plan = await api(dentist, "post", `/api/patients/${patientId}/treatment-plans`, {
    idempotencyKey: `bill-plan-${stamp}`, title: "Gum health plan",
    items: [a, b].map((x) => ({ diagnosisId: d.body.id, procedureId: x.body.summary.id, toothKey: null, surface: null })),
  });
  expect(plan.status).toBe(200);
  planId = plan.body.id;
  performed = plan.body.items[0].id;
  accepted = plan.body.items[1].id;
  let step = await api(dentist, "post", `/api/treatment-plans/${planId}/items/${performed}/decision`, { decision: "Accepted", rowVersion: plan.body.rowVersion });
  step = await api(dentist, "post", `/api/treatment-plans/${planId}/items/${accepted}/decision`, { decision: "Accepted", rowVersion: step.body.rowVersion });
  expect(step.status).toBe(200);
  seedLegacyCompletion(planId, performed);                // completed the old way: the route is retired (410), so it is seeded (see support/legacyCompletion.ts)
});

test("ACCEPTANCE 1: a completed procedure is billed and the charge is recorded at the plan's fee, with no amount to type", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await expect(page.getByText("Balance owed: $0.00")).toBeVisible();
  await expect(page.getByText(/Nothing has been billed to this patient yet/)).toBeVisible();
  const ready = page.getByRole("list", { name: "Completed procedures ready to bill" });
  await expect(ready).toContainText("PERIO-1");
  await expect(ready).toContainText("$120.00");
  await expect(ready).not.toContainText("SCALE-1");                  // accepted but not completed: not offered
  await expect(page.getByRole("form")).toHaveCount(0);               // nothing to pay yet, so no payment or adjustment form
  await shot(page, "01-ready-to-bill.png");

  await page.getByRole("button", { name: "Bill PERIO-1" }).click();
  await expect(statusLine(page)).toContainText("PERIO-1 billed for $120.00. Balance owed is now $120.00.");
  await expect(page.getByText("Balance owed: $120.00")).toBeVisible();
  const row = ledger(page).getByRole("row").nth(1);
  await expect(row).toContainText("Charge");
  await expect(row).toContainText("PERIO-1 Periodontal maintenance");
  await expect(row).toContainText(NAMES.Billing);
  await expect(page.getByText("No completed procedure is waiting to be billed.")).toBeVisible();
  await shot(page, "02-charged.png");
  await scanBothThemes(page, "charged");

  const a = await account(page);
  expect(a.entries[0]).toMatchObject({ kind: "Charge", amount: 120, planItemId: performed, procedureCode: "PERIO-1" });
  evidence.acceptance1 = { entry: a.entries[0] };
});

test("the API refuses billing what the plan has not completed and billing it twice", async () => {
  const page = sessions.Billing.page;
  const notCompleted = await api(page, "post", `/api/patients/${patientId}/billing/charges`, { planItemId: accepted, idempotencyKey: `nc-${stamp}` });
  expect([notCompleted.status, notCompleted.body.error]).toEqual([409, "not_completed"]);
  const twice = await api(page, "post", `/api/patients/${patientId}/billing/charges`, { planItemId: performed, idempotencyKey: `again-${stamp}` });
  expect([twice.status, twice.body.error]).toEqual([409, "already_billed"]);
  const tampered = await api(page, "post", `/api/patients/${patientId}/billing/charges`, { planItemId: performed, idempotencyKey: `tamper-${stamp}`, amount: 1 });
  expect(tampered.status).toBe(409);                                  // an amount in the request changes nothing: the plan's fee is the only amount
  expect((await account(page)).entries).toHaveLength(1);
});

test("ACCEPTANCE 2: a payment lowers the balance and the history keeps every entry as it was; an over-payment is refused beside its field", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await pay(page, "50", "Card", "Front desk");
  await expect(statusLine(page)).toContainText("Payment of $50.00 recorded. Balance owed is now $70.00.");
  await expect(page.getByText("Balance owed: $70.00")).toBeVisible();
  const rows = ledger(page).getByRole("row");
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(1)).toContainText("$120.00");                 // the charge is exactly as it was
  await expect(rows.nth(2)).toContainText("Payment");
  await expect(rows.nth(2)).toContainText("Card - Front desk");
  await expect(rows.nth(2)).toContainText("-$50.00");
  await expect(rows.nth(2)).toContainText("$70.00");
  await shot(page, "03-payment-recorded.png");

  await pay(page, "500", "Cash");
  await expect(paymentForm(page).getByText(/A payment cannot be more than the balance owed \(70\.00 USD\)/)).toBeVisible();
  await expect(paymentForm(page).getByLabel(/^Amount/)).toHaveValue("500");     // what was typed is kept
  await expect(page.getByText("Balance owed: $70.00")).toBeVisible();
  await shot(page, "04-over-payment-refused.png");
  await scanBothThemes(page, "payment-refused");
  evidence.acceptance2 = { entries: (await account(page)).entries.map((e: any) => ({ n: e.entryNumber, kind: e.kind, amount: e.amount, balanceAfter: e.balanceAfter })) };
});

test("an adjustment corrects the balance with a reason and is added to the history", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await adjustmentForm(page).getByLabel(/^Amount/).fill("-10");
  await adjustmentForm(page).getByRole("button", { name: "Record adjustment" }).click();
  await expect(adjustmentForm(page).getByText("An adjustment needs a reason, so the history explains it.")).toBeVisible();   // named before anything is sent
  await adjustmentForm(page).getByLabel("Reason").fill("Courtesy discount");
  await adjustmentForm(page).getByRole("button", { name: "Record adjustment" }).click();
  await expect(statusLine(page)).toContainText("Adjustment of -$10.00 recorded. Balance owed is now $60.00.");
  await expect(ledger(page).getByRole("row")).toHaveCount(4);
  await expect(ledger(page).getByRole("row").nth(3)).toContainText("Courtesy discount");
  await shot(page, "05-adjustment.png");
  await scanBothThemes(page, "adjusted");
});

test("a payment made from an out-of-date second screen is a conflict that changes nothing", async () => {
  const stale = admin;
  await gotoBilling(stale);                                             // a second person has the account open, balance $60.00
  await expect(stale.getByText("Balance owed: $60.00")).toBeVisible();
  const billing = sessions.Billing.page;
  const current = await account(billing);
  const other = await api(billing, "post", `/api/patients/${patientId}/billing/payments`, { amount: 10, method: "Cash", note: null, rowVersion: current.rowVersion, idempotencyKey: `other-${stamp}` });
  expect(other.status).toBe(200);                                       // the first person records $10: balance $50.00

  await pay(stale, "5");
  await expect(paymentForm(stale).getByRole("alert")).toContainText("Not recorded: someone else changed this account");
  await expect(stale.getByText("Someone else changed this while you were editing")).toBeVisible();     // and the shared conflict banner
  await shot(stale, "06-stale-conflict.png");
  expect((await account(billing)).balanceOwed).toBe(50);                // the stale payment recorded nothing
  await stale.getByRole("button", { name: /reload/i }).click();
  await expect(stale.getByText("Balance owed: $50.00")).toBeVisible();
  await pay(stale, "5");
  await expect(stale.locator("p.alv-clinical__status")).toContainText("Payment of $5.00 recorded. Balance owed is now $45.00.");
});

test("ACCEPTANCE 3: every billing action is in the audit log with who and when", async () => {
  const entries = (await account(sessions.Billing.page)).entries as any[];
  expect(entries.map((e) => e.kind)).toEqual(["Charge", "Payment", "Adjustment", "Payment", "Payment"]);
  entries.forEach((e, i) => {
    expect(e.recordedByName, "who").toBe(i < 4 ? NAMES.Billing : "Staff member");      // the last payment was made by the administrator, who has no staff profile
    expect(Date.parse(e.recordedAtUtc), "when").toBeGreaterThan(0);
  });
  const audit = (await api(admin, "get", "/api/auth/audit-log")).body as any;
  const log = JSON.stringify(audit);
  for (const type of ["BillingChargeRecorded", "BillingPaymentRecorded", "BillingAdjustmentRecorded"]) expect(log).toContain(type);
  expect(log).not.toContain("Ann");                                      // no patient name in the audit text
  evidence.acceptance3 = { entries: entries.map((e) => ({ n: e.entryNumber, kind: e.kind, by: e.recordedByName, at: e.recordedAtUtc })), auditHasBillingEvents: true };
});

test("FAILURE PATH: when the log cannot be written nothing is recorded and billing is told so, then the same save works", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  sql(`CREATE TRIGGER [TR_E2E_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1; INSERT INTO [AuditLogEntries] SELECT * FROM inserted; END`);
  let dropped = false;
  try {
    const before = (await account(page)).balanceOwed;
    await pay(page, "7", "Check");
    await expect(page.getByRole("alert").first()).toContainText("Not recorded: The entry could not be saved, so nothing was recorded.");
    await expect(page.getByRole("alert").first()).not.toContainText(/audit|sql/i);        // no internals reach the person
    await expect(paymentForm(page).getByLabel(/^Amount/)).toHaveValue("7");                // what was typed is kept
    expect((await account(page)).balanceOwed).toBe(before);                                // nothing was recorded
    await shot(page, "07-save-failed-nothing-recorded.png");
    await scanBothThemes(page, "save-failed");

    sql("DROP TRIGGER [TR_E2E_RefuseBillingAudit]");
    dropped = true;
    await paymentForm(page).getByRole("button", { name: "Record payment" }).click();
    await expect(statusLine(page)).toContainText("Payment of $7.00 recorded.");
    expect((await account(page)).balanceOwed).toBe(before - 7);                            // recorded once, not twice
    await shot(page, "08-saved-once-the-log-is-back.png");
  } finally {
    if (!dropped) sql("DROP TRIGGER [TR_E2E_RefuseBillingAudit]");
  }
});

test("a dentist can read the account but is offered no way to change it, and a hygienist has no billing tab", async () => {
  const dentist = sessions.Dentist.page;
  await gotoBilling(dentist);
  await expect(dentist.getByText(/Balance owed: \$/)).toBeVisible();
  await expect(ledger(dentist).getByRole("row").first()).toBeVisible();
  await expect(dentist.getByRole("form")).toHaveCount(0);
  await expect(dentist.getByRole("button", { name: /^Bill |Record/ })).toHaveCount(0);
  await expect(statusLine(dentist)).toContainText("You can read this but your role cannot change it.");
  await shot(dentist, "09-dentist-read-only.png");
  expect((await api(dentist, "post", `/api/patients/${patientId}/billing/payments`, { amount: 1, method: "Cash", note: null, rowVersion: "AAAA", idempotencyKey: `d-${stamp}` })).status).toBe(403);

  const hygienist = sessions.Hygienist.page;
  await hygienist.goto(`/patients/${patientId}/billing`);
  await expect(hygienist.getByRole("heading", { name: "Billing", exact: true })).toHaveCount(0);
  expect((await api(hygienist, "get", `/api/patients/${patientId}/billing`)).status).toBe(403);
  expect((await api(hygienist, "post", `/api/patients/${patientId}/billing/charges`, { planItemId: performed, idempotencyKey: `h-${stamp}` })).status).toBe(403);
});

test("the API refuses a missing CSRF token, an unknown patient and a bad amount", async () => {
  const page = sessions.Billing.page;
  const current = await account(page);
  expect((await api(page, "post", `/api/patients/${patientId}/billing/payments`, { amount: 1, method: "Cash", note: null, rowVersion: current.rowVersion, idempotencyKey: `c-${stamp}` }, {}, false)).status).toBe(400);
  expect((await api(page, "get", `/api/patients/00000000-0000-0000-0000-000000000000/billing`)).status).toBe(404);
  const bad = await api(page, "post", `/api/patients/${patientId}/billing/payments`, { amount: 10.005, method: "Barter", note: null, rowVersion: current.rowVersion, idempotencyKey: `b-${stamp}` });
  expect([bad.status, bad.body.error]).toEqual([400, "validation_failed"]);
  expect(Object.keys(bad.body.fieldErrors).sort()).toEqual(["amount", "method"]);
});
