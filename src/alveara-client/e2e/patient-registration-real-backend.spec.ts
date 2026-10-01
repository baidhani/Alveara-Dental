import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-003 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Like auth-real-backend.spec.ts it is excluded from the default mocked run (playwright.config.ts) and run with
// playwright.patients.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// Walks the three acceptance criteria as a front-desk user would, then checks the audit trail as an admin, and
// scans the form (default, error and success states) in both themes with axe.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PATIENT_E2E_OUT ?? path.join(process.cwd(), "patient-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};

let adminContext: BrowserContext;
let admin: Page;
let deskContext: BrowserContext;
let desk: Page;
let deskUsername: string;
let patientId = "";

async function csrf(page: Page) {
  return (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
}

async function createUser(role: string, username: string) {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: "front-desk-pass-1!" } });
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
  await page.waitForTimeout(300); // let finite colour transitions finish so axe samples settled colours
}

async function scan(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
  const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
  (evidence.axe as Record<string, unknown>)[label] = { blocking: blocking.length, violations: results.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length })), passes: results.passes.length };
  expect(blocking.map((v) => `${v.id}:${v.nodes.length}`), `${label}: critical/serious axe violations`).toEqual([]);
}

async function fillValid(page: Page, first: string, last: string) {
  await page.getByLabel("First name *").fill(first);
  await page.getByLabel("Last name *").fill(last);
  await page.getByLabel("Date of birth *").fill("1985-03-09");
  await page.getByLabel("Phone *").fill("555-010-0100");
  await page.getByLabel("Email").fill("demo.patient@example.test");
  await page.getByLabel("Address *").fill("1 Main St");
  await page.getByLabel("City *").fill("Austin");
  await page.getByLabel("State *").fill("TX");
  await page.getByLabel("Postal code *").fill("78701");
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  evidence.axe = {};
  const adminName = `admin-${Date.now()}`;
  deskUsername = `desk-${Date.now()}`;
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: adminName, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, adminName, "admin-password-1!");
  await createUser("FrontDesk", deskUsername);
  await createUser("Dentist", `dentist-${Date.now()}`);
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, deskUsername, "front-desk-pass-1!");
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "patient-registration-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  await deskContext.close();
});

test("front desk reaches Register Patient from the nav; the form marks required fields and scans clean in both themes", async () => {
  await desk.getByRole("link", { name: "Register Patient" }).click();
  await expect(desk.getByRole("heading", { name: "Register patient" })).toBeVisible();
  for (const theme of ["light", "dark"] as const) {
    await setTheme(desk, theme);
    await scan(desk, `form-default-${theme}`);
  }
  await setTheme(desk, "light");
  await desk.screenshot({ path: path.join(OUT, "01-empty-form.png"), fullPage: true });
});

test("ACCEPTANCE 2: an incomplete registration prompts for each required field and stores nothing", async () => {
  await desk.getByLabel("Last name *").fill("Partial");
  await desk.getByRole("button", { name: "Register patient" }).click();

  for (const label of ["First name", "Date of birth", "Phone", "Address", "City", "State", "Postal code"]) {
    await expect(desk.getByText(`${label} is required.`)).toBeVisible();
  }
  await expect(desk.getByLabel("First name *")).toBeFocused();
  evidence.incompleteRegistrationPrompts = "all seven missing required fields named; focus on the first";
  for (const theme of ["light", "dark"] as const) {
    await setTheme(desk, theme);
    await scan(desk, `form-errors-${theme}`);
  }
  await setTheme(desk, "light");
  await desk.screenshot({ path: path.join(OUT, "02-required-field-prompts.png"), fullPage: true });
});

