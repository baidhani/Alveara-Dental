import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { seedLegacyCompletion } from "./support/legacyCompletion";
import { createAccount } from "./support/testAccounts";

// ALV-N024 (brief revision 2 section 9 step 5), un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, on a fresh database.
// Run ALONE with playwright.billing-integrity.config.ts against an API the caller started (scripts/Invoke-RealBackendWalkthrough.ps1; E2E_DB_NAME names its database). One account, walked in order:
// (1) RESPONSIBILITY before anything is billed: the form is offered (Q-5bf) and naming the patient, who is already responsible, records nothing (F-197);
// (2) PAYER: the registered guarantor and a household member are offered with how they are related, the responsible party is chosen to start with (PD-18, F-205), a guarantor's payment is recorded
//     as theirs, and the API refuses anyone else;
// (3) RECEIPT: a receipt is issued once; asking again opens it, and the API refuses a second one (PD-17, F-114);
// (4) REPLACE: a replacement needs a reason, gets the next number and supersedes the first, as the list says (F-115);
// (5) REVERSE: once the payment is reversed the list says so;
// (6) STATEMENT: a version-3 statement marks the reversed line and explains the adjustments, the screen explains them too (F-116, F-117), and the CSV starts with its identity block (F-119, CR-2).

test.describe.configure({ mode: "serial" });

const OUT = process.env.BILLING_INTEGRITY_E2E_OUT ?? path.join(process.cwd(), "billing-integrity-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "integrity-pass-1!";
let adminContext: BrowserContext;
let admin: Page;
let billingContext: BrowserContext;
let billing: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

let patients = 0;
async function patient(first: string, last: string): Promise<string> {
  patients += 1;                                                                                          // distinct details, so the duplicate check never mistakes one for another
  const p = await api(admin, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: `198${patients}-0${patients}-1${patients}`, sex: "Female", phone: `555-010-01${patients}${patients}`,
    addressLine1: `${patients} Main St`, city: "Austin", state: "TX", postalCode: "78701" },
    { "Idempotency-Key": `n024-${first}-${stamp}` });
  expect(p.status, `patient ${first}`).toBe(201);
  return p.body.id as string;
}

const statusLine = (page: Page) => page.locator("p.alv-clinical__status").first();
const docsStatus = (page: Page) => page.locator("section[aria-labelledby='alv-bill-docs-title'] p[aria-live='polite']");
const docRows = (page: Page) => page.getByRole("table", { name: "Issued documents" }).getByRole("row");
/** The list row of one document, matched on its own name in the first cell (another row may mention it, e.g. "Replaces receipt 1"). */
const docRow = (page: Page, name: string) => docRows(page).filter({ has: page.getByRole("cell", { name: new RegExp(`^${name} \\(`) }) });
const account = async () => (await api(billing, "get", `/api/patients/${ids.ann}/billing`)).body as any;

async function gotoBilling(page: Page) {
  await page.goto(`/patients/${ids.ann}/billing`);
  await expect(page.getByRole("heading", { name: "Billing", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Account history" })).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  expect((await createAccount(admin.request, { username: `admin-${stamp}`, password: "admin-password-1!", role: "Admin" })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  const token = await csrf(admin);
  const reg = await createAccount(admin.request, { username: `billing-${stamp}`, password: PASSWORD, role: "Unassigned" });
  expect(reg.ok()).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role: "Billing" } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  billingContext = await browser.newContext();
  billing = await billingContext.newPage();
  await signIn(billing, `billing-${stamp}`, PASSWORD);
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "billing-integrity-e2e.json"), JSON.stringify(evidence, null, 2));
  await billingContext?.close();
  await adminContext?.close();
});

