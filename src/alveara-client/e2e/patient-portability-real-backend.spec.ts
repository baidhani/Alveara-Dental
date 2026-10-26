import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-018 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.patient-portability.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// The front desk has registered a patient. Only the administrator sees "Export patient file" and downloads the patient's JSON file in the documented layout; the administrator and the office manager (not the front
// desk, not the dentist) see "Import a patient". A broken file is rejected with every problem listed and nothing stored; the same file corrected, as another person, imports and links to the new record; the
// same file again says it was imported before and creates nothing; a patient who is already registered is refused with a link to the record. The audit log then shows every export and import action with the user and the time.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PATIENT_PORTABILITY_E2E_OUT ?? path.join(process.cwd(), "patient-portability-e2e-out");
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
let exportedPath = "";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
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

const auditEntries = async (types: string[]) => ((await api(admin, "get", "/api/auth/audit-log?take=500")).body as any[]).filter((e) => types.includes(e.eventType));
const patientsNamed = async (lastName: string) => (await api(admin, "get", `/api/patients?q=${encodeURIComponent(lastName)}&includeInactive=true&take=50`)).body as any[];
const chooseFile = (page: Page, name: string, content: object | string) =>
  page.getByLabel(/Patient file/).setInputFiles({ name, mimeType: "application/json", buffer: Buffer.from(typeof content === "string" ? content : JSON.stringify(content)) });

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  ids.adminUser = ((await api(admin, "get", "/api/auth/users")).body as any[]).find((u) => u.username === `admin-${stamp}`).id;
  for (const role of ["Dentist", "FrontDesk", "OfficeManager"]) {
    ids[role] = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "patient-portability-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: the front desk registers a patient", async () => {
  const r = await api(sessions.FrontDesk.page, "post", "/api/patients", {
    firstName: "Cy", lastName: `Moss${stamp}`, dateOfBirth: "1979-06-02", sex: "Male", phone: "555-010-0311", email: null, addressLine1: "4 Pine St", addressLine2: null, city: "Austin", state: "TX", postalCode: "78701",
  }, { "Idempotency-Key": `pp-cy-${stamp}` });
  expect(r.status).toBe(201);
  ids.cy = r.body.id as string;
});

test("WHO MAY: only the administrator is offered the export; the administrator and the office manager are offered the import; the API refuses everyone else", async () => {
  for (const role of ["Dentist", "FrontDesk"]) {
    const page = sessions[role].page;
    expect((await page.request.get(`/api/patients/${ids.cy}/export`)).status(), `${role} export`).toBe(403);
    expect((await api(page, "post", "/api/patients/import", {}, { "Content-Type": "application/json" })).status, `${role} import`).toBe(403);
    await page.goto(`/patients/${ids.cy}`);
    await expect(page.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Export patient file" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Import a patient" })).toHaveCount(0);
  }
  const manager = sessions.OfficeManager.page;
  expect((await manager.request.get(`/api/patients/${ids.cy}/export`)).status(), "office manager export").toBe(403);          // export is the administrator's alone
  await manager.goto(`/patients/${ids.cy}`);
  await expect(manager.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
  await expect(manager.getByRole("button", { name: "Export patient file" })).toHaveCount(0);
  await expect(manager.getByRole("link", { name: "Import a patient" })).toBeVisible();
  const anonymous = await adminContext.browser()!.newContext();
  expect((await anonymous.request.get(`/api/patients/${ids.cy}/export`)).status()).toBe(401);
  expect((await anonymous.request.post("/api/patients/import", { data: "{}" })).status()).toBe(401);
  await anonymous.close();
  await admin.goto(`/patients/${ids.cy}`);
  await expect(admin.getByRole("button", { name: "Export patient file" })).toBeVisible();
  await expect(admin.getByRole("link", { name: "Import a patient" })).toBeVisible();
  evidence.access = { dentist: [403, 403], frontDesk: [403, 403], officeManagerExport: 403, anonymous: [401, 401] };
});

test("EXPORT: the administrator downloads the patient's file in the documented JSON layout", async () => {
  await admin.goto(`/patients/${ids.cy}`);
  const [download] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Export patient file" }).click()]);
  expect(download.suggestedFilename()).toMatch(/^alveara-patient-[0-9a-f]{32}\.json$/);
  exportedPath = path.join(OUT, "exported-patient.json");
  await download.saveAs(exportedPath);
  await expect(admin.getByText(/^Saved alveara-patient-[0-9a-f]{32}\.json\. The export was logged\.$/)).toBeVisible();
  const file = JSON.parse(readFileSync(exportedPath, "utf8"));
  expect(Object.keys(file).sort()).toEqual(["exportId", "exportedAtUtc", "format", "formatVersion", "patient", "source"]);
  expect(file.format).toBe("alveara.patient-export");
  expect(file.formatVersion).toBe(1);
  expect(file.source.sourcePatientId).toBe(ids.cy);
  expect(file.patient).toMatchObject({ firstName: "Cy", lastName: `Moss${stamp}`, dateOfBirth: "1979-06-02", phone: "555-010-0311", city: "Austin", state: "TX", postalCode: "78701", middleName: null, email: null });
  await shot(admin, "01-export.png");
  await scanBothThemes(admin, "export");
  evidence.export = { fileName: download.suggestedFilename(), format: file.format, formatVersion: file.formatVersion, members: Object.keys(file.patient).length };
});

