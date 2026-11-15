import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-011 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server. Run with playwright.flow.config.ts through the walkthrough runner
// (see docs/testing/REAL_BACKEND_E2E.md). It walks a visit from the CALENDAR drawer as the receptionist and the dentist would, with every change in the appointment's history and in the
// audit log with the user and the time, and the failure paths (out-of-order and repeated moves, a second user changing the appointment first, six people pressing Check in at once, a
// cancelled appointment, roles that may only look).
//
// ALV-N021 A2 (reworked): the drawer draws only the server's per-user actions - the same list as the Visit board. One path, no shortcuts (STORY-011's "check in, then complete" and
// "skip treatment" are refused); start and end treatment are the booked provider's (the dentist account is linked to Dr. Rivera); completion waits for the drawer's checkout panel;
// cancel needs a category. The legacy routes api/appointments/{id}/check-in|start-treatment|complete run the same policy. Check-in is allowed only on the appointment's own practice
// day, so this walkthrough runs on the chain host's injected clock ("now" = 2030-01-14 07:30 Chicago):
//   .\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.flow.config.ts -OutputVariable FLOW_E2E_OUT -InjectedClockUtc 2030-01-14T13:30:00Z

test.describe.configure({ mode: "serial" });

const OUT = process.env.FLOW_E2E_OUT ?? path.join(process.cwd(), "flow-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "flow-pass-1!";
const DAY = "2030-01-14"; // the injected "today" (a Monday)
const contexts: BrowserContext[] = [];
let admin: Page;
let desk: Page;
let dentist: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const users: Record<string, string> = {};

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
  return id as string;
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

async function newSession(browser: Browser, username: string) {
  const ctx = await browser.newContext();
  contexts.push(ctx);
  const page = await ctx.newPage();
  await signIn(page, username, PASSWORD);
  return page;
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

type Appt = { id: string; rowVersion: string; startLocal: string; status: string; flowState: string; flowChangedAtUtc: string | null; patientName: string };
type Event = { eventType: string; actorUserId: string | null; occurredAtUtc: string; detail: string | null };

async function book(patient: string, startLocal: string, key: string, op = ids.op1, provider = ids.rivera) {
  const r = await call(desk, "post", "/api/appointments", { patientId: patient, providerId: provider, operatoryId: op, appointmentTypeId: ids.exam, startLocal }, { "Idempotency-Key": key });
  expect(r.status, `book ${startLocal}: ${JSON.stringify(r.body)}`).toBe(201);
  return r.body as unknown as Appt;
}

const load = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}`)).json()) as Appt;
const historyOf = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}/history`)).json()) as Event[];
const move = (page: Page, id: string, target: string, rowVersion: string | null) => call(page, "post", `/api/visits/${id}/state`, { target, rowVersion });
const legacy = (page: Page, id: string, action: "check-in" | "start-treatment" | "complete", rowVersion: string | null) => call(page, "post", `/api/appointments/${id}/${action}`, { rowVersion });
async function walk(page: Page, id: string, states: string[]) {
  for (const s of states) {
    const r = await move(page, id, s, (await load(page, id)).rowVersion);
    expect(r.status, `${s}: ${JSON.stringify(r.body)}`).toBe(200);
  }
}

async function gotoCalendar(page: Page, day = DAY) {
  await page.goto("/calendar");
  await expect(page.getByRole("heading", { name: "Calendar", exact: true })).toBeVisible();
  await page.getByLabel("Date", { exact: true }).fill(day);
  await expect(page.locator(".cal")).toBeVisible();
}

