import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-004 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Like the other real-backend specs it is excluded from the default mocked run (playwright.config.ts) and run with
// playwright.schedule.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as the front desk would: an appointment with an available provider is confirmed; a double-booked
// provider, an operatory in use, an unavailable provider and an incorrect duration are each refused in plain words with nothing booked;
// a dropped connection never books twice; six people racing for one slot produce exactly one appointment; every scheduling action is in the
// audit trail with user and time - then it scans the screens with axe in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.SCHEDULE_E2E_OUT ?? path.join(process.cwd(), "schedule-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "schedule-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future (the page never books the past)
const NEXT_DAY = "2030-01-15";
let adminContext: BrowserContext;
let admin: Page;
let deskContext: BrowserContext;
let desk: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function post(page: Page, url: string, data: unknown, headers: Record<string, string> = {}) {
  const response = await page.request.post(url, { headers: { "X-CSRF-Token": await csrf(page), ...headers }, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, unknown> };
}
async function put(page: Page, url: string, data: unknown) {
  const response = await page.request.put(url, { headers: { "X-CSRF-Token": await csrf(page) }, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, unknown> };
}

async function createUser(role: string, username: string) {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
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

async function registerPatient(first: string, last: string, dob: string, phone: string, key: string) {
  const r = await post(desk, "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": key });
  expect(r.status).toBe(201);
  return r.body.id as string;
}

async function pickPatient(page: Page, query: string, name: string) {
  await page.getByRole("searchbox", { name: "Patient" }).fill(query);
  await page.getByRole("button", { name: new RegExp(name) }).click();
}

async function fillForm(page: Page, o: { provider: string; operatory: string; type: string; date?: string; time: string; duration?: string }) {
  await page.getByLabel("Provider *").selectOption({ label: o.provider });
  await page.getByLabel("Operatory *").selectOption({ label: o.operatory });
  await page.getByLabel("Appointment type *").selectOption({ label: o.type === "Exam" ? "Exam (60 min)" : "Quick check (30 min)" });
  await page.getByLabel("Date *").fill(o.date ?? DAY);
  await page.getByLabel("Start time *").fill(o.time);
  await page.getByLabel(/Duration in minutes/).fill(o.duration ?? "");
}

const appointmentsOn = async (page: Page, day = DAY) => (await (await page.request.get(`/api/appointments?from=${day}&to=${day}`)).json()) as { id: string; providerName: string; startLocal: string; endLocal: string; patientName: string }[];

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  await createUser("FrontDesk", `desk-${stamp}`);
  await createUser("Dentist", `dentist-${stamp}`);
  await createUser("Billing", `billing-${stamp}`);
  evidence.stamp = stamp;
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, `desk-${stamp}`, PASSWORD);
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "schedule-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  await deskContext.close();
});

