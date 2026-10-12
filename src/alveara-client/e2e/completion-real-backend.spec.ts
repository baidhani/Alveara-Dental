import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-008-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.completion.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// A dentist reviews what completing a plan's procedures will do (the chart, the charge, a no-fee procedure, a procedure that is not ready), completes them as one set with a provider and an encounter, and sees
// the result in the clinical history, the ledger and the chart. When the log is unavailable nothing is completed and the same click works once it is back; a second screen that is out of date is told and
// completes nothing; an ambiguous chart match refuses its procedure only because the person unticks it; a correction keeps the original; the legacy button is labelled and records none of it.

test.describe.configure({ mode: "serial" });

const OUT = process.env.COMPLETION_E2E_OUT ?? path.join(process.cwd(), "completion-e2e-out");
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
const NAMES = { providerA: "Dr. Dana Dentist", providerB: "Dr. Sam Second" };

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log demonstration needs it)");
  execFileSync("sqlcmd", ["-S", "(localdb)\\MSSQLLocalDB", "-d", db, "-I", "-b", "-Q", statement], { stdio: "pipe" });
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
const reviewForm = (page: Page) => page.getByRole("form", { name: "Review and complete procedures" });
const reviewRow = (page: Page, code: string) => reviewForm(page).getByRole("row").filter({ hasText: code });
const history = (page: Page) => page.getByRole("region", { name: "Clinical history" });
const tabUrl = "treatment-plan";
const plansOf = async (page: Page) => (await api(page, "get", `/api/patients/${ids.ann}/treatment-plans`)).body.plans as any[];
const planNamed = async (page: Page, title: string) => (await plansOf(page)).find((p) => p.title === title);
const completedOf = async (page: Page) => (await api(page, "get", `/api/patients/${ids.ann}/completed-procedures`)).body.procedures as any[];
const ledgerOf = async (page: Page) => {
  const r = await api(page, "get", `/api/patients/${ids.ann}/billing`);
  return r.status === 200 ? (r.body.entries as any[]) : [];
};
const findingsOf = async (page: Page) => (await api(page, "get", `/api/patients/${ids.ann}/odontogram`)).body.findings as any[];

