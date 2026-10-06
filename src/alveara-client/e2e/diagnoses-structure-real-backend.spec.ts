import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-013-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.diagnoses-structure.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the companion's acceptance items as a clinician would, against REAL encounters, REAL odontogram findings and REAL periodontal charts: a diagnosis is stored with no coding at all, and with
// optional coding, source and region that survive a round trip; an amendment keeps what it replaced, who changed it, when and why, and carries the treatment-plan forward reference through unresolved;
// resolve and reactivate are recorded and reversible; links go only to the patient's own findings and charts; the treatment-plan reference can never be marked resolved; and every failure path (a wrong
// coding, an invalid link, a stale amendment, a withdrawn diagnosis, roles, CSRF) is refused with nothing changed. STORY-013's own walkthrough is unchanged and runs separately.

test.describe.configure({ mode: "serial" });

const OUT = process.env.DIAGNOSES_STRUCTURE_E2E_OUT ?? path.join(process.cwd(), "diagnoses-structure-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "diag-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const encounters: Record<string, string> = {};
const findings: Record<string, string> = {};
const charts: Record<string, string> = {};
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
    (evidence.axe as Record<string, unknown>)[`${label}-${theme}`] = { blocking: blocking.length, violations: results.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length })) };
    expect(blocking.map((v) => `${v.id}:${v.nodes.map((n) => `${n.target.join(" ")} ${(n.any[0]?.message ?? n.failureSummary ?? "").slice(0, 160)}`).join(" | ")}`), `${label} (${theme}): critical/serious axe violations`).toEqual([]);
  }
  await setTheme(page, "light");
}

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

const D = (patient: string) => `/api/patients/${ids[patient]}/diagnoses`;
const listOf = async (page: Page, patient: string, withdrawn = true) => (await api(page, "get", `${D(patient)}${withdrawn ? "?includeWithdrawn=true" : ""}`)).body.diagnoses as any[];
const byLabel = async (page: Page, patient: string, label: string) => (await listOf(page, patient)).find((d) => d.label === label);
const item = (page: Page, label: string) => page.getByRole("listitem", { name: `Diagnosis: ${label}` });
const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const recordForm = (page: Page) => page.getByRole("form", { name: "Record a diagnosis" });
const record = (page: Page, patient: string, key: string, over: Record<string, unknown> = {}) =>
  api(page, "post", D(patient), { idempotencyKey: key, encounterId: encounters[patient], label: "Chronic periodontitis", ...over });

