import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Locator, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { createAccount } from "./support/testAccounts";

// ALV-016-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.prescription-workflow.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// A dentist writes a prescription as a draft (saved incomplete, kept across a reload), is told what is missing, finalizes it through a confirmation, and it is logged with who and when; a medication that matches a
// recorded allergy waits for the dentist's own words; a patient whose allergies were never reviewed gets the same; an allergy added after the draft was started is shown and must be acknowledged; an amendment
// replaces a prescription without editing it; a cancellation needs a reason; a print is issued on the server and opened as the server's PDF (ALV-N026: never the application page through
// window.print), and an ended prescription only as a record copy; when the log is unavailable nothing is finalized and the same click works once it is back;
// a hygienist reads and prints but cannot change anything; and nothing can be edited or deleted. Nothing on any screen claims interaction checking or dose appropriateness.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PRESCRIPTION_WORKFLOW_E2E_OUT ?? path.join(process.cwd(), "prescription-workflow-e2e-out");
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
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log and immutability demonstrations need it)");
  execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
}

async function createUser(role: string, username: string): Promise<string> {
  const token = await csrf(admin);
  const reg = await createAccount(admin.request, { username, password: PASSWORD, role: "Unassigned" });
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
const composer = (page: Page) => page.getByRole("form", { name: "Prescription composer" });
const confirmation = (page: Page) => page.getByRole("region", { name: "Confirm finalization" });
const safetyPanel = (page: Page) => page.getByRole("complementary", { name: "Patient safety" });
const record = (page: Page, status: string, medication: string) => page.locator(`li.alv-rx__item[data-status="${status}"]`).filter({ hasText: medication });
const recordsOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${patient}/prescription-records`)).body.prescriptions as any[];
const auditOf = async (type: string) => ((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).filter((e) => e.eventType === type);

/**
 * ALV-N026 R02 (review item 2), corrected in R03: clicks a print button and returns what the browser did. The button must issue on the server (POST .../issues) and then fetch the issue's
 * PDF; the PDF must reach the user as a new tab or a download (either is accepted - the implemented flow opens a tab and sends it to the PDF), and the application page must never call
 * window.print(). R02's version counted only tabs still open and downloads on the application page, and so missed a headless browser that turns the new tab's navigation to a PDF into a
 * download on that tab (and may close it): every tab opened during the click is now counted even if it closed, downloads are heard on every page, and where the PDF arrived is recorded.
 * R05 (owner decision 2026-11-22): the intercepted browser response is kept for diagnostics only (status, Content-Type, Content-Length if sent, and the byte count Playwright observed,
 * which can be empty once the page has taken the body as a blob). The PDF's bytes are validated from one follow-up fetch of the same, already-issued PDF through the same signed-in
 * session; an empty or non-PDF follow-up is a product failure.
 */
async function pdfFromClick(page: Page, button: Locator) {
  await page.evaluate(() => { (window as any).__printed = 0; window.print = () => { (window as any).__printed++; }; });
  const context = page.context();
  const before = new Set(context.pages());
  const arrivals: string[] = [];
  const opened: Page[] = [];
  const onAppDownload = () => { arrivals.push("download on the application page"); };
  const onPage = (tab: Page) => {
    opened.push(tab);
    arrivals.push("new tab");
    tab.on("download", () => { arrivals.push("download on the new tab"); });
  };
  page.on("download", onAppDownload);
  context.on("page", onPage);
  try {
    const issued = page.waitForResponse((r) => r.request().method() === "POST" && /\/api\/prescriptions\/[0-9a-f-]+\/issues$/.test(new URL(r.url()).pathname));
    const pdf = page.waitForResponse((r) => r.request().method() === "GET" && /\/api\/prescription-issues\/[0-9a-f-]+\/pdf$/.test(new URL(r.url()).pathname));
    await button.click();
    const issue = await issued;
    const response = await pdf;
    let observedBytes: number | string;
    try { observedBytes = (await response.body()).length; } catch (e) { observedBytes = `unavailable: ${(e as Error).message.split("\n")[0]}`; }
    const intercepted = { status: response.status(), contentType: response.headers()["content-type"] ?? "", contentLength: response.headers()["content-length"] ?? null, observedBytes };
    await expect.poll(() => arrivals.length, { message: "the PDF reached the user as a tab or a download" }).toBeGreaterThan(0);
    await page.waitForTimeout(500);                                                                                // let a tab's download, if any, be heard before recording how it arrived
    const printed = await page.evaluate(() => (window as any).__printed as number);
    for (const tab of opened) if (!tab.isClosed() && !before.has(tab)) await tab.close();
    const issueId = ((await issue.json()) as { id: string }).id;
    const followUp = await page.request.get(`/api/prescription-issues/${issueId}/pdf`);                         // the same issued PDF, once, same signed-in session
    const bytes = await followUp.body();
    return {
      issueStatus: issue.status(), issueId, status: response.status(), contentType: intercepted.contentType, intercepted,
      followUpStatus: followUp.status(), followUpContentType: followUp.headers()["content-type"] ?? "", bytes,
      tabs: opened.length, downloads: arrivals.filter((a) => a.startsWith("download")).length, arrivals: [...arrivals], printed, pageUrl: page.url(),
    };
  } finally {
    page.off("download", onAppDownload);
    context.off("page", onPage);
  }
}

/**
 * The safe server-PDF result: the user's click got a 200 application/pdf in the browser and reached a tab or a download; the same issued PDF, fetched once more through the same session,
 * is a real PDF (an empty or non-PDF follow-up is a product failure); no application page was printed; and no application address or raw server detail is inside it.
 */
function expectServerPdf(result: Awaited<ReturnType<typeof pdfFromClick>>) {
  expect(result.issueStatus).toBe(201);
  expect(result.status).toBe(200);                                                                               // the browser-side request
  expect(result.contentType).toMatch(/^application\/pdf/);
  expect(result.arrivals.length).toBeGreaterThan(0);
  expect(result.followUpStatus, "PRODUCT: the authenticated follow-up fetch of the issued PDF").toBe(200);
  expect(result.followUpContentType, "PRODUCT: the follow-up is a PDF").toMatch(/^application\/pdf/);
  expect(result.bytes.length, "PRODUCT: the follow-up PDF is not empty").toBeGreaterThan(0);
  expect(result.bytes.subarray(0, 5).toString("latin1"), "PRODUCT: the follow-up starts with the PDF signature").toBe("%PDF-");
  expect(result.printed).toBe(0);                                                                                 // the application page was never printed
  const raw = result.bytes.toString("latin1");
  expect(raw).not.toContain(new URL(result.pageUrl).host);                                                        // no app address
  expect(raw).not.toMatch(/localhost|\/api\/|Exception|stack trace/i);
}

async function gotoComposer(page: Page, patient = ids.ann) {
  await page.goto(`/patients/${patient}/prescription-composer`);
  await expect(page.getByRole("heading", { name: "Prescription composer", exact: true })).toBeVisible();
}

type Entry = { medication: string; dose?: string; unit?: string; frequency?: string; instructions?: string; quantity?: string; quantityUnit?: string; refills?: string };
async function fill(page: Page, e: Entry, complete = true) {
  const c = composer(page);
  await c.getByLabel("Medication").fill(e.medication);
  await c.getByLabel("Medication").press("Tab");
  await c.getByLabel("Dose", { exact: true }).fill(e.dose ?? "500");
  await c.getByLabel("Unit", { exact: true }).selectOption(e.unit ?? "mg");
  await c.getByLabel("How often").selectOption(e.frequency ?? "Three times daily");
  if (!complete) return;
  await c.getByLabel("Instructions for the patient").fill(e.instructions ?? "Take one with food until finished.");
  await c.getByLabel("Quantity", { exact: true }).fill(e.quantity ?? "21");
  await c.getByLabel("Quantity unit").selectOption(e.quantityUnit ?? "capsule");
  await c.getByLabel("Refills").fill(e.refills ?? "0");
}

async function newDraft(page: Page) {
  await page.getByRole("button", { name: "New prescription draft" }).click();
  await expect(composer(page)).toBeVisible();
}

/** Finalizes the entry in the composer through the confirmation. */
async function finalize(page: Page, medication: string) {
  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  await expect(confirmation(page)).toBeVisible();
  await confirmation(page).getByRole("button", { name: "Finalize prescription" }).click();
  await expect(statusLine(page)).toContainText(`${medication} finalized and logged.`);
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  expect((await createAccount(admin.request, { username: `admin-${stamp}`, password: "admin-password-1!", role: "Admin" })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "FrontDesk"]) {
    const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    if (role === "Dentist") {
      ids.dentistUser = userId;
      expect((await api(admin, "post", "/api/test-support/staff-profiles", { displayName: `${DENTIST_NAME} ${stamp}`, userAccountId: userId })).status, "staff profile for the dentist").toBe(201);
    }
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "prescription-workflow-evidence.json"), JSON.stringify(evidence, null, 2));
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
  ids.ann = await patient("Ann", "Alder", "rxw-ann", "1985-03-09", "555-010-0100", "1 Main St");
  ids.bo = await patient("Bo", "Brandt", "rxw-bo", "1972-11-21", "555-010-0222", "9 Oak Ave");
  for (const [name, reaction, severity] of [["Penicillin", "Hives", "Severe"], ["Latex", "Rash", "Mild"]]) {
    expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Allergy", name, reaction, severity })).status, name).toBe(200);
  }
  expect((await api(dentist, "put", `/api/patients/${ids.ann}/clinical-record/sections/Allergy/review`, { state: "Reviewed" })).status).toBe(200);
  // what the shared safety framework (ALV-N011) knows besides allergies: a current medication and an active alert, shown while prescribing
  expect((await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Medication", name: "Lisinopril", dose: "10 mg", frequency: "daily" })).status).toBe(200);
  expect((await api(dentist, "post", `/api/patients/${ids.ann}/safety/alerts`, { category: "Condition", title: "Prosthetic heart valve", severity: "High", sourceNote: "Reported by the patient" })).status).toBe(200);
});

test("THE SCREEN: a prescriber gets the composer beside the safety panel and a banner that says no drug-knowledge source is configured", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await expect(page.getByRole("note").filter({ hasText: "Not configured" })).toContainText("Drug interaction checking and clinical dose-appropriateness checking are not configured");
  await newDraft(page);
  const panel = safetyPanel(page);
  await expect(panel).toContainText("Penicillin (high) Hives");
  await expect(panel.getByRole("list", { name: "Current medications" })).toContainText("Lisinopril");               // current medications and ALV-N011 alerts are visible while prescribing
  await expect(panel.getByRole("list", { name: "Other active alerts" })).toContainText("Prosthetic heart valve (high)");
  await expect(panel).toContainText("Not checked: drug interactions, and whether a dose is clinically appropriate");
  await expect(composer(page)).toContainText("not checked against any clinical dose range");
  await expect(composer(page)).toContainText("no controlled-substance rule");                          // the quantity and refills limitation
  await expect(page.locator("body")).not.toContainText(/interaction check(ed|s) passed|dose is (safe|appropriate)|cleared/i);
  await shot(page, "01-composer.png");
  await scanBothThemes(page, "composer");
});

test("A DRAFT is saved incomplete, kept across a reload, and an incomplete one is not finalized (every missing part is named)", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await newDraft(page);
  await fill(page, { medication: "Naproxen", dose: "250" }, false);
  await composer(page).getByRole("button", { name: "Save draft" }).click();
  await expect(composer(page)).toContainText("Draft saved. Nothing is final until you finalize it.");

  await page.reload();
  await expect(page.getByRole("list", { name: "Open drafts" })).toContainText("Naproxen");                 // the draft survived the reload
  await page.getByRole("button", { name: "Open draft Naproxen" }).click();
  await expect(composer(page).getByLabel("Medication")).toHaveValue("Naproxen");

  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  await expect(composer(page)).toContainText("This prescription is not ready to finalize. Complete the entries marked below.");
  const problems = composer(page).getByRole("list", { name: "Entries to correct" });
  for (const text of ["Write the instructions for the patient", "Enter the quantity to dispense", "Choose the quantity unit", "Enter the number of refills"]) await expect(problems).toContainText(text);
  await expect(composer(page).getByLabel("Instructions for the patient")).toHaveAttribute("aria-invalid", "true");
  await expect(confirmation(page)).toHaveCount(0);
  expect(await recordsOf(page, ids.ann)).toHaveLength(0);
  await shot(page, "02-incomplete-not-finalized.png");
  await scanBothThemes(page, "incomplete");

  await composer(page).getByLabel("Quantity", { exact: true }).fill("a lot");
  await composer(page).getByRole("button", { name: "Save draft" }).click();
  await expect(composer(page).getByRole("list", { name: "Entries to correct" })).toContainText("Enter the quantity as a number");   // caught on the screen; nothing is sent
  evidence.draft = { savedIncomplete: true, survivedReload: true, finalizedWhileIncomplete: 0 };
});

test("FINALIZING: a complete entry goes through a confirmation, is logged with the user and the time, and appears with its instructions, quantity and refills", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await page.getByRole("button", { name: "Open draft Naproxen" }).click();
  await composer(page).getByRole("button", { name: "Discard draft" }).click();
  await composer(page).getByLabel(/Why are you discarding this draft/).fill("Started with the wrong medication.");
  await composer(page).getByRole("button", { name: "Confirm discard" }).click();
  await expect(statusLine(page)).toContainText("The draft was discarded. It is kept in the record and cannot be finalized.");

  const before = (await auditOf("PrescriptionRecorded")).length;
  await newDraft(page);
  await fill(page, { medication: "Amoxicillin" });
  await expect(safetyPanel(page)).toContainText("This is a name comparison only, not a clearance.");     // amoxicillin is a penicillin; the check does not know that and says so
  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  await expect(confirmation(page)).toBeVisible();
  for (const text of ["Amoxicillin", "500 mg, three times daily", "Take one with food until finished.", "21 capsule", "cannot be edited afterwards", "Not checked:"]) await expect(confirmation(page)).toContainText(text);
  expect((await recordsOf(page, ids.ann)).length).toBe(0);                                                  // nothing is recorded until it is confirmed
  await shot(page, "03-confirmation.png");
  await scanBothThemes(page, "confirmation");
  await confirmation(page).getByRole("button", { name: "Finalize prescription" }).click();
  await expect(statusLine(page)).toContainText("Amoxicillin finalized and logged.");

  const item = record(page, "Active", "Amoxicillin");
  await expect(item).toContainText("Take one with food until finished.");
  await expect(item).toContainText("Quantity 21 capsule · Refills 0");
  await expect(item).toContainText(`Prescribed by ${DENTIST_NAME} ${stamp}`);
  await expect(item).toContainText("name comparison, not a clearance");
  await shot(page, "04-finalized.png");

  const rows = await recordsOf(page, ids.ann);
  expect(rows).toHaveLength(1);
  expect([rows[0].status, rows[0].instructions, rows[0].quantityValue, rows[0].refills, rows[0].hasDetails]).toEqual(["Active", "Take one with food until finished.", 21, 0, true]);
  const audit = await auditOf("PrescriptionRecorded");
  expect(audit).toHaveLength(before + 1);
  expect(audit[0].performedByUserAccountId).toBe(ids.dentistUser);
  expect(new Date(audit[0].timestampUtc).getTime()).toBeGreaterThan(Date.now() - 10 * 60_000);
  expect(JSON.stringify(audit[0])).not.toMatch(/Amoxicillin|500|Penicillin|Latex|food/);                  // who and when, never the contents
  ids.amoxicillin = rows[0].id;
  evidence.finalized = { recorded: 1, loggedWithUser: true, containsNoClinicalContent: true };
});

test("ALLERGY MATCH: penicillin waits for the dentist's own words, then finalizes with them kept", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await newDraft(page);
  await fill(page, { medication: "Penicillin V Potassium", dose: "500", frequency: "Four times daily", quantity: "40" });
  await expect(safetyPanel(page)).toContainText("matches an allergy recorded for this patient");
  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  const note = composer(page).getByLabel(/Why are you prescribing this despite the matching allergy/);
  await expect(note).toBeFocused();
  await expect(confirmation(page)).toHaveCount(0);
  expect((await recordsOf(page, ids.ann)).filter((r) => r.medication.startsWith("Penicillin"))).toHaveLength(0);
  await shot(page, "05-allergy-match-needs-review.png");
  await note.fill("Childhood rash, tolerated since; discussed with the patient today.");
  await finalize(page, "Penicillin V Potassium");
  const item = record(page, "Active", "Penicillin V Potassium");
  await expect(item).toContainText("matched a recorded allergy (Penicillin); the prescriber reviewed it");
  await expect(item).toContainText("Prescriber's review: Childhood rash, tolerated since; discussed with the patient today.");
});

test("UNREVIEWED ALLERGIES: Bo's list is never treated as clear: finalizing waits for the dentist's own words", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page, ids.bo);
  await newDraft(page);
  await fill(page, { medication: "Ibuprofen", dose: "200", frequency: "Twice daily", quantity: "20", quantityUnit: "tablet" });
  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  const note = composer(page).getByLabel(/allergies are not established\. Confirm in your own words/);
  await expect(note).toBeFocused();
  expect(await recordsOf(page, ids.bo)).toHaveLength(0);
  await shot(page, "06-unreviewed-allergies.png");
  await note.fill("Asked the patient today: no allergies.");
  await finalize(page, "Ibuprofen");
  await expect(record(page, "Active", "Ibuprofen")).toContainText("allergies were not established when this was prescribed");
});

test("AMENDING replaces a prescription without editing it: the original stays as it was and shows Superseded", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await record(page, "Active", "Amoxicillin").getByRole("button", { name: "Amend Amoxicillin" }).click();
  await expect(statusLine(page)).toContainText("An amendment was started as a draft. The original stays active until you finalize it.");
  await expect(page.getByRole("heading", { name: "Amend a prescription (draft)" })).toBeVisible();
  await expect(composer(page).getByLabel("Dose")).toHaveValue("500");                                       // prefilled from the original
  expect((await recordsOf(page, ids.ann)).find((r) => r.id === ids.amoxicillin).status).toBe("Active");        // nothing has changed yet
  await composer(page).getByLabel("Dose").fill("250");
  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  await expect(confirmation(page)).toContainText("It will replace the prescription it amends");
  await confirmation(page).getByRole("button", { name: "Finalize prescription" }).click();
  await expect(statusLine(page)).toContainText("Amoxicillin finalized and logged.");

  await expect(record(page, "Superseded", "Amoxicillin")).toContainText("500 mg");
  await expect(record(page, "Active", "Amoxicillin")).toContainText("250 mg");
  await expect(record(page, "Superseded", "Amoxicillin").getByRole("button", { name: /^(Amend|Cancel|Discontinue) / })).toHaveCount(0);   // an ended prescription cannot end again
  const rows = await recordsOf(page, ids.ann);
  const old = rows.find((r) => r.id === ids.amoxicillin);
  expect([old.status, old.doseValue, old.events.map((e: any) => e.eventType)]).toEqual(["Superseded", 500, ["Finalized", "Superseded"]]);
  expect(old.supersededByPrescriptionId).toBe(rows.find((r) => r.status === "Active" && r.medication === "Amoxicillin").id);
  await shot(page, "07-amended.png");
});

test("CANCELLING needs a reason: without one nothing is recorded, with one the prescription is cancelled and says so when printed", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  const item = record(page, "Active", "Penicillin V Potassium");
  await item.getByRole("button", { name: "Cancel Penicillin V Potassium" }).click();
  await item.getByRole("button", { name: "Confirm cancellation" }).click();
  await expect(item).toContainText("Nothing was recorded.");
  await expect(item).toContainText("Say why, in your own words");
  expect((await recordsOf(page, ids.ann)).find((r) => r.medication === "Penicillin V Potassium").status).toBe("Active");
  await item.getByLabel(/Why is this prescription being cancelled/).fill("Entered for the wrong patient.");
  await item.getByRole("button", { name: "Confirm cancellation" }).click();
  await expect(statusLine(page)).toContainText("Penicillin V Potassium cancelled and logged.");
  const cancelled = record(page, "Cancelled", "Penicillin V Potassium");
  await expect(cancelled).toContainText("Cancelled: it should not be taken or dispensed");
  await expect(cancelled.getByRole("button", { name: "Print Penicillin V Potassium (PDF)" })).toHaveCount(0);       // an ended prescription is never printed to dispense
  const copy = await pdfFromClick(page, cancelled.getByRole("button", { name: "Record copy of Penicillin V Potassium (PDF)" }));
  expectServerPdf(copy);                                                                                         // its watermark text is proven on the rendered PDF server-side
  evidence.recordCopyArrivals = copy.arrivals;
  evidence.recordCopyIntercepted = copy.intercepted;                                                              // diagnostics: what Playwright saw of the browser's own response
  await expect(statusLine(page)).toContainText("A record copy dated today was issued and opened as a PDF.");
  await shot(page, "08-cancelled-record-copy.png");
  const events = (await recordsOf(page, ids.ann)).find((r) => r.medication === "Penicillin V Potassium").events;
  expect([events.at(-1).eventType, events.at(-1).reason]).toEqual(["Cancelled", "Entered for the wrong patient."]);
  const audit = (await auditOf("PrescriptionCancelled"))[0];
  expect(audit.performedByUserAccountId).toBe(ids.dentistUser);
  expect(JSON.stringify(audit)).not.toMatch(/wrong patient|Penicillin/);                                   // the reason is kept with the prescription, never in the log
});

test("PRINTING: Print (PDF) issues the prescription on the server and opens the server's PDF; the application page is never printed and each issue is logged with who and when", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  const item = record(page, "Active", "Amoxicillin");
  await expect(item.getByRole("button", { name: "Print", exact: true })).toHaveCount(0);                          // no in-app print preview any more
  const issuedBefore = (await auditOf("PrescriptionIssued")).length;
  const first = await pdfFromClick(page, item.getByRole("button", { name: "Print Amoxicillin (PDF)" }));
  expectServerPdf(first);
  await expect(statusLine(page)).toContainText("The prescription was issued and opened as a PDF.");
  await expect(page.getByRole("article", { name: "Prescription" })).toHaveCount(0);                               // nothing is laid out on the application page
  await shot(page, "09-print-pdf-opened.png");
  await scanBothThemes(page, "print-pdf");

  const again = await pdfFromClick(page, item.getByRole("button", { name: "Print Amoxicillin (PDF)" }));          // a second click is a second issue, also a server PDF
  expectServerPdf(again);
  expect(again.issueId).not.toBe(first.issueId);
  const pdf = await page.request.get(`/api/prescription-issues/${first.issueId}/pdf`);                          // the first issue reprints as a PDF
  expect([pdf.status(), pdf.headers()["content-type"]?.startsWith("application/pdf")]).toEqual([200, true]);
  expect((await api(page, "get", `/api/prescriptions/${ids.amoxicillin}/print`)).status).toBe(410);               // the old browser print is retired

  const issued = await auditOf("PrescriptionIssued");
  expect(issued.length).toBeGreaterThanOrEqual(issuedBefore + 2);
  expect(issued.every((e) => e.performedByUserAccountId === ids.dentistUser && !Number.isNaN(Date.parse(e.timestampUtc)))).toBe(true);
  expect(JSON.stringify(issued)).not.toMatch(/Amoxicillin|Ann|250/);
  evidence.print = { flow: "server PDF", arrivals: { first: first.arrivals, again: again.arrivals, recordCopy: evidence.recordCopyArrivals },
    intercepted: { first: first.intercepted, again: again.intercepted, recordCopy: evidence.recordCopyIntercepted }, followUpBytes: { first: first.bytes.length, again: again.bytes.length },
    windowPrintCalls: first.printed + again.printed, issues: issued.length };
});

test("FAILURE PATH: when the log is unavailable nothing is finalized, no internals are shown, and the same finalization works once it is back", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await newDraft(page);
  await fill(page, { medication: "Naproxen", dose: "250", frequency: "Twice daily", quantity: "14", quantityUnit: "tablet" });
  const before = { rows: (await recordsOf(page, ids.ann)).length, audit: (await auditOf("PrescriptionRecorded")).length };
  sql(`CREATE TRIGGER [TR_Test_RefuseRecorded] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'PrescriptionRecorded') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END`);
  try {
    await composer(page).getByRole("button", { name: "Finalize…" }).click();
    await confirmation(page).getByRole("button", { name: "Finalize prescription" }).click();
    await expect(composer(page)).toContainText("Nothing was saved: The prescription could not be saved, so nothing was recorded.");
    await expect(composer(page)).not.toContainText(/audit|SqlException|sql/i);
    await expect(composer(page).getByLabel("Medication")).toHaveValue("Naproxen");                          // what was typed is still there
    expect((await recordsOf(page, ids.ann)).length).toBe(before.rows);
    expect((await auditOf("PrescriptionRecorded")).length).toBe(before.audit);
    await shot(page, "10-failed-log-nothing-finalized.png");
  } finally {
    sql("DROP TRIGGER [TR_Test_RefuseRecorded]");
  }
  await finalize(page, "Naproxen");
  expect((await recordsOf(page, ids.ann)).length).toBe(before.rows + 1);
  expect((await auditOf("PrescriptionRecorded")).length).toBe(before.audit + 1);
  evidence.failurePath = { finalizedWhileLogDown: 0, finalizedAfter: 1 };

  await record(page, "Active", "Naproxen").getByRole("button", { name: "Discontinue Naproxen" }).click();                // and a discontinuation records a reason
  await record(page, "Active", "Naproxen").getByLabel(/Why is this prescription being discontinued/).fill("Stopped after stomach upset.");
  await record(page, "Active", "Naproxen").getByRole("button", { name: "Confirm discontinuation" }).click();
  await expect(statusLine(page)).toContainText("Naproxen discontinued and logged.");
  await expect(record(page, "Discontinued", "Naproxen")).toContainText("Discontinued: it was stopped.");
});

test("A PRESCRIPTION SAVED BEFORE THE WORKFLOW says so, cannot be printed, and is amended by filling in the rest", async () => {
  const dentist = sessions.Dentist.page;
  const legacy = await api(dentist, "post", `/api/patients/${ids.bo}/prescriptions`, { idempotencyKey: `legacy-${stamp}`, medication: "Acetaminophen", doseValue: 500, doseUnit: "mg", frequency: "Every 6 hours", reviewNote: "Asked the patient today: no allergies." });
  expect(legacy.status).toBe(200);
  await gotoComposer(dentist, ids.bo);
  const item = record(dentist, "Active", "Acetaminophen");
  await expect(item).toContainText("Saved before instructions, quantity and refills were recorded");
  await expect(item.getByRole("button", { name: /^Print / })).toHaveCount(0);
  expect((await api(dentist, "post", `/api/prescriptions/${legacy.body.prescription.id}/issues`, { kind: "RecordCopy", idempotencyKey: `legacy-copy-${stamp}` })).body.error).toBe("print_incomplete");
  await item.getByRole("button", { name: "Amend Acetaminophen" }).click();
  await expect(composer(dentist).getByLabel("Instructions for the patient")).toHaveValue("");
  await composer(dentist).getByRole("button", { name: "Finalize…" }).click();
  await expect(composer(dentist)).toContainText("This prescription is not ready to finalize");
  await shot(dentist, "11-legacy-amend-needs-details.png");
});

test("READ-ONLY: a hygienist reads and prints but cannot change anything and is refused by the server; front desk has no tab and is refused", async () => {
  const hyg = sessions.Hygienist.page;
  await gotoComposer(hyg);
  await expect(hyg.getByRole("button", { name: "New prescription draft" })).toHaveCount(0);
  await expect(hyg.getByText("You can read and print these but your role cannot prescribe.")).toBeVisible();
  const item = record(hyg, "Active", "Amoxicillin");
  await expect(item).toBeVisible();
  await expect(hyg.getByRole("button", { name: /^(Amend|Cancel|Discontinue) / })).toHaveCount(0);
  await expect(item.getByRole("button", { name: "Print Amoxicillin (PDF)" })).toBeVisible();
  await shot(hyg, "12-hygienist-read-only.png");
  const draft = { idempotencyKey: "ro", medication: "Ibuprofen", doseValue: 400, doseUnit: "mg", frequency: "Once daily", instructions: "x", quantityValue: 1, quantityUnit: "tablet", refills: 0 };
  expect((await api(hyg, "post", `/api/patients/${ids.ann}/prescription-drafts`, draft)).status).toBe(403);
  expect((await api(hyg, "post", `/api/prescriptions/${ids.amoxicillin}/cancel`, { reason: "x" })).status).toBe(403);
  expect((await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/prescription-drafts`, draft, {}, false)).status).toBe(400);   // no CSRF token

  const front = sessions.FrontDesk.page;
  await front.goto(`/patients/${ids.ann}`);
  await expect(front.getByRole("link", { name: "Prescription composer" })).toHaveCount(0);
  expect((await api(front, "get", `/api/patients/${ids.ann}/prescription-records`)).status).toBe(403);
  expect((await api(front, "post", `/api/patients/${ids.ann}/prescription-drafts`, draft)).status).toBe(403);
  evidence.readOnly = { hygienistWrite: 403, frontDeskRead: 403 };
});

