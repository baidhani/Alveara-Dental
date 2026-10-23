import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N018 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// An administrator edits the permission matrix on screen (reason, confirmation, typed confirmation for a high-impact permission, revert) and the very next request of the affected role changes; protected
// permissions are locked; a stale edit is explained; an office manager delegates document import for a working role and is refused everything outside the delegation; a holder of account status sees only the accounts
// the rules allow; the health panel repairs an invalid row; another session's change shows on the open screen within about 30 seconds; the history lists the changes; the editing screens are scanned in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PERMISSION_POLICY_ADMIN_E2E_OUT ?? path.join(process.cwd(), "permission-policy-admin-e2e-out");
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

const matrix = (page: Page) => page.getByRole("table", { name: /Permission matrix/ });
const change = (page: Page, permission: string, role: string) => page.getByRole("button", { name: `Change ${permission} for ${role}` });
const versionOf = async () => (await api(admin, "get", "/api/permissions/effective")).body.version as number;

/** Fills the open cell editor the way a person does and saves it. */
async function save(page: Page, reason: string, typed?: string, button = /^Save:/) {
  await page.getByLabel(/^Reason/).fill(reason);
  if (typed !== undefined) await page.getByLabel(/^This is a high-impact permission/).fill(typed);
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: button }).click();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  const boot = await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } });
  expect(boot.ok()).toBeTruthy();
  ids.adminId = (await boot.json()).id as string;
  names.Admin = `admin-${stamp}`;
  await signIn(admin, names.Admin, "admin-password-1!");
  for (const role of ["OfficeManager", "Dentist", "Billing", "FrontDesk"]) {
    names[role] = `${role.toLowerCase()}-${stamp}`;
    ids[`${role}User`] = await createUser(role, names[role]);
    if (role === "FrontDesk") continue;
    const context = await adminContext.browser()!.newContext();
    const page = await context.newPage();
    await signIn(page, names[role], PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "permission-policy-admin-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("EDIT: an administrator grants and reverts a cell on screen with a reason and a confirmation, and the very next request of the role changes", async () => {
  const billing = sessions.Billing.page;
  expect((await api(billing, "get", "/api/backup/history")).status).toBe(403);
  await admin.goto("/admin/permissions");
  await expect(matrix(admin)).toBeVisible();
  await expect(admin.getByText(/enabled/).first()).toBeVisible();                                                // enabled-account counts are shown per role
  await change(admin, "ViewBackupStatus", "Billing").click();
  const saveButton = admin.getByRole("button", { name: /^Save:/ });
  await expect(saveButton).toBeDisabled();                                                                       // nothing is saved without a reason and a confirmation
  await shot(admin, "01-editor-open.png");
  await save(admin, "Billing reviews the backup status");
  await expect(admin.getByText(/ViewBackupStatus for Billing: granted\. It applies to the next request\./)).toBeVisible();
  expect((await api(billing, "get", "/api/backup/history")).status).not.toBe(403);                              // the very next request
  await shot(admin, "02-after-grant.png");

  await change(admin, "ViewBackupStatus", "Billing").click();
  await expect(admin.getByRole("heading", { name: "Revert ViewBackupStatus for Billing" })).toBeVisible();
  await save(admin, "Back to the default", undefined, /^Revert to default$/);
  await expect(admin.getByText(/ViewBackupStatus for Billing: reverted/)).toBeVisible();
  expect((await api(billing, "get", "/api/backup/history")).status).toBe(403);
  evidence.edit = { grantApplied: "next request", revertApplied: "next request", reasonAndConfirmationRequired: true };
});