test("ACCEPTANCE 1: a valid registration captures demographics and contact details (read back from the real API)", async () => {
  await desk.reload();
  await fillValid(desk, "Demo", "Patient");
  await desk.getByRole("button", { name: "Register patient" }).click();

  const done = desk.getByRole("status", { name: "Registration complete" });
  await expect(done).toContainText("Demo Patient, born 1985-03-09");
  const idText = await done.getByText(/^Patient ID:/).innerText();
  patientId = idText.replace("Patient ID:", "").trim();
  expect(patientId).toMatch(/^[0-9a-f-]{36}$/);

  const stored = await (await desk.request.get(`/api/patients/${patientId}`)).json();
  expect(stored).toMatchObject({ firstName: "Demo", lastName: "Patient", dateOfBirth: "1985-03-09", phone: "555-010-0100", email: "demo.patient@example.test", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" });
  evidence.storedPatient = { id: patientId, fieldsCaptured: Object.keys(stored) };
  for (const theme of ["light", "dark"] as const) {
    await setTheme(desk, theme);
    await scan(desk, `registered-${theme}`);
  }
  await setTheme(desk, "light");
  await desk.screenshot({ path: path.join(OUT, "03-registered.png"), fullPage: true });
});

test("FAILURE PATH: registering the same person again is refused and names the existing record", async () => {
  await desk.getByRole("button", { name: "Register another patient" }).click();
  await fillValid(desk, "  demo ", "PATIENT");
  await desk.getByRole("button", { name: "Register patient" }).click();

  const banner = desk.getByRole("alert");
  await expect(banner).toContainText("already registered");
  await expect(banner).toContainText(`Existing patient ID: ${patientId}`);
  await expect(desk.getByLabel("First name *")).toHaveValue("  demo "); // typed values are kept
  await desk.screenshot({ path: path.join(OUT, "04-duplicate-refused.png"), fullPage: true });
});

test("keyboard-only: the whole form can be completed and submitted without a mouse", async () => {
  await desk.reload();
  await desk.getByLabel("First name *").focus();
  // One field = type then Tab. Chromium's date input has an extra tab stop (its calendar button) after the year segment, hence the doubled blank after the date.
  const keys: [string, string][] = [["", "Kay"], ["", ""], ["", "Board"], ["", "12311990"], ["", ""], ["", ""], ["", "555-020-0200"], ["", ""], ["", "2 Elm St"], ["", ""], ["", "Dallas"], ["", "TX"], ["", "75001"]];
  for (const [, text] of keys) {
    if (text) await desk.keyboard.type(text);
    await desk.keyboard.press("Tab");
  }
  await desk.keyboard.press("Enter"); // submit from the keyboard
  await expect(desk.getByRole("status", { name: "Registration complete" })).toContainText("Kay Board");
  evidence.keyboardOnlyRegistration = "passed";
});

test("ACCEPTANCE 3: the audit trail records each registration with the user and a timestamp (and no patient details)", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=50")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string; entityType: string | null; targetUserAccountId: string }[];
  const registrations = entries.filter((e) => e.eventType === "PatientRegistered");
  expect(registrations).toHaveLength(2); // Demo Patient and Kay Board; the refused duplicate and the incomplete attempt left none
  const demo = registrations.find((e) => e.targetUserAccountId === patientId)!;
  expect(demo.performedByUserAccountId).toBeTruthy();
  expect(Date.now() - Date.parse(demo.timestampUtc)).toBeLessThan(10 * 60_000);
  expect(demo.entityType).toBe("Patient");
  for (const e of registrations) expect(e.details).not.toMatch(/Demo|Kay|Board|demo.patient|555-/);
  evidence.auditEntries = registrations.map((e) => ({ eventType: e.eventType, performedBy: e.performedByUserAccountId, timestampUtc: e.timestampUtc, details: e.details }));

  await admin.goto("/admin/audit-log");
  await expect(admin.getByText("PatientRegistered").first()).toBeVisible();
  await admin.screenshot({ path: path.join(OUT, "05-audit-log.png"), fullPage: true });
});

test("a dentist (no RegisterPatients) has no nav link and is denied the page and the API", async ({ browser }) => {
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  const dentistName = `dentist2-${Date.now()}`;
  await createUser("Dentist", dentistName);
  await signIn(page, dentistName, "front-desk-pass-1!");
  await expect(page.getByRole("link", { name: "Register Patient" })).toHaveCount(0);
  await page.goto("/patients/register");
  await expect(page.getByText(/don't have permission/i)).toBeVisible();
  const token = await csrf(page);
  const denied = await page.request.post("/api/patients", { headers: { "X-CSRF-Token": token, "Idempotency-Key": "k-denied" }, data: { firstName: "No", lastName: "Access" } });
  expect(denied.status()).toBe(403);
  evidence.dentistDenied = { navLink: false, page: "denied", apiStatus: denied.status() };
  await ctx.close();
});
