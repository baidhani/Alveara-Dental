import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { seedLegacyCompletion } from "./support/legacyCompletion";

// ALV-008-C02 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.completion-adoption.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// The old completion routes are retired (410) and no screen offers them. Procedures completed the old way (seeded straight into the database, support/legacyCompletion.ts) are adopted through the screen: the
// provider, encounter, time and reason are recorded, the chart follows and exactly one charge is made. When the log is unavailable nothing is recorded and the same click works once it is back; a second
// screen that is out of date is told and records nothing; a read-only role cannot adopt; and the reconciliation report is all zero at the end.

test.describe.configure({ mode: "serial" });

const OUT = process.env.COMPLETION_ADOPTION_E2E_OUT ?? path.join(process.cwd(), "completion-adoption-e2e-out");
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
  writeFileSync(path.join(OUT, "completion-adoption-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

const adoptButton = (page: Page) => page.getByRole("button", { name: "Adopt earlier completions…" });
const adoptForm = (page: Page) => page.getByRole("form", { name: "Adopt an earlier completion" });
const planCard = () => "Restorative history";
const reconciliation = async (page: Page) => (await api(page, "get", "/api/completion/reconciliation")).body as Record<string, number | boolean | string>;
const counts = async (page: Page) => ({ completed: (await completedOf(page)).length, charges: (await ledgerOf(page)).length, plan: (await planNamed(page, planCard())).rowVersion as string });

async function openAdopt(page: Page) {
  await gotoTab(page);
  await card(page, planCard()).getByRole("button", { name: "Adopt earlier completions…" }).click();
  await expect(adoptForm(page)).toBeVisible();
  await expect(adoptForm(page).getByLabel("Procedure")).toBeVisible();
}

async function fillAdopt(page: Page, reason: string) {
  await adoptForm(page).getByLabel("Provider").selectOption(ids.providerA);
  await adoptForm(page).getByLabel("Encounter").selectOption(ids.encounter);
  await adoptForm(page).getByLabel("Why is this being recorded now?").fill(reason);
}

test("SETUP: a provider, a patient with an encounter, a chart finding, and one plan whose three procedures were completed the old way", async () => {
  const front = sessions.FrontDesk.page;
  const dentist = sessions.Dentist.page;
  const staff = await api(admin, "post", "/api/config/staff", { displayName: `${NAMES.providerA} ${stamp}`, userAccountId: ids.dentistUser });
  expect(staff.status).toBe(201);
  const provider = await api(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" });
  expect([200, 201]).toContain(provider.status);
  ids.providerA = provider.body.id;
  const p = await api(front, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `c08c02-ann-${stamp}` });
  expect(p.status).toBe(201);
  ids.ann = p.body.id;
  const e = await api(dentist, "post", `/api/patients/${ids.ann}/encounters`, {}, { "Idempotency-Key": `c08c02-enc-${stamp}` });
  expect([200, 201]).toContain(e.status);
  ids.encounter = e.body.id;
  for (const [code, description, scope, fee] of [["FILL-1", "Resin filling", "ToothSurface", 140], ["SCALE-1", "Scaling", "WholeMouth", 70], ["PERIO-1", "Perio review", "WholeMouth", 50]] as const) {
    const r = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code, description, category: scope === "WholeMouth" ? "Periodontic" : "Restorative", scope, dentition: "Both", fee });
    expect(r.status, code).toBe(200);
    procedures[code] = r.body.summary.id;
  }
  const dx = await api(dentist, "post", `/api/patients/${ids.ann}/diagnoses`, { idempotencyKey: `c08c02-dx-${stamp}`, encounterId: ids.encounter, label: "Caries on 16", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(dx.status).toBe(200);
  const finding = await api(dentist, "post", `/api/patients/${ids.ann}/odontogram/findings`, { toothKey: "16", surface: "O", condition: "Caries", state: "Planned" });
  expect(finding.status).toBe(200);
  ids.finding = (finding.body.findings as any[]).find((f) => f.toothKey === "16" && f.surface === "O").id;
  expect((await api(dentist, "post", `/api/diagnoses/${dx.body.id}/links`, { linkType: "Finding", targetId: ids.finding })).status).toBe(200);
  const created = await api(dentist, "post", `/api/patients/${ids.ann}/treatment-plans`, {
    idempotencyKey: `c08c02-plan-${stamp}`, title: planCard(), items: [["FILL-1", "16", "O"], ["SCALE-1", null, null], ["PERIO-1", null, null]].map(([code, tooth, surface]) => ({ diagnosisId: dx.body.id, procedureId: procedures[code as string], toothKey: tooth, surface })),
  });
  expect(created.status).toBe(200);
  expect((await api(dentist, "post", `/api/treatment-plans/${created.body.id}/decision`, { decision: "Accepted", rowVersion: created.body.rowVersion })).status).toBe(200);
  const plan = await planNamed(dentist, planCard());
  for (const item of plan.items) seedLegacyCompletion(plan.id, item.id);                                    // completed the old way: the routes are retired, so it is seeded (support/legacyCompletion.ts)
  const now = await planNamed(dentist, planCard());
  expect(now.items.map((i: any) => i.completion)).toEqual(["Completed", "Completed", "Completed"]);
  evidence.setup = { items: now.items.length, completed: (await completedOf(dentist)).length, charges: (await ledgerOf(dentist)).length };
});

test("RETIRED: no screen offers the old completion, and the old routes answer 410 and change nothing", async () => {
  const page = sessions.Dentist.page;
  const before = await counts(page);
  await gotoTab(page);
  await expect(page.getByRole("button", { name: /Complete accepted procedures|^Mark .* completed$/ })).toHaveCount(0);
  await expect(page.getByRole("group", { name: "Legacy completion" })).toHaveCount(0);
  await expect(card(page, planCard()).getByRole("button", { name: "Review and complete…" })).toHaveCount(0);   // nothing is left to complete: they are all completed
  await expect(card(page, planCard()).getByRole("button", { name: "Adopt earlier completions…" })).toBeVisible();
  const plan = await planNamed(page, planCard());
  const all = await api(page, "post", `/api/treatment-plans/${plan.id}/complete-accepted`, { rowVersion: plan.rowVersion });
  const one = await api(page, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[0].id}/complete`, {});
  expect([all.status, all.body.error, one.status, one.body.error]).toEqual([410, "legacy_completion_retired", 410, "legacy_completion_retired"]);
  expect(await counts(page)).toEqual(before);
  const readiness = (await api(page, "get", `/api/treatment-plans/${plan.id}/completion-readiness`)).body.items as any[];
  expect(readiness.map((r) => r.legacyCompleted)).toEqual([true, true, true]);
  const report = await reconciliation(admin);
  expect([report.legacyCompletedNotAdopted, report.completedMissingCharge, report.orphanedCharges, report.chartMismatch]).toEqual([3, 0, 0, 0]);
  evidence.retired = { complete_accepted: all.body.error, complete_item: one.body.error, reconciliationBefore: report };
  await shot(page, "01-legacy-completions-waiting.png");
});

test("ADOPT: the missing fields are named, then the adoption records the provider, the chart effect and the one charge", async () => {
  const page = sessions.Dentist.page;
  const before = await counts(page);
  await openAdopt(page);
  await adoptForm(page).getByLabel("Procedure").selectOption({ label: "FILL-1 · Resin filling · tooth 16 · surface O" });
  await expect(adoptForm(page)).toContainText("Adopting FILL-1: the linked chart finding will be marked Completed; a charge of $140.00 will be made.");
  await adoptForm(page).getByRole("button", { name: "Adopt FILL-1" }).click();
  await expect(adoptForm(page)).toContainText("Choose the provider who performed the care.");
  await expect(adoptForm(page)).toContainText("Say why this is being recorded now.");
  expect(await counts(page)).toEqual(before);                                                               // nothing was sent
  await scanBothThemes(page, "adopt-form");
  await shot(page, "02-adopt-form-effects-and-gaps.png");

  await fillAdopt(page, "Completed at the visit before procedure records existed");
  await adoptForm(page).getByRole("button", { name: "Adopt FILL-1" }).click();
  await expect(statusLine(page)).toContainText("FILL-1 adopted: its provider, encounter and time are recorded");
  const after = await counts(page);
  expect([after.completed, after.charges]).toEqual([before.completed + 1, before.charges + 1]);
  const record = (await completedOf(page)).find((c: any) => c.procedureCode === "FILL-1");
  expect([record.adopted, record.adoptionReason, record.chartOutcome, record.charge.amount]).toEqual([true, "Completed at the visit before procedure records existed", "Updated", 140]);
  expect((await findingsOf(page)).find((f: any) => f.id === ids.finding).state).toBe("Completed");        // the chart followed
  const entry = history(page).getByRole("listitem").filter({ hasText: "FILL-1" });
  await expect(entry).toContainText(`Performed by ${NAMES.providerA} ${stamp}`);
  const report = await reconciliation(admin);
  expect([report.legacyCompletedNotAdopted, report.chartMismatch, report.completedMissingCharge]).toEqual([2, 0, 0]);
  evidence.adopt = { record: { adopted: record.adopted, chartOutcome: record.chartOutcome, charge: record.charge.amount }, reconciliation: report };
  await shot(page, "03-adopted-in-history.png");
});

test("FAILURE: when the log is unavailable nothing is recorded, and the same click works once it is back", async () => {
  const page = sessions.Dentist.page;
  const before = await counts(page);
  sql(`CREATE TRIGGER [TR_Test_RefuseAdoptionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
  IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'CompletionAdopted') THROW 59040, 'audit unavailable', 1;
  INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END`);
  try {
    await openAdopt(page);
    await adoptForm(page).getByLabel("Procedure").selectOption({ label: "SCALE-1 · Scaling" });
    await fillAdopt(page, "Completed at an earlier visit");
    await adoptForm(page).getByRole("button", { name: "Adopt SCALE-1" }).click();
    await expect(adoptForm(page).getByRole("alert")).toContainText("Nothing was recorded");
    await expect(adoptForm(page).getByRole("alert")).not.toContainText("audit");
    expect(await counts(page)).toEqual(before);                                                             // no record, no charge, same plan version
    await shot(page, "04-failed-adoption-records-nothing.png");
  } finally {
    sql("DROP TRIGGER [TR_Test_RefuseAdoptionAudit]");
  }
  await adoptForm(page).getByRole("button", { name: "Adopt SCALE-1" }).click();                              // the same form, the same key
  await expect(statusLine(page)).toContainText("SCALE-1 adopted");
  const after = await counts(page);
  expect([after.completed, after.charges]).toEqual([before.completed + 1, before.charges + 1]);
  evidence.failure = { recordedAfterRetry: after.completed - before.completed, chargesAfterRetry: after.charges - before.charges };
});

test("STALE: a second screen that is out of date is told, records nothing, and works after a reload", async () => {
  const page = sessions.Dentist.page;
  const second = await sessions.Dentist.context.newPage();
  try {
    await openAdopt(page);
    await openAdopt(second);
    await fillAdopt(second, "Completed at an earlier visit");
    const plan = await planNamed(page, planCard());
    const renamed = await api(page, "post", `/api/treatment-plans/${plan.id}/rename`, { title: `${planCard()} (reviewed)`, rowVersion: plan.rowVersion });      // someone else saves the plan first
    expect(renamed.status).toBe(200);
    const before = { completed: (await completedOf(page)).length, charges: (await ledgerOf(page)).length };
    await adoptForm(second).getByRole("button", { name: "Adopt PERIO-1" }).click();
    await expect(second.getByText(/Nothing was recorded: this plan changed since you opened this screen/)).toBeVisible();
    expect({ completed: (await completedOf(page)).length, charges: (await ledgerOf(page)).length }).toEqual(before);
    evidence.stale = { renameStatus: renamed.status };
    await shot(second, "05-stale-second-screen.png");
  } finally {
    await second.close();
  }
  await gotoTab(page);
  await card(page, planCard()).getByRole("button", { name: "Adopt earlier completions…" }).click();
  await fillAdopt(page, "Completed at an earlier visit");
  await adoptForm(page).getByRole("button", { name: "Adopt PERIO-1" }).click();
  await expect(statusLine(page)).toContainText("PERIO-1 adopted");
});

test("PARENT (STORY-008 criteria 2 and 3): revising afterwards keeps the original revision and the completed care, and the plan's log shows every change once, adoption adding none", async () => {
  const page = sessions.Dentist.page;
  const plan = (await plansOf(page)).find((p: any) => p.title.startsWith(planCard()));
  const revised = await api(page, "post", `/api/treatment-plans/${plan.id}/revise`, {
    reason: "Phased after treatment", clinicalRationale: "Healing first", patientExplanation: "The next visit follows.", phases: [{ number: 1, name: "Healing", plannedVisit: null, targetDate: null }], placements: null, rowVersion: plan.rowVersion,
  });
  expect(revised.status).toBe(200);
  await gotoTab(page);
  const revisions = (await api(page, "get", `/api/treatment-plans/${plan.id}/revisions`)).body as any[];
  expect(revisions.map((r) => r.revisionNumber)).toEqual([1, 2, 3]);                                                  // 2 is the rename made by the STALE step, 3 is this revision
  expect((await api(page, "get", `/api/treatment-plans/${plan.id}/revisions/1`)).body.items).toHaveLength(3);          // the original revision still holds all three procedures
  await expect(history(page)).toContainText("revision 1");
  expect(((await api(page, "get", `/api/patients/${ids.ann}/clinical-history`)).body.entries as any[])).toHaveLength(3);
  const log = (await api(page, "get", `/api/treatment-plans/${plan.id}/history`)).body as any[];
  expect(log.filter((e) => e.changeType === "ItemCompleted")).toHaveLength(3);                                         // the three seeded completions: adopting them added no completion event
  expect([log.filter((e) => e.changeType === "Renamed").length, log.filter((e) => e.changeType === "Revised").length]).toEqual([1, 1]);
  expect(log.every((e) => e.actorName && new Date(e.occurredAtUtc).getTime() > Date.now() - 60 * 60_000)).toBe(true);
  expect(new Set(log.map((e) => e.eventNumber)).size).toBe(log.length);
  evidence.planLogChangeTypes = log.map((e) => e.changeType);
  await shot(page, "05b-revised-original-kept.png");
});

test("READ-ONLY: a hygienist sees no way to adopt and is refused by the server; front desk is refused; only the audit-log roles read the reconciliation", async () => {
  const hyg = sessions.Hygienist.page;
  await gotoTab(hyg);
  await expect(hyg.getByRole("button", { name: /Adopt earlier completions|Review and complete/ })).toHaveCount(0);
  const plan = (await plansOf(hyg)).find((p: any) => p.title.startsWith(planCard()));
  const body = { idempotencyKey: "ro-1", rowVersion: plan.rowVersion, encounterId: ids.encounter, providerId: ids.providerA, performedAtUtc: new Date().toISOString(), reason: "x" };
  expect((await api(hyg, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[0].id}/adopt-completion`, body)).status).toBe(403);
  expect((await api(sessions.FrontDesk.page, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[0].id}/adopt-completion`, body)).status).toBe(403);
  expect((await api(sessions.Dentist.page, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[0].id}/adopt-completion`, body, {}, false)).status).toBe(400);   // no CSRF token
  expect((await api(sessions.Dentist.page, "get", "/api/completion/reconciliation")).status).toBe(403);
  expect((await api(admin, "get", "/api/completion/reconciliation")).status).toBe(200);
  const again = await api(sessions.Dentist.page, "post", `/api/treatment-plans/${plan.id}/items/${plan.items[0].id}/adopt-completion`, { ...body, idempotencyKey: "ro-2" });
  expect([again.status, again.body.error]).toEqual([409, "not_a_legacy_completion"]);                      // already adopted
});

test("GATE D: after the walk every legacy completion is adopted, every charge has a completion, and the chart agrees", async () => {
  const page = sessions.Dentist.page;
  const report = await reconciliation(admin);
  expect(report).toMatchObject({ legacyCompletedNotAdopted: 0, completedMissingCharge: 0, completedWithUnexpectedCharge: 0, orphanedCharges: 0, chartMismatch: 0, duplicateCharges: 0, clean: true });
  const ledger = await ledgerOf(page);
  expect(ledger.filter((e: any) => e.kind === "Charge").map((e: any) => e.amount).sort((a: number, b: number) => a - b)).toEqual([50, 70, 140]);
  expect((await completedOf(page)).every((c: any) => c.adopted)).toBe(true);
  expect((await findingsOf(page)).find((f: any) => f.id === ids.finding).state).toBe("Completed");
  evidence.gateD = report;
  await gotoTab(page);
  await shot(page, "06-gate-d-reconciled.png");
});
