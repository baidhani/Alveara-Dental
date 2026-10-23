import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N015 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api (with its real background consumer) -> real SQL Server.
// Excluded from the default mocked run and run with playwright.notifications.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// A dentist writes a Normal then an Urgent message to the front desk through the screen; the bell counts them, only the Urgent one raises the generic banner, the banner can be dismissed, opening a message
// shows the sender a read receipt, a read notice is hidden from the front desk's history yet stays in the administrator's metadata history (which never shows text), a patient link opens only through the
// server, a confirmed duplicate document import is delivered to the administrator and the office manager exactly once by the background consumer (and not to the dentist), and the administrator opens one
// message only with a reason and a confirmation, every attempt audited, with no message text in the audit log. Both themes are scanned for accessibility.

test.describe.configure({ mode: "serial" });

const OUT = process.env.NOTIFICATIONS_E2E_OUT ?? path.join(process.cwd(), "notifications-e2e-out");
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

const PNG_1X1 = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const RADIOGRAPH = "d0c00000-0000-0000-0000-000000000001";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", statement], { stdio: "pipe" }).toString();
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

/** Writes a message through the compose form the way a person does. */
async function compose(page: Page, to: string, subject: string, body: string, priority: "Normal" | "Important" | "Urgent") {
  await page.goto("/messages");
  await page.getByRole("button", { name: "New message" }).click();
  await page.getByLabel("Find a person").fill(to);
  await page.getByRole("checkbox", { name: new RegExp(to) }).check();
  await page.getByLabel("Subject").fill(subject);
  await page.getByLabel(/Message \(plain text/).fill(body);
  await page.getByLabel("Priority").selectOption(priority);
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.getByRole("heading", { name: subject })).toBeVisible();
}

const bellOf = (page: Page, text: string | RegExp) => page.getByRole("link", { name: text });

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["OfficeManager", "Dentist", "FrontDesk"]) {
    ids[`${role}User`] = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    names[role] = `${role} ${stamp}`;
    expect((await api(admin, "post", "/api/config/staff", { displayName: names[role], userAccountId: ids[`${role}User`] })).status, `staff profile for ${role}`).toBe(201);
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "notifications-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a patient exists", async () => {
  const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `notif-ann-${stamp}` });
  expect(r.status).toBe(201);
  ids.ann = r.body.id as string;
});

test("BELL: a Normal message is counted by the bell and raises no banner", async () => {
  const dentist = sessions.Dentist.page;
  const desk = sessions.FrontDesk.page;
  await compose(dentist, names.FrontDesk, "Room 3 setup", "Please set up room 3 for the 2 pm crown.", "Normal");
  await shot(dentist, "01-message-sent.png");
  await desk.goto("/");
  await expect(bellOf(desk, "Notifications, 1 unread")).toBeVisible();
  await expect(desk.getByText(/important notification|urgent notification/)).toHaveCount(0);
  await shot(desk, "02-bell-normal.png");
  evidence.bell = { unread: 1, banner: false };
});

test("BANNER: an Urgent message raises a generic, dismissible banner that carries no subject and returns after sign-in", async () => {
  const dentist = sessions.Dentist.page;
  const desk = sessions.FrontDesk.page;
  await compose(dentist, names.FrontDesk, "Confidential-urgent-subject", "Confidential-urgent-body.", "Urgent");
  await desk.goto("/");
  await expect(bellOf(desk, "Notifications, 2 unread")).toBeVisible();
  const banner = desk.getByRole("alert").filter({ hasText: "urgent notification" });
  await expect(banner).toBeVisible();
  await expect(banner).not.toContainText(/Confidential|Dentist|Ann|Alder/);
  await shot(desk, "03-banner-urgent.png");
  await banner.getByRole("button", { name: "Dismiss" }).click();
  await expect(banner).toHaveCount(0);
  await desk.reload();                                                                   // a new page load is a new sign-in view: the unread urgent notice is shown again
  await expect(desk.getByRole("alert").filter({ hasText: "urgent notification" })).toBeVisible();
  evidence.banner = { generic: true, dismissible: true, returnsAfterReload: true };
});

test("RECEIPT: opening a message shows the sender when it was read, and the message stays unchanged", async () => {
  const desk = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  await desk.goto("/messages");
  await desk.getByRole("link", { name: "Room 3 setup" }).click();
  await expect(desk.getByText("Please set up room 3 for the 2 pm crown.")).toBeVisible();
  await expect(desk.getByText(/not read yet|read \d/)).toHaveCount(0);                    // the receipt is for the sender only
  await dentist.goto("/messages");
  await dentist.getByRole("tab", { name: "Sent" }).click();
  await dentist.getByRole("link", { name: "Room 3 setup" }).click();
  await expect(dentist.getByText(new RegExp(`${names.FrontDesk}: read `))).toBeVisible();
  await shot(dentist, "04-read-receipt.png");
  evidence.receipt = { senderSeesRead: true, recipientSeesReceipt: false };
});

