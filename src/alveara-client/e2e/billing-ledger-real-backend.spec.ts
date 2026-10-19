import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { seedLegacyCompletion } from "./support/legacyCompletion";

// ALV-007-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.billing-ledger.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the ledger as a billing person would: a payment applied to a chosen charge with the rest left unapplied and an over-allocation refused beside its charge; the rest applied to the oldest charge; a payment
// reversed (its allocations released, the original kept and marked); a charge that still has payments refused until they are reversed; the history verified from the preserved entries; a guarantor whose responsibility
// is kept apart from the registered one and who is recorded as the payer; a configured payment method used and then stopped; an out-of-date second screen; the failure path (nothing recorded when the log cannot be
// written, then the same save works); read-only roles; and the API refusing what the screen does not offer.

test.describe.configure({ mode: "serial" });

const OUT = process.env.BILLING_LEDGER_E2E_OUT ?? path.join(process.cwd(), "billing-ledger-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "ledger-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
let annId = "";
let boId = "";
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
const ledger = (page: Page) => page.getByRole("table", { name: "Account history" });
const row = (page: Page, n: number) => ledger(page).getByRole("row").nth(n);
const account = async (page: Page, patient = annId) => (await api(page, "get", `/api/patients/${patient}/billing`)).body as any;
const balance = (page: Page) => page.getByText(/^Balance owed: \$/);

async function gotoBilling(page: Page, patient = annId) {
  await page.goto(`/patients/${patient}/billing`);
  await expect(page.getByRole("heading", { name: "Billing", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Account history" })).toBeVisible();
}

async function pay(page: Page, amount: string, method = "Cash", opts: { charge?: [number, string]; payer?: string } = {}) {
  const form = paymentForm(page);
  await form.getByLabel(/^Amount/).fill(amount);
  await form.getByLabel("How it was received").selectOption(method);
  if (opts.payer) await form.getByLabel("Who paid").selectOption({ label: opts.payer });
  if (opts.charge) {
    await form.getByLabel(/Choose which charges this pays/).check();
    await form.getByLabel(`Apply to charge ${opts.charge[0]} (US dollars)`).fill(opts.charge[1]);
  }
  await form.getByRole("button", { name: "Record payment" }).click();
}

async function reverse(page: Page, entry: number, reason: string) {
  await page.getByRole("button", { name: `Reverse entry ${entry}`, exact: true }).click();
  const form = page.getByRole("form", { name: `Reverse entry ${entry}` });
  await form.getByLabel("Why is it being reversed?").fill(reason);
  await form.getByRole("button", { name: "Reverse entry", exact: true }).click();
}

async function setRegisteredGuarantor(patient: string, guarantor: string | null) {
  const detail = await api(admin, "get", `/api/patients/${patient}`);
  expect(detail.status).toBe(200);
  const r = await api(admin, "put", `/api/patients/${patient}/guarantor`, { guarantorPatientId: guarantor, rowVersion: detail.body.rowVersion });
  expect(r.status, JSON.stringify(r.body)).toBe(200);
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
  writeFileSync(path.join(OUT, "billing-ledger-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: Ann has two completed procedures (PERIO-1 at 120 and SCALE-1 at 70) and Bo is a second patient", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const register = async (firstName: string, lastName: string, key: string, dateOfBirth: string, phone: string, addressLine1: string) => {
    const p = await api(front, "post", "/api/patients", { firstName, lastName, dateOfBirth, sex: "Female", phone, addressLine1, city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": key });
    expect(p.status).toBe(201);
    return p.body.id as string;
  };
  annId = await register("Ann", "Alder", `ledger-ann-${stamp}`, "1985-03-09", "555-010-0100", "1 Main St");
  boId = await register("Bo", "Kim", `ledger-bo-${stamp}`, "1962-11-30", "555-020-0200", "9 Oak Ave");
  const e = await api(dentist, "post", `/api/patients/${annId}/encounters`, {}, { "Idempotency-Key": `ledger-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  const d = await api(dentist, "post", `/api/patients/${annId}/diagnoses`, { idempotencyKey: `ledger-dx-${stamp}`, encounterId: e.body.id, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(d.status).toBe(200);
  const a = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", fee: 120 });
  const b = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: "SCALE-1", description: "Scaling", category: "Periodontic", scope: "WholeMouth", fee: 70 });
  expect([a.status, b.status]).toEqual([200, 200]);
  const plan = await api(dentist, "post", `/api/patients/${annId}/treatment-plans`, {
    idempotencyKey: `ledger-plan-${stamp}`, title: "Gum health plan", items: [a, b].map((x) => ({ diagnosisId: d.body.id, procedureId: x.body.summary.id, toothKey: null, surface: null })),
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

test("PAYMENT ALLOCATION: a payment is applied to the charge chosen and the rest stays unapplied; an over-allocation is refused beside its charge", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await page.getByRole("button", { name: "Bill PERIO-1" }).click();
  await expect(statusLine(page)).toContainText("PERIO-1 billed for $120.00");
  await page.getByRole("button", { name: "Bill SCALE-1" }).click();
  await expect(balance(page)).toHaveText("Balance owed: $190.00");

  await pay(page, "100", "Cash", { charge: [2, "40"] });
  await expect(statusLine(page)).toContainText("Payment of $100.00 recorded. Balance owed is now $90.00.");
  await expect(row(page, 2)).toContainText("Paid $40.00, open $30.00");
  await expect(row(page, 3)).toContainText("Applied $40.00, unapplied $60.00");
  await expect(page.getByText("Still to pay on charges: $150.00. Payments not yet applied to a charge: $60.00.")).toBeVisible();
  await shot(page, "01-payment-applied-to-a-chosen-charge.png");

  await pay(page, "30", "Cash", { charge: [2, "31"] });
  await expect(paymentForm(page).getByText("Charge 2 has only 30.00 USD left to pay.")).toBeVisible();
  await expect(paymentForm(page).getByLabel("Apply to charge 2 (US dollars)")).toHaveValue("31");       // what was typed is kept
  await expect(balance(page)).toHaveText("Balance owed: $90.00");
  await shot(page, "02-over-allocation-refused-beside-its-charge.png");
  await scanBothThemes(page, "allocation-refused");
});

test("APPLYING THE REST: what a payment still holds goes to the oldest open charge, and nothing already recorded changes", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  const before = await account(page);
  await page.getByRole("button", { name: "Apply payment 3" }).click();
  await page.getByRole("form", { name: "Apply payment 3" }).getByRole("button", { name: "Apply payment", exact: true }).click();
  await expect(statusLine(page)).toContainText("Payment 3 applied. $0.00 of payments is still unapplied.");
  await expect(row(page, 1)).toContainText("Paid $60.00, open $60.00");              // the oldest charge got the rest
  await expect(row(page, 3)).toContainText("Applied $100.00, unapplied $0.00");
  await expect(page.getByRole("button", { name: "Apply payment 3" })).toHaveCount(0);
  const after = await account(page);
  expect(after.entries.map((e: any) => [e.entryNumber, e.kind, e.amount, e.balanceAfter])).toEqual(before.entries.map((e: any) => [e.entryNumber, e.kind, e.amount, e.balanceAfter]));   // the entries are exactly as they were
  expect(after.allocations).toHaveLength(2);
  await shot(page, "03-rest-applied-to-the-oldest-charge.png");
});

test("REVERSAL: a payment is reversed by a new entry, its allocations are released, and the original stays in the history marked as reversed", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await reverse(page, 3, "Paid on the wrong account");
  await expect(statusLine(page)).toContainText("Payment 3 reversed. Balance owed is now $190.00.");
  await expect(row(page, 3)).toContainText("Reversed by entry 4");
  await expect(row(page, 4)).toContainText("Reverses entry 3");
  await expect(row(page, 4)).toContainText("Paid on the wrong account");
  await expect(row(page, 1)).toContainText("Paid $0.00, open $120.00");              // released
  await expect(page.getByRole("button", { name: "Reverse entry 3", exact: true })).toHaveCount(0);   // already reversed
  await expect(page.getByRole("button", { name: "Reverse entry 4", exact: true })).toHaveCount(0);   // a reversal cannot be reversed
  await shot(page, "04-payment-reversed.png");
  await scanBothThemes(page, "reversed");
  const a = await account(page);
  expect(a.entries[2]).toMatchObject({ entryNumber: 3, kind: "Payment", amount: -100 });          // still there, still -100
  expect(a.allocations.every((x: any) => x.released)).toBe(true);
});

test("A charge that payments are applied to cannot be reversed until they are; then the history is verified from the preserved entries", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await pay(page, "50", "Cash");                                                       // applied to the oldest charge by default
  await expect(statusLine(page)).toContainText("Payment of $50.00 recorded. Balance owed is now $140.00.");
  await expect(row(page, 1)).toContainText("Paid $50.00, open $70.00");

  await reverse(page, 1, "Billed in error");
  await expect(page.getByRole("form", { name: "Reverse entry 1" }).getByRole("alert")).toContainText("Not recorded: Charge 1 has payments applied to it. Reverse those payments first, then reverse the charge.");
  await expect(page.getByRole("form", { name: "Reverse entry 1" }).getByLabel("Why is it being reversed?")).toHaveValue("Billed in error");
  expect((await account(page)).entries).toHaveLength(5);                                // nothing was added
  await shot(page, "05-charge-with-payments-cannot-be-reversed.png");

  await page.getByRole("button", { name: "Cancel" }).click();
  await reverse(page, 5, "Wrong amount");
  await expect(statusLine(page)).toContainText("Payment 5 reversed. Balance owed is now $190.00.");
  await reverse(page, 1, "Billed in error");
  await expect(statusLine(page)).toContainText("Charge 1 reversed. Balance owed is now $70.00.");
  await expect(row(page, 1)).toContainText("Reversed by entry 7");
  await shot(page, "06-charge-reversed-after-its-payments.png");

  const check = await api(page, "get", `/api/patients/${annId}/billing/verification`);
  expect(check.body).toMatchObject({ consistent: true, balanceOwed: 70, entryCount: 7, allocationCount: 3 });
  expect(check.body.findings).toEqual([]);
  evidence.verification = check.body;
});

test("RESPONSIBILITY: a registered guarantor is followed until billing records one; the two stay independent; the guarantor sees the account and can be recorded as the payer", async () => {
  const page = sessions.Billing.page;
  await setRegisteredGuarantor(annId, boId);
  await gotoBilling(page);
  await expect(page.locator("strong", { hasText: /Bo Kim/ })).toBeVisible();
  await expect(page.getByText(/the guarantor registered for this patient, until billing records otherwise/)).toBeVisible();

  await gotoBilling(page, boId);                                                       // Bo's own page: the account he answers for
  const table = page.getByRole("table", { name: "Accounts this patient is responsible for" });
  await expect(table).toContainText("Ann Alder");
  await expect(table).toContainText("$70.00");
  await shot(page, "07-guarantor-sees-the-account.png");

  await gotoBilling(page);
  const form = page.getByRole("form", { name: "Record who is responsible" });
  await form.getByLabel("Responsible party").selectOption({ label: "Bo Kim (registered guarantor)" });
  await form.getByLabel("Why (optional)").fill("Bo pays for Ann");
  await form.getByRole("button", { name: "Record responsibility" }).click();
  await expect(statusLine(page)).toContainText("Bo Kim is recorded as responsible for this account.");
  await expect(page.getByText(/recorded by billing/)).toBeVisible();
  await expect(page.getByRole("table", { name: "Who has been responsible" })).toContainText("Bo pays for Ann");

  await setRegisteredGuarantor(annId, null);                                            // changing the registration does not rewrite what billing recorded
  const patient = await api(admin, "get", `/api/patients/${annId}`);
  expect(patient.body.guarantor).toBeNull();
  await gotoBilling(page);
  await expect(page.locator("strong", { hasText: /Bo Kim/ })).toBeVisible();
  await expect(page.getByText(/recorded by billing/)).toBeVisible();

  await pay(page, "10", "Cash", { payer: "Bo Kim (responsible for this account)" });
  await expect(statusLine(page)).toContainText("Payment of $10.00 recorded. Balance owed is now $60.00.");
  await expect(row(page, 8)).toContainText("paid by Bo Kim");
  await shot(page, "08-responsibility-recorded-and-guarantor-as-payer.png");
  await scanBothThemes(page, "responsibility");
});

test("PAYMENT METHODS: billing adds a named method, uses it, then stops using it; the old payment keeps showing it", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  await page.getByText("Payment methods", { exact: true }).click();
  const section = page.locator("details.alv-bill__methods");
  await expect(section.getByRole("table", { name: "Payment methods" })).toContainText("Card (built in)");
  await section.getByLabel("Method name").fill("Venmo");
  await section.getByRole("button", { name: "Add method" }).click();
  await expect(statusLine(page)).toContainText("Payment method Venmo is available.");

  await pay(page, "5", "Venmo");
  await expect(statusLine(page)).toContainText("Payment of $5.00 recorded. Balance owed is now $55.00.");
  await expect(row(page, 9)).toContainText("Venmo");
  await shot(page, "09-configured-method-used.png");

  await section.getByRole("button", { name: "Stop using Venmo" }).click();
  await expect(statusLine(page)).toContainText("Venmo is no longer offered for new payments.");
  await expect(paymentForm(page).getByLabel("How it was received").locator("option", { hasText: "Venmo" })).toHaveCount(0);
  await expect(row(page, 9)).toContainText("Venmo");                                    // the old payment still names it
  await expect(section.getByRole("button", { name: /Stop using (Cash|Card|Check|Other)/ })).toHaveCount(0);   // a built-in is always available
});

test("a payment made from an out-of-date second screen is a conflict that records nothing", async () => {
  const stale = admin;
  await gotoBilling(stale);                                                              // a second person has the account open
  const billing = sessions.Billing.page;
  const current = await account(billing);
  const other = await api(billing, "post", `/api/patients/${annId}/billing/payments`, { amount: 1, method: "Cash", note: null, rowVersion: current.rowVersion, idempotencyKey: `stale-${stamp}` });
  expect(other.status).toBe(200);

  await reverse(stale, 9, "Venmo payment returned");
  await expect(stale.getByRole("form", { name: "Reverse entry 9" }).getByRole("alert")).toContainText("Not recorded: someone else changed this account");
  await expect(stale.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(stale, "10-stale-screen-conflict.png");
  expect((await account(billing)).entries).toHaveLength(10);
  await stale.getByRole("button", { name: /reload/i }).click();
  await reverse(stale, 9, "Venmo payment returned");
  await expect(stale.locator("p.alv-clinical__status")).toContainText("Payment 9 reversed. Balance owed is now $59.00.");
});

test("FAILURE PATH: when the log cannot be written nothing is recorded and billing is told so, then the same reversal works", async () => {
  const page = sessions.Billing.page;
  await gotoBilling(page);
  sql(`CREATE TRIGGER [TR_E2E_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1; INSERT INTO [AuditLogEntries] SELECT * FROM inserted; END`);
  let dropped = false;
  try {
    const before = (await account(page)).balanceOwed;
    await reverse(page, 8, "Paid by the wrong person");
    const form = page.getByRole("form", { name: "Reverse entry 8" });
    await expect(form.getByRole("alert")).toContainText("Not recorded: The entry could not be saved, so nothing was recorded.");
    await expect(form.getByRole("alert")).not.toContainText(/audit|sql/i);              // no internals reach the person
    await expect(form.getByLabel("Why is it being reversed?")).toHaveValue("Paid by the wrong person");
    expect((await account(page)).balanceOwed).toBe(before);
    expect((await account(page)).entries).toHaveLength(11);
    await shot(page, "11-save-failed-nothing-recorded.png");
    await scanBothThemes(page, "save-failed");

    sql("DROP TRIGGER [TR_E2E_RefuseBillingAudit]");
    dropped = true;
    await form.getByRole("button", { name: "Reverse entry", exact: true }).click();
    await expect(statusLine(page)).toContainText("Payment 8 reversed.");
    expect((await account(page)).entries).toHaveLength(12);                              // recorded once, not twice
  } finally {
    if (!dropped) sql("DROP TRIGGER [TR_E2E_RefuseBillingAudit]");
  }
  const check = await api(page, "get", `/api/patients/${annId}/billing/verification`);
  expect(check.body).toMatchObject({ consistent: true, entryCount: 12 });                  // after everything, the history still adds up
  await shot(page, "12-final-ledger.png");
});

test("a dentist reads the ledger with what each entry stands for but is offered no way to change it; a hygienist has no billing tab", async () => {
  const dentist = sessions.Dentist.page;
  await gotoBilling(dentist);
  await expect(row(dentist, 1)).toContainText("Reversed by entry 7");
  await expect(dentist.getByRole("form")).toHaveCount(0);
  await expect(dentist.getByRole("button", { name: /^Bill |Record|Reverse|Apply|Stop using|Add method/ })).toHaveCount(0);
  await expect(dentist.getByText("Payment methods", { exact: true })).toHaveCount(0);
  await expect(dentist.locator("strong", { hasText: /Bo Kim/ })).toBeVisible();     // the responsibility is readable
  await shot(dentist, "13-dentist-read-only.png");
  const denied = await api(dentist, "post", `/api/patients/${annId}/billing/reversals`, { entryNumber: 1, reason: "x", rowVersion: "AAAA", idempotencyKey: `d-${stamp}` });
  expect(denied.status).toBe(403);

  const hygienist = sessions.Hygienist.page;
  await hygienist.goto(`/patients/${annId}/billing`);
  await expect(hygienist.getByRole("heading", { name: "Billing", exact: true })).toHaveCount(0);
  expect((await api(hygienist, "get", `/api/patients/${annId}/billing/verification`)).status).toBe(403);
});

test("the API refuses what the screen does not offer: a missing CSRF token, reversing a reversal or twice, and a payment applied for more than it holds", async () => {
  const page = sessions.Billing.page;
  const current = await account(page);
  const noCsrf = await api(page, "post", `/api/patients/${annId}/billing/reversals`, { entryNumber: 5, reason: "x", rowVersion: current.rowVersion, idempotencyKey: `c-${stamp}` }, {}, false);
  expect(noCsrf.status).toBe(400);
  const ofReversal = await api(page, "post", `/api/patients/${annId}/billing/reversals`, { entryNumber: 4, reason: "x", rowVersion: current.rowVersion, idempotencyKey: `r1-${stamp}` });
  expect([ofReversal.status, ofReversal.body.error]).toEqual([409, "cannot_reverse_a_reversal"]);
  const twice = await api(page, "post", `/api/patients/${annId}/billing/reversals`, { entryNumber: 3, reason: "x", rowVersion: current.rowVersion, idempotencyKey: `r2-${stamp}` });
  expect([twice.status, twice.body.error]).toEqual([409, "already_reversed"]);
  const reversedPayment = await api(page, "post", `/api/patients/${annId}/billing/payments/apply`, { paymentEntryNumber: 3, rowVersion: current.rowVersion, idempotencyKey: `a-${stamp}` });
  expect([reversedPayment.status, reversedPayment.body.error]).toEqual([409, "payment_reversed"]);
  const over = await api(page, "post", `/api/patients/${annId}/billing/payments`, { amount: 5, method: "Cash", note: null, rowVersion: current.rowVersion, idempotencyKey: `o-${stamp}`, allocations: [{ chargeEntryNumber: 2, amount: 6 }] });
  expect([over.status, over.body.error]).toEqual([400, "validation_failed"]);
  expect(Object.keys(over.body.fieldErrors)).toContain("allocations");
});