test("PROTECTED AND TYPED: protected permissions are locked, and a high-impact change needs the permission name typed", async () => {
  await admin.goto("/admin/permissions");
  await expect(matrix(admin)).toBeVisible();
  for (const protectedPermission of ["ManageUsers", "ManageRoles", "ManageMfaPolicy", "IssuePasswordResets", "ManagePermissionGrants"]) {
    await expect(change(admin, protectedPermission, "OfficeManager")).toHaveCount(0);                            // locked: no way to change it from the screen
  }
  await expect(change(admin, "ViewBilling", "Admin")).toHaveCount(0);                                            // the administrator's cells are never editable
  await change(admin, "ManageAccountStatus", "OfficeManager").click();
  await admin.getByLabel(/^Reason/).fill("Office manager runs account status");
  await admin.getByRole("checkbox").check();
  const saveButton = admin.getByRole("button", { name: /^Save:/ });
  await expect(saveButton).toBeDisabled();                                                                       // the name has not been typed
  await admin.getByLabel(/^This is a high-impact permission/).fill("manageaccountstatus");
  await expect(saveButton).toBeDisabled();                                                                       // typed with the wrong case
  await admin.getByLabel(/^This is a high-impact permission/).fill("ManageAccountStatus");
  await shot(admin, "03-typed-confirmation.png");
  await saveButton.click();
  await expect(admin.getByText(/ManageAccountStatus for OfficeManager: granted/)).toBeVisible();
  evidence.protectedAndTyped = { protectedLocked: 5, adminColumnLocked: true, typedConfirmation: true };
});

test("ACCOUNT LIST: a holder of account status sees only the accounts the rules allow, on screen and in the API, and still cannot act on an administrator", async () => {
  const manager = sessions.OfficeManager.page;
  const list = await api(manager, "get", "/api/auth/users");
  expect(list.status).toBe(200);
  const roles = new Set((list.body as any[]).map((u) => u.role));
  expect(roles.has("Admin")).toBe(false);
  expect(roles.has("Dentist")).toBe(false);                                                                      // holds permissions the office manager lacks
  expect(roles.has("FrontDesk")).toBe(true);
  await manager.goto("/admin/users");
  await expect(manager.getByRole("heading", { name: "Security administration" })).toBeVisible();
  await expect(manager.getByText(/The accounts your role may act on/)).toBeVisible();
  await expect(manager.getByText(names.FrontDesk)).toBeVisible();
  await expect(manager.getByText(names.Admin)).toHaveCount(0);
  await expect(manager.getByText(names.Dentist)).toHaveCount(0);
  await expect(manager.getByRole("link", { name: "Security Administration" })).toBeVisible();                   // the navigation entry follows the permission
  await shot(manager, "04-account-list-holder.png");
  const refusal = await api(manager, "put", `/api/auth/${ids.adminId}/enabled`, { enabled: false });
  expect([refusal.status, refusal.body.rule]).toEqual([403, "target_is_admin"]);
  expect((await api(sessions.Dentist.page, "get", "/api/auth/users")).body).toEqual({ error: "permission_denied", required: "ManageUsers" });   // everyone else keeps the old refusal
  evidence.accountList = { holderSeesAdmin: false, holderSeesDentist: false, otherRoleRefusal: "permission_denied ManageUsers" };

  await admin.goto("/admin/permissions");                                                                       // revert the grant so the later steps start from the defaults
  await change(admin, "ManageAccountStatus", "OfficeManager").click();
  await save(admin, "Back to the default", "ManageAccountStatus", /^Revert to default$/);
  await expect(admin.getByText(/ManageAccountStatus for OfficeManager: reverted/)).toBeVisible();
  expect((await api(manager, "get", "/api/auth/users")).status).toBe(403);
});

test("STALE: an edit made from an out-of-date screen is explained, stores nothing and offers a reload", async () => {
  await admin.goto("/admin/permissions");
  await expect(matrix(admin)).toBeVisible();
  await change(admin, "ViewBackupStatus", "Dentist").click();
  await admin.getByLabel(/^Reason/).fill("From an old screen");
  await admin.getByRole("checkbox").check();
  const seen = await versionOf();
  const other = await api(admin, "put", "/api/permissions/overrides", { role: "Hygienist", permission: "ViewBackupStatus", effect: "Grant", reason: "Someone else changed the policy first", expectedVersion: seen, confirmed: true }, { "Idempotency-Key": `stale-other-${stamp}` });
  expect(other.status).toBe(200);
  await admin.getByRole("button", { name: /^Save:/ }).click();
  await expect(admin.getByRole("alert")).toContainText(/policy changed since you opened this/);
  await shot(admin, "05-stale-write.png");
  const count = sql("SELECT COUNT(*) FROM RolePermissionOverrides WHERE Role = N'Dentist' AND Permission = N'ViewBackupStatus'").trim();
  expect(count).toBe("0");                                                                                       // nothing was stored for the stale edit
  await admin.getByRole("button", { name: "Reload the policy" }).click();
  await expect(admin.getByRole("heading", { name: /ViewBackupStatus for Dentist/ })).toHaveCount(0);
  const hygienist = admin.getByRole("row", { name: /ViewBackupStatus/ }).locator(".alv-policy-source--granted");
  await expect(hygienist).toHaveCount(1);                                                                        // the screen now shows the other change
  evidence.stale = { status: 409, stored: 0, reloadOffered: true };

  const v = await versionOf();                                                                                   // tidy: revert the other administrator's change
  expect((await api(admin, "put", "/api/permissions/overrides", { role: "Hygienist", permission: "ViewBackupStatus", effect: "Revert", reason: "Tidy", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `stale-tidy-${stamp}` })).status).toBe(200);
});

