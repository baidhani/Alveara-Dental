import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-009 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, with the real background reminder pass (every 3 s here, 60 s by default).
// A dentist creates a follow-up that is already due for a hygienist through the screen; within seconds the hygienist finds a reminder in the notification center (carrying no title or note), opens the patient's
// follow-ups from it, completes the task and a next follow-up is made; the history shows who did what and when; a stale change is explained; billing sees nothing; revoking the manage permission removes the buttons
// on the next request; a reminder for a person who has left goes to the office manager; the audit trail names the actions; the screens are scanned in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.FOLLOW_UPS_E2E_OUT ?? path.join(process.cwd(), "follow-ups-e2e-out");
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
const names: Record<string, string> = {};
const TITLE = "Check the temporary crown";
const NOTE = "Private note about the bite";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
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

/** The value of a datetime-local input for a moment some minutes from now on this machine's clock (the browser runs here too). */
function localInput(minutesFromNow: number): string {
  const d = new Date(Date.now() + minutesFromNow * 60_000);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

const followUpsUrl = () => `/patients/${ids.ann}/follow-ups`;
const listItem = (page: Page, title: string) => page.getByRole("listitem").filter({ hasText: title });

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  const boot = await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } });
  expect(boot.ok()).toBeTruthy();
  ids.adminId = (await boot.json()).id as string;
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "FrontDesk", "Billing", "OfficeManager"]) {
    names[role] = `${role.toLowerCase()}-${stamp}`;
    ids[role] = await createUser(role, names[role]);
    const context = await adminContext.browser()!.newContext();
    const page = await context.newPage();
    await signIn(page, names[role], PASSWORD);
    sessions[role] = { context, page };
  }
  const patient = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `fu-ann-${stamp}` });
  expect(patient.status).toBe(201);
  ids.ann = patient.body.id as string;
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "follow-ups-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("CREATE: a dentist checks the form, then creates a follow-up that is already due for the hygienist", async () => {
  const dr = sessions.Dentist.page;
  await dr.goto(followUpsUrl());
  await expect(dr.getByRole("heading", { name: "Follow-ups" })).toBeVisible();
  await expect(dr.getByText("No open follow-ups")).toBeVisible();
  await dr.getByRole("button", { name: "New follow-up" }).click();
  await dr.getByRole("button", { name: "Create follow-up" }).click();
  await expect(dr.getByText("Write a title.")).toBeVisible();                                                  // checked before anything is sent
  await expect(dr.getByText("Choose when it is due.")).toBeVisible();
  await shot(dr, "01-form-checked.png");
  expect((await api(admin, "get", `/api/patients/${ids.ann}/follow-ups?status=all`)).body.count).toBe(0);

  await dr.getByLabel(/^Title/).fill(TITLE);
  await dr.getByLabel(/^Note/).fill(NOTE);
  await dr.getByLabel(/^Due/).fill(localInput(-2));
  await dr.getByLabel(/^Responsible/).selectOption({ label: `${names.Hygienist} (Hygienist)` });
  await dr.getByLabel(/^Repeat every/).fill("90");
  await shot(dr, "02-form-filled.png");
  await dr.getByRole("button", { name: "Create follow-up" }).click();
  await expect(dr.getByRole("status").filter({ hasText: `Created the follow-up: ${TITLE}.` })).toBeVisible();
  const item = listItem(dr, TITLE);
  await expect(item).toContainText(`Responsible: ${names.Hygienist}`);
  await expect(item).toContainText("Repeats 90 days after completion");
  await shot(dr, "03-created.png");
  evidence.create = { createdThroughTheScreen: true, validationBeforeSending: true };
});

