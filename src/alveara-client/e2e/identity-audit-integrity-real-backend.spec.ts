import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N025 (brief section 8, "Browser"), un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, run with
// playwright.identity-audit.config.ts against an API the caller started (scripts/Invoke-RealBackendWalkthrough.ps1). It walks what ALV-N025 changed as staff would:
// registering anyway with a reason, the near-duplicate with another birth date flagged, the MRN shown and found by search, the patient-open audit row written once per
// session, sign-out recorded, the History link followed by an Admin and refused to a ViewAuditLog-only account by direct URL, and the manager's read-only matrix.
// (The "no expiry after sign-out" half of BR-01 is proven server-side in AuditSessionLifecycleTests: the shortest session timeout is 5 minutes.)

test.describe.configure({ mode: "serial" });

const OUT = process.env.IDENTITY_AUDIT_E2E_OUT ?? path.join(process.cwd(), "identity-audit-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const stamp = Date.now();
const contexts: BrowserContext[] = [];
let admin: Page;
let desk: Page;
const users: Record<string, { id: string; username: string }> = {};
const ids: Record<string, string> = {};
const PASSWORD = "identity-audit-pass-1!";

async function csrf(page: Page) {
  return (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

async function newPage(browser: Browser) {
  const context = await browser.newContext();
  contexts.push(context);
  return context.newPage();
}

async function createUser(key: string, role: string) {
  const username = `${key}-${stamp}`;
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.ok(), `register ${key}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  users[key] = { id, username };
}

/** Changes one role's permission through the real policy API (the path an administrator uses), with the typed confirmation sensitive permissions need. */
async function setPolicy(role: string, permission: string, effect: "Grant" | "Revoke") {
  const version = (await (await admin.request.get("/api/permissions/effective")).json()).version as number;
  const r = await admin.request.put("/api/permissions/overrides", {
    headers: { "X-CSRF-Token": await csrf(admin), "Idempotency-Key": `n025-${role}-${permission}-${stamp}` },
    data: { role, permission, effect, reason: "ALV-N025 walkthrough: an audit reader without patient access", expectedVersion: version, confirmed: true, typedConfirmation: permission },
  });
  expect(r.ok(), `${effect} ${permission} for ${role}: ${r.status()} ${await r.text()}`).toBeTruthy();
}

async function auditRows(eventType: string) {
  const r = await admin.request.get(`/api/auth/audit-log?take=500&eventType=${eventType}`);
  expect(r.ok()).toBeTruthy();
  return (await r.json()) as { targetUserAccountId: string; details: string }[];
}

async function fillPatient(page: Page, p: { first: string; last: string; dob: string; phone: string }) {
  await page.getByLabel("First name *").fill(p.first);
  await page.getByLabel("Last name *").fill(p.last);
  await page.getByLabel("Date of birth *").fill(p.dob);
  await page.getByLabel("Phone *").fill(p.phone);
  await page.getByLabel("Address *").fill("12 Elm Street");
  await page.getByLabel("City *").fill("Austin");
  await page.getByLabel("State *").selectOption("TX");
  await page.getByLabel("Postal code *").fill("78701");
}

test.beforeAll(async ({ browser }) => {
  test.setTimeout(120_000);   // a fresh dev server compiles on first load, and this setup also changes the permission policy and creates four users
  admin = await newPage(browser);
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: PASSWORD, secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, PASSWORD);
  await createUser("desk", "FrontDesk");
  await createUser("leaver", "FrontDesk");
  await createUser("auditor", "Billing");
  await createUser("manager", "OfficeManager");
  await setPolicy("Billing", "ViewAuditLog", "Grant");                                                // BR-02: ViewAuditLog without ViewPatientRecords
  await setPolicy("Billing", "ViewPatientRecords", "Revoke");
  desk = await newPage(browser);
  await signIn(desk, users.desk.username, PASSWORD);
  evidence.users = Object.fromEntries(Object.entries(users).map(([k, v]) => [k, v.username]));
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "identity-audit-e2e.json"), JSON.stringify(evidence, null, 2));
  for (const c of contexts) await c.close();
});

test("REGISTRATION: a patient gets an 11-digit MRN; the same name with another birth date and the same phone is flagged; registering anyway needs a reason", async () => {
  await desk.goto("/patients/register");
  await fillPatient(desk, { first: "Carla", last: `Chen${stamp}`, dob: "1992-04-02", phone: "555-321-0001" });
  await desk.getByRole("button", { name: "Register patient" }).click();
  const done = desk.getByRole("status", { name: "Registration complete" });
  await expect(done).toBeVisible();
  const mrn = (await done.getByText(/^Patient number \(MRN\):/).innerText()).replace(/\D/g, "");
  expect(mrn).toMatch(/^\d{11}$/);
  ids.carla = (await done.getByText(/^Patient ID:/).innerText()).replace("Patient ID:", "").trim();
  ids.mrn = mrn;

  await desk.getByRole("button", { name: "Register another patient" }).click();
  await fillPatient(desk, { first: "Carla", last: `Chen${stamp}`, dob: "1997-04-02", phone: "555-321-0001" });
  await desk.getByRole("button", { name: "Register patient" }).click();
  const panel = desk.getByRole("region", { name: "These patients may be the same person" });
  await expect(panel).toContainText("Same name and phone number, different birth date");
  const anyway = panel.getByRole("button", { name: /Register anyway/ });
  await expect(anyway).toBeDisabled();                                                                // not without a reason
  await shot(desk, "01-near-duplicate-flagged.png");
  await panel.getByLabel("Why is this a different person? (required)").fill("Mother and daughter, checked ID");
  await anyway.click();
  await expect(desk.getByRole("status", { name: "Registration complete" })).toBeVisible();
  const overrides = (await auditRows("PatientRegistered")).filter((r) => r.details.includes(ids.carla));
  expect(overrides).toHaveLength(1);                                                                  // the audit names the acknowledged id ...
  expect(overrides[0].details).not.toContain("Chen");                                                 // ... never the name
  evidence.registration = { mrn, overrideAudited: true };
});

test("MRN: shown in the header and found first by an exact search", async () => {
  await desk.goto("/patients");
  await desk.getByLabel("Search patients").fill(ids.mrn);
  const table = desk.getByRole("table", { name: /Patients, in name order/ });
  await expect(table.getByRole("row").nth(1)).toContainText(ids.mrn);
  await table.getByRole("row").nth(1).getByRole("link").click();
  await expect(desk.getByRole("region", { name: "Patient context" })).toContainText(`MRN ${ids.mrn}`);
  await shot(desk, "02-mrn-header.png");
});

test("ACCESS TRAIL: opening a record writes one audit row per session, not one per page view", async () => {
  const before = (await auditRows("PatientRecordOpened")).filter((r) => r.targetUserAccountId === ids.carla).length;
  await desk.goto(`/patients/${ids.carla}`);
  await expect(desk.getByRole("region", { name: "Patient context" })).toContainText("Carla");
  await desk.goto(`/patients/${ids.carla}/history`);
  await desk.goto(`/patients/${ids.carla}`);
  await expect(desk.getByRole("region", { name: "Patient context" })).toContainText("Carla");
  const after = (await auditRows("PatientRecordOpened")).filter((r) => r.targetUserAccountId === ids.carla).length;
  expect(after).toBe(Math.max(before, 1));                                                            // this session opened it once, however often it looked
  evidence.recordOpenedRowsForThisSession = after;
});

test("SIGN-OUT: signing out is recorded once for that account", async ({ browser }) => {
  const leaver = await newPage(browser);
  await signIn(leaver, users.leaver.username, PASSWORD);
  await leaver.getByRole("button", { name: "Sign out" }).click();
  await expect(leaver).toHaveURL(/\/login/);
  await expect.poll(async () => (await auditRows("UserSignedOut")).filter((r) => r.targetUserAccountId === users.leaver.id).length).toBe(1);
  evidence.signOutRecorded = true;
});

test("HISTORY LINK (BR-02): an Admin follows it; a ViewAuditLog-only account sees no link and is refused the History page by direct URL", async ({ browser }) => {
  await desk.goto(`/patients/${ids.carla}`);
  await desk.getByLabel("City *").fill("Round Rock");
  await desk.getByRole("button", { name: "Save changes" }).click();
  await expect(desk.getByRole("status").filter({ hasText: /saved/i }).first()).toBeVisible();

  await admin.goto("/admin/audit-log");
  await admin.getByLabel("Event").selectOption("PatientUpdated");
  await admin.getByRole("button", { name: "Show" }).click();
  await admin.getByRole("link", { name: "View in patient history" }).first().click();
  await expect(admin.getByRole("heading", { name: "History" })).toBeVisible();
  await expect(admin.getByRole("table")).toContainText("Round Rock");
  await shot(admin, "03-history-from-audit.png");

  const auditor = await newPage(browser);
  await signIn(auditor, users.auditor.username, PASSWORD);
  await auditor.goto("/admin/audit-log");
  await auditor.getByLabel("Event").selectOption("PatientUpdated");
  await auditor.getByRole("button", { name: "Show" }).click();
  await expect(auditor.getByText("History is available to staff who may see this patient.").first()).toBeVisible();
  await expect(auditor.getByRole("link", { name: "View in patient history" })).toHaveCount(0);
  await expect(auditor.locator("table")).not.toContainText("Round Rock");                              // no values in the audit rows
  await auditor.goto(`/patients/${ids.carla}/history`);
  await expect(auditor.getByText("You don't have permission to view this")).toBeVisible();
  expect((await auditor.request.get(`/api/patients/${ids.carla}/history`)).status()).toBe(403);
  await shot(auditor, "04-history-refused.png");
  evidence.historyBoundary = { adminFollowed: true, auditorRefused: true };
});

test("MATRIX (Q-5cl): a manager reads the permission matrix and is offered no change", async ({ browser }) => {
  const manager = await newPage(browser);
  await signIn(manager, users.manager.username, PASSWORD);
  await manager.goto("/admin/permissions");
  await expect(manager.getByRole("heading", { name: "Permission matrix" })).toBeVisible();
  await expect(manager.getByRole("button", { name: /Change/ })).toHaveCount(0);
  await shot(manager, "05-manager-matrix.png");
  evidence.managerMatrixReadOnly = true;
});