const blockOf = (page: Page, id: string) => page.locator(`[data-appointment="${id}"]`);
const drawerOf = (page: Page) => page.getByRole("dialog");
async function openDrawer(page: Page, id: string) {
  await gotoCalendar(page);
  await blockOf(page, id).click();
  await expect(drawerOf(page).getByRole("table", { name: /what happened/i })).toBeVisible();
}
const bookingButtons = ["Reschedule", "Cancel appointment", "Mark no-show"];

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  expect(process.env.E2E_CLOCK_MODE, "this walkthrough needs the injected clock (run it with -InjectedClockUtc 2030-01-14T13:30:00Z)").toBe("injected");
  stamp = Date.now();
  const actx = await browser.newContext();
  contexts.push(actx);
  admin = await actx.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  users.desk = await createUser("FrontDesk", `desk-${stamp}`);
  users.desk2 = await createUser("FrontDesk", `desk2-${stamp}`);
  users.dentist = await createUser("Dentist", `dentist-${stamp}`);
  users.billing = await createUser("Billing", `billing-${stamp}`);
  evidence.stamp = stamp;
  desk = await newSession(browser, `desk-${stamp}`);
  dentist = await newSession(browser, `dentist-${stamp}`);
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "flow-e2e.json"), JSON.stringify(evidence, null, 2));
  for (const c of contexts) await c.close();
});

