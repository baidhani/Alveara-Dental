import { test, expect } from "@playwright/test";
import type { Browser, Page } from "@playwright/test";
import { ACCOUNTS, ADMIN, ADMIN_PASSWORD, DAY, book, call, cardOf, findAt, gotoBoard, historyOf, load, lookUpPractice, move, session, signIn, walk } from "./stage3-support";
import type { AccountKey, Practice } from "./stage3-support";

// ALV-N021 A2: stage 4 (visit flow) of the accumulated chain, R05 cases 3.11 and 3.12 (PKG-05), under the A2 rules: one path, the booked provider treats, completion waits for the checkout, a
// provider change moves the booking, blocked time flags what it covers. Runs after stage 3 (whose bookings it finds through the API); "now" is the injected Thursday 09:45 left by 3.09.
// Nothing here relies on the history's ORDER: on the injected clock events share one time and the history lists equal times by type.

test.describe.configure({ mode: "serial" });

let admin: Page;
let p: Practice;
const pages: Partial<Record<AccountKey, Page>> = {};
let alice = "";
let cara = "";

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  const context = await browser.newContext();
  admin = await context.newPage();
  await signIn(admin, ADMIN, ADMIN_PASSWORD);
  p = await lookUpPractice(admin);
  for (const key of Object.keys(ACCOUNTS) as AccountKey[]) pages[key] = (await session(browser, ACCOUNTS[key][0])).page;
  alice = (await findAt(admin, "Alice Anderson", "13:30")).id;
  cara = (await findAt(admin, "Cara Carter", "11:00")).id;
});

test("3.11 the desk confirms and checks Alice in from the Visit board (no check-in before confirmation); the history names who did it", async () => {
  const desk = pages.desk!;
  await gotoBoard(desk);
  await expect(cardOf(desk, alice).getByRole("button", { name: "Check in Alice Anderson" })).toHaveCount(0);
  await cardOf(desk, alice).getByRole("button", { name: "Confirm Alice Anderson" }).click();
  await cardOf(desk, alice).getByRole("button", { name: "Check in Alice Anderson" }).click();
  await expect(desk.getByText("Alice Anderson is now checked in. Waiting for chairside staff to mark the patient ready.")).toBeVisible();
  const events = await historyOf(desk, alice);
  expect(events.find((e) => e.eventType === "CheckedIn")?.actorUserId).toBe(p.users.desk);
  expect(events.find((e) => e.eventType === "CheckedIn")?.detail).toMatch(/^Confirmed -> CheckedIn/);
});

test("3.12 per role the moves are allowed or refused as the transition table says; moving to the chair never changes the booking", async () => {
  const { desk, assistant, hygienist, dentist } = pages as Record<AccountKey, Page>;
  const ready = await move(desk, alice, "Ready", (await load(desk, alice)).rowVersion);
  expect([ready.status, ready.body.reason]).toEqual([403, "chairside_only"]);
  await gotoBoard(assistant);
  await cardOf(assistant, alice).getByRole("button", { name: "Mark ready Alice Anderson" }).click();
  await cardOf(assistant, alice).getByRole("button", { name: "Seat patient Alice Anderson" }).click();
  await expect(cardOf(assistant, alice)).toContainText("Waiting for the booked provider to start treatment.");
  const seated = await load(assistant, alice);
  expect([seated.flowState, seated.providerName, seated.operatoryName, seated.startLocal]).toEqual(["Seated", "Dr. Dana", "Operatory 1", `${DAY}T13:30`]);
  const notHers = await move(hygienist, alice, "InTreatment", seated.rowVersion);   // Harper is a provider, but not Alice's booked one
  expect([notHers.status, notHers.body.reason]).toEqual([403, "booked_provider_only"]);
  const byAssistant = await move(assistant, alice, "InTreatment", seated.rowVersion);
  expect(byAssistant.status).toBe(403);
  await gotoBoard(dentist);
  await cardOf(dentist, alice).getByRole("button", { name: "Start treatment Alice Anderson" }).click();
  await cardOf(dentist, alice).getByRole("button", { name: "End treatment / ready for checkout Alice Anderson" }).click();
  await expect(cardOf(dentist, alice)).toHaveAttribute("data-flow", "CheckedOut");
});

