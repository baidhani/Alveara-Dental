import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { seedLegacyCompletion } from "./support/legacyCompletion";

// ALV-007-C02 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.billing-statements.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the new money flows as a billing person would: a refund (money back to the original payer, the unapplied money first, then the newest applications released), an over-refund refused beside its field,
// a move of what a payment pays, a correction (reverse and replace), the timeline and aging, a receipt and a statement previewed, issued, reprinted as issued with later changes written apart, exported as CSV and
// printed; a stale preview (a name changed on a second screen) that issues nothing and shows the new preview; the failure path (nothing recorded when the log cannot be written, then the same save works);
// a read-only role; and the API refusing what the screen does not offer.

test.describe.configure({ mode: "serial" });

const OUT = process.env.BILLING_STATEMENTS_E2E_OUT ?? path.join(process.cwd(), "billing-statements-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "statements-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
let annId = "";
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
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log and stale-preview demonstrations need it)");
  execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
}

const statusLine = (page: Page) => page.locator("p.alv-clinical__status").first();
const docsStatus = (page: Page) => page.locator("section[aria-labelledby='alv-bill-docs-title'] p[aria-live='polite']");
const paymentForm = (page: Page) => page.getByRole("form", { name: "Record a payment" });
const ledger = (page: Page) => page.getByRole("table", { name: "Account history" });
const row = (page: Page, n: number) => ledger(page).getByRole("row").nth(n);
const account = async (page: Page) => (await api(page, "get", `/api/patients/${annId}/billing`)).body as any;
const balance = (page: Page) => page.getByText(/^Balance owed: \$/).first();

