import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-016 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.prescriptions.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// A dentist prescribes for a patient whose allergies are recorded: a correct entry is saved and logged with who and when; an incorrect dose is refused beside its box with what was typed kept and the message
// saying the ceiling is not a clinical limit; a medication that matches a recorded allergy is refused until the dentist gives their own words, and is then saved with them; a patient whose allergies were never
// reviewed gets the same treatment, never "clear"; when the log is unavailable nothing is saved and the same click works once it is back; a replay changes nothing; a hygienist reads and cannot prescribe.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PRESCRIPTIONS_E2E_OUT ?? path.join(process.cwd(), "prescriptions-e2e-out");
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
const DENTIST_NAME = "Dr. Dana Dentist";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
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
const form = (page: Page) => page.getByRole("form", { name: "Prescribe a medication" });
const safetyPanel = (page: Page) => page.getByRole("complementary", { name: "Patient safety" });
const list = (page: Page) => page.getByRole("list", { name: "Prescriptions" });
const prescriptionsOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${patient}/prescriptions`)).body.prescriptions as any[];
const recordedAudit = async () => ((await api(admin, "get", "/api/auth/audit-log?take=500")).body as any[]).filter((e) => e.eventType === "PrescriptionRecorded");

async function gotoTab(page: Page, patient = ids.ann) {
  await page.goto(`/patients/${patient}/prescriptions`);
  await expect(page.getByRole("heading", { name: "Prescriptions", exact: true })).toBeVisible();
}

async function enter(page: Page, e: { medication: string; dose: string; unit: string; frequency: string }, blur = true) {
  const f = form(page);
  await f.getByLabel("Medication").fill(e.medication);
  if (blur) await f.getByLabel("Medication").press("Tab");
  await f.getByLabel("Dose").fill(e.dose);
  await f.getByLabel("Unit").selectOption(e.unit);
  await f.getByLabel("How often").selectOption(e.frequency);
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
    if (role === "Dentist") {
      ids.dentistUser = userId;
      expect((await api(admin, "post", "/api/config/staff", { displayName: `${DENTIST_NAME} ${stamp}`, userAccountId: userId })).status, "staff profile for the dentist").toBe(201);
    }
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "prescriptions-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: Ann has her allergies reviewed (penicillin and latex); Bo's allergies were never reviewed", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const patient = async (firstName: string, lastName: string, key: string, dob: string, phone: string, address: string) => {
    const r = await api(front, "post", "/api/patients", { firstName, lastName, dateOfBirth: dob, sex: "Female", phone, addressLine1: address, city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `${key}-${stamp}` });
    expect(r.status).toBe(201);
    return r.body.id as string;
  };
  ids.ann = await patient("Ann", "Alder", "rx-ann", "1985-03-09", "555-010-0100", "1 Main St");
  ids.bo = await patient("Bo", "Brandt", "rx-bo", "1972-11-21", "555-010-0222", "9 Oak Ave");
  for (const [name, reaction, severity] of [["Penicillin", "Hives", "Severe"], ["Latex", "Rash", "Mild"]]) {
    expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Allergy", name, reaction, severity })).status, name).toBe(200);
  }
  expect((await api(dentist, "put", `/api/patients/${ids.ann}/clinical-record/sections/Allergy/review`, { state: "Reviewed" })).status).toBe(200);
});

test("THE SCREEN: a prescriber sees the form, what is recorded for the patient, and what is NOT checked; the list starts empty and says what that means", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await expect(form(page)).toBeVisible();
  const panel = safetyPanel(page);
  await expect(panel).toContainText("Penicillin (high) Hives");
  await expect(panel).toContainText("Latex (low) Rash");
  await expect(panel).toContainText("Not checked: drug interactions, and whether a dose is clinically appropriate");
  await expect(panel).toContainText("does not know drug families");
  await expect(panel).toContainText("not checked against any clinical dose range");
  await expect(page.getByText(/means none has been recorded here, not that none was ever given/)).toBeVisible();
  await shot(page, "01-prescribe-screen.png");
  await scanBothThemes(page, "prescribe-screen");
});

test("CRITERION 1 and TRUST: a correct entry is checked for allergies, saved, listed with who prescribed it and logged with the user and the time", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const before = (await recordedAudit()).length;
  await enter(page, { medication: "Ibuprofen", dose: "400", unit: "mg", frequency: "Three times daily" });
  await expect(safetyPanel(page)).toContainText("No allergy recorded for this patient matches this medication's name. This is a name comparison only, not a clearance.");
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(statusLine(page)).toContainText("Ibuprofen prescribed and logged.");
  await expect(list(page)).toContainText("Ibuprofen");
  await expect(list(page)).toContainText(`Prescribed by ${DENTIST_NAME} ${stamp}`);
  await expect(list(page)).toContainText("No recorded allergy matched this medication's name. That is a name comparison, not a clearance.");
  await shot(page, "02-saved-and-listed.png");

  const rows = await prescriptionsOf(page, ids.ann);
  expect(rows).toHaveLength(1);
  expect([rows[0].medication, rows[0].doseValue, rows[0].doseUnit, rows[0].frequency, rows[0].allergyOutcome]).toEqual(["Ibuprofen", 400, "mg", "Three times daily", "NoRecordedMatch"]);
  const audit = await recordedAudit();
  expect(audit).toHaveLength(before + 1);
  const entry = audit[0];
  expect(entry.performedByUserAccountId).toBe(ids.dentistUser);
  expect(entry.targetUserAccountId).toBe(rows[0].id);
  expect(new Date(entry.timestampUtc).getTime()).toBeGreaterThan(Date.now() - 10 * 60_000);
  expect(JSON.stringify(entry)).not.toMatch(/Ibuprofen|400|Penicillin|Latex/);                               // the log names who and when, never the medication, the dose or an allergy
  evidence.audit = { events: audit.length, performedBy: "dentist", containsNoClinicalContent: true };
});

test("CRITERION 2: an incorrect dose is refused beside its box with what was typed kept and the ceiling said not to be a clinical limit; correcting it saves", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const before = (await prescriptionsOf(page, ids.ann)).length;
  await enter(page, { medication: "Amoxicillin", dose: "6000", unit: "mg", frequency: "Three times daily" });
  await expect(safetyPanel(page)).toContainText("No allergy recorded for this patient matches this medication's name");     // amoxicillin is a penicillin; the check does not know that and says so
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(form(page)).toContainText("Nothing was saved. Correct the entries marked below.");
  await expect(form(page)).toContainText("is above the 5000 mg this screen accepts");
  await expect(form(page)).toContainText("not a clinical dose limit");
  await expect(form(page).getByLabel("Dose")).toHaveAttribute("aria-invalid", "true");
  await expect(form(page).getByLabel("Dose")).toHaveValue("6000");                                           // what was typed is still there
  await expect(form(page).getByLabel("Medication")).toHaveValue("Amoxicillin");
  expect(await prescriptionsOf(page, ids.ann)).toHaveLength(before);
  await shot(page, "03-incorrect-dose-refused.png");
  await scanBothThemes(page, "incorrect-dose");

  await form(page).getByLabel("Dose").fill("four hundred");                                                  // not a number: caught in the browser, nothing is sent
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(form(page)).toContainText("Enter the dose as a number, for example 500 or 2.5.");
  expect(await prescriptionsOf(page, ids.ann)).toHaveLength(before);

  await form(page).getByLabel("Dose").fill("500");
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(statusLine(page)).toContainText("Amoxicillin prescribed and logged.");
  expect(await prescriptionsOf(page, ids.ann)).toHaveLength(before + 1);
  evidence.incorrectDose = { refusedAt: 6000, savedAt: 500 };
});

test("ALLERGY MATCH: penicillin is refused until the dentist gives their own words, then saved with them, and the list says it was reviewed", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const before = (await prescriptionsOf(page, ids.ann)).length;
  await enter(page, { medication: "Penicillin V Potassium", dose: "500", unit: "mg", frequency: "Four times daily" });
  await expect(safetyPanel(page)).toContainText("matches an allergy recorded for this patient: Penicillin (high), reaction: Hives");
  const note = form(page).getByLabel(/Why are you prescribing this despite the matching allergy/);
  await expect(note).toBeVisible();

  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(form(page)).toContainText("This medication matches an allergy recorded for this patient. Nothing was saved.");
  await expect(note).toBeFocused();
  expect(await prescriptionsOf(page, ids.ann)).toHaveLength(before);
  await shot(page, "04-allergy-match-needs-review.png");
  await scanBothThemes(page, "allergy-match");

  await note.fill("Childhood rash, tolerated since; discussed with the patient today.");
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(statusLine(page)).toContainText("Penicillin V Potassium prescribed and logged.");
  const item = list(page).getByRole("listitem").filter({ hasText: "Penicillin V Potassium" });
  await expect(item).toContainText("matched a recorded allergy (Penicillin); the prescriber reviewed it");
  await expect(item).toContainText("Prescriber's review: Childhood rash, tolerated since; discussed with the patient today.");
  const rows = await prescriptionsOf(page, ids.ann);
  expect(rows).toHaveLength(before + 1);
  expect([rows[0].allergyOutcome, rows[0].allergyMatches]).toEqual(["RecordedAllergyMatch", "Penicillin"]);
  await shot(page, "05-allergy-match-reviewed-and-saved.png");
});

test("UNREVIEWED ALLERGIES: Bo's list is never treated as clear: the panel says so and a prescription waits for the dentist's own words", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, ids.bo);
  await expect(safetyPanel(page)).toContainText("Allergies have not been reviewed. Nothing listed here does not mean none.");
  await enter(page, { medication: "Ibuprofen", dose: "200", unit: "mg", frequency: "Twice daily" });
  await expect(safetyPanel(page)).toContainText("allergies have not been reviewed");
  const note = form(page).getByLabel(/allergies are not established\. Confirm in your own words/);
  await expect(note).toBeVisible();
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(form(page)).toContainText("allergies are not established, so the allergy check cannot say anything. Nothing was saved.");
  expect(await prescriptionsOf(page, ids.bo)).toHaveLength(0);
  await shot(page, "06-unreviewed-allergies.png");
  await note.fill("Asked the patient today: no allergies.");
  await form(page).getByRole("button", { name: "Save prescription" }).click();
  await expect(statusLine(page)).toContainText("Ibuprofen prescribed and logged.");
  await expect(list(page)).toContainText("allergies were not established when this was prescribed");
  expect((await prescriptionsOf(page, ids.bo))[0].allergyOutcome).toBe("NotEstablished");
});

test("FAILURE PATH: when the log is unavailable nothing is saved, no internals are shown, and the same click works once it is back", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const before = { rows: (await prescriptionsOf(page, ids.ann)).length, audit: (await recordedAudit()).length };
  sql(`CREATE TRIGGER [TR_Test_RefusePrescriptionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'PrescriptionRecorded') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END`);
  try {
    await enter(page, { medication: "Naproxen", dose: "250", unit: "mg", frequency: "Twice daily" });
    await form(page).getByRole("button", { name: "Save prescription" }).click();
    await expect(form(page)).toContainText("Nothing was saved: The prescription could not be saved, so nothing was recorded.");
    await expect(form(page)).not.toContainText(/audit|SqlException|sql/i);
    await expect(form(page).getByLabel("Medication")).toHaveValue("Naproxen");                                // what was typed is still there
    expect(await prescriptionsOf(page, ids.ann)).toHaveLength(before.rows);
    expect(await recordedAudit()).toHaveLength(before.audit);
    await shot(page, "07-failed-log-nothing-saved.png");
  } finally {
    sql("DROP TRIGGER [TR_Test_RefusePrescriptionAudit]");
  }
  await form(page).getByRole("button", { name: "Save prescription" }).click();                                // the same click, the same save key
  await expect(statusLine(page)).toContainText("Naproxen prescribed and logged.");
  expect(await prescriptionsOf(page, ids.ann)).toHaveLength(before.rows + 1);
  expect(await recordedAudit()).toHaveLength(before.audit + 1);
  evidence.failurePath = { savedWhileLogDown: 0, savedAfter: 1 };
});

test("REPLAY and APPEND-ONLY: the same request again changes nothing, a changed one under the same key is a conflict, and a prescription can be neither changed nor deleted", async () => {
  const dentist = sessions.Dentist.page;
  const body = { idempotencyKey: `replay-${stamp}`, medication: "Acetaminophen", doseValue: 500, doseUnit: "mg", frequency: "Every 6 hours", reviewNote: null };
  const before = (await prescriptionsOf(dentist, ids.ann)).length;
  const first = await api(dentist, "post", `/api/patients/${ids.ann}/prescriptions`, body);
  const again = await api(dentist, "post", `/api/patients/${ids.ann}/prescriptions`, body);
  expect([first.status, first.body.replayed, again.status, again.body.replayed, again.body.prescription.id]).toEqual([200, false, 200, true, first.body.prescription.id]);
  const other = await api(dentist, "post", `/api/patients/${ids.ann}/prescriptions`, { ...body, medication: "Naproxen" });
  expect([other.status, other.body.error]).toEqual([409, "idempotency_key_reused"]);
  expect(await prescriptionsOf(dentist, ids.ann)).toHaveLength(before + 1);

  const id = first.body.prescription.id;
  for (const verb of ["put", "delete"] as const) expect([404, 405], `${verb} must not be routed`).toContain((await api(dentist, verb, `/api/patients/${ids.ann}/prescriptions/${id}`, { medication: "x" })).status);   // there is no route that changes or deletes one
  expect(() => sql("UPDATE [Prescriptions] SET [DoseValue] = 1")).toThrow();                                // and the database refuses it too
  expect(() => sql("DELETE FROM [Prescriptions]")).toThrow();
  expect((await prescriptionsOf(dentist, ids.ann)).find((p) => p.id === id).doseValue).toBe(500);
});

test("READ-ONLY: a hygienist reads the prescriptions and the safety panel, has no form and is refused by the server; front desk has no tab and is refused", async () => {
  const hyg = sessions.Hygienist.page;
  await gotoTab(hyg);
  await expect(form(hyg)).toHaveCount(0);
  await expect(hyg.getByText("You can read this but your role cannot prescribe.")).toBeVisible();
  await expect(safetyPanel(hyg)).toContainText("Penicillin (high) Hives");
  await expect(list(hyg)).toContainText("Ibuprofen");
  await shot(hyg, "08-hygienist-read-only.png");
  const body = { idempotencyKey: "ro", medication: "Ibuprofen", doseValue: 400, doseUnit: "mg", frequency: "Once daily", reviewNote: null };
  expect((await api(hyg, "post", `/api/patients/${ids.ann}/prescriptions`, body)).status).toBe(403);
  expect((await api(hyg, "post", `/api/patients/${ids.ann}/prescriptions/check`, body)).status).toBe(403);
  expect((await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/prescriptions`, body, {}, false)).status).toBe(400);   // no CSRF token

  const front = sessions.FrontDesk.page;
  await front.goto(`/patients/${ids.ann}`);
  await expect(front.getByRole("link", { name: "Prescriptions" })).toHaveCount(0);
  expect((await api(front, "get", `/api/patients/${ids.ann}/prescriptions`)).status).toBe(403);
  expect((await api(front, "post", `/api/patients/${ids.ann}/prescriptions`, body)).status).toBe(403);
  evidence.readOnly = { hygienistWrite: 403, frontDeskRead: 403 };
});

test("TRUST: every saved prescription is in the audit log with the dentist's id and a time, and none of it names a medication, a dose or an allergy", async () => {
  const audit = await recordedAudit();
  const saved = (await prescriptionsOf(sessions.Dentist.page, ids.ann)).length + (await prescriptionsOf(sessions.Dentist.page, ids.bo)).length;
  expect(audit).toHaveLength(saved);
  expect(audit.every((e) => e.performedByUserAccountId === ids.dentistUser && !Number.isNaN(Date.parse(e.timestampUtc)) && e.correlationId)).toBe(true);
  expect(JSON.stringify(audit)).not.toMatch(/Ibuprofen|Amoxicillin|Penicillin|Naproxen|Acetaminophen|Latex/);
  evidence.trust = { prescriptions: saved, auditEntries: audit.length };
  await gotoTab(sessions.Dentist.page);
  await shot(sessions.Dentist.page, "09-final-list.png");
});
