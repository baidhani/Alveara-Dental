import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N017 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run and run with playwright.permission-policy.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// With no stored policy the matrix and every role behave as before; a stored revoke takes effect on the very next request and a revert restores it; a stored grant of a protected permission is invalid data that
// denies its role (with its own code) until an administrator removes the row with a reason; the health, history and audit show it; the only enabled administrator cannot be demoted or disabled, not even by
// raw SQL; a granted office manager can act only on accounts the target rules allow; the database refuses to change the policy history. The permission matrix screen is scanned in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PERMISSION_POLICY_E2E_OUT ?? path.join(process.cwd(), "permission-policy-e2e-out");
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

async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", `SET NOCOUNT ON; ${statement}`], { stdio: "pipe" }).toString();
}

function sqlRefusal(statement: string): string {
  try { sql(statement); return ""; } catch (e) { const err = e as { stdout?: Buffer; stderr?: Buffer }; return `${err.stdout ?? ""}${err.stderr ?? ""}`; }
}

/** One policy change as a committed transaction: the version moves with the row, exactly as the engine expects. */
function policy(change: string) {
  sql(`BEGIN TRAN; UPDATE PermissionPolicyState SET Version = Version + 1, UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 1; ${change}; COMMIT;`);
}

const addOverride = (role: string, permission: string, effect: string) =>
  policy(`INSERT INTO RolePermissionOverrides (Id, Role, Permission, Effect, Reason, ChangedByUserId, ChangedAtUtc) VALUES (NEWID(), N'${role}', N'${permission}', N'${effect}', N'walkthrough', '${ids.adminId}', SYSDATETIMEOFFSET())`);

const removeOverrides = (role: string) => policy(`DELETE FROM RolePermissionOverrides WHERE Role = N'${role}'`);

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

const matrixHeading = (page: Page) => page.getByRole("heading", { name: "Permission matrix" });
const denied = (page: Page) => page.getByText("You don't have permission to view this");

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  const boot = await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } });
  expect(boot.ok()).toBeTruthy();
  ids.adminId = (await boot.json()).id as string;
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["OfficeManager", "Dentist"]) {
    ids[`${role}User`] = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    const context = await adminContext.browser()!.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "permission-policy-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("DEFAULTS: with no stored policy the matrix, the effective view and the health match the code defaults and the user list is as before", async () => {
  const manager = sessions.OfficeManager.page;
  await admin.goto("/admin/permissions");
  await expect(matrixHeading(admin)).toBeVisible();
  await manager.goto("/admin/permissions");
  await expect(matrixHeading(manager)).toBeVisible();
  await shot(manager, "01-matrix-default.png");

  const effective = await api(admin, "get", "/api/permissions/effective");
  expect(effective.status).toBe(200);
  expect(effective.body.health.status).toBe("Healthy");
  const cells = effective.body.roles.flatMap((r: any) => r.permissions) as any[];
  expect(cells.every((c) => c.source === "default")).toBe(true);
  expect((await api(admin, "get", "/api/permissions/history")).body.total).toBe(0);
  expect((await api(admin, "get", "/api/permissions/health")).body.status).toBe("Healthy");

  expect((await api(admin, "get", "/api/auth/users")).status).toBe(200);
  expect((await api(manager, "get", "/api/auth/users")).status).toBe(403);
  evidence.defaults = { health: "Healthy", cellsNotDefault: 0, history: 0, officeManagerUserList: 403 };
});