test("3.12 a blocked time over a visit under way is refused with the visit listed, even when confirmed; nothing is saved (R03 b3)", async () => {
  const url = `/api/config/providers/${p.dana}/blocked-time`;
  const before = ((await (await admin.request.get(url)).json()) as unknown[]).length;
  for (const confirmOverlaps of [false, true]) {
    const r = await call(admin, "post", url, { startLocal: `${DAY}T13:00`, endLocal: `${DAY}T14:30`, reason: "Meeting", confirmOverlaps });
    expect([r.status, r.body.error], `confirm=${confirmOverlaps}`).toEqual([409, "block_overlaps_active_visit"]);
    expect((r.body.overlaps as { appointmentId: string }[]).map((o) => o.appointmentId)).toEqual([alice]);
  }
  expect(((await (await admin.request.get(url)).json()) as unknown[]).length).toBe(before);
});

test("3.12 Complete waits for the checkout; the desk resolves it on the board and the manager completes Alice's visit", async () => {
  const { desk, manager } = pages as Record<AccountKey, Page>;
  await gotoBoard(manager);
  await expect(cardOf(manager, alice).getByRole("button", { name: "Complete visit Alice Anderson" })).toBeDisabled();
  const refused = await move(manager, alice, "Completed", (await load(manager, alice)).rowVersion);
  expect([refused.status, refused.body.error]).toEqual([409, "checkout_incomplete"]);

  await gotoBoard(desk);
  await cardOf(desk, alice).getByRole("button", { name: "Show checkout for Alice Anderson" }).click();
  const panel = cardOf(desk, alice).getByRole("region", { name: "Checkout for Alice Anderson" });
  for (const [form, outcome] of [[/patient has left/i, "Patient has left"], [/follow-up/i, "Patient declined"]] as const) {
    await panel.getByRole("form", { name: form }).getByRole("combobox").selectOption({ label: outcome });
    await panel.getByRole("form", { name: form }).getByRole("button", { name: "Record" }).click();
    await expect(panel).toContainText(`${outcome === "Patient has left" ? "Patient has left" : "Follow-up"} recorded.`);
  }
  await gotoBoard(manager);
  await cardOf(manager, alice).getByRole("button", { name: "Complete visit Alice Anderson" }).click();
  await cardOf(manager, alice).getByRole("button", { name: "Yes, complete the visit" }).click();
  await expect(cardOf(manager, alice)).toHaveAttribute("data-flow", "Completed");
  const types = (await historyOf(manager, alice)).map((e) => e.eventType);
  for (const t of ["Confirmed", "CheckedIn", "Ready", "Seated", "TreatmentStarted", "CheckedOut", "Completed"]) expect(types, t).toContain(t);
  expect(types.filter((t) => t === "CheckoutRecorded")).toHaveLength(2);
});

test("3.12 the manager moves Cara's booking to Harper in Hygiene 1 with a reason (the booking moves; Harper is told); a chairside person may change only the room", async () => {
  const { manager, assistant, hygienist } = pages as Record<AccountKey, Page>;
  await gotoBoard(assistant);
  await expect(cardOf(assistant, cara).getByRole("button", { name: "Change room for Cara Carter" })).toBeVisible();
  await expect(cardOf(assistant, cara).getByRole("button", { name: "Change provider or room for Cara Carter" })).toHaveCount(0);

  const noReason = await call(manager, "put", `/api/visits/${cara}/assignment`, { providerId: p.harper, operatoryId: p.hyg1, rowVersion: (await load(manager, cara)).rowVersion });
  expect([noReason.status, noReason.body.error]).toEqual([400, "reason_required"]);
  const moved = await call(manager, "put", `/api/visits/${cara}/assignment`, { providerId: p.harper, operatoryId: p.hyg1, rowVersion: (await load(manager, cara)).rowVersion, reason: "Dr. Dana is in surgery" });
  expect(moved.status, JSON.stringify(moved.body)).toBe(200);
  const now = await load(manager, cara);
  expect([now.providerName, now.operatoryName]).toEqual(["Harper", "Hygiene 1"]);
  const change = (await historyOf(manager, cara)).find((e) => e.eventType === "ProviderReassigned");
  expect([change?.actorUserId, change?.reason]).toEqual([p.users.manager, "Dr. Dana is in surgery"]);
  const notices = await (await hygienist.request.get("/api/notifications?status=unread")).text();
  expect(notices).toMatch(/"kind":"appointment\.provider_reassigned"[^}]*"priority":"Urgent"/);
  expect(notices).not.toContain("Cara");
});