test("REMINDER: within seconds the hygienist finds a reminder, with no title or note, and opens the patient's follow-ups from it", async () => {
  const hana = sessions.Hygienist.page;
  const started = Date.now();
  await expect(async () => {
    await hana.goto("/notifications");
    await expect(hana.getByText("A follow-up is due")).toBeVisible({ timeout: 1500 });
  }).toPass({ timeout: 45_000, intervals: [1000] });
  const seconds = Math.round((Date.now() - started) / 100) / 10;
  await shot(hana, "04-reminder-in-the-center.png");

  const raw = JSON.stringify((await api(hana, "get", "/api/notifications?status=unread")).body);
  expect(raw).toContain("followup.due");
  expect(raw).not.toContain(TITLE);
  expect(raw).not.toContain(NOTE);                                                                              // the reminder says that something is due, never what

  expect(JSON.stringify((await api(sessions.Dentist.page, "get", "/api/notifications?status=unread")).body)).not.toContain("followup.due");   // the creator is not the responsible party
  await hana.getByRole("button", { name: "Open patient" }).first().click();
  await expect(hana).toHaveURL(new RegExp(`/patients/${ids.ann}/follow-ups$`));
  await expect(listItem(hana, TITLE)).toBeVisible();
  evidence.reminder = { secondsUntilTheReminderWasVisible: seconds, carriesNoTitleOrNote: true, openedThePatientsFollowUps: true };
});

test("COMPLETE: the hygienist completes it, the next follow-up is made, and the history says who did what and when", async () => {
  const hana = sessions.Hygienist.page;
  await hana.goto(followUpsUrl());
  await hana.getByRole("button", { name: `Complete ${TITLE}` }).click();
  await expect(hana.getByText(/A new follow-up will be made for 90 days/)).toBeVisible();
  await hana.getByLabel(/^Note/).fill("Spoke to the patient; the crown is comfortable");
  await hana.getByRole("button", { name: "Mark complete" }).click();
  await expect(hana.getByRole("status").filter({ hasText: `Completed: ${TITLE}. The next follow-up was created.` })).toBeVisible();

  const open = listItem(hana, TITLE);
  await expect(open).toHaveCount(1);                                                                              // the recall is the one open task now
  await expect(open).toContainText("Made by completing an earlier follow-up");
  await hana.getByRole("button", { name: "Closed" }).click();
  const closed = listItem(hana, TITLE);
  await expect(closed).toContainText("Completed");
  await expect(closed).toContainText("the crown is comfortable");
  await closed.getByRole("button", { name: `History of ${TITLE}` }).click();
  const history = hana.getByRole("list", { name: "History" });
  await expect(history.getByRole("listitem")).toHaveCount(4);
  await expect(history.getByRole("listitem").nth(0)).toContainText(`Created by ${names.Dentist}`);
  await expect(history.getByRole("listitem").nth(1)).toContainText("Reminder sent by the system");
  await expect(history.getByRole("listitem").nth(2)).toContainText(`Completed by ${names.Hygienist}`);
  await expect(history.getByRole("listitem").nth(3)).toContainText("Next follow-up created");
  await shot(hana, "05-completed-with-history.png");

  const recall = (await api(hana, "get", `/api/patients/${ids.ann}/follow-ups?status=open`)).body.items[0];
  const days = (new Date(recall.dueAtUtc).getTime() - Date.now()) / 86_400_000;
  expect(days).toBeGreaterThan(89.9);
  expect(days).toBeLessThan(90.1);
  expect(recall.assigneeUserId).toBe(ids.Hygienist);
  evidence.complete = { recallCreated: true, recallDueInDays: Math.round(days * 10) / 10, historyEntries: 4 };
});

