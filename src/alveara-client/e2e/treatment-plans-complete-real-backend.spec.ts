import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { seedLegacyCompletion } from "./support/legacyCompletion";

// ALV-015-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.treatment-plans-complete.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the companion story as a dentist would: a plan revised into phases with a rationale and a patient explanation; a patient's answers recorded with who gave them (and the whole-plan answer refused when it
// would overwrite an opposite one); a procedure scheduled and completed (and then final); two revisions compared; the patient copy shown and printed; an old diagnosis reference tied to the right plan (and never to
// another patient's) with its original text kept. A hygienist reads everything and changes nothing, and the API refuses what the screen does not offer.

test.describe.configure({ mode: "serial" });

const OUT = process.env.TREATMENT_PLANS_COMPLETE_E2E_OUT ?? path.join(process.cwd(), "treatment-plans-complete-e2e-out");
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
const procedures: Record<string, string> = {};
const NAMES: Record<string, string> = { Dentist: "Dr. Dana Dentist" };

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
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
const card = (page: Page, title: string) => page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: new RegExp(title) }) }).first();
const row = (page: Page, code: string) => page.locator("li.alv-tp__item", { has: page.getByText(code, { exact: true }) });
const planUrl = (patient: string) => `/patients/${ids[patient]}/treatment-plan`;
const plansOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${ids[patient]}/treatment-plans`)).body.plans as any[];

async function gotoTab(page: Page, patient = "ann") {
  await page.goto(planUrl(patient));
  await expect(page.getByRole("heading", { name: "Treatment plan", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Plans on record" })).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "FrontDesk"]) {
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
  writeFileSync(path.join(OUT, "treatment-plans-complete-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: two patients, diagnoses (two with old plan references), a catalog and a plan for the other patient", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"]] as const) {
    const p = await api(front, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `c01-${key}-${stamp}` });
    expect(p.status).toBe(201);
    ids[key] = p.body.id;
    const e = await api(dentist, "post", `/api/patients/${ids[key]}/encounters`, {}, { "Idempotency-Key": `c01-enc-${key}-${stamp}` });
    expect([200, 201]).toContain(e.status);
    ids[`${key}Encounter`] = e.body.id;
  }
  const dx = async (patient: string, label: string, reference: string | null, key: string) => {
    const d = await api(dentist, "post", `/api/patients/${ids[patient]}/diagnoses`, { idempotencyKey: `c01-${key}-${stamp}`, encounterId: ids[`${patient}Encounter`], label, toothKey: null, notes: null, treatmentPlanReference: reference });
    expect(d.status).toBe(200);
    return d.body.id as string;
  };
  ids.dxMain = await dx("ann", "Chronic periodontitis", null, "dx-main");
  ids.dxOld = await dx("ann", "Gingivitis", "plan-7", "dx-old");
  ids.dxNote = await dx("ann", "Caries", "old note", "dx-note");
  ids.dxShared = await dx("ann", "Bruxism", "SHARED", "dx-shared");
  ids.dxBo = await dx("bo", "Chronic periodontitis", null, "dx-bo");
  for (const [code, description, fee] of [["PERIO-1", "Periodontal maintenance", 120], ["SCALE-1", "Scaling", 70], ["POL-1", "Polish", 30]] as const) {
    const r = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code, description, category: "Periodontic", scope: "WholeMouth", fee });
    expect(r.status).toBe(200);
    procedures[code] = r.body.summary.id;
  }
  // another patient's plan carries the code an Ann diagnosis mentions: it must never be matched to Ann
  const bos = await api(dentist, "post", `/api/patients/${ids.bo}/treatment-plans`, { idempotencyKey: `c01-bo-plan-${stamp}`, title: "Bo's plan", items: [{ diagnosisId: ids.dxBo, procedureId: procedures["PERIO-1"], toothKey: null, surface: null }] });
  expect(bos.status).toBe(200);
  expect((await api(dentist, "post", `/api/treatment-plans/${bos.body.id}/reference-code`, { code: "SHARED", rowVersion: bos.body.rowVersion })).status).toBe(200);
  ids.boPlan = bos.body.id;
});

test("a plan is created and a second procedure added on screen, a third through the API", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const f = page.getByRole("form", { name: "Create a treatment plan" });
  await f.getByLabel("Plan title").fill("Gum health plan");
  await f.getByLabel(/^Diagnosis this procedure/).selectOption({ label: "Chronic periodontitis" });
  await f.getByLabel(/^Procedure/).selectOption({ label: "PERIO-1 - Periodontal maintenance ($120.00)" });
  await f.getByRole("button", { name: "Create treatment plan" }).click();
  await expect(statusLine(page)).toContainText('Treatment plan "Gum health plan" created.');

  await card(page, "Gum health plan").getByRole("button", { name: "Add procedure" }).click();
  const add = page.getByRole("form", { name: "Add a procedure to this treatment plan" });
  await add.getByLabel(/^Diagnosis this procedure/).selectOption({ label: "Chronic periodontitis" });
  await add.getByLabel(/^Procedure/).selectOption({ label: "SCALE-1 - Scaling ($70.00)" });
  await add.getByRole("button", { name: "Add procedure" }).click();
  await expect(statusLine(page)).toContainText("Procedure added to the plan.");

  const plan = (await plansOf(page, "ann"))[0];
  ids.annPlan = plan.id;
  const third = await api(page, "post", `/api/treatment-plans/${plan.id}/items`, { idempotencyKey: `c01-third-${stamp}`, diagnosisId: ids.dxMain, procedureId: procedures["POL-1"], rowVersion: plan.rowVersion });
  expect(third.status).toBe(200);
  await gotoTab(page);
  await expect(card(page, "Gum health plan")).toContainText("Estimate total: $220.00");
});

test("REVISION: phases, a clinical rationale and a patient explanation are laid out in one revision that needs a reason, and the plan shows them grouped by phase", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await card(page, "Gum health plan").getByRole("button", { name: "Revise plan" }).click();
  const f = page.getByRole("form", { name: "Revise this treatment plan" });

  await f.getByRole("button", { name: "Save revision" }).click();                                       // no reason yet
  await expect(f.locator("span.alv-tp__error", { hasText: "Say why this plan is being revised." })).toBeVisible();
  expect((await plansOf(page, "ann"))[0].currentRevision).toBe(3);                                   // nothing was saved

  await f.getByLabel("Why is this plan being revised?").fill("Phased for healing");
  await f.getByLabel(/^Clinical rationale/).fill("Deep pockets on the lower left; probing depths of 6 mm.");
  await f.getByLabel(/^Explanation for the patient/).fill("We will clean below the gums first, then check how they heal.");
  await f.getByRole("button", { name: "Add a phase" }).click();
  await f.getByLabel(/^Phase 1 name/).fill("First visit");
  await f.getByLabel(/^Phase 1 planned visit/).fill("Today");
  await f.getByRole("button", { name: "Add a phase" }).click();
  await f.getByLabel(/^Phase 2 name/).fill("After healing");
  await f.getByLabel(/^Phase 2 planned visit/).fill("Two weeks later");
  await f.getByLabel(/^Phase 2 target date/).fill("2030-06-15");
  await f.getByLabel(/^PERIO-1/).selectOption("1");
  await f.getByLabel(/^SCALE-1/).selectOption("2");
  await shot(page, "01-revise-form.png");
  await scanBothThemes(page, "revise-form");
  await f.getByRole("button", { name: "Save revision" }).click();

  await expect(statusLine(page)).toContainText("Plan revised: this is now revision 4. The earlier version is kept.");
  const c = card(page, "Gum health plan");
  await expect(c.getByRole("region", { name: "Phase 1: First visit of Gum health plan" })).toContainText("PERIO-1");
  await expect(c.getByRole("region", { name: "Phase 2: After healing of Gum health plan" })).toContainText("SCALE-1");
  await expect(c.getByRole("region", { name: "Phase 2: After healing of Gum health plan" })).toContainText("Two weeks later");
  await expect(c.getByRole("region", { name: "Not yet placed in a phase of Gum health plan" })).toContainText("POL-1");
  await expect(c).toContainText("Clinical rationale: Deep pockets on the lower left");
  await expect(c).toContainText("For the patient: We will clean below the gums first");
  await expect(c).toContainText("Revision 4");
  await expect(c).toContainText("Estimate total: $220.00");                                          // phases do not change the money
  await shot(page, "02-grouped-by-phase.png");
  evidence.revision = { currentRevision: 4, phases: 2, unplaced: ["POL-1"] };
  await scanBothThemes(page, "grouped");
});

test("ANSWERS: a decline needs a reason, an answer carries who gave it, and the whole-plan answer is refused when it would overwrite an opposite one", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await row(page, "PERIO-1").getByRole("button", { name: "Accept PERIO-1" }).click();
  const accept = page.getByRole("form", { name: "Accept PERIO-1" });
  await accept.getByLabel(/^Who gave this answer/).fill("Ann Alder (patient)");
  await accept.getByRole("button", { name: "Record acceptance" }).click();
  await expect(statusLine(page)).toContainText("PERIO-1 accepted.");
  await expect(row(page, "PERIO-1")).toContainText("Accepted");
  await expect(row(page, "PERIO-1")).toContainText("Answer given by Ann Alder (patient).");
  await expect(row(page, "PERIO-1")).toContainText(`Entered by ${NAMES.Dentist}`);

  await row(page, "SCALE-1").getByRole("button", { name: "Decline SCALE-1" }).click();
  const decline = page.getByRole("form", { name: "Decline SCALE-1" });
  await decline.getByRole("button", { name: "Record decline" }).click();
  await expect(decline.getByRole("alert")).toContainText("Not recorded: say why it was declined.");
  await decline.getByLabel("Why was it declined?").fill("Cannot afford it now");
  await decline.getByRole("button", { name: "Record decline" }).click();
  await expect(statusLine(page)).toContainText("SCALE-1 declined.");
  await expect(row(page, "SCALE-1")).toContainText("Reason: Cannot afford it now");
  await expect(card(page, "Gum health plan")).toContainText("Patient's answer: Partly accepted");

  // answering the whole plan against the declined procedure is refused, and nothing is recorded
  await card(page, "Gum health plan").getByRole("button", { name: "Accept all pending" }).click();
  const all = page.getByRole("form", { name: "Accept all pending procedures" });
  await all.getByRole("button", { name: "Record acceptance" }).click();
  await expect(all.getByRole("alert")).toContainText("1 of the 3 procedures already declined");
  await shot(page, "03-partial-conflict.png");
  await all.getByRole("button", { name: "Cancel" }).click();
  await expect(row(page, "POL-1")).toContainText("Not yet answered");

  await row(page, "POL-1").getByRole("button", { name: "Accept POL-1" }).click();
  await page.getByRole("form", { name: "Accept POL-1" }).getByRole("button", { name: "Record acceptance" }).click();
  await expect(statusLine(page)).toContainText("POL-1 accepted.");
  await expect(card(page, "Gum health plan")).toContainText("Of that, accepted so far: $150.00");
  await shot(page, "04-answers.png");
  const states = (await plansOf(page, "ann"))[0].items.map((i: any) => [i.procedureCode, i.acceptance]);
  expect(states).toEqual([["PERIO-1", "Accepted"], ["SCALE-1", "Declined"], ["POL-1", "Accepted"]]);
  evidence.answers = { states, acceptedEstimateTotal: 150 };
});

test("PROGRESS: only an accepted procedure is scheduled, completing is final, and a completed procedure cannot be re-answered or withdrawn", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await expect(row(page, "SCALE-1").getByRole("button", { name: /Schedule|completed/ })).toHaveCount(0);      // declined: nothing to schedule
  await row(page, "PERIO-1").getByRole("button", { name: "Schedule PERIO-1" }).click();
  const sched = page.getByRole("form", { name: "Schedule PERIO-1" });
  await expect(sched.getByText("It does not book an appointment.")).toBeVisible();
  await sched.getByLabel("Planned date (optional)").fill("2030-06-15");
  await sched.getByRole("button", { name: "Save schedule" }).click();
  await expect(statusLine(page)).toContainText("PERIO-1 scheduled.");
  await expect(row(page, "PERIO-1")).toContainText("Scheduled for");

  await expect(row(page, "PERIO-1").getByRole("button", { name: /Mark .* completed/ })).toHaveCount(0);       // ALV-008-C02: the per-procedure completion is retired; completing is Review and complete
  const open = (await plansOf(page, "ann"))[0];
  seedLegacyCompletion(open.id, open.items[0].id);                                                        // a procedure completed the old way (retired), seeded: see support/legacyCompletion.ts
  await gotoTab(page);
  await expect(row(page, "PERIO-1")).toContainText("Completed");
  await expect(row(page, "PERIO-1").getByRole("button")).toHaveCount(0);                                  // nothing more can be done with it, not even withdrawing
  await shot(page, "05-completed.png");

  const plan = (await plansOf(page, "ann"))[0];
  const perio = plan.items[0].id;
  const redo = await api(page, "post", `/api/treatment-plans/${plan.id}/items/${perio}/decision`, { decision: "Declined", reason: "x", rowVersion: plan.rowVersion });
  const withdraw = await api(page, "post", `/api/treatment-plans/${plan.id}/items/${perio}/withdraw`, { reason: "Mistake", rowVersion: plan.rowVersion });
  const resched = await api(page, "post", `/api/treatment-plans/${plan.id}/items/${perio}/schedule`, { scheduled: false });
  expect([[redo.status, redo.body.error], [withdraw.status, withdraw.body.error], [resched.status, resched.body.error]]).toEqual([[409, "item_completed"], [409, "item_completed"], [409, "item_completed"]]);
  evidence.completedIsFinal = { redecide: redo.body.error, withdraw: withdraw.body.error, reschedule: resched.body.error };
});

test("HISTORY: every revision is listed, any one opens as it was saved, and two are compared with every difference named", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await card(page, "Gum health plan").getByRole("button", { name: "Show revision history" }).click();
  const list = page.getByRole("list", { name: "Revisions of Gum health plan" });
  await expect(list.getByRole("listitem")).toHaveCount(4);
  await expect(list.getByRole("listitem").nth(0)).toContainText("Revision 1: Plan created");
  await expect(list.getByRole("listitem").nth(3)).toContainText("Current");
  await expect(list.getByRole("listitem").nth(3)).toContainText("Reason: Phased for healing");

  await page.getByRole("button", { name: "View revision 1" }).click();
  const first = page.getByRole("region", { name: "Revision 1 as saved" });
  await expect(first).toContainText("PERIO-1");
  await expect(first).not.toContainText("SCALE-1");                                                  // revision 1 had one procedure
  await expect(first).not.toContainText("Phase 1");

  const compare = page.getByRole("form", { name: "Compare two revisions" });
  await compare.getByLabel("Compare revision").selectOption("3");
  await compare.getByLabel("with revision").selectOption("4");
  await compare.getByRole("button", { name: "Compare" }).click();
  const changes = page.getByRole("region", { name: "Changes from revision 3 to revision 4" });
  await expect(changes).toContainText("Clinical rationale changed. Before: (none)");
  await expect(changes).toContainText("Phases added: Phase 1: First visit; Phase 2: After healing");
  await expect(changes).toContainText("PERIO-1 moved from not placed to phase 1.");
  await expect(changes).toContainText("Estimate: $220.00 then, $220.00 now (no change)");
  await expect(changes).toContainText("not an insurance estimate");
  await shot(page, "06-compare.png");
  await scanBothThemes(page, "history");

  const compared = (await api(page, "get", `/api/treatment-plans/${ids.annPlan}/revisions/compare?from=1&to=4`)).body;
  expect([compared.phasesAdded.length, compared.itemsAdded.map((i: any) => i.procedureCode), compared.estimateDelta]).toEqual([2, ["SCALE-1", "POL-1"], 100]);
  evidence.compare = { from: 1, to: 4, phasesAdded: 2, itemsAdded: ["SCALE-1", "POL-1"], estimateDelta: 100 };
});

test("PATIENT COPY: a plain-language page without staff detail, with the estimate labelled, that prints as one clean page", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await card(page, "Gum health plan").getByRole("button", { name: "Show patient copy" }).click();
  const copy = page.getByRole("region", { name: "Patient copy of Gum health plan" });
  await expect(copy.getByRole("heading", { level: 3 })).toHaveText("Your treatment plan: Gum health plan");
  await expect(copy).toContainText("Prepared for Ann Alder");
  await expect(copy).toContainText("We will clean below the gums first, then check how they heal.");
  await expect(copy).toContainText("Phase 1: First visit");
  await expect(copy).toContainText("Estimated fee: $120.00 · Your answer: accepted · planned for");
  await expect(copy).toContainText("Estimated fee: $70.00 · Your answer: declined");
  await expect(copy).toContainText("Estimated total: $220.00");
  await expect(copy).toContainText("Of that, the part you have accepted so far: $150.00");
  await expect(copy).toContainText("not an insurance estimate and not a guaranteed patient cost");
  await expect(copy).toContainText("It does not include any insurance benefits");
  const text = (await copy.textContent()) ?? "";
  for (const staffOnly of ["Deep pockets", "probing depths", NAMES.Dentist, "Entered by", "revision history", "Cannot afford it now"]) expect(text, `staff-only text "${staffOnly}" must not appear`).not.toContain(staffOnly);
  await shot(page, "07-patient-copy.png");
  await scanBothThemes(page, "patient-copy");

  // printing: everything else on the screen is hidden and the copy is plain black on white
  await page.emulateMedia({ media: "print" });
  await expect(page.getByRole("heading", { name: "Plans on record" })).toBeHidden();
  await expect(copy).toBeVisible();
  await expect(copy.getByRole("button", { name: "Print this page" })).toBeHidden();
  const colours = await copy.evaluate((el) => { const s = getComputedStyle(el); return [s.color, s.backgroundColor]; });
  expect(colours).toEqual(["rgb(0, 0, 0)", "rgb(255, 255, 255)"]);
  await shot(page, "08-patient-copy-print.png");
  await page.emulateMedia({ media: "screen" });
  await copy.getByRole("button", { name: "Close patient copy" }).click();
  await expect(copy).toHaveCount(0);
  evidence.patientCopy = { staffOnlyAbsent: true, printHidesRest: true, printColours: colours };
});

test("OLD REFERENCES: a reference is tied to the right plan and never to another patient's, the original text is kept, and the choice offers only this patient's plans", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  await card(page, "Gum health plan").getByRole("button", { name: "Set reference code" }).click();
  const code = page.getByRole("form", { name: "Set the reference code of this treatment plan" });
  await code.getByLabel("Reference code").fill("PLAN-7");
  await code.getByRole("button", { name: "Set reference code" }).click();
  await expect(statusLine(page)).toContainText("Reference code set.");
  await expect(card(page, "Gum health plan")).toContainText("Reference code PLAN-7");

  const refs = page.getByRole("region", { name: "Old diagnosis references" });
  await refs.getByRole("button", { name: "Review old diagnosis references" }).click();
  const list = refs.getByRole("list", { name: "Old diagnosis references" });
  await expect(list.getByRole("listitem")).toHaveCount(3);
  const item = (label: string) => list.getByRole("listitem").filter({ hasText: label });
  await expect(item("Gingivitis")).toContainText("Can be tied to “Gum health plan”.");
  await expect(item("Caries")).toContainText("No matching plan. It stays unresolved.");
  await expect(item("Bruxism")).toContainText("No matching plan. It stays unresolved.");               // "SHARED" is another patient's plan code: never matched
  expect((await api(page, "get", `/api/patients/${ids.ann}/diagnoses`)).body.diagnoses.every((d: any) => d.treatmentPlanReferenceState !== "Resolved")).toBe(true);   // reviewing changed nothing
  await shot(page, "09-references-review.png");

  await refs.getByRole("button", { name: "Tie 1 matching reference to its plan" }).click();
  await expect(refs.getByText("1 reference tied to plans. Each diagnosis keeps its original text.")).toBeVisible();
  await expect(item("Gingivitis")).toContainText("Tied to “Gum health plan”.");
  await expect(refs.getByRole("button", { name: /Tie \d+ matching/ })).toHaveCount(0);

  // choosing: only this patient's plans are offered, never Bo's
  const choose = refs.getByRole("form", { name: "Choose the plan for Caries" });
  expect(await choose.getByRole("option").allTextContents()).toEqual(["Choose a plan", "Gum health plan (PLAN-7)"]);
  await choose.getByLabel(/^Plan for/).selectOption({ label: "Gum health plan (PLAN-7)" });
  await choose.getByRole("button", { name: "Tie to this plan" }).click();
  await expect(item("Caries")).toContainText("Tied to “Gum health plan”.");
  await expect(item("Bruxism")).toContainText("No matching plan. It stays unresolved.");
  await shot(page, "10-references-tied.png");
  await scanBothThemes(page, "references");

  // the Diagnoses screen shows both as tied, with the original text kept; the other stays unresolved
  await page.goto(`/patients/${ids.ann}/diagnoses`);
  await expect(page.getByRole("heading", { name: "Diagnoses on record" })).toBeVisible();
  const diag = (label: string) => page.getByRole("listitem", { name: `Diagnosis: ${label}` });
  await expect(diag("Gingivitis")).toContainText("Treatment plan reference (tied to a plan): plan-7 is the treatment plan Gum health plan");
  await expect(diag("Gingivitis")).toContainText("Matched to the plan's reference code");
  await expect(diag("Gingivitis")).toContainText("The original text is kept as it was written.");
  await expect(diag("Gingivitis").getByRole("link", { name: "Gum health plan" })).toHaveAttribute("href", `/patients/${ids.ann}/treatment-plan`);
  await expect(diag("Caries")).toContainText("Treatment plan reference (tied to a plan): old note is the treatment plan Gum health plan");
  await expect(diag("Caries")).toContainText("Chosen by a person");
  await expect(diag("Bruxism")).toContainText("Treatment plan reference (unresolved): SHARED");
  await shot(page, "11-diagnoses-resolved.png");
  await scanBothThemes(page, "diagnoses-resolved");

  const all = (await api(page, "get", `/api/patients/${ids.ann}/diagnoses`)).body.diagnoses as any[];
  const byLabel = Object.fromEntries(all.map((d) => [d.label, d]));
  expect([byLabel.Gingivitis.treatmentPlanReference, byLabel.Gingivitis.treatmentPlanReferenceState, byLabel.Gingivitis.resolvedMethod]).toEqual(["plan-7", "Resolved", "ReferenceCode"]);
  expect([byLabel.Caries.treatmentPlanReference, byLabel.Caries.resolvedMethod, byLabel.Bruxism.treatmentPlanReferenceState]).toEqual(["old note", "Chosen", "Unresolved"]);
  const history = (await api(page, "get", `/api/diagnoses/${byLabel.Gingivitis.id}/history`)).body.versions as any[];
  expect(history.map((v) => v.treatmentPlanReference)).toEqual(["plan-7"]);                           // its history is untouched
  evidence.references = { resolved: ["Gingivitis", "Caries"], unresolved: ["Bruxism"], original: "plan-7", historyVersions: history.length };
});

test("a hygienist reads the plan, its answers, its history and the patient copy but is offered no way to change anything", async () => {
  const page = sessions.Hygienist.page;
  await gotoTab(page);
  const c = card(page, "Gum health plan");
  await expect(c.getByRole("region", { name: "Phase 1: First visit of Gum health plan" })).toContainText("PERIO-1");
  await expect(c).toContainText("Patient's answer: Partly accepted");
  await expect(row(page, "SCALE-1")).toContainText("Reason: Cannot afford it now");
  await expect(c).toContainText("Clinical rationale: Deep pockets");
  await expect(page.getByRole("form")).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Revise plan|Accept|Decline|Schedule|completed|Withdraw|Set reference code|Add procedure|Rename plan|Review old/ })).toHaveCount(0);
  await expect(page.getByRole("region", { name: "Old diagnosis references" })).toHaveCount(0);

  await c.getByRole("button", { name: "Show revision history" }).click();
  await expect(page.getByRole("list", { name: "Revisions of Gum health plan" }).getByRole("listitem")).toHaveCount(4);
  await page.getByRole("form", { name: "Compare two revisions" }).getByRole("button", { name: "Compare" }).click();
  await expect(page.getByRole("region", { name: /Changes from revision/ })).toBeVisible();
  await c.getByRole("button", { name: "Show patient copy" }).click();
  await expect(page.getByRole("region", { name: "Patient copy of Gum health plan" })).toContainText("Your treatment plan: Gum health plan");
  await shot(page, "12-hygienist-read-only.png");
  await scanBothThemes(page, "read-only");

  const plan = (await plansOf(page, "ann"))[0];
  const attempts = [
    await api(page, "post", `/api/treatment-plans/${plan.id}/revise`, { reason: "x", clinicalRationale: "x", rowVersion: plan.rowVersion }),
    await api(page, "post", `/api/treatment-plans/${plan.id}/decision`, { decision: "Accepted", rowVersion: plan.rowVersion }),
    await api(page, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[2].id}/schedule`, { scheduled: true }),
    await api(page, "post", `/api/patients/${ids.ann}/treatment-plans/reconcile-references`, { apply: true }),
    await api(page, "post", `/api/diagnoses/${ids.dxShared}/resolve-plan-reference`, { planId: plan.id }),
  ];
  expect(attempts.map((a) => a.status)).toEqual([403, 403, 403, 403, 403]);
  evidence.hygienist = { writesRefused: attempts.length };
});

