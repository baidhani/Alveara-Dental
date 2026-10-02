import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-011 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.flow.config.ts against an API the caller started
// (see docs/testing/REAL_BACKEND_E2E.md). It walks the story's acceptance items as a receptionist would: a scheduled patient checks in and
// their status becomes "checked in"; treatment is completed and their status becomes "completed"; every change is in the appointment's history and
// in the audit log with the user and the time; and the failure paths behave (out-of-order and repeated moves, a second user changing the
// appointment first, six people pressing Check in at once, a cancelled appointment, a role that may only look).

test.describe.configure({ mode: "serial" });

const OUT = process.env.FLOW_E2E_OUT ?? path.join(process.cwd(), "flow-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "flow-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future
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

type Appt = { id: string; rowVersion: string; startLocal: string; status: string; flowState: string; flowChangedAtUtc: string | null; patientName: string };
type Event = { eventType: string; actorUserId: string | null; occurredAtUtc: string; detail: string | null };

async function book(patient: string, startLocal: string, key: string, op = ids.op1, provider = ids.rivera) {
  const r = await call(desk, "post", "/api/appointments", { patientId: patient, providerId: provider, operatoryId: op, appointmentTypeId: ids.exam, startLocal }, { "Idempotency-Key": key });
  expect(r.status, `book ${startLocal}: ${JSON.stringify(r.body)}`).toBe(201);
  return r.body as unknown as Appt;
}

const load = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}`)).json()) as Appt;
const historyOf = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}/history`)).json()) as Event[];
const move = (page: Page, id: string, action: "check-in" | "start-treatment" | "complete", rowVersion: string | null) => call(page, "post", `/api/appointments/${id}/${action}`, { rowVersion });

async function gotoCalendar(page: Page, day = DAY) {
  await page.goto("/calendar");
  await expect(page.getByRole("heading", { name: "Calendar", exact: true })).toBeVisible();
  await page.getByLabel("Date", { exact: true }).fill(day);
  await expect(page.locator(".cal")).toBeVisible();
}

const blockOf = (page: Page, id: string) => page.locator(`[data-appointment="${id}"]`);
const drawerOf = (page: Page) => page.getByRole("dialog");
const flowButtons = ["Check in", "Start treatment", "Complete treatment"];
const changeButtons = ["Reschedule", "Cancel appointment", "Mark no-show"];
let deskUserId = "";

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  deskUserId = await createUser("FrontDesk", `desk-${stamp}`);
  await createUser("FrontDesk", `desk2-${stamp}`);
  await createUser("Dentist", `dentist-${stamp}`);
  await createUser("Billing", `billing-${stamp}`);
  evidence.stamp = stamp;
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, `desk-${stamp}`, PASSWORD);
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "flow-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  await deskContext.close();
});

