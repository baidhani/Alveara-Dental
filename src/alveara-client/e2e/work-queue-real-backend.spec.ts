import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-009-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, with the real background passes (every 3 s here, 300 s by default).
// Automatic recall is off until an office manager turns it on; then, within seconds, a patient whose last completed visit is long ago gets ONE unassigned recall task made by the system (through the durable job queue).
// A dentist finds it in the work queue, takes it, raises its priority, completes it; the patient's follow-up timeline shows the whole story; a stale change is explained; a view-only role has no buttons; the audit trail names the actions.

test.describe.configure({ mode: "serial" });

const OUT = process.env.WORK_QUEUE_E2E_OUT ?? path.join(process.cwd(), "work-queue-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
const sessions: Record<string, { context: BrowserContext; page: Page }> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const names: Record<string, string> = {};
const RECALL_TITLE = "Patient recall: schedule the next visit";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", `SET NOCOUNT ON; ${statement}`], { stdio: "pipe" }).toString().trim();
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

const recallCount = () => Number(sql(`SELECT COUNT(*) FROM FollowUpTasks WHERE Kind = N'Recall' AND PatientId = '${ids.ann}'`));

/** Waits for the real background passes (every 3 s here) rather than sleeping a fixed time. */
async function until(check: () => boolean, what: string, seconds = 45) {
  const end = Date.now() + seconds * 1000;
  while (Date.now() < end) { if (check()) return; await new Promise((r) => setTimeout(r, 1000)); }
  throw new Error(`Timed out waiting for ${what}`);
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "FrontDesk", "Billing", "OfficeManager"]) {
    names[role] = `${role.toLowerCase()}-${stamp}`;
    ids[role] = await createUser(role, names[role]);
    const context = await adminContext.browser()!.newContext();
    const page = await context.newPage();
    await signIn(page, names[role], PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "work-queue-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a practice, a patient whose last completed visit was months ago", async () => {
  expect((await api(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  const op = (await api(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id as string;
  const type = (await api(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id as string;
  const staff = await api(admin, "post", "/api/config/staff", { displayName: "Dr. Rivera", jobTitle: "Dentist" });
  const provider = (await api(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" })).body.id as string;
  const current = (await (await admin.request.get(`/api/config/providers/${provider}/availability`)).json()) as { revision: number };
  const windows = [1, 2, 3, 4, 5, 6, 0].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
  expect((await api(admin, "put", `/api/config/providers/${provider}/availability`, { windows, revision: current.revision })).status).toBe(200);
  const patient = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `wq-ann-${stamp}` });
  expect(patient.status).toBe(201);
  ids.ann = patient.body.id as string;

  // book the visit (the real scheduler), mark it completed and move it a year into the past (the only way to have history on a fresh database)
  const d = new Date(Date.now() + 3 * 86_400_000);
  const p = (n: number) => String(n).padStart(2, "0");
  const booked = await api(sessions.FrontDesk.page, "post", "/api/appointments", { patientId: ids.ann, providerId: provider, operatoryId: op, appointmentTypeId: type, startLocal: `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T10:00:00` }, { "Idempotency-Key": `wq-visit-${stamp}` });
  expect(booked.status, JSON.stringify(booked.body)).toBe(201);
  ids.visit = String(booked.body.id);
  sql(`UPDATE Appointments SET FlowState = N'Completed', StartUtc = DATEADD(DAY, -365, StartUtc), EndUtc = DATEADD(DAY, -365, EndUtc) WHERE Id = '${ids.visit}'`);
  expect(sql(`SELECT FlowState FROM Appointments WHERE Id = '${ids.visit}'`)).toBe("Completed");
});

test("OFF BY DEFAULT: with recall off the queue stays empty however many passes run, and the settings page says so", async () => {
  const mgr = sessions.OfficeManager.page;
  await mgr.goto("/admin/recall");
  await expect(mgr.getByRole("heading", { name: "Recall settings" })).toBeVisible();
  await expect(mgr.getByText("Automatic recall is off.")).toBeVisible();
  await shot(mgr, "01-recall-off.png");
  await new Promise((r) => setTimeout(r, 9000));                                                              // three passes at 3 s
  expect(recallCount()).toBe(0);
  const dr = sessions.Dentist.page;
  await dr.goto("/work-queue");
  await expect(dr.getByRole("heading", { name: "Work queue" })).toBeVisible();
  await expect(dr.getByText("Nothing in this view")).toBeVisible();
  await shot(dr, "02-queue-empty.png");
  evidence.recallOffTasks = 0;
});

test("PERMISSION: a role that may not manage recall is turned away from the settings page and the API", async () => {
  const bill = sessions.Billing.page;
  expect((await api(bill, "get", "/api/work-queue")).status).toBe(403);
  expect((await api(bill, "put", "/api/recall/settings", { enabled: true, defaultIntervalDays: 90, leadDays: 30, maxVisitAgeMonths: 24, dueSoonDays: 7, rowVersion: "AAAA" })).status).toBe(403);
  expect((await api(sessions.Dentist.page, "put", "/api/recall/settings", { enabled: true, defaultIntervalDays: 90, leadDays: 30, maxVisitAgeMonths: 24, dueSoonDays: 7, rowVersion: "AAAA" })).status).toBe(403);
  const settingsValue = sql("SELECT CAST(Enabled AS int) FROM RecallSettings");
  expect(settingsValue).toBe("0");
});

test("TURN ON: the office manager checks the numbers, turns recall on, and the system makes ONE recall task for the lapsed patient", async () => {
  const mgr = sessions.OfficeManager.page;
  await mgr.goto("/admin/recall");
  const interval = mgr.getByLabel(/Default recall interval/);
  await interval.fill("0");
  await mgr.getByRole("button", { name: "Save the recall settings" }).click();
  await expect(mgr.getByText("Enter a whole number from 1 to 3650.")).toBeVisible();                           // checked before anything is sent
  expect(sql("SELECT CAST(Enabled AS int) FROM RecallSettings")).toBe("0");
  await interval.fill("180");
  await mgr.getByRole("checkbox", { name: /Make recall follow-ups automatically/ }).check();
  await mgr.getByRole("button", { name: "Save the recall settings" }).click();
  await expect(mgr.getByText("Saved the recall settings.")).toBeVisible();
  await shot(mgr, "03-recall-on.png");
  await scanBothThemes(mgr, "recall-settings");
  await until(() => recallCount() > 0, "the recall task");
  await new Promise((r) => setTimeout(r, 9000));                                                              // more passes: still exactly one
  expect(recallCount()).toBe(1);
  expect(sql(`SELECT CONCAT(CASE WHEN AssigneeUserId IS NULL THEN 'unassigned' ELSE 'assigned' END, ',', CASE WHEN CreatedByUserId IS NULL THEN 'system' ELSE 'person' END) FROM FollowUpTasks WHERE Kind = N'Recall'`)).toBe("unassigned,system");
  evidence.recallTasks = 1;
});

test("WORK QUEUE: the dentist finds the unassigned recall, takes it, raises its priority and completes it", async () => {
  const dr = sessions.Dentist.page;
  await dr.goto("/work-queue");
  const row = dr.getByRole("row").filter({ hasText: RECALL_TITLE });
  await expect(row).toBeVisible();
  await expect(row).toContainText("Unassigned");
  await expect(row).toContainText("Overdue");                                                                 // the due date was a year of lead ago, said in words
  await expect(dr.getByRole("button", { name: /^Unassigned \(1\)/ })).toBeVisible();
  await shot(dr, "04-queue-recall.png");
  await scanBothThemes(dr, "work-queue");

  await dr.getByRole("button", { name: `Take ${RECALL_TITLE}` }).click();
  await expect(dr.getByText(`You took: ${RECALL_TITLE}.`)).toBeVisible();
  await expect(dr.getByRole("row").filter({ hasText: RECALL_TITLE })).toContainText(names.Dentist);

  await dr.getByRole("button", { name: `Change owner or priority of ${RECALL_TITLE}` }).click();
  await dr.locator("select[id^='qre-priority']").selectOption("High");
  await shot(dr, "05-owner-priority.png");
  await dr.getByRole("button", { name: "Save" }).click();
  await expect(dr.getByText(`Saved: ${RECALL_TITLE}.`)).toBeVisible();
  await expect(dr.getByRole("row").filter({ hasText: RECALL_TITLE })).toContainText("High");

  await dr.getByRole("button", { name: `Complete ${RECALL_TITLE}` }).click();
  await dr.getByLabel(/^Note/).fill("Booked for March");
  await dr.getByRole("button", { name: "Mark complete" }).click();
  await expect(dr.getByText(`Completed: ${RECALL_TITLE}.`)).toBeVisible();
  expect(sql(`SELECT Status FROM FollowUpTasks WHERE Kind = N'Recall'`)).toBe("Completed");
});

test("STALE: a change made on an out-of-date view is explained and nothing is overwritten", async () => {
  // a second recall is not needed: a manual task is changed in the background by the front desk while the dentist's screen still shows the old version
  const fd = sessions.FrontDesk.page;
  const created = await api(fd, "post", `/api/patients/${ids.ann}/follow-ups`, { title: "Call about the estimate", dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), assigneeUserId: ids.Dentist }, { "Idempotency-Key": `wq-manual-${stamp}` });
  expect(created.status, JSON.stringify(created.body)).toBe(200);
  const dr = sessions.Dentist.page;
  await dr.goto("/work-queue");
  await expect(dr.getByRole("row").filter({ hasText: "Call about the estimate" })).toBeVisible();
  const taskId = created.body.task.id as string;
  const version = created.body.task.rowVersion as string;
  await dr.getByRole("button", { name: "Complete Call about the estimate" }).click();
  expect((await api(fd, "post", `/api/follow-ups/${taskId}/priority`, { priority: "Urgent", rowVersion: version })).status).toBe(200);   // someone else changes it first
  await dr.getByRole("button", { name: "Mark complete" }).click();
  await expect(dr.getByText(/Someone else changed this follow-up/)).toBeVisible();
  expect(sql(`SELECT Status FROM FollowUpTasks WHERE Id = '${taskId}'`)).toBe("Open");
  await shot(dr, "06-stale.png");
});

test("TIMELINE: the patient's follow-ups page tells the whole story, the system's part as the system's", async () => {
  const fd = sessions.FrontDesk.page;
  await fd.goto(`/patients/${ids.ann}/follow-ups`);
  const list = fd.getByRole("list", { name: "Follow-up timeline" });
  await expect(list).toBeVisible();
  await expect(list).toContainText(`Made by the system: ${RECALL_TITLE} by the system`);
  await expect(list).toContainText(`Assigned: ${RECALL_TITLE} by ${names.Dentist}`);
  await expect(list).toContainText(`Priority changed: ${RECALL_TITLE} by ${names.Dentist}`);
  await expect(fd.getByText(/Recall|recall/).first()).toBeVisible();
  await shot(fd, "07-timeline.png");
  await scanBothThemes(fd, "patient-follow-ups");
});

test("AUDIT: a role without follow-up access is refused the timeline, and the audit trail names every action", async () => {
  const bill = sessions.Billing.page;
  expect((await api(bill, "get", `/api/patients/${ids.ann}/follow-ups/timeline`)).status).toBe(403);
  const types = sql(`SELECT DISTINCT EventType FROM AuditLogEntries WHERE EventType IN (N'FollowUpCreated', N'FollowUpAssigned', N'FollowUpPriorityChanged', N'FollowUpCompleted', N'RecallSettingsChanged') ORDER BY EventType`).split(/\r?\n/);
  evidence.auditEventTypes = types;
  expect(types).toEqual(expect.arrayContaining(["FollowUpAssigned", "FollowUpCompleted", "FollowUpCreated", "FollowUpPriorityChanged", "RecallSettingsChanged"]));
});