test("the API refuses what the screen does not offer: a stale answer, another patient's plan, a missing CSRF token and a resolved reference being changed", async () => {
  const dentist = sessions.Dentist.page;
  const plan = (await plansOf(dentist, "ann"))[0];
  const stale = await api(dentist, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[2].id}/decision`, { decision: "Declined", reason: "x", rowVersion: "AAAAAAAAAAA=" });
  expect([stale.status, stale.body.error]).toEqual([409, "concurrency_conflict"]);

  const cross = await api(dentist, "post", `/api/diagnoses/${ids.dxShared}/resolve-plan-reference`, { planId: ids.boPlan });
  const unknown = await api(dentist, "post", `/api/diagnoses/${ids.dxShared}/resolve-plan-reference`, { planId: "00000000-0000-0000-0000-00000000dead" });
  expect([cross.status, cross.body.error, cross.body.message]).toEqual([404, "plan_not_found", unknown.body.message]);        // it does not reveal that the plan exists for someone else

  const noCsrf = await api(dentist, "post", `/api/treatment-plans/${plan.id}/revise`, { reason: "x", rowVersion: plan.rowVersion }, {}, false);
  expect(noCsrf.status).toBe(400);

  const diagnosis = (await api(dentist, "get", `/api/diagnoses/${ids.dxOld}`)).body;
  const change = await api(dentist, "post", `/api/diagnoses/${ids.dxOld}/correct`, { rowVersion: diagnosis.rowVersion, label: "Gingivitis", treatmentPlanReference: "plan-8", reason: "Wrong plan" });
  expect([change.status, change.body.error]).toEqual([409, "treatment_plan_reference_resolved"]);
  const label = await api(dentist, "post", `/api/diagnoses/${ids.dxOld}/correct`, { rowVersion: diagnosis.rowVersion, label: "Gingivitis (mild)", reason: "More precise" });
  expect([label.status, label.body.treatmentPlanReference, label.body.treatmentPlanReferenceState]).toEqual([200, "plan-7", "Resolved"]);   // the rest of the diagnosis can still be corrected

  const audit = JSON.stringify((await api(admin, "get", "/api/auth/audit-log")).body);
  for (const event of ["TreatmentPlanRevised", "TreatmentPlanItemAccepted", "TreatmentPlanItemDeclined", "TreatmentPlanItemScheduled", "TreatmentPlanItemCompleted", "DiagnosisPlanReferenceResolved", "TreatmentPlanReferenceCodeSet"]) expect(audit, event).toContain(event);
  evidence.refusals = { stale: stale.body.error, crossPatient: cross.body.error, noCsrf: noCsrf.status, resolvedReferenceLocked: change.body.error, auditEvents: 7 };
});

test("CATALOG USAGE (review finding R01-01): inactivating a procedure that treatment plans use shows the plans in the warning, needs confirmation, leaves the plans readable and takes the procedure out of new work", async () => {
  const dentist = sessions.Dentist.page;
  await admin.goto("/procedures");
  await expect(admin.getByRole("heading", { name: "Procedures and fees" })).toBeVisible();
  const usage = (await api(admin, "get", `/api/procedures/${procedures["PERIO-1"]}/usage`)).body;
  expect([usage.count, usage.bySource.find((b: any) => b.source === "Treatment plans").count]).toEqual([2, 2]);        // Ann's plan and Bo's plan

  await admin.getByRole("button", { name: "Inactivate procedure PERIO-1" }).click();
  const form = admin.getByRole("form", { name: "Inactivate procedure PERIO-1" });
  await expect(form.getByText(/PERIO-1 is referred to by 2 records/)).toBeVisible();
  await expect(form).toContainText("(Treatment plans: 2)");
  await expect(form).toContainText("does not change or remove those records");
  await form.getByLabel("Why is PERIO-1 being inactivated?").fill("No longer offered");
  await form.getByRole("button", { name: "Inactivate procedure" }).click();
  await expect(form.getByText("Confirm that you understand before inactivating.")).toBeVisible();                 // nothing is sent until it is confirmed
  expect((await api(admin, "get", `/api/procedures/${procedures["PERIO-1"]}`)).body.summary.status).toBe("Active");
  await shot(admin, "13-catalog-usage-warning.png");
  await scanBothThemes(admin, "catalog-usage-warning");

  await form.getByRole("checkbox").check();
  await form.getByRole("button", { name: "Inactivate procedure" }).click();
  await expect.poll(async () => (await api(admin, "get", `/api/procedures/${procedures["PERIO-1"]}`)).body.summary.status).toBe("Inactive");

  // the plans keep the procedure and the fee they copied
  await gotoTab(dentist);
  await expect(card(dentist, "Gum health plan")).toContainText("PERIO-1");
  await expect(card(dentist, "Gum health plan")).toContainText("Fee $120.00");
  await expect(card(dentist, "Gum health plan")).toContainText("Estimate total: $220.00");
  // and it is no longer offered for new work, on screen or through the API
  const create = dentist.getByRole("form", { name: "Create a treatment plan" });
  await expect(create.getByRole("option", { name: /PERIO-1/ })).toHaveCount(0);
  await expect(create.getByRole("option", { name: /POL-1/ })).toHaveCount(1);
  const plan = (await plansOf(dentist, "ann"))[0];
  const refused = await api(dentist, "post", `/api/treatment-plans/${plan.id}/items`, { idempotencyKey: `c01-r02-${stamp}`, diagnosisId: ids.dxMain, procedureId: procedures["PERIO-1"], rowVersion: plan.rowVersion });
  expect([refused.status, refused.body.error]).toEqual([400, "validation_failed"]);
  expect(refused.body.fieldErrors.procedureId).toContain("inactive");
  await shot(dentist, "14-plan-after-inactivation.png");
  evidence.catalogUsage = { treatmentPlanUses: 2, warned: true, confirmedBeforeInactivating: true, planStillShowsFee: 120, refusedForNewWork: true };
});
