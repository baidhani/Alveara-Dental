import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-004-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.calendar.config.ts against an API the caller started
// (see docs/testing/REAL_BACKEND_E2E.md). It walks the story's acceptance items as the front desk would: the calendar shows the practice's real
// provider/operatory assignments; every conflict (provider, operatory, patient, availability, blocked time) is explained and nothing is booked;
// appointments are rescheduled, cancelled and marked no-show and stay visible and historical; a stale edit by a second user is reported rather than
// applied; and racing requests never double-book - then it scans the screens with axe in both themes.
//
// One arrangement uses the database directly: the API (correctly) refuses to book a start in the past, so the no-show demonstration moves one booked
// appointment ten years back with a single SQL UPDATE of its times, exactly as a long-past appointment would look.

test.describe.configure({ mode: "serial" });

const OUT = process.env.CALENDAR_E2E_OUT ?? path.join(process.cwd(), "calendar-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "calendar-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future
const NEXT = "2030-01-15";
const PAST_DAY = "2020-01-14";
let adminContext: BrowserContext;
let admin: Page;
let deskContext: BrowserContext;
let desk: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function call(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
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
  const r = await call(desk, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": key });
  expect(r.status).toBe(201);
  return r.body.id as string;
}

type Appt = { id: string; rowVersion: string; startLocal: string; endLocal: string; providerName: string; operatoryName: string; status: string; patientName: string };

async function book(page: Page, patient: string, provider: string, operatory: string, startLocal: string, key: string, type = "exam", duration?: number) {
  const r = await call(page, "post", "/api/appointments", { patientId: patient, providerId: provider, operatoryId: operatory, appointmentTypeId: ids[type], startLocal, durationMinutes: duration }, { "Idempotency-Key": key });
  expect(r.status, `book ${startLocal}: ${JSON.stringify(r.body)}`).toBe(201);
  return r.body as unknown as Appt;
}

const calendarOn = async (page: Page, day = DAY) => (await (await page.request.get(`/api/appointments?from=${day}&to=${day}&includeAll=true`)).json()) as Appt[];

async function gotoCalendar(page: Page, day: string, view: "Day" | "Week" = "Day") {
  await page.goto("/calendar");
  await expect(page.getByRole("heading", { name: "Calendar", exact: true })).toBeVisible();
  await page.getByLabel("Date", { exact: true }).fill(day);
  if (view === "Week") await page.getByRole("button", { name: "Week" }).click();
  await expect(page.locator(".cal")).toBeVisible();
}

const blockOf = (page: Page, id: string) => page.locator(`[data-appointment="${id}"]`);
const drawerOf = (page: Page) => page.getByRole("dialog");

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the arrangement for the no-show demonstration needs it)");
  // -I turns quoted identifiers ON, which the filtered indexes on Appointments require for an UPDATE
  execFileSync("sqlcmd", ["-S", "(localdb)\\MSSQLLocalDB", "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  await createUser("FrontDesk", `desk-${stamp}`);
  await createUser("FrontDesk", `desk2-${stamp}`);
  await createUser("Dentist", `dentist-${stamp}`);
  await createUser("Billing", `billing-${stamp}`);
  evidence.stamp = stamp;
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, `desk-${stamp}`, PASSWORD);
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "calendar-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  await deskContext.close();
});

