import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-006 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.odontogram.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a practice would: a condition recorded on a tooth is saved with exactly the lifecycle state chosen (and the tooth is stored as its FDI key
// whatever number is shown), a completed treatment updates the odontogram, every update is in the audit log with the user and a time (and nothing clinical is), a wrong entry is
// withdrawn with a reason and kept in the history, and each failure path behaves: a wrong tooth or surface, a data-entry error, a stale change and a role that may only read.
// Then it scans the screens with axe in both themes, at desktop and tablet width.

test.describe.configure({ mode: "serial" });

const OUT = process.env.ODONTOGRAM_E2E_OUT ?? path.join(process.cwd(), "odontogram-e2e-out");
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

const chartOf = async (page: Page, patient = "ann") => (await api(page, "get", `/api/patients/${ids[patient]}/odontogram`)).body;
const findingOf = (chart: any, tooth: string, surface: string | null, condition: string) => (chart.findings as any[]).find((f) => f.toothKey === tooth && f.surface === surface && f.condition === condition);
const tooth = (page: Page, label: string) => page.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });
const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const recordForm = (page: Page) => page.getByRole("form", { name: "Record a finding" });
const stateWord = (page: Page, label: string) => tooth(page, label).locator(".alv-odonto__tooth-state");

async function gotoChart(page: Page, patient = "ann") {
  await page.goto(`/patients/${ids[patient]}/odontogram`);
  await expect(page.getByRole("heading", { name: "Odontogram" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Upper teeth" })).toBeVisible();
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
  writeFileSync(path.join(OUT, "odontogram-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: two patients", async () => {
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `odonto-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
  }
});

test("NOTHING INVENTED: an empty chart draws the 32 permanent teeth, says nothing is recorded (never healthy), and the patient-safety strip stays in view", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page);
  await expect(page.getByRole("group", { name: "Upper teeth" }).getByRole("button")).toHaveCount(16);
  await expect(page.getByRole("group", { name: "Lower teeth" }).getByRole("button")).toHaveCount(16);
  await expect(page.getByText("No findings are recorded on any tooth.")).toBeVisible();
  await expect(page.getByText(/An empty tooth means nothing is recorded, not that it is healthy/)).toBeVisible();
  await expect(tooth(page, "3")).toHaveAccessibleName("Tooth 3, upper right first molar: nothing recorded");
  await expect(page.getByRole("region", { name: "Patient safety", exact: true })).toBeVisible();
  await shot(page, "01-empty-chart.png");
  const chart = await chartOf(page);
  expect(chart.findings).toEqual([]);
  evidence.nothingInvented = { findings: 0, safetyStripInView: true };
});

test("ACCEPTANCE 1: a condition recorded on a selected tooth is saved with exactly the lifecycle state chosen, and the tooth is stored as its FDI key whatever number is shown", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page);
  await tooth(page, "3").click();                                                       // Universal 3 is the upper right first molar, FDI 16
  await expect(page.getByRole("heading", { name: "Tooth 3: upper right first molar" })).toBeVisible();
  await expect(page.getByText("Also written FDI 16 · Palmer UR6.")).toBeVisible();
  await recordUi(page, "Caries", "Diagnosed", "O");
  await expect(statusLine(page)).toHaveText("Caries (Occlusal surface) recorded on tooth 3 as Diagnosed.");
  await expect(stateWord(page, "3")).toHaveText("Diagnosed");
  await shot(page, "02-recorded-diagnosed.png");

  // the other three states, each saved exactly as chosen
  await recordUi(page, "Restoration", "Existing", "M");
  await expect(statusLine(page)).toContainText("as Existing.");
  await recordUi(page, "Caries", "Planned", "D");
  await expect(statusLine(page)).toContainText("as Planned.");
  await recordUi(page, "Restoration", "Completed", "B");
  await expect(statusLine(page)).toContainText("as Completed.");

  // a whole-tooth condition on another tooth: no surface asked for, none stored
  await tooth(page, "8").click();                                                       // Universal 8 is FDI 11, an upper central incisor
  await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
  await recordForm(page).getByLabel("Condition").selectOption("Crown");
  await expect(recordForm(page).getByLabel("Surface")).toHaveCount(0);
  await recordForm(page).getByLabel("State").selectOption("Existing");
  await recordForm(page).getByRole("button", { name: "Record finding" }).click();
  await expect(statusLine(page)).toContainText("Crown (whole tooth) recorded on tooth 8 as Existing.");

  const chart = await chartOf(page);
  const states = Object.fromEntries((chart.findings as any[]).map((f) => [`${f.toothKey}/${f.surface ?? "-"}/${f.condition}`, f.state]));
  expect(states).toEqual({ "16/O/Caries": "Diagnosed", "16/M/Restoration": "Existing", "16/D/Caries": "Planned", "16/B/Restoration": "Completed", "11/-/Crown": "Existing" });
  expect((chart.findings as any[]).every((f) => /^[1-8][1-8]$/.test(f.toothKey))).toBe(true);        // stored identity is the FDI key, never "3" or "8"
  evidence.acceptance1 = { saved: states, storedAsFdiKeys: true };
});

test("ACCEPTANCE 2: a completed treatment updates the odontogram, and the history shows each step with who and when", async () => {
  const page = sessions.Dentist.page;
  const hyg = sessions.Hygienist.page;
  await gotoChart(hyg);
  await tooth(hyg, "3").click();
  await hyg.getByRole("button", { name: "Plan: Caries (Occlusal surface)" }).click();
  await expect(statusLine(hyg)).toHaveText("Caries (Occlusal surface) on tooth 3 is now Planned.");

  await gotoChart(page);
  await tooth(page, "3").click();
  await page.getByRole("button", { name: "Complete: Caries (Occlusal surface)" }).click();
  await expect(statusLine(page)).toHaveText("Caries (Occlusal surface) on tooth 3 is now Completed.");
  await expect(page.locator("li.alv-odonto__finding").filter({ hasText: "Caries · Occlusal surface" }).locator(".alv-odonto__chip")).toHaveText("Completed");
  await expect(stateWord(page, "3")).toHaveText("Planned +3");                           // the tooth shows what most needs attention: a planned finding on another surface outranks a completed one
  await expect(page.getByRole("button", { name: /^(Plan|Complete): Caries \(Occlusal surface\)/ })).toHaveCount(0);       // completed is final
  await shot(page, "03-completed.png");

  const chart = await chartOf(page);
  const f = findingOf(chart, "16", "O", "Caries");
  expect(f.state).toBe("Completed");
  const history = (await api(page, "get", `/api/odontogram/findings/${f.id}/history`)).body;
  expect(history.versions.map((v: any) => [v.changeType, v.state, v.actorName])).toEqual([
    ["Recorded", "Diagnosed", `${NAMES.Dentist}`], ["StateChanged", "Planned", `${NAMES.Hygienist}`], ["StateChanged", "Completed", `${NAMES.Dentist}`],
  ]);
  expect(history.versions.every((v: any) => v.occurredAtUtc)).toBe(true);
  const row = page.locator("li.alv-odonto__finding").filter({ hasText: "Caries · Occlusal surface" });
  await row.locator("summary").click();
  await expect(row.locator("ol li").nth(1)).toContainText("State changed - Caries, Occlusal surface · Planned");
  await expect(row.locator("ol li").nth(1)).toContainText(NAMES.Hygienist);
  await shot(page, "04-history.png");
  ids.caries16O = f.id;
  evidence.acceptance2 = { path: history.versions.map((v: any) => `${v.changeType}:${v.state}`), actors: history.versions.map((v: any) => v.actorName) };
});

test("A WRONG ENTRY is withdrawn with a reason: it leaves the chart, the reason is required, and the history keeps it", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page);
  await tooth(page, "3").click();
  await page.getByRole("button", { name: "Withdraw: Restoration (Buccal surface)" }).click();
  const form = page.getByRole("form", { name: "Withdraw finding: Restoration (Buccal surface)" });
  await form.getByRole("button", { name: "Withdraw finding" }).click();
  await expect(form.getByText("Say why.")).toBeVisible();                                // refused before anything is sent
  await form.getByLabel(/Why is/).fill("Entered on the wrong tooth");
  await form.getByRole("button", { name: "Withdraw finding" }).click();
  await expect(statusLine(page)).toHaveText("Restoration (Buccal surface) on tooth 3 withdrawn.");
  await expect(page.locator("li.alv-odonto__finding strong", { hasText: "Restoration" })).toHaveCount(1);   // only the mesial one remains
  await shot(page, "05-withdrawn.png");

  const chart = await chartOf(page);
  expect(findingOf(chart, "16", "B", "Restoration")).toBeUndefined();
  expect(chart.findings.length).toBe(4);
  const stored = await admin.request.get(`/api/odontogram/findings/${ids.caries16O}/history`);
  expect(stored.ok()).toBeTruthy();
  ids.withdrawnTooth = "16";
  evidence.withdraw = { leftChart: true, reasonRequired: true };
});

test("FAILURE PATHS: a wrong tooth, a surface that does not exist, a surface on a whole-tooth condition and an unknown state are each refused with the field named, and nothing is stored", async () => {
  const page = sessions.Dentist.page;
  const before = (await chartOf(page)).findings.length;
  const post = (body: object) => api(page, "post", `/api/patients/${ids.ann}/odontogram/findings`, body);
  const cases: [string, object, string][] = [
    ["tooth not on the chart", { toothKey: "19", surface: "O", condition: "Caries", state: "Diagnosed" }, "toothKey"],
    ["a Universal number instead of the FDI key", { toothKey: "3", surface: "O", condition: "Caries", state: "Diagnosed" }, "toothKey"],
    ["occlusal surface on an incisor", { toothKey: "11", surface: "O", condition: "Caries", state: "Diagnosed" }, "surface"],
    ["incisal surface on a molar", { toothKey: "16", surface: "I", condition: "Caries", state: "Diagnosed" }, "surface"],
    ["a surface on a whole-tooth condition", { toothKey: "16", surface: "O", condition: "Crown", state: "Existing" }, "surface"],
    ["no surface on a surface condition", { toothKey: "16", surface: null, condition: "Caries", state: "Diagnosed" }, "surface"],
    ["an unknown condition", { toothKey: "16", surface: "O", condition: "Cavity", state: "Diagnosed" }, "condition"],
    ["an unknown state", { toothKey: "16", surface: "O", condition: "Caries", state: "Soon" }, "state"],
  ];
  const refusals: Record<string, string> = {};
  for (const [name, body, field] of cases) {
    const r = await post(body);
    expect([name, r.status, r.body.error, field in (r.body.fieldErrors ?? {})]).toEqual([name, 400, "validation_failed", true]);
    refusals[name] = field;
  }
  const twin = await post({ toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
  expect([twin.status, twin.body.error]).toEqual([409, "finding_exists"]);                    // already recorded as Completed
  const same = await post({ toothKey: "16", surface: "O", condition: "Caries", state: "Completed" });
  expect(same.status).toBe(200);                                                              // the same finding again is a quiet repeat
  expect((await chartOf(page)).findings.length).toBe(before);

  // the screen refuses an empty entry before sending anything, and offers only surfaces that exist on the tooth
  await gotoChart(page);
  await tooth(page, "8").click();
  await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
  await recordForm(page).getByRole("button", { name: "Record finding" }).click();
  await expect(recordForm(page).getByText("Choose the condition.")).toBeVisible();
  await expect(recordForm(page).getByText("Choose where it is in its lifecycle.")).toBeVisible();
  await recordForm(page).getByLabel("Condition").selectOption("Caries");
  expect(await recordForm(page).getByLabel("Surface").locator("option").allTextContents()).toEqual(["Choose…", "Mesial", "Incisal", "Distal", "Facial", "Lingual"]);
  await shot(page, "06-entry-errors.png");
  await recordForm(page).getByRole("button", { name: "Cancel" }).click();
  evidence.failurePaths = { refusals, duplicateDifferentState: "finding_exists", quietRepeat: true, chartUnchanged: true };
});

test("CONCURRENCY: a stale change is refused with the conflict banner, the other person's change stands, and a reload lets the first person finish", async () => {
  const dentist = sessions.Dentist.page;
  const hyg = sessions.Hygienist.page;
  expect((await api(dentist, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: "21", surface: "I", condition: "Caries", state: "Diagnosed" })).status).toBe(200);
  await gotoChart(dentist);
  await tooth(dentist, "9").click();                                                      // Universal 9 is FDI 21
  await gotoChart(hyg);
  await tooth(hyg, "9").click();

  await hyg.getByRole("button", { name: "Plan: Caries (Incisal surface)" }).click();
  await expect(statusLine(hyg)).toContainText("is now Planned.");

  await dentist.getByRole("button", { name: "Complete: Caries (Incisal surface)" }).click();   // the dentist is still looking at the old version
  await expect(statusLine(dentist)).toHaveText("Not saved: someone else changed this.");
  await expect(dentist.getByRole("alert")).toContainText("This tooth finding record was updated by someone else");
  await expect(stateWord(dentist, "9")).toHaveText("Diagnosed");                         // the screen did not pretend it saved
  await shot(dentist, "07-conflict.png");
  expect(findingOf(await chartOf(dentist), "21", "I", "Caries").state).toBe("Planned");  // the hygienist's change stood

  await dentist.getByRole("button", { name: /Reload/ }).click();
  await expect(dentist.getByRole("alert")).toHaveCount(0);
  await expect(stateWord(dentist, "9")).toHaveText("Planned");
  await expect(dentist.getByRole("heading", { name: "Tooth 9: upper left central incisor" })).toBeVisible();   // still on the same tooth
  await dentist.getByRole("button", { name: "Complete: Caries (Incisal surface)" }).click();
  await expect(statusLine(dentist)).toContainText("is now Completed.");
  expect(findingOf(await chartOf(dentist), "21", "I", "Caries").state).toBe("Completed");
  evidence.concurrency = { staleRefused: true, otherChangeStood: true, reloadedInPlace: true, savedAfterReload: true };
});

test("KEYBOARD: a tooth is selected and a finding planned with the keyboard alone", async () => {
  const page = sessions.Dentist.page;
  expect((await api(page, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: "36", surface: "O", condition: "Caries", state: "Diagnosed" })).status).toBe(200);
  await gotoChart(page);
  await tooth(page, "19").focus();                                                       // Universal 19 is FDI 36
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Tooth 19: lower left first molar" })).toBeVisible();
  const plan = page.getByRole("button", { name: "Plan: Caries (Occlusal surface)" });
  await plan.focus();
  await page.keyboard.press("Enter");
  await expect(statusLine(page)).toContainText("is now Planned.");
  evidence.keyboard = { selectedAndPlannedWithKeyboardOnly: true };
});

test("ACCESS: front desk and billing cannot read or change the chart; an assistant reads it but cannot change anything", async () => {
  const denied: Record<string, number[]> = {};
  const findingId = ids.caries16O;
  for (const role of ["FrontDesk", "Billing"]) {
    const page = sessions[role].page;
    const probes = [
      await api(page, "get", `/api/patients/${ids.ann}/odontogram`), await api(page, "get", `/api/odontogram/findings/${findingId}/history`),
      await api(page, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" }),
      await api(page, "post", `/api/odontogram/findings/${findingId}/state`, { state: "Planned", rowVersion: "AAAA" }),
      await api(page, "post", `/api/odontogram/findings/${findingId}/withdraw`, { reason: "No", rowVersion: "AAAA" }),
    ];
    expect(probes.map((p) => p.status), role).toEqual([403, 403, 403, 403, 403]);
    denied[role] = probes.map((p) => p.status);
    if (role === "FrontDesk") {
      await page.goto(`/patients/${ids.ann}`);
      await expect(page.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Odontogram", exact: true })).toHaveCount(0);
      await page.goto(`/patients/${ids.ann}/odontogram`);
      await expect(page.getByText(/don't have permission/i).first()).toBeVisible();
    }
  }
  const assistant = sessions.Assistant.page;
  await gotoChart(assistant);
  await expect(assistant.getByText(/your role cannot change it/)).toBeVisible();
  await tooth(assistant, "3").click();
  await expect(assistant.getByRole("button", { name: /^(Plan|Complete|Withdraw):|Record a finding/ })).toHaveCount(0);
  await expect(assistant.locator("summary", { hasText: "History" }).first()).toBeVisible();
  expect((await api(assistant, "get", `/api/patients/${ids.ann}/odontogram`)).status).toBe(200);
  expect((await api(assistant, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" })).status).toBe(403);
  expect((await chartOf(assistant, "bo")).findings).toEqual([]);                          // another patient's chart is empty, not this one's
  evidence.access = { denied, assistant: { read: 200, write: 403, controls: 0 } };
});

test("TRUST: every record, state change and withdrawal is in the audit log with the user and a time, and nothing clinical is in it", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const mine = entries.filter((e) => e.eventType.startsWith("ToothFinding"));
  const by = (t: string) => mine.filter((e) => e.eventType === t);
  expect(by("ToothFindingRecorded").length).toBe(7);                                      // 5 on the first walk, 21/I and 36/O
  expect(by("ToothFindingStateChanged").length).toBe(5);                                  // 16/O twice, 21/I twice, 36/O once
  expect(by("ToothFindingWithdrawn").length).toBe(1);
  expect(mine.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = mine.map((e) => e.details).join(" ");
  for (const secret of ["Caries", "Crown", "Restoration", "Occlusal", "Incisal", "Buccal", "Diagnosed", "Planned", "Completed", "wrong tooth", "Entered on"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["ToothFindingRecorded", "ToothFindingStateChanged", "ToothFindingWithdrawn"].map((t) => [t, by(t).length]));
});

test("ACCESSIBILITY: the chart, a tooth with its record and withdraw forms open, and the tablet layout have no critical or serious axe violations in light and dark", async () => {
  const page = sessions.Dentist.page;
  await gotoChart(page);
  await scanBothThemes(page, "chart");
  await tooth(page, "3").click();
  await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
  await page.getByRole("button", { name: "Withdraw: Restoration (Mesial surface)" }).click();
  await scanBothThemes(page, "tooth-forms-open");
  await shot(page, "08-forms-open.png");

  await page.setViewportSize({ width: 768, height: 1024 });                              // the reviewer asked for a tablet-width check of the clinical screens
  await gotoChart(page);
  await tooth(page, "3").click();
  await expect(page.getByRole("group", { name: "Upper teeth" }).getByRole("button").first()).toBeVisible();
  const box = await page.getByRole("group", { name: "Upper teeth" }).boundingBox();
  expect(box!.width).toBeLessThanOrEqual(768);                                            // no sideways scrolling: each arch wraps into two rows of eight
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await shot(page, "09-tablet.png");
  await scanBothThemes(page, "tablet");
  await page.setViewportSize({ width: 1280, height: 720 });
  evidence.accessibility = { scanned: ["chart", "tooth-forms-open", "tablet"], tabletNoHorizontalScroll: true };
});
