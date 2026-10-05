import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-006-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.odontogram-longitudinal.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a practice would: permanent, primary and mixed dentition on one patient (switching the view loses nothing), surface-specific findings on primary teeth,
// a condition catalogue a dentist extends and retires (retiring stops new findings and leaves old ones as they were), tooth-state validation (an implant is not placed in a primary tooth, a missing
// tooth takes no other finding), a tooth's longitudinal history that stays complete after later changes, links from a finding to a diagnosis, plan and procedure, the audit log with no clinical content,
// each role's access, a stale catalogue change, the keyboard - then axe in both themes, at desktop and tablet width. The original STORY-006 walkthrough is run alongside as the parent regression.

test.describe.configure({ mode: "serial" });

const OUT = process.env.ODONTOGRAM_LONGITUDINAL_E2E_OUT ?? path.join(process.cwd(), "odontogram-longitudinal-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "odonto-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const NAMES: Record<string, string> = { Dentist: "Dr. Dana Dentist", Hygienist: "Hana Hygienist" };

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

const chartOf = async (page: Page, patient: string) => (await api(page, "get", `/api/patients/${ids[patient]}/odontogram`)).body;
const tooth = (page: Page, label: string) => page.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });
const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const recordForm = (page: Page) => page.getByRole("form", { name: "Record a finding" });
const stateWord = (page: Page, label: string) => tooth(page, label).locator(".alv-odonto__tooth-state");
const catalogue = (page: Page) => page.locator("details.alv-odonto__catalogue");
const conditionRow = (page: Page, label: string) => catalogue(page).locator("li.alv-odonto__condition").filter({ has: page.locator("strong", { hasText: new RegExp(`^${label}$`) }) });

