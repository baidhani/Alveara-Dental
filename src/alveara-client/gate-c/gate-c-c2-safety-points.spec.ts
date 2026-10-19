import { test, expect } from "@playwright/test";
import type { Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// Gate C criterion C2 (late evaluation): "Manual walkthrough with flagged patient ... alert visible at each gated point".
// Gate tooling, not a product test: real Chromium -> real Vite proxy -> real Alveara.Api -> real SQL Server (run with gate-c/playwright.c2.config.ts against an API the caller started).
// A synthetic patient is flagged (a Critical alert, a severe allergy, a current medication and an open clearance); then, as a dentist, every screen where treatment is
// PLANNED, COMPLETED and PRESCRIBED is opened and the patient safety strip must be on screen, above the work area, carrying the alert (with its highest severity), the allergy, the medication and the open clearance, with a link to the details.

test.describe.configure({ mode: "serial" });
const OUT = process.env.GATE_C_C2_OUT ?? path.join(process.cwd(), "gate-c-c2-out");
mkdirSync(OUT, { recursive: true });
const PASSWORD = "gate-c-pass-1!";
const evidence: Record<string, unknown> = { points: {} };
const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, extra: Record<string, string> = {}) {
  const headers = method === "get" ? extra : { "X-CSRF-Token": await csrf(page), ...extra };
  const r = await page.request[method](url, method === "get" ? { headers } : { headers, data });
  return { status: r.status(), body: (await r.json().catch(() => ({}))) as any };
}
async function signIn(page: Page, username: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

let patientId = "";
let dentist: Page;

test.beforeAll(async ({ browser }) => {
  const stamp = Date.now();
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await admin.goto("/login");
  await admin.getByLabel("Username").fill(`admin-${stamp}`);
  await admin.getByLabel("Password").fill("admin-password-1!");
  await admin.getByRole("button", { name: "Sign in" }).click();
  await expect(admin.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  for (const role of ["Dentist", "FrontDesk"]) {
    const reg = await admin.request.post("/api/auth/register", { data: { username: `${role.toLowerCase()}-${stamp}`, password: PASSWORD } });
    const { id } = await reg.json();
    const token = await csrf(admin);
    await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } });
    await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } });
  }
  const dentistContext = await browser.newContext();
  dentist = await dentistContext.newPage();
  await signIn(dentist, `dentist-${stamp}`);
  const front = await (await browser.newContext()).newPage();
  await signIn(front, `frontdesk-${stamp}`);
  const created = await api(front, "post", "/api/patients", { firstName: "Gate", lastName: "Flagged", dateOfBirth: "1980-02-03", sex: "Female", phone: "555-0199-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `gate-c-patient-${stamp}` });
  expect(created.status).toBe(201);
  patientId = created.body.id;
  expect((await api(dentist, "post", `/api/patients/${patientId}/clinical-record/items`, { kind: "Allergy", name: "Penicillin", reaction: "Hives", severity: "Severe" })).status).toBe(200);
  expect((await api(dentist, "post", `/api/patients/${patientId}/clinical-record/items`, { kind: "Medication", name: "Warfarin", dose: "5 mg", frequency: "daily" })).status).toBe(200);
  expect((await api(dentist, "post", `/api/patients/${patientId}/safety/alerts`, { category: "Anticoagulant", title: "Anticoagulant therapy", severity: "Critical", sourceNote: "Cardiology letter, synthetic" })).status).toBe(200);
  expect((await api(dentist, "post", `/api/patients/${patientId}/safety/clearances`, { kind: "Medical", reason: "Cardiology clearance before surgery", requestedFrom: "Dr. Heart" })).status).toBe(200);
});

test.afterAll(() => writeFileSync(path.join(OUT, "c2-safety-points.json"), JSON.stringify(evidence, null, 2)));

const POINTS: Array<[string, string, string]> = [
  ["planning", "treatment-plan", "Treatment plan"],
  ["completion (the treatment-plan screen is where the completion review is opened)", "treatment-plan", "Treatment plan"],
  ["prescribing, composer", "prescription-composer", ""],
  ["prescribing, prescriptions list", "prescriptions", ""],
  ["charting: odontogram", "odontogram", ""],
  ["diagnoses", "diagnoses", ""],
];

for (const [label, route, heading] of POINTS) {
  test(`C2 ${label}: the safety strip carries the alert, allergy, medication and open clearance above the work area`, async () => {
    await dentist.goto(`/patients/${patientId}/${route}`);
    const strip = dentist.getByRole("region", { name: "Patient safety", exact: true });
    await expect(strip).toBeVisible();
    await expect(strip.getByText(/1 active alert \(highest: Critical\)/)).toBeVisible();
    await expect(strip.getByText(/1 active allergy/)).toBeVisible();
    await expect(strip.getByText(/1 current medication/)).toBeVisible();
    await expect(strip.getByText(/1 clearance open/)).toBeVisible();
    await expect(strip.getByRole("link", { name: "Open safety details" })).toBeVisible();
    const stripBox = (await strip.boundingBox())!;
    const main = (await dentist.getByRole("main").boundingBox())!;
    expect(stripBox.y, "the strip sits above the main work area").toBeLessThanOrEqual(main.y);
    if (heading) await expect(dentist.getByRole("heading", { name: heading, exact: true })).toBeVisible();
    await dentist.screenshot({ path: path.join(OUT, `c2-${route}.png`), fullPage: false });
    (evidence.points as Record<string, unknown>)[label] = { route, stripTop: stripBox.y, mainTop: main.y, visible: true };
  });
}
