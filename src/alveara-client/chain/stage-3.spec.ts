import { test, expect } from "@playwright/test";
import type { Browser, Page } from "@playwright/test";
import {
  ACCOUNTS, ADMIN, ADMIN_PASSWORD, DAY, advanceClock, blockOf, book, call, clockNow, dayList, drawerOf, gotoCalendar, historyOf, load, lookUpPractice, session, signIn,
} from "./stage3-support";
import type { AccountKey, Practice } from "./stage3-support";

// ALV-N021 A2: stage 3 (scheduling) of the accumulated chain, R05 cases 3.01-3.10, 3.13, 3.14 (PKG-05). Runs only through scripts/Invoke-AccumulatedChain.ps1, after stage 0 (administrator)
// and stage 1 (Alice Anderson and Brian Baker). On the injected clock "now" is Thursday 2026-01-15 09:00 Chicago; 3.09 moves it forward through the chain host's test-only clock-advance route
// (owner decision 2026-11-15: no SQL time shift). Step titles start with their source id; the evidence builder reads it. Stage 4 (visit flow) continues from what this stage leaves.

test.describe.configure({ mode: "serial" });

let browserRef: Browser;
let admin: Page;
let p: Practice;
const pages: Partial<Record<AccountKey, Page>> = {};
const at: Record<string, string> = {};   // appointment ids by role in the story

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  const context = await browser.newContext();
  admin = await context.newPage();
  await signIn(admin, ADMIN, ADMIN_PASSWORD);
});

test("3.00 setup: the practice (Dr. Dana and Harper with real accounts, Operatory 1, Hygiene 1, a lunch block), six stage-3 staff accounts and two more patients", async () => {
  const clock = await clockNow(admin);
  expect([clock.mode, clock.utcNow.slice(0, 16)]).toEqual(["injected", "2026-01-15T15:00"]);
  const register = async (key: AccountKey) => {
    const [username, role] = ACCOUNTS[key];
    const reg = await call(admin, "post", "/api/auth/register", { username, password: "stage3-user-password-1!" });
    expect(reg.status, `register ${username}`).toBeLessThan(300);
    const id = reg.body.id as string;
    expect((await call(admin, "put", `/api/auth/${id}/role`, { role })).status).toBe(200);
    expect((await call(admin, "put", `/api/auth/${id}/enabled`, { enabled: true })).status).toBe(200);
    return id;
  };
  const ids = {} as Record<AccountKey, string>;
  for (const key of Object.keys(ACCOUNTS) as AccountKey[]) ids[key] = await register(key);

  expect((await call(admin, "post", "/api/config/locations", { name: "Chain Dental" })).status).toBe(201);
  for (const name of ["Operatory 1", "Hygiene 1"]) expect((await call(admin, "post", "/api/config/operatories", { name })).status).toBe(201);
  expect((await call(admin, "post", "/api/config/appointment-types", { name: "New patient exam", defaultDurationMinutes: 60 })).status).toBe(201);
  for (const [name, title, account] of [["Dr. Dana", "Dentist", ids.dentist], ["Harper", "Hygienist", ids.hygienist]] as const) {
    const staff = await call(admin, "post", "/api/config/staff", { displayName: name, jobTitle: title, userAccountId: account });
    expect(staff.status, JSON.stringify(staff.body)).toBe(201);
    const provider = await call(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: title });
    expect(provider.status).toBe(201);
    const id = provider.body.id as string;
    const current = (await (await admin.request.get(`/api/config/providers/${id}/availability`)).json()) as { revision: number };
    const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
    expect((await call(admin, "put", `/api/config/providers/${id}/availability`, { windows, revision: current.revision })).status).toBe(200);
    if (name === "Dr. Dana") expect((await call(admin, "post", `/api/config/providers/${id}/blocked-time`, { startLocal: `${DAY}T12:00`, endLocal: `${DAY}T13:00`, reason: "Lunch", calendarLabel: "lunch" })).status).toBe(201);
  }
  for (const [first, last, dob] of [["Cara", "Carter", "1992-04-04"], ["Dev", "Dalton", "1988-08-08"]] as const) {
    const r = await call(admin, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone: "555-010-0103", addressLine1: "2 Main St", city: "Springfield", state: "IL", postalCode: "62701" }, { "Idempotency-Key": `chain-stage3-${first}` });
    expect(r.status).toBe(201);
  }
  p = await lookUpPractice(admin);
  for (const key of Object.keys(ACCOUNTS) as AccountKey[]) pages[key] = (await session(browserRef, ACCOUNTS[key][0])).page;
});