async function gotoChart(page: Page, patient: string) {
  await page.goto(`/patients/${ids[patient]}/odontogram`);
  await expect(page.getByRole("heading", { name: "Odontogram" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Teeth shown" })).toBeVisible();
}

async function recordUi(page: Page, condition: string, state: string, surface?: string) {
  await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
  await recordForm(page).getByLabel("Condition").selectOption(condition);
  if (surface) await recordForm(page).getByLabel("Surface").selectOption(surface);
  await recordForm(page).getByLabel("State").selectOption(state);
  await recordForm(page).getByRole("button", { name: "Record finding" }).click();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "Assistant", "FrontDesk", "OfficeManager"]) {
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
  writeFileSync(path.join(OUT, "odontogram-longitudinal-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: a child with mixed teeth and an adult", async () => {
  for (const [key, first, last, dob, phone] of [["cy", "Cy", "Child", "2016-05-02", "555-010-0188"], ["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `odl-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
  }
});

test("MIXED DENTITION: one patient has findings on permanent and primary teeth, drawn as permanent, primary or mixed, and switching the view loses nothing", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page, "cy");
  await expect(page.getByRole("button", { name: "Permanent teeth" })).toHaveAttribute("aria-pressed", "true");     // nothing on a primary tooth yet: opens as permanent
  await page.getByRole("button", { name: "Mixed (both)" }).click();
  await expect(page.getByRole("button", { name: /^Tooth / })).toHaveCount(52);
  await expect(page.getByRole("group", { name: "Upper primary teeth" }).getByRole("button")).toHaveCount(10);
  await expect(page.getByRole("group", { name: "Lower permanent teeth" }).getByRole("button")).toHaveCount(16);

  await tooth(page, "A").click();                                                       // Universal A is FDI 55, a primary second molar
  await expect(page.getByRole("heading", { name: "Tooth A: upper right primary second molar" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Surfaces" }).getByRole("button")).toHaveCount(5);
  await recordUi(page, "Caries", "Diagnosed", "O");
  await expect(statusLine(page)).toHaveText("Caries (Occlusal surface) recorded on tooth A as Diagnosed.");
  await tooth(page, "3").click();                                                       // the permanent first molar on the same patient
  await recordUi(page, "Caries", "Planned", "D");
  await expect(statusLine(page)).toContainText("on tooth 3 as Planned.");
  await tooth(page, "F").click();                                                       // a primary central incisor: facial and incisal surfaces
  await expect(page.getByRole("group", { name: "Surfaces" }).getByRole("button", { name: /Incisal/ })).toBeVisible();
  await expect(page.getByRole("group", { name: "Surfaces" }).getByRole("button", { name: /Occlusal/ })).toHaveCount(0);
  await shot(page, "01-mixed-chart.png");

  const before = (await chartOf(page, "cy")).findings.map((f: any) => `${f.toothKey}/${f.surface}/${f.condition}/${f.state}`).sort();
  expect(before).toEqual(["16/D/Caries/Planned", "55/O/Caries/Diagnosed"]);
  await page.getByRole("button", { name: "Permanent teeth" }).click();                  // the primary finding is not drawn - and is listed, not hidden
  await expect(page.getByRole("heading", { name: "Recorded on teeth not drawn in this view" })).toBeVisible();
  await expect(page.getByText("Tooth A (upper right primary second molar): Caries, Occlusal surface: Diagnosed")).toBeVisible();
  await page.getByRole("button", { name: "Primary teeth" }).click();
  await expect(stateWord(page, "A")).toHaveText("Diagnosed");
  await expect(page.getByText("Tooth 3 (upper right first molar): Caries, Distal surface: Planned")).toBeVisible();
  await page.getByRole("button", { name: "Mixed (both)" }).click();
  await expect(stateWord(page, "A")).toHaveText("Diagnosed");
  await expect(stateWord(page, "3")).toHaveText("Planned");
  expect((await chartOf(page, "cy")).findings.map((f: any) => `${f.toothKey}/${f.surface}/${f.condition}/${f.state}`).sort()).toEqual(before);       // nothing changed by looking
  await page.reload();
  await expect(page.getByRole("button", { name: "Mixed (both)" })).toHaveAttribute("aria-pressed", "true");          // a patient with a primary finding opens as mixed
  evidence.mixedDentition = { teethDrawn: 52, findings: before, switchingChangedNothing: true };
});

test("TOOTH-STATE VALIDATION: an implant is not placed in a primary tooth, and a missing tooth takes no other finding until the entry is withdrawn", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page, "cy");
  await tooth(page, "A").click();
  await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
  const options = await recordForm(page).getByLabel("Condition").locator("option").allTextContents();
  expect(options).not.toContain("Implant");                                              // not offered for a primary tooth
  await recordForm(page).getByRole("button", { name: "Cancel" }).click();
  const refused = await api(page, "post", `/api/patients/${ids.cy}/odontogram/findings`, { toothKey: "55", condition: "Implant", state: "Existing" });
  expect([refused.status, refused.body.error, "condition" in refused.body.fieldErrors]).toEqual([400, "validation_failed", true]);

  await tooth(page, "3").click();                                                        // a permanent tooth that is missing
  await recordUi(page, "Missing", "Existing");
  await expect(statusLine(page)).toContainText("Missing tooth (whole tooth) recorded on tooth 3 as Existing.");
  await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
  await recordForm(page).getByLabel("Condition").selectOption("Crown");
  await recordForm(page).getByLabel("State").selectOption("Planned");
  await recordForm(page).getByRole("button", { name: "Record finding" }).click();
  await expect(statusLine(page)).toContainText("Not saved: This tooth is recorded as missing, so no other finding can be recorded on it.");
  await shot(page, "02-tooth-absent.png");
  await recordForm(page).getByRole("button", { name: "Cancel" }).click();
  const direct = await api(page, "post", `/api/patients/${ids.cy}/odontogram/findings`, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
  expect([direct.status, direct.body.error]).toEqual([409, "tooth_absent"]);

  await recordUi(page, "Implant", "Planned");                                            // an implant may stand in for it
  await expect(statusLine(page)).toContainText("Implant (whole tooth) recorded on tooth 3 as Planned.");
  const missing = (await chartOf(page, "cy")).findings.find((f: any) => f.condition === "Missing");
  await page.getByRole("button", { name: "Withdraw: Missing tooth (whole tooth)" }).click();
  await page.getByRole("form", { name: "Withdraw finding: Missing tooth (whole tooth)" }).getByLabel(/Why is/).fill("Entered on the wrong tooth");
  await page.getByRole("form", { name: "Withdraw finding: Missing tooth (whole tooth)" }).getByRole("button", { name: "Withdraw finding" }).click();
  await expect(statusLine(page)).toContainText("withdrawn.");
  expect((await api(page, "post", `/api/patients/${ids.cy}/odontogram/findings`, { toothKey: "16", surface: "M", condition: "Caries", state: "Diagnosed" })).status).toBe(200);        // present again
  expect(missing.id).toBeTruthy();
  evidence.toothState = { implantOnPrimary: "validation_failed", findingOnMissingTooth: "tooth_absent", implantAllowed: true, presentAgainAfterWithdrawal: true };
});

test("EXTENSIBLE CONDITIONS: a dentist adds a condition, a hygienist records it but cannot change the catalogue, and retiring it stops new findings while old ones read as they did", async () => {
  const dentist = sessions.Dentist.page;
  const hygienist = sessions.Hygienist.page;
  await gotoChart(dentist, "ann");
  await dentist.getByText(/^Condition types \(/).click();
  await expect(catalogue(dentist).locator(":scope > summary")).toContainText("Condition types (6 active)");
  await dentist.getByRole("button", { name: "Add a condition type" }).click();
  const form = dentist.getByRole("form", { name: "Add a condition type" });
  await form.getByLabel("Label").fill("Tooth fracture");
  await expect(form.getByLabel("Code")).toHaveValue("ToothFracture");
  await form.getByLabel("Recorded on").selectOption("Surface");
  await form.getByLabel("Applies to").selectOption("Both");
  await form.getByRole("button", { name: "Add condition type" }).click();
  await expect(statusLine(dentist)).toHaveText("Tooth fracture added to the catalogue.");
  await expect(catalogue(dentist).locator(":scope > summary")).toContainText("Condition types (7 active)");
  await shot(dentist, "03-catalogue-add.png");

  await gotoChart(hygienist, "ann");                                                     // the hygienist sees it and can record with it
  await hygienist.getByText(/^Condition types \(/).click();
  await expect(conditionRow(hygienist, "Tooth fracture")).toContainText("Code ToothFracture · recorded on one surface · for permanent and primary teeth");
  await expect(hygienist.getByRole("button", { name: /Add a condition type|Retire|Reactivate/ })).toHaveCount(0);   // but not change it
  await tooth(hygienist, "3").click();
  await recordUi(hygienist, "ToothFracture", "Diagnosed", "B");
  await expect(statusLine(hygienist)).toContainText("Tooth fracture (Buccal surface) recorded on tooth 3 as Diagnosed.");
  expect((await api(hygienist, "post", "/api/odontogram/condition-types", { code: "Nope", label: "Nope", scope: "Surface", appliesTo: "Both" })).status).toBe(403);

  await dentist.reload();
  await dentist.getByText(/^Condition types \(/).click();
  await conditionRow(dentist, "Tooth fracture").getByRole("button", { name: "Retire the Tooth fracture condition" }).click();
  const retire = dentist.getByRole("form", { name: "Retire condition: Tooth fracture" });
  await retire.getByRole("button", { name: "Retire condition" }).click();
  await expect(retire.getByText("Say why.")).toBeVisible();
  await retire.getByLabel(/Why is/).fill("Replaced by a more specific condition");
  await retire.getByRole("button", { name: "Retire condition" }).click();
  await expect(statusLine(dentist)).toHaveText("Tooth fracture retired.");
  await expect(catalogue(dentist).locator(":scope > summary")).toContainText("Condition types (6 active, 1 retired)");

  const blocked = await api(hygienist, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: "26", surface: "O", condition: "ToothFracture", state: "Diagnosed" });
  expect([blocked.status, blocked.body.error]).toEqual([409, "condition_inactive"]);
  await tooth(dentist, "3").click();
  await expect(dentist.locator("li.alv-odonto__finding strong", { hasText: "Tooth fracture" })).toBeVisible();   // the old finding still reads as it did
  await dentist.getByRole("button", { name: "Record a finding on this tooth" }).click();
  expect(await recordForm(dentist).getByLabel("Condition").locator("option").allTextContents()).not.toContain("Tooth fracture");
  await recordForm(dentist).getByRole("button", { name: "Cancel" }).click();
  await shot(dentist, "04-catalogue-retired.png");

  await conditionRow(dentist, "Tooth fracture").getByRole("button", { name: "Reactivate the Tooth fracture condition" }).click();
  await dentist.getByRole("button", { name: "Reactivate condition" }).click();
  await expect(statusLine(dentist)).toHaveText("Tooth fracture reactivated.");
  await conditionRow(dentist, "Tooth fracture").getByText("History").click();
  await expect(conditionRow(dentist, "Tooth fracture").locator("ol li")).toHaveCount(3);              // created, retired, reactivated - each with who and why
  const types = (await api(dentist, "get", "/api/odontogram/condition-types")).body as any[];
  expect(types.find((t) => t.code === "ToothFracture")).toMatchObject({ label: "Tooth fracture", scope: "Surface", appliesTo: "Both", toothEffect: "None", isActive: true });
  evidence.catalogue = { added: "ToothFracture", hygienistRecordedIt: true, hygienistCannotChange: 403, retireNeedsReason: true, retiredBlocksNew: "condition_inactive", oldFindingStillReads: true, reactivated: true };
});

test("LONGITUDINAL HISTORY: a tooth's history lists every finding and change, withdrawn ones too, in order, and stays complete after later changes", async () => {
  const page = sessions.Dentist.page;
  const hyg = sessions.Hygienist.page;
  const caries = (await chartOf(page, "ann")).findings.find((f: any) => f.toothKey === "16" && f.condition === "ToothFracture");
  expect(caries).toBeTruthy();
  await gotoChart(hyg, "ann");
  await tooth(hyg, "3").click();
  await hyg.getByRole("button", { name: "Plan: Tooth fracture (Buccal surface)" }).click();
  await expect(statusLine(hyg)).toContainText("is now Planned.");
  await gotoChart(page, "ann");
  await tooth(page, "3").click();
  await page.getByRole("button", { name: "Complete: Tooth fracture (Buccal surface)" }).click();
  await expect(statusLine(page)).toContainText("is now Completed.");
  await recordUi(page, "Crown", "Planned");
  await expect(statusLine(page)).toContainText("Crown (whole tooth) recorded on tooth 3 as Planned.");
  await page.getByRole("button", { name: "Withdraw: Crown (whole tooth)" }).click();
  await page.getByRole("form", { name: "Withdraw finding: Crown (whole tooth)" }).getByLabel(/Why is/).fill("Patient declined the crown");
  await page.getByRole("form", { name: "Withdraw finding: Crown (whole tooth)" }).getByRole("button", { name: "Withdraw finding" }).click();
  await expect(statusLine(page)).toContainText("withdrawn.");
  await recordUi(page, "Crown", "Planned");                                              // the same finding entered again as a new one
  await expect(statusLine(page)).toContainText("recorded on tooth 3 as Planned.");

  await page.getByLabel("History of tooth 3").click();
  const timeline = page.getByRole("list", { name: "Everything recorded on tooth 3, oldest first" });
  await expect(timeline.getByRole("listitem")).toHaveCount(6);
  const lines = (await timeline.getByRole("listitem").allTextContents()).map((t) => t.replace(/\s+/g, " "));
  expect(lines.map((l) => l.split(" - ")[0])).toEqual(["Recorded", "State changed", "State changed", "Recorded", "Withdrawn", "Recorded"]);
  expect(lines[1]).toContain(NAMES.Hygienist);
  expect(lines[2]).toContain(NAMES.Dentist);
  expect(lines[4]).toContain("Reason: Patient declined the crown");
  await shot(page, "05-tooth-history.png");

  const api16 = (await api(page, "get", `/api/patients/${ids.ann}/odontogram/teeth/16/history`)).body;
  expect(api16.events.length).toBe(6);
  expect(new Set(api16.events.map((e: any) => e.findingId)).size).toBe(3);                // three findings, two of them gone from or changed on the chart, all still on the timeline
  expect(api16.events.map((e: any) => e.occurredAtUtc)).toEqual([...api16.events.map((e: any) => e.occurredAtUtc)].sort());
  expect((await api(page, "get", `/api/patients/${ids.ann}/odontogram/teeth/19/history`)).status).toBe(400);
  ids.timelineFinding = caries.id;
  evidence.longitudinal = { events: lines.map((l) => l.split(" - ")[0]), findingsOnTimeline: 3, withdrawnKept: true, inOrder: true };
});

test("LINKS: a finding is linked to a diagnosis, a treatment plan and a procedure, shown beside it and in the history; linking again is quiet and does not change the finding", async () => {
  const page = sessions.Dentist.page;
  const before = (await chartOf(page, "ann")).findings.find((f: any) => f.id === ids.timelineFinding);
  for (const [linkType, reference] of [["Diagnosis", "DX-1001"], ["TreatmentPlan", "TP-2002"], ["Procedure", "PROC-3003"], ["Diagnosis", "DX-1001"]]) {
    const r = await api(page, "post", `/api/odontogram/findings/${ids.timelineFinding}/links`, { linkType, reference });
    expect(r.status).toBe(200);
  }
  const after = (await chartOf(page, "ann")).findings.find((f: any) => f.id === ids.timelineFinding);
  expect(after.links.map((l: any) => `${l.linkType}:${l.reference}`)).toEqual(["Diagnosis:DX-1001", "TreatmentPlan:TP-2002", "Procedure:PROC-3003"]);
  expect(after.rowVersion).toBe(before.rowVersion);                                       // linking never invalidates someone else's edit
  const bad = await api(page, "post", `/api/odontogram/findings/${ids.timelineFinding}/links`, { linkType: "Note", reference: "" });
  expect([bad.status, bad.body.error]).toEqual([400, "validation_failed"]);

  await gotoChart(page, "ann");
  await tooth(page, "3").click();
  const links = page.getByRole("list", { name: "Records linked to Tooth fracture (Buccal surface)" });
  await expect(links.getByRole("listitem")).toHaveCount(3);
  await expect(links).toContainText("Linked Treatment plan: TP-2002");
  await page.getByLabel("History of tooth 3").click();
  await expect(page.getByRole("list", { name: "Everything recorded on tooth 3, oldest first" }).getByRole("listitem")).toHaveCount(9);
  await shot(page, "06-links.png");
  evidence.links = { linked: ["Diagnosis", "TreatmentPlan", "Procedure"], repeatQuiet: true, findingUnchanged: true };
});

test("A STALE CATALOGUE CHANGE: the conflict banner names a condition type, the other person's change stands, and a reload lets the first person finish", async () => {
  const dentist = sessions.Dentist.page;
  const second = await sessions.Dentist.context.newPage();                               // the same dentist on a second screen
  await gotoChart(dentist, "ann");
  await dentist.getByText(/^Condition types \(/).click();
  await gotoChart(second, "ann");
  await second.getByText(/^Condition types \(/).click();
  await conditionRow(second, "Root canal").getByRole("button", { name: "Retire the Root canal condition" }).click();
  await second.getByRole("form", { name: "Retire condition: Root canal" }).getByLabel(/Why is/).fill("Not done here");
  await second.getByRole("form", { name: "Retire condition: Root canal" }).getByRole("button", { name: "Retire condition" }).click();
  await expect(statusLine(second)).toHaveText("Root canal retired.");
  await conditionRow(second, "Root canal").getByRole("button", { name: "Reactivate the Root canal condition" }).click();   // and reactivated: active again, but on a newer version than the first screen holds
  await second.getByRole("button", { name: "Reactivate condition" }).click();
  await expect(statusLine(second)).toHaveText("Root canal reactivated.");

  await conditionRow(dentist, "Root canal").getByRole("button", { name: "Retire the Root canal condition" }).click();   // still holding the old version
  await dentist.getByRole("form", { name: "Retire condition: Root canal" }).getByLabel(/Why is/).fill("Also not done here");
  await dentist.getByRole("form", { name: "Retire condition: Root canal" }).getByRole("button", { name: "Retire condition" }).click();
  await expect(statusLine(dentist)).toHaveText("Not saved: someone else changed this.");
  await expect(dentist.getByRole("alert")).toContainText("This condition type record was updated by someone else");
  await shot(dentist, "07-catalogue-conflict.png");
  await dentist.getByRole("button", { name: /Reload/ }).click();
  await expect(dentist.getByRole("alert")).toHaveCount(0);
  await expect(conditionRow(dentist, "Root canal")).toContainText("Active");                 // the other screen's change stood; the stale retire did not overwrite it
  const types = (await api(dentist, "get", "/api/odontogram/condition-types")).body as any[];
  expect(types.find((t) => t.code === "RootCanal").isActive).toBe(true);
  await second.close();
  evidence.catalogueConflict = { staleRefused: true, otherChangeStood: true, reloadedInPlace: true };
});

test("KEYBOARD: arrow keys move around the mixed chart without selecting, and Enter selects", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page, "cy");
  await tooth(page, "8").focus();
  await page.keyboard.press("ArrowRight");
  await expect(tooth(page, "9")).toBeFocused();
  await page.keyboard.press("ArrowDown");
  expect(await page.evaluate(() => document.activeElement?.closest("[role=group]")?.getAttribute("aria-label"))).toBe("Upper primary teeth");
  await page.keyboard.press("ArrowDown");
  expect(await page.evaluate(() => document.activeElement?.closest("[role=group]")?.getAttribute("aria-label"))).toBe("Lower primary teeth");
  await page.keyboard.press("ArrowDown");
  expect(await page.evaluate(() => document.activeElement?.closest("[role=group]")?.getAttribute("aria-label"))).toBe("Lower permanent teeth");
  await expect(page.getByText("Select a tooth to see what is recorded on it.")).toBeVisible();                       // moving is not selecting
  await page.keyboard.press("Enter");
  await expect(page.locator("button.alv-odonto__tooth[aria-pressed=true]")).toHaveCount(1);
  evidence.keyboard = { arrowsAcrossFourArches: true, enterSelects: true };
});

test("ACCESS: the catalogue is read by the clinical team, changed only by a dentist or administrator, and not seen by front desk or the practice manager", async () => {
  const dentist = sessions.Dentist.page;
  const probes: Record<string, number[]> = {};
  const create = (role: string) => ({ code: `Probe${role}`, label: `Probe ${role}`, scope: "Surface", appliesTo: "Both", toothEffect: "None" });
  for (const role of ["FrontDesk", "OfficeManager", "Assistant", "Hygienist"]) {
    const page = sessions[role].page;
    probes[role] = [
      (await api(page, "get", "/api/odontogram/condition-types")).status,
      (await api(page, "get", `/api/patients/${ids.ann}/odontogram/teeth/16/history`)).status,
      (await api(page, "post", "/api/odontogram/condition-types", create(role))).status,
    ];
  }
  expect(probes).toEqual({ FrontDesk: [403, 403, 403], OfficeManager: [403, 403, 403], Assistant: [200, 200, 403], Hygienist: [200, 200, 403] });
  expect((await api(dentist, "post", "/api/odontogram/condition-types", create("Dentist"))).status).toBe(200);
  expect((await api(admin, "get", "/api/odontogram/condition-types")).status).toBe(200);
  const assistant = sessions.Assistant.page;
  await gotoChart(assistant, "ann");
  await assistant.getByText(/^Condition types \(/).click();
  await expect(assistant.getByRole("button", { name: /Add a condition type|Retire|Reactivate/ })).toHaveCount(0);
  const fd = sessions.FrontDesk.page;
  await fd.goto(`/patients/${ids.ann}`);
  await expect(fd.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
  await expect(fd.getByRole("link", { name: "Odontogram", exact: true })).toHaveCount(0);
  evidence.access = probes;
});

test("TRUST AND NUMBERING: catalogue changes and links are in the audit log with the user and a time and nothing clinical is in it; the stored tooth is the FDI key, never a display number", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=1000")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const mine = entries.filter((e) => e.eventType.startsWith("ConditionType") || e.eventType === "ToothFindingLinked");
  const by = (t: string) => mine.filter((e) => e.eventType === t);
  expect(by("ConditionTypeCreated").length).toBe(2);                                      // ToothFracture and the dentist's probe
  expect(by("ConditionTypeRetired").length).toBe(2);                                      // ToothFracture and RootCanal
  expect(by("ConditionTypeReactivated").length).toBe(2);
  expect(by("ToothFindingLinked").length).toBe(3);
  expect(mine.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = mine.map((e) => e.details).join(" ");
  for (const secret of ["fracture", "Fracture", "Root canal", "DX-1001", "TP-2002", "PROC-3003", "Replaced by", "Not done"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);

  const chart = JSON.stringify(await chartOf(sessions.Dentist.page, "cy"));
  expect(chart).not.toMatch(/universal|palmer|numbering/i);                                // numbering is presentation, not part of what the server holds
  const keys = ((await chartOf(sessions.Dentist.page, "ann")).findings as any[]).map((f) => f.toothKey);
  expect(keys.every((k) => /^[1-8][1-8]$/.test(k))).toBe(true);
  evidence.trust = Object.fromEntries(["ConditionTypeCreated", "ConditionTypeRetired", "ConditionTypeReactivated", "ToothFindingLinked"].map((t) => [t, by(t).length]));
});

test("ACCESSIBILITY: the mixed chart, a tooth's history with its links, the catalogue with its add form, and the tablet layout have no critical or serious axe violations in light and dark", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page, "cy");
  await scanBothThemes(page, "mixed-chart");
  await gotoChart(page, "ann");
  await tooth(page, "3").click();
  await page.getByLabel("History of tooth 3").click();
  await page.getByText(/^Condition types \(/).click();
  await page.getByRole("button", { name: "Add a condition type" }).click();
  await scanBothThemes(page, "tooth-history-and-catalogue");
  await shot(page, "08-history-and-catalogue.png");

  await page.setViewportSize({ width: 768, height: 1024 });
  await gotoChart(page, "cy");
  await page.getByRole("button", { name: "Mixed (both)" }).click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);        // no sideways scrolling: arches wrap
  await shot(page, "09-tablet-mixed.png");
  await scanBothThemes(page, "tablet-mixed");
  await page.setViewportSize({ width: 1280, height: 720 });
  evidence.accessibility = { scanned: ["mixed-chart", "tooth-history-and-catalogue", "tablet-mixed"], tabletNoHorizontalScroll: true };
});