test("STALE: a change made from an out-of-date screen is explained, stores nothing and the reload shows the current task", async () => {
  const task = await api(admin, "post", `/api/patients/${ids.ann}/follow-ups`, { title: "Order the night guard", note: null, dueAtUtc: new Date(Date.now() + 5 * 86_400_000).toISOString(), assigneeUserId: ids.Dentist, recallIntervalDays: null }, { "Idempotency-Key": `fu-stale-${stamp}` });
  expect(task.status).toBe(200);
  const dr = sessions.Dentist.page;
  const hana = sessions.Hygienist.page;
  await dr.goto(followUpsUrl());
  await hana.goto(followUpsUrl());
  await expect(listItem(hana, "Order the night guard")).toBeVisible();

  await dr.getByRole("button", { name: "Edit Order the night guard" }).click();
  await dr.getByLabel(/^Title/).fill("Order the night guard and the retainer");
  await dr.getByRole("button", { name: "Save changes" }).click();
  await expect(dr.getByRole("status").filter({ hasText: "Saved the changes to: Order the night guard and the retainer." })).toBeVisible();

  await hana.getByRole("button", { name: "Complete Order the night guard" }).click();
  await hana.getByRole("button", { name: "Mark complete" }).click();
  await expect(hana.getByRole("alert")).toContainText("Someone else changed this follow-up");
  await shot(hana, "06-stale-change.png");
  expect(sql(`SELECT Status FROM FollowUpTasks WHERE Title = N'Order the night guard and the retainer'`)).toBe("Open");   // nothing was stored for the stale completion
  await hana.getByRole("button", { name: "Reload the follow-up" }).click();
  await expect(hana.getByRole("status").filter({ hasText: "Reloaded the follow-ups." })).toBeVisible();
  await expect(listItem(hana, "Order the night guard and the retainer")).toBeVisible();
  evidence.stale = { explained: true, stored: "nothing", reloadShowsTheCurrentTitle: true };
});

test("ROLES: billing sees nothing, and revoking the manage permission removes the buttons on the next request", async () => {
  const billing = sessions.Billing.page;
  await billing.goto(`/patients/${ids.ann}`);
  await expect(billing.getByRole("link", { name: "Follow-ups" })).toHaveCount(0);                               // no tab
  expect((await api(billing, "get", `/api/patients/${ids.ann}/follow-ups`)).status).toBe(403);
  expect((await api(billing, "get", "/api/follow-ups/mine")).body).toEqual({ error: "permission_denied", required: "ViewFollowUps" });
  await billing.goto(followUpsUrl());
  await expect(billing.getByRole("heading", { name: "Follow-ups" })).toHaveCount(0);
  await shot(billing, "07-billing-denied.png");

  const desk = sessions.FrontDesk.page;
  await desk.goto(followUpsUrl());
  await expect(desk.getByRole("button", { name: "New follow-up" })).toBeVisible();
  const version = (await api(admin, "get", "/api/permissions/effective")).body.version as number;
  const revoke = await api(admin, "put", "/api/permissions/overrides", { role: "FrontDesk", permission: "ManageFollowUps", effect: "Revoke", reason: "Front desk only reads follow-ups", expectedVersion: version, confirmed: true }, { "Idempotency-Key": `fu-revoke-${stamp}` });
  expect(revoke.status).toBe(200);
  expect((await api(desk, "post", `/api/patients/${ids.ann}/follow-ups`, { title: "x", dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), assigneeUserId: ids.Dentist }, { "Idempotency-Key": `fu-desk-${stamp}` })).status).toBe(403);   // the very next request
  await desk.goto(followUpsUrl());
  await expect(desk.getByText("Order the night guard and the retainer")).toBeVisible();                          // still reads
  await expect(desk.getByRole("button", { name: "New follow-up" })).toHaveCount(0);
  await expect(desk.getByText(/your role cannot change them/)).toBeVisible();
  await shot(desk, "08-front-desk-read-only.png");

  const v2 = (await api(admin, "get", "/api/permissions/effective")).body.version as number;
  expect((await api(admin, "put", "/api/permissions/overrides", { role: "FrontDesk", permission: "ManageFollowUps", effect: "Revert", reason: "Back to the default", expectedVersion: v2, confirmed: true }, { "Idempotency-Key": `fu-revert-${stamp}` })).status).toBe(200);
  evidence.roles = { billingDenied: true, revokeAppliedOnTheNextRequest: true, readOnlyScreenShown: true, revertedToDefault: true };
});