test("INCORRECT FILE: the office manager's import of a broken file is rejected with every problem listed and nothing is stored", async () => {
  const manager = sessions.OfficeManager.page;
  const before = (await patientsNamed(`Moss${stamp}`)).length;
  const broken = JSON.parse(readFileSync(exportedPath, "utf8"));
  broken.formatVersion = 7;
  delete broken.patient.city;
  broken.patient.phone = "12";
  await manager.goto("/admin/patient-import");
  await expect(manager.getByRole("heading", { name: "Import a patient" })).toBeVisible();
  await expect(manager.getByRole("button", { name: "Import patient" })).toBeDisabled();
  await chooseFile(manager, "broken.json", broken);
  await manager.getByRole("button", { name: "Import patient" }).click();
  const alert = manager.getByRole("alert").filter({ hasText: "The file was not imported" });
  await expect(alert).toContainText("Correct the problems listed and choose the file again. Nothing was stored.");
  const table = alert.getByRole("table");
  await expect(table).toContainText("formatVersion");
  await expect(table).toContainText("patient.city");
  await expect(table).toContainText("patient.phone");
  await expect(manager.getByRole("link", { name: "Open the patient" })).toHaveCount(0);
  expect((await patientsNamed(`Moss${stamp}`)).length).toBe(before);
  await shot(manager, "02-rejected-file.png");
  await scanBothThemes(manager, "rejected");
  evidence.rejected = { problems: await table.getByRole("row").count() - 1, patientsBefore: before, patientsAfter: (await patientsNamed(`Moss${stamp}`)).length };
});

test("CORRECTED FILE: the same file, correct and as another person, imports and links to the new record; the same file again creates nothing", async () => {
  const manager = sessions.OfficeManager.page;
  const another = JSON.parse(readFileSync(exportedPath, "utf8"));
  another.exportId = "11111111-2222-4333-8444-555555555555";
  another.patient.firstName = "Dee";
  another.patient.dateOfBirth = "1971-02-03";
  await manager.goto("/admin/patient-import");
  await chooseFile(manager, "dee.json", another);
  await manager.getByRole("button", { name: "Import patient" }).click();
  await expect(manager.getByText("The patient was imported.")).toBeVisible();
  await shot(manager, "03-imported.png");
  const countAfter = (await patientsNamed(`Moss${stamp}`)).length;
  await manager.getByRole("link", { name: "Open the patient" }).click();
  await expect(manager.getByLabel("First name *")).toHaveValue("Dee");
  await expect(manager.getByLabel("Last name *")).toHaveValue(`Moss${stamp}`);
  await shot(manager, "04-new-patient.png");

  await manager.goto("/admin/patient-import");
  await chooseFile(manager, "dee.json", another);
  await manager.getByRole("button", { name: "Import patient" }).click();
  await expect(manager.getByText(/This file was imported before\. The patient it created is shown; nothing new was created\./)).toBeVisible();
  expect((await patientsNamed(`Moss${stamp}`)).length).toBe(countAfter);
  await shot(manager, "05-imported-before.png");
  evidence.imported = { patientsAfterFirstImport: countAfter, patientsAfterRepeat: (await patientsNamed(`Moss${stamp}`)).length };
});

test("ALREADY REGISTERED: a file for a patient who is already in the system is refused with a link to the record", async () => {
  const manager = sessions.OfficeManager.page;
  const same = JSON.parse(readFileSync(exportedPath, "utf8"));
  same.exportId = "99999999-2222-4333-8444-555555555555";                                                  // the same person as Cy, from another file
  const before = (await patientsNamed(`Moss${stamp}`)).length;
  await manager.goto("/admin/patient-import");
  await chooseFile(manager, "cy-again.json", same);
  await manager.getByRole("button", { name: "Import patient" }).click();
  const alert = manager.getByRole("alert").filter({ hasText: "already registered" });
  await expect(alert).toContainText("Nothing was imported.");
  await expect(alert.getByRole("link", { name: "Open the existing patient" })).toHaveAttribute("href", `/patients/${ids.cy}`);
  expect((await patientsNamed(`Moss${stamp}`)).length).toBe(before);
  await shot(manager, "06-already-registered.png");
});

test("THE LOG: the audit log shows every export and import action with the user and the time", async () => {
  const entries = await auditEntries(["PatientExported", "PatientImported", "PatientImportRejected"]);
  const kinds = entries.map((e) => `${e.eventType}:${e.performedByUserAccountId === ids.adminUser ? "admin" : e.performedByUserAccountId === ids.OfficeManager ? "officeManager" : "other"}`).sort();
  expect(kinds).toEqual(["PatientExported:admin", "PatientImportRejected:officeManager", "PatientImportRejected:officeManager", "PatientImported:officeManager", "PatientImported:officeManager"]);
  for (const e of entries) {
    expect(Math.abs(Date.now() - new Date(e.timestampUtc).getTime())).toBeLessThan(30 * 60 * 1000);
    expect(e.details).not.toMatch(new RegExp(`Cy|Dee|Moss${stamp}|555-010`));                                 // no names or contact details in the log
  }
  await admin.goto("/admin/audit-log");
  await expect(admin.getByRole("heading", { name: "Audit log" })).toBeVisible();
  const table = admin.getByRole("table");
  await expect(table.getByRole("cell", { name: "PatientExported", exact: true })).toHaveCount(1);
  await expect(table.getByRole("cell", { name: "PatientImported", exact: true })).toHaveCount(2);
  await expect(table.getByRole("cell", { name: "PatientImportRejected", exact: true })).toHaveCount(2);
  await shot(admin, "07-audit-log.png");
  await scanBothThemes(admin, "audit-log");
  evidence.log = { entries: kinds };
});
