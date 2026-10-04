import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N011 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.safety.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a practice would: nothing is invented for an empty chart (and what is NOT established is said), allergies and medications are read from the
// clinical record, a clinician states an alert with its source, acknowledging is shown to be different from resolving, resolution needs a reason and keeps who/when/why, clearances
// run requested -> received -> resolved (with the missing-document state), the live visit board gets only a two-boolean indicator for authorized roles and never a diagnosis, allergy or
// medication, the people who must not read any of it cannot - then it scans every new screen with axe in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.SAFETY_E2E_OUT ?? path.join(process.cwd(), "safety-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "safety-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const NAMES: Record<string, string> = { Dentist: "Dr. Dana Dentist", Hygienist: "Hana Hygienist" };
const PATIENTS = { ann: "Ann Alert", ben: "Ben Allergy", cy: "Cy Clear" };

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

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
    (evidence.axe as Record<string, unknown>)[`${label}-${theme}`] = { blocking: blocking.length, violations: results.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length })), passes: results.passes.length };
    expect(blocking.map((v) => `${v.id}:${v.nodes.map((n) => `${n.target.join(" ")} ${(n.any[0]?.message ?? n.failureSummary ?? "").slice(0, 160)}`).join(" | ")}`), `${label} (${theme}): critical/serious axe violations`).toEqual([]);
  }
  await setTheme(page, "light");
}

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

const safetyOf = async (page: Page, patient = "ann") => (await api(page, "get", `/api/patients/${ids[patient]}/safety`)).body;
const strip = (page: Page) => page.getByRole("region", { name: "Patient safety", exact: true });
const entry = (page: Page, title: string) => page.locator("li.alv-safety__entry").filter({ has: page.locator("p.alv-safety__entry-title", { hasText: title }) });
const activeEntries = (ctx: any) => (ctx.entries as any[]).filter((e) => e.origin === "Alert");
const statusLine = (page: Page) => page.locator("p.alv-clinical__status");

async function gotoSafety(page: Page, patient = "ann") {
  await page.goto(`/patients/${ids[patient]}/safety`);
  await expect(page.getByRole("heading", { name: "Patient safety details" })).toBeVisible();
}