test("HISTORY: a read notice moves to history, can be hidden, and stays in the administrator's metadata history without any text", async () => {
  const desk = sessions.FrontDesk.page;
  await desk.goto("/notifications");
  await expect(desk.getByText("Loading notifications…")).toHaveCount(0);
  await expect(desk.getByText("Room 3 setup")).toHaveCount(0);                            // read, so no longer in the unread list
  await desk.getByRole("tab", { name: "History" }).click();
  const row = desk.getByRole("listitem").filter({ hasText: "Room 3 setup" });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Hide" }).click();
  await expect(desk.getByRole("listitem").filter({ hasText: "Room 3 setup" })).toHaveCount(0);
  await shot(desk, "05-history-hidden.png");

  await admin.goto("/admin/notifications");
  await admin.getByLabel("Status").selectOption("Hidden");
  await expect(admin.getByRole("table")).toContainText(names.FrontDesk);
  await expect(admin.getByRole("table")).toContainText("Hidden");
  await expect(admin.getByRole("table")).not.toContainText(/Room 3|Confidential|crown/);
  await shot(admin, "06-admin-history.png");
  evidence.history = { hiddenByRecipient: true, adminSeesHidden: true, adminSeesText: false };
});

test("LINK: a patient link resolves through the server and opens the patient", async () => {
  const dentist = sessions.Dentist.page;
  const desk = sessions.FrontDesk.page;
  const sent = await api(dentist, "post", "/api/messages", { recipientUserIds: [ids.FrontDeskUser], subject: "About a patient", body: "Please call about the estimate.", priority: "Normal", relatedPatientId: ids.ann }, { "Idempotency-Key": `link-${stamp}` });
  expect(sent.status).toBe(201);
  const list = await api(desk, "get", "/api/messages?box=inbox");
  expect(JSON.stringify(list.body)).not.toContain(ids.ann);                              // no list carries the patient id
  await desk.goto("/messages");
  await desk.getByRole("link", { name: "About a patient" }).click();
  await desk.getByRole("button", { name: "Open patient" }).click();
  await expect(desk).toHaveURL(new RegExp(`/patients/${ids.ann}`));
  evidence.link = { listHasPatientId: false, opensPatient: true };
});

test("DUPLICATE: a confirmed duplicate import reaches the administrator and the office manager exactly once, and not the dentist", async () => {
  const upload = async (confirm?: string) => {
    const multipart: Record<string, unknown> = { file: { name: "bitewing.png", mimeType: "image/png", buffer: PNG_1X1 }, categoryId: RADIOGRAPH, documentDate: "2026-10-01", description: "Bitewing." };
    if (confirm) multipart.confirmDuplicateOf = confirm;
    const r = await admin.request.post(`/api/patients/${ids.ann}/documents`, { headers: { "X-CSRF-Token": await csrf(admin), "Idempotency-Key": `dup-${confirm ? "b" : "a"}-${stamp}` }, multipart: multipart as never });
    return { status: r.status(), body: (await r.json().catch(() => ({}))) as any };
  };
  const first = await upload();
  expect(first.status).toBe(201);
  const second = await upload(first.body.document.id);
  expect(second.status).toBe(201);

  const manager = sessions.OfficeManager.page;
  await expect.poll(async () => (await api(manager, "get", "/api/notifications/summary")).body.unreadImportant, { timeout: 40_000, intervals: [1000] }).toBe(1);
  await expect.poll(async () => (await api(admin, "get", "/api/notifications/summary")).body.unreadImportant, { timeout: 40_000, intervals: [1000] }).toBe(1);
  await new Promise((r) => setTimeout(r, 12_000));                                         // two more consumer rounds: still exactly one each
  for (const page of [manager, admin]) expect((await api(page, "get", "/api/notifications/summary")).body.unreadImportant).toBe(1);
  expect((await api(sessions.Dentist.page, "get", "/api/notifications/summary")).body.unreadImportant).toBe(0);

  await manager.goto("/");
  await expect(manager.getByRole("status").filter({ hasText: "important notification" })).toBeVisible();
  await manager.goto("/notifications");
  const item = manager.getByRole("listitem").filter({ hasText: "A duplicate document was added" });
  await expect(item).toBeVisible();
  await expect(item).not.toContainText(/bitewing|Alder|Ann/i);
  await shot(manager, "07-duplicate-notification.png");
  await item.getByRole("button", { name: "Open patient" }).click();
  await expect(manager).toHaveURL(new RegExp(`/patients/${ids.ann}/documents`));
  const rows = sql("SET NOCOUNT ON; SELECT COUNT(*) FROM Notifications WHERE Kind = 'document.duplicate_added'").trim();
  expect(rows).toBe("2");
  evidence.duplicate = { adminUnreadImportant: 1, officeManagerUnreadImportant: 1, dentist: 0, rowsInDatabase: 2, textInNotice: false };
});

