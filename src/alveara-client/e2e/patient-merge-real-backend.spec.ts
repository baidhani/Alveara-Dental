import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-017 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.patient-merge.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// The front desk has registered the same person twice (Ann and Anne Alder, the same birth date, different phone and address); each chart holds an allergy. Only the administrator can see the duplicate review;
// it suggests the pair with its reasons; the review shows the two records side by side and will not merge until every differing field is answered, a reason is written and the merge is confirmed; the merge moves
// the retired chart's allergy to the surviving record without losing either, leaves the retired record in place and uneditable, and is in the merge log and the audit log with the administrator's id and a time;
// asking again with the same key changes nothing.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PATIENT_MERGE_E2E_OUT ?? path.join(process.cwd(), "patient-merge-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using");
  execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
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

const mergeButton = (page: Page) => page.getByRole("button", { name: "Merge the records" });
const auditMerges = async () => ((await api(admin, "get", "/api/auth/audit-log?take=500")).body as any[]).filter((e) => e.eventType === "PatientMerged");
const recordOf = async (page: Page, patient: string) => JSON.stringify((await api(page, "get", `/api/patients/${patient}/clinical-record`)).body);

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  ids.adminUser = ((await api(admin, "get", "/api/auth/users")).body as any[]).find((u) => u.username === `admin-${stamp}`).id;
  for (const role of ["Dentist", "FrontDesk"]) {
    await createUser(role, `${role.toLowerCase()}-${stamp}`);
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "patient-merge-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: the same person was registered twice, and each chart holds an allergy", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const patient = async (firstName: string, key: string, phone: string, address: string, email: string | null) => {
    const r = await api(front, "post", "/api/patients", { firstName, lastName: `Alder${stamp}`, dateOfBirth: firstName === "Ann" ? "1985-03-09" : "1985-03-10", sex: "Female", phone, email, addressLine1: address, city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": key });
    expect(r.status).toBe(201);
    return r.body.id as string;
  };
  ids.ann = await patient("Ann", `pm-ann-${stamp}`, "555-010-0100", "1 Main St", null);
  ids.anne = await patient("Anne", `pm-anne-${stamp}`, "555-010-0222", "9 Oak Ave", "anne@example.org");
  sql(`UPDATE [Patients] SET [DateOfBirth] = '1985-03-09' WHERE [Id] = '${ids.anne}'`);                       // the front desk typed the day wrong the second time
  for (const [patientId, name] of [[ids.ann, "Penicillin"], [ids.anne, "Latex"]]) {
    expect((await api(dentist, "post", `/api/patients/${patientId}/clinical-record/items`, { kind: "Allergy", name, reaction: "Rash", severity: "Mild" })).status, name).toBe(200);
  }
});

test("ONLY THE ADMINISTRATOR: the dentist and the front desk do not see the review and the API refuses them", async () => {
  for (const role of ["Dentist", "FrontDesk"]) {
    const page = sessions[role].page;
    expect((await api(page, "get", "/api/patient-merges/duplicates")).status).toBe(403);
    expect((await api(page, "post", "/api/patient-merges", {}, { "Idempotency-Key": `x-${role}` })).status).toBe(403);
    await page.goto("/");
    await expect(page.getByRole("link", { name: "Duplicate patients" })).toHaveCount(0);
  }
  const anonymous = await adminContext.browser()!.newContext();
  expect((await anonymous.request.get("/api/patient-merges/duplicates")).status()).toBe(401);
  await anonymous.close();
  evidence.access = { dentist: 403, frontDesk: 403, anonymous: 401 };
});

test("THE LIST: the administrator sees the pair with its reasons and a suggested record to keep", async () => {
  await admin.goto("/admin/patient-merge");
  await expect(admin.getByRole("heading", { name: "Duplicate patient records" })).toBeVisible();
  const row = admin.getByRole("row", { name: new RegExp(`Ann Alder${stamp}.*Anne Alder${stamp}`) });
  await expect(row).toContainText("Same last name and date of birth");
  await shot(admin, "01-possible-duplicates.png");
  await scanBothThemes(admin, "list");
});

test("THE REVIEW: two records side by side; the merge stays off until every differing field is answered, a reason is written and it is confirmed", async () => {
  await admin.goto("/admin/patient-merge");
  await admin.getByRole("button", { name: new RegExp(`Review Ann Alder${stamp} and Anne Alder${stamp}`) }).click();
  await expect(admin.getByRole("heading", { name: `Merge Anne Alder${stamp} into Ann Alder${stamp}` })).toBeVisible();
  await expect(admin.getByText(/Filled in from the retired record.*Email/)).toBeVisible();
  await expect(mergeButton(admin)).toBeDisabled();
  await shot(admin, "02-review-side-by-side.png");
  await admin.getByRole("group", { name: "Phone" }).getByLabel(/Take the retired record's/).check();
  await expect(mergeButton(admin)).toBeDisabled();
  await admin.getByRole("group", { name: "Address line 1" }).getByLabel(/Keep the kept record's/).check();
  await admin.getByLabel(/Reason for the merge/).fill("The front desk registered the same person twice");
  await expect(mergeButton(admin)).toBeDisabled();                                                              // not confirmed yet
  await admin.getByLabel(/I have compared the two records/).check();
  await expect(mergeButton(admin)).toBeEnabled();
  await shot(admin, "03-review-answered-and-confirmed.png");
  await scanBothThemes(admin, "review");
  expect((await api(admin, "get", "/api/auth/audit-log?take=50")).body.filter((e: any) => e.eventType === "PatientMerged")).toHaveLength(0);       // nothing has happened yet
});

test("THE MERGE: it is made once, the chart is whole on the surviving record, the retired record stays in place and cannot be edited", async () => {
  const beforeAnn = await recordOf(admin, ids.ann); const beforeAnne = await recordOf(admin, ids.anne);
  expect(beforeAnn).toContain("Penicillin"); expect(beforeAnne).toContain("Latex");
  await mergeButton(admin).click();
  await expect(admin.getByRole("status").first()).toContainText(`Merged Anne Alder${stamp} into Ann Alder${stamp}`);
  await shot(admin, "04-merged.png");

  const survivor = await recordOf(admin, ids.ann);
  expect(survivor).toContain("Penicillin"); expect(survivor).toContain("Latex");                                // nothing lost, nothing left behind
  expect(await recordOf(admin, ids.anne)).not.toContain("Latex");
  const kept = (await api(admin, "get", `/api/patients/${ids.ann}`)).body;
  expect([kept.phone, kept.email, kept.addressLine1]).toEqual(["555-010-0222", "anne@example.org", "1 Main St"]);   // the answers, and the email that was empty
  const retired = (await api(admin, "get", `/api/patients/${ids.anne}`)).body;
  expect(retired.isActive).toBe(false);                                                                         // still there, marked
  const edit = await api(admin, "put", `/api/patients/${ids.anne}`, { firstName: "Anne", lastName: `Alder${stamp}`, dateOfBirth: "1985-03-09", phone: "555-999-0000", addressLine1: "9 Oak Ave", city: "Austin", state: "TX", postalCode: "78701", rowVersion: retired.rowVersion });
  expect([edit.status, edit.body.error]).toEqual([409, "patient_merged"]);
  evidence.merge = { survivorHasBothAllergies: true, retiredStillThere: true, retiredEdit: edit.status };
});

test("TRUST: the merge log and the audit log say who and when; asking again with the same key changes nothing", async () => {
  const log = (await api(admin, "get", "/api/patient-merges/log")).body as any[];
  expect(log).toHaveLength(1);
  expect(log[0].mergedByUserId).toBe(ids.adminUser);
  expect(Number.isNaN(Date.parse(log[0].mergedAtUtc))).toBe(false);
  const audit = await auditMerges();
  expect(audit).toHaveLength(2);
  expect(audit.every((e) => e.performedByUserAccountId === ids.adminUser && !Number.isNaN(Date.parse(e.timestampUtc)) && e.correlationId === log[0].mergeId)).toBe(true);
  expect(JSON.stringify(audit)).not.toMatch(/Penicillin|Latex|555-010/);                                         // the audit text carries no patient values

  const body = { sourcePatientId: ids.anne, survivorPatientId: ids.ann, reason: "again", sourceRowVersion: null, survivorRowVersion: null, fieldChoices: {} };
  const again = await api(admin, "post", "/api/patient-merges", body, { "Idempotency-Key": "will-not-match" });
  expect([again.status, again.body.error]).toEqual([409, "already_merged"]);
  expect(await auditMerges()).toHaveLength(2);

  await admin.goto("/admin/patient-merge");
  await expect(admin.getByRole("table", { name: "Merges made" })).toContainText("The front desk registered the same person twice");
  await shot(admin, "05-merge-log.png");
  await scanBothThemes(admin, "log");
  evidence.trust = { logEntries: log.length, auditEntries: audit.length };
});
