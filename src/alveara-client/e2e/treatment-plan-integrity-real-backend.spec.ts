import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N023 (brief section 9 step 5), un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, on a fresh database.
// Run ALONE with playwright.treatment-plan-integrity.config.ts against an API the caller started (scripts/Invoke-RealBackendWalkthrough.ps1). One patient's plan, walked in order:
// (1) the dentist records the patient's acceptance; the procedure says "Not booked.";
// (2) the front desk books an appointment and ticks the accepted procedure in the same form (OQ-1): one save links it, and the plan says Booked (DR-1);
// (3) the appointment is cancelled: the plan says so and the procedure is not booked again;
// (4) a target date in the past is saved only after confirming, and shows Overdue (PD-15); it never books anything;
// (5) the plan is finished with completed care: its state comes from the server (Closed with completed care), it is locked, and only "Revise anyway" is offered; the API refuses a change (CR-3, addendum 01);
// (6) the patient copy carries the practice's name, address and phone (F-183).

test.describe.configure({ mode: "serial" });

const OUT = process.env.TREATMENT_PLAN_INTEGRITY_E2E_OUT ?? path.join(process.cwd(), "treatment-plan-integrity-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "integrity-pass-1!";
const DAY = "2030-01-14";            // a Monday far in the future: the booking is in the future on the real clock
const PAST = "2020-01-14";           // a target date that has passed
const PRACTICE = { name: "Alder Street Dental", phone: "555-010-0999", addressLine: "12 Alder Street, Austin, TX 78701" };
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
let dentistName = "";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
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

const statusLine = (page: Page) => page.locator("p.alv-clinical__status").first();
const card = (page: Page) => page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: /Crown plan/ }) }).first();
const row = (page: Page, code: string) => page.locator("li.alv-tp__item", { has: page.getByText(code, { exact: true }) });
const thePlan = async (page: Page) => (await api(page, "get", `/api/patients/${ids.ann}/treatment-plans`)).body.plans[0] as any;

