import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N022 A3 (CR-2), un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, on a fresh database.
// Run with playwright.a3-display.config.ts against an API the caller started (docs/testing/REAL_BACKEND_E2E.md). Three workflows:
// (1) F-106: the Prescriptions tab shows a workflow prescription's status, instructions, quantity and refills, and its superseded version marked as replaced;
// (2) F-091: the odontogram shows a Diagnosed missing finding with its state and NO absent mark, and an Existing one with the mark;
// (3) D-1 B: the perio linked-record picker offers only THIS patient's diagnoses, and an API attempt with another patient's diagnosis is refused.

test.describe.configure({ mode: "serial" });

const OUT = process.env.A3_DISPLAY_E2E_OUT ?? path.join(process.cwd(), "a3-display-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "a3-display-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};

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

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "FrontDesk"]) {
    const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    if (role === "Dentist") expect((await api(admin, "post", "/api/config/staff", { displayName: `Dr. Dana Dentist ${stamp}`, userAccountId: userId })).status).toBe(201);
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  for (const [key, first, last, phone] of [["ann", "Ann", "Alder", "555-010-0100"], ["bo", "Bo", "Birch", "555-010-0177"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: "1985-03-09", sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `a3d-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "a3-display-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("F-106 PRESCRIPTIONS TAB: a workflow prescription shows its status, instructions, quantity and refills; the version it replaced is marked superseded", async () => {
  const page = sessions.Dentist.page;
  const fields = { medication: "Amoxicillin", doseValue: 500, doseUnit: "mg", frequency: "Three times daily", instructions: "Take one with food until finished.", quantityValue: 21, quantityUnit: "capsule", refills: 0,
    reviewNote: "Asked the patient today: no allergies." };
  const draft = await api(page, "post", `/api/patients/${ids.ann}/prescription-drafts`, { idempotencyKey: `a3d-rx-${stamp}`, ...fields });
  expect(draft.status, JSON.stringify(draft.body)).toBe(200);
  const first = await api(page, "post", `/api/prescription-drafts/${draft.body.draft.id}/finalize`, { rowVersion: draft.body.draft.rowVersion });
  expect(first.status, JSON.stringify(first.body)).toBe(200);
  const amend = await api(page, "post", `/api/prescriptions/${first.body.record.id}/amend`, { idempotencyKey: `a3d-amend-${stamp}` });
  expect(amend.status, JSON.stringify(amend.body)).toBe(200);
  const changed = await api(page, "put", `/api/prescription-drafts/${amend.body.draft.id}`, { ...fields, doseValue: 250, instructions: "Take one with food for seven days.", rowVersion: amend.body.draft.rowVersion });
  expect(changed.status, JSON.stringify(changed.body)).toBe(200);
  const second = await api(page, "post", `/api/prescription-drafts/${amend.body.draft.id}/finalize`, { rowVersion: (changed.body.draft ?? changed.body).rowVersion });
  expect(second.status, JSON.stringify(second.body)).toBe(200);

  await page.goto(`/patients/${ids.ann}/prescriptions`);
  const list = page.getByRole("list", { name: "Prescriptions" });
  const active = list.locator('li[data-status="Active"]');
  const superseded = list.locator('li[data-status="Superseded"]');
  await expect(active).toHaveCount(1);
  await expect(superseded).toHaveCount(1);
  await expect(active).toContainText("250 mg");
  await expect(active).toContainText("Take one with food for seven days.");
  await expect(active).toContainText(/Quantity 21 capsule/);
  await expect(active).toContainText("Refills 0");
  await expect(active).toContainText("This is an amendment; it replaces an earlier version (listed below).");
  await expect(superseded).toContainText("500 mg");
  await expect(superseded).toContainText(/Status:\s*Superseded/);
  await expect(superseded).toContainText("Replaced by a later amendment (listed above).");
  await shot(page, "01-prescriptions-tab.png");
  evidence.prescriptions = { active: 1, superseded: 1, marked: true };
});

test("F-091 ABSENT MARK: a Diagnosed missing tooth shows its state and no mark; an Existing missing tooth shows the mark", async () => {
  const page = sessions.Dentist.page;
  const tooth = (label: string) => page.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });
  const record = async (label: string, state: string) => {
    await tooth(label).click();
    await page.getByRole("button", { name: "Record a finding on this tooth" }).click();
    const form = page.getByRole("form", { name: "Record a finding" });
    await form.getByLabel("Condition").selectOption("Missing");
    await form.getByLabel("State").selectOption(state);
    await form.getByRole("button", { name: "Record finding" }).click();
    await expect(page.locator("p.alv-clinical__status")).toContainText(`recorded on tooth ${label} as ${state}.`);
  };
  await page.goto(`/patients/${ids.ann}/odontogram`);
  await expect(page.getByRole("heading", { name: "Odontogram" })).toBeVisible();

  await record("19", "Diagnosed");
  await expect(tooth("19").locator(".alv-odonto__tooth-state")).toHaveText("Diagnosed");
  await expect(tooth("19").locator("[data-absent-mark]")).toHaveCount(0);
  await expect(tooth("19")).not.toHaveAccessibleName(/not present/);

  await record("30", "Existing");
  await expect(tooth("30").locator("[data-absent-mark]")).toHaveCount(1);
  await expect(tooth("30")).toHaveAccessibleName(/, not present$/);
  await expect(tooth("30")).toHaveClass(/alv-odonto__tooth--absent/);
  await shot(page, "02-absent-mark.png");
  evidence.absentMark = { diagnosedMarked: false, existingMarked: true };
});

test("D-1 B PERIO LINK: the picker offers only this patient's diagnoses; another patient's diagnosis is refused by the API", async () => {
  const page = sessions.Dentist.page;
  const diagnosisFor = async (patient: string, label: string) => {
    const enc = await api(page, "post", `/api/patients/${ids[patient]}/encounters`, {}, { "Idempotency-Key": `a3d-enc-${patient}-${stamp}` });
    expect([200, 201]).toContain(enc.status);
    const dx = await api(page, "post", `/api/patients/${ids[patient]}/diagnoses`, { idempotencyKey: `a3d-dx-${patient}-${stamp}`, encounterId: enc.body.id, label, toothKey: null, notes: null, treatmentPlanReference: null });
    expect([200, 201], JSON.stringify(dx.body)).toContain(dx.status);
    return (dx.body.diagnosis ?? dx.body).id as string;
  };
  const annDx = await diagnosisFor("ann", "Generalized periodontitis");
  const boDx = await diagnosisFor("bo", "Localized gingivitis");

  const s = (await api(page, "post", `/api/patients/${ids.ann}/periodontal/sessions`)).body;
  const saved = await api(page, "post", `/api/periodontal/sessions/${s.id}/entries`, { rowVersion: s.rowVersion, readings: [{ toothKey: "16", site: "B", probingDepthMm: 5, recessionMm: 0, bleeding: true, suppuration: null, plaque: null }] });
  expect(saved.status, JSON.stringify(saved.body)).toBe(200);
  const done = await api(page, "post", `/api/periodontal/sessions/${s.id}/finalize`, { rowVersion: saved.body.rowVersion });
  expect(done.status, JSON.stringify(done.body)).toBe(200);

  const foreign = await api(page, "post", `/api/periodontal/charts/${done.body.id}/links`, { linkType: "Diagnosis", diagnosisId: boDx });
  expect(foreign.status, JSON.stringify(foreign.body)).toBe(400);                                       // another patient's diagnosis
  const typed = await api(page, "post", `/api/periodontal/charts/${done.body.id}/links`, { linkType: "Diagnosis", reference: "dx-typed" });
  expect(typed.status).toBe(400);                                                                       // free text

  await page.goto(`/patients/${ids.ann}/periodontal`);
  const chart = page.getByRole("region", { name: "Charts on record" }).getByRole("group").first();
  const form = chart.getByRole("form", { name: "Link this chart" });
  await form.getByLabel("Link to").selectOption("Diagnosis");
  const picker = form.getByLabel("Diagnosis", { exact: true });
  await expect(picker.locator("option", { hasText: "Generalized periodontitis" })).toHaveCount(1);
  await expect(picker.locator("option", { hasText: "Localized gingivitis" })).toHaveCount(0);
  await picker.selectOption(annDx);
  await form.getByRole("button", { name: "Add link" }).click();
  await expect(chart.getByRole("list", { name: "Linked records" })).toContainText("Diagnosis: Generalized periodontitis");
  await shot(page, "03-perio-diagnosis-link.png");
  evidence.perioLink = { otherPatientRefused: foreign.status, freeTextRefused: typed.status, offeredOnlyOwn: true };
});