test("SETUP: the practice (one provider pair, two operatories, an exam type) and patients", async () => {
  expect((await call(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op1 = (await call(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id as string;
  ids.op2 = (await call(admin, "post", "/api/config/operatories", { name: "Op 2" })).body.id as string;
  ids.exam = (await call(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id as string;
  for (const [key, name] of [["rivera", "Dr. Rivera"], ["patel", "Dr. Patel"]] as const) {
    const staff = await call(admin, "post", "/api/config/staff", { displayName: name, jobTitle: "Dentist" });
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

test("CHECK-IN: a scheduled patient checks in from the calendar and their status becomes checked in, on the drawer, the calendar and the server", async () => {
  const a = await book(ids.ann, `${DAY}T09:00`, `flow-a1-${stamp}`);
  ids.a1 = a.id;
  expect(a.flowState).toBe("Scheduled");
  await gotoCalendar(desk);
  await expect(blockOf(desk, a.id)).toHaveAttribute("data-flow", "Scheduled");
  await expect(blockOf(desk, a.id)).not.toContainText("Checked in");
  await blockOf(desk, a.id).click();
  for (const name of changeButtons) await expect(drawerOf(desk).getByRole("button", { name })).toBeVisible();
  await expect(drawerOf(desk).getByRole("button", { name: "Check in" })).toBeEnabled();
  await shot(desk, "01-scheduled.png");

  await drawerOf(desk).getByRole("button", { name: "Check in" }).click();

  await expect(drawerOf(desk).getByText("Patient checked in.")).toBeVisible();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Checked in");
  await expect(blockOf(desk, a.id)).toContainText("09:00–10:00 · Checked in");
  await expect(blockOf(desk, a.id)).toHaveAttribute("aria-label", /Checked in$/);
  for (const name of changeButtons) await expect(drawerOf(desk).getByRole("button", { name })).toHaveCount(0);
  await expect(drawerOf(desk).getByRole("button", { name: "Start treatment" })).toBeVisible();
  const stored = await load(desk, a.id);
  expect(stored.flowState).toBe("CheckedIn");
  expect(stored.flowChangedAtUtc).toBeTruthy();
  await shot(desk, "02-checked-in.png");
  await scanBothThemes(desk, "checked-in");
  evidence.checkIn = { status: stored.flowState, changeButtonsGone: true };
});

test("COMPLETE: treatment is started and completed and the status becomes completed; a finished visit still holds its time", async () => {
  await gotoCalendar(desk);
  await blockOf(desk, ids.a1).click();
  await drawerOf(desk).getByRole("button", { name: "Start treatment" }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("In treatment");
  await drawerOf(desk).getByRole("button", { name: "Complete treatment" }).click();

  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Completed");
  for (const name of [...flowButtons, ...changeButtons]) await expect(drawerOf(desk).getByRole("button", { name })).toHaveCount(0);
  await expect(blockOf(desk, ids.a1)).toContainText("· Completed");
  expect((await load(desk, ids.a1)).flowState).toBe("Completed");
  const rows = await drawerOf(desk).getByRole("table", { name: /what happened/i }).locator("tbody tr td:nth-child(2)").allTextContents();
  expect(rows).toEqual(["Scheduled", "Patient checked in", "Treatment started", "Treatment completed"]);
  await shot(desk, "03-completed.png");
  await scanBothThemes(desk, "completed");

  // the completed visit still holds Dr. Rivera and Op 1 at 09:30: a second booking there is refused
  const clash = await call(desk, "post", "/api/appointments", { patientId: ids.bo, providerId: ids.rivera, operatoryId: ids.op2, appointmentTypeId: ids.exam, startLocal: `${DAY}T09:30` }, { "Idempotency-Key": `flow-clash-${stamp}` });
  expect(clash.status).toBe(409);
  expect(clash.body.error).toBe("provider_double_booked");
  evidence.complete = { status: "Completed", historyRows: rows, stillHoldsTime: true };
});

test("SKIP TREATMENT: a checked-in patient can go straight to completed", async () => {
  const b = await book(ids.bo, `${DAY}T11:00`, `flow-b-${stamp}`, ids.op2, ids.patel);
  const ci = await move(desk, b.id, "check-in", b.rowVersion);
  expect(ci.status).toBe(200);
  const done = await move(desk, b.id, "complete", ci.body.rowVersion as string);
  expect(done.status).toBe(200);
  expect(done.body.flowState).toBe("Completed");
  expect((await historyOf(desk, b.id)).map((e) => e.eventType)).toEqual(["Scheduled", "CheckedIn", "Completed"]);
  evidence.skip = { inTreatmentOptional: true };
});

test("REFUSALS: out-of-order moves, repeats, a cancelled appointment and an unknown one behave and change nothing", async () => {
  const c = await book(ids.cy, `${DAY}T13:00`, `flow-c-${stamp}`);
  ids.c = c.id;

  const skip = await move(desk, c.id, "start-treatment", c.rowVersion);
  expect([skip.status, skip.body.error]).toEqual([409, "invalid_flow_transition"]);
  const skipDone = await move(desk, c.id, "complete", c.rowVersion);
  expect([skipDone.status, skipDone.body.error]).toEqual([409, "invalid_flow_transition"]);
  expect((await load(desk, c.id)).flowState).toBe("Scheduled");

  const first = await move(desk, c.id, "check-in", c.rowVersion);
  expect(first.status).toBe(200);
  const again = await move(desk, c.id, "check-in", c.rowVersion); // even with the old version: already checked in
  expect([again.status, again.body.rowVersion]).toEqual([200, first.body.rowVersion]);
  expect((await historyOf(desk, c.id)).filter((e) => e.eventType === "CheckedIn")).toHaveLength(1);

  const missing = await move(desk, c.id, "start-treatment", null);
  expect([missing.status, missing.body.error]).toEqual([400, "row_version_required"]);
  const stale = await move(desk, c.id, "start-treatment", c.rowVersion);
  expect([stale.status, stale.body.error]).toEqual([409, "concurrency_conflict"]);

  const cancel = await call(desk, "post", `/api/appointments/${c.id}/cancel`, { reason: "Changed mind", rowVersion: first.body.rowVersion });
  expect([cancel.status, cancel.body.error]).toEqual([409, "appointment_in_progress"]);
  const noShow = await call(desk, "post", `/api/appointments/${c.id}/no-show`, { rowVersion: first.body.rowVersion });
  expect([noShow.status, noShow.body.error]).toEqual([409, "appointment_in_progress"]);
  const reschedule = await call(desk, "put", `/api/appointments/${c.id}/reschedule`, { providerId: ids.rivera, operatoryId: ids.op1, startLocal: `${DAY}T15:00`, rowVersion: first.body.rowVersion });
  expect([reschedule.status, reschedule.body.error]).toEqual([409, "appointment_in_progress"]);
  expect((await load(desk, c.id)).startLocal).toBe(`${DAY}T13:00`);

  const d = await book(ids.di, `${DAY}T14:00`, `flow-d-${stamp}`, ids.op2, ids.patel);
  const cancelled = await call(desk, "post", `/api/appointments/${d.id}/cancel`, { reason: "Illness", rowVersion: d.rowVersion });
  expect(cancelled.status).toBe(200);
  const refused = await move(desk, d.id, "check-in", cancelled.body.rowVersion as string);
  expect([refused.status, refused.body.error]).toEqual([409, "appointment_not_scheduled"]);

  const unknown = await move(desk, "00000000-0000-0000-0000-000000000000", "check-in", "AAAA");
  expect([unknown.status, unknown.body.error]).toEqual([404, "appointment_not_found"]);
  evidence.refusals = { outOfOrder: 409, repeat: "no-op", missingVersion: 400, stale: 409, cancelAfterCheckIn: 409, cancelledCannotCheckIn: 409, unknown: 404 };
});

test("STALE EDIT: a second user moves the patient first; a repeat of their move is a quiet no-op, a different move is reported and not applied, and Reload shows the real state", async ({ browser }) => {
  const e = await book(ids.ed, `${DAY}T15:30`, `flow-e-${stamp}`, ids.op2, ids.patel);
  const ctx2 = await browser.newContext();
  const desk2 = await ctx2.newPage();
  await signIn(desk2, `desk2-${stamp}`, PASSWORD);

  await gotoCalendar(desk);
  await blockOf(desk, e.id).click();                                       // user 1 opens it and holds its version
  const checkedIn = await move(desk2, e.id, "check-in", e.rowVersion);    // user 2 checks the patient in
  expect(checkedIn.status).toBe(200);

  // user 1 presses Check in too: the patient is already checked in, so nothing changes, nothing is duplicated and nobody is alarmed
  await drawerOf(desk).getByRole("button", { name: "Check in" }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Checked in");
  await expect(desk.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  expect((await historyOf(desk, e.id)).filter((x) => x.eventType === "CheckedIn")).toHaveLength(1);

  // user 2 now starts treatment; user 1's drawer still says "checked in" and holds the version from before that
  const started = await move(desk2, e.id, "start-treatment", (await load(desk2, e.id)).rowVersion);
  expect(started.status).toBe(200);
  await drawerOf(desk).getByRole("button", { name: "Complete treatment" }).click();
  await expect(desk.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(desk, "04-stale.png");
  await scanBothThemes(desk, "stale");
  expect((await load(desk, e.id)).flowState).toBe("InTreatment");          // not completed on top of user 2's change
  expect((await historyOf(desk, e.id)).some((x) => x.eventType === "Completed")).toBe(false);

  await desk.getByRole("button", { name: /Reload/ }).click();
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("In treatment");
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  await ctx2.close();
  evidence.staleEdit = { repeatWasQuietNoOp: true, differentMoveReported: true, notAppliedOnTop: true };
});

test("RACE: six people pressing Check in at once produce exactly one check-in", async () => {
  const f = await book(ids.ann, `${DAY}T16:00`, `flow-f-${stamp}`);
  const results = await Promise.all(Array.from({ length: 6 }, () => move(desk, f.id, "check-in", f.rowVersion)));
  expect(results.every((r) => r.status === 200 || (r.status === 409 && r.body.error === "concurrency_conflict"))).toBe(true);
  expect(results.some((r) => r.status === 200)).toBe(true);
  expect((await historyOf(desk, f.id)).filter((x) => x.eventType === "CheckedIn")).toHaveLength(1);
  expect((await load(desk, f.id)).flowState).toBe("CheckedIn");
  evidence.race = { statuses: results.map((r) => r.status), checkIns: 1 };
});

test("PERMISSIONS: a dentist sees where the patient is but cannot move them (403); billing has no Calendar", async ({ browser }) => {
  const dctx = await browser.newContext();
  const dentist = await dctx.newPage();
  await signIn(dentist, `dentist-${stamp}`, PASSWORD);
  await gotoCalendar(dentist);
  await blockOf(dentist, ids.c).click();
  await expect(drawerOf(dentist).getByTestId("flow-badge")).toHaveText("Checked in");
  for (const name of [...flowButtons, ...changeButtons]) await expect(drawerOf(dentist).getByRole("button", { name })).toHaveCount(0);
  const current = await load(dentist, ids.c);
  const refused = await move(dentist, ids.c, "start-treatment", current.rowVersion);
  expect(refused.status).toBe(403);
  expect((await load(desk, ids.c)).flowState).toBe("CheckedIn");
  await dctx.close();

  const bctx = await browser.newContext();
  const billing = await bctx.newPage();
  await signIn(billing, `billing-${stamp}`, PASSWORD);
  const billed = await move(billing, ids.c, "start-treatment", current.rowVersion);
  expect(billed.status).toBe(403);
  await bctx.close();
  evidence.permissions = { dentistApiStatus: refused.status, billingApiStatus: billed.status, dentistSeesFlow: true };
});

test("KEYBOARD: a patient can be checked in and completed without a mouse", async () => {
  const g = await book(ids.bo, `${DAY}T08:00`, `flow-g-${stamp}`, ids.op1, ids.patel);
  await gotoCalendar(desk);
  await blockOf(desk, g.id).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByRole("heading", { name: "Bo Bridge" })).toBeFocused();
  await drawerOf(desk).getByRole("button", { name: "Check in" }).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Checked in");
  await expect(drawerOf(desk).getByRole("heading", { name: "Bo Bridge" })).toBeFocused(); // focus is not lost when the button disappears
  await drawerOf(desk).getByRole("button", { name: "Complete treatment" }).focus();
  await desk.keyboard.press("Enter");
  await expect(drawerOf(desk).getByTestId("flow-badge")).toHaveText("Completed");
  await desk.keyboard.press("Escape");
  await expect(desk.getByRole("dialog")).toHaveCount(0);
  evidence.keyboard = { checkedInAndCompleted: true };
});

test("ACCEPTANCE: every status change is in the audit log with the user and the time, and in the appointment's history, with no patient details", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; targetUserAccountId: string; timestampUtc: string; details: string }[];
  const by = (t: string) => entries.filter((e) => e.eventType === t);
  expect(by("PatientCheckedIn").length).toBe(6);                           // a1, b, c, e, f, g: one per appointment, never two
  expect(by("PatientTreatmentStarted").length).toBe(2);                   // a1 and e
  expect(by("PatientTreatmentCompleted").length).toBeGreaterThanOrEqual(3); // a1, b, g
  const flow = entries.filter((e) => ["PatientCheckedIn", "PatientTreatmentStarted", "PatientTreatmentCompleted"].includes(e.eventType));
  for (const e of flow) {
    expect(e.performedByUserAccountId).toBeTruthy();
    expect(Date.now() - Date.parse(e.timestampUtc)).toBeLessThan(60 * 60_000);
    expect(e.details).not.toMatch(/Ann|Bo |Cy |Di |Ed |Arrival|Bridge|Crown|Denture|Enamel|Rivera|Patel|555-|Arrived early/);
  }
  const deskActions = flow.filter((e) => e.performedByUserAccountId === deskUserId);
  expect(deskActions.length).toBeGreaterThan(0);
  const targets = new Set(by("PatientCheckedIn").map((e) => e.targetUserAccountId));
  expect(targets.size).toBe(by("PatientCheckedIn").length);               // one check-in per appointment: no duplicates from repeats or the race
  for (const id of [ids.a1]) {
    const h = await historyOf(admin, id);
    for (const e of h.filter((x) => ["CheckedIn", "TreatmentStarted", "Completed"].includes(x.eventType))) {
      expect(e.actorUserId).toBe(deskUserId);
      expect(Date.now() - Date.parse(e.occurredAtUtc)).toBeLessThan(60 * 60_000);
    }
  }
  evidence.audit = Object.fromEntries(["PatientCheckedIn", "PatientTreatmentStarted", "PatientTreatmentCompleted"].map((t) => [t, by(t).length]));
});