test("FALLBACK: a reminder for someone who has left goes to the office manager", async () => {
  const leaverName = `leaver-${stamp}`;
  const leaver = await createUser("Hygienist", leaverName);
  const created = await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/follow-ups`, { title: "Chase the lab", note: null, dueAtUtc: new Date(Date.now() + 86_400_000).toISOString(), assigneeUserId: leaver, recallIntervalDays: null }, { "Idempotency-Key": `fu-leaver-${stamp}` });
  expect(created.status).toBe(200);
  expect((await api(admin, "put", `/api/auth/${leaver}/enabled`, { enabled: false })).status).toBe(200);
  const moved = await api(sessions.Dentist.page, "put", `/api/follow-ups/${created.body.task.id}`, {
    title: "Chase the lab", note: null, dueAtUtc: new Date(Date.now() - 120_000).toISOString(), assigneeUserId: leaver, recallIntervalDays: null, rowVersion: created.body.task.rowVersion });
  expect(moved.status).toBe(200);                                                                                // the date of a task whose person has left can still be changed

  const manager = sessions.OfficeManager.page;
  await expect(async () => {
    await manager.goto("/notifications");
    await expect(manager.getByText("A follow-up is due").first()).toBeVisible({ timeout: 1500 });
  }).toPass({ timeout: 45_000, intervals: [1000] });
  expect(sql(`SELECT Outcome FROM FollowUpReminderDeliveries d JOIN FollowUpTasks t ON t.Id = d.TaskId WHERE t.Title = N'Chase the lab'`)).toBe("Fallback");
  await shot(manager, "09-fallback-to-the-office-manager.png");
  evidence.fallback = { deliveredToTheOfficeManager: true, outcome: "Fallback" };
});

test("AUDIT: every follow-up action is in the audit trail with who and when, and the reminder is the system's", async () => {
  const rows = sql(`SELECT EventType + N':' + CAST(COUNT(*) AS NVARCHAR(10)) FROM AuditLogEntries WHERE EventType LIKE N'FollowUp%' GROUP BY EventType ORDER BY EventType`).split(/\r?\n/);
  const counts = Object.fromEntries(rows.map((r) => r.split(":")));
  expect(Number(counts.FollowUpCreated)).toBeGreaterThanOrEqual(4);                                                // the first task, its recall, the night guard and the lab task
  expect(Number(counts.FollowUpCompleted)).toBe(1);
  expect(Number(counts.FollowUpChanged)).toBeGreaterThanOrEqual(2);
  expect(Number(counts.FollowUpReminderSent)).toBeGreaterThanOrEqual(2);
  expect(sql(`SELECT COUNT(*) FROM AuditLogEntries WHERE EventType = N'FollowUpCompleted' AND PerformedByUserAccountId = '${ids.Hygienist}'`)).toBe("1");
  expect(sql(`SELECT COUNT(*) FROM AuditLogEntries WHERE EventType = N'FollowUpReminderSent' AND PerformedByUserAccountId IS NOT NULL`)).toBe("0");
  expect(sql(`SELECT COUNT(*) FROM AuditLogEntries WHERE EventType LIKE N'FollowUp%' AND (Details LIKE N'%${TITLE}%' OR Details LIKE N'%bite%')`)).toBe("0");   // the entries name ids, never the task text
  evidence.audit = counts;
});

test("ACCESSIBILITY: the follow-up screens pass an axe scan in both themes", async () => {
  const dr = sessions.Dentist.page;
  await dr.goto(followUpsUrl());
  await expect(dr.getByRole("heading", { name: "Follow-ups" })).toBeVisible();
  await dr.getByRole("button", { name: "New follow-up" }).click();
  await expect(dr.getByLabel(/^Title/)).toBeVisible();
  await scanBothThemes(dr, "follow-ups-with-form");
  await dr.getByRole("button", { name: "Cancel" }).first().click();
  await dr.getByRole("button", { name: "All" }).click();
  await expect(dr.getByRole("list", { name: "all follow-ups" })).toBeVisible();
  await scanBothThemes(dr, "follow-ups-all");
  await dr.goto("/follow-ups");
  await expect(dr.getByRole("heading", { name: "My follow-ups" })).toBeVisible();
  await scanBothThemes(dr, "my-follow-ups");
});