async function gotoPlan(page: Page) {
  await page.goto(`/patients/${ids.ann}/treatment-plan`);
  await expect(page.getByRole("heading", { name: "Plans on record" })).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "FrontDesk"]) {
    const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    if (role === "Dentist") {
      dentistName = `Dr. Dana Dentist ${stamp}`;
      const staff = await api(admin, "post", "/api/config/staff", { displayName: dentistName, userAccountId: userId });
      expect(staff.status, "dentist staff profile").toBe(201);
      const provider = await api(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" });
      expect([200, 201], "dentist provider").toContain(provider.status);
      ids.provider = provider.body.id;
    }
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "treatment-plan-integrity-e2e.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: the practice (name, address, phone, one operatory, one type, the dentist's hours), a patient with an encounter and a diagnosis, and a two-procedure plan", async () => {
  expect((await api(admin, "put", "/api/config/practice", { ...PRACTICE, rowVersion: null })).status, "practice details").toBe(200);
  expect((await api(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op = (await api(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id;
  ids.exam = (await api(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id;
  const current = (await api(admin, "get", `/api/config/providers/${ids.provider}/availability`)).body as { revision: number };
  const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
  expect((await api(admin, "put", `/api/config/providers/${ids.provider}/availability`, { windows, revision: current.revision })).status).toBe(200);

  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const p = await api(front, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `n023-ann-${stamp}` });
  expect(p.status).toBe(201);
  ids.ann = p.body.id;
  const e = await api(dentist, "post", `/api/patients/${ids.ann}/encounters`, {}, { "Idempotency-Key": `n023-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  ids.encounter = e.body.id;
  const d = await api(dentist, "post", `/api/patients/${ids.ann}/diagnoses`, { idempotencyKey: `n023-dx-${stamp}`, encounterId: ids.encounter, label: "Fractured cusp", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(d.status).toBe(200);
  const procedures: Record<string, string> = {};
  for (const [code, description, fee] of [["CROWN-1", "Crown", 100], ["POL-1", "Polish", 30]] as const) {
    const r = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code, description, category: "Restorative", scope: "WholeMouth", fee });
    expect(r.status).toBe(200);
    procedures[code] = r.body.summary.id;
  }
  const plan = await api(dentist, "post", `/api/patients/${ids.ann}/treatment-plans`, {
    idempotencyKey: `n023-plan-${stamp}`, title: "Crown plan",
    items: [{ diagnosisId: d.body.id, procedureId: procedures["CROWN-1"], toothKey: null, surface: null }, { diagnosisId: d.body.id, procedureId: procedures["POL-1"], toothKey: null, surface: null }],
  });
  expect(plan.status).toBe(200);
  ids.plan = plan.body.id;
});

test("ACCEPT: the dentist records the patient's acceptance; the procedure is accepted and says it is not booked", async () => {
  const page = sessions.Dentist.page;
  await gotoPlan(page);
  await expect(card(page).getByTestId("plan-state")).toHaveText("Proposed");
  await row(page, "CROWN-1").getByRole("button", { name: "Accept CROWN-1" }).click();
  const f = page.getByRole("form", { name: "Accept CROWN-1" });
  await f.getByLabel(/^Who gave this answer/).fill("Ann Alder (patient)");
  await f.getByRole("button", { name: "Record acceptance" }).click();
  await expect(statusLine(page)).toContainText("CROWN-1 accepted.");
  await expect(row(page, "CROWN-1").getByTestId("booking")).toHaveText("Not booked.");
  await shot(page, "01-accepted-not-booked.png");
  evidence.accepted = { planState: (await thePlan(page)).planState };
});

test("BOOK (OQ-1, DR-1): the front desk books an appointment for the accepted procedure in one save; the plan says Booked", async () => {
  const desk = sessions.FrontDesk.page;
  await desk.goto("/calendar");
  await expect(desk.getByRole("heading", { name: "Calendar", exact: true })).toBeVisible();
  await desk.getByLabel("Date", { exact: true }).fill(DAY);
  await desk.getByRole("button", { name: "New appointment" }).click();
  const form = desk.getByRole("dialog").getByRole("form", { name: "New appointment" });
  await form.getByRole("searchbox", { name: "Patient" }).fill("Ann");
  await form.getByRole("button", { name: /Ann Alder/ }).click();
  const treatment = form.getByRole("group", { name: "For accepted treatment (optional)" });
  await expect(treatment.getByRole("checkbox")).toHaveCount(1);                                            // only the accepted procedure is offered
  const choice = treatment.getByRole("checkbox", { name: /CROWN-1 Crown/ });
  await expect(treatment).not.toContainText("Crown plan");                                                  // no plan title, no diagnosis
  await expect(treatment).not.toContainText("Fractured cusp");
  await choice.check();
  await form.getByLabel("Appointment type *").selectOption({ label: "Exam (60 min)" });
  await form.getByLabel("Provider *").selectOption({ label: dentistName });
  await form.getByLabel("Operatory *").selectOption({ label: "Op 1" });
  await form.getByLabel("Date *").fill(DAY);
  await form.getByLabel("Start time *").fill("09:00");
  await shot(desk, "02-booking-with-treatment.png");
  await form.getByRole("button", { name: "Book appointment" }).click();

  await expect.poll(async () => (await api(desk, "get", `/api/appointments?from=${DAY}&to=${DAY}&includeAll=true`)).body.length).toBe(1);
  const appointment = (await api(desk, "get", `/api/appointments?from=${DAY}&to=${DAY}&includeAll=true`)).body[0];
  ids.appointment = appointment.id;
  ids.appointmentVersion = appointment.rowVersion;
  const books = (await api(desk, "get", `/api/appointments/${ids.appointment}/treatment-items`)).body;
  const items = (books.items ?? books) as any[];
  expect(items.map((i) => i.procedureCode)).toEqual(["CROWN-1"]);                                           // linked in the same save

  const page = sessions.Dentist.page;
  await gotoPlan(page);
  await expect(row(page, "CROWN-1").getByTestId("booking")).toContainText("Booked:");
  await expect(row(page, "CROWN-1").getByTestId("booking")).toContainText(`with ${dentistName}`);
  await shot(page, "03-plan-booked.png");
  evidence.booked = { appointmentId: ids.appointment, linked: items.map((i) => i.procedureCode) };
});

test("CANCEL (DR-1): cancelling the appointment leaves the procedure not booked, and the plan says why", async () => {
  const desk = sessions.FrontDesk.page;
  const cancelled = await api(desk, "post", `/api/appointments/${ids.appointment}/cancel`, { category: "patient", note: null, rowVersion: ids.appointmentVersion });
  expect(cancelled.status).toBe(200);
  const page = sessions.Dentist.page;
  await gotoPlan(page);
  await expect(row(page, "CROWN-1").getByTestId("booking")).toContainText("was cancelled, so it is not booked.");
  const bookable = (await api(desk, "get", `/api/patients/${ids.ann}/bookable-treatment`)).body;
  expect(((bookable.items ?? bookable) as any[]).map((i) => i.procedureCode)).toEqual(["CROWN-1"]);         // it can be booked again
  await shot(page, "04-cancelled-not-booked.png");
  evidence.cancelled = { bookableAgain: true };
});

test("TARGET DATE (PD-15): a past target date is saved only after confirming, shows Overdue, and books nothing", async () => {
  const page = sessions.Dentist.page;
  await gotoPlan(page);
  await row(page, "CROWN-1").getByRole("button", { name: "Set target date of CROWN-1" }).click();
  const f = page.getByRole("form", { name: "Target date of CROWN-1" });
  await f.getByLabel("Target date").fill(PAST);
  await f.getByRole("button", { name: "Save target date" }).click();
  await expect(f.getByRole("alert")).toHaveText("That date has passed. Save it anyway as an overdue target date?");
  expect((await thePlan(page)).items[0].plannedDate).toBeNull();                                           // nothing saved before the confirmation
  await f.getByRole("button", { name: "Save as overdue" }).click();
  await expect(statusLine(page)).toContainText("Target date for CROWN-1 saved (overdue). It does not book an appointment.");
  await expect(row(page, "CROWN-1")).toContainText("Overdue");
  await expect(row(page, "CROWN-1").getByTestId("booking")).toContainText("not booked");                  // a target date is not a booking
  await shot(page, "05-overdue-target.png");
  const item = (await thePlan(page)).items[0];
  expect([item.plannedDate, item.targetOverdue]).toEqual([PAST, true]);
  evidence.targetDate = { plannedDate: item.plannedDate, targetOverdue: item.targetOverdue };
});

test("FINISHED (PD-14, CR-3, addendum 01): with POL-1 declined and CROWN-1 completed the plan is Closed with completed care, so it is locked; only Revise anyway is offered and the API refuses a change", async () => {
  const page = sessions.Dentist.page;
  let plan = await thePlan(page);
  const pol = plan.items.find((i: any) => i.procedureCode === "POL-1");
  expect((await api(page, "post", `/api/treatment-plans/${plan.id}/items/${pol.id}/decision`, { decision: "Declined", reason: "Not needed now", decidedByName: "Ann Alder (patient)", rowVersion: plan.rowVersion })).status).toBe(200);
  plan = await thePlan(page);
  const crown = plan.items.find((i: any) => i.procedureCode === "CROWN-1");
  const done = await api(page, "post", `/api/treatment-plans/${plan.id}/complete-procedures`, {
    idempotencyKey: `n023-complete-${stamp}`, rowVersion: plan.rowVersion, encounterId: ids.encounter, providerId: ids.provider, performedAtUtc: new Date().toISOString(), itemIds: [crown.id],
  });
  expect(done.status, JSON.stringify(done.body)).toBe(200);

  await gotoPlan(page);
  const c = card(page);
  await expect(c.getByTestId("plan-state")).toHaveText("Closed - all remaining procedures are completed or declined");
  await expect(c).toContainText("This plan contains completed care and is finished, so it can no longer be changed. A correction is made with Revise anyway.");
  await expect(c.getByRole("button", { name: "Revise anyway…" })).toBeVisible();
  await expect(c.getByRole("button", { name: /Accept|Decline|target date|appointment|Withdraw|Rename|Add procedure|Revise plan/ })).toHaveCount(0);
  await shot(page, "06-finished-locked.png");

  plan = await thePlan(page);
  expect([plan.planState, plan.closedReason, plan.locked]).toEqual(["Closed", "CompletedOrDeclined", true]);
  const refused = await api(page, "post", `/api/treatment-plans/${plan.id}/withdraw`, { reason: "Mistake", rowVersion: plan.rowVersion });
  expect([refused.status, refused.body.error]).toEqual([409, "plan_finished"]);
  expect((await thePlan(page)).status).not.toBe("Withdrawn");                                              // nothing changed
  evidence.finished = { planState: plan.planState, closedReason: plan.closedReason, locked: plan.locked, withdraw: [refused.status, refused.body.error] };
});

test("PATIENT COPY (F-183): the copy carries the practice's name, address and phone", async () => {
  const page = sessions.Dentist.page;
  await gotoPlan(page);
  await card(page).getByRole("button", { name: "Show patient copy" }).click();
  const header = page.getByRole("region", { name: "Patient copy of Crown plan" }).getByTestId("copy-practice");
  await expect(header).toContainText(PRACTICE.name);
  await expect(header).toContainText(PRACTICE.addressLine);
  await expect(header).toContainText(`Phone: ${PRACTICE.phone}`);
  await shot(page, "07-patient-copy-header.png");
  evidence.patientCopy = { practiceHeader: true };
});
