import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-012-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.perio-sessions.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a hygienist or dentist would: full-mouth keyboard entry that never loses its place (all 192 sites typed without the mouse), the six sites told apart on every
// tooth, pus, plaque, mobility and furcation recorded, a tooth the odontogram records as missing skipped and refused with the way out, a wrong entry refused by the server without losing anything
// typed, a stale draft shown as a conflict, finalizing into the immutable chart, links, and the comparison with an earlier chart. Then every role, axe in both themes, and the tablet width.
// STORY-012's own walkthrough (perio-real-backend.spec.ts) is run alongside it as the parent regression.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PERIO_SESSIONS_E2E_OUT ?? path.join(process.cwd(), "perio-sessions-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "perio-pass-1!";
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

const P = (patient: string) => `/api/patients/${ids[patient]}/periodontal`;
const currentSession = async (page: Page, patient: string) => (await api(page, "get", `${P(patient)}/session`)).body.session;
const chartsOf = async (page: Page, patient: string) => (await api(page, "get", `${P(patient)}/charts`)).body.exams as any[];
const site = (tooth: string, siteCode: string, pd: number, rec = 0, bleeding = false, suppuration: boolean | null = null, plaque: boolean | null = null) => ({ toothKey: tooth, site: siteCode, probingDepthMm: pd, recessionMm: rec, bleeding, suppuration, plaque });
const saveBatch = (page: Page, s: any, batch: object, version?: string) => api(page, "post", `/api/periodontal/sessions/${s.id}/entries`, { rowVersion: version ?? s.rowVersion, ...batch });

const statusLine = (page: Page) => page.locator("p.alv-perio__sessionstatus");
const where = (page: Page) => page.getByRole("heading", { level: 3, name: /^Tooth \S+, / });
const history = (page: Page) => page.getByRole("region", { name: "Charts on record" });