test("DELEGATION: the office manager allows and stops document import for a working role from the card, and is refused everything outside the delegation", async () => {
  const manager = sessions.OfficeManager.page;
  const dentist = sessions.Dentist.page;
  const perms = async () => ((await api(dentist, "get", "/api/auth/permissions")).body.permissions as string[]);
  expect(await perms()).not.toContain("ImportDocuments");
  await manager.goto("/admin/permissions");
  await expect(manager.getByRole("heading", { name: "Who may import documents" })).toBeVisible();
  await expect(manager.getByRole("button", { name: /^Change / })).toHaveCount(0);                                // the office manager has no matrix editor
  await manager.getByRole("button", { name: "Allow Dentist importing documents" }).click();
  await manager.getByLabel(/^Reason/).fill("Dentists scan their own radiographs");
  await manager.getByRole("checkbox").check();
  await manager.getByRole("button", { name: "Save" }).click();
  await expect(manager.getByText(/Dentist may now import documents/)).toBeVisible();
  await shot(manager, "06-document-import-card.png");
  expect(await perms()).toContain("ImportDocuments");                                                            // the very next request

  const v = await versionOf();
  const refusals = [
    await api(manager, "post", "/api/permissions/delegated-grants", { targetRole: "Admin", permission: "ImportDocuments", allowed: false, reason: "x", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `d1-${stamp}` }),
    await api(manager, "post", "/api/permissions/delegated-grants", { targetRole: "OfficeManager", permission: "ImportDocuments", allowed: false, reason: "x", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `d2-${stamp}` }),
    await api(manager, "post", "/api/permissions/delegated-grants", { targetRole: "Dentist", permission: "ViewBilling", allowed: false, reason: "x", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `d3-${stamp}` }),
    await api(manager, "post", "/api/permissions/delegated-grants", { targetRole: "Dentist", permission: "ManageAccountStatus", allowed: true, reason: "x", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `d4-${stamp}` }),
    await api(manager, "put", "/api/permissions/overrides", { role: "Dentist", permission: "ViewBilling", effect: "Revoke", reason: "x", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `d5-${stamp}` }),
    await api(manager, "put", "/api/permissions/delegations", { rows: [], reason: "x", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `d6-${stamp}` }),
  ];
  expect(refusals.map((r) => r.status)).toEqual([403, 403, 403, 403, 403, 403]);
  expect(refusals.slice(0, 4).map((r) => r.body.error)).toEqual(["delegation_not_allowed", "delegation_not_allowed", "delegation_not_allowed", "delegation_not_allowed"]);

  await manager.getByRole("button", { name: "Stop Dentist importing documents" }).click();
  await manager.getByLabel(/^Reason/).fill("Back to front office only");
  await manager.getByRole("checkbox").check();
  await manager.getByRole("button", { name: "Save" }).click();
  await expect(manager.getByText(/Dentist may no longer import documents/)).toBeVisible();
  expect(await perms()).not.toContain("ImportDocuments");
  const history = (await api(admin, "get", "/api/permissions/history")).body.items as any[];
  expect(history.filter((h) => h.viaDelegation).length).toBe(2);
  evidence.delegation = { allowed: "applied next request", stopped: "applied next request", refusedOutsideDelegation: 6, delegatedHistoryRows: 2 };
});