test("SETUP: Ann, a guarantor (Gia) and a household member (Hal), and two procedures completed and ready to bill", async () => {
  ids.ann = await patient("Ann", "Alder");
  ids.gia = await patient("Gia", "Guard");
  ids.hal = await patient("Hal", "Alder");
  ids.stu = await patient("Stu", "Stranger");
  const e = await api(admin, "post", `/api/patients/${ids.ann}/encounters`, {}, { "Idempotency-Key": `n024-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  const d = await api(admin, "post", `/api/patients/${ids.ann}/diagnoses`, { idempotencyKey: `n024-dx-${stamp}`, encounterId: e.body.id, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(d.status).toBe(200);
  const procs = [];
  for (const [code, fee] of [["PERIO-1", 120], ["SCALE-1", 70]] as const) {
    const r = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code, description: code === "PERIO-1" ? "Periodontal maintenance" : "Scaling", category: "Periodontic", scope: "WholeMouth", fee });
    expect(r.status).toBe(200);
    procs.push(r.body.summary.id);
  }
  const plan = await api(admin, "post", `/api/patients/${ids.ann}/treatment-plans`, {
    idempotencyKey: `n024-plan-${stamp}`, title: "Gum health plan", items: procs.map((id) => ({ diagnosisId: d.body.id, procedureId: id, toothKey: null, surface: null })),
  });
  expect(plan.status).toBe(200);
  let version = plan.body.rowVersion;
  ids.items = JSON.stringify(plan.body.items.map((i: any) => i.id));
  for (const item of plan.body.items) {
    const step = await api(admin, "post", `/api/treatment-plans/${plan.body.id}/items/${item.id}/decision`, { decision: "Accepted", rowVersion: version });
    expect(step.status).toBe(200);
    version = step.body.rowVersion;
    seedLegacyCompletion(plan.body.id, item.id);
  }
});

test("RESPONSIBILITY: offered before anything is billed; naming the patient, who is already responsible, records nothing", async () => {
  await gotoBilling(billing);
  const form = billing.getByRole("form", { name: "Record who is responsible" });
  await expect(form).toBeVisible();                                                                       // Q-5bf: before the first charge
  await form.getByRole("button", { name: "Record responsibility" }).click();
  await expect(statusLine(billing)).toContainText("Nothing changed: Ann Alder is already responsible for this account, so nothing was recorded.");
  expect((await api(billing, "get", `/api/patients/${ids.ann}/billing/responsibility`)).body.history).toHaveLength(0);
  expect((await account()).hasAccount).toBe(false);                                                      // no account opened for nothing
  await shot(billing, "01-responsibility-nothing-changed.png");
});

test("PAYER: the guarantor and the household are offered with how they are related, the responsible party first, and nobody else is accepted", async () => {
  for (const item of JSON.parse(ids.items) as string[])
    expect((await api(billing, "post", `/api/patients/${ids.ann}/billing/charges`, { planItemId: item, idempotencyKey: `n024-ch-${item}` })).status).toBe(200);
  const ann = (await api(admin, "get", `/api/patients/${ids.ann}`)).body;
  expect((await api(admin, "put", `/api/patients/${ids.ann}/guarantor`, { guarantorPatientId: ids.gia, rowVersion: ann.rowVersion })).status).toBe(200);
  const hal = (await api(admin, "get", `/api/patients/${ids.hal}`)).body;
  expect((await api(admin, "put", `/api/patients/${ids.hal}/household`, { anchorPatientId: ids.ann, relationship: "Spouse", rowVersion: hal.rowVersion })).status).toBe(200);

  await gotoBilling(billing);
  const form = billing.getByRole("form", { name: "Record a payment" });
  const who = form.getByLabel("Who paid");
  await expect(who.getByRole("option")).toHaveText(["Ann Alder (the patient)", "Hal Alder (household member (spouse))", "Gia Guard (registered guarantor)"]);
  await expect(who).toHaveValue(ids.gia);                                                                 // F-205: the responsible party (the registered guarantor) to start with
  // ALV-N024 R02 (CR-2, F-196): the rendered checkbox keeps a checkbox's own size beside its text - not stretched to the width of a field, not on a line above its label
  const choose = form.getByLabel(/Choose which charges this pays/);
  const box = (await choose.boundingBox())!;
  const line = (await form.locator("label.alv-bill__choose").boundingBox())!;
  const text = (await form.locator("label.alv-bill__choose span").boundingBox())!;
  evidence.checkbox = {
    box, line, text,
    styles: await choose.evaluate((el) => { const c = getComputedStyle(el), l = getComputedStyle(el.parentElement!); return { inputMargin: c.margin, inputHeight: c.height, inputDisplay: c.display, flexDirection: l.flexDirection, justifyContent: l.justifyContent, alignItems: l.alignItems, labelPadding: l.padding, labelLineHeight: l.lineHeight }; }),
  };
  writeFileSync(path.join(OUT, "checkbox-geometry.json"), JSON.stringify(evidence.checkbox, null, 2));
  expect(box.width, "checkbox width").toBeLessThan(32);
  expect(await choose.evaluate((el) => getComputedStyle(el.parentElement!).display)).toBe("flex");
  expect(await choose.evaluate((el) => getComputedStyle(el.parentElement!).flexDirection)).toBe("row");
  expect(box.x, "checkbox before its text").toBeLessThan(text.x);
  expect(Math.abs(box.y + box.height / 2 - (line.y + line.height / 2)), "checkbox centred on its text line").toBeLessThan(8);
  await form.getByLabel(/^Amount/).fill("40");
  await form.getByLabel("How it was received").selectOption("Cash");
  await form.getByRole("button", { name: "Record payment" }).click();
  await expect(statusLine(billing)).toContainText("Payment of $40.00 recorded.");
  await expect(billing.getByRole("table", { name: "Account history" }).getByRole("row").nth(3)).toContainText("paid by Gia Guard");
  await shot(billing, "02-payer-guarantor.png");

  const a = await account();
  const refused = await api(billing, "post", `/api/patients/${ids.ann}/billing/payments`, { amount: 5, method: "Cash", rowVersion: a.rowVersion, idempotencyKey: `n024-stu-${stamp}`, paidByPatientId: ids.stu });
  expect([refused.status, refused.body.error]).toEqual([400, "payer_not_eligible"]);
  evidence.payerRefusal = refused.body.error;
});

test("RECEIPT: issued once; asking again opens it, and the API refuses a second one", async () => {
  await gotoBilling(billing);
  await billing.getByRole("button", { name: "Receipt for payment 3" }).click();
  await expect(billing.getByText("Preview: not issued yet.")).toBeVisible();
  await billing.getByRole("button", { name: "Issue this receipt" }).click();
  await expect(docsStatus(billing)).toContainText("Receipt 1 issued.");
  await expect(billing.locator(".alv-bill__docnumber", { hasText: "Receipt 1" })).toBeVisible();          // Q-5av: the number is printed with it

  await billing.locator("section[aria-labelledby='alv-bill-docs-title']").getByRole("button", { name: "Close", exact: true }).click();
  await billing.getByRole("button", { name: "Receipt for payment 3" }).click();
  await expect(docsStatus(billing)).toContainText("Payment 3 already has receipt 1, shown below.");
  await expect(billing.getByText("Preview: not issued yet.")).toHaveCount(0);
  const again = await api(billing, "get", `/api/patients/${ids.ann}/billing/documents/preview?type=Receipt&paymentEntryNumber=3`);
  expect([again.status, again.body.error]).toEqual([409, "receipt_exists"]);
  await shot(billing, "03-receipt-once.png");
});

test("REPLACE: a replacement needs a reason, gets the next number and supersedes the first", async () => {
  await billing.getByRole("button", { name: "Issue a replacement for receipt 1…" }).click();
  await expect(billing.getByText(/It will replace receipt 1\./)).toBeVisible();
  await billing.getByRole("button", { name: "Issue the replacement for receipt 1" }).click();
  await expect(billing.getByText("Say why.")).toBeVisible();
  await billing.getByLabel("Why is receipt 1 being replaced?").fill("Patient lost the first copy");
  await billing.getByRole("button", { name: "Issue the replacement for receipt 1" }).click();
  await expect(docsStatus(billing)).toContainText("Receipt 2 issued; it replaces receipt 1.");
  await expect(docRow(billing, "Receipt 1")).toContainText("Superseded by receipt 2");
  await expect(docRow(billing, "Receipt 2")).toContainText("Replaces receipt 1");
  const docs = (await api(billing, "get", `/api/patients/${ids.ann}/billing/documents`)).body as any[];
  expect(docs.map((d) => [d.documentNumber, d.replacesNumber ?? null, d.supersededByNumber ?? null])).toEqual([[1, null, 2], [2, 1, null]]);
  await shot(billing, "04-receipt-replaced.png");
});

test("REVERSE: once the payment is reversed the list says so beside its receipt", async () => {
  await gotoBilling(billing);
  await billing.getByRole("button", { name: "Reverse entry 3", exact: true }).click();
  const form = billing.getByRole("form", { name: "Reverse entry 3" });
  await form.getByLabel("Why is it being reversed?").fill("Recorded on the wrong account");
  await form.getByRole("button", { name: "Reverse entry", exact: true }).click();
  await expect(statusLine(billing)).toContainText("Payment 3 reversed.");
  await expect(docRow(billing, "Receipt 2")).toContainText("Payment reversed");
  await shot(billing, "05-payment-reversed.png");
});

test("STATEMENT: version 3 marks the reversed line and explains the adjustments, and the CSV starts with its identity block", async () => {
  const a = await account();
  expect((await api(billing, "post", `/api/patients/${ids.ann}/billing/adjustments`, { amount: -5, reason: "Courtesy discount", rowVersion: a.rowVersion, idempotencyKey: `n024-adj-${stamp}` })).status).toBe(200);
  await gotoBilling(billing);
  await expect(billing.getByText(/Adjustments not applied to a charge: -\$5\.00/)).toBeVisible();          // F-116 on the account screen
  await billing.getByRole("button", { name: "Preview a statement" }).click();
  const statement = billing.getByRole("article", { name: "Statement" });
  await expect(statement).toContainText("(reversed by 4)");                                              // F-117
  await expect(statement).toContainText("Adjustments not applied to a charge: -$5.00");
  await expect(billing.getByLabel("Preview of a statement")).toBeFocused();                              // F-113
  await billing.getByRole("button", { name: "Issue this statement" }).click();
  await expect(docsStatus(billing)).toContainText("Statement 1 issued.");
  const issued = (await api(billing, "get", `/api/patients/${ids.ann}/billing/documents`)).body.find((d: any) => d.documentType === "Statement");
  expect(issued.templateVersion).toBe(3);
  const csv = await (await billing.request.get(`/api/patients/${ids.ann}/billing/documents/${issued.id}/csv`)).text();
  const lines = csv.split("\r\n");
  expect(lines.slice(0, 3)).toEqual([expect.stringMatching(/^Practice,/), "Patient,Ann Alder", "Statement,1"]);
  expect(lines.find((l) => l.startsWith("3,Payment,"))).toMatch(/,4$/);                                  // the reversed payment names its reversal
  evidence.csvHead = lines.slice(0, 7);
  await shot(billing, "06-statement-v3.png");
  expect((await api(billing, "get", `/api/patients/${ids.ann}/billing/verification`)).body.consistent).toBe(true);
});