async function gotoBoard(page: Page) {
  await page.goto("/flow");
  await expect(page.getByRole("heading", { name: "Visit board", exact: true })).toBeVisible();
  await page.getByLabel("Date", { exact: true }).fill(DAY);
  await expect(page.getByRole("region", { name: /^Scheduled \(/ })).toBeVisible();
  await expect(page.getByText(/^Updated /)).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "Assistant", "FrontDesk", "OfficeManager", "Billing"]) {
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
  writeFileSync(path.join(OUT, "safety-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: the practice, three patients and a booked visit each", async () => {
  expect((await api(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op1 = (await api(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id;
  ids.exam = (await api(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 30 })).body.id;
  const staff = await api(admin, "post", "/api/config/staff", { displayName: "Dr. Rivera", jobTitle: "Dentist" });
  ids.rivera = (await api(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" })).body.id;
  const current = (await (await admin.request.get(`/api/config/providers/${ids.rivera}/availability`)).json()) as { revision: number };
  const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
  expect((await api(admin, "put", `/api/config/providers/${ids.rivera}/availability`, { windows, revision: current.revision })).status).toBe(200);
  let hour = 9;
  for (const [key, name] of Object.entries(PATIENTS)) {
    const [first, last] = name.split(" ");
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: "1985-03-09", sex: "Female", phone: `555-01${hour}-0100`, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `safety-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
    const b = await api(sessions.FrontDesk.page, "post", "/api/appointments", { patientId: ids[key], providerId: ids.rivera, operatoryId: ids.op1, appointmentTypeId: ids.exam, startLocal: `${DAY}T${String(hour++).padStart(2, "0")}:00` }, { "Idempotency-Key": `safety-appt-${key}-${stamp}` });
    expect(b.status).toBe(201);
  }
});

test("NOTHING INVENTED: an empty chart shows no alerts, says what is NOT established, and never says 'none'", async () => {
  const page = sessions.Dentist.page;
  await page.goto(`/patients/${ids.ann}`);
  await expect(page.getByText("No alerts, active allergies or current medications are recorded.")).toBeVisible();
  await expect(strip(page).getByText("Allergies have not been reviewed. Nothing listed here does not mean none.")).toBeVisible();
  await expect(strip(page).getByText("Medications have not been reviewed. Nothing listed here does not mean none.")).toBeVisible();
  await expect(strip(page).getByText(/active alert/)).toHaveCount(0);
  await shot(page, "01-empty-header.png");

  await gotoSafety(page);
  await expect(page.getByText(/This is not a statement that there are none\./)).toBeVisible();
  await expect(page.getByText("Not established")).toBeVisible();
  await shot(page, "02-empty-safety.png");
  const ctx = await safetyOf(page);
  expect([ctx.entries.length, ctx.resolved.length, ctx.clearances.length, ctx.gaps.length]).toEqual([0, 0, 0, 3]);
  expect(ctx.summary).toMatchObject({ activeAlertCount: 0, activeAllergyCount: 0, currentMedicationCount: 0, openClearanceCount: 0, highestSeverity: null });
  evidence.nothingInvented = { entries: 0, alerts: 0, gaps: ctx.gaps.map((g: any) => g.section) };
});

test("ALLERGIES AND MEDICATIONS are read from the clinical record, in the safety picture with their source and without being copied; a milder or resolved item is not shown as active", async () => {
  const dentist = sessions.Dentist.page;
  const add = async (kind: string, name: string, extra: object = {}) => {
    const r = await api(dentist, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind, name, ...extra });
    expect(r.status).toBe(200);
    return r.body;
  };
  await add("Allergy", "Penicillin", { reaction: "Hives", severity: "Severe" });
  await add("Allergy", "Latex");                                                         // severity not recorded: shown as such, never guessed
  await add("Medication", "Lisinopril", { dose: "10 mg", frequency: "daily" });
  await add("MedicalHistory", "Atrial fibrillation");
  const record = (await api(dentist, "get", `/api/patients/${ids.ann}/clinical-record`)).body;
  const latex = record.sections.find((s: any) => s.kind === "Allergy").items.find((i: any) => i.name === "Latex");
  const resolved = await api(dentist, "post", `/api/clinical-record/items/${latex.id}/status`, { status: "Resolved", rowVersion: latex.rowVersion, reason: "Tested negative" });
  expect(resolved.status).toBe(200);

  await gotoSafety(dentist);
  await expect(entry(dentist, "Penicillin")).toContainText("High");
  await expect(entry(dentist, "Penicillin")).toContainText("Source: Clinical record: allergies");
  await expect(entry(dentist, "Penicillin")).toContainText("Hives");
  await expect(entry(dentist, "Lisinopril")).toContainText("10 mg · daily");
  await expect(entry(dentist, "Latex")).toHaveCount(0);                                  // resolved in the record: no longer active safety information
  await expect(entry(dentist, "Penicillin").getByRole("button")).toHaveCount(0);        // one source of truth: it is changed in the clinical record
  await expect(entry(dentist, "Penicillin").getByRole("link", { name: "Change it in the clinical record" })).toBeVisible();
  await expect(strip(dentist).getByText(/1 active allergy · 1 current medication/)).toBeVisible();
  await shot(dentist, "03-record-entries.png");

  const ctx = await safetyOf(dentist);
  expect(ctx.entries.map((e: any) => [e.origin, e.category, e.title, e.severity])).toEqual([["ClinicalRecord", "Allergy", "Penicillin", "High"], ["ClinicalRecord", "Medication", "Lisinopril", null]]);
  expect(ctx.gaps.map((g: any) => g.section)).toEqual(["Allergy", "Medication", "MedicalHistory"]);                      // listed but never confirmed: still not established
  for (const section of ["Allergy", "Medication", "MedicalHistory"]) expect((await api(dentist, "put", `/api/patients/${ids.ann}/clinical-record/sections/${section}/review`, { state: "Reviewed" })).status).toBe(200);
  expect((await safetyOf(dentist)).gaps).toEqual([]);                                                                      // confirmed: no gap
  evidence.recordEntries = { fromRecord: ctx.entries.map((e: any) => `${e.category}:${e.title}`), resolvedItemHidden: true };
});

test("ALERT LIFECYCLE: a clinician states an alert with its source; acknowledging is shown to be different from resolving; resolving needs a reason and keeps who, when and why", async () => {
  const dentist = sessions.Dentist.page;
  const hyg = sessions.Hygienist.page;
  await gotoSafety(dentist);
  await dentist.getByRole("button", { name: "Add an alert" }).click();
  await dentist.getByLabel("What kind of alert").selectOption("Anticoagulant");
  await dentist.getByLabel("Title", { exact: true }).fill("Anticoagulant therapy");
  await dentist.getByLabel("Severity").selectOption("Critical");
  await dentist.getByLabel("Details", { exact: true }).fill("Warfarin, INR 2.5");
  await dentist.getByRole("button", { name: "Add alert" }).click();
  await expect(dentist.getByText("Say where this information came from.")).toBeVisible();                       // no source: stopped on screen
  const refused = await api(dentist, "post", `/api/patients/${ids.ann}/safety/alerts`, { category: "Anticoagulant", title: "Anticoagulant therapy", severity: "Critical" });
  expect([refused.status, refused.body.error]).toEqual([400, "source_required"]);                                // and by the API
  await dentist.getByLabel("Source of this information").fill("Cardiology letter, 2030-01-02");
  await dentist.getByRole("button", { name: "Add alert" }).click();
  await expect(entry(dentist, "Anticoagulant therapy")).toBeVisible();
  await expect(entry(dentist, "Anticoagulant therapy")).toContainText("Source: Cardiology letter, 2030-01-02");
  await expect(entry(dentist, "Anticoagulant therapy")).toContainText(`by ${NAMES.Dentist}`);
  await expect(strip(dentist).getByText(/1 active alert \(highest: Critical\)/)).toBeVisible();
  await expect(dentist.locator("li.alv-safety__entry").first()).toContainText("Anticoagulant therapy");                // most urgent first
  expect(activeEntries(await safetyOf(dentist)).length).toBe(1);

  // the hygienist has not seen it: acknowledging records that, and says it is STILL ACTIVE
  await gotoSafety(hyg);
  await expect(strip(hyg).getByText("1 alert you have not acknowledged yet.")).toBeVisible();
  await shot(hyg, "04-unacknowledged.png");
  await hyg.getByRole("button", { name: "Acknowledge: Anticoagulant therapy" }).click();
  await expect(entry(hyg, "Anticoagulant therapy")).toContainText("It is still active - acknowledging does not resolve it.");
  await expect(entry(hyg, "Anticoagulant therapy").getByText("Active", { exact: true })).toBeVisible();
  await expect(strip(hyg).getByText(/you have not acknowledged/)).toHaveCount(0);
  await shot(hyg, "05-acknowledged-still-active.png");
  const afterAck = activeEntries(await safetyOf(hyg))[0];
  expect([afterAck.status, afterAck.acknowledgedByMe]).toEqual(["Active", true]);
  expect(activeEntries(await safetyOf(dentist))[0].acknowledgedByMe).toBe(false);                                    // acknowledgement is per person
  ids.alertId = afterAck.id;

  // a change means it must be seen again, and the old revision can no longer be acknowledged
  const before = activeEntries(await safetyOf(dentist))[0];
  await entry(dentist, "Anticoagulant therapy").getByRole("button", { name: "Change alert: Anticoagulant therapy" }).click();
  await dentist.getByLabel("Details", { exact: true }).fill("Warfarin, INR 3.1 (rechecked)");
  await dentist.getByRole("button", { name: "Save changes" }).click();
  await expect(statusLine(dentist)).toHaveText("Alert changes saved.");                      // the save finished (text alone would match the open textarea)
  await expect(entry(dentist, "Anticoagulant therapy")).toContainText("INR 3.1");
  const stale = await api(hyg, "post", `/api/safety/alerts/${ids.alertId}/acknowledge`, { revision: before.revision });
  expect([stale.status, stale.body.error], `stale revision ${before.revision}, current ${JSON.stringify(activeEntries(await safetyOf(dentist)).map((e: any) => [e.revision, e.detail]))}`).toEqual([409, "alert_changed"]);
  expect(activeEntries(await safetyOf(hyg))[0].acknowledgedByMe).toBe(false);
  await hyg.reload();
  await expect(strip(hyg).getByText("1 alert you have not acknowledged yet.")).toBeVisible();

  // resolving needs a reason, on screen and in the API
  await gotoSafety(dentist);
  await entry(dentist, "Anticoagulant therapy").getByRole("button", { name: "Resolve alert: Anticoagulant therapy" }).click();
  await dentist.getByRole("button", { name: "Resolve alert", exact: true }).click();
  await expect(dentist.getByText("Say why.")).toBeVisible();
  const cur = activeEntries(await safetyOf(dentist))[0];
  const noReason = await api(dentist, "post", `/api/safety/alerts/${ids.alertId}/resolve`, { rowVersion: cur.rowVersion });
  expect([noReason.status, noReason.body.error]).toEqual([400, "reason_required"]);
  await dentist.getByLabel(/Why is "Anticoagulant therapy" resolved\?/).fill("Therapy stopped by the cardiologist");
  await dentist.getByRole("button", { name: "Resolve alert", exact: true }).click();
  await expect(dentist.getByText("Resolved alerts (1)")).toBeVisible();
  await dentist.getByText("Resolved alerts (1)").click();
  await expect(dentist.getByText(new RegExp(`Resolved .* by ${NAMES.Dentist}: Therapy stopped by the cardiologist`))).toBeVisible();
  await shot(dentist, "06-resolved-with-reason.png");
  const ctx = await safetyOf(dentist);
  expect([activeEntries(ctx).length, ctx.resolved.length, ctx.resolved[0].resolutionReason, ctx.resolved[0].resolvedByName]).toEqual([0, 1, "Therapy stopped by the cardiologist", NAMES.Dentist]);

  // reopen (a reason again), then the history keeps every step with names
  await dentist.getByRole("button", { name: "Reopen alert: Anticoagulant therapy" }).click();
  await dentist.getByLabel(/Why is "Anticoagulant therapy" being reopened\?/).fill("Restarted in March");
  await dentist.getByRole("button", { name: "Reopen alert", exact: true }).click();
  await expect(statusLine(dentist)).toHaveText("Anticoagulant therapy reopened.");
  await expect(dentist.getByText("Resolved alerts (1)")).toHaveCount(0);
  const history = (await api(dentist, "get", `/api/safety/alerts/${ids.alertId}/history`)).body;
  expect(history.versions.map((v: any) => [v.changeType, v.reason, v.actorName])).toEqual([
    ["Created", null, NAMES.Dentist], ["Changed", null, NAMES.Dentist], ["Resolved", "Therapy stopped by the cardiologist", NAMES.Dentist], ["Reopened", "Restarted in March", NAMES.Dentist],
  ]);
  expect(history.versions.map((v: any) => v.title)).toEqual(Array(4).fill("Anticoagulant therapy"));   // what it said is kept
  evidence.alertLifecycle = { stated: "with source", acknowledgedStillActive: true, ackIsPerPerson: true, staleAckRefused: "alert_changed", resolveNeedsReason: "reason_required", history: history.versions.map((v: any) => v.changeType) };

  await gotoSafety(dentist);
  await scanBothThemes(dentist, "safety-details");
  await dentist.getByRole("button", { name: "Add an alert" }).click();
  await entry(dentist, "Anticoagulant therapy").getByRole("button", { name: "Resolve alert: Anticoagulant therapy" }).click();
  await dentist.getByRole("button", { name: "Request a clearance" }).click();
  await scanBothThemes(dentist, "safety-details-forms-open");
});

test("CLEARANCES: requested -> received (without its document, said in words) -> document attached later -> resolved with a reason; a clearance still waiting cannot be resolved", async () => {
  const dentist = sessions.Dentist.page;
  await gotoSafety(dentist);
  await dentist.getByRole("button", { name: "Request a clearance" }).click();
  await dentist.getByLabel("Why is it needed?").fill("Cardiac clearance before extraction");
  await dentist.getByLabel("Requested from (optional)").fill("Dr. Singh, cardiology");
  await dentist.getByRole("button", { name: "Request clearance" }).click();
  const label = "Medical clearance: Cardiac clearance before extraction";
  await expect(dentist.getByText("Requested - waiting")).toBeVisible();
  await expect(dentist.getByRole("button", { name: `Resolve: ${label}` })).toHaveCount(0);
  let ctx = await safetyOf(dentist);
  ids.clearanceId = ctx.clearances[0].id;
  const early = await api(dentist, "post", `/api/safety/clearances/${ids.clearanceId}/resolve`, { reason: "Assumed", rowVersion: ctx.clearances[0].rowVersion });
  expect([early.status, early.body.error]).toEqual([409, "clearance_not_received"]);
  await expect(strip(dentist).getByText(/1 clearance open/)).toBeVisible();

  await dentist.getByRole("button", { name: `Mark as received: ${label}` }).click();
  await dentist.getByLabel("Note (optional)").fill("Phoned through by the cardiologist's office");
  await dentist.getByRole("button", { name: "Mark as received", exact: true }).click();
  await expect(dentist.getByText("Received - not yet resolved")).toBeVisible();                                      // received is NOT resolved
  await expect(dentist.getByText(/Supporting document not yet attached\./)).toBeVisible();                          // the document is not available yet
  await expect(strip(dentist).getByText(/1 clearance open/)).toBeVisible();
  await shot(dentist, "07-clearance-received-no-document.png");

  await dentist.getByRole("button", { name: `Attach document: ${label}` }).click();
  await dentist.getByLabel("Document reference").fill("cardiac-letter-2030-01-12.pdf");
  await dentist.getByRole("button", { name: "Attach document", exact: true }).click();
  await expect(dentist.getByText("Supporting document: cardiac-letter-2030-01-12.pdf")).toBeVisible();
  await expect(dentist.getByText(/Supporting document not yet attached/)).toHaveCount(0);

  await dentist.getByRole("button", { name: `Resolve: ${label}` }).click();
  await dentist.getByRole("button", { name: "Resolve clearance" }).click();
  await expect(dentist.getByText("Say why.")).toBeVisible();
  await dentist.getByLabel("Why is this clearance resolved?").fill("Cleared for extraction with epinephrine limits");
  await dentist.getByRole("button", { name: "Resolve clearance" }).click();
  await expect(dentist.getByText(new RegExp(`Resolved .* by ${NAMES.Dentist}: Cleared for extraction with epinephrine limits`))).toBeVisible();
  await shot(dentist, "08-clearance-resolved.png");

  ctx = await safetyOf(dentist);
  expect([ctx.clearances[0].status, ctx.clearances[0].closedByName, ctx.clearances[0].documentPending, ctx.summary.openClearanceCount]).toEqual(["Resolved", NAMES.Dentist, false, 0]);
  const history = (await api(dentist, "get", `/api/safety/clearances/${ids.clearanceId}/history`)).body;
  expect(history.versions.map((v: any) => v.changeType)).toEqual(["Requested", "Received", "DocumentAttached", "Resolved"]);
  expect(history.versions[1].documentReference).toBeNull();                                                          // before the document arrived it said so
  // a second clearance stays open for the board, and a closed one can be cancelled only with a reason
  const second = await api(dentist, "post", `/api/patients/${ids.ann}/safety/clearances`, { kind: "Dental", reason: "Specialist opinion on the implant site", requestedFrom: "Dr. Patel" });
  expect(second.status).toBe(200);
  evidence.clearances = { workflow: history.versions.map((v: any) => v.changeType), waitingCannotBeResolved: "clearance_not_received", documentPendingShown: true, resolvedBy: ctx.clearances[0].closedByName };
  await scanBothThemes(dentist, "safety-clearances");
});

test("HEADER AND ENCOUNTER: safety information is in view before documenting - on every patient screen and at the top of an encounter", async () => {
  const dentist = sessions.Dentist.page;
  await dentist.goto(`/patients/${ids.ann}/clinical`);
  await expect(dentist.getByRole("heading", { name: "Clinical documentation" })).toBeVisible();
  await expect(strip(dentist).getByText(/1 active alert \(highest: Critical\)/)).toBeVisible();                     // the patient header, on a screen that is not the safety tab
  await dentist.getByRole("button", { name: "Start an encounter" }).click();
  await expect(dentist.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();
  const own = dentist.getByRole("region", { name: "Patient safety for this encounter" });
  await expect(own).toBeVisible();
  await expect(own.getByText(/1 active alert \(highest: Critical\)/)).toBeVisible();
  await expect(own.getByText(/1 clearance open/)).toBeVisible();
  const first = await own.boundingBox();
  const sections = await dentist.getByRole("region", { name: "Medical history" }).boundingBox();
  expect(first!.y).toBeLessThan(sections!.y);                                                                        // above the documentation sections
  await shot(dentist, "09-encounter-strip.png");
  await scanBothThemes(dentist, "encounter-with-safety-strip");
  evidence.encounterStrip = { aboveDocumentation: true, alert: "1 active alert (highest: Critical)" };
});

test("LIVE BOARD: authorized roles see only 'Safety alert on file' and 'Clearance open'; others see nothing; no diagnosis, allergy or medication ever reaches the shared board", async () => {
  const dentist = sessions.Dentist.page;
  // Ben has only a severe allergy in the record (no alert, no clearance); Cy has nothing at all
  expect((await api(dentist, "post", `/api/patients/${ids.ben}/clinical-record/items`, { kind: "Allergy", name: "Sulfa drugs", reaction: "Anaphylaxis", severity: "Severe" })).status).toBe(200);
  await gotoBoard(dentist);
  const cards = dentist.locator("article.flow-card");
  const ann = cards.filter({ hasText: PATIENTS.ann });
  const ben = cards.filter({ hasText: PATIENTS.ben });
  const cy = cards.filter({ hasText: PATIENTS.cy });
  await expect(ann.getByText("Safety alert on file")).toBeVisible();
  await expect(ann.getByText("Clearance open")).toBeVisible();
  await expect(ben.getByText("Safety alert on file")).toBeVisible();                                                  // a severe allergy raises the flag...
  await expect(ben.getByText("Clearance open")).toHaveCount(0);
  await expect(ben).not.toContainText("Sulfa");                                                                       // ...without naming it
  await expect(cy.locator("[data-safety]")).toHaveCount(0);                                                           // nothing on file: nothing drawn
  await shot(dentist, "10-board-indicator.png");

  const secrets = ["Anticoagulant", "Warfarin", "Penicillin", "Lisinopril", "Sulfa", "Anaphylaxis", "Atrial", "Cardiac", "Critical", "cardiology", "INR"];
  const boardJson = async (page: Page) => JSON.stringify((await api(page, "get", `/api/visits/board?date=${DAY}`)).body);
  const forDentist = await boardJson(dentist);
  const parsed = JSON.parse(forDentist);
  const safetyKeys = new Set<string>();
  for (const card of parsed.visits) if (card.safety) Object.keys(card.safety).forEach((k) => safetyKeys.add(k));
  expect([...safetyKeys].sort()).toEqual(["alert", "clearance"]);                                                     // the whole contract: two booleans
  for (const s of secrets) expect(forDentist, `board must not contain ${s}`).not.toContain(s);

  for (const role of ["Hygienist", "Assistant"]) {
    const body = JSON.parse(await boardJson(sessions[role].page));
    expect(body.visits.some((c: any) => c.safety?.alert === true), `${role} sees the indicator`).toBe(true);
  }
  for (const role of ["FrontDesk", "OfficeManager"]) {
    const page = sessions[role].page;
    const body = await boardJson(page);
    expect(body.includes('"safety":{'), `${role} must get no safety information`).toBe(false);
    await gotoBoard(page);
    await expect(page.getByText("Safety alert on file")).toHaveCount(0);
    await expect(page.getByText("Clearance open")).toHaveCount(0);
  }
  expect((await api(sessions.Billing.page, "get", `/api/visits/board?date=${DAY}`)).status).toBe(403);
  await scanBothThemes(dentist, "board-with-indicator");
  evidence.board = { safetyKeys: [...safetyKeys].sort(), withIndicator: ["Dentist", "Hygienist", "Assistant"], without: ["FrontDesk", "OfficeManager"], billing: 403, leaked: [] };
});

test("CONCURRENCY: a stale alert edit is refused with the conflict banner and what was typed survives the reload", async () => {
  const first = sessions.Dentist.page;
  const second = sessions.Hygienist.page;
  await gotoSafety(first);
  await gotoSafety(second);
  await entry(second, "Anticoagulant therapy").getByRole("button", { name: "Change alert: Anticoagulant therapy" }).click();
  await second.getByLabel("Details", { exact: true }).fill("Hygienist's note: bleeds easily");
  await entry(first, "Anticoagulant therapy").getByRole("button", { name: "Change alert: Anticoagulant therapy" }).click();
  await first.getByLabel("Details", { exact: true }).fill("Dentist's note: INR 3.4");
  await first.getByRole("button", { name: "Save changes" }).click();
  await expect(statusLine(first)).toHaveText("Alert changes saved.");
  await expect(entry(first, "Anticoagulant therapy")).toContainText("INR 3.4");

  await second.getByRole("button", { name: "Save changes" }).click();
  await expect(second.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(second, "11-stale-edit.png");
  expect(activeEntries(await safetyOf(first))[0].detail).toBe("Dentist's note: INR 3.4");                                // nothing was overwritten
  await scanBothThemes(second, "safety-conflict-banner");
  await second.getByRole("button", { name: /reload/i }).click();
  await expect(second.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  await expect(second.getByLabel("Details", { exact: true })).toHaveValue("Hygienist's note: bleeds easily");                              // the typed text survived the in-place reload
  await second.getByRole("button", { name: "Save changes" }).click();
  await expect(statusLine(second)).toHaveText("Alert changes saved.");
  await expect(entry(second, "Anticoagulant therapy")).toContainText("bleeds easily");
  expect(activeEntries(await safetyOf(first))[0].detail).toBe("Hygienist's note: bleeds easily");
  evidence.concurrency = { staleEditRefused: true, otherChangeStood: true, typedValuesSurvivedReload: true, savedAfterReload: true };
});

test("KEYBOARD: an alert can be acknowledged without a mouse", async () => {
  const assistant = sessions.Assistant.page;
  await gotoSafety(assistant);
  await assistant.getByRole("button", { name: "Acknowledge: Anticoagulant therapy" }).focus();
  await assistant.keyboard.press("Enter");
  await expect(entry(assistant, "Anticoagulant therapy")).toContainText("You acknowledged this");
  evidence.keyboard = { acknowledgedWithKeyboardOnly: true };
});

test("ACCESS: front desk, office manager and billing cannot read any of it; an assistant reads and acknowledges but cannot change anything", async () => {
  const denied: Record<string, number[]> = {};
  for (const role of ["FrontDesk", "OfficeManager", "Billing"]) {
    const page = sessions[role].page;
    const probes = [
      await api(page, "get", `/api/patients/${ids.ann}/safety`), await api(page, "get", `/api/patients/${ids.ann}/safety/summary`),
      await api(page, "post", `/api/patients/${ids.ann}/safety/alerts`, { category: "Custom", title: "No", severity: "Low", sourceNote: "x" }), await api(page, "post", `/api/safety/alerts/${ids.alertId}/acknowledge`, { revision: 1 }),
      await api(page, "get", `/api/safety/alerts/${ids.alertId}/history`), await api(page, "post", `/api/patients/${ids.ann}/safety/clearances`, { kind: "Medical", reason: "No" }),
    ];
    expect(probes.map((p) => p.status), role).toEqual([403, 403, 403, 403, 403, 403]);
    denied[role] = probes.map((p) => p.status);
    if (role !== "Billing") {
      await page.goto(`/patients/${ids.ann}`);
      await expect(page.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
      await expect(strip(page)).toHaveCount(0);                                                                         // no strip, no request
      await expect(page.getByRole("link", { name: "Safety", exact: true })).toHaveCount(0);
      await page.goto(`/patients/${ids.ann}/safety`);
      await expect(page.getByText(/don't have permission/i).first()).toBeVisible();
    }
  }
  const assistant = sessions.Assistant.page;
  await gotoSafety(assistant);
  await expect(assistant.getByText(/your role cannot change it/)).toBeVisible();
  await expect(assistant.getByRole("button", { name: /Add an alert|Request a clearance|Change alert|Resolve|Reopen|Mark as received|Attach document|Cancel clearance/ })).toHaveCount(0);
  expect((await api(assistant, "post", `/api/patients/${ids.ann}/safety/alerts`, { category: "Custom", title: "No", severity: "Low", sourceNote: "x" })).status).toBe(403);
  expect((await api(assistant, "get", `/api/patients/${ids.ann}/safety`)).status).toBe(200);
  evidence.access = { denied, assistant: { read: 200, write: 403, acknowledge: "allowed", controls: 0 } };
});

test("TRUST: every alert, acknowledgement, resolution and clearance step is in the audit log with the user and a time, and no safety content is in it", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const safety = entries.filter((e) => e.eventType.startsWith("SafetyAlert") || e.eventType.startsWith("Clearance"));
  const by = (t: string) => safety.filter((e) => e.eventType === t);
  expect(by("SafetyAlertCreated").length).toBe(1);
  expect(by("SafetyAlertChanged").length).toBe(3);
  expect(by("SafetyAlertAcknowledged").length).toBe(2);
  expect(by("SafetyAlertResolved").length).toBe(1);
  expect(by("SafetyAlertReopened").length).toBe(1);
  expect(by("ClearanceRequested").length).toBe(2);
  expect(by("ClearanceReceived").length).toBe(1);
  expect(by("ClearanceDocumentAttached").length).toBe(1);
  expect(by("ClearanceResolved").length).toBe(1);
  expect(safety.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  expect(by("SafetyAlertAcknowledged").every((e) => e.details.includes("still active"))).toBe(true);
  const text = safety.map((e) => e.details).join(" ");
  for (const secret of ["Anticoagulant", "Warfarin", "INR", "Cardiology", "cardiac", "Cardiac", "Singh", "Patel", "epinephrine", "stopped", "Restarted", "pdf", "Phoned"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["SafetyAlertCreated", "SafetyAlertChanged", "SafetyAlertAcknowledged", "SafetyAlertResolved", "SafetyAlertReopened", "ClearanceRequested", "ClearanceReceived", "ClearanceDocumentAttached", "ClearanceResolved"].map((t) => [t, by(t).length]));
});