test("NOTHING CAN BE EDITED OR DELETED: no route does it and the database refuses it too", async () => {
  const dentist = sessions.Dentist.page;
  for (const verb of ["put", "delete"] as const) {
    expect([404, 405], `${verb} must not be routed`).toContain((await api(dentist, verb, `/api/prescriptions/${ids.amoxicillin}`, { medication: "x" })).status);
  }
  expect([404, 405], "a draft is never deleted").toContain((await api(dentist, "delete", `/api/prescription-drafts/${ids.amoxicillin}`)).status);
  for (const statement of ["UPDATE [Prescriptions] SET [DoseValue] = 1", "DELETE FROM [Prescriptions]", "UPDATE [PrescriptionDetails] SET [Refills] = 5", "DELETE FROM [PrescriptionDetails]",
    "UPDATE [PrescriptionEvents] SET [Reason] = 'x'", "DELETE FROM [PrescriptionEvents]", "DELETE FROM [PrescriptionDrafts]", "UPDATE [PrescriptionDrafts] SET [Medication] = 'x' WHERE [Status] <> 'Draft'"])
    expect(() => sql(statement), statement).toThrow();
  const old = (await recordsOf(dentist, ids.ann)).find((r) => r.id === ids.amoxicillin);
  expect([old.doseValue, old.refills]).toEqual([500, 0]);
});

