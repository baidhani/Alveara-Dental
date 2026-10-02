import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-011-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.board.config.ts against an API the caller started
// (see docs/testing/REAL_BACKEND_E2E.md). It walks the story's acceptance items as the front office and the chairside team would: a visit travels the whole
// production chain across four different roles; the provider and operatory assignment is visible and changeable without moving the booking; a room never holds
// two patients; cancelled/no-show stay distinct from completed; two users cannot silently overwrite each other; the board updates to persisted truth by itself;
// and the check-in cue reports the REAL state of the required forms (signed on the current wording, started, or not done) without fabricating anything.
//
// One arrangement uses the database directly: the API correctly refuses to book a start in the past, so the "visit left open from an earlier day" is made by
// moving one seated appointment's times back with a single SQL UPDATE, exactly as a visit left open overnight would look.

test.describe.configure({ mode: "serial" });

const OUT = process.env.BOARD_E2E_OUT ?? path.join(process.cwd(), "board-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "board-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future
const NAMES = { ann: "Ann Arrival", bo: "Bo Bridge", cy: "Cy Crown", di: "Di Denture", ed: "Ed Enamel" };
let stamp = 0;
const ids: Record<string, string> = {};
const users: Record<string, string> = {};
const contexts: Record<string, BrowserContext> = {};
const pages: Record<string, Page> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function call(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, unknown> };
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

async function userPage(browser: Browser, key: string, role: string) {
  const admin = pages.admin;
  const token = await csrf(admin);
  const username = `${key}-${stamp}`;
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  users[key] = id as string;
  contexts[key] = await browser.newContext();
  pages[key] = await contexts[key].newPage();
  await signIn(pages[key], username, PASSWORD);
}

type Appt = { id: string; rowVersion: string; flowState: string; status: string; providerName: string; operatoryName: string; visitProviderName: string; visitOperatoryName: string; patientName: string };
type Event = { eventType: string; actorUserId: string | null; occurredAtUtc: string; detail: string | null };

async function book(patientKey: string, startLocal: string, key: string, provider = ids.rivera, operatory = ids.op1) {
  const r = await call(pages.desk, "post", "/api/appointments", { patientId: ids[patientKey], providerId: provider, operatoryId: operatory, appointmentTypeId: ids.exam, startLocal }, { "Idempotency-Key": key });
  expect(r.status, `book ${startLocal}: ${JSON.stringify(r.body)}`).toBe(201);
  return r.body as unknown as Appt;
}

const load = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}`)).json()) as Appt;
const historyOf = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}/history`)).json()) as Event[];
const move = (page: Page, id: string, target: string, rowVersion: string | null) => call(page, "post", `/api/visits/${id}/state`, { target, rowVersion });

async function gotoBoard(page: Page, day: string | null = DAY) {
  await page.goto("/flow");
  await expect(page.getByRole("heading", { name: "Visit board", exact: true })).toBeVisible();
  if (day) await page.getByLabel("Date", { exact: true }).fill(day);
  await expect(page.getByRole("region", { name: /^Scheduled \(/ })).toBeVisible();
  await expect(page.getByText(/^Updated /)).toBeVisible();
}

const cardOf = (page: Page, id: string) => page.locator(`[data-visit="${id}"]`);
const column = (page: Page, state: string) => page.getByRole("region", { name: new RegExp(`^${state} \\(`) });
const inColumn = (page: Page, state: string, patient: string) => column(page, state).getByRole("heading", { name: patient });
const press = (page: Page, id: string, button: string, patient: string) => cardOf(page, id).getByRole("button", { name: `${button} ${patient}` }).click();

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the 'left open from an earlier day' arrangement needs it)");
  execFileSync("sqlcmd", ["-S", "(localdb)\\MSSQLLocalDB", "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  contexts.admin = await browser.newContext();
  pages.admin = await contexts.admin.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await pages.admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(pages.admin, `admin-${stamp}`, "admin-password-1!");
  for (const [key, role] of [["desk", "FrontDesk"], ["desk2", "FrontDesk"], ["assistant", "Assistant"], ["dentist", "Dentist"], ["manager", "OfficeManager"], ["billing", "Billing"]] as const)
    await userPage(browser, key, role);
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "board-e2e.json"), JSON.stringify(evidence, null, 2));
  for (const c of Object.values(contexts)) await c.close();
});