test("REVOKE: a stored revoke takes effect on the very next request, on screen and in the API, and a revert restores it", async () => {
  const manager = sessions.OfficeManager.page;
  addOverride("OfficeManager", "ViewPermissionMatrix", "Revoke");
  const api403 = await api(manager, "get", "/api/auth/permission-matrix");
  expect(api403.status).toBe(403);
  expect(api403.body).toEqual({ error: "permission_denied", required: "ViewPermissionMatrix" });                 // the 403 body every client already handles
  await manager.goto("/admin/permissions");
  await expect(denied(manager)).toBeVisible();
  await shot(manager, "02-matrix-revoked.png");
  await expect(matrixHeading(admin)).toBeVisible();                                                              // another role is untouched
  const effective = (await api(admin, "get", "/api/permissions/effective")).body;
  const cell = effective.roles.find((r: any) => r.role === "OfficeManager").permissions.find((c: any) => c.permission === "ViewPermissionMatrix");
  expect([cell.source, cell.effective, cell.reason]).toEqual(["revoked", false, "walkthrough"]);

  removeOverrides("OfficeManager");
  await manager.goto("/admin/permissions");
  await expect(matrixHeading(manager)).toBeVisible();
  expect((await api(manager, "get", "/api/auth/permission-matrix")).status).toBe(200);
  evidence.revoke = { apiStatus: 403, body: "permission_denied", screen: "denied", afterRevert: "visible", otherRoleAffected: false };
});

