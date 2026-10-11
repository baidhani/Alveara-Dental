import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-008 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.treatment-plans-complete-accepted.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// A dentist completes a patient's accepted procedures in one step and sees them in the clinical history. When the log is unavailable nothing is completed (and the same click works once it is back); a revision
// keeps the original; every change is logged with who and when; a second screen that is out of date is told and completes nothing; a hygienist reads everything and changes nothing.

test.describe.configure({ mode: "serial" });

const OUT = process.env.TREATMENT_PLANS_COMPLETE_ACCEPTED_E2E_OUT ?? path.join(process.cwd(), "treatment-plans-complete-accepted-e2e-out");
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
const procedures: Record<string, string> = {};
const NAMES: Record<string, string> = { Dentist: "Dr. Dana Dentist" };

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log demonstration needs it)");
  execFileSync("sqlcmd", ["-S", "(localdb)\\MSSQLLocalDB", "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
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

const statusLine = (page: Page) => page.locator("p.alv-clinical__status").first();
const history = (page: Page) => page.getByRole("region", { name: "Clinical history" });
const completeForm = (page: Page) => page.getByRole("form", { name: "Complete the accepted procedures" });
const completeButton = (page: Page) => page.getByRole("button", { name: "Complete accepted procedures" });
const planUrl = (patient: string) => `/patients/${ids[patient]}/treatment-plan`;
const plansOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${ids[patient]}/treatment-plans`)).body.plans as any[];
const historyOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${ids[patient]}/clinical-history`)).body.entries as any[];

async function gotoTab(page: Page, patient = "ann") {
  await page.goto(planUrl(patient));
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
  for (const role of ["Dentist", "Hygienist", "FrontDesk"]) {
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
  writeFileSync(path.join(OUT, "treatment-plans-complete-accepted-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: two patients, a catalog, a plan for Ann with two accepted procedures and one declined, a plan for Bo", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"]] as const) {
    const p = await api(front, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `s08-${key}-${stamp}` });
    expect(p.status).toBe(201);
    ids[key] = p.body.id;
    const e = await api(dentist, "post", `/api/patients/${ids[key]}/encounters`, {}, { "Idempotency-Key": `s08-enc-${key}-${stamp}` });
    expect([200, 201]).toContain(e.status);
    const d = await api(dentist, "post", `/api/patients/${ids[key]}/diagnoses`, { idempotencyKey: `s08-dx-${key}-${stamp}`, encounterId: e.body.id, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null });
    expect(d.status).toBe(200);
    ids[`${key}Dx`] = d.body.id;
  }
  for (const [code, description, fee] of [["PERIO-1", "Periodontal maintenance", 120], ["SCALE-1", "Scaling", 70], ["POL-1", "Polish", 30]] as const) {
    const r = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code, description, category: "Periodontic", scope: "WholeMouth", fee });
    expect(r.status).toBe(200);
    procedures[code] = r.body.summary.id;
  }
  const item = (patient: string, code: string) => ({ diagnosisId: ids[`${patient}Dx`], procedureId: procedures[code], toothKey: null, surface: null });
  const ann = await api(dentist, "post", `/api/patients/${ids.ann}/treatment-plans`, { idempotencyKey: `s08-ann-plan-${stamp}`, title: "Gum health plan", items: [item("ann", "PERIO-1"), item("ann", "SCALE-1"), item("ann", "POL-1")] });
  expect(ann.status).toBe(200);
  ids.annPlan = ann.body.id;
  const first = await api(dentist, "post", `/api/treatment-plans/${ids.annPlan}/items/${ann.body.items[0].id}/decision`, { decision: "Accepted", rowVersion: ann.body.rowVersion });
  const second = await api(dentist, "post", `/api/treatment-plans/${ids.annPlan}/items/${ann.body.items[1].id}/decision`, { decision: "Accepted", rowVersion: first.body.rowVersion });
  const third = await api(dentist, "post", `/api/treatment-plans/${ids.annPlan}/items/${ann.body.items[2].id}/decision`, { decision: "Declined", reason: "Not now", rowVersion: second.body.rowVersion });
  expect([first.status, second.status, third.status]).toEqual([200, 200, 200]);
  const bo = await api(dentist, "post", `/api/patients/${ids.bo}/treatment-plans`, { idempotencyKey: `s08-bo-plan-${stamp}`, title: "Bo's plan", items: [item("bo", "PERIO-1")] });
  expect(bo.status).toBe(200);
  ids.boPlan = bo.body.id;
  const accepted = await api(dentist, "post", `/api/treatment-plans/${ids.boPlan}/decision`, { decision: "Accepted", rowVersion: bo.body.rowVersion });
  expect(accepted.status).toBe(200);
});