async function gotoBilling(page: Page) {
  await page.goto(`/patients/${annId}/billing`);
  await expect(page.getByRole("heading", { name: "Billing", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Account history" })).toBeVisible();
}

async function pay(page: Page, amount: string, opts: { charge?: [number, string] } = {}) {
  const form = paymentForm(page);
  await form.getByLabel(/^Amount/).fill(amount);
  await form.getByLabel("How it was received").selectOption("Cash");
  if (opts.charge) {
    await form.getByLabel(/Choose which charges this pays/).check();
    await form.getByLabel(`Apply to charge ${opts.charge[0]} (US dollars)`).fill(opts.charge[1]);
  }
  await form.getByRole("button", { name: "Record payment" }).click();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Billing", "FrontDesk"]) {
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
  writeFileSync(path.join(OUT, "billing-statements-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: Ann has two completed procedures (PERIO-1 at 120 and SCALE-1 at 70)", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const p = await api(front, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `stmt-ann-${stamp}` });
  expect(p.status).toBe(201);
  annId = p.body.id as string;
  const e = await api(dentist, "post", `/api/patients/${annId}/encounters`, {}, { "Idempotency-Key": `stmt-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  const d = await api(dentist, "post", `/api/patients/${annId}/diagnoses`, { idempotencyKey: `stmt-dx-${stamp}`, encounterId: e.body.id, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(d.status).toBe(200);
  const a = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", fee: 120 });
  const b = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "SCALE-1", description: "Scaling", category: "Periodontic", scope: "WholeMouth", fee: 70 });
  expect([a.status, b.status]).toEqual([200, 200]);
  const plan = await api(dentist, "post", `/api/patients/${annId}/treatment-plans`, {
    idempotencyKey: `stmt-plan-${stamp}`, title: "Gum health plan", items: [a, b].map((x) => ({ diagnosisId: d.body.id, procedureId: x.body.summary.id, toothKey: null, surface: null })),
  });
  expect(plan.status).toBe(200);
  let version = plan.body.rowVersion;
  for (const item of plan.body.items) {
    const step = await api(dentist, "post", `/api/treatment-plans/${plan.body.id}/items/${item.id}/decision`, { decision: "Accepted", rowVersion: version });
    expect(step.status).toBe(200);
    version = step.body.rowVersion;
    seedLegacyCompletion(plan.body.id, item.id);          // completed the old way: the route is retired (410), so it is seeded (see support/legacyCompletion.ts)
  }
});

test("REFUND: the money goes back to the original payer, the unapplied money first and then the newest application; an over-refund is refused beside the amount", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await page.getByRole("button", { name: "Bill PERIO-1" }).click();
  await expect(statusLine(page)).toContainText("PERIO-1 billed for $120.00");
  await page.getByRole("button", { name: "Bill SCALE-1" }).click();
  await expect(balance(page)).toHaveText("Balance owed: $190.00");
  await pay(page, "150");                                                                // 120 to charge 1 and 30 to charge 2: nothing unapplied
  await expect(statusLine(page)).toContainText("Payment of $150.00 recorded. Balance owed is now $40.00.");

  await page.getByRole("button", { name: "Refund payment 3" }).click();
  const form = page.getByRole("form", { name: "Refund payment 3" });
  await expect(form).toContainText("The money goes back to Ann Alder, who paid it; it cannot be sent to anyone else. Up to $150.00 can still be refunded.");
  await expect(form).toContainText("newest first, which become open again");
  await form.getByRole("button", { name: /Refund to Ann Alder/ }).click();
  await expect(form.getByRole("alert")).toContainText("Nothing was sent");
  await expect(form.getByText("A refund needs a reason, so the history explains it.")).toBeVisible();
  expect((await account(page)).entries).toHaveLength(3);                                 // nothing was added
  await shot(page, "01-refund-form-names-gaps-before-sending.png");

  await form.getByLabel(/^Amount to refund/).fill("150.01");
  await form.getByLabel("How the money is returned").selectOption("Cash");
  await form.getByLabel("Why is it being refunded?").fill("Treatment cancelled");
  await form.getByRole("button", { name: /Refund to Ann Alder/ }).click();
  await expect(form.getByText("Payment 3 can be refunded for 150.00 USD at most.")).toBeVisible();
  await expect(form.getByLabel(/^Amount to refund/)).toHaveValue("150.01");              // what was typed is kept
  await shot(page, "02-over-refund-refused-beside-the-amount.png");

  await form.getByLabel(/^Amount to refund/).fill("50");
  await form.getByRole("button", { name: /Refund to Ann Alder/ }).click();
  await expect(statusLine(page)).toContainText("$50.00 refunded to Ann Alder. Balance owed is now $90.00.");
  await expect(row(page, 4)).toContainText("Refund");
  await expect(row(page, 4)).toContainText("Refunds payment 3");
  await expect(row(page, 3)).toContainText("refunded $50.00");
  await expect(row(page, 1)).toContainText("Paid $100.00, open $20.00");                 // the newest application (charge 2, 30) was released first, then 20 of charge 1
  await expect(row(page, 2)).toContainText("Paid $0.00, open $70.00");
  await expect(page.getByRole("button", { name: "Reverse entry 3", exact: true })).toHaveCount(0);      // a refunded payment is not reversed
  await expect(page.getByRole("button", { name: "Correct entry 3", exact: true })).toHaveCount(0);
  await shot(page, "03-refund-recorded-releases-shown.png");
  await scanBothThemes(page, "refund-recorded");
  const a = await account(page);
  expect(a.allocations.map((x: any) => x.releasedAmount)).toEqual([20, 30]);
  expect(a.entries[3]).toMatchObject({ kind: "Refund", amount: 50, refundsEntryNumber: 3 });
  evidence.refund = { entries: a.entries.length, balance: a.balanceOwed, releases: a.allocations.map((x: any) => x.releasedAmount) };
});

test("MOVE: part of what a payment pays is moved to another charge in one step; the balance does not change", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await pay(page, "40", { charge: [2, "40"] });                                          // payment 5: 40 on charge 2
  await expect(statusLine(page)).toContainText("Payment of $40.00 recorded. Balance owed is now $50.00.");
  await expect(row(page, 2)).toContainText("Paid $40.00, open $30.00");

  await page.getByRole("button", { name: "Move payment 5" }).click();
  const form = page.getByRole("form", { name: "Move what payment 5 pays" });
  await expect(form).toContainText("The payment, its amount and the balance owed do not change.");
  await form.getByLabel("Take it off").selectOption("2");
  await form.getByLabel("Put it on").selectOption("1");
  await form.getByLabel(/^Amount to move/).fill("25");
  await form.getByLabel("Why is it being moved?").fill("Paid the wrong procedure");
  await form.getByRole("button", { name: "Move", exact: true }).click();
  await expect(form.getByText("Charge 1 has only 20.00 USD left to pay.")).toBeVisible();      // refused beside the field, nothing recorded
  expect((await account(page)).allocations).toHaveLength(3);
  await shot(page, "04-move-refused-nothing-recorded.png");

  await form.getByLabel(/^Amount to move/).fill("15");
  await form.getByRole("button", { name: "Move", exact: true }).click();
  await expect(statusLine(page)).toContainText("$15.00 of payment 5 moved from charge 2 to charge 1. Balance owed is unchanged.");
  await expect(row(page, 1)).toContainText("Paid $115.00, open $5.00");
  await expect(row(page, 2)).toContainText("Paid $25.00, open $45.00");
  await expect(balance(page)).toHaveText("Balance owed: $50.00");
  await shot(page, "05-move-recorded.png");
  const check = await api(page, "get", `/api/patients/${annId}/billing/verification`);
  expect(check.body.consistent).toBe(true);
});

test("RECEIPT, THEN CORRECTION: a receipt is issued for payment 5; the payment is then corrected (reversed and replaced) and the receipt still reprints as issued with the change written apart", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await page.getByRole("button", { name: "Receipt for payment 5" }).click();
  await expect(page.getByRole("heading", { name: "Receipt for payment 5" })).toBeVisible();
  await expect(page.getByText("Amount received: $40.00")).toBeVisible();
  await page.getByRole("button", { name: "Issue this receipt" }).click();
  await expect(docsStatus(page)).toContainText("Receipt 1 issued.");
  await expect(page.getByText(/Shown exactly as issued/)).toBeVisible();
  await shot(page, "06-receipt-issued.png");

  await page.getByRole("button", { name: "Correct entry 5", exact: true }).click();
  const form = page.getByRole("form", { name: "Correct payment 5" });
  await expect(form).toContainText("The replacement payment starts unapplied");
  await form.getByLabel(/^Corrected payment amount/).fill("30");
  await form.getByLabel("How it was received").selectOption("Cash");
  await form.getByLabel("Why is it being corrected?").fill("Typed 40 instead of 30");
  await form.getByRole("button", { name: "Reverse and replace" }).click();
  await expect(statusLine(page)).toContainText("Payment 5 corrected: reversed and replaced. Balance owed is now $60.00.");
  await expect(row(page, 5)).toContainText("Reversed by entry 6");
  await expect(row(page, 7)).toContainText("Replaces entry 5");
  await expect(row(page, 7)).toContainText("Applied $0.00, unapplied $30.00");            // the replacement starts unapplied
  await shot(page, "07-payment-corrected.png");

  await page.getByRole("button", { name: "View receipt 1" }).click();
  await expect(page.getByText(/Shown exactly as issued/)).toBeVisible();
  await expect(page.getByText("Amount received: $40.00")).toBeVisible();                 // as issued, not as corrected
  await expect(page.getByRole("note", { name: "Since this document was issued" })).toContainText("Payment 5 has since been reversed.");
  await shot(page, "08-receipt-reprinted-as-issued.png");
  const check = await api(page, "get", `/api/patients/${annId}/billing/verification`);
  expect(check.body.consistent).toBe(true);
});

test("TIMELINE AND AGING: the history reads in order with the completed procedure behind each charge, and what is open is shown by age", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  const aging = page.getByRole("table", { name: "Open charges by age" }).first();
  await expect(aging).toBeVisible();
  await expect(aging).toContainText("$90.00");
  await page.getByRole("button", { name: "Show the timeline" }).click();
  const list = page.getByRole("list", { name: "Account timeline" });
  await expect(list).toBeVisible();
  await expect(list.getByRole("listitem").filter({ hasText: "Charge for PERIO-1" })).toContainText("Completed procedure performed");
  await expect(list.getByRole("listitem").filter({ hasText: "Refund of payment 3" })).toBeVisible();
  await expect(list.getByRole("listitem").filter({ hasText: "released from charge" }).first()).toBeVisible();
  await expect(list.getByRole("listitem").filter({ hasText: "Receipt 1 issued" })).toBeVisible();
  await shot(page, "09-timeline-and-aging.png");
  await scanBothThemes(page, "timeline-and-aging");

  // R02 (review finding ALV-007-C02-R01-01): the billing user follows a charge back to its source without any clinical permission
  await expect(page.getByRole("link", { name: "Clinical history" })).toHaveCount(0);                  // billing alone offers no link into the clinical record
  await page.getByRole("button", { name: "View source of charge 1" }).click();
  const source = page.getByRole("group", { name: "Source of charge 1" });
  await expect(source).toContainText("PERIO-1 Periodontal maintenance");
  await expect(source).toContainText("Charge $120.00.");
  await expect(source).toContainText("completed the old way");                                       // these procedures were completed with the old state-only button
  await shot(page, "16-charge-source-for-a-billing-reader.png");
  await scanBothThemes(page, "charge-source");
  const sourceApi = await api(page, "get", `/api/patients/${annId}/billing/entries/1/source`);
  expect(sourceApi.status).toBe(200);
  expect(Object.keys(sourceApi.body).filter((k) => /provider|encounter|chart|diagnos|note|finding/i.test(k))).toEqual([]);
  expect((await api(page, "get", `/api/patients/${annId}/completed-procedures`)).status).toBe(403);   // and the same role is still refused the clinical record
  evidence.chargeSource = { status: sourceApi.status, keys: Object.keys(sourceApi.body), clinicalRecordStatus: 403 };
  const tl = (await api(page, "get", `/api/patients/${annId}/billing/timeline`)).body as any[];
  evidence.timelineTypes = tl.map((e) => e.type);
});

test("STATEMENT: previewed, issued, reprinted as issued, exported as CSV and printed; a stale preview issues nothing and shows the new preview", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await page.getByRole("button", { name: "Preview a statement" }).click();
  const statement = page.getByRole("article", { name: "Statement" });
  await expect(statement).toContainText("Addressed to: Ann Alder");
  await expect(statement).toContainText("Balance owed: $60.00");
  await expect(page.getByText("Preview: not issued yet.")).toBeVisible();
  await shot(page, "10-statement-preview.png");

  sql(`UPDATE Patients SET LastName = 'Alder-Smith' WHERE Id = '${annId}'`);               // a name changes on another screen between the preview and the issue
  await page.getByRole("button", { name: "Issue this statement" }).click();
  await expect(docsStatus(page)).toContainText("nothing was issued. Review the new preview below and issue that.");
  await expect(page.getByRole("article", { name: "Statement" })).toContainText("For: Ann Alder-Smith");
  expect((await api(page, "get", `/api/patients/${annId}/billing/documents`)).body.filter((d: any) => d.documentType === "Statement")).toHaveLength(0);
  await shot(page, "11-stale-preview-nothing-issued.png");

  await page.getByRole("button", { name: "Issue this statement" }).click();
  await expect(docsStatus(page)).toContainText("Statement 1 issued.");
  const detail = page.getByText(/Statement 1, issued/);
  await expect(detail).toBeVisible();
  await expect(page.getByRole("link", { name: "Download CSV" })).toBeVisible();
  const issued = (await api(page, "get", `/api/patients/${annId}/billing/documents`)).body.find((d: any) => d.documentType === "Statement");
  const csv = await page.request.get(`/api/patients/${annId}/billing/documents/${issued.id}/csv`);
  expect(csv.status()).toBe(200);
  expect(csv.headers()["content-type"]).toContain("text/csv");
  const text = await csv.text();
  expect(text.split("\r\n")[0]).toBe("Number,Kind,Amount,BalanceAfter,ProcedureCode,Description,Method,Reason,RecordedAtUtc");
  expect(text).toContain("Refund");
  evidence.statementCsvRows = text.split("\r\n").filter(Boolean).length;

  await page.emulateMedia({ media: "print" });                                            // what a printer would draw: only the document
  await expect(page.getByRole("heading", { name: "Billing", exact: true })).toBeHidden();
  await expect(page.getByRole("article", { name: "Statement" })).toBeVisible();
  await shot(page, "12-statement-print-view.png");
  await page.emulateMedia({ media: "screen" });
  await scanBothThemes(page, "statement-issued");
});

test("FAILURE PATH: when the log cannot be written a refund records nothing and says so; the same request then works", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  const before = await account(page);
  sql(`CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1; INSERT INTO [AuditLogEntries] SELECT * FROM inserted; END`);
  try {
    await page.getByRole("button", { name: "Refund payment 7", exact: true }).click();
    const form = page.getByRole("form", { name: "Refund payment 7" });
    await form.getByLabel(/^Amount to refund/).fill("10");
    await form.getByLabel("How the money is returned").selectOption("Cash");
    await form.getByLabel("Why is it being refunded?").fill("Failure demonstration");
    await form.getByRole("button", { name: /Refund to Ann/ }).click();
    await expect(form.getByRole("alert")).toContainText("Not recorded:");
    await expect(form.getByLabel(/^Amount to refund/)).toHaveValue("10");
    const after = await account(page);
    expect([after.entries.length, after.allocations.length, after.balanceOwed]).toEqual([before.entries.length, before.allocations.length, before.balanceOwed]);
    await shot(page, "13-save-failed-nothing-recorded.png");
  } finally {
    sql("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
  }
  await page.getByRole("form", { name: "Refund payment 7" }).getByRole("button", { name: /Refund to Ann/ }).click();
  await expect(statusLine(page)).toContainText("$10.00 refunded to Ann");
  const check = await api(page, "get", `/api/patients/${annId}/billing/verification`);
  expect(check.body.consistent).toBe(true);
  evidence.finalVerification = check.body;
});

test("READ-ONLY: a dentist reads receipts and statements but is offered no way to change anything, and the API agrees", async () => {
  const page = sessions.Dentist.page;
  await gotoBilling(page);
  await expect(page.getByRole("button", { name: "Preview a statement" })).toBeVisible();
  await expect(page.getByRole("button", { name: /Refund payment|Move payment|Correct entry|Reverse entry/ })).toHaveCount(0);
  await page.getByRole("button", { name: "Preview a statement" }).click();
  await expect(page.getByText("Preview: not issued yet.")).toBeVisible();
  await expect(page.getByRole("button", { name: /Issue this/ })).toHaveCount(0);
  const refused = [
    await api(page, "post", `/api/patients/${annId}/billing/refunds`, { paymentEntryNumber: 3, amount: 1, method: "Cash", reason: "x", rowVersion: "AAAA", idempotencyKey: "ro-1" }),
    await api(page, "post", `/api/patients/${annId}/billing/corrections`, { entryNumber: 7, reason: "x", amount: 1, method: "Cash", rowVersion: "AAAA", idempotencyKey: "ro-2" }),
    await api(page, "post", `/api/patients/${annId}/billing/documents`, { previewToken: "x", idempotencyKey: "ro-3" }),
  ];
  expect(refused.map((r) => r.status)).toEqual([403, 403, 403]);
  expect((await api(page, "get", `/api/patients/${annId}/billing/timeline`)).status).toBe(200);
  await shot(page, "14-read-only-dentist.png");
});

test("STALE SECOND SCREEN: a refund from an out-of-date copy of the account is refused as a conflict and records nothing", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  const second = await sessions.Billing.context.newPage();
  await gotoBilling(second);
  await second.getByRole("button", { name: "Refund payment 7", exact: true }).click();
  const other = second.getByRole("form", { name: "Refund payment 7" });
  await other.getByLabel(/^Amount to refund/).fill("5");
  await other.getByLabel("How the money is returned").selectOption("Cash");
  await other.getByLabel("Why is it being refunded?").fill("From the second screen");

  await page.getByRole("button", { name: "Refund payment 7", exact: true }).click();                  // the first screen refunds first
  const form = page.getByRole("form", { name: "Refund payment 7" });
  await form.getByLabel(/^Amount to refund/).fill("5");
  await form.getByLabel("How the money is returned").selectOption("Cash");
  await form.getByLabel("Why is it being refunded?").fill("From the first screen");
  await form.getByRole("button", { name: /Refund to Ann/ }).click();
  await expect(statusLine(page)).toContainText("$5.00 refunded");
  const before = (await account(page)).entries.length;

  await other.getByRole("button", { name: /Refund to Ann/ }).click();
  await expect(second.getByText(/someone else changed this account/).first()).toBeVisible();
  expect((await account(page)).entries.length).toBe(before);                              // the second screen recorded nothing
  await shot(second, "15-stale-second-screen-conflict.png");
  await second.close();
});