test("3.01 the desk books Alice, New patient exam, Dr. Dana, Operatory 1 at 10:00 today: 201 and it appears in the calendar query and on the calendar", async () => {
  const r = await book(pages.desk!, p, "alice", "10:00", p.dana, p.op1, "s3-alice-1000");
  expect(r.status, JSON.stringify(r.body)).toBe(201);
  at.alice = r.body.id as string;
  expect((await dayList(pages.desk!)).map((a) => a.id)).toContain(at.alice);
  await gotoCalendar(pages.desk!);
  await expect(blockOf(pages.desk!, at.alice)).toHaveAttribute("aria-label", /10:00 to 11:00, Alice Anderson, New patient exam, Dr\. Dana, Operatory 1/);
});

test("3.02 booking Brian with Dr. Dana at 10:30 (overlaps 3.01) is refused: provider double-booked, nothing created", async () => {
  const before = (await dayList(admin)).length;
  const r = await book(pages.desk!, p, "brian", "10:30", p.dana, p.hyg1, "s3-302");
  expect([r.status, r.body.error, r.body.conflictingAppointmentId]).toEqual([409, "provider_double_booked", at.alice]);
  expect((await dayList(admin)).length).toBe(before);
});

test("3.03 booking Brian with Harper in Operatory 1 at 10:00 is refused: operatory in use", async () => {
  const r = await book(pages.desk!, p, "brian", "10:00", p.harper, p.op1, "s3-303");
  expect([r.status, r.body.error]).toEqual([409, "operatory_conflict"]);
});

test("3.04 booking Alice again at 10:15 with Harper in Hygiene 1 is refused: patient double-booked", async () => {
  const r = await book(pages.desk!, p, "alice", "10:15", p.harper, p.hyg1, "s3-304");
  expect([r.status, r.body.error]).toEqual([409, "patient_double_booked"]);
});

test("3.05 07:00 (before hours) and 12:30 (Dr. Dana's blocked lunch) are each refused with the reason", async () => {
  // 07:00 TODAY is already in the past on the injected clock (09:00), which is refused first; before-hours is shown on the next weekday
  const early = await book(pages.desk!, p, "brian", "07:00", p.dana, p.op1, "s3-305a", "2026-01-16");
  expect([early.status, early.body.error, early.body.reason]).toEqual([409, "provider_unavailable", "outside_working_hours"]);
  const lunch = await book(pages.desk!, p, "brian", "12:30", p.dana, p.op1, "s3-305b");
  expect([lunch.status, lunch.body.error, lunch.body.reason]).toEqual([409, "provider_unavailable", "blocked_time"]);
});

test("3.06 booking in the past (08:30 today, the injected clock says 09:00) is refused", async () => {
  const r = await book(pages.desk!, p, "brian", "08:30", p.dana, p.op1, "s3-306");
  expect([r.status, r.body.error]).toEqual([400, "start_in_past"]);
});

test("3.07 a back-to-back booking (Brian 11:00, when Alice's 10:00 ends) is allowed", async () => {
  const r = await book(pages.desk!, p, "brian", "11:00", p.dana, p.op1, "s3-brian-1100");
  expect(r.status, JSON.stringify(r.body)).toBe(201);
  at.brian = r.body.id as string;
});

test("3.08 rescheduling Alice to 13:30 moves her and the history records the old slot and who moved it (F-049)", async () => {
  const a = await load(pages.desk!, at.alice);
  const r = await call(pages.desk!, "put", `/api/appointments/${at.alice}/reschedule`, { providerId: p.dana, operatoryId: p.op1, startLocal: `${DAY}T13:30`, durationMinutes: null, rowVersion: a.rowVersion });
  expect(r.status, JSON.stringify(r.body)).toBe(200);
  expect((r.body as unknown as { startLocal: string }).startLocal).toBe(`${DAY}T13:30`);
  const moved = (await historyOf(pages.desk!, at.alice)).find((e) => e.eventType === "Rescheduled");
  expect([moved?.previousStartLocal, moved?.newStartLocal, moved?.actorUserId]).toEqual([`${DAY}T10:00`, `${DAY}T13:30`, p.users.desk]);   // from and to, recorded with the move (M4)
  expect(moved?.actorName).toBeTruthy();                                                                                                    // and the person is named (F-046)
});