test("the clinical history starts empty and says what that means; a hygienist reads it and has no button", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await expect(history(page)).toContainText("No procedure has been completed for this patient yet. That means none has been recorded, not that no care was given elsewhere.");
  await expect(completeButton(page)).toBeVisible();
  await shot(page, "01-history-empty-button-offered.png");

  const hyg = sessions.Hygienist.page;
  await gotoTab(hyg);
  await expect(history(hyg)).toContainText("No procedure has been completed for this patient yet.");
  await expect(completeButton(hyg)).toHaveCount(0);
});

test("FAILURE PATH: when the log is unavailable nothing is completed, no internals are shown, and the same click works once it is back", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const states = async () => (await plansOf(page, "ann"))[0].items.map((i: any) => i.completion);
  const before = await states();
  sql(`CREATE TRIGGER [TR_E2E_RefuseCompletionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'TreatmentPlanItemCompleted') THROW 59031, 'audit unavailable', 1; INSERT INTO [AuditLogEntries] SELECT * FROM inserted; END`);
  let dropped = false;
  try {
    await completeButton(page).click();
    const form = completeForm(page);
    await expect(form.getByRole("list", { name: "Procedures that will be completed" }).getByRole("listitem")).toHaveCount(2);
    await form.getByRole("button", { name: "Confirm: complete 2" }).click();
    await expect(form.getByRole("alert")).toContainText("Nothing was completed: The treatment plan could not be saved, so nothing was recorded.");
    await expect(form.getByRole("alert")).not.toContainText(/audit|sql/i);
    expect(await states()).toEqual(before);
    expect(await historyOf(page, "ann")).toHaveLength(0);
    await shot(page, "02-save-failed-nothing-completed.png");
    await scanBothThemes(page, "save-failed");

    sql("DROP TRIGGER [TR_E2E_RefuseCompletionAudit]");
    dropped = true;
    await form.getByRole("button", { name: "Confirm: complete 2" }).click();
    await expect(statusLine(page)).toContainText("2 accepted procedures completed and added to the clinical history.");
    expect(await states()).toEqual(["Completed", "Completed", "NotStarted"]);
  } finally {
    if (!dropped) sql("DROP TRIGGER [TR_E2E_RefuseCompletionAudit]");
  }
});

test("CRITERION 1: the completed procedures appear in the clinical history with who and when, newest first, and the button goes away", async () => {
  const page = sessions.Dentist.page;
  await expect(history(page).getByRole("list", { name: "Completed procedures" }).getByRole("listitem")).toHaveCount(2);
  await expect(history(page)).toContainText("PERIO-1");
  await expect(history(page)).toContainText("SCALE-1");
  await expect(history(page)).toContainText(`Completed by ${NAMES.Dentist}`);
  await expect(history(page)).toContainText("from Gum health plan, revision 1");
  await expect(history(page)).not.toContainText("POL-1");                                 // the declined one was not completed
  await expect(completeButton(page)).toHaveCount(0);
  await shot(page, "03-history-after-completion.png");
  await scanBothThemes(page, "history");

  const entries = await historyOf(page, "ann");
  expect(entries).toHaveLength(2);
  expect(entries.every((e) => e.completedByName === NAMES.Dentist && new Date(e.completedAtUtc).getTime() > Date.now() - 10 * 60_000)).toBe(true);
  expect(await historyOf(page, "bo")).toHaveLength(0);                                    // another patient's history is untouched

  const hyg = sessions.Hygienist.page;
  await gotoTab(hyg);
  await expect(history(hyg).getByRole("list", { name: "Completed procedures" }).getByRole("listitem")).toHaveCount(2);
});

test("a replay is quiet: completing again from the API changes nothing", async () => {
  const page = sessions.Dentist.page;
  const plan = (await plansOf(page, "ann"))[0];
  const again = await api(page, "post", `/api/treatment-plans/${ids.annPlan}/complete-accepted`, { rowVersion: plan.rowVersion });
  expect(again.status).toBe(200);
  expect(await historyOf(page, "ann")).toHaveLength(2);
  const log = (await api(page, "get", `/api/treatment-plans/${ids.annPlan}/history`)).body as any[];
  expect(log.filter((e) => e.changeType === "ItemCompleted")).toHaveLength(2);
});