async function gotoTab(page: Page, patient: string) {
  await page.goto(`/patients/${ids[patient]}/diagnoses`);
  await expect(page.getByRole("heading", { name: "Diagnoses", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Diagnoses on record" })).toBeVisible();
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
  writeFileSync(path.join(OUT, "diagnoses-structure-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: two patients, real encounters, a real odontogram finding and a real periodontal chart for each", async () => {
  const page = sessions.Dentist.page;
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `dxs-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
    const e = await api(page, "post", `/api/patients/${ids[key]}/encounters`, {}, { "Idempotency-Key": `dxs-${key}-${stamp}` });
    expect([200, 201]).toContain(e.status);
    encounters[key] = e.body.id;
  }
  for (const [key, tooth] of [["ann", "16"], ["bo", "26"]] as const) {
    const f = await api(page, "post", `/api/patients/${ids[key]}/odontogram/findings`, { toothKey: tooth, surface: null, condition: "Crown", state: "Existing" });
    expect(f.status, `finding for ${key}`).toBe(200);
    findings[key] = f.body.findings.find((x: any) => x.toothKey === tooth).id;
    const c = await api(page, "post", `/api/patients/${ids[key]}/periodontal/charts`, { idempotencyKey: `chart-${key}-${stamp}`, readings: [{ toothKey: "11", site: "B", probingDepthMm: 3, recessionMm: 0, bleeding: false }] });
    expect(c.status, `chart for ${key}`).toBe(200);
    charts[key] = c.body.id;
  }
});

test("NO CODING NEEDED: a diagnosis with no coding system, code, source or region is stored structurally and reads as manual and uncoded", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const f = recordForm(page);
  await f.getByLabel("Diagnosis").fill("Gingivitis");
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis recorded.");
  const d = await byLabel(page, "ann", "Gingivitis");
  expect([d.codingSystem, d.code, d.source, d.sourceNote, d.regionKey, d.status]).toEqual([null, null, "Manual", null, null, "Active"]);
  await expect(item(page, "Gingivitis").getByRole("list", { name: "Linked records" })).toHaveCount(0);
  await shot(page, "01-no-coding.png");
  evidence.noCoding = { stored: true, source: d.source };
});

test("CODING AND PROVENANCE ROUND TRIP: coding, source and region typed on screen are stored, read back, listed and in the history, shown as chips and never as checked", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const f = recordForm(page);
  await f.getByLabel("Diagnosis").fill("Generalized periodontitis");
  await f.getByLabel("Oral region (optional)").selectOption("UpperArch");
  await f.getByLabel("Coding system").selectOption("ICD-10-CM");
  await f.getByLabel("Code", { exact: true }).fill("K05.311");
  await f.getByLabel("Where it came from").selectOption("Imported");
  await f.getByLabel("Source note (optional)").fill("From the previous practice system");
  await shot(page, "02-structured-form.png");
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis recorded.");

  const chips = item(page, "Generalized periodontitis").getByRole("list", { name: "Context of this diagnosis" });
  await expect(chips).toContainText("Region: Upper arch");
  await expect(chips).toContainText("ICD-10-CM K05.311");
  await expect(chips).toContainText("Imported: From the previous practice system");
  await expect(item(page, "Generalized periodontitis")).not.toContainText(/validated|verified/i);
  await shot(page, "03-structured-recorded.png");

  const d = await byLabel(page, "ann", "Generalized periodontitis");
  expect([d.codingSystem, d.code, d.source, d.sourceNote, d.regionKey, d.toothKey]).toEqual(["ICD-10-CM", "K05.311", "Imported", "From the previous practice system", "UpperArch", null]);
  const history = (await api(page, "get", `/api/diagnoses/${d.id}/history`)).body.versions;
  expect([history[0].codingSystem, history[0].code, history[0].source, history[0].regionKey]).toEqual(["ICD-10-CM", "K05.311", "Imported", "UpperArch"]);
  evidence.roundTrip = { coding: "ICD-10-CM K05.311", source: "Imported", region: "UpperArch" };
});

test("INCORRECT STRUCTURE: the screen lists every problem linked to its box and sends nothing; the server refuses the same mistakes for any caller, naming each one", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const before = (await listOf(page, "ann")).length;
  const f = recordForm(page);
  await f.getByLabel("Diagnosis").fill("Perio");
  await f.getByLabel("Coding system").selectOption("Local");
  await f.getByLabel("Code", { exact: true }).fill("A 1");
  let sent = 0;
  page.on("request", (r) => { if (r.method() === "POST" && r.url().includes("/diagnoses")) sent++; });
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  const alert = page.getByRole("alert").filter({ hasText: "Some entries need correcting" });
  await expect(alert).toContainText("A code can contain only letters, digits, dots, hyphens and underscores");
  await expect(alert).toBeFocused();
  await alert.getByRole("button", { name: "Code" }).click();
  await expect(f.getByLabel("Code", { exact: true })).toBeFocused();
  await shot(page, "04-structure-problems.png");
  expect(sent).toBe(0);
  expect((await listOf(page, "ann")).length).toBe(before);

  const bad = await record(page, "ann", "bad-1", { toothKey: "16", codingSystem: "SNOMED", code: null, source: "Wrong", sourceNote: "x", regionKey: "Nowhere", treatmentPlanReferenceState: "Resolved" });
  expect([bad.status, bad.body.error]).toEqual([400, "validation_failed"]);
  const problems = bad.body.problems.map((p: any) => `${p.field}:${p.code}`);
  for (const expected of ["codingSystem:unsupported_system", "code:coding_incomplete", "source:unsupported_source", "regionKey:unknown_region", "treatmentPlanReferenceState:not_supported"]) expect(problems).toContain(expected);
  const both = await record(page, "ann", "bad-2", { toothKey: "16", regionKey: "UpperArch" });
  expect(both.body.problems.map((p: any) => p.code)).toContain("conflicts_with_tooth");
  expect((await listOf(page, "ann")).length).toBe(before);
  evidence.incorrectStructure = { sentWhileWrong: sent, listedProblems: problems };
});

test("AMENDMENT: it asks why, keeps what it replaced with who, when and why, carries the treatment plan reference through unresolved, and an amendment that changes nothing is quiet", async () => {
  const page = sessions.Dentist.page;
  const created = await record(page, "ann", "amend-1", { label: "Chronic periodontitis", toothKey: "16", treatmentPlanReference: "plan-2026-07" });
  expect(created.status).toBe(200);
  await gotoTab(page, "ann");
  const li = item(page, "Chronic periodontitis");
  await li.getByRole("button", { name: "Amend" }).click();
  const amend = page.getByRole("form", { name: "Amend this diagnosis" });
  await expect(amend.getByLabel("Tooth (optional)")).toHaveValue("16");
  await amend.getByLabel("Oral region (optional)").selectOption("UpperRight");
  await expect(amend.getByLabel("Tooth (optional)")).toHaveValue("");
  await amend.getByLabel("Coding system").selectOption("Local");
  await amend.getByLabel("Code", { exact: true }).fill("P-9");
  await amend.getByRole("button", { name: "Save amendment" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Some entries need correcting" })).toContainText("Say why this diagnosis is being amended");
  await shot(page, "05-amend-needs-reason.png");
  await amend.getByLabel("Why is this being amended?").fill("Moved to the quadrant and coded locally");
  await amend.getByRole("button", { name: "Save amendment" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis amended.");
  await expect(li).toContainText("Region: Upper right quadrant");
  await expect(li).toContainText("Local P-9");
  await expect(li).toContainText("Treatment plan reference (unresolved): plan-2026-07");

  await li.getByRole("button", { name: "History" }).click();
  const rows = li.getByRole("table").getByRole("row");
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(1)).toContainText("Recorded");
  await expect(rows.nth(1)).toContainText("Chronic periodontitis (tooth 16 FDI)");                 // what the amendment replaced is still there
  await expect(rows.nth(2)).toContainText("Amended (structure)");
  await expect(rows.nth(2)).toContainText("Moved to the quadrant and coded locally");
  await expect(rows.nth(2)).toContainText(NAMES.Dentist);
  await expect(rows.nth(2)).toContainText("region Upper right quadrant; Local P-9");
  await expect(rows.nth(1)).toContainText("plan-2026-07 (unresolved)");
  await expect(rows.nth(2)).toContainText("plan-2026-07 (unresolved)");
  await shot(page, "06-amendment-history.png");

  const d = await byLabel(page, "ann", "Chronic periodontitis");
  const quiet = await api(page, "post", `/api/diagnoses/${d.id}/amend`, { rowVersion: d.rowVersion, toothKey: null, regionKey: "UpperRight", codingSystem: "Local", code: "P-9", source: null, sourceNote: null, reason: "Nothing changes" });
  expect([quiet.status, quiet.body.rowVersion]).toEqual([200, d.rowVersion]);
  const h = (await api(page, "get", `/api/diagnoses/${d.id}/history`)).body.versions;
  expect(h.map((v: any) => v.changeType)).toEqual(["Recorded", "Amended"]);
  expect(h.every((v: any) => v.treatmentPlanReference === "plan-2026-07" && v.treatmentPlanReferenceState === "Unresolved")).toBe(true);
  evidence.amendment = { versions: h.map((v: any) => v.changeType), referenceThroughout: "plan-2026-07 Unresolved" };
});

test("LIFECYCLE: a diagnosis is marked resolved with a reason, stays on the list as Resolved, is made active again with a reason, and every step is in the history", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const li = item(page, "Gingivitis");
  await li.getByRole("button", { name: "Mark resolved" }).click();
  const form = page.getByRole("form", { name: "Mark this diagnosis resolved" });
  await expect(form.getByRole("button", { name: "Mark resolved" })).toBeDisabled();
  await form.getByLabel("Why is this diagnosis resolved?").fill("Healed at the six-week review");
  await form.getByRole("button", { name: "Mark resolved" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis marked resolved.");
  await expect(li).toContainText("Resolved");
  await shot(page, "07-resolved.png");
  expect((await api(page, "get", D("ann"))).body.diagnoses.map((d: any) => d.label)).toContain("Gingivitis");     // still on the default list
  await li.getByRole("button", { name: "Make active again" }).click();
  await page.getByRole("form", { name: "Make this diagnosis active again" }).getByLabel("Why is this diagnosis active again?").fill("Returned at the next visit");
  await page.getByRole("form", { name: "Make this diagnosis active again" }).getByRole("button", { name: "Make active again" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis made active again.");
  const d = await byLabel(page, "ann", "Gingivitis");
  const h = (await api(page, "get", `/api/diagnoses/${d.id}/history`)).body.versions;
  expect(h.map((v: any) => [v.changeType, v.status, v.reason])).toEqual([["Recorded", "Active", null], ["Resolved", "Resolved", "Healed at the six-week review"], ["Reactivated", "Active", "Returned at the next visit"]]);
  expect(d.status).toBe("Active");
  evidence.lifecycle = h.map((v: any) => v.changeType);
});

test("LINKS: only this patient's own findings and charts are offered; a link records who and when; the same link twice adds nothing; another patient's record and a missing one are refused in the same words", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const li = item(page, "Generalized periodontitis");
  await li.getByRole("button", { name: "Link" }).click();
  const form = page.getByRole("form", { name: "Link this diagnosis" });
  await expect(form.getByLabel("Link to")).toBeVisible();
  const options = await form.getByLabel("Link to").locator("option").allTextContents();
  expect(options).toHaveLength(3);                                                                  // the prompt, ann's finding and ann's chart
  expect(options.join("|")).toContain("Finding: Crown on tooth 16");
  expect(options.join("|")).not.toContain("tooth 26");                                              // bo's finding is not offered
  await shot(page, "08-link-picker.png");
  await form.getByLabel("Link to").selectOption({ label: options[1] });
  await form.getByRole("button", { name: "Link diagnosis" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis linked.");
  const links = li.getByRole("list", { name: "Linked records" });
  await expect(links).toContainText("Crown on tooth 16");
  await expect(links).toContainText(`linked by ${NAMES.Dentist}`);
  await shot(page, "09-linked.png");

  const d = await byLabel(page, "ann", "Generalized periodontitis");
  const chart = await api(page, "post", `/api/diagnoses/${d.id}/links`, { linkType: "PerioExam", targetId: charts.ann });
  expect(chart.body.links.map((l: any) => l.linkType)).toEqual(["Finding", "PerioExam"]);
  const again = await api(page, "post", `/api/diagnoses/${d.id}/links`, { linkType: "PerioExam", targetId: charts.ann });
  expect(again.body.links).toHaveLength(2);
  const refusals = [
    await api(page, "post", `/api/diagnoses/${d.id}/links`, { linkType: "Finding", targetId: findings.bo }),
    await api(page, "post", `/api/diagnoses/${d.id}/links`, { linkType: "PerioExam", targetId: charts.bo }),
    await api(page, "post", `/api/diagnoses/${d.id}/links`, { linkType: "Finding", targetId: "00000000-0000-4000-8000-000000000000" }),
  ];
  for (const r of refusals) expect([r.status, r.body.error, r.body.message]).toEqual([404, "link_target_not_found", refusals[0].body.message]);
  const type = await api(page, "post", `/api/diagnoses/${d.id}/links`, { linkType: "TreatmentPlan", targetId: findings.ann });
  expect([type.status, type.body.error]).toEqual([400, "validation_failed"]);
  expect((await byLabel(page, "ann", "Generalized periodontitis")).links).toHaveLength(2);
  evidence.links = { offered: options.length - 1, linked: 2, otherPatientRefusedAs: "link_target_not_found" };
});

test("TREATMENT PLAN REFERENCE: it can never be marked resolved or validated, on record, correct or amend, and stays unresolved throughout", async () => {
  const page = sessions.Dentist.page;
  const d = await byLabel(page, "ann", "Chronic periodontitis");
  const asks = [
    await record(page, "ann", "plan-state-1", { label: "Another", treatmentPlanReference: "plan-9", treatmentPlanReferenceState: "Resolved" }),
    await api(page, "post", `/api/diagnoses/${d.id}/correct`, { rowVersion: d.rowVersion, label: d.label, reason: "x", treatmentPlanReferenceState: "Validated" }),
    await api(page, "post", `/api/diagnoses/${d.id}/amend`, { rowVersion: d.rowVersion, codingSystem: "Local", code: "A1", reason: "x", treatmentPlanReferenceState: "Resolved" }),
  ];
  for (const r of asks) {
    expect([r.status, r.body.error]).toEqual([400, "validation_failed"]);
    expect(r.body.problems.map((p: any) => `${p.field}:${p.code}`)).toContain("treatmentPlanReferenceState:not_supported");
  }
  const after = await byLabel(page, "ann", "Chronic periodontitis");
  expect([after.rowVersion, after.treatmentPlanReference, after.treatmentPlanReferenceState]).toEqual([d.rowVersion, "plan-2026-07", "Unresolved"]);
  expect(await byLabel(page, "ann", "Another")).toBeUndefined();
  evidence.planReference = { refusedOn: ["record", "correct", "amend"], state: after.treatmentPlanReferenceState };
});

test("STALE AMENDMENT AND WITHDRAWN: an amendment made from a stale screen shows the conflict and keeps what was typed; a withdrawn diagnosis refuses every structural change", async () => {
  const page = sessions.Dentist.page;
  const made = await record(page, "bo", "stale-1", { label: "Recession" });
  await gotoTab(page, "bo");
  const li = item(page, "Recession");
  await li.getByRole("button", { name: "Amend" }).click();
  const amend = page.getByRole("form", { name: "Amend this diagnosis" });
  const other = await api(sessions.Hygienist.page, "post", `/api/diagnoses/${made.body.id}/amend`, { rowVersion: made.body.rowVersion, toothKey: "26", regionKey: null, codingSystem: null, code: null, source: null, sourceNote: null, reason: "Hygienist found the tooth" });
  expect(other.status).toBe(200);
  await amend.getByLabel("Oral region (optional)").selectOption("FullMouth");
  await amend.getByLabel("Why is this being amended?").fill("Wider than first thought");
  await amend.getByRole("button", { name: "Save amendment" }).click();
  await expect(page.getByText(/someone else changed this diagnosis/).first()).toBeVisible();
  await expect(amend.getByLabel("Why is this being amended?")).toHaveValue("Wider than first thought");
  await shot(page, "10-stale-amendment.png");
  const kept = await byLabel(page, "bo", "Recession");
  expect([kept.toothKey, kept.regionKey]).toEqual(["26", null]);                                    // the first change stands; the stale one changed nothing

  const w = await api(page, "post", `/api/diagnoses/${kept.id}/withdraw`, { rowVersion: kept.rowVersion, reason: "Entered on the wrong patient" });
  expect(w.status).toBe(200);
  for (const [action, body] of [
    ["amend", { rowVersion: w.body.rowVersion, codingSystem: "Local", code: "A1", reason: "x" }], ["resolve", { rowVersion: w.body.rowVersion, reason: "x" }],
    ["reactivate", { rowVersion: w.body.rowVersion, reason: "x" }], ["links", { linkType: "Finding", targetId: findings.bo }],
  ] as const) {
    const r = await api(page, "post", `/api/diagnoses/${kept.id}/${action}`, body);
    expect([r.status, r.body.error], action).toEqual([409, "diagnosis_withdrawn"]);
  }
  evidence.staleAndWithdrawn = { staleChangedNothing: true, withdrawnRefused: ["amend", "resolve", "reactivate", "links"] };
});

test("TRUST: every amendment, resolve, reactivate and link is in the audit log with the user and a time, and nothing diagnosed or coded is", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=1000")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const mine = entries.filter((e) => e.eventType.startsWith("Diagnosis"));
  const count = (t: string) => mine.filter((e) => e.eventType === t).length;
  for (const t of ["DiagnosisAmended", "DiagnosisResolved", "DiagnosisReactivated", "DiagnosisLinked"]) expect(count(t), t).toBeGreaterThan(0);
  expect(mine.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = JSON.stringify(mine.map((e) => e.details)).toLowerCase();
  for (const secret of ["k05", "p-9", "upperarch", "upperright", "previous practice", "crown", "plan-2026", "periodontitis", "gingivitis", "recession", "healed", "quadrant"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["DiagnosisAmended", "DiagnosisResolved", "DiagnosisReactivated", "DiagnosisLinked"].map((t) => [t, count(t)]));
});

test("ROLES: dentist and hygienist can amend, resolve and link; the assistant reads the chips, links and history but is refused and shown no way to change anything; front desk and billing are denied; anonymous callers get 401; a write without a CSRF token is refused", async () => {
  const d = await byLabel(sessions.Dentist.page, "ann", "Generalized periodontitis");
  const hygienist = await api(sessions.Hygienist.page, "post", `/api/diagnoses/${d.id}/amend`, { rowVersion: d.rowVersion, toothKey: null, regionKey: "UpperArch", codingSystem: "ICD-10-CM", code: "K05.31", source: "Imported", sourceNote: "From the previous practice system", reason: "Corrected the code" });
  expect(hygienist.status).toBe(200);
  const fresh = (await api(sessions.Dentist.page, "get", `/api/diagnoses/${d.id}`)).body;
  for (const role of ["Assistant", "FrontDesk", "Billing"]) {
    const page = sessions[role].page;
    for (const [action, body] of [["amend", { rowVersion: fresh.rowVersion, codingSystem: "Local", code: "Z9", reason: "x" }], ["resolve", { rowVersion: fresh.rowVersion, reason: "x" }], ["reactivate", { rowVersion: fresh.rowVersion, reason: "x" }], ["links", { linkType: "Finding", targetId: findings.ann }]] as const)
      expect((await api(page, "post", `/api/diagnoses/${d.id}/${action}`, body)).status, `${role} ${action}`).toBe(403);
  }
  const assistant = sessions.Assistant.page;
  await gotoTab(assistant, "ann");
  const li = item(assistant, "Generalized periodontitis");
  await expect(li).toContainText("ICD-10-CM K05.31");
  await expect(li.getByRole("list", { name: "Linked records" })).toContainText("Crown on tooth 16");
  for (const name of ["Correct", "Amend", "Mark resolved", "Make active again", "Link", "Withdraw"]) await expect(li.getByRole("button", { name })).toHaveCount(0);
  await expect(recordForm(assistant)).toHaveCount(0);
  await li.getByRole("button", { name: "History" }).click();
  await expect(li.getByRole("table")).toContainText("Amended (structure)");
  await shot(assistant, "11-assistant-read-only.png");
  for (const role of ["FrontDesk", "Billing"]) {
    await sessions[role].page.goto(`/patients/${ids.ann}/diagnoses`);
    await expect(sessions[role].page.getByRole("heading", { name: "Diagnoses", exact: true })).toHaveCount(0);
  }
  const anonymous = await (await admin.context().browser()!.newContext()).request.post(`/api/diagnoses/${d.id}/amend`, { data: {} });
  expect(anonymous.status()).toBe(401);
  const noCsrf = await sessions.Dentist.page.request.post(`/api/diagnoses/${d.id}/resolve`, { data: { rowVersion: fresh.rowVersion, reason: "x" } });
  expect(noCsrf.ok()).toBeFalsy();
  expect((await api(sessions.Dentist.page, "get", `/api/diagnoses/${d.id}`)).body.status).toBe("Active");
  evidence.roles = { hygienistAmended: true, assistantRefused: true, frontDeskDenied: true, billingDenied: true, anonymous: 401, noCsrfRefused: true };
});

test("ACCESSIBILITY AND TABLET: axe finds no critical or serious issue with chips and links shown and with the amend, resolve and link forms open, in both themes; at 768 px the page does not scroll sideways", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  await scanBothThemes(page, "list-with-chips");
  const li = item(page, "Generalized periodontitis");
  await li.getByRole("button", { name: "Amend", exact: true }).click();
  await expect(page.getByRole("form", { name: "Amend this diagnosis" })).toBeVisible();
  await scanBothThemes(page, "amend-form");
  await li.getByRole("button", { name: "Amend", exact: true }).click();
  await li.getByRole("button", { name: "Mark resolved", exact: true }).first().click();
  await expect(page.getByRole("form", { name: "Mark this diagnosis resolved" })).toBeVisible();
  await scanBothThemes(page, "resolve-form");
  await li.getByRole("button", { name: "Mark resolved", exact: true }).first().click();
  await li.getByRole("button", { name: "Link", exact: true }).click();
  await expect(page.getByRole("form", { name: "Link this diagnosis" })).toBeVisible();
  await scanBothThemes(page, "link-form");
  await li.getByRole("button", { name: "Link", exact: true }).click();
  await li.getByRole("button", { name: "History" }).click();
  await scanBothThemes(page, "history");
  await page.setViewportSize({ width: 768, height: 1024 });
  await expect(page.getByRole("heading", { name: "Diagnoses on record" })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  await shot(page, "12-tablet.png");
  await scanBothThemes(page, "tablet");
  await page.setViewportSize({ width: 1280, height: 800 });
  evidence.accessibility = { scanned: ["list-with-chips", "amend-form", "resolve-form", "link-form", "history", "tablet"], tabletNoHorizontalPageScroll: true };
});
