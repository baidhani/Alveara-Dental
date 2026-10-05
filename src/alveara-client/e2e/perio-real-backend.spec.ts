import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-012 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.perio.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a practice would: periodontal charting records probing depth, recession and bleeding (the tooth stored as its FDI key whatever number is
// shown, attachment loss derived), incorrect data is refused with every problem named and nothing saved, and every chart is in the audit log with the user and a time (and nothing
// clinical is). Then each failure path: a dropped connection that keeps what was typed and makes one chart on retry, a retry with the same key, a different chart under a used key, and
// every role. Finally it scans the screens with axe in both themes, at desktop and tablet width.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PERIO_E2E_OUT ?? path.join(process.cwd(), "perio-e2e-out");
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

const chartsOf = async (page: Page, patient = "ann") => (await api(page, "get", `/api/patients/${ids[patient]}/periodontal/charts`)).body.exams as any[];
const site = (tooth: string, siteCode: string, pd: number, rec: number, bleeding: boolean) => ({ toothKey: tooth, site: siteCode, probingDepthMm: pd, recessionMm: rec, bleeding });
const saveViaApi = (page: Page, key: string, readings: unknown[], patient = "ann") => api(page, "post", `/api/patients/${ids[patient]}/periodontal/charts`, { idempotencyKey: key, readings });

// Universal numbering is shown, so Universal 3 is FDI 16 and Universal 14 is FDI 26
const box = (page: Page, what: "Probing depth in millimetres" | "Recession in millimetres", tooth: string, siteName: string) => page.getByRole("textbox", { name: `${what}, tooth ${tooth}, ${siteName}` });
const bleed = (page: Page, tooth: string, siteName: string) => page.getByRole("checkbox", { name: `Bleeding on probing, tooth ${tooth}, ${siteName}` });
const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const history = (page: Page) => page.getByRole("region", { name: "Charts on record" });

async function gotoPerio(page: Page, patient = "ann") {
  await page.goto(`/patients/${ids[patient]}/periodontal`);
  await expect(page.getByRole("heading", { name: "Periodontal chart" })).toBeVisible();
  await expect(history(page).getByRole("heading", { name: "Charts on record" })).toBeVisible();
}

async function chart(page: Page, tooth: string, siteName: string, pd: string, rec: string, bleeding = false) {
  await box(page, "Probing depth in millimetres", tooth, siteName).fill(pd);
  await box(page, "Recession in millimetres", tooth, siteName).fill(rec);
  if (bleeding) await bleed(page, tooth, siteName).check();
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
  writeFileSync(path.join(OUT, "perio-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: two patients", async () => {
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `perio-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
  }
});

test("NOTHING INVENTED: an empty chart has 192 empty boxes, says no chart is on record (never healthy), and the patient-safety strip stays in view", async () => {
  const page = sessions.Dentist.page;
  await gotoPerio(page);
  await expect(page.getByRole("textbox")).toHaveCount(32 * 6 * 2);
  await expect(page.getByRole("checkbox")).toHaveCount(32 * 6);
  expect(await page.getByRole("textbox").evaluateAll((els) => els.every((e) => (e as HTMLInputElement).value === ""))).toBe(true);
  await expect(history(page)).toContainText("No periodontal chart has been recorded for this patient. That means none has been taken, not that the gums are healthy.");
  await expect(page.getByRole("region", { name: "Patient safety", exact: true })).toBeVisible();
  await shot(page, "01-empty-chart.png");
  expect(await chartsOf(page)).toEqual([]);
  evidence.nothingInvented = { charts: 0, emptyBoxes: 192, safetyStripInView: true };
});

test("ACCEPTANCE 1: charting records probing depth, recession and bleeding, the tooth is stored as its FDI key whatever number is shown, and attachment loss is derived", async () => {
  const page = sessions.Dentist.page;
  await gotoPerio(page);
  await chart(page, "3", "mid buccal", "5", "2", true);                                   // Universal 3 = FDI 16
  await chart(page, "3", "mesial buccal", "3", "0");
  await chart(page, "14", "distal lingual", "7", "1", true);                              // Universal 14 = FDI 26
  await expect(statusLine(page)).toHaveText("3 sites entered. Nothing is saved until you save the chart.");
  await shot(page, "02-entered.png");
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(statusLine(page)).toContainText("Chart saved with 3 sites.");

  const [saved, ...rest] = await chartsOf(page);
  expect(rest).toEqual([]);
  expect(saved.recordedByName).toBe(NAMES.Dentist);
  expect(saved.readingCount).toBe(3);
  const byKey = Object.fromEntries(saved.readings.map((r: any) => [`${r.toothKey}${r.site}`, [r.probingDepthMm, r.recessionMm, r.attachmentLossMm, r.bleeding]]));
  expect(byKey).toEqual({ "16B": [5, 2, 7, true], "16MB": [3, 0, 3, false], "26DL": [7, 1, 8, true] });   // FDI keys stored; attachment loss = depth + recession

  const latest = history(page).getByRole("group").first();
  await expect(latest).toContainText(`by ${NAMES.Dentist} — 3 sites on 2 teeth, 67% bleeding (latest)`);
  await expect(latest.getByRole("list", { name: "Figures for this chart" })).toContainText("Sites 4 mm or deeper: 2");
  await expect(latest.getByRole("row", { name: /^14 DL 7 1 8 Yes$/ })).toBeVisible();
  await shot(page, "03-saved-with-history.png");
  evidence.acceptance1 = { saved: byKey, recordedBy: saved.recordedByName };
});

test("ACCEPTANCE 2: incorrect data is refused with every problem named, the boxes are marked, and nothing is saved", async () => {
  const page = sessions.Dentist.page;
  await gotoPerio(page);
  const before = (await chartsOf(page)).length;
  await chart(page, "3", "mid buccal", "99", "1");
  await box(page, "Probing depth in millimetres", "3", "mesial buccal").fill("4");          // no recession entered
  await page.getByRole("button", { name: "Save chart" }).click();
  const alert = page.getByRole("alert").filter({ hasText: "Some entries need correcting" });
  await expect(alert).toContainText("Tooth 3, mid buccal: Probing depth must be a whole number from 0 to 15 mm.");
  await expect(alert).toContainText("Tooth 3, mesial buccal: Enter the recession for this site, or 0 if there is none.");
  await expect(statusLine(page)).toHaveText("Not saved: some entries need correcting. Nothing was sent.");
  await expect(box(page, "Probing depth in millimetres", "3", "mid buccal")).toHaveAttribute("aria-invalid", "true");
  await expect(alert).toBeFocused();
  await shot(page, "04-problems.png");
  expect((await chartsOf(page)).length).toBe(before);                                   // nothing saved

  await alert.getByRole("button", { name: "Tooth 3, mesial buccal" }).click();
  await expect(box(page, "Recession in millimetres", "3", "mesial buccal")).toBeFocused();   // the list takes the person to the box to correct
  await box(page, "Probing depth in millimetres", "3", "mid buccal").fill("6");
  await box(page, "Recession in millimetres", "3", "mesial buccal").fill("0");
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(statusLine(page)).toContainText("Chart saved with 2 sites.");
  expect((await chartsOf(page)).length).toBe(before + 1);                               // corrected, then saved as a new chart

  // the server refuses the same mistakes for any caller, naming every one, whatever the screen does
  const sent = await saveViaApi(page, "bad-1", [site("16", "B", 99, -1, true), site("19", "B", 3, 0, false), site("17", "Q", 3, 0, false), site("55", "B", 3, 0, false), site("26", "B", 3, 1, true)]);
  expect(sent.status).toBe(400);
  expect(sent.body.error).toBe("validation_failed");
  expect(sent.body.problems.map((p: any) => p.code)).toEqual(["out_of_range", "out_of_range", "unknown_tooth", "unknown_site", "primary_tooth"]);
  const missing = await api(page, "post", `/api/patients/${ids.ann}/periodontal/charts`, { idempotencyKey: "bad-2", readings: [{ toothKey: "16", site: "B" }] });
  expect(missing.body.problems.map((p: any) => p.field)).toEqual(["probingDepthMm", "recessionMm", "bleeding"]);       // a value left out is never defaulted
  expect((await chartsOf(page)).length).toBe(before + 1);
  evidence.acceptance2 = { screenProblems: 2, serverProblems: sent.body.problems.map((p: any) => p.code), missingFields: missing.body.problems.map((p: any) => p.field) };
});

test("RETRIES: the same key and chart returns the chart already saved; a different chart under a used key is refused; the history lists each chart once", async () => {
  const page = sessions.Dentist.page;
  const before = (await chartsOf(page)).length;
  const readings = [site("11", "B", 3, 0, false), site("11", "MB", 4, 1, true)];
  const first = await saveViaApi(page, "visit-a", readings);
  const again = await saveViaApi(page, "visit-a", [...readings].reverse());                // the same chart, sent in another order
  expect([first.status, again.status, again.body.id === first.body.id]).toEqual([200, 200, true]);
  const other = await saveViaApi(page, "visit-a", [site("11", "B", 9, 0, false)]);
  expect([other.status, other.body.error]).toEqual([409, "idempotency_key_reused"]);
  expect((await chartsOf(page)).length).toBe(before + 1);
  const races = await Promise.all(Array.from({ length: 6 }, () => saveViaApi(page, "double-click", [site("21", "B", 3, 0, false)])));
  expect(new Set(races.map((r) => r.body.id)).size).toBe(1);                              // six simultaneous saves, one chart
  expect((await chartsOf(page)).length).toBe(before + 2);
  evidence.retries = { sameKeySameChart: "one chart", sameKeyDifferentChart: 409, sixSimultaneous: "one chart" };
});

test("FAILURE: a dropped connection says so and keeps what was typed; the retry makes exactly one chart; a save the server cannot make says nothing was recorded", async () => {
  const page = sessions.Dentist.page;
  await gotoPerio(page);
  const before = (await chartsOf(page)).length;
  await chart(page, "5", "distal buccal", "4", "1", true);                                 // Universal 5 = FDI 14
  let dropOnce = true;
  await page.route("**/api/patients/*/periodontal/charts", async (route) => {
    if (route.request().method() === "POST" && dropOnce) { dropOnce = false; await route.fetch(); await route.abort("connectionreset"); }   // the server stores the chart, the answer is lost
    else await route.continue();
  });
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(statusLine(page)).toHaveText("Not saved: the connection dropped. What you entered is still here; save again to retry.");
  await expect(box(page, "Probing depth in millimetres", "5", "distal buccal")).toHaveValue("4");
  await shot(page, "05-dropped.png");
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(statusLine(page)).toContainText("Chart saved with 1 site.");
  expect((await chartsOf(page)).length).toBe(before + 1);                                  // stored once, though sent twice
  await page.unroute("**/api/patients/*/periodontal/charts");

  await chart(page, "6", "mid lingual", "3", "0");
  await page.route("**/api/patients/*/periodontal/charts", (route) => route.request().method() === "POST"
    ? route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ error: "save_failed", message: "The chart could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support." }) })
    : route.continue());
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(statusLine(page)).toContainText("Not saved: The chart could not be saved, so nothing was recorded.");
  await expect(box(page, "Probing depth in millimetres", "6", "mid lingual")).toHaveValue("3");
  await page.unroute("**/api/patients/*/periodontal/charts");
  expect((await chartsOf(page)).length).toBe(before + 1);
  evidence.failure = { droppedConnection: "kept entries, one chart on retry", saveFailed: "nothing recorded message, entries kept" };
});

test("CORRECTIONS: a saved chart is never edited; starting from it makes a new chart and the old one stays as it was", async () => {
  const page = sessions.Dentist.page;
  await gotoPerio(page);
  const before = await chartsOf(page);
  const target = before[0];
  const targetReadings = JSON.stringify(target.readings);
  await history(page).getByRole("group").first().getByRole("button", { name: "Start a new chart from these values" }).click();
  await expect(statusLine(page)).toContainText(/sites? entered/);
  const sites = target.readings as any[];
  expect(await page.getByRole("textbox").evaluateAll((els) => els.filter((e) => (e as HTMLInputElement).value !== "").length)).toBe(sites.length * 2);
  await shot(page, "06-started-from-earlier.png");
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(statusLine(page)).toContainText("Chart saved");
  const after = await chartsOf(page);
  expect(after.length).toBe(before.length + 1);
  expect(JSON.stringify(after.find((c) => c.id === target.id).readings)).toBe(targetReadings);   // the earlier chart is exactly as it was
  expect(new Set(after.map((c) => c.id)).size).toBe(after.length);
  evidence.corrections = { newChartsAfterCopy: 1, earlierChartUnchanged: true };
});

test("TRUST: every chart is in the audit log with the user and a time, and nothing clinical is in it", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const mine = entries.filter((e) => e.eventType === "PerioExamRecorded");
  const exams = (await chartsOf(sessions.Dentist.page)).length;
  expect(mine.length).toBe(exams);                                                          // one entry per chart, none for refused or repeated saves
  expect(mine.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = mine.map((e) => e.details).join(" ");
  for (const secret of ["16", "26", "probing", "recession", "bleeding", "mm"]) expect(text.toLowerCase(), `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = { PerioExamRecorded: mine.length, charts: exams };
});

test("ROLES: the dentist and hygienist can chart; the assistant can only read; front desk and billing cannot see it; nothing is saved without sign-in or a token", async () => {
  const hygienist = sessions.Hygienist.page;
  const saved = await saveViaApi(hygienist, "hyg-1", [site("31", "B", 2, 0, false)], "bo");
  expect(saved.status).toBe(200);
  expect(saved.body.recordedByName).toBe(NAMES.Hygienist);

  const assistant = sessions.Assistant.page;
  await gotoPerio(assistant, "bo");
  await expect(assistant.getByRole("button", { name: "Save chart" })).toHaveCount(0);
  await expect(statusLine(assistant)).toHaveText("You can read this but your role cannot chart.");
  await expect(history(assistant).getByRole("group")).toHaveCount(1);
  expect((await saveViaApi(assistant, "asst-1", [site("31", "B", 2, 0, false)], "bo")).status).toBe(403);

  for (const role of ["FrontDesk", "Billing"]) {
    const page = sessions[role].page;
    expect((await api(page, "get", `/api/patients/${ids.bo}/periodontal/charts`)).status, `${role} read`).toBe(403);
    expect((await saveViaApi(page, `${role}-1`, [site("31", "B", 2, 0, false)], "bo")).status, `${role} save`).toBe(403);
    await page.goto(`/patients/${ids.bo}`);
    await expect(page.getByRole("link", { name: "Periodontal" })).toHaveCount(0);
  }
  const anon = await admin.context().browser()!.newContext();
  const anonPage = await anon.newPage();
  expect((await anonPage.request.get(`/api/patients/${ids.bo}/periodontal/charts`)).status()).toBe(401);
  expect((await anonPage.request.post(`/api/patients/${ids.bo}/periodontal/charts`, { data: {} })).status()).toBe(401);
  await anon.close();
  const noToken = await sessions.Dentist.page.request.post(`/api/patients/${ids.bo}/periodontal/charts`, { data: { idempotencyKey: "no-csrf", readings: [site("31", "B", 2, 0, false)] } });
  expect(noToken.ok()).toBe(false);
  expect((await chartsOf(sessions.Dentist.page, "bo")).length).toBe(1);                      // only the hygienist's chart; every refused save left nothing
  evidence.roles = { dentist: "chart", hygienist: "chart", assistant: "read only (403 on save)", frontDesk: "403, no tab", billing: "403, no tab", anonymous: 401, noCsrfToken: "refused" };
});

test("ACCESSIBILITY AND TABLET: axe finds no critical or serious issue on the grid, with problems shown and with charts listed, in both themes; at 768 px the page does not scroll sideways", async () => {
  const page = sessions.Dentist.page;
  await gotoPerio(page);
  await scanBothThemes(page, "grid-and-history");
  await box(page, "Probing depth in millimetres", "3", "mid buccal").fill("99");
  await page.getByRole("button", { name: "Save chart" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Some entries need correcting" })).toBeVisible();
  await scanBothThemes(page, "problems-shown");

  await page.setViewportSize({ width: 768, height: 1024 });
  await gotoPerio(page);
  await expect(page.getByRole("table", { name: "Upper teeth" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);   // the grid scrolls inside its own box, never the page
  await shot(page, "07-tablet.png");
  await scanBothThemes(page, "tablet");
  await page.setViewportSize({ width: 1280, height: 720 });
  evidence.accessibility = { scanned: ["grid-and-history", "problems-shown", "tablet"], tabletNoHorizontalPageScroll: true };
});