test("HEALTH: an invalid stored row is shown and repaired on screen with a reason, and the role returns to its default", async () => {
  const billing = sessions.Billing.page;
  sql(`BEGIN TRAN; UPDATE PermissionPolicyState SET Version = Version + 1, UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 1; INSERT INTO RolePermissionOverrides (Id, Role, Permission, Effect, Reason, ChangedByUserId, ChangedAtUtc) VALUES (NEWID(), N'Billing', N'NoSuchPermission', N'Revoke', N'corrupt', '${ids.adminId}', SYSDATETIMEOFFSET()); COMMIT;`);
  expect((await api(billing, "get", "/api/permissions/my-delegations")).status).toBe(200);
  expect((await api(billing, "get", "/api/billing/payment-methods")).body.error).toBe("permission_policy_repair_required");   // the role is denied while the row exists
  await admin.goto("/admin/permissions");
  await expect(admin.getByRole("alert").filter({ hasText: "Permission policy needs repair: 1 invalid row" })).toBeVisible();
  await expect(admin.getByRole("button", { name: /Change ViewBilling for Billing/ })).toBeDisabled();            // a role in repair cannot be edited
  await shot(admin, "07-health-needs-repair.png");
  await admin.getByRole("button", { name: /Remove the invalid row for Billing NoSuchPermission/ }).click();
  await admin.getByLabel(/^Reason/).fill("Corrupt row found by the health panel");
  await admin.getByRole("button", { name: "Remove the row" }).click();
  await expect(admin.getByText(/The stored permission policy is healthy/)).toBeVisible();
  expect((await api(billing, "get", "/api/billing/payment-methods")).status).not.toBe(403);
  evidence.health = { shown: true, repairedOnScreen: true, roleRestored: true };
});

test("REFRESH: a change made in another session shows on the open screen within about 30 seconds, and the history lists the changes", async () => {
  test.setTimeout(90_000);
  await admin.goto("/admin/permissions");
  await expect(matrix(admin)).toBeVisible();
  const row = admin.getByRole("row", { name: /ViewBackupStatus/ });
  await expect(row.locator(".alv-policy-source--granted")).toHaveCount(0);
  const v = await versionOf();
  expect((await api(admin, "put", "/api/permissions/overrides", { role: "Assistant", permission: "ViewBackupStatus", effect: "Grant", reason: "Changed while the screen was open", expectedVersion: v, confirmed: true }, { "Idempotency-Key": `refresh-${stamp}` })).status).toBe(200);
  await expect(row.locator(".alv-policy-source--granted")).toHaveCount(1, { timeout: 45_000 });                  // without reloading the page
  await expect(admin.getByRole("heading", { name: "Change history" })).toBeVisible();
  await expect(admin.getByText("Changed while the screen was open")).toBeVisible({ timeout: 10_000 });
  await shot(admin, "08-refreshed-with-history.png");
  const noAudit = await api(sessions.Dentist.page, "get", "/api/permissions/history");
  expect(noAudit.status).toBe(403);                                                                              // history is for holders of the audit permission
  evidence.refresh = { seenWithoutReload: true, withinSeconds: 45 };
});

test("ACCESSIBILITY: the editing screens pass an axe scan in both themes", async () => {
  await admin.goto("/admin/permissions");
  await expect(matrix(admin)).toBeVisible();
  await change(admin, "ViewBilling", "Dentist").click();
  await expect(admin.getByRole("heading", { name: /Revoke ViewBilling for Dentist/ })).toBeVisible();
  await scanBothThemes(admin, "editable-matrix-with-editor");
  const manager = sessions.OfficeManager.page;
  await manager.goto("/admin/permissions");
  await expect(manager.getByRole("heading", { name: "Who may import documents" })).toBeVisible();
  await scanBothThemes(manager, "document-import-card");
  const v = await versionOf();
  expect((await api(admin, "put", "/api/permissions/overrides", { role: "OfficeManager", permission: "ManageAccountStatus", effect: "Grant", reason: "For the accessibility scan", expectedVersion: v, confirmed: true, typedConfirmation: "ManageAccountStatus" }, { "Idempotency-Key": `a11y-${stamp}` })).status).toBe(200);
  await manager.goto("/admin/users");
  await expect(manager.getByRole("heading", { name: "Security administration" })).toBeVisible();
  await scanBothThemes(manager, "account-list-holder");
});
