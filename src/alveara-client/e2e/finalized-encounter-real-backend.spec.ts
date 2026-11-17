import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N022 A2 Stage 1, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server (run with playwright.finalized-encounter.config.ts against an API
// the caller started; see docs/testing/REAL_BACKEND_E2E.md). OD-06 as amended 2026-11-17: a finalized encounter accepts no new DIAGNOSIS (the diagnosis form offers only open
// encounters and says why; the API refuses), while a completed procedure may still be ATTRIBUTED to a finalized encounter (ALV-008-C01 D6), which never changes that encounter.

test.describe.configure({ mode: "serial" });

const OUT = process.env.FINALIZED_ENCOUNTER_E2E_OUT ?? path.join(process.cwd(), "finalized-encounter-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "finalized-pass-1!";
const contexts: BrowserContext[] = [];
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const pages: Record<string, Page> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
async function api(page: Page, method: "post" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

async function session(browser: Browser, role: string): Promise<{ page: Page; userId: string }> {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username: `${role.toLowerCase()}-${stamp}`, password: PASSWORD } });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  const context = await browser.newContext();
  contexts.push(context);
  const page = await context.newPage();
  await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
  return { page, userId: id };
}

/** What must not change on the finalized encounter: status, row version, finalize stamp and its history length. */
const encounterState = async (page: Page, id: string) => {
  const e = (await api(page, "get", `/api/encounters/${id}`)).body;
  return { status: e.status, rowVersion: e.rowVersion, finalizedAtUtc: e.finalizedAtUtc, history: e.history.length };
};

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  const adminContext = await browser.newContext();
  contexts.push(adminContext);
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  const desk = await session(browser, "FrontDesk");
  const dentist = await session(browser, "Dentist");
  pages.dentist = dentist.page;

  // a provider for the dentist, a patient, a catalog procedure
  const staff = await api(admin, "post", "/api/config/staff", { displayName: `Dr. Final ${stamp}`, userAccountId: dentist.userId });
  expect(staff.status).toBe(201);
  const provider = await api(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" });
  expect([200, 201]).toContain(provider.status);
  ids.provider = provider.body.id;
  const p = await api(desk.page, "post", "/api/patients", { firstName: "Fay", lastName: `Final${stamp}`, dateOfBirth: "1980-05-05", sex: "Female", phone: "555-040-0400", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `fe-pt-${stamp}` });
  expect(p.status).toBe(201);
  ids.patient = p.body.id;
  const proc = await api(admin, "post", "/api/procedures", { codeSystem: "Local", code: `EXAM-F${stamp % 100000}`, description: "Periodic exam", category: "Periodontic", scope: "WholeMouth", dentition: "Both", fee: 0 });
  expect(proc.status).toBe(200);

  // encounter 1: documented and FINALIZED; encounter 2: DRAFT
  const first = await api(dentist.page, "post", `/api/patients/${ids.patient}/encounters`, {}, { "Idempotency-Key": `fe-enc1-${stamp}` });
  ids.finalized = first.body.id;
  let version = first.body.rowVersion as string;
  for (const kind of ["MedicalHistory", "DentalHistory", "Allergy", "Medication"]) {
    const marked = await api(dentist.page, "post", `/api/encounters/${ids.finalized}/sections/${kind}/none-reported`, { rowVersion: version });
    expect(marked.status, kind).toBe(200);
    version = marked.body.rowVersion;
  }
  const fin = await api(dentist.page, "post", `/api/encounters/${ids.finalized}/finalize`, { rowVersion: version });
  expect([fin.status, fin.body.status]).toEqual([200, "Finalized"]);
  const second = await api(dentist.page, "post", `/api/patients/${ids.patient}/encounters`, {}, { "Idempotency-Key": `fe-enc2-${stamp}` });
  ids.draft = second.body.id;

  // a diagnosis on the draft encounter and an accepted plan with one procedure to complete
  const dx = await api(dentist.page, "post", `/api/patients/${ids.patient}/diagnoses`, { idempotencyKey: `fe-dx-${stamp}`, encounterId: ids.draft, label: "Gingivitis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect(dx.status).toBe(200);
  const plan = await api(dentist.page, "post", `/api/patients/${ids.patient}/treatment-plans`, { idempotencyKey: `fe-plan-${stamp}`, title: "Exam plan", items: [{ diagnosisId: dx.body.id, procedureId: proc.body.summary.id, toothKey: null, surface: null }] });
  expect(plan.status).toBe(200);
  expect((await api(dentist.page, "post", `/api/treatment-plans/${plan.body.id}/decision`, { decision: "Accepted", rowVersion: plan.body.rowVersion })).status).toBe(200);
  ids.plan = plan.body.id;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "finalized-encounter-e2e.json"), JSON.stringify(evidence, null, 2));
  for (const c of contexts) await c.close();
});