test("BREAK-GLASS: one message opens only with a reason and a confirmation, every attempt is audited, and no text reaches the audit log", async () => {
  const sent = await api(sessions.Dentist.page, "get", "/api/messages?box=sent");
  const urgent = (sent.body.items as any[]).find((m) => m.subject === "Confidential-urgent-subject");
  await admin.goto("/admin/notifications");
  await admin.getByLabel("Message id").fill(urgent.id);
  await admin.getByRole("button", { name: "Open", exact: true }).click();
  await expect(admin.getByRole("alert").filter({ hasText: /reason/i })).toBeVisible();
  await expect(admin.getByText("Confidential-urgent-body.")).toHaveCount(0);
  await admin.getByLabel("Reason (required)").fill("Complaint review 42");
  await admin.getByRole("button", { name: "Open", exact: true }).click();
  await expect(admin.getByRole("alert").filter({ hasText: /confirm/i })).toBeVisible();
  await expect(admin.getByText("Confidential-urgent-body.")).toHaveCount(0);
  await admin.getByLabel(/I confirm I am opening private messages/).check();
  await admin.getByRole("button", { name: "Open", exact: true }).click();
  await expect(admin.getByText("Confidential-urgent-body.")).toBeVisible();
  await shot(admin, "08-break-glass.png");

  const audit = (await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[];
  const of = (t: string) => audit.filter((e) => e.eventType === t);
  expect(of("MessageBreakGlassAttempted").length).toBe(3);
  expect(of("MessageBreakGlassRefused").length).toBe(2);
  expect(of("MessageBreakGlassSucceeded").length).toBe(1);
  const text = JSON.stringify(audit.filter((e) => /^(Message|Notification)/.test(e.eventType)));         // this feature's own entries
  expect(JSON.parse(text).length).toBeGreaterThanOrEqual(6);
  expect(text).not.toMatch(/Confidential-urgent|Room 3|crown|bitewing|Alder/i);
  expect(text).not.toContain(ids.ann);
  const dentistTry = await api(sessions.Dentist.page, "post", "/api/admin/messages/break-glass", { messageId: urgent.id, reason: "x", confirmed: true });
  expect(dentistTry.status).toBe(403);
  evidence.breakGlass = { attempted: 3, refused: 2, succeeded: 1, textInAudit: false, nonAdmin: 403 };
});

test("DB: the database itself refuses to change or delete a message or a notification, and to hide an unread one", async () => {
  const refused = (statement: string) => { try { sql(statement); return false; } catch (e) { return true; } };
  expect(refused("UPDATE Messages SET Body = N'changed'")).toBe(true);
  expect(refused("DELETE FROM Messages")).toBe(true);
  expect(refused("DELETE FROM Notifications")).toBe(true);
  expect(refused("UPDATE Notifications SET Priority = 'Normal'")).toBe(true);
  expect(refused("UPDATE Notifications SET DeletedAtUtc = SYSDATETIMEOFFSET() WHERE ReadAtUtc IS NULL")).toBe(true);
  evidence.database = { updateMessage: "refused", deleteMessage: "refused", deleteNotification: "refused", changePriority: "refused", hideUnread: "refused" };
});

test("ACCESSIBILITY: the bell, notifications, messages, thread, compose and administrator screens pass an axe scan in both themes", async () => {
  const desk = sessions.FrontDesk.page;
  await desk.goto("/notifications");
  await expect(desk.getByRole("heading", { name: "Notifications", exact: true })).toBeVisible();
  await scanBothThemes(desk, "notifications");
  await desk.goto("/messages");
  await expect(desk.getByRole("heading", { name: "Messages", exact: true })).toBeVisible();
  await desk.getByRole("button", { name: "New message" }).click();
  await scanBothThemes(desk, "messages-compose");
  await desk.getByRole("link", { name: "About a patient" }).click();
  await expect(desk.getByRole("button", { name: "Reply" })).toBeVisible();
  await scanBothThemes(desk, "message-thread");
  await admin.goto("/admin/notifications");
  await expect(admin.getByRole("heading", { name: "Notification history" })).toBeVisible();
  await expect(admin.getByRole("table")).toBeVisible();
  await scanBothThemes(admin, "admin-history-break-glass");
  expect((await api(sessions.Dentist.page, "post", "/api/messages", { recipientUserIds: [ids.OfficeManagerUser], subject: "Heads up", body: "Important heads up.", priority: "Important" }, { "Idempotency-Key": `banner-${stamp}` })).status).toBe(201);
  await sessions.OfficeManager.page.goto("/");
  await expect(sessions.OfficeManager.page.getByRole("status").filter({ hasText: "important notification" })).toBeVisible();
  await scanBothThemes(sessions.OfficeManager.page, "banner");
});