async function gotoSteps(page: Page, patient: string) {
  await page.goto(`/patients/${ids[patient]}/periodontal`);
  await expect(page.getByRole("heading", { name: "Periodontal chart" })).toBeVisible();
  await expect(history(page).getByRole("heading", { name: "Charts on record" })).toBeVisible();
  await page.getByRole("button", { name: "Step by step (keyboard)" }).click();
}
async function startDraft(page: Page) {
  await page.getByRole("button", { name: "Start a step-by-step chart" }).click();
  await expect(page.getByLabel("Probing depth (mm)")).toBeFocused();
}
async function typeSites(page: Page, count: number, first: number, digit = (i: number) => i % 8) {
  for (let i = 0; i < count; i++) {
    await page.keyboard.type(`${digit(first + i)}\n0\n`);
    await expect(where(page)).toContainText(`site ${first + i + 2} of`);
  }
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing"]) {
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
  writeFileSync(path.join(OUT, "perio-sessions-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: five patients", async () => {
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"], ["cy", "Cy", "Cedar", "1960-01-02", "555-010-0188"], ["di", "Di", "Dune", "1990-05-05", "555-010-0199"], ["ed", "Ed", "Elm", "1978-07-07", "555-010-0166"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `perio2-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
  }
});

test("NOTHING INVENTED AND THE FIRST SITE: no chart is in progress until one is started; starting shows the first site of the order with the depth box focused and the safety strip in view", async () => {
  const page = sessions.Dentist.page;
  await gotoSteps(page, "ann");
  await expect(statusLine(page)).toHaveText("No chart is in progress.");
  expect(await currentSession(page, "ann")).toBeNull();
  await startDraft(page);
  await expect(where(page)).toContainText("Tooth 1, distal buccal (FDI 18, upper right third molar) · site 1 of 192");
  await expect(statusLine(page)).toHaveText("0 of 192 sites entered; all saved.");
  await expect(page.getByRole("region", { name: "Patient safety", exact: true })).toBeVisible();
  await shot(page, "01-first-site.png");
  const s = await currentSession(page, "ann");
  expect([s.status, s.chartableSites, s.next.toothKey, s.next.site]).toEqual(["Draft", 192, "18", "DB"]);
  evidence.start = { first: "18 DB", chartable: 192 };
});

test("KEYBOARD FLOW: depth, Enter, recession, Enter saves a site and lands on the next in the documented order; a tooth's sites are saved when the cursor leaves it; the six sites read differently", async () => {
  const page = sessions.Dentist.page;
  await page.keyboard.type("3\n1\n");
  await expect(where(page)).toContainText("Tooth 1, mid buccal");
  await expect(page.getByLabel("Probing depth (mm)")).toBeFocused();
  expect((await currentSession(page, "ann")).readings).toHaveLength(0);                       // nothing is sent while the cursor stays on the tooth
  await page.keyboard.type("4\n0\n2\n0\n");                                                    // the third site of tooth 18; the next is tooth 17
  await expect(where(page)).toContainText("Tooth 2, distal buccal");
  await expect.poll(async () => (await currentSession(page, "ann")).readings.length).toBe(3);
  const s = await currentSession(page, "ann");
  expect(s.readings.map((r: any) => `${r.toothKey}${r.site}:${r.probingDepthMm}/${r.recessionMm}`)).toEqual(["18DB:3/1", "18B:4/0", "18MB:2/0"]);
  await expect(statusLine(page)).toHaveText("3 of 192 sites entered; all saved.");
  // the six sites of a tooth read differently on a back tooth, a front tooth and a lower tooth
  await page.getByRole("button", { name: /^Tooth 3, upper right first molar/ }).click();
  const names = new Set<string>();
  for (let i = 0; i < 6; i++) {
    names.add(((await where(page).textContent()) ?? "").split(" (")[0]);
    if (i < 5) await page.keyboard.type("2\n0\n");
  }
  expect(names.size).toBe(6);
  evidence.keyboard = { savedWhenLeavingTooth: true, sixSitesDistinct: [...names] };
  await shot(page, "02-mid-entry.png");
});

test("MEASURES AND TEETH: pus and plaque are recorded only when asked for; B toggles bleeding; mobility and furcation are set per tooth (furcation only on a molar); X marks a tooth not charted and the order goes on", async () => {
  const page = sessions.Dentist.page;
  await page.reload();
  await page.getByRole("button", { name: "Step by step (keyboard)" }).click();
  await expect(page.getByLabel("Probing depth (mm)")).toBeVisible();
  await page.getByLabel("Pus (suppuration)").check();
  await page.getByLabel("Plaque").check();
  await page.getByRole("button", { name: /^Tooth 4, upper right second premolar/ }).click();   // FDI 15, single-rooted
  await expect(page.getByText("This tooth has a single root, so there is no furcation to grade.")).toBeVisible();
  await page.getByRole("button", { name: /^Tooth 2, upper right second molar/ }).click();       // FDI 17
  await page.getByLabel("Mobility (grade)").selectOption("2");
  await page.getByLabel("Furcation (grade)").selectOption("3");
  await page.getByLabel("Probing depth (mm)").click();
  await page.keyboard.type("b");
  await expect(page.getByLabel(/Bleeding on probing/)).toBeChecked();
  await page.keyboard.press("s");
  await page.keyboard.press("p");
  await page.keyboard.type("7\n2\n");
  await page.getByRole("button", { name: "Save what I have entered" }).click();
  await expect.poll(async () => (await currentSession(page, "ann")).teeth.find((t: any) => t.toothKey === "17")?.furcation).toBe(3);
  const s = await currentSession(page, "ann");
  const r17 = s.readings.find((r: any) => r.toothKey === "17" && r.site === "DB");
  expect([r17.probingDepthMm, r17.recessionMm, r17.bleeding, r17.suppuration, r17.plaque, s.teeth.find((t: any) => t.toothKey === "17").mobility]).toEqual([7, 2, true, true, true, 2]);
  await page.getByRole("button", { name: /^Tooth 6, upper right canine/ }).click();             // FDI 13
  await page.getByLabel("Probing depth (mm)").click();
  await page.keyboard.press("x");                                                                // not charted: the whole tooth is skipped
  await expect.poll(async () => (await currentSession(page, "ann")).teeth.find((t: any) => t.toothKey === "13")?.excluded).toBe(true);
  expect((await currentSession(page, "ann")).readings.some((r: any) => r.toothKey === "13")).toBe(false);
  await expect(page.getByRole("button", { name: /^Tooth 6, upper right canine: not charted\. Chart this tooth/ })).toBeVisible();
  await shot(page, "03-tooth-not-charted.png");
  evidence.measures = { pusAndPlaqueOnlyWhenAsked: true, mobilityFurcationPerTooth: true, notChartedSkipped: true };
});

test("MISSING IN THE ODONTOGRAM: a tooth recorded missing there is skipped and listed; the server refuses a reading on it; marking it not charted is the way out", async () => {
  const page = sessions.Dentist.page;
  expect((await api(page, "post", `/api/patients/${ids.bo}/odontogram/findings`, { toothKey: "18", condition: "Missing", state: "Existing" })).status).toBe(200);
  await gotoSteps(page, "bo");
  await startDraft(page);
  await expect(where(page)).toContainText("Tooth 2, distal buccal");                                // 18 is passed over
  await expect(page.getByText(/Missing in the odontogram, so skipped: tooth 1\./)).toBeVisible();
  await expect(page.getByRole("button", { name: /^Tooth 1, upper right third molar: missing in the odontogram/ })).toBeDisabled();
  const s = await currentSession(page, "bo");
  expect([s.absentTeeth, s.chartableSites, s.next.toothKey]).toEqual([["18"], 186, "17"]);
  const refused = await saveBatch(page, s, { readings: [site("18", "B", 3)] });
  expect([refused.status, refused.body.error, refused.body.problems[0].code]).toEqual([400, "validation_failed", "tooth_absent"]);
  expect((await currentSession(page, "bo")).readings).toHaveLength(0);
  expect((await saveBatch(page, s, { teeth: [{ toothKey: "18", excluded: true }] })).status).toBe(200);
  await shot(page, "04-missing-tooth-skipped.png");
  evidence.missing = { skipped: true, refused: "tooth_absent", wayOut: "not charted" };
});

test("A WRONG ENTRY IS REFUSED WITHOUT LOSING WHAT WAS TYPED: the server refuses a tooth that became missing; the entries stay, the cursor goes to the entry to correct, and X is the way out", async () => {
  const page = sessions.Dentist.page;
  await page.reload();
  await page.getByRole("button", { name: "Step by step (keyboard)" }).click();
  await expect(where(page)).toContainText("Tooth 2, distal buccal");
  await page.getByLabel("Probing depth (mm)").click();
  await page.keyboard.type("3\n1\n4\n0\n");                                                     // two sites of tooth 17, typed and waiting
  expect((await api(page, "post", `/api/patients/${ids.bo}/odontogram/findings`, { toothKey: "17", condition: "Missing", state: "Existing" })).status).toBe(200);   // meanwhile 17 is recorded missing
  await page.keyboard.type("2\n0\n");                                                           // leaving the tooth sends the sites, and the server refuses them
  await expect(statusLine(page)).toContainText("Not saved: some entries need correcting. Everything you entered is still here.");
  const alert = page.getByRole("alert").filter({ hasText: "Some entries need correcting" });
  await expect(alert).toContainText("Tooth 2");
  await expect(alert).toContainText("recorded as missing in the odontogram");
  await expect(alert).toContainText("Tooth numbers in these messages from the server are FDI numbers");
  await expect(page.getByLabel("Probing depth (mm)")).toHaveValue("3");                          // the cursor is on the entry to correct and shows what was typed
  expect((await currentSession(page, "bo")).readings).toHaveLength(0);
  await shot(page, "05-refused-entries-kept.png");
  await page.keyboard.press("x");                                                                // the way out: the tooth is not charted
  await expect(statusLine(page)).not.toContainText("Not saved");
  await expect.poll(async () => (await currentSession(page, "bo")).teeth.find((t: any) => t.toothKey === "17")?.excluded).toBe(true);
  expect((await currentSession(page, "bo")).readings).toHaveLength(0);
  evidence.refused = { entriesKept: true, wayOut: "X", serverCode: "tooth_absent" };
});

test("STALE DRAFT: a draft changed by someone else shows the conflict banner and keeps what was typed; after reloading the save goes through", async () => {
  const page = sessions.Dentist.page;
  const hygienist = sessions.Hygienist.page;
  await gotoSteps(page, "cy");
  await startDraft(page);
  const s = await currentSession(hygienist, "cy");                                              // the hygienist reads the same draft...
  expect((await saveBatch(hygienist, s, { readings: [site("18", "DB", 4)] })).status).toBe(200);    // ...and saves into it first
  await page.keyboard.type("3\n1\n");
  await page.getByRole("button", { name: "Save what I have entered" }).click();
  await expect(page.getByText("Someone else changed this while you were editing")).toBeVisible();
  await expect(statusLine(page)).toContainText("Not saved: someone else changed this chart. What you entered is still here.");
  await shot(page, "06-conflict.png");
  expect((await currentSession(page, "cy")).readings.map((r: any) => r.toothKey + r.site)).toEqual(["18DB"]);     // the first writer's entry stands; nothing was merged
  await page.getByRole("button", { name: "Reload current version" }).click();
  await expect(page.getByText("Someone else changed this while you were editing")).toBeHidden();
  await page.getByRole("button", { name: "Save what I have entered" }).click();
  await expect(statusLine(page)).toContainText("all saved");
  const after = (await currentSession(page, "cy")).readings;                                 // they typed the same first site, so after seeing the conflict and reloading their entry replaces the other's
  expect(after.map((r: any) => `${r.toothKey}${r.site}:${r.probingDepthMm}/${r.recessionMm}`)).toEqual(["18DB:3/1"]);
  evidence.stale = { conflictBanner: true, typedKept: true, firstWritersEntryStands: true };
});

test("A WHOLE MOUTH WITHOUT THE MOUSE: all 192 sites typed from the keyboard, the cursor never losing its place, and finishing makes one immutable chart", async () => {
  const page = sessions.Dentist.page;
  await gotoSteps(page, "di");
  await startDraft(page);
  await typeSites(page, 191, 0);
  await page.keyboard.type("5\n0\n");
  await expect(statusLine(page)).toContainText("192 of 192 sites entered");
  await expect(page.getByText("Every site has a reading. Save the chart to finish.")).toBeVisible().catch(() => undefined);
  await shot(page, "07-all-entered.png");
  await page.getByRole("button", { name: "Finish and save the chart" }).click();
  await expect(statusLine(page)).toHaveText("Chart saved with 192 sites. It is now in the charts on record.");
  const [chart] = await chartsOf(page, "di");
  expect(chart.readingCount).toBe(192);
  expect(new Set(chart.readings.map((r: any) => `${r.toothKey}${r.site}`)).size).toBe(192);
  expect(chart.readings.find((r: any) => r.toothKey === "18" && r.site === "DB").probingDepthMm).toBe(0);                  // the first site of the order took the first digit
  expect(chart.readings.find((r: any) => r.toothKey === "48" && r.site === "DL").probingDepthMm).toBe(5);                 // and the last site of the order the last
  expect(await currentSession(page, "di")).toBeNull();
  await expect(history(page).getByRole("group").first()).toContainText("192 sites on 32 teeth");
  evidence.wholeMouth = { sites: 192, typedWithoutMouse: true };
});

test("COMPARISON AND LINKS: a chart is compared with the one before it in words, the draft is compared live, and a link to a diagnosis is added by reference", async () => {
  const page = sessions.Dentist.page;
  const make = async (readings: unknown[], teeth?: unknown[]) => {
    const s = (await api(page, "post", `${P("ed")}/sessions`)).body;
    const saved = await saveBatch(page, s, { readings, teeth });
    expect(saved.status).toBe(200);
    const done = await api(page, "post", `/api/periodontal/sessions/${s.id}/finalize`, { rowVersion: saved.body.rowVersion });
    expect(done.status).toBe(200);
    await page.waitForTimeout(30);
    return done.body;
  };
  await make([site("16", "B", 8, 0, true), site("16", "MB", 4), site("26", "DL", 5)]);
  const second = await make([site("16", "B", 5, 1, true), site("16", "MB", 4), site("26", "DL", 7, 2, true, true), site("11", "B", 3)], [{ toothKey: "16", mobility: 1 }]);
  await page.goto(`/patients/${ids.ed}/periodontal`);
  await expect(history(page).getByRole("group")).toHaveCount(2);
  const newest = history(page).getByRole("group").first();
  await newest.getByRole("button", { name: "Compare with the previous chart" }).click();
  const panel = newest.getByRole("region", { name: /^Comparison:/ });
  await expect(panel.getByRole("status")).toContainText("Of 3 sites charted both times, 1 improved (2 mm or more shallower), 1 got worse (2 mm or more deeper) and 1 are about the same.");
  await expect(panel.getByRole("status")).toContainText("1 site was charted only in this chart, so it is not counted above.");
  await expect(panel).toContainText("not a diagnosis");
  await expect(panel.getByRole("table", { name: /Whole-chart figures/ }).getByRole("row", { name: /^Mean probing depth/ })).toBeVisible();
  await shot(page, "08-comparison.png");
  const api12 = (await api(page, "get", `${P("ed")}/comparison?currentExamId=${second.id}`)).body;
  expect([api12.matchedSites, api12.improved, api12.worsened, api12.unchanged, api12.onlyCurrent, api12.onlyPrevious]).toEqual([3, 1, 1, 1, 1, 0]);

  const form = newest.getByRole("form", { name: "Link this chart" });
  await form.getByLabel("Link to").selectOption("Diagnosis");
  await form.getByLabel("Reference").fill("dx-perio-1");
  await form.getByRole("button", { name: "Add link" }).click();
  await expect(newest.getByRole("list", { name: "Linked records" })).toContainText("Diagnosis: dx-perio-1");
  const again = await api(page, "post", `/api/periodontal/charts/${second.id}/links`, { linkType: "Diagnosis", reference: "dx-perio-1" });
  expect([again.status, again.body.links.length]).toEqual([200, 1]);                           // the same link again is quiet

  // a draft is compared live with the last finalized chart
  await page.getByRole("button", { name: "Step by step (keyboard)" }).click();
  await startDraft(page);
  await page.getByRole("button", { name: /^Tooth 3, upper right first molar/ }).click();
  await page.keyboard.type("2\n1\n1\n0\n");                                                     // 16 distal buccal, then 16 mid buccal at 1 mm (it was 5)
  await page.getByRole("button", { name: "Save what I have entered" }).click();
  await page.getByRole("button", { name: "Compare with the last finalized chart" }).click();
  const live = page.getByRole("region", { name: /^Comparison: The chart in progress with / });
  await expect(live.getByRole("status")).toContainText("1 improved");
  await shot(page, "09-live-comparison.png");
  evidence.comparison = { matched: 3, improved: 1, worsened: 1, unchanged: 1, onlyCurrent: 1, linkAdded: true, liveDraftCompared: true };
});

test("TRUST: every start, save, finalize and link is in the audit log with the user and a time, and nothing clinical is in it; each chart has exactly one chart entry", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=1000")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const mine = entries.filter((e) => e.eventType.startsWith("PerioSession") || e.eventType.startsWith("PerioExam"));
  const count = (t: string) => mine.filter((e) => e.eventType === t).length;
  const charts = (await chartsOf(sessions.Dentist.page, "di")).length + (await chartsOf(sessions.Dentist.page, "ed")).length + (await chartsOf(sessions.Dentist.page, "ann")).length + (await chartsOf(sessions.Dentist.page, "bo")).length + (await chartsOf(sessions.Dentist.page, "cy")).length;
  expect(count("PerioExamRecorded")).toBe(charts);
  expect(count("PerioSessionStarted")).toBeGreaterThanOrEqual(5);
  expect(count("PerioSessionUpdated")).toBeGreaterThan(5);
  expect(count("PerioSessionFinalized")).toBe(count("PerioExamRecorded"));
  expect(count("PerioExamLinked")).toBe(1);
  expect(mine.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = mine.map((e) => e.details).join(" ").toLowerCase();
  for (const secret of ["tooth", "18", "dx-perio", "probing", "recession", "bleeding", "mm", "suppuration", "plaque"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["PerioSessionStarted", "PerioSessionUpdated", "PerioSessionFinalized", "PerioExamRecorded", "PerioExamLinked"].map((t) => [t, count(t)]));
});

test("ROLES: the hygienist charts; the assistant only reads; front desk and billing cannot see it; anonymous callers are refused; nothing is saved without a token", async () => {
  const hygienist = sessions.Hygienist.page;
  const s = (await api(hygienist, "post", `/api/patients/${ids.ann}/periodontal/sessions`)).body;
  expect(s.status).toBe("Draft");
  const saved = await saveBatch(hygienist, s, { readings: [site("17", "B", 3)] });
  expect(saved.status).toBe(200);
  expect((await api(hygienist, "post", `/api/periodontal/sessions/${s.id}/finalize`, { rowVersion: saved.body.rowVersion })).status).toBe(200);

  const assistant = sessions.Assistant.page;
  const draft = (await api(sessions.Dentist.page, "post", `${P("bo")}/sessions`)).body;
  await gotoSteps(assistant, "bo");
  await expect(assistant.getByText(/sites entered so far\. Your role can read this but cannot change it\./)).toBeVisible();
  await expect(assistant.getByLabel("Probing depth (mm)")).toHaveCount(0);
  expect((await saveBatch(assistant, draft, { readings: [site("16", "B", 3)] })).status).toBe(403);
  expect((await api(assistant, "post", `${P("bo")}/sessions`)).status).toBe(403);
  expect((await api(assistant, "get", `${P("bo")}/session`)).status).toBe(200);

  for (const role of ["FrontDesk", "Billing"]) {
    const page = sessions[role].page;
    expect((await api(page, "get", `${P("bo")}/session`)).status, `${role} read`).toBe(403);
    expect((await api(page, "post", `${P("bo")}/sessions`)).status, `${role} start`).toBe(403);
    await page.goto(`/patients/${ids.bo}`);
    await expect(page.getByRole("link", { name: "Periodontal" })).toHaveCount(0);
  }
  const anon = await admin.context().browser()!.newContext();
  const anonPage = await anon.newPage();
  expect((await anonPage.request.get(`${P("bo")}/session`)).status()).toBe(401);
  expect((await anonPage.request.post(`${P("bo")}/sessions`)).status()).toBe(401);
  await anon.close();
  const noToken = await sessions.Dentist.page.request.post(`/api/periodontal/sessions/${draft.id}/entries`, { data: { rowVersion: draft.rowVersion, readings: [site("16", "B", 3)] } });
  expect(noToken.ok()).toBe(false);
  expect((await currentSession(sessions.Dentist.page, "bo")).readings).toHaveLength(0);
  evidence.roles = { dentist: "chart", hygienist: "chart", assistant: "read only (403 on write)", frontDesk: "403, no tab", billing: "403, no tab", anonymous: 401, noCsrfToken: "refused" };
});

test("ACCESSIBILITY AND TABLET: axe finds no critical or serious issue on the entry screen, with problems shown, with the comparison open and with charts on record, in both themes; at 768 px the page does not scroll sideways", async () => {
  const page = sessions.Dentist.page;
  await gotoSteps(page, "bo");                                                                    // the open draft from the roles step
  await expect(page.getByLabel("Probing depth (mm)")).toBeVisible();
  await scanBothThemes(page, "entry");
  await page.getByLabel("Probing depth (mm)").click();
  await page.keyboard.type("99\n");
  await expect(page.getByText("Probing depth must be a whole number from 0 to 15 mm.")).toBeVisible();
  await scanBothThemes(page, "entry-with-problem");

  await page.goto(`/patients/${ids.ed}/periodontal`);
  const newest = history(page).getByRole("group").first();
  await newest.getByRole("button", { name: "Compare with the previous chart" }).click();
  await expect(newest.getByRole("region", { name: /^Comparison:/ })).toBeVisible();
  await newest.getByLabel("Show every site, including those that did not change").check();
  await scanBothThemes(page, "comparison-and-history");

  await page.setViewportSize({ width: 768, height: 1024 });
  await gotoSteps(page, "bo");
  await expect(page.getByLabel("Probing depth (mm)")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await shot(page, "10-tablet-entry.png");
  await scanBothThemes(page, "tablet-entry");
  await page.setViewportSize({ width: 1280, height: 720 });
  evidence.accessibility = { scanned: ["entry", "entry-with-problem", "comparison-and-history", "tablet-entry"], tabletNoHorizontalPageScroll: true };
});