test("SETUP: the practice, six staff, five patients, two required forms and their real signing state", async () => {
  const admin = pages.admin;
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
  const people: [string, string, string, string, string][] = [["ann", "Ann", "Arrival", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Bridge", "1990-01-01", "555-020-0200"], ["cy", "Cy", "Crown", "1975-05-05", "555-030-0300"], ["di", "Di", "Denture", "1980-07-07", "555-040-0400"], ["ed", "Ed", "Enamel", "1970-02-02", "555-050-0500"]];
  for (const [key, first, last, dob, phone] of people) {
    const r = await call(pages.desk, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `board-e2e-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id as string;
  }

  // two form templates, both required at check-in through the real endpoint (audited, no new version)
  for (const [key, title] of [["privacy", "Privacy notice"], ["financial", "Financial policy"]] as const) {
    const created = await call(admin, "post", "/api/forms/templates", { key: `${key}-notice`, category: key === "privacy" ? "Privacy" : "Financial", title, body: `${title} wording.`, fields: [{ id: "ok", label: "I have read this", kind: "checkbox", required: true, options: null }], changeNote: null });
    expect(created.status).toBe(201);
    const t = (created.body as { template: { id: string; rowVersion: string } }).template;
    ids[`${key}Template`] = t.id;
    const required = await call(admin, "put", `/api/forms/templates/${t.id}/required-at-check-in`, { required: true, rowVersion: t.rowVersion });
    expect(required.status).toBe(200);
    expect((required.body as { template: { requiredAtCheckIn: boolean; versionCount: number } }).template).toMatchObject({ requiredAtCheckIn: true, versionCount: 1 });
  }
  async function startForm(patient: string, template: string) {
    const r = await call(pages.desk, "post", `/api/patients/${ids[patient]}/forms`, { templateId: ids[template] });
    expect([200, 201]).toContain(r.status);
    return r.body as unknown as { summary: { id: string }; rowVersion: string; version: { id: string } };
  }
  async function signForm(patient: string, template: string) {
    const draft = await startForm(patient, template);
    const saved = await call(pages.desk, "put", `/api/forms/${draft.summary.id}/responses`, { responses: { ok: "true" }, rowVersion: draft.rowVersion });
    expect(saved.status).toBe(200);
    const detail = saved.body as unknown as { rowVersion: string; version: { id: string } };
    const signed = await call(pages.desk, "post", `/api/forms/${draft.summary.id}/sign`, { signerName: NAMES[patient as keyof typeof NAMES], relationship: "Self", relationshipNote: null, signatureText: NAMES[patient as keyof typeof NAMES], attested: true, templateVersionId: detail.version.id, rowVersion: detail.rowVersion }, { "Idempotency-Key": `board-sign-${patient}-${template}-${stamp}` });
    expect(signed.status, JSON.stringify(signed.body)).toBe(201);
  }
  await signForm("ann", "privacyTemplate");
  await signForm("ann", "financialTemplate"); // Ann: everything signed
  await signForm("bo", "privacyTemplate");
  await startForm("bo", "financialTemplate");  // Bo: one signed, one only started
  // Cy: nothing

  ids.a1 = (await book("ann", `${DAY}T09:00`, `board-a1-${stamp}`)).id;
  ids.a2 = (await book("bo", `${DAY}T11:00`, `board-a2-${stamp}`)).id;
  ids.a3 = (await book("cy", `${DAY}T09:00`, `board-a3-${stamp}`, ids.patel, ids.op2)).id;
  ids.a4 = (await book("di", `${DAY}T13:00`, `board-a4-${stamp}`, ids.patel, ids.op2)).id;
  ids.a5 = (await book("ed", `${DAY}T15:00`, `board-a5-${stamp}`)).id;
});

test("BOARD: a column per state with each appointment where the server put it, the real form readiness at check-in, and no axe violations", async () => {
  const desk = pages.desk;
  await gotoBoard(desk);

  await expect(column(desk, "Scheduled")).toBeVisible();
  await expect(desk.getByRole("region", { name: "Scheduled (5)" })).toBeVisible();
  for (const state of ["Confirmed", "Checked in", "Ready", "Seated", "In treatment", "Checked out", "Completed"]) await expect(desk.getByRole("region", { name: `${state} (0)` })).toBeVisible();
  await expect(desk.getByRole("region", { name: "Cancelled and no-show (0)" })).toBeVisible();
  await expect(cardOf(desk, ids.a1)).toContainText("Dr. Rivera · Op 1");
  await expect(cardOf(desk, ids.a3)).toContainText("Dr. Patel · Op 2");

  // the form cue is the REAL state of the forms: Ann signed both, Bo signed one and started the other, Cy did nothing
  await expect(cardOf(desk, ids.a1)).toContainText("Required forms complete (2 of 2).");
  await expect(cardOf(desk, ids.a2)).toContainText("Forms: 1 of 2 complete.");
  await expect(cardOf(desk, ids.a2)).toContainText("Financial policy — Started, not signed");
  await expect(cardOf(desk, ids.a2)).not.toContainText("Privacy notice —"); // only what is outstanding is listed
  await expect(cardOf(desk, ids.a3)).toContainText("Forms: 0 of 2 complete.");
  await expect(cardOf(desk, ids.a3)).toContainText("Privacy notice — Not done");
  await shot(desk, "01-board-scheduled.png");
  await scanBothThemes(desk, "board-scheduled");

  // viewing a form never completes it: Bo's started form is still only started after looking at it and at the board
  await desk.goto(`/patients/${ids.bo}/forms`);
  await desk.waitForTimeout(300);
  const readiness = (await (await desk.request.get(`/api/patients/${ids.bo}/forms/check-in-readiness`)).json()) as { ready: boolean; items: { status: string }[] };
  expect(readiness.ready).toBe(false);
  expect(readiness.items.map((i) => i.status).sort()).toEqual(["Complete", "InProgress"]);
  evidence.board = { columns: 9, readiness: { ann: "2 of 2", bo: "1 of 2 (one started)", cy: "0 of 2" } };
});

test("FRONT OFFICE: the front desk confirms and checks in from the board; chairside buttons are not offered to them", async () => {
  const desk = pages.desk;
  await gotoBoard(desk);

  await press(desk, ids.a1, "Confirm", NAMES.ann);
  await expect(inColumn(desk, "Confirmed", NAMES.ann)).toBeVisible();
  expect((await load(desk, ids.a1)).flowState).toBe("Confirmed");
  await press(desk, ids.a1, "Check in", NAMES.ann);
  await expect(inColumn(desk, "Checked in", NAMES.ann)).toBeVisible();
  await expect(desk.getByText(`${NAMES.ann} is now checked in.`)).toBeVisible();
  expect((await load(desk, ids.a1)).flowState).toBe("CheckedIn");
  await expect(cardOf(desk, ids.a1).getByRole("button", { name: `Mark ready ${NAMES.ann}` })).toHaveCount(0); // chairside work is not the front desk's
  await expect(cardOf(desk, ids.a1)).toContainText("Required forms complete (2 of 2).");

  await press(desk, ids.a2, "Check in", NAMES.bo); // Bo arrives with a form still open: check-in is never blocked by it
  await expect(inColumn(desk, "Checked in", NAMES.bo)).toBeVisible();
  await expect(cardOf(desk, ids.a2)).toContainText("Forms: 1 of 2 complete.");
  await shot(desk, "02-checked-in.png");
  evidence.frontOffice = { confirmed: true, checkedIn: true, checkInNotBlockedByOpenForm: true, chairsideButtonsHidden: true };
});

test("CHAIRSIDE: the assistant marks patients ready and seats one; they cannot do the front desk's moves", async () => {
  const assistant = pages.assistant;
  await gotoBoard(assistant);
  await expect(cardOf(assistant, ids.a1).getByRole("button", { name: `Check in ${NAMES.ann}` })).toHaveCount(0);
  await expect(cardOf(assistant, ids.a1).getByRole("button", { name: `Confirm ${NAMES.ann}` })).toHaveCount(0);

  await press(assistant, ids.a1, "Mark ready", NAMES.ann);
  await expect(inColumn(assistant, "Ready", NAMES.ann)).toBeVisible();
  await press(assistant, ids.a1, "Seat patient", NAMES.ann);
  await expect(inColumn(assistant, "Seated", NAMES.ann)).toBeVisible();
  await expect(cardOf(assistant, ids.a1)).toContainText("Dr. Rivera · Op 1");
  await expect(cardOf(assistant, ids.a1)).toContainText(/Just moved to this step|In this step/); // the elapsed cue, from the server's clock
  await expect(cardOf(assistant, ids.a1)).not.toContainText("forms"); // the form cue is only for patients not yet seen
  expect((await load(assistant, ids.a1)).flowState).toBe("Seated");

  await press(assistant, ids.a2, "Mark ready", NAMES.bo);
  await expect(inColumn(assistant, "Ready", NAMES.bo)).toBeVisible();
  evidence.chairside = { ready: true, seated: true, frontOfficeButtonsHidden: true };
});

test("ROOM: a second patient cannot be seated in an occupied room; the refusal says so in words and nothing changes", async () => {
  const assistant = pages.assistant;
  await press(assistant, ids.a2, "Seat patient", NAMES.bo); // Bo is booked in Op 1, where Ann is seated

  await expect(cardOf(assistant, ids.a2).getByRole("alert")).toContainText("Op 1 already has a patient who is seated or in treatment. Nothing was changed.");
  await expect(inColumn(assistant, "Ready", NAMES.bo)).toBeVisible();
  expect((await load(assistant, ids.a2)).flowState).toBe("Ready");
  await shot(assistant, "03-room-occupied.png");
  await scanBothThemes(assistant, "room-occupied");

  const api = await move(assistant, ids.a2, "Seated", (await load(assistant, ids.a2)).rowVersion);
  expect([api.status, api.body.error, api.body.conflictingAppointmentId]).toEqual([409, "operatory_occupied", ids.a1]);
  evidence.room = { refused: true, namesOccupier: true };
});

test("WHOLE CHAIN: treatment, check-out and completion by four different people free the room and leave a complete, attributed record", async () => {
  const dentist = pages.dentist, desk = pages.desk, manager = pages.manager, assistant = pages.assistant;
  await gotoBoard(dentist);
  await press(dentist, ids.a1, "Start treatment", NAMES.ann);
  await expect(inColumn(dentist, "In treatment", NAMES.ann)).toBeVisible();

  await gotoBoard(desk);
  await press(desk, ids.a1, "Check out", NAMES.ann);
  await expect(inColumn(desk, "Checked out", NAMES.ann)).toBeVisible();
  await shot(desk, "04-checked-out.png");

  // Ann has left the chair, so the room is free and Bo can now be seated
  await gotoBoard(assistant);
  await press(assistant, ids.a2, "Seat patient", NAMES.bo);
  await expect(inColumn(assistant, "Seated", NAMES.bo)).toBeVisible();

  // completing is final, so the manager is asked first
  await gotoBoard(manager);
  await press(manager, ids.a1, "Complete visit", NAMES.ann);
  await expect(cardOf(manager, ids.a1)).toContainText("Complete the visit for Ann Arrival? This cannot be undone.");
  await cardOf(manager, ids.a1).getByRole("button", { name: "Keep it open" }).click();
  expect((await load(manager, ids.a1)).flowState).toBe("CheckedOut"); // declining changed nothing
  await press(manager, ids.a1, "Complete visit", NAMES.ann);
  await cardOf(manager, ids.a1).getByRole("button", { name: "Yes, complete the visit" }).click();
  await expect(inColumn(manager, "Completed", NAMES.ann)).toBeVisible();
  await shot(manager, "05-completed.png");

  const events = await historyOf(manager, ids.a1);
  expect(events.map((e) => e.eventType)).toEqual(["Scheduled", "Confirmed", "CheckedIn", "Ready", "Seated", "TreatmentStarted", "CheckedOut", "Completed"]);
  expect(events.map((e) => e.detail).slice(1)).toEqual(["Scheduled -> Confirmed", "Confirmed -> CheckedIn", "CheckedIn -> Ready", "Ready -> Seated", "Seated -> InTreatment", "InTreatment -> CheckedOut", "CheckedOut -> Completed"]);
  const actors = new Set(events.slice(1).map((e) => e.actorUserId));
  expect(actors).toEqual(new Set([users.desk, users.assistant, users.dentist, users.manager]));
  expect(events.every((e) => Date.now() - Date.parse(e.occurredAtUtc) < 60 * 60_000)).toBe(true);
  evidence.chain = { states: events.map((e) => e.eventType), distinctPeople: actors.size, completionAskedFirst: true, roomFreedForNextPatient: true };
});

test("ASSIGNMENT: the provider and operatory a patient is actually with can change without moving the booking; occupied rooms are refused for a seated patient", async () => {
  const assistant = pages.assistant, desk = pages.desk;
  // Di is checked in (not yet in a chair): reassign to Dr. Rivera in Op 1 from the board
  await gotoBoard(desk);
  await press(desk, ids.a4, "Check in", NAMES.di);
  await expect(inColumn(desk, "Checked in", NAMES.di)).toBeVisible();
  await gotoBoard(assistant);
  await cardOf(assistant, ids.a4).getByRole("button", { name: `Change room or provider for ${NAMES.di}` }).click();
  const form = cardOf(assistant, ids.a4).getByRole("form", { name: `Change room or provider for ${NAMES.di}` });
  await expect(form.getByRole("button", { name: "Save" })).toBeDisabled();
  await form.getByLabel("Provider").selectOption({ label: "Dr. Rivera" });
  await form.getByLabel("Operatory").selectOption({ label: "Op 1" });
  await form.getByRole("button", { name: "Save" }).click();
  await expect(cardOf(assistant, ids.a4)).toContainText("Dr. Rivera · Op 1");
  await expect(cardOf(assistant, ids.a4)).toContainText("Booked: Dr. Patel · Op 2");
  const stored = await load(assistant, ids.a4);
  expect([stored.providerName, stored.operatoryName, stored.visitProviderName, stored.visitOperatoryName]).toEqual(["Dr. Patel", "Op 2", "Dr. Rivera", "Op 1"]); // the booking did not move
  const calendar = (await (await assistant.request.get(`/api/appointments?from=${DAY}&to=${DAY}&includeAll=true`)).json()) as Appt[];
  expect(calendar.find((a) => a.id === ids.a4)!.providerName).toBe("Dr. Patel"); // and the calendar still places it by the booking
  await shot(assistant, "06-assigned.png");

  // Di is ready and tries to be seated in the room she was moved to: Bo is seated there
  await press(assistant, ids.a4, "Mark ready", NAMES.di);
  await expect(inColumn(assistant, "Ready", NAMES.di)).toBeVisible();
  await press(assistant, ids.a4, "Seat patient", NAMES.di);
  await expect(cardOf(assistant, ids.a4).getByRole("alert")).toContainText("Op 1 already has a patient who is seated or in treatment. Nothing was changed.");

  // a seated patient cannot be moved into an occupied room either (Cy seated in Op 2, moved toward Op 1 where Bo sits)
  for (const target of ["CheckedIn", "Ready", "Seated"]) {
    const r = await move(pages.admin, ids.a3, target, (await load(pages.admin, ids.a3)).rowVersion);
    expect(r.status, `${target}: ${JSON.stringify(r.body)}`).toBe(200);
  }
  await gotoBoard(assistant);
  await cardOf(assistant, ids.a3).getByRole("button", { name: `Change room or provider for ${NAMES.cy}` }).click();
  const cyForm = cardOf(assistant, ids.a3).getByRole("form", { name: `Change room or provider for ${NAMES.cy}` });
  await cyForm.getByLabel("Operatory").selectOption({ label: "Op 1" });
  await cyForm.getByRole("button", { name: "Save" }).click();
  await expect(cardOf(assistant, ids.a3).getByRole("alert")).toContainText("Op 1 already has a patient who is seated or in treatment. Nothing was changed.");
  expect((await load(assistant, ids.a3)).visitOperatoryName).toBe("Op 2");
  evidence.assignment = { visibleWithBooked: true, bookingUnmoved: true, occupiedRoomRefusedAtSeating: true, occupiedRoomRefusedWhenSeated: true };
});

test("CONFLICT: when someone else moves the patient first, the stale press is reported and not applied, and Reload shows the real state", async () => {
  const desk = pages.desk, desk2 = pages.desk2;
  await gotoBoard(desk);
  await expect(cardOf(desk, ids.a5)).toBeVisible();
  const confirmed = await move(desk2, ids.a5, "Confirmed", (await load(desk2, ids.a5)).rowVersion); // the second desk confirms Ed first
  expect(confirmed.status).toBe(200);

  await press(desk, ids.a5, "Check in", NAMES.ed); // the first desk's card still shows Ed as Scheduled, with the old version
  await expect(desk.getByText("Someone else changed this while you were editing")).toBeVisible();
  expect((await load(desk, ids.a5)).flowState).toBe("Confirmed"); // not checked in on top of the other desk's change
  await shot(desk, "07-conflict.png");
  await scanBothThemes(desk, "conflict");
  await desk.getByRole("button", { name: /Reload/ }).click();
  await expect(inColumn(desk, "Confirmed", NAMES.ed)).toBeVisible();
  await expect(desk.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  evidence.conflict = { reported: true, otherUsersChangeStood: true };
});

test("LIVE: what another workstation does shows up on an open board by itself, without a reload or a click", async () => {
  const desk = pages.desk, desk2 = pages.desk2;
  await gotoBoard(desk);
  await expect(inColumn(desk, "Confirmed", NAMES.ed)).toBeVisible();
  const before = await desk.getByText(/^Updated /).textContent();

  const r = await move(desk2, ids.a5, "CheckedIn", (await load(desk2, ids.a5)).rowVersion); // another workstation checks Ed in
  expect(r.status).toBe(200);

  await expect(inColumn(desk, "Checked in", NAMES.ed)).toBeVisible({ timeout: 25_000 }); // the board re-reads every 15 seconds
  await expect(inColumn(desk, "Confirmed", NAMES.ed)).toHaveCount(0);
  expect(await desk.getByText(/^Updated /).textContent()).not.toBe(before);
  evidence.live = { updatedWithoutReload: true, refreshSeconds: 15 };
});

test("CANCELLED AND NO-SHOW: they stay on the board, apart from the chain and distinct from completed", async () => {
  const desk = pages.desk;
  const extra = await book("ann", `${DAY}T16:00`, `board-a6-${stamp}`, ids.patel, ids.op2);
  const cancelled = await call(desk, "post", `/api/appointments/${extra.id}/cancel`, { reason: "Illness", rowVersion: extra.rowVersion });
  expect(cancelled.status).toBe(200);
  await gotoBoard(desk);
  const closed = desk.getByRole("region", { name: "Cancelled and no-show (1)" });
  await expect(closed.getByRole("heading", { name: NAMES.ann })).toBeVisible();
  await expect(closed).toContainText("· Cancelled");
  await expect(closed.getByRole("button")).toHaveCount(0);
  await expect(desk.getByRole("region", { name: "Completed (1)" }).getByRole("heading", { name: NAMES.ann })).toBeVisible(); // the completed visit is somewhere else
  await shot(desk, "08-cancelled-apart.png");
  evidence.closedApart = { cancelledDistinctFromCompleted: true };
});

test("LEFT OPEN: a visit still open from an earlier day appears on today's board marked as carried over, and the room it holds stays blocked", async () => {
  const desk = pages.desk;
  // Cy is seated in Op 2: make that appointment look like it was left open from days ago (the API refuses to book in the past)
  sql(`UPDATE Appointments SET StartUtc = DATEADD(hour, -72, SYSDATETIMEOFFSET()), EndUtc = DATEADD(hour, -71, SYSDATETIMEOFFSET()) WHERE Id = '${ids.a3}'`);
  const board = (await (await desk.request.get("/api/visits/board")).json()) as { date: string; visits: { carriedOver: boolean; appointment: { id: string } }[] };
  expect(board.visits.filter((v) => v.carriedOver).map((v) => v.appointment.id)).toEqual([ids.a3]);

  await gotoBoard(desk, null); // today
  await expect(inColumn(desk, "Seated", NAMES.cy)).toBeVisible();
  await expect(cardOf(desk, ids.a3)).toContainText(/Carried over from \d{4}-\d{2}-\d{2} and still open\./);
  await shot(desk, "09-carried-over.png");
  await scanBothThemes(desk, "carried-over");

  // and it is not decoration: the room it holds refuses another patient
  const target = await book("ed", `${DAY}T12:00`, `board-a7-${stamp}`, ids.patel, ids.op2); // booked in Op 2 at another time, so only the room rule can stop it
  for (const s of ["CheckedIn", "Ready"]) expect((await move(pages.admin, target.id, s, (await load(pages.admin, target.id)).rowVersion)).status).toBe(200);
  const blocked = await move(pages.admin, target.id, "Seated", (await load(pages.admin, target.id)).rowVersion);
  expect([blocked.status, blocked.body.error, blocked.body.conflictingAppointmentId]).toEqual([409, "operatory_occupied", ids.a3]);
  evidence.leftOpen = { carriedOver: true, roomStillBlocked: true };
});

test("PERMISSIONS: the dentist moves patients chairside only; billing has no board and no way in; STORY-011's own endpoints are unchanged", async () => {
  const dentist = pages.dentist, billing = pages.billing, desk = pages.desk;
  await gotoBoard(dentist);
  await expect(dentist.getByRole("button", { name: /^Confirm / })).toHaveCount(0);
  await expect(dentist.getByRole("button", { name: /^Check in / })).toHaveCount(0);
  await expect(cardOf(dentist, ids.a5).getByRole("button", { name: `Mark ready ${NAMES.ed}` })).toBeVisible();

  await expect(billing.getByRole("navigation", { name: "Primary navigation" }).getByRole("link", { name: "Visit board" })).toHaveCount(0);
  await billing.goto("/flow");
  await expect(billing.getByText(/don't have permission/i)).toBeVisible();
  expect((await billing.request.get("/api/visits/board")).status()).toBe(403);
  expect((await move(billing, ids.a5, "Ready", "AAAA")).status).toBe(403);
  const forbidden = await move(desk, ids.a5, "Ready", (await load(desk, ids.a5)).rowVersion); // front desk cannot do chairside work either
  expect([forbidden.status, forbidden.body.required]).toEqual([403, "UpdateChairsideFlow"]);

  // STORY-011's endpoints on api/appointments keep their own rule: ManageAppointments (front desk yes, dentist no)
  const legacyDentist = await call(dentist, "post", `/api/appointments/${ids.a5}/check-in`, { rowVersion: (await load(dentist, ids.a5)).rowVersion });
  expect([legacyDentist.status, legacyDentist.body.required]).toEqual([403, "ManageAppointments"]);
  const fresh = await book("bo", `${DAY}T08:00`, `board-a8-${stamp}`, ids.patel, ids.op2);
  const legacy = await call(desk, "post", `/api/appointments/${fresh.id}/check-in`, { rowVersion: fresh.rowVersion });
  expect([legacy.status, (legacy.body as { flowState: string }).flowState]).toEqual([200, "CheckedIn"]);
  ids.a8 = fresh.id;
  evidence.permissions = { dentistChairsideOnly: true, billingApiStatus: 403, frontDeskCannotDoChairside: 403, story011EndpointsUnchanged: true };
});

test("KEYBOARD: a move can be made without a mouse and focus stays on the visit", async () => {
  const desk = pages.desk;
  const a = await book("di", `${DAY}T14:00`, `board-a9-${stamp}`, ids.patel, ids.op2);
  await gotoBoard(desk);
  await cardOf(desk, a.id).getByRole("button", { name: `Confirm ${NAMES.di}` }).focus();
  await desk.keyboard.press("Enter");
  await expect(inColumn(desk, "Confirmed", NAMES.di)).toBeVisible();
  await expect(cardOf(desk, a.id).getByRole("heading", { name: NAMES.di })).toBeFocused(); // the pressed button is gone, focus is not lost
  evidence.keyboard = { confirmedWithKeyboard: true, focusKept: true };
});

test("RESPONSIVE: at tablet and phone width the page does not scroll sideways, a phone stacks the columns, and a card's actions stay reachable", async () => {
  const desk = pages.desk;
  await gotoBoard(desk);
  const reachable = async (width: number) => {
    const button = cardOf(desk, ids.a8).getByRole("button", { name: `Change room or provider for ${NAMES.bo}` });
    await button.scrollIntoViewIfNeeded();
    await expect(button).toBeVisible();
    const box = await button.boundingBox();
    expect(box, `${width}px: the button has a position`).toBeTruthy();
    expect(box!.x, `${width}px: the button starts on screen`).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width, `${width}px: the button ends on screen`).toBeLessThanOrEqual(width + 1);
  };
  for (const [width, height] of [[768, 1000], [390, 844]] as const) {
    await desk.setViewportSize({ width, height });
    await desk.waitForTimeout(250);
    const overflow = await desk.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `${width}px: the page itself must not scroll sideways (only the board's own columns may)`).toBeLessThanOrEqual(1);
  }
  // on a phone the columns sit one above the other, each using the screen width, and the card's actions can be reached
  const first = await column(desk, "Confirmed").boundingBox();
  const second = await column(desk, "Checked in").boundingBox();
  expect(first && second).toBeTruthy();
  expect(Math.abs(first!.x - second!.x), "stacked columns share a left edge").toBeLessThanOrEqual(1);
  expect(second!.y, "the second column is below the first").toBeGreaterThan(first!.y);
  expect(first!.width, "a stacked column uses the screen width").toBeGreaterThan(300);
  await reachable(390);
  await shot(desk, "10-phone.png");
  await scanBothThemes(desk, "board-phone");
  await desk.setViewportSize({ width: 1280, height: 720 });
  evidence.responsive = { tabletNoSidewaysPageScroll: true, phoneStacked: true, phoneActionsReachable: true, phoneAxe: true };
});

test("ACCEPTANCE: every state and assignment change is in the audit log with user and time and no patient details, once per move", async () => {
  const entries = (await (await pages.admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const by = (t: string) => entries.filter((e) => e.eventType === t);
  expect(by("PatientConfirmed").length).toBeGreaterThanOrEqual(3);
  expect(by("PatientCheckedIn").length).toBeGreaterThanOrEqual(5);
  expect(by("PatientReady").length).toBeGreaterThanOrEqual(3);
  expect(by("PatientSeated").length).toBeGreaterThanOrEqual(3);
  expect(by("PatientTreatmentStarted").length).toBe(1);
  expect(by("PatientCheckedOut").length).toBe(1);
  expect(by("PatientTreatmentCompleted").length).toBe(1);
  expect(by("VisitAssignmentChanged").length).toBe(1);
  expect(by("FormTemplateRequirementChanged").length).toBe(2);
  const mine = entries.filter((e) => /^(Patient(Confirmed|CheckedIn|Ready|Seated|TreatmentStarted|CheckedOut|TreatmentCompleted)|VisitAssignmentChanged|FormTemplateRequirementChanged)$/.test(e.eventType));
  for (const e of mine) {
    expect(e.performedByUserAccountId).toBeTruthy();
    expect(Date.now() - Date.parse(e.timestampUtc)).toBeLessThan(60 * 60_000);
    expect(e.details).not.toMatch(/Ann|Bo |Cy |Di |Ed |Arrival|Bridge|Crown|Denture|Enamel|Rivera|Patel|555-|Illness/);
  }
  expect(new Set(by("PatientSeated").map((e) => e.performedByUserAccountId)).size).toBeGreaterThanOrEqual(1);
  evidence.audit = Object.fromEntries(["PatientConfirmed", "PatientCheckedIn", "PatientReady", "PatientSeated", "PatientTreatmentStarted", "PatientCheckedOut", "PatientTreatmentCompleted", "VisitAssignmentChanged", "FormTemplateRequirementChanged"].map((t) => [t, by(t).length]));
});