test("SETUP: the practice (two providers, the dentist linked to Dr. Rivera, two operatories, an exam type) and patients", async () => {
  expect((await call(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op1 = (await call(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id as string;
  ids.op2 = (await call(admin, "post", "/api/config/operatories", { name: "Op 2" })).body.id as string;
  ids.exam = (await call(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id as string;
  for (const [key, name, account] of [["rivera", "Dr. Rivera", users.dentist], ["patel", "Dr. Patel", null]] as const) {
    const staff = await call(admin, "post", "/api/config/staff", { displayName: name, jobTitle: "Dentist", userAccountId: account });
    expect(staff.status, JSON.stringify(staff.body)).toBe(201);
    ids[key] = (await call(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" })).body.id as string;
    const current = (await (await admin.request.get(`/api/config/providers/${ids[key]}/availability`)).json()) as { revision: number };
    const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
    expect((await call(admin, "put", `/api/config/providers/${ids[key]}/availability`, { windows, revision: current.revision })).status).toBe(200);
  }
  const people: [string, string, string, string][] = [["Ann", "Arrival", "1985-03-09", "555-010-0100"], ["Bo", "Bridge", "1990-01-01", "555-020-0200"], ["Cy", "Crown", "1975-05-05", "555-030-0300"], ["Di", "Denture", "1980-07-07", "555-040-0400"], ["Ed", "Enamel", "1970-02-02", "555-050-0500"]];
  for (const [first, last, dob, phone] of people) {
    const r = await call(desk, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `flow-e2e-${first}-${stamp}` });
    expect(r.status).toBe(201);
    ids[first.toLowerCase()] = r.body.id as string;
  }
});

test("CHECK-IN: the receptionist confirms and checks a patient in from the calendar; the status shows on the drawer, the calendar and the server", async () => {
  const a = await book(ids.ann, `${DAY}T09:00`, `flow-a1-${stamp}`);
  ids.a1 = a.id;
  expect(a.flowState).toBe("Scheduled");
  await openDrawer(desk, a.id);
  await expect(blockOf(desk, a.id)).toHaveAttribute("data-flow", "Scheduled");
  for (const name of bookingButtons) await expect(drawerOf(desk).getByRole("button", { name })).toBeVisible();
  await expect(drawerOf(desk).getByRole("button", { name: "Check in" })).toHaveCount(0); // confirmation comes first
  await expect(drawerOf(desk)).toContainText("Waiting for the front desk to confirm.");
  await shot(desk, "01-scheduled.png");

  await drawerOf(desk).getByRole("button", { name: "Confirm" }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Confirmed");
  await drawerOf(desk).getByRole("button", { name: "Check in" }).click();

  await expect(drawerOf(desk).getByText("Patient checked in. Waiting for chairside staff to mark the patient ready.")).toBeVisible();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Checked in");
  await expect(blockOf(desk, a.id)).toContainText("09:00–10:00 · Checked in");
  await expect(blockOf(desk, a.id)).toHaveAttribute("aria-label", /Checked in$/);
  for (const name of bookingButtons) await expect(drawerOf(desk).getByRole("button", { name })).toHaveCount(0); // the patient has arrived
  await expect(drawerOf(desk).getByRole("button", { name: "Undo check-in" })).toBeVisible();
  await expect(drawerOf(desk).getByRole("button", { name: "Start treatment" })).toHaveCount(0); // treatment is the booked provider's
  const stored = await load(desk, a.id);
  expect(stored.flowState).toBe("CheckedIn");
  expect(stored.flowChangedAtUtc?.slice(0, 10)).toBe(DAY); // stamped by the injected clock
  await shot(desk, "02-checked-in.png");
  await scanBothThemes(desk, "checked-in");
  evidence.checkIn = { status: stored.flowState, confirmedFirst: true, bookingButtonsGone: true };
});

test("COMPLETE: the dentist treats, the receptionist resolves the checkout in the drawer and completes; a finished visit still holds its time", async () => {
  await walk(admin, ids.a1, ["Ready", "Seated"]);   // chairside steps (an administrator holds the chairside permission)
  await openDrawer(dentist, ids.a1);
  await drawerOf(dentist).getByRole("button", { name: "Start treatment" }).click();
  await expect(drawerOf(dentist).getByTestId("flow-badge")).toHaveText("In treatment");
  await drawerOf(dentist).getByRole("button", { name: "End treatment / ready for checkout" }).click();
  await expect(drawerOf(dentist).getByTestId("flow-badge")).toHaveText("Ready for checkout");

  await openDrawer(desk, ids.a1);
  await expect(drawerOf(desk).getByRole("button", { name: "Complete visit" })).toBeDisabled();   // the checkout is still open
  const panel = drawerOf(desk).getByRole("region", { name: "Checkout for Ann Arrival" });
  await panel.getByRole("form", { name: /patient has left/i }).getByRole("combobox").selectOption({ label: "Patient has left" });
  await panel.getByRole("form", { name: /patient has left/i }).getByRole("button", { name: "Record" }).click();
  await expect(panel).toContainText("Patient has left recorded.");
  await panel.getByRole("form", { name: /follow-up/i }).getByRole("combobox").selectOption({ label: "Not clinically required" });
  await panel.getByRole("form", { name: /follow-up/i }).getByRole("button", { name: "Record" }).click();
  await expect(panel.getByRole("alert")).toContainText("Your role may not record this outcome. Nothing was recorded.");   // "not clinically required" is the treating provider's call
  await panel.getByRole("form", { name: /follow-up/i }).getByRole("combobox").selectOption({ label: "Patient declined" });
  await panel.getByRole("form", { name: /follow-up/i }).getByRole("button", { name: "Record" }).click();
  await expect(panel).toContainText("every item is resolved");
  await shot(desk, "03-checkout.png");

  await drawerOf(desk).getByRole("button", { name: "Complete visit" }).click();
  await drawerOf(desk).getByRole("button", { name: "Yes, complete the visit" }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Completed");
  await expect(drawerOf(desk).getByRole("group", { name: "Patient flow" })).toHaveCount(0);
  await expect(drawerOf(desk).getByRole("group", { name: "Booking" })).toHaveCount(0);
  await expect(blockOf(desk, ids.a1)).toContainText("· Completed");
  // on the injected clock every event has the same time, so the table lists them by event type: compare what is there, not the order (the order is proven by the board walkthrough's path check)
  const rows = await drawerOf(desk).getByRole("table", { name: /what happened/i }).locator("tbody tr td:nth-child(2)").allTextContents();
  expect([...rows].sort()).toEqual(["Scheduled", "Confirmed", "Patient checked in", "Marked ready", "Seated", "Treatment started", "Ready for checkout", "Visit completed",
    "Checkout decision recorded", "Checkout decision recorded"].sort());
  await shot(desk, "04-completed.png");
  await scanBothThemes(desk, "completed");

  // the completed visit still holds Dr. Rivera at 09:30: a second booking there is refused
  const clash = await call(desk, "post", "/api/appointments", { patientId: ids.bo, providerId: ids.rivera, operatoryId: ids.op2, appointmentTypeId: ids.exam, startLocal: `${DAY}T09:30` }, { "Idempotency-Key": `flow-clash-${stamp}` });
  expect([clash.status, clash.body.error]).toEqual([409, "provider_double_booked"]);
  evidence.complete = { status: "Completed", historyRows: rows, completeWaitedForCheckout: true, providerOnlyOutcomeRefused: true, stillHoldsTime: true };
});

test("NO SHORTCUTS: STORY-011's 'check in, then complete' and 'skip treatment' are refused on every route, and nothing changes", async () => {
  const b = await book(ids.bo, `${DAY}T11:00`, `flow-b-${stamp}`, ids.op2, ids.rivera);   // Dr. Rivera's, so her linked dentist may start treatment: only the path can refuse the skip
  const early = await legacy(desk, b.id, "check-in", b.rowVersion);
  expect([early.status, early.body.error]).toEqual([409, "illegal_transition"]);   // not confirmed yet
  await walk(desk, b.id, ["Confirmed", "CheckedIn"]);
  const viaState = await move(desk, b.id, "Completed", (await load(desk, b.id)).rowVersion);
  expect([viaState.status, viaState.body.error], "visits/{id}/state").toEqual([409, "illegal_transition"]);
  const viaLegacy = await legacy(desk, b.id, "complete", (await load(desk, b.id)).rowVersion);
  expect([viaLegacy.status, viaLegacy.body.error], "legacy complete").toEqual([409, "illegal_transition"]);
  const skip = await move(dentist, b.id, "InTreatment", (await load(dentist, b.id)).rowVersion);   // the booked provider, who may start treatment - but not from Checked in
  expect([skip.status, skip.body.error]).toEqual([409, "illegal_transition"]);
  expect((await load(desk, b.id)).flowState).toBe("CheckedIn");
  // nothing was added by the refused moves (compared as a set: on the injected clock all events share one time, so the history lists them by type)
  expect((await historyOf(desk, b.id)).map((e) => e.eventType).sort()).toEqual(["CheckedIn", "Confirmed", "Scheduled"]);
  evidence.noShortcuts = { checkInBeforeConfirm: 409, completeFromCheckedIn: 409, skipToTreatment: 409 };
});

test("REFUSALS: repeats, a missing or stale version, a cancelled appointment and an unknown one behave and change nothing", async () => {
  const c = await book(ids.cy, `${DAY}T13:00`, `flow-c-${stamp}`);
  ids.c = c.id;
  const confirmed = await move(desk, c.id, "Confirmed", c.rowVersion);
  const first = await move(desk, c.id, "CheckedIn", confirmed.body.rowVersion as string);
  expect(first.status).toBe(200);
  const again = await move(desk, c.id, "CheckedIn", c.rowVersion); // even with the old version: already checked in
  expect([again.status, again.body.rowVersion]).toEqual([200, first.body.rowVersion]);
  expect((await historyOf(desk, c.id)).filter((e) => e.eventType === "CheckedIn")).toHaveLength(1);

  const missing = await move(admin, c.id, "Ready", null);
  expect([missing.status, missing.body.error]).toEqual([400, "row_version_required"]);
  const stale = await move(admin, c.id, "Ready", c.rowVersion);
  expect([stale.status, stale.body.error]).toEqual([409, "concurrency_conflict"]);

  const cancel = await call(desk, "post", `/api/appointments/${c.id}/cancel`, { category: "patient", note: "Changed mind", rowVersion: first.body.rowVersion });
  expect([cancel.status, cancel.body.error]).toEqual([409, "appointment_in_progress"]);
  const noShow = await call(desk, "post", `/api/appointments/${c.id}/no-show`, { rowVersion: first.body.rowVersion });
  expect([noShow.status, noShow.body.error]).toEqual([409, "appointment_in_progress"]);
  const reschedule = await call(desk, "put", `/api/appointments/${c.id}/reschedule`, { providerId: ids.rivera, operatoryId: ids.op1, startLocal: `${DAY}T15:00`, rowVersion: first.body.rowVersion });
  expect([reschedule.status, reschedule.body.error]).toEqual([409, "appointment_in_progress"]);
  expect((await load(desk, c.id)).startLocal).toBe(`${DAY}T13:00`);

  const d = await book(ids.di, `${DAY}T14:00`, `flow-d-${stamp}`, ids.op2, ids.patel);
  const noCategory = await call(desk, "post", `/api/appointments/${d.id}/cancel`, { note: "Illness", rowVersion: d.rowVersion });
  expect([noCategory.status, noCategory.body.error]).toEqual([400, "category_required"]);
  const cancelled = await call(desk, "post", `/api/appointments/${d.id}/cancel`, { category: "patient", note: "Illness", rowVersion: d.rowVersion });
  expect(cancelled.status).toBe(200);
  const refused = await move(desk, d.id, "Confirmed", cancelled.body.rowVersion as string);
  expect(refused.status).toBe(409);

  const unknown = await move(desk, "00000000-0000-0000-0000-000000000000", "CheckedIn", "AAAA");
  expect([unknown.status, unknown.body.error]).toEqual([404, "appointment_not_found"]);
  evidence.refusals = { repeat: "no-op", missingVersion: 400, stale: 409, cancelAfterCheckIn: 409, cancelWithoutCategory: 400, cancelledCannotMove: 409, unknown: 404 };
});

test("STALE EDIT: a second user moves the patient first; a repeat of their move is a quiet no-op, a different move is reported and not applied, and Reload shows the real state", async ({ browser }) => {
  const e = await book(ids.ed, `${DAY}T15:30`, `flow-e-${stamp}`, ids.op2, ids.patel);
  await walk(desk, e.id, ["Confirmed"]);
  const desk2 = await newSession(browser, `desk2-${stamp}`);

  await openDrawer(desk, e.id);                                                              // user 1 opens it and holds its version
  expect((await move(desk2, e.id, "CheckedIn", (await load(desk2, e.id)).rowVersion)).status).toBe(200);   // user 2 checks the patient in

  // user 1 presses Check in too: the patient is already checked in, so nothing changes, nothing is duplicated and nobody is alarmed
  await drawerOf(desk).getByRole("button", { name: "Check in" }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Checked in");
  await expect(desk.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  expect((await historyOf(desk, e.id)).filter((x) => x.eventType === "CheckedIn")).toHaveLength(1);

  // chairside marks the patient ready (a DIFFERENT move); user 1's drawer still says "checked in" and holds the version from before that
  // (an undo by both desks would be the same move twice: the second is a quiet no-op, not a conflict)
  await walk(admin, e.id, ["Ready"]);
  await drawerOf(desk).getByRole("button", { name: "Undo check-in" }).click();
  await drawerOf(desk).getByLabel("Reason (required)").fill("Arrived early by mistake");
  await drawerOf(desk).getByRole("button", { name: "Undo check-in" }).click();
  await expect(desk.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(desk, "05-stale.png");
  await scanBothThemes(desk, "stale");
  expect((await load(desk, e.id)).flowState).toBe("Ready");   // not undone on top of the other change
  expect((await historyOf(desk, e.id)).filter((x) => x.eventType === "CheckInUndone")).toHaveLength(0);

  await desk.getByRole("button", { name: /Reload/ }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Ready");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  evidence.staleEdit = { repeatWasQuietNoOp: true, differentMoveReported: true, notAppliedOnTop: true };
});

test("RACE: six people pressing Check in at once produce exactly one check-in", async () => {
  const f = await book(ids.ann, `${DAY}T16:00`, `flow-f-${stamp}`);
  await walk(desk, f.id, ["Confirmed"]);
  const version = (await load(desk, f.id)).rowVersion;
  const results = await Promise.all(Array.from({ length: 6 }, () => move(desk, f.id, "CheckedIn", version)));
  expect(results.every((r) => r.status === 200 || (r.status === 409 && r.body.error === "concurrency_conflict"))).toBe(true);
  expect(results.some((r) => r.status === 200)).toBe(true);
  expect((await historyOf(desk, f.id)).filter((x) => x.eventType === "CheckedIn")).toHaveLength(1);
  expect((await load(desk, f.id)).flowState).toBe("CheckedIn");
  evidence.race = { statuses: results.map((r) => r.status), checkIns: 1 };
});

test("PERMISSIONS: the dentist sees where the patient is and may not do front-desk moves (403, also on the legacy route); billing has no Calendar and no way in", async ({ browser }) => {
  await openDrawer(dentist, ids.c);
  await expect(drawerOf(dentist).getByTestId("flow-badge")).toHaveText("Checked in");
  for (const name of [...bookingButtons, "Undo check-in", "Start treatment"]) await expect(drawerOf(dentist).getByRole("button", { name })).toHaveCount(0);
  await expect(drawerOf(dentist).getByRole("button", { name: "Mark ready" })).toBeVisible(); // chairside work is the dentist's
  const current = await load(dentist, ids.c);
  const refused = await call(dentist, "post", `/api/visits/${ids.c}/undo-check-in`, { reason: "x", rowVersion: current.rowVersion });
  expect([refused.status, refused.body.reason]).toEqual([403, "front_office_only"]);
  const legacyRefused = await legacy(dentist, ids.c, "check-in", current.rowVersion);
  expect(legacyRefused.status).toBe(403);
  expect((await load(desk, ids.c)).flowState).toBe("CheckedIn");

  const billing = await newSession(browser, `billing-${stamp}`);
  const billed = await move(billing, ids.c, "Ready", current.rowVersion);
  expect(billed.status).toBe(403);
  evidence.permissions = { dentistUndoStatus: refused.status, dentistLegacyStatus: legacyRefused.status, billingApiStatus: billed.status, dentistSeesFlow: true };
});

test("KEYBOARD: a patient can be confirmed and checked in without a mouse", async () => {
  const g = await book(ids.bo, `${DAY}T08:00`, `flow-g-${stamp}`, ids.op1, ids.patel);
  await gotoCalendar(desk);
  await blockOf(desk, g.id).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByRole("heading", { name: "Bo Bridge" })).toBeFocused();
  await drawerOf(desk).getByRole("button", { name: "Confirm" }).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Confirmed");
  await expect(drawerOf(desk).getByRole("heading", { name: "Bo Bridge" })).toBeFocused(); // focus is not lost when the button disappears
  await drawerOf(desk).getByRole("button", { name: "Check in" }).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Checked in");
  await desk.keyboard.press("Escape");
  await expect(desk.getByRole("dialog")).toHaveCount(0);
  evidence.keyboard = { confirmedAndCheckedIn: true };
});

test("ACCEPTANCE: every status change is in the audit log with the user, and in the appointment's history, with no patient details", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; targetUserAccountId: string; timestampUtc: string; details: string }[];
  const by = (t: string) => entries.filter((e) => e.eventType === t);
  expect(by("PatientCheckedIn").length).toBe(6);                           // a1, b, c, e, f, g: one per appointment, never two
  expect(by("PatientTreatmentStarted").length).toBe(1);                   // a1, by the booked dentist
  expect(by("PatientTreatmentCompleted").length).toBe(1);                 // a1
  expect(by("PatientCheckInUndone").length).toBe(0);                      // the stale undo on e was refused, so none was recorded
  const flow = entries.filter((e) => ["PatientConfirmed", "PatientCheckedIn", "PatientTreatmentStarted", "PatientTreatmentCompleted", "PatientCheckInUndone"].includes(e.eventType));
  for (const e of flow) {
    expect(e.performedByUserAccountId).toBeTruthy();
    expect(e.details).not.toMatch(/Ann|Bo |Cy |Di |Ed |Arrival|Bridge|Crown|Denture|Enamel|Rivera|Patel|555-|Wrong patient|mistake/);
  }
  const targets = new Set(by("PatientCheckedIn").map((e) => e.targetUserAccountId));
  expect(targets.size).toBe(by("PatientCheckedIn").length);               // one check-in per appointment: no duplicates from repeats or the race
  const h = await historyOf(admin, ids.a1);
  expect(h.find((x) => x.eventType === "CheckedIn")?.actorUserId).toBe(users.desk);
  expect(h.find((x) => x.eventType === "TreatmentStarted")?.actorUserId).toBe(users.dentist);
  expect(h.find((x) => x.eventType === "Completed")?.actorUserId).toBe(users.desk);
  evidence.audit = Object.fromEntries(["PatientCheckedIn", "PatientTreatmentStarted", "PatientTreatmentCompleted", "PatientCheckInUndone"].map((t) => [t, by(t).length]));
});