async function gotoTab(page: Page) {
  await page.goto(`/patients/${ids.ann}/${tabUrl}`);
  await expect(page.getByRole("heading", { name: "Treatment plan", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Plans on record" })).toBeVisible();
}

async function openReview(page: Page, title: string) {
  await gotoTab(page);
  await card(page, title).getByRole("button", { name: "Review and complete…" }).click();
  await expect(reviewForm(page)).toBeVisible();
  await expect(reviewForm(page).getByRole("table", { name: "Procedures to complete" })).toBeVisible();
}

async function choose(page: Page, provider = ids.providerA) {
  await reviewForm(page).getByLabel("Provider").selectOption(provider);
  await reviewForm(page).getByLabel("Encounter").selectOption(ids.encounter);
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
    if (role === "Dentist") ids.dentistUser = userId;
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "completion-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: two providers, a patient with an encounter, a catalog, findings on the chart and five accepted plans", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  for (const [key, name, userAccountId] of [["providerA", `${NAMES.providerA} ${stamp}`, ids.dentistUser], ["providerB", `${NAMES.providerB} ${stamp}`, null]] as const) {
    const staff = await api(admin, "post", "/api/config/staff", { displayName: name, userAccountId });
    expect(staff.status, `staff ${key}`).toBe(201);
    const provider = await api(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" });
    expect([200, 201], `provider ${key}`).toContain(provider.status);
    ids[key] = provider.body.id;
  }
  const p = await api(front, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `c08-ann-${stamp}` });
  expect(p.status).toBe(201);
  ids.ann = p.body.id;
  const e = await api(dentist, "post", `/api/patients/${ids.ann}/encounters`, {}, { "Idempotency-Key": `c08-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  ids.encounter = e.body.id;

  for (const [code, description, scope, fee] of [["FILL-1", "Resin filling", "ToothSurface", 140], ["EXAM-1", "Periodic exam", "WholeMouth", 0], ["SCALE-1", "Scaling", "WholeMouth", 70], ["CROWN-1", "Crown", "Tooth", 100],
    ["SEAL-1", "Sealant", "ToothSurface", 40], ["SCALE-2", "Scaling, light", "WholeMouth", 30], ["PERIO-2", "Perio review", "WholeMouth", 50], ["PERIO-3", "Perio follow-up", "WholeMouth", 55]] as const) {
    const r = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code, description, category: scope === "WholeMouth" ? "Periodontic" : "Restorative", scope, dentition: "Both", fee });
    expect(r.status, code).toBe(200);
    procedures[code] = r.body.summary.id;
  }

  const diagnosis = async (key: string, label: string) => {
    const d = await api(dentist, "post", `/api/patients/${ids.ann}/diagnoses`, { idempotencyKey: `c08-dx-${key}-${stamp}`, encounterId: ids.encounter, label, toothKey: null, notes: null, treatmentPlanReference: null });
    expect(d.status).toBe(200);
    return d.body.id as string;
  };
  const finding = async (tooth: string, surface: string | null, condition: string) => {
    const r = await api(dentist, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: tooth, surface, condition, state: "Planned" });
    expect(r.status, `finding ${tooth}${surface ?? ""} ${condition}`).toBe(200);
    return (r.body.findings as any[]).find((f) => f.toothKey === tooth && f.surface === surface && f.condition === condition).id as string;
  };
  const link = async (dx: string, target: string) => expect((await api(dentist, "post", `/api/diagnoses/${dx}/links`, { linkType: "Finding", targetId: target })).status).toBe(200);
  const plan = async (title: string, dx: string, items: [string, string | null, string | null][]) => {
    const created = await api(dentist, "post", `/api/patients/${ids.ann}/treatment-plans`, {
      idempotencyKey: `c08-plan-${title}-${stamp}`, title, items: items.map(([code, tooth, surface]) => ({ diagnosisId: dx, procedureId: procedures[code], toothKey: tooth, surface })),
    });
    expect(created.status, title).toBe(200);
    expect((await api(dentist, "post", `/api/treatment-plans/${created.body.id}/decision`, { decision: "Accepted", rowVersion: created.body.rowVersion })).status).toBe(200);
  };

  const dxMain = await diagnosis("main", "Caries on 16");
  ids.fillingFinding = await finding("16", "O", "Caries");
  await link(dxMain, ids.fillingFinding);
  await plan("Restorative visit", dxMain, [["FILL-1", "16", "O"], ["EXAM-1", null, null], ["SCALE-1", null, null]]);

  const dxCrown = await diagnosis("crown", "Broken tooth 17");
  ids.crownA = await finding("17", null, "Crown");
  ids.crownB = await finding("17", null, "RootCanal");
  await link(dxCrown, ids.crownA);
  await link(dxCrown, ids.crownB);
  await plan("Crown plan", dxCrown, [["CROWN-1", "17", null], ["SEAL-1", "18", "O"]]);

  const dxOther = await diagnosis("other", "General care");
  await plan("Legacy plan", dxOther, [["SCALE-2", null, null]]);
  await plan("Stale plan", dxOther, [["PERIO-2", null, null]]);
  await plan("Amount plan", dxOther, [["PERIO-3", null, null]]);
});

test("REVIEW: the screen shows what completing will do for each procedure, labels the legacy button and offers the providers and the encounter", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page);
  const legacy = card(page, "Restorative visit").getByRole("group", { name: "Legacy completion" });
  await expect(legacy).toContainText("records no provider, encounter, chart change or charge");
  await expect(card(page, "Restorative visit").getByRole("button", { name: "Review and complete…" })).toHaveClass(/btn-primary/);

  await openReview(page, "Restorative visit");
  await expect(reviewRow(page, "FILL-1")).toContainText("Tooth 16, O");
  await expect(reviewRow(page, "FILL-1")).toContainText("The chart finding will be marked Completed.");
  await expect(reviewRow(page, "FILL-1")).toContainText("Charge $140.00");
  await expect(reviewRow(page, "EXAM-1")).toContainText("No charge (no fee)");
  await expect(reviewRow(page, "SCALE-1")).toContainText("Charge $70.00");
  await expect(reviewForm(page)).toContainText("3 procedures will be completed; 2 charges totalling $210.00 will be made; 1 has no fee; 1 chart finding will be marked Completed.");
  await expect(reviewForm(page).getByLabel("Provider").locator("option")).toHaveCount(3);                 // the placeholder and the two providers
  await shot(page, "01-review-what-will-happen.png");
  await scanBothThemes(page, "review");

  const readiness = await api(page, "get", `/api/treatment-plans/${(await planNamed(page, "Restorative visit")).id}/completion-readiness`);
  expect(readiness.body.items.every((i: any) => i.ready)).toBe(true);
  expect(await completedOf(page)).toHaveLength(0);                                                        // looking recorded nothing
});

test("FAILURE PATH: when the log is unavailable nothing is completed, no internals are shown, and the same click works once it is back", async () => {
  const page = sessions.Dentist.page;
  await choose(page);
  sql(`CREATE TRIGGER [TR_E2E_RefuseCompletionSet] ON [AuditLogEntries] INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'CompletionSetRecorded') THROW 59050, 'audit unavailable', 1; INSERT INTO [AuditLogEntries] SELECT * FROM inserted; END`);
  let dropped = false;
  try {
    await reviewForm(page).getByRole("button", { name: "Confirm completion of 3 procedures" }).click();
    await expect(reviewForm(page).getByRole("alert")).toContainText("Nothing was completed: The completion could not be saved, so nothing was completed.");
    await expect(reviewForm(page).getByRole("alert")).not.toContainText(/audit|sql/i);
    await expect(page.getByRole("region", { name: "Completion recorded" })).toHaveCount(0);
    await expect(reviewForm(page).getByLabel("Provider")).toHaveValue(ids.providerA);                       // what was entered is kept

    expect(await completedOf(page)).toHaveLength(0);
    expect(await ledgerOf(page)).toHaveLength(0);
    expect((await findingsOf(page)).find((f) => f.id === ids.fillingFinding).state).toBe("Planned");
    expect((await planNamed(page, "Restorative visit")).items.map((i: any) => i.completion)).toEqual(["NotStarted", "NotStarted", "NotStarted"]);
    await shot(page, "02-save-failed-nothing-completed.png");
    await scanBothThemes(page, "save-failed");

    sql("DROP TRIGGER [TR_E2E_RefuseCompletionSet]");
    dropped = true;
    await reviewForm(page).getByRole("button", { name: "Confirm completion of 3 procedures" }).click();
    await expect(page.getByRole("region", { name: "Completion recorded" })).toBeVisible();
  } finally {
    if (!dropped) sql("DROP TRIGGER [TR_E2E_RefuseCompletionSet]");
  }
  expect(await completedOf(page)).toHaveLength(3);                                                        // recorded once, not twice
});

test("RESULT: what was recorded, the charge, the chart and the history, with links to each", async () => {
  const page = sessions.Dentist.page;
  const done = page.getByRole("region", { name: "Completion recorded" });
  await expect(done).toContainText("3 procedures completed");
  await expect(done).toContainText("charged $140.00 (account entry 1)");
  await expect(done).toContainText("charged $70.00 (account entry 2)");
  await expect(done).toContainText("chart finding marked Completed");
  await expect(done).toContainText("no charge");
  await shot(page, "03-result-recorded.png");

  const entries = await ledgerOf(page);
  expect(entries.map((e) => [e.kind, e.amount])).toEqual([["Charge", 140], ["Charge", 70]]);
  expect((await findingsOf(page)).find((f) => f.id === ids.fillingFinding).state).toBe("Completed");

  const rows = await completedOf(page);
  expect(rows.map((r) => r.procedureCode).sort()).toEqual(["EXAM-1", "FILL-1", "SCALE-1"]);
  expect(new Set(rows.map((r) => r.providerName)).size).toBe(1);
  expect(rows.every((r) => r.encounterId === ids.encounter && r.planRevisionNumber >= 1)).toBe(true);

  await expect(history(page).getByRole("list", { name: "Completed procedures" }).getByRole("listitem")).toHaveCount(3);
  await expect(history(page)).toContainText(`Performed by ${NAMES.providerA} ${stamp}`);
  await expect(history(page)).toContainText("charged $140.00 (account entry 1)");
  await expect(history(page)).toContainText("chart finding completed");
  await expect(history(page)).toContainText("no charge");
  await scanBothThemes(page, "history");

  await done.getByRole("link", { name: "account ledger" }).click();
  await expect(page).toHaveURL(new RegExp(`/patients/${ids.ann}/billing$`));
  await expect(page.getByRole("table", { name: "Account history" }).getByRole("row")).toHaveCount(3);       // the heading row and the two charges
  await shot(page, "04-ledger-shows-the-charges.png");
  await gotoTab(page);
  await expect(card(page, "Restorative visit").getByRole("button", { name: "Review and complete…" })).toHaveCount(0);        // nothing left to complete there
});

test("a retry or a second submission completes nothing twice: the API replays the same set and refuses the same procedures under a new key", async () => {
  const page = sessions.Dentist.page;
  const plan = await planNamed(page, "Restorative visit");
  const body = (key: string, version: string) => ({ idempotencyKey: key, rowVersion: version, encounterId: ids.encounter, providerId: ids.providerA, performedAtUtc: new Date(Date.now() + 2000).toISOString(), itemIds: plan.items.map((i: any) => i.id) });
  const again = await api(page, "post", `/api/treatment-plans/${plan.id}/complete-procedures`, body("never-used", plan.rowVersion));
  expect([again.status, again.body.error]).toEqual([409, "items_not_completable"]);
  expect(again.body.items.map((i: any) => i.reason)).toEqual(["already_completed", "already_completed", "already_completed"]);
  expect(await completedOf(page)).toHaveLength(3);
  expect(await ledgerOf(page)).toHaveLength(2);
});

test("AMBIGUITY: a procedure with more than one matching chart finding cannot be ticked, the rest complete, and the chart is untouched", async () => {
  const page = sessions.Dentist.page;
  await openReview(page, "Crown plan");
  await expect(reviewRow(page, "CROWN-1")).toContainText("2 chart findings match; unselect this procedure or correct the diagnosis links.");
  await expect(reviewForm(page).getByRole("checkbox", { name: "Complete procedure 1 CROWN-1" })).toBeDisabled();
  await expect(reviewForm(page).getByRole("checkbox", { name: "Complete procedure 1 CROWN-1" })).not.toBeChecked();
  await expect(reviewRow(page, "SEAL-1")).toContainText("Charge $40.00");
  await expect(reviewForm(page)).toContainText("1 procedure will be completed; 1 charge totalling $40.00 will be made");
  await shot(page, "05-ambiguous-chart-match.png");
  await scanBothThemes(page, "ambiguity");

  await choose(page, ids.providerB);
  await reviewForm(page).getByRole("button", { name: "Confirm completion of 1 procedure" }).click();
  await expect(page.getByRole("region", { name: "Completion recorded" })).toContainText("1 procedure completed");
  const crown = await planNamed(page, "Crown plan");
  expect(crown.items.map((i: any) => i.completion)).toEqual(["NotStarted", "Completed"]);
  expect((await findingsOf(page)).filter((f) => f.toothKey === "17").map((f) => f.state)).toEqual(["Planned", "Planned"]);

  const forced = await api(page, "post", `/api/treatment-plans/${crown.id}/complete-procedures`, {
    idempotencyKey: "force-ambiguous", rowVersion: (await planNamed(page, "Crown plan")).rowVersion, encounterId: ids.encounter, providerId: ids.providerA,
    performedAtUtc: new Date(Date.now() + 2000).toISOString(), itemIds: [crown.items[0].id],
  });
  expect([forced.status, forced.body.error]).toEqual([409, "ambiguous_chart_finding"]);                    // the server refuses it too
  expect(forced.body.items[0]).toMatchObject({ itemNumber: 1, toothKey: "17", matches: 2 });
});

test("STALE SECOND SCREEN: an out-of-date review is told, completes nothing, and works after it is reloaded", async () => {
  const dentist = sessions.Dentist.page;
  await openReview(admin, "Stale plan");
  await choose(admin);
  const plan = await planNamed(dentist, "Stale plan");
  expect((await api(dentist, "post", `/api/treatment-plans/${plan.id}/rename`, { title: "Stale plan, renamed", rowVersion: plan.rowVersion })).status).toBe(200);   // someone else saves first

  await reviewForm(admin).getByRole("button", { name: "Confirm completion of 1 procedure" }).click();
  await expect(reviewForm(admin).getByRole("alert")).toContainText("Nothing was completed: this plan changed since you opened this screen.");
  expect((await completedOf(dentist)).some((r) => r.procedureCode === "PERIO-2")).toBe(false);
  await shot(admin, "06-stale-screen-conflict.png");

  await reviewForm(admin).getByRole("button", { name: "Reload the review" }).click();
  await choose(admin);
  await reviewForm(admin).getByRole("button", { name: "Confirm completion of 1 procedure" }).click();
  await expect(admin.getByRole("region", { name: "Completion recorded" })).toContainText("1 procedure completed");
  expect((await completedOf(dentist)).filter((r) => r.procedureCode === "PERIO-2")).toHaveLength(1);
});

test("CORRECTION: an amendment changes the current provider, keeps the original and is safe to repeat or race", async () => {
  const page = sessions.Dentist.page;
  const filling = (await completedOf(page)).find((r) => r.procedureCode === "FILL-1");
  const amend = (key: string, expected: number, extra: Record<string, unknown>) => api(page, "post", `/api/completed-procedures/${filling.id}/amend`, { idempotencyKey: key, expectedAmendmentNumber: expected, reason: "Recorded under the wrong provider", ...extra });

  const first = await amend("fix-1", 0, { providerId: ids.providerB });
  expect([first.status, first.body.amendmentNumber, first.body.replayed]).toEqual([200, 1, false]);
  expect(first.body.procedure.providerProfileId).toBe(ids.providerB);
  expect(first.body.procedure.originalProviderProfileId).toBe(ids.providerA);                              // the original is kept
  const replay = await amend("fix-1", 0, { providerId: ids.providerB });
  expect([replay.body.replayed, replay.body.amendmentNumber]).toEqual([true, 1]);
  const stale = await amend("fix-2", 0, { providerId: ids.providerA });
  expect(stale.status).toBe(409);                                                                          // made against an amendment that is no longer the latest
  const early = await amend("fix-3", 1, { performedAtUtc: "2000-01-02T00:00:00Z" });
  expect([early.status, early.body.error]).toEqual([400, "validation_failed"]);
  expect(early.body.fieldErrors.performedAtUtc).toContain("before this plan was created");
  expect((await api(page, "get", `/api/completed-procedures/${filling.id}`)).body.amendments).toHaveLength(1);
  expect((await ledgerOf(page)).map((e) => e.amount)).toEqual([140, 70, 40, 50]);                          // the charges are untouched by a correction (the filling, the scaling, the sealant and the review)

  await gotoTab(page);
  await expect(history(page)).toContainText(`Performed by ${NAMES.providerB} ${stamp}`);
  await shot(page, "07-correction-shown-in-history.png");
});

test("the charge is always the plan's stored fee: an amount in the request means nothing", async () => {
  const page = sessions.Dentist.page;
  const plan = await planNamed(page, "Amount plan");
  const done = await api(page, "post", `/api/treatment-plans/${plan.id}/complete-procedures`, {
    idempotencyKey: "amount-1", rowVersion: plan.rowVersion, encounterId: ids.encounter, providerId: ids.providerA, performedAtUtc: new Date(Date.now() + 2000).toISOString(),
    itemIds: [plan.items[0].id], amount: 1, charge: { amount: 1 },
  });
  expect(done.status).toBe(200);
  expect(done.body.procedures[0].charge).toMatchObject({ outcome: "created", amount: 55 });
});

test("LEGACY: the old button is still there and still records only a state: no provider, chart change or charge, and the history says so", async () => {
  const page = sessions.Dentist.page;
  const before = { completed: (await completedOf(page)).length, charges: (await ledgerOf(page)).length };
  await gotoTab(page);
  await card(page, "Legacy plan").getByRole("button", { name: "Complete accepted procedures" }).click();
  await page.getByRole("form", { name: "Complete the accepted procedures" }).getByRole("button", { name: "Confirm: complete 1" }).click();
  await expect(statusLine(page)).toContainText("1 accepted procedure completed and added to the clinical history.");

  expect((await planNamed(page, "Legacy plan")).items[0].completion).toBe("Completed");
  expect({ completed: (await completedOf(page)).length, charges: (await ledgerOf(page)).length }).toEqual(before);   // no record, no charge
  const entry = history(page).getByRole("listitem").filter({ hasText: "SCALE-2" });
  await expect(entry).toContainText("Completed by");
  await expect(entry).not.toContainText("Performed by");
  const apiEntry = ((await api(page, "get", `/api/patients/${ids.ann}/clinical-history`)).body.entries as any[]).find((e) => e.procedureCode === "SCALE-2");
  expect([apiEntry.completedProcedureId, apiEntry.providerName, apiEntry.chargeEntryNumber]).toEqual([null, null, null]);
  await shot(page, "08-legacy-completion-leaves-the-detail-empty.png");
});

test("a hygienist reads the history and the completed procedures and has no way to complete or correct; front desk reads neither", async () => {
  const hyg = sessions.Hygienist.page;
  await gotoTab(hyg);
  await expect(history(hyg)).toContainText("Performed by");
  await expect(hyg.getByRole("button", { name: /Review and complete|Complete accepted procedures/ })).toHaveCount(0);
  const plan = await planNamed(hyg, "Stale plan, renamed");
  const forbidden = await api(hyg, "post", `/api/treatment-plans/${plan.id}/complete-procedures`, { idempotencyKey: "h", rowVersion: plan.rowVersion, encounterId: ids.encounter, providerId: ids.providerA, performedAtUtc: new Date().toISOString(), itemIds: [plan.items[0].id] });
  expect(forbidden.status).toBe(403);
  expect((await api(hyg, "get", `/api/treatment-plans/${plan.id}/completion-readiness`)).status).toBe(403);
  const any = (await completedOf(hyg))[0];
  expect((await api(hyg, "post", `/api/completed-procedures/${any.id}/amend`, { idempotencyKey: "h2", expectedAmendmentNumber: 0, providerId: ids.providerA, reason: "x" })).status).toBe(403);

  const front = sessions.FrontDesk.page;
  expect((await api(front, "get", `/api/patients/${ids.ann}/completed-procedures`)).status).toBe(403);
  expect((await api(front, "get", `/api/patients/${ids.ann}/clinical-history`)).status).toBe(403);
  const noToken = await api(sessions.Dentist.page, "post", `/api/completed-procedures/${any.id}/amend`, { idempotencyKey: "n", expectedAmendmentNumber: 0, providerId: ids.providerA, reason: "x" }, {}, false);
  expect(noToken.status).toBe(400);                                                                         // a write without a CSRF token
});