test("CRITERION 2: revising the plan afterwards keeps the original revision and the completed care", async () => {
  const page = sessions.Dentist.page;
  const plan = (await plansOf(page, "ann"))[0];
  const revised = await api(page, "post", `/api/treatment-plans/${ids.annPlan}/revise`, {
    reason: "Phased after treatment", clinicalRationale: "Healing first", patientExplanation: "The next visit follows.", phases: [{ number: 1, name: "Healing", plannedVisit: null, targetDate: null }], placements: null, rowVersion: plan.rowVersion,
  });
  expect(revised.status).toBe(200);
  await gotoTab(page);
  await expect(page.getByText("Revision 2", { exact: false }).first()).toBeVisible();
  const revisions = (await api(page, "get", `/api/treatment-plans/${ids.annPlan}/revisions`)).body as any[];
  expect(revisions.map((r) => r.revisionNumber)).toEqual([1, 2]);
  const original = (await api(page, "get", `/api/treatment-plans/${ids.annPlan}/revisions/1`)).body;
  expect(original.summary.revisionNumber).toBe(1);
  expect(original.items).toHaveLength(3);                                                 // the original revision still holds all three procedures
  await expect(history(page)).toContainText("revision 1");                              // completed under revision 1, and still says so
  expect(await historyOf(page, "ann")).toHaveLength(2);
  await shot(page, "04-revised-original-kept.png");
});

test("CRITERION 3: every change is in the plan's log with who and when", async () => {
  const page = sessions.Dentist.page;
  const log = (await api(page, "get", `/api/treatment-plans/${ids.annPlan}/history`)).body as any[];
  expect(log.map((e) => e.changeType)).toEqual(["Created", "ItemAdded", "ItemAdded", "ItemAdded", "ItemAccepted", "ItemAccepted", "ItemDeclined", "ItemCompleted", "ItemCompleted", "Revised"]);
  expect(log.every((e) => e.actorName && new Date(e.occurredAtUtc).getTime() > Date.now() - 60 * 60_000)).toBe(true);
  expect(new Set(log.map((e) => e.eventNumber)).size).toBe(log.length);
  evidence.planLogChangeTypes = log.map((e) => e.changeType);
});

test("an out-of-date second screen completes nothing, is told, and works after a reload", async () => {
  const dentist = sessions.Dentist.page;
  await gotoTab(admin, "bo");
  await completeButton(admin).click();
  const plan = (await plansOf(dentist, "bo"))[0];
  expect((await api(dentist, "post", `/api/treatment-plans/${ids.boPlan}/rename`, { title: "Bo's updated plan", rowVersion: plan.rowVersion })).status).toBe(200);   // someone else saves first
  await completeForm(admin).getByRole("button", { name: /Confirm: complete 1/ }).click();
  await expect(completeForm(admin).getByRole("alert")).toContainText("Nothing was completed: this plan changed since you opened it.");
  expect(await historyOf(dentist, "bo")).toHaveLength(0);
  await shot(admin, "05-stale-screen-conflict.png");

  await gotoTab(admin, "bo");
  await completeButton(admin).click();
  await completeForm(admin).getByRole("button", { name: /Confirm: complete 1/ }).click();
  await expect(statusLine(admin)).toContainText("1 accepted procedure completed");
  expect(await historyOf(dentist, "bo")).toHaveLength(1);
});

test("the API refuses what the screen does not offer", async () => {
  const dentist = sessions.Dentist.page;
  const hyg = sessions.Hygienist.page;
  const plan = (await plansOf(dentist, "ann"))[0];
  expect((await api(hyg, "post", `/api/treatment-plans/${ids.annPlan}/complete-accepted`, { rowVersion: plan.rowVersion })).status).toBe(403);
  expect((await api(dentist, "post", `/api/treatment-plans/${ids.annPlan}/complete-accepted`, { rowVersion: plan.rowVersion }, {}, false)).status).toBe(400);
  const missing = await api(dentist, "post", `/api/treatment-plans/${ids.annPlan}/complete-accepted`, {});
  expect([missing.status, missing.body.error]).toEqual([400, "row_version_required"]);
  expect((await api(sessions.FrontDesk.page, "get", `/api/patients/${ids.ann}/clinical-history`)).status).toBe(403);
});