test("INVALID DATA: a stored grant of a protected permission denies its role with its own code until an administrator removes the row with a reason", async () => {
  const manager = sessions.OfficeManager.page;
  addOverride("OfficeManager", "ManageUsers", "Grant");
  const attempt = await api(manager, "get", "/api/auth/users");
  expect(attempt.status).toBe(403);                                                                              // the protected grant was not applied
  const everything = await api(manager, "get", "/api/auth/permissions");
  expect(everything.body.permissions).toEqual([]);                                                               // a role in repair holds nothing, so no screen offers what a request would refuse
  const anything = await api(manager, "get", "/api/auth/permission-matrix");
  expect([anything.status, anything.body.error]).toEqual([403, "permission_policy_repair_required"]);
  await manager.goto("/admin/permissions");
  await expect(denied(manager)).toBeVisible();

  const health = (await api(admin, "get", "/api/permissions/health")).body;
  expect(health.status).toBe("RepairRequired");
  expect(health.rolesNeedingRepair).toEqual(["OfficeManager"]);
  const row = health.invalidRows[0];
  expect([row.role, row.permission, row.reason]).toEqual(["OfficeManager", "ManageUsers", "ProtectedPermissionOverride"]);
  await shot(admin, "03-health-before-repair.png");

  const noReason = await api(admin, "post", `/api/permissions/invalid-rows/${row.id}/remove`, { reason: " ", rowVersion: row.rowVersion });
  expect([noReason.status, noReason.body.error]).toEqual([400, "reason_required"]);
  const removed = await api(admin, "post", `/api/permissions/invalid-rows/${row.id}/remove`, { reason: "Walkthrough repair of a corrupt row", rowVersion: row.rowVersion });
  expect(removed.status).toBe(200);
  expect((await api(admin, "post", `/api/permissions/invalid-rows/${row.id}/remove`, { reason: "again", rowVersion: row.rowVersion })).status).toBe(404);   // quiet the second time

  await manager.goto("/admin/permissions");
  await expect(matrixHeading(manager)).toBeVisible();
  expect((await api(admin, "get", "/api/permissions/health")).body.status).toBe("Healthy");
  const history = (await api(admin, "get", "/api/permissions/history")).body;
  expect([history.total, history.items[0].kind, history.items[0].reason]).toEqual([1, "InvalidRowRemoved", "Walkthrough repair of a corrupt row"]);
  const audit = ((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).map((e) => e.eventType);
  expect(audit).toContain("PermissionPolicyInvalidRowDetected");
  expect(audit).toContain("InvalidPolicyRowRemoved");
  const auditText = JSON.stringify(((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).filter((e) => /PermissionPolicy|InvalidPolicyRow/.test(e.eventType)));
  expect(auditText).not.toContain("ManageUsers");                                                                // an audit entry about a row never copies the role or permission text from it
  evidence.invalid = { repairRequiredCode: "permission_policy_repair_required", healthAfter: "Healthy", history: 1, detectedOnce: true };
});

test("LAST ADMINISTRATOR: the only enabled administrator cannot be demoted or disabled, through the API or raw SQL", async () => {
  const role = await api(admin, "put", `/api/auth/${ids.adminId}/role`, { role: "Dentist" });
  expect([role.status, role.body.error]).toEqual([409, "last_admin"]);
  const disable = await api(admin, "put", `/api/auth/${ids.adminId}/enabled`, { enabled: false });
  expect([disable.status, disable.body.error]).toEqual([409, "last_admin"]);
  expect(sqlRefusal(`UPDATE UserAccounts SET Role = 0 WHERE Id = '${ids.adminId}'`)).toContain("51207");
  expect(sqlRefusal(`UPDATE UserAccounts SET IsDisabled = 1 WHERE Role = 6`)).toContain("51207");
  expect(sqlRefusal(`DELETE FROM UserAccounts WHERE Id = '${ids.adminId}'`)).toMatch(/51207|547/);                  // the last-administrator trigger, or the history that references the administrator (a foreign key), whichever is evaluated first
  await admin.goto("/admin/permissions");
  await expect(matrixHeading(admin)).toBeVisible();                                                              // still signed in, still an administrator
  expect(sql("SELECT COUNT(*) FROM UserAccounts WHERE Role = 6 AND IsDisabled = 0").trim()).toBe("1");
  evidence.lastAdmin = { roleChange: "409 last_admin", disable: "409 last_admin", rawSql: "refused (update and bulk update by trigger 51207; delete by trigger 51207 or the history foreign key)" };
});

test("TARGET RULES: an office manager who has been granted account administration can act only on accounts the rules allow, and the user list stays closed", async () => {
  const manager = sessions.OfficeManager.page;
  addOverride("OfficeManager", "ManageAccountStatus", "Grant");
  addOverride("OfficeManager", "RevokeSessions", "Grant");
  const onAdmin = await api(manager, "put", `/api/auth/${ids.adminId}/enabled`, { enabled: false });
  expect([onAdmin.status, onAdmin.body.error, onAdmin.body.rule]).toEqual([403, "account_target_not_allowed", "target_is_admin"]);
  const onDentist = await api(manager, "put", `/api/auth/${ids.DentistUser}/enabled`, { enabled: false });
  expect([onDentist.status, onDentist.body.rule]).toEqual([403, "target_exceeds_actor"]);
  const self = await api(manager, "put", `/api/auth/${ids.OfficeManagerUser}/enabled`, { enabled: false });
  expect([self.status, self.body.rule]).toEqual([403, "self_disable"]);
  expect((await api(manager, "get", "/api/auth/users")).status).toBe(403);                                      // no broader user-list access in this story
  const refusals = ((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).filter((e) => e.eventType === "AccountActionRefused");
  expect(refusals.length).toBeGreaterThanOrEqual(3);
  expect(sql("SELECT COUNT(*) FROM UserAccounts WHERE IsDisabled = 1").trim()).toBe("0");                       // nothing was disabled
  removeOverrides("OfficeManager");
  evidence.targetRules = { onAdmin: "target_is_admin", onDentist: "target_exceeds_actor", self: "self_disable", userList: 403, refusalsAudited: refusals.length };
});

test("DATABASE: the policy history cannot be changed or deleted and the policy version only moves forward by one", async () => {
  expect(sqlRefusal("UPDATE RolePermissionHistories SET Reason = N'rewritten'")).toContain("51204");
  expect(sqlRefusal("DELETE FROM RolePermissionHistories")).toContain("51204");
  expect(sqlRefusal("UPDATE PermissionPolicyState SET Version = Version + 5 WHERE Id = 1")).toContain("51205");
  expect(sqlRefusal("DELETE FROM PermissionPolicyState")).toContain("51205");
  evidence.database = { historyUpdate: "refused 51204", historyDelete: "refused 51204", versionJump: "refused 51205", stateDelete: "refused 51205" };
});

test("ACCESSIBILITY: the permission matrix screen passes an axe scan in both themes", async () => {
  await admin.goto("/admin/permissions");
  await expect(matrixHeading(admin)).toBeVisible();
  await scanBothThemes(admin, "permission-matrix");
  await shot(admin, "04-matrix-admin.png");
});