test("A finalized encounter receives no new diagnosis, but a completed procedure may still be attributed to it without changing it", async () => {
  const page = pages.dentist;
  const before = await encounterState(page, ids.finalized);
  expect(before.status).toBe("Finalized");

  // 1. the diagnosis form offers only the draft encounter and says why
  await page.goto(`/patients/${ids.patient}/diagnoses`);
  await expect(page.getByRole("heading", { name: "Diagnoses", exact: true })).toBeVisible();
  const choice = page.getByRole("form", { name: "Record a diagnosis" }).getByLabel("Encounter");
  const offered = await choice.locator("option").evaluateAll((os) => os.map((o) => (o as HTMLOptionElement).value).filter(Boolean));
  expect(offered).toEqual([ids.draft]);
  await expect(page.getByText("Finalized encounters cannot receive new diagnoses. Start a new encounter.")).toBeVisible();
  await shot(page, "01-diagnosis-picker.png");

  // ... and the API refuses a diagnosis on the finalized encounter, writing nothing
  const countBefore = ((await api(page, "get", `/api/patients/${ids.patient}/diagnoses`)).body.diagnoses as any[]).length;
  const refused = await api(page, "post", `/api/patients/${ids.patient}/diagnoses`, { idempotencyKey: `fe-dx-fin-${stamp}`, encounterId: ids.finalized, label: "Late diagnosis", toothKey: null, notes: null, treatmentPlanReference: null });
  expect([refused.status, refused.body.error]).toEqual([409, "encounter_finalized"]);
  expect(((await api(page, "get", `/api/patients/${ids.patient}/diagnoses`)).body.diagnoses as any[]).length).toBe(countBefore);

  // 2. the completion picker still offers both encounters with their status, and completing against the finalized one succeeds
  await page.goto(`/patients/${ids.patient}/treatment-plan`);
  await expect(page.getByRole("heading", { name: "Plans on record" })).toBeVisible();
  await page.getByRole("button", { name: "Review and complete…" }).click();
  const review = page.getByRole("form", { name: "Review and complete procedures" });
  await expect(review).toBeVisible();
  const encounterSelect = review.getByLabel("Encounter");
  const texts = await encounterSelect.locator("option").evaluateAll((os) => os.filter((o) => (o as HTMLOptionElement).value).map((o) => [(o as HTMLOptionElement).value, o.textContent ?? ""]));
  expect(texts.map(([v]) => v).sort()).toEqual([ids.draft, ids.finalized].sort());
  expect(texts.find(([v]) => v === ids.finalized)![1]).toMatch(/Finalized/);
  expect(texts.find(([v]) => v === ids.draft)![1]).toMatch(/Draft/);
  await review.getByLabel("Provider").selectOption(ids.provider);
  await encounterSelect.selectOption(ids.finalized);
  await shot(page, "02-completion-picker.png");
  await review.getByRole("button", { name: "Confirm completion of 1 procedure" }).click();
  await expect(page.getByRole("region", { name: "Completion recorded" })).toContainText("1 procedure completed");

  const done = ((await api(page, "get", `/api/patients/${ids.patient}/completed-procedures`)).body.procedures as any[]);
  expect(done.map((r) => r.encounterId)).toEqual([ids.finalized]);
  const after = await encounterState(page, ids.finalized);
  expect(after).toEqual(before);                                                     // status, row version, finalize stamp and history unchanged
  evidence.result = { diagnosisPickerOffered: "draft only", diagnosisOnFinalized: "409 encounter_finalized", completionPickerOffered: "draft and finalized", completionAttributedToFinalized: true, finalizedEncounterUnchanged: true };
});