test("AN ALLERGY ADDED AFTER THE DRAFT WAS STARTED is shown and must be acknowledged before the prescription can be finalized", async () => {
  const page = sessions.Dentist.page;
  await gotoComposer(page);
  await newDraft(page);
  await fill(page, { medication: "Cephalexin", dose: "250", frequency: "Four times daily", quantity: "28" });
  await composer(page).getByRole("button", { name: "Save draft" }).click();
  await expect(composer(page)).toContainText("Draft saved.");
  expect((await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Allergy", name: "Codeine", reaction: "Nausea", severity: "Mild" })).status).toBe(200);

  await page.reload();
  await page.getByRole("button", { name: "Open draft Cephalexin" }).click();
  const alert = page.getByRole("alert").filter({ hasText: "changed since you last looked" });
  await expect(alert).toContainText("Allergy: Codeine");
  await shot(page, "13-allergy-added-after-draft.png");
  await scanBothThemes(page, "list-changed");

  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  await expect(composer(page)).toContainText("Read the changes below, then continue.");
  await expect(confirmation(page)).toHaveCount(0);                                                        // a change is never overridden silently
  expect((await recordsOf(page, ids.ann)).filter((r) => r.medication === "Cephalexin")).toHaveLength(0);

  await alert.getByRole("button", { name: "I have read the changes" }).click();
  await expect(composer(page)).toContainText("You have acknowledged the allergy and medication lists as they are now.");
  await expect(alert).toHaveCount(0);
  await composer(page).getByRole("button", { name: "Finalize…" }).click();
  const note = composer(page).getByLabel(/allergies are not established\. Confirm in your own words/);       // an allergy was added after the list was confirmed, so it needs the dentist's words too
  await expect(note).toBeFocused();
  await note.fill("Reviewed the new codeine allergy with the patient; no conflict.");
  await finalize(page, "Cephalexin");
  evidence.staleLists = { acknowledged: true, finalized: 1 };
});

test("TRUST: every finalized prescription is in the audit log with the dentist's id and a time, and none of it names a medication, a dose or an allergy", async () => {
  const dentist = sessions.Dentist.page;
  const finalized = (await recordsOf(dentist, ids.ann)).length + (await recordsOf(dentist, ids.bo)).length;
  const audit = await auditOf("PrescriptionRecorded");
  expect(audit).toHaveLength(finalized);                                                                    // one per prescription, the one saved through STORY-016's route included
  expect(audit.every((e) => e.performedByUserAccountId === ids.dentistUser && !Number.isNaN(Date.parse(e.timestampUtc)) && e.correlationId)).toBe(true);
  const all = [...await auditOf("PrescriptionDraftCreated"), ...await auditOf("PrescriptionDraftChanged"), ...await auditOf("PrescriptionDraftDiscarded"), ...await auditOf("PrescriptionDraftSafetyAcknowledged"),
    ...await auditOf("PrescriptionAmendmentStarted"), ...await auditOf("PrescriptionSuperseded"), ...await auditOf("PrescriptionCancelled"), ...await auditOf("PrescriptionDiscontinued"), ...audit];
  expect(JSON.stringify(all)).not.toMatch(/Amoxicillin|Penicillin|Latex|Codeine|Naproxen|Cephalexin|Ibuprofen|Acetaminophen|wrong patient|upset/);
  evidence.trust = { finalized, auditEntries: audit.length, clinicalContentInLog: false };
  await gotoComposer(dentist);
  await shot(dentist, "14-final-list.png");
});