test("3.09 cancel needs a category and frees the slot; a no-show is refused before the start, then allowed once the injected clock has passed it, and frees the slot", async () => {
  const desk = pages.desk!;
  const b = await load(desk, at.brian);
  expect((await call(desk, "post", `/api/appointments/${at.brian}/cancel`, { note: "Called", rowVersion: b.rowVersion })).body.error).toBe("category_required");
  const cancelled = await call(desk, "post", `/api/appointments/${at.brian}/cancel`, { category: "patient", note: "Called to cancel", rowVersion: b.rowVersion });
  expect([cancelled.status, (cancelled.body as { cancelCategory?: string }).cancelCategory]).toEqual([200, "patient"]);
  const retake = await book(desk, p, "cara", "11:00", p.dana, p.op1, "s3-cara-1100");   // the cancelled slot is free again
  expect(retake.status, JSON.stringify(retake.body)).toBe(201);
  at.cara = retake.body.id as string;

  // Dev at 09:30 with Harper: before the start the control is disabled with the server's reason, and the server refuses
  const dev = await book(desk, p, "dev", "09:30", p.harper, p.hyg1, "s3-dev-0930");
  expect(dev.status, JSON.stringify(dev.body)).toBe(201);
  at.dev = dev.body.id as string;
  await gotoCalendar(desk);
  await blockOf(desk, at.dev).click();
  const noShow = drawerOf(desk).getByRole("button", { name: "Mark no-show" });
  await expect(noShow).toBeDisabled();
  await expect(noShow).toHaveAccessibleDescription("An appointment can only be marked as a no-show once its start time has passed.");
  const early = await call(desk, "post", `/api/appointments/${at.dev}/no-show`, { rowVersion: (await load(desk, at.dev)).rowVersion });
  expect([early.status, early.body.error]).toEqual([409, "no_show_too_early"]);
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();

  // the injected clock moves to 09:45: Dev's start has passed
  const advanced = await advanceClock(admin, 45);
  expect([advanced.previousUtc.slice(0, 16), advanced.advancedByMinutes, advanced.utcNow.slice(0, 16)]).toEqual(["2026-01-15T15:00", 45, "2026-01-15T15:45"]);
  expect((await clockNow(admin)).utcNow.slice(0, 16)).toBe("2026-01-15T15:45");

  await gotoCalendar(desk);
  await blockOf(desk, at.dev).click();
  await expect(drawerOf(desk).getByRole("button", { name: "Mark no-show" })).toBeEnabled();
  await drawerOf(desk).getByRole("button", { name: "Mark no-show" }).click();
  await expect(drawerOf(desk).getByText(/^Marked as a no-show\./).first()).toBeVisible();
  // ALV-N021 A2 (F-073): the final no-show offers Rebook and Open patient, from the server's actions list
  const next = drawerOf(desk).getByRole("group", { name: "Next steps" });
  await expect(next.getByRole("button", { name: "Rebook" })).toBeVisible();
  await expect(next.getByRole("link", { name: "Open patient" })).toHaveAttribute("href", /^\/patients\/[0-9a-f-]{36}$/);
  await drawerOf(desk).getByRole("button", { name: "Close drawer" }).click();
  await expect(blockOf(desk, at.dev)).toHaveClass(/cal__block--noshow/);
  expect((await load(desk, at.dev)).status).toBe("NoShow");
  expect((await historyOf(desk, at.dev)).filter((e) => e.eventType === "NoShow").map((e) => e.actorUserId)).toEqual([p.users.desk]);
  const after = await book(desk, p, "brian", "10:00", p.harper, p.hyg1, "s3-brian-1000");   // Dev's 09:30-10:30 no longer holds Harper or Hygiene 1
  expect(after.status, JSON.stringify(after.body)).toBe(201);
});