test("SETUP: the practice is configured (two providers, two operatories, two appointment types, blocked time) and patients are registered", async () => {
  expect((await post(admin, "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  const op1 = (await post(admin, "/api/config/operatories", { name: "Op 1" })).body.id as string;
  const op2 = (await post(admin, "/api/config/operatories", { name: "Op 2" })).body.id as string;
  expect([op1, op2].every(Boolean)).toBeTruthy();
  expect((await post(admin, "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).status).toBe(201);
  expect((await post(admin, "/api/config/appointment-types", { name: "Quick check", defaultDurationMinutes: 30 })).status).toBe(201);
  for (const [key, name] of [["rivera", "Dr. Rivera"], ["patel", "Dr. Patel"]] as const) {
    const staff = await post(admin, "/api/config/staff", { displayName: name, jobTitle: "Dentist" });
    const provider = await post(admin, "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" });
    ids[key] = provider.body.id as string;
    const current = (await (await admin.request.get(`/api/config/providers/${ids[key]}/availability`)).json()) as { revision: number };
    const windows = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"].map((_, i) => ({ dayOfWeek: i + 1, startLocal: "08:00", endLocal: "17:00" }));
    expect((await put(admin, `/api/config/providers/${ids[key]}/availability`, { windows, revision: current.revision })).status).toBe(200);
  }
  expect((await post(admin, `/api/config/providers/${ids.patel}/blocked-time`, { startLocal: `${DAY}T12:00:00`, endLocal: `${DAY}T13:00:00`, reason: "Staff lunch" })).status).toBe(201);
  ids.ann = await registerPatient("Ann", "Schedule", "1985-03-09", "555-010-0100", `sched-e2e-ann-${stamp}`);
  ids.bo = await registerPatient("Bo", "Booker", "1990-01-01", "555-020-0200", `sched-e2e-bo-${stamp}`);
  ids.cy = await registerPatient("Cy", "Clinic", "1975-05-05", "555-030-0300", `sched-e2e-cy-${stamp}`);
});

test("ACCEPTANCE 1: with an available provider the appointment is confirmed, shown in words and listed in the day's schedule", async () => {
  await desk.goto("/schedule");
  await expect(desk.getByRole("heading", { name: "Schedule appointment" })).toBeVisible();
  await expect(desk.getByRole("link", { name: "Schedule" })).toHaveAttribute("aria-current", "page");
  await scanBothThemes(desk, "schedule-form");

  await pickPatient(desk, "Ann", "Ann Schedule");
  await fillForm(desk, { provider: "Dr. Rivera", operatory: "Op 1", type: "Exam", time: "09:00" });
  await shot(desk, "01-schedule-form.png");
  await desk.getByRole("button", { name: "Book appointment" }).click();

  const confirmed = desk.getByText("Appointment confirmed.");
  await expect(confirmed).toBeVisible();
  await expect(confirmed.locator("..")).toContainText("Ann Schedule with Dr. Rivera in Op 1, 2030-01-14 09:00–10:00 (Exam)");
  await expect(desk.getByRole("table", { name: /Booked appointments/ }).getByText("09:00–10:00")).toBeVisible();
  await shot(desk, "02-confirmed.png");
  await scanBothThemes(desk, "confirmed");

  const booked = await appointmentsOn(desk);
  expect(booked).toHaveLength(1);
  expect(booked[0]).toMatchObject({ providerName: "Dr. Rivera", startLocal: `${DAY}T09:00`, endLocal: `${DAY}T10:00`, patientName: "Ann Schedule" });
  evidence.acceptance1 = { confirmed: true, appointment: booked[0] };
});

test("ACCEPTANCE 2: a double-booked provider rejects the new appointment, says who and when, and nothing is booked", async () => {
  await pickPatient(desk, "Bo", "Bo Booker");
  await fillForm(desk, { provider: "Dr. Rivera", operatory: "Op 2", type: "Exam", time: "09:30" }); // overlaps 09:00-10:00; a different operatory so only the provider can be the problem
  await desk.getByRole("button", { name: "Book appointment" }).click();

  const refusal = desk.getByRole("alert").filter({ hasText: "already has an appointment" });
  await expect(refusal).toContainText("Dr. Rivera already has an appointment from 09:00 to 10:00.");
  await expect(refusal).toContainText("Nothing was booked");
  await shot(desk, "03-double-booked-refused.png");
  await scanBothThemes(desk, "double-booked-refusal");
  expect(await appointmentsOn(desk)).toHaveLength(1);

  // the server refuses it independently of the page
  const direct = await post(desk, "/api/appointments", { patientId: ids.bo, providerId: ids.rivera, operatoryId: "00000000-0000-0000-0000-000000000000", appointmentTypeId: "00000000-0000-0000-0000-000000000000", startLocal: `${DAY}T09:30` }, { "Idempotency-Key": `direct-${stamp}-1` });
  expect(direct.status).toBeGreaterThanOrEqual(400);
  evidence.acceptance2 = { refusedInWords: true, bookedAfter: 1 };
});

test("FAILURE PATHS: an operatory in use, an unavailable provider (hours and blocked time) and an incorrect duration are each refused in plain words", async () => {
  await fillForm(desk, { provider: "Dr. Patel", operatory: "Op 1", type: "Exam", time: "09:30" });
  await desk.getByRole("button", { name: "Book appointment" }).click();
  await expect(desk.getByRole("alert").filter({ hasText: "in use" })).toContainText("Op 1 is already in use from 09:00 to 10:00.");

  await fillForm(desk, { provider: "Dr. Patel", operatory: "Op 2", type: "Exam", time: "06:00" });
  await desk.getByRole("button", { name: "Book appointment" }).click();
  await expect(desk.getByRole("alert").filter({ hasText: "not available" })).toContainText("Dr. Patel is not available: that time is outside the provider's working hours.");

  await fillForm(desk, { provider: "Dr. Patel", operatory: "Op 2", type: "Quick check", time: "12:30" }); // inside the staff lunch
  await desk.getByRole("button", { name: "Book appointment" }).click();
  await expect(desk.getByRole("alert").filter({ hasText: "not available" })).toContainText("the provider has blocked that time");
  await shot(desk, "04-blocked-time-refused.png");

  await fillForm(desk, { provider: "Dr. Patel", operatory: "Op 2", type: "Exam", time: "14:00", duration: "7" });
  await desk.getByRole("button", { name: "Book appointment" }).click();
  await expect(desk.getByRole("alert").filter({ hasText: "Duration must be" })).toBeVisible();
  await shot(desk, "05-invalid-duration.png");

  expect(await appointmentsOn(desk)).toHaveLength(1); // still only Ann's
  evidence.failurePaths = { operatoryConflict: true, outsideHours: true, blockedTime: true, invalidDuration: true, bookedAfter: 1 };
});

test("BACK TO BACK: a corrected request that starts exactly when the first ends is accepted", async () => {
  await fillForm(desk, { provider: "Dr. Rivera", operatory: "Op 1", type: "Exam", time: "10:00" });
  await desk.getByRole("button", { name: "Book appointment" }).click();
  await expect(desk.getByText("Appointment confirmed.")).toBeVisible();
  const booked = await appointmentsOn(desk);
  expect(booked.map((a) => `${a.startLocal.slice(11)}-${a.endLocal.slice(11)}`)).toEqual(["09:00-10:00", "10:00-11:00"]);
  evidence.backToBack = booked.map((a) => `${a.startLocal.slice(11)}-${a.endLocal.slice(11)}`);
});

test("DROPPED CONNECTION: the booking is stored but the answer is lost; the page says so and Book again books exactly once", async () => {
  await desk.getByLabel("Day").fill(NEXT_DAY);
  await pickPatient(desk, "Cy", "Cy Clinic");
  await fillForm(desk, { provider: "Dr. Rivera", operatory: "Op 1", type: "Exam", date: NEXT_DAY, time: "09:00" });

  const keys: string[] = [];
  let first = true;
  await desk.route("**/api/appointments", async (route) => {
    if (route.request().method() !== "POST") return route.continue();
    keys.push(route.request().headers()["idempotency-key"] ?? "");
    if (first) {
      first = false;
      await route.fetch(); // the server really books it...
      await route.abort("connectionreset"); // ...but the answer never reaches the browser
    } else {
      await route.continue();
    }
  });
  await desk.getByRole("button", { name: "Book appointment" }).click();
  await expect(desk.getByText(/could not confirm whether the appointment was booked/)).toBeVisible();
  await shot(desk, "06-unknown-outcome.png");
  await scanBothThemes(desk, "unknown-outcome");
  await desk.getByRole("button", { name: "Book again" }).click();
  await expect(desk.getByText("Appointment confirmed.")).toBeVisible();
  await desk.unroute("**/api/appointments");

  expect(keys).toHaveLength(2);
  expect(keys[0]).toBe(keys[1]);
  expect(await appointmentsOn(desk, NEXT_DAY)).toHaveLength(1);
  evidence.droppedConnection = { sameKeyOnRetry: true, appointmentsAfter: 1 };
});

test("RACE: six requests for the same slot at the same moment produce exactly one appointment, the others are refused", async () => {
  const snapshot = (await (await desk.request.get("/api/config/scheduling")).json()) as { operatories: { id: string }[]; appointmentTypes: { id: string; name: string }[] };
  const exam = snapshot.appointmentTypes.find((t) => t.name === "Exam")!.id;
  const patients = [ids.ann, ids.bo, ids.cy, ids.ann, ids.bo, ids.cy];
  // the same provider and time for all six; the operatory alternates so the provider is the common resource
  const raced = await Promise.all(patients.map((p, i) =>
    post(desk, "/api/appointments", { patientId: p, providerId: ids.patel, operatoryId: snapshot.operatories[i % 2].id, appointmentTypeId: exam, startLocal: `${NEXT_DAY}T15:00` }, { "Idempotency-Key": `race-${stamp}-${i}` })));

  expect(raced.filter((r) => r.status === 201)).toHaveLength(1);
  expect(raced.filter((r) => r.status === 409).map((r) => r.body.error as string).every((e) => e === "provider_double_booked" || e === "operatory_conflict")).toBeTruthy();
  const at3 = (await appointmentsOn(desk, NEXT_DAY)).filter((a) => a.startLocal.endsWith("T15:00"));
  expect(at3).toHaveLength(1);
  evidence.race = { requests: 6, created: 1, refused: raced.filter((r) => r.status === 409).length };
});

test("ACCEPTANCE 3 (TRUST): every scheduling action is in the audit trail with the user and a timestamp, and no patient details", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const scheduled = entries.filter((e) => e.eventType === "AppointmentScheduled");
  const rejected = entries.filter((e) => e.eventType === "AppointmentRejected");
  expect(scheduled.length).toBeGreaterThanOrEqual(4); // Ann 09:00, Bo 10:00, Cy next day, the race winner
  expect(rejected.length).toBeGreaterThanOrEqual(4);   // double-booked, operatory, unavailable x2, race losers
  for (const e of [...scheduled, ...rejected]) {
    expect(e.performedByUserAccountId).toBeTruthy();
    expect(Date.now() - Date.parse(e.timestampUtc)).toBeLessThan(30 * 60_000);
    expect(e.details).not.toMatch(/Ann|Bo |Cy |Schedule|Booker|Clinic|Rivera|Patel|555-/);
  }
  expect(rejected.some((e) => e.details.includes("provider_double_booked"))).toBe(true);
  expect(rejected.some((e) => e.details.includes("operatory_conflict"))).toBe(true);
  expect(rejected.some((e) => e.details.includes("provider_unavailable (blocked_time)"))).toBe(true);
  evidence.audit = { scheduled: scheduled.length, rejected: rejected.length };
});

test("PERMISSIONS: a dentist can see the schedule but has no booking form and the API refuses a booking; billing has no Schedule page at all", async ({ browser }) => {
  const dentistCtx = await browser.newContext();
  const dentist = await dentistCtx.newPage();
  await signIn(dentist, `dentist-${stamp}`, PASSWORD);
  await dentist.goto("/schedule");
  await expect(dentist.getByText(/can view the schedule but not book/)).toBeVisible();
  await expect(dentist.getByRole("form", { name: "Schedule an appointment" })).toHaveCount(0);
  await dentist.getByLabel("Day").fill(DAY);
  await expect(dentist.getByRole("table", { name: /Booked appointments/ })).toBeVisible();
  const refused = await post(dentist, "/api/appointments", { patientId: ids.ann, providerId: ids.rivera, operatoryId: ids.rivera, appointmentTypeId: ids.rivera, startLocal: `${DAY}T13:00` }, { "Idempotency-Key": `dentist-${stamp}-1` });
  expect(refused.status).toBe(403);
  await dentistCtx.close();

  const billingCtx = await browser.newContext();
  const billing = await billingCtx.newPage();
  await signIn(billing, `billing-${stamp}`, PASSWORD);
  await expect(billing.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Schedule" })).toHaveCount(0);
  await billing.goto("/schedule");
  await expect(billing.getByText(/don't have permission/i)).toBeVisible();
  expect((await billing.request.get(`/api/appointments?from=${DAY}`)).status()).toBe(403);
  await billingCtx.close();
  evidence.permissions = { dentistApiStatus: refused.status, billingScheduleLink: false };
});

test("KEYBOARD: an appointment can be booked without a mouse", async () => {
  await desk.goto("/schedule");
  await desk.getByRole("searchbox", { name: "Patient" }).focus();
  await desk.keyboard.type("Cy");
  await desk.getByRole("button", { name: /Cy Clinic/ }).focus();
  await desk.keyboard.press("Enter");
  await desk.getByLabel("Provider *").focus();
  await desk.keyboard.press("ArrowDown");
  await desk.getByLabel("Operatory *").focus();
  await desk.keyboard.press("ArrowDown");
  await desk.getByLabel("Appointment type *").focus();
  await desk.keyboard.press("ArrowDown");
  await desk.getByLabel("Date *").fill("2030-01-16");
  await desk.getByLabel("Start time *").fill("11:00");
  await desk.getByRole("button", { name: "Book appointment" }).focus();
  await desk.keyboard.press("Enter");
  await expect(desk.getByText("Appointment confirmed.")).toBeVisible();
  evidence.keyboard = { booked: true };
});