test("3.12 the desk checks Cara in and undoes it with a reason; the check-in stays in the history and she can be checked in again", async () => {
  const desk = pages.desk!;
  await walk(desk, cara, ["Confirmed", "CheckedIn"]);
  const noReason = await call(desk, "post", `/api/visits/${cara}/undo-check-in`, { rowVersion: (await load(desk, cara)).rowVersion });
  expect(noReason.status).toBe(400);
  const undone = await call(desk, "post", `/api/visits/${cara}/undo-check-in`, { reason: "Wrong patient called", rowVersion: (await load(desk, cara)).rowVersion });
  expect([undone.status, (undone.body as { flowState: string }).flowState]).toEqual([200, "Confirmed"]);
  const events = await historyOf(desk, cara);
  expect(events.filter((e) => e.eventType === "CheckedIn")).toHaveLength(1);
  expect(events.find((e) => e.eventType === "CheckInUndone")?.reason).toBe("Wrong patient called");
  await walk(desk, cara, ["CheckedIn"]);
});

test("3.12 blocked time over a booked appointment needs a confirmation and a reason, flags it, refuses its check-in until the desk keeps it", async () => {
  const desk = pages.desk!;
  // Alice: not in 3.10's race (whose winner, Dev or Brian, varies) and no visit under way (her 13:30-14:30 visit is completed; 14:30 is back-to-back, allowed per 3.07)
  const dev = await book(desk, p, "alice", "14:30", p.harper, p.hyg1, "s4-alice-1430");
  expect(dev.status, JSON.stringify(dev.body)).toBe(201);
  const id = dev.body.id as string;
  await walk(desk, id, ["Confirmed"]);
  const url = `/api/config/providers/${p.harper}/blocked-time`;
  const unconfirmed = await call(admin, "post", url, { startLocal: `${DAY}T14:00`, endLocal: `${DAY}T15:30`, reason: "Course" });
  expect([unconfirmed.status, unconfirmed.body.error]).toEqual([409, "block_overlaps_appointments"]);
  expect((unconfirmed.body.overlaps as { appointmentId: string }[]).map((o) => o.appointmentId)).toEqual([id]);
  const noReason = await call(admin, "post", url, { startLocal: `${DAY}T14:00`, endLocal: `${DAY}T15:30`, confirmOverlaps: true });
  expect([noReason.status, noReason.body.error]).toEqual([400, "reason_required"]);
  expect((await call(admin, "post", url, { startLocal: `${DAY}T14:00`, endLocal: `${DAY}T15:30`, reason: "Course", confirmOverlaps: true })).status).toBe(201);

  await gotoBoard(desk);
  await expect(cardOf(desk, id)).toContainText("Blocked time");
  await expect(cardOf(desk, id).getByRole("button", { name: "Check in Alice Anderson" })).toBeDisabled();
  const refused = await move(desk, id, "CheckedIn", (await load(desk, id)).rowVersion);
  expect([refused.status, refused.body.error]).toEqual([409, "block_flag_unresolved"]);
  await cardOf(desk, id).getByRole("button", { name: "Keep despite blocked time Alice Anderson" }).click();
  await cardOf(desk, id).getByLabel("Reason (required)").fill("Harper sees Alice before the course");
  await cardOf(desk, id).getByRole("button", { name: "Keep appointment" }).click();
  await expect(cardOf(desk, id).getByRole("button", { name: "Check in Alice Anderson" })).toBeEnabled();
  await walk(desk, id, ["CheckedIn"]);
  expect((await historyOf(desk, id)).find((e) => e.eventType === "KeptDespiteBlock")?.reason).toBe("Harper sees Alice before the course");
});