test("SETUP: the practice (two providers, two operatories, two types, a blocked lunch) and six patients", async () => {
  expect((await call(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op1 = (await call(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id as string;
  ids.op2 = (await call(admin, "post", "/api/config/operatories", { name: "Op 2" })).body.id as string;
  ids.exam = (await call(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id as string;
  ids.quick = (await call(admin, "post", "/api/config/appointment-types", { name: "Quick check", defaultDurationMinutes: 30 })).body.id as string;
  for (const [key, name] of [["rivera", "Dr. Rivera"], ["patel", "Dr. Patel"]] as const) {
    const staff = await call(admin, "post", "/api/config/staff", { displayName: name, jobTitle: "Dentist" });
    ids[key] = (await call(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" })).body.id as string;
    const current = (await (await admin.request.get(`/api/config/providers/${ids[key]}/availability`)).json()) as { revision: number };
    const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
    expect((await call(admin, "put", `/api/config/providers/${ids[key]}/availability`, { windows, revision: current.revision })).status).toBe(200);
  }
  expect((await call(admin, "post", `/api/config/providers/${ids.patel}/blocked-time`, { startLocal: `${DAY}T12:00:00`, endLocal: `${DAY}T13:00:00`, reason: "Staff lunch" })).status).toBe(201);
  const people: [string, string, string, string][] = [["Ann", "Calendar", "1985-03-09", "555-010-0100"], ["Bo", "Booker", "1990-01-01", "555-020-0200"], ["Cy", "Clinic", "1975-05-05", "555-030-0300"], ["Di", "Dental", "1980-07-07", "555-040-0400"], ["Ed", "Early", "1970-02-02", "555-050-0500"], ["Flo", "Frame", "1965-08-08", "555-060-0600"]];
  for (const [first, last, dob, phone] of people) ids[first.toLowerCase()] = await registerPatient(first, last, dob, phone, `cal-e2e-${first}-${stamp}`);
});

test("CALENDAR: the day view shows the practice's real provider and operatory assignments at the right time and size, with working hours and filters", async () => {
  ids.a1 = (await book(desk, ids.ann, ids.rivera, ids.op1, `${DAY}T09:00`, `cal-a1-${stamp}`)).id;
  ids.a2 = (await book(desk, ids.bo, ids.patel, ids.op2, `${DAY}T10:30`, `cal-a2-${stamp}`, "quick")).id;
  await gotoCalendar(desk, DAY);

  await expect(desk.getByRole("heading", { name: "Monday 2030-01-14" })).toBeVisible();
  const columns = desk.locator(".cal__column");
  await expect(columns).toHaveCount(2);
  const riveraBlock = desk.locator(".cal__column", { has: desk.locator(".cal__head", { hasText: "Dr. Rivera" }) }).locator(`[data-appointment="${ids.a1}"]`);
  const patelBlock = desk.locator(".cal__column", { has: desk.locator(".cal__head", { hasText: "Dr. Patel" }) }).locator(`[data-appointment="${ids.a2}"]`);
  await expect(riveraBlock).toBeVisible();
  await expect(patelBlock).toBeVisible();
  await expect(riveraBlock).toHaveAttribute("aria-label", /09:00 to 10:00, Ann Calendar, Exam, Dr\. Rivera, Op 1, Scheduled/);
  await expect(patelBlock).toHaveAttribute("aria-label", /10:30 to 11:00, Bo Booker, Quick check, Dr\. Patel, Op 2, Scheduled/);
  const style = async (loc: typeof riveraBlock) => ({ top: await loc.evaluate((e) => (e as HTMLElement).style.top), height: await loc.evaluate((e) => (e as HTMLElement).style.height) });
  expect(await style(riveraBlock)).toEqual({ top: "120px", height: "60px" });   // 09:00 is 120 minutes after the 07:00 axis start
  expect(await style(patelBlock)).toEqual({ top: "210px", height: "30px" });    // 10:30, 30 minutes
  await expect(desk.locator('[data-working="08:00-17:00"]')).toHaveCount(2);     // each provider's real weekly hours
  await shot(desk, "01-day-view.png");
  await scanBothThemes(desk, "day-view");

  await desk.getByLabel("Provider", { exact: true }).selectOption({ label: "Dr. Patel" });
  await expect(desk.locator(".cal__column")).toHaveCount(1);
  await expect(desk.locator(".cal__block")).toHaveCount(1);
  await desk.getByLabel("Provider", { exact: true }).selectOption({ label: "All providers" });
  await desk.getByLabel("Operatory", { exact: true }).selectOption({ label: "Op 1" });
  await expect(desk.locator(".cal__block")).toHaveCount(1);
  await expect(blockOf(desk, ids.a1)).toBeVisible();
  await desk.getByLabel("Operatory", { exact: true }).selectOption({ label: "All operatories" });
  evidence.calendar = { providerColumns: 2, a1: await style(riveraBlock), a2: await style(patelBlock), filtersWork: true };
});

test("BOOK + CONFLICTS: the drawer books an appointment; provider, operatory, patient, hours and blocked-time conflicts are each explained and nothing is booked", async () => {
  async function tryBooking(patient: string, search: string, provider: string, operatory: string, time: string, type = "Exam", date = DAY) {
    await desk.getByRole("button", { name: "New appointment" }).click();
    const form = drawerOf(desk).getByRole("form", { name: "New appointment" });
    await form.getByRole("searchbox", { name: "Patient" }).fill(search);
    await form.getByRole("button", { name: new RegExp(patient) }).click();
    await form.getByLabel("Appointment type *").selectOption({ label: type === "Exam" ? "Exam (60 min)" : "Quick check (30 min)" });
    await form.getByLabel("Provider *").selectOption({ label: provider });
    await form.getByLabel("Operatory *").selectOption({ label: operatory });
    await form.getByLabel("Date *").fill(date);
    await form.getByLabel("Start time *").fill(time);
    await form.getByRole("button", { name: "Book appointment" }).click();
    return form;
  }
  const before = (await calendarOn(desk)).length;

  await tryBooking("Cy Clinic", "Cy", "Dr. Rivera", "Op 2", "09:30");                       // provider double-booked
  await expect(drawerOf(desk).getByRole("alert")).toContainText("Dr. Rivera already has an appointment from 09:00 to 10:00.");
  await shot(desk, "02-conflict-provider.png");
  await scanBothThemes(desk, "conflict-explained");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  await tryBooking("Cy Clinic", "Cy", "Dr. Patel", "Op 1", "09:30");                         // operatory in use
  await expect(drawerOf(desk).getByRole("alert")).toContainText("Op 1 is already in use from 09:00 to 10:00.");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  await tryBooking("Ann Calendar", "Ann", "Dr. Patel", "Op 2", "09:30");                     // patient already booked elsewhere (Ann at 09:00 with Dr. Rivera)
  await expect(drawerOf(desk).getByRole("alert")).toContainText("This patient already has an appointment from 09:00 to 10:00.");
  await expect(drawerOf(desk).getByRole("alert")).toContainText("A patient cannot be in two places at once.");
  await shot(desk, "03-conflict-patient.png");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  await tryBooking("Cy Clinic", "Cy", "Dr. Patel", "Op 1", "06:00");                         // outside working hours
  await expect(drawerOf(desk).getByRole("alert")).toContainText("outside the provider's working hours");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  await tryBooking("Cy Clinic", "Cy", "Dr. Patel", "Op 1", "12:30", "Quick check");          // inside the blocked lunch
  await expect(drawerOf(desk).getByRole("alert")).toContainText("the provider has blocked that time");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  expect((await calendarOn(desk)).length).toBe(before); // none of the five attempts booked anything

  await tryBooking("Cy Clinic", "Cy", "Dr. Rivera", "Op 1", "10:00");                        // a real, conflict-free booking, starting exactly when Ann's ends
  await expect(drawerOf(desk).getByRole("heading", { name: "Cy Clinic" })).toBeVisible();
  const booked = (await calendarOn(desk)).find((a) => a.patientName === "Cy Clinic")!;
  ids.a3 = booked.id;
  await expect(blockOf(desk, booked.id)).toBeVisible();
  await shot(desk, "04-booked-in-drawer.png");
  evidence.conflicts = { provider: true, operatory: true, patient: true, outsideHours: true, blockedTime: true, bookedAfterRefusals: before + 1 };
});

test("RESCHEDULE: moving an appointment through the drawer, a refused move, and the history that remembers where it was", async () => {
  await drawerOf(desk).getByRole("button", { name: "Reschedule" }).click();
  let form = drawerOf(desk).getByRole("form", { name: "Reschedule appointment" });
  await form.getByLabel("Provider *").selectOption({ label: "Dr. Patel" });
  await form.getByLabel("Operatory *").selectOption({ label: "Op 2" });
  await form.getByLabel("Start time *").fill("15:00");
  await form.getByRole("button", { name: "Save new time" }).click();
  await expect(drawerOf(desk).getByText("Appointment rescheduled.")).toBeVisible();
  await expect(drawerOf(desk).getByText("2030-01-14 15:00–16:00 (60 min)")).toBeVisible();
  await expect(drawerOf(desk).getByText("Was 2030-01-14 10:00 with Dr. Rivera in Op 1")).toBeVisible();
  await shot(desk, "05-rescheduled-with-history.png");
  await expect(desk.locator(".cal__column", { has: desk.locator(".cal__head", { hasText: "Dr. Patel" }) }).locator(`[data-appointment="${ids.a3}"]`)).toBeVisible(); // now in Dr. Patel's column

  // a refused move: into Bo's 10:30 appointment with Dr. Patel
  await drawerOf(desk).getByRole("button", { name: "Reschedule" }).click();
  form = drawerOf(desk).getByRole("form", { name: "Reschedule appointment" });
  await form.getByLabel("Start time *").fill("10:45");
  await form.getByRole("button", { name: "Save new time" }).click();
  await expect(drawerOf(desk).getByRole("alert")).toContainText("Dr. Patel already has an appointment from 10:30 to 11:00.");
  await shot(desk, "06-reschedule-refused.png");
  expect((await calendarOn(desk)).find((a) => a.id === ids.a3)!.startLocal).toBe(`${DAY}T15:00`); // unchanged
  await form.getByRole("button", { name: "Keep as it is" }).click();
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  evidence.reschedule = { moved: true, historyRemembersPrevious: true, refusedMoveLeftItWhereItWas: true };
});

test("STALE EDIT: a second user moves the appointment first; the first user's change is reported and NOT applied, and Reload shows the real state", async ({ browser }) => {
  const ctx2 = await browser.newContext();
  const desk2 = await ctx2.newPage();
  await signIn(desk2, `desk2-${stamp}`, PASSWORD);

  await blockOf(desk, ids.a3).click();                                    // user 1 opens it (and holds its version)
  await drawerOf(desk).getByRole("button", { name: "Reschedule" }).click();
  const moved = await call(desk2, "put", `/api/appointments/${ids.a3}/reschedule`, { providerId: ids.patel, operatoryId: ids.op2, startLocal: `${DAY}T16:00` , rowVersion: (await calendarOn(desk2)).find((a) => a.id === ids.a3)!.rowVersion });
  expect(moved.status).toBe(200);                                         // user 2 moved it to 16:00

  const form = drawerOf(desk).getByRole("form", { name: "Reschedule appointment" });
  await form.getByLabel("Start time *").fill("14:00");                     // user 1 still thinks it is at 15:00
  await form.getByRole("button", { name: "Save new time" }).click();
  await expect(desk.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(desk, "07-stale-edit.png");
  await scanBothThemes(desk, "stale-edit");
  expect((await calendarOn(desk)).find((a) => a.id === ids.a3)!.startLocal).toBe(`${DAY}T16:00`); // user 2's change stands

  await desk.getByRole("button", { name: /Reload/ }).click();
  await expect(drawerOf(desk).getByText("2030-01-14 16:00–17:00 (60 min)")).toBeVisible();
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  await ctx2.close();
  evidence.staleEdit = { reported: true, otherUsersChangeStood: true };
});

test("CONCURRENT EDITS: six appointments racing into one slot produce one winner; two users rescheduling the same appointment produce one change; no double booking", async () => {
  const patients = [ids.di, ids.ed, ids.flo, ids.ann, ids.bo, ids.cy];
  const appts: Appt[] = [];
  for (const [i, p] of patients.entries()) appts.push(await book(desk, p, ids.rivera, i % 2 === 0 ? ids.op1 : ids.op2, `${NEXT}T${String(8 + i).padStart(2, "0")}:00`, `cal-race-${i}-${stamp}`));

  const results = await Promise.all(appts.map((a, i) =>
    call(desk, "put", `/api/appointments/${a.id}/reschedule`, { providerId: ids.rivera, operatoryId: i % 2 === 0 ? ids.op1 : ids.op2, startLocal: `${NEXT}T15:00`, rowVersion: a.rowVersion })));
  expect(results.filter((r) => r.status === 200)).toHaveLength(1);
  expect(results.filter((r) => r.status === 409).every((r) => ["provider_double_booked", "operatory_conflict", "patient_double_booked", "concurrency_conflict"].includes(r.body.error as string))).toBe(true);
  const day = (await calendarOn(desk, NEXT)).filter((a) => a.status === "Scheduled");
  expect(day.filter((a) => a.startLocal.endsWith("T15:00"))).toHaveLength(1);

  const same = (await calendarOn(desk, NEXT)).find((a) => a.status === "Scheduled" && !a.startLocal.endsWith("T15:00"))!; // any of the five that lost the race above
  const twoWay = await Promise.all(["T14:00", "T16:00"].map((t) => // two free slots, so each move is valid on its own: the loser can only be refused as a stale edit
    call(desk, "put", `/api/appointments/${same.id}/reschedule`, { providerId: ids.rivera, operatoryId: ids.op1, startLocal: `${NEXT}${t}`, rowVersion: same.rowVersion })));
  expect(twoWay.map((r) => r.status).sort()).toEqual([200, 409]);
  expect(twoWay.find((r) => r.status === 409)!.body.error).toBe("concurrency_conflict");
  evidence.concurrency = { sixWayRaceWinners: 1, sameAppointmentTwoWay: twoWay.map((r) => r.status).sort() };
});

test("CANCEL: a cancellation needs a reason, the appointment stays on the calendar marked cancelled (and holds no time), and its history is kept", async () => {
  await gotoCalendar(desk, DAY);
  await blockOf(desk, ids.a3).click();
  await drawerOf(desk).getByRole("button", { name: "Cancel appointment" }).click();
  await drawerOf(desk).getByRole("button", { name: "Confirm cancellation" }).click(); // no reason
  await expect(drawerOf(desk).getByRole("alert")).toContainText("A reason for the cancellation is required.");
  await drawerOf(desk).getByLabel("Reason for cancelling (required)").fill("Patient called to cancel");
  await drawerOf(desk).getByRole("button", { name: "Confirm cancellation" }).click();
  await expect(drawerOf(desk).getByText("Appointment cancelled.")).toBeVisible();
  await expect(drawerOf(desk).getByText("Patient called to cancel", { exact: true }).first()).toBeVisible();
  await expect(drawerOf(desk).getByRole("button", { name: "Reschedule" })).toHaveCount(0);
  await shot(desk, "08-cancelled-drawer.png");
  await scanBothThemes(desk, "cancelled-drawer");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  const cancelled = blockOf(desk, ids.a3);
  await expect(cancelled).toHaveClass(/cal__block--cancelled/);
  await expect(cancelled).toContainText("Cancelled");
  await shot(desk, "09-cancelled-on-calendar.png");
  // the cancelled appointment no longer holds Dr. Patel's 16:00 slot: another patient can take it
  const rebook = await call(desk, "post", "/api/appointments", { patientId: ids.di, providerId: ids.patel, operatoryId: ids.op2, appointmentTypeId: ids.exam, startLocal: `${DAY}T16:00` }, { "Idempotency-Key": `cal-rebook-${stamp}` });
  expect(rebook.status).toBe(201);
  const stored = (await calendarOn(desk)).find((a) => a.id === ids.a3)!;
  expect(stored.status).toBe("Cancelled");
  evidence.cancel = { reasonRequired: true, stillOnCalendar: true, freedTheSlot: true };
});

test("NO-SHOW: not before the start time; once the start has passed the appointment is marked no-show, shown distinctly, and stays on the record", async () => {
  // a future appointment cannot be a no-show: the control is disabled and the server refuses
  await gotoCalendar(desk, DAY);
  await blockOf(desk, ids.a1).click();
  await expect(drawerOf(desk).getByRole("button", { name: "Mark no-show" })).toBeDisabled();
  await expect(drawerOf(desk).getByText("A no-show can be recorded once the start time has passed.")).toBeVisible();
  const early = await call(desk, "post", `/api/appointments/${ids.a1}/no-show`, { rowVersion: (await calendarOn(desk)).find((a) => a.id === ids.a1)!.rowVersion });
  expect(early.status).toBe(409);
  expect(early.body.error).toBe("no_show_too_early");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  // arrangement: move one booked appointment ten years back (the API refuses to BOOK the past; this is what an old appointment looks like)
  const victim = (await calendarOn(desk, NEXT)).find((a) => a.startLocal.endsWith("T15:00") && a.status === "Scheduled")!;
  sql(`UPDATE Appointments SET StartUtc = DATEADD(year, -10, StartUtc), EndUtc = DATEADD(year, -10, EndUtc) WHERE Id = '${victim.id}'`);
  await gotoCalendar(desk, PAST_DAY.replace("2020-01-14", "2020-01-15"));
  await blockOf(desk, victim.id).click();
  await expect(drawerOf(desk).getByRole("button", { name: "Mark no-show" })).toBeEnabled();
  await drawerOf(desk).getByRole("button", { name: "Mark no-show" }).click();
  await expect(drawerOf(desk).getByText("Marked as a no-show.").first()).toBeVisible();
  await shot(desk, "10-no-show-drawer.png");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  await expect(blockOf(desk, victim.id)).toHaveClass(/cal__block--noshow/);
  await expect(blockOf(desk, victim.id)).toContainText("No-show");
  await shot(desk, "11-no-show-on-calendar.png");
  await scanBothThemes(desk, "no-show-on-calendar");
  expect((await calendarOn(desk, "2020-01-15")).find((a) => a.id === victim.id)!.status).toBe("NoShow");
  evidence.noShow = { refusedBeforeStart: early.body.error, markedAfterStart: true, shownDistinctly: true };
});

test("PATIENT OVERLAP + WEEK VIEW: a patient cannot be in two places at once; the week view puts overlapping providers side by side", async () => {
  const dup = await call(desk, "post", "/api/appointments", { patientId: ids.ann, providerId: ids.patel, operatoryId: ids.op2, appointmentTypeId: ids.exam, startLocal: `${DAY}T09:30` }, { "Idempotency-Key": `cal-patient-${stamp}` });
  expect(dup.status).toBe(409);
  expect(dup.body.error).toBe("patient_double_booked");
  await book(desk, ids.ed, ids.patel, ids.op2, `${DAY}T09:15`, `cal-week-${stamp}`);   // overlaps Ann's 09:00-10:00 with Dr. Rivera, at the same time on the same day

  await gotoCalendar(desk, DAY, "Week");
  await expect(desk.getByRole("heading", { name: "Week of 2030-01-14 to 2030-01-20" })).toBeVisible();
  await expect(desk.locator(".cal__column .cal__head strong")).toHaveText(["Monday 2030-01-14", "Tuesday 2030-01-15", "Wednesday 2030-01-16", "Thursday 2030-01-17", "Friday 2030-01-18", "Saturday 2030-01-19", "Sunday 2030-01-20"]);
  const lefts = await desk.locator(`.cal__column[data-x], .cal__body[data-day="${DAY}"] .cal__block`).evaluateAll((els) => els.filter((e) => (e as HTMLElement).style.left).map((e) => (e as HTMLElement).style.left));
  expect(lefts).toContain("0%");
  expect(lefts).toContain("50%"); // two overlapping appointments share the day column side by side
  await shot(desk, "12-week-view.png");
  await scanBothThemes(desk, "week-view");
  evidence.weekView = { sevenDays: true, overlappingSideBySide: true, patientOverlapRefused: dup.body.error };
});

test("PERMISSIONS: a dentist sees the calendar and appointment details but cannot change anything (the API returns 403); billing has no Calendar", async ({ browser }) => {
  const dctx = await browser.newContext();
  const dentist = await dctx.newPage();
  await signIn(dentist, `dentist-${stamp}`, PASSWORD);
  await gotoCalendar(dentist, DAY);
  await expect(dentist.getByRole("button", { name: "New appointment" })).toHaveCount(0);
  await blockOf(dentist, ids.a1).click();
  await expect(drawerOf(dentist).getByRole("heading", { name: "Ann Calendar" })).toBeVisible();
  for (const name of ["Reschedule", "Cancel appointment", "Mark no-show", "Save note"]) await expect(drawerOf(dentist).getByRole("button", { name })).toHaveCount(0);
  const current = (await calendarOn(dentist)).find((a) => a.id === ids.a1)!;
  const refused = await call(dentist, "put", `/api/appointments/${ids.a1}/reschedule`, { providerId: ids.rivera, operatoryId: ids.op1, startLocal: `${DAY}T13:00`, rowVersion: current.rowVersion });
  expect(refused.status).toBe(403);
  expect((await calendarOn(desk)).find((a) => a.id === ids.a1)!.startLocal).toBe(`${DAY}T09:00`);
  await dctx.close();

  const bctx = await browser.newContext();
  const billing = await bctx.newPage();
  await signIn(billing, `billing-${stamp}`, PASSWORD);
  await expect(billing.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Calendar" })).toHaveCount(0);
  await billing.goto("/calendar");
  await expect(billing.getByText(/don't have permission/i)).toBeVisible();
  await bctx.close();
  evidence.permissions = { dentistApiStatus: refused.status, billingCalendarLink: false };
});

test("KEYBOARD: an appointment can be rescheduled without a mouse", async () => {
  await gotoCalendar(desk, DAY);
  await blockOf(desk, ids.a2).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByRole("heading", { name: "Bo Booker" })).toBeFocused();
  await desk.getByRole("button", { name: "Reschedule" }).focus();
  await desk.keyboard.press("Enter");
  await desk.getByRole("form", { name: "Reschedule appointment" }).getByLabel("Start time *").fill("14:00");
  await desk.getByRole("button", { name: "Save new time" }).focus();
  await desk.keyboard.press("Enter");
  await expect(desk.getByRole("dialog").getByText("2030-01-14 14:00–14:30 (30 min)")).toBeVisible();
  await desk.keyboard.press("Escape");
  await expect(desk.getByRole("dialog")).toHaveCount(0);
  evidence.keyboard = { rescheduled: true, escapeClosesDrawer: true };
});

test("ACCEPTANCE: the audit trail holds every create, reschedule, cancel, no-show and refusal with user and time, and no patient details or reasons", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const by = (t: string) => entries.filter((e) => e.eventType === t);
  expect(by("AppointmentScheduled").length).toBeGreaterThanOrEqual(10);
  expect(by("AppointmentRescheduled").length).toBeGreaterThanOrEqual(4);
  expect(by("AppointmentCancelled").length).toBe(1);
  expect(by("AppointmentNoShow").length).toBe(1);
  expect(by("AppointmentRejected").length).toBeGreaterThanOrEqual(8);
  for (const e of entries.filter((x) => x.eventType.startsWith("Appointment"))) {
    expect(e.performedByUserAccountId).toBeTruthy();
    expect(Date.now() - Date.parse(e.timestampUtc)).toBeLessThan(60 * 60_000);
    expect(e.details).not.toMatch(/Ann|Bo |Cy |Di |Ed |Flo|Calendar|Booker|Clinic|Dental|Early|Frame|Rivera|Patel|555-|Patient called/);
  }
  expect(by("AppointmentRejected").some((e) => e.details.includes("patient_double_booked"))).toBe(true);
  expect(by("AppointmentRejected").some((e) => e.details.includes("provider_double_booked"))).toBe(true);
  evidence.audit = Object.fromEntries(["AppointmentScheduled", "AppointmentRescheduled", "AppointmentCancelled", "AppointmentNoShow", "AppointmentRejected"].map((t) => [t, by(t).length]));
});