test("3.10 two windows book the same free slot (Dr. Dana, Operatory 1, 15:00) from the Calendar at once: exactly one appointment, and the other user sees the refusal", async () => {
  const fill = async (page: Page, search: string, patient: string) => {
    await gotoCalendar(page);
    await page.getByRole("button", { name: "New appointment" }).click();
    const form = drawerOf(page).getByRole("form", { name: "New appointment" });
    await form.getByRole("searchbox", { name: "Patient" }).fill(search);
    await form.getByRole("button", { name: new RegExp(patient) }).click();
    await form.getByLabel("Appointment type *").selectOption({ label: "New patient exam (60 min)" });
    await form.getByLabel("Provider *").selectOption({ label: "Dr. Dana" });
    await form.getByLabel("Operatory *").selectOption({ label: "Operatory 1" });
    await form.getByLabel("Date *").fill(DAY);
    await form.getByLabel("Start time *").fill("15:00");
    return form;
  };
  const first = await fill(pages.desk!, "Dev", "Dev Dalton");
  const second = await fill(pages.desk2!, "Brian", "Brian Baker");
  await Promise.all([first.getByRole("button", { name: "Book appointment" }).click(), second.getByRole("button", { name: "Book appointment" }).click()]);
  // a click returns when the button is pressed, not when the booking is answered: wait until each window shows its outcome (the booked appointment, or a refusal)
  const settled = (page: Page, patient: string) => expect(drawerOf(page).getByRole("heading", { name: patient }).or(drawerOf(page).getByRole("alert"))).toBeVisible({ timeout: 15_000 });
  await Promise.all([settled(pages.desk!, "Dev Dalton"), settled(pages.desk2!, "Brian Baker")]);

  const at1500 = (await dayList(admin)).filter((a) => a.status === "Scheduled" && a.startLocal === `${DAY}T15:00` && a.providerId === p.dana);
  expect(at1500).toHaveLength(1);
  at.slot1500 = at1500[0].id;
  const loser = at1500[0].patientName === "Dev Dalton" ? pages.desk2! : pages.desk!;
  await expect(drawerOf(loser).getByRole("alert")).toContainText(/Dr\. Dana already has an appointment|Operatory 1 is already in use/);
  await expect(drawerOf(loser).getByRole("alert")).toContainText("Nothing was booked.");   // ALV-N021 A2 (F-072): a refused booking says booked
});

test("3.13 day and week views, the provider and operatory filters and Today change what is shown; working hours are marked", async () => {
  const desk = pages.desk!;
  await gotoCalendar(desk);
  await expect(desk.getByRole("heading", { name: /2026-01-15/ })).toBeVisible();
  await expect(desk.locator('[data-working="08:00-17:00"]')).toHaveCount(2);   // each provider's weekly hours on the day view
  const dayBlocks = await desk.locator(".cal__block").count();
  expect(dayBlocks).toBe((await dayList(desk)).length);

  await desk.getByLabel("Provider", { exact: true }).selectOption({ label: "Harper" });
  await expect(desk.locator(".cal__column")).toHaveCount(1);
  await expect(desk.locator(".cal__block")).toHaveCount((await dayList(desk)).filter((a) => a.providerId === p.harper).length);
  await desk.getByLabel("Provider", { exact: true }).selectOption({ label: "All providers" });
  await desk.getByLabel("Operatory", { exact: true }).selectOption({ label: "Operatory 1" });
  await expect(desk.locator(".cal__block")).toHaveCount((await dayList(desk)).filter((a) => a.operatoryId === p.op1).length);
  await desk.getByLabel("Operatory", { exact: true }).selectOption({ label: "All operatories" });

  await desk.getByRole("button", { name: "Week" }).click();
  await expect(desk.getByRole("heading", { name: "Week of 2026-01-12 to 2026-01-18" })).toBeVisible();
  await expect(desk.locator(".cal__block")).toHaveCount(dayBlocks);   // nothing else is booked that week

  // Today: the page's "today" is the BROWSER's practice-zone day (A3 scope), not the injected one; it moves the view there
  const browserToday = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Chicago" }).format(new Date());
  await desk.getByRole("button", { name: "Today" }).click();
  await expect(desk.getByLabel("Date", { exact: true })).toHaveValue(browserToday);
});

test("3.14 the Schedule page lists the same appointments for the day as the calendar query", async () => {
  const desk = pages.desk!;
  await desk.goto("/schedule");
  await desk.getByLabel("Day", { exact: true }).fill(DAY);
  const scheduled = await dayList(desk, false);   // the Schedule page lists booked (not cancelled / no-show) appointments
  const rows = desk.getByRole("table", { name: /Booked appointments/ }).locator("tbody tr");
  await expect(rows).toHaveCount(scheduled.length);
  for (const a of scheduled) await expect(desk.getByRole("table", { name: /Booked appointments/ })).toContainText(`${a.startLocal.slice(11)}–${a.endLocal.slice(11)}`);
  // ALV-N021 A2 (F-069): a row opens its appointment in the calendar - the drawer of exactly that appointment
  const first = scheduled[0];
  await desk.getByRole("link", { name: new RegExp(`^${first.startLocal.slice(11)}–${first.endLocal.slice(11)}: open the appointment of `) }).first().click();
  await expect(desk).toHaveURL(new RegExp(`/calendar\\?day=${DAY}&appointment=`));
  await expect(drawerOf(desk).getByRole("heading", { level: 2 })).toBeVisible();
});
