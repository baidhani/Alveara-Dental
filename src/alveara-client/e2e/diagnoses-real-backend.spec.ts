import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-013 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.diagnoses.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a clinician would, against REAL encounters started through the clinical documentation API: a diagnosis recorded for an encounter is linked to the patient and
// to the encounter and carries an optional treatment-plan reference that is shown as unresolved and never looked up; incorrect data is refused with every problem named and nothing saved; and every
// entry is in the audit log with the user and a time (and nothing diagnosed is). Then each failure path (a wrong encounter, a stale correction, a dropped connection, a retry under one key), the
// reference kept through corrections and withdrawal, every role, axe in both themes and the tablet width.

test.describe.configure({ mode: "serial" });

const OUT = process.env.DIAGNOSES_E2E_OUT ?? path.join(process.cwd(), "diagnoses-e2e-out");
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

const D = (patient: string) => `/api/patients/${ids[patient]}/diagnoses`;
const listOf = async (page: Page, patient: string, withdrawn = false) => (await api(page, "get", `${D(patient)}${withdrawn ? "?includeWithdrawn=true" : ""}`)).body.diagnoses as any[];
const entry = (patient: string, key: string, over: Record<string, unknown> = {}) => ({ idempotencyKey: key, encounterId: encounters[patient], label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, ...over });
const item = (page: Page, label: string) => page.getByRole("listitem", { name: `Diagnosis: ${label}` });
const statusLine = (page: Page) => page.locator("p.alv-clinical__status");
const recordForm = (page: Page) => page.getByRole("form", { name: "Record a diagnosis" });

async function startEncounter(page: Page, patient: string) {
  const r = await api(page, "post", `/api/patients/${ids[patient]}/encounters`, {}, { "Idempotency-Key": `diag-${patient}-${stamp}` });
  expect([200, 201]).toContain(r.status);
  encounters[patient] = r.body.id;
}

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
  writeFileSync(path.join(OUT, "diagnoses-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("SETUP: three patients", async () => {
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Alder", "1985-03-09", "555-010-0100"], ["bo", "Bo", "Birch", "1972-11-23", "555-010-0177"], ["cy", "Cy", "Cedar", "1960-01-02", "555-010-0188"]] as const) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `diag-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id;
  }
});

test("NOTHING INVENTED: with no encounter there is no form and it says why; once an encounter is started the form appears and the list says none has been recorded; the safety strip stays in view", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  await expect(recordForm(page)).toHaveCount(0);
  await expect(page.getByRole("note")).toContainText("This patient has no encounter yet");
  await startEncounter(page, "ann");
  await startEncounter(page, "bo");
  await startEncounter(page, "cy");
  await gotoTab(page, "ann");
  await expect(recordForm(page)).toBeVisible();
  await expect(page.getByText("No diagnosis has been recorded for this patient. That means none has been recorded, not that nothing is wrong.")).toBeVisible();
  await expect(page.getByRole("region", { name: "Patient safety", exact: true })).toBeVisible();
  await shot(page, "01-empty.png");
  expect(await listOf(page, "ann")).toEqual([]);
  evidence.nothingInvented = { noEncounterNoForm: true, emptyListHonest: true };
});

test("ACCEPTANCE 1: a diagnosis recorded for an encounter is linked to the patient and the encounter, the tooth is stored as its FDI key, and the treatment plan reference is shown as unresolved", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const f = recordForm(page);
  await f.getByLabel("Diagnosis").fill("  Caries   on the occlusal surface ");
  await f.getByLabel("Tooth (optional)").selectOption("16");                                      // Universal 3 is FDI 16
  await f.getByLabel(/^Notes/).fill("Sensitive to cold.");
  await f.getByLabel("Reference", { exact: true }).fill("  plan-2026-07 ");
  await shot(page, "02-form-filled.png");
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis recorded.");

  const li = item(page, "Caries on the occlusal surface");
  await expect(li).toContainText("Tooth 3 (upper right first molar)");
  await expect(li).toContainText("Treatment plan reference (unresolved): plan-2026-07");
  await expect(li).toContainText("it does not mean a treatment plan with this reference exists");
  await expect(li).toContainText(`recorded by ${NAMES.Dentist}`);
  await shot(page, "03-recorded.png");

  const [d] = await listOf(page, "ann");
  expect([d.patientId, d.encounterId, d.label, d.toothKey, d.notes, d.treatmentPlanReference, d.treatmentPlanReferenceState, d.status]).toEqual([ids.ann, encounters.ann, "Caries on the occlusal surface", "16", "Sensitive to cold.", "plan-2026-07", "Unresolved", "Active"]);
  const byEncounter = (await api(page, "get", `${D("ann")}?encounterId=${encounters.ann}`)).body.diagnoses;
  expect(byEncounter).toHaveLength(1);
  evidence.acceptance1 = { linkedToPatient: true, linkedToEncounter: true, toothStoredAs: "16", referenceState: "Unresolved" };
});

test("ACCEPTANCE 2: incorrect data is refused on screen with every problem listed and linked, nothing is sent, and the server refuses the same mistakes for any caller", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  const before = (await listOf(page, "ann")).length;
  const f = recordForm(page);
  await f.getByLabel("Reference", { exact: true }).fill("   ");
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  const alert = page.getByRole("alert").filter({ hasText: "Some entries need correcting" });
  await expect(alert).toContainText("Diagnosis: Enter the diagnosis.");
  await expect(alert).toContainText("Treatment plan reference: The treatment-plan reference is blank.");
  await expect(alert).toBeFocused();
  await shot(page, "04-problems.png");
  expect((await listOf(page, "ann")).length).toBe(before);
  await alert.getByRole("button", { name: "Diagnosis" }).click();
  await expect(f.getByLabel("Diagnosis")).toBeFocused();
  await f.getByLabel("Reference", { exact: true }).fill("");

  const sent = await api(page, "post", D("ann"), { idempotencyKey: "bad-1", encounterId: encounters.ann, label: "", toothKey: "19", notes: "bell\u0007", treatmentPlanReference: "   " });
  expect([sent.status, sent.body.error]).toEqual([400, "validation_failed"]);
  expect(sent.body.problems.map((p: any) => `${p.field}:${p.code}`)).toEqual(["label:required", "toothKey:unknown_tooth", "notes:invalid_characters", "treatmentPlanReference:blank"]);
  const missing = await api(page, "post", D("ann"), { idempotencyKey: "bad-2", label: "x" });
  expect(missing.body.problems[0].field).toBe("encounterId");
  expect((await listOf(page, "ann")).length).toBe(before);
  evidence.acceptance2 = { screenProblems: 2, serverProblems: sent.body.problems.map((p: any) => p.code) };
});

test("THE LINKS ARE AUTHORITATIVE: an encounter of another patient and one that does not exist are refused identically and nothing is saved", async () => {
  const page = sessions.Dentist.page;
  const before = (await listOf(page, "ann")).length;
  const other = await api(page, "post", D("ann"), entry("ann", "wrong-1", { encounterId: encounters.bo }));
  const none = await api(page, "post", D("ann"), entry("ann", "wrong-2", { encounterId: "00000000-0000-0000-0000-00000000dead" }));
  expect([other.status, other.body.error, none.status, none.body.error]).toEqual([404, "encounter_not_found", 404, "encounter_not_found"]);
  expect(other.body.message).toBe(none.body.message);                                                // it does not reveal that the encounter exists for someone else
  expect((await api(page, "post", `/api/patients/00000000-0000-0000-0000-00000000dead/diagnoses`, entry("ann", "wrong-3"))).status).toBe(404);
  expect((await listOf(page, "ann")).length).toBe(before);
  evidence.links = { anotherPatientsEncounter: "encounter_not_found", missingEncounter: "encounter_not_found" };
});

test("THE REFERENCE IS KEPT: a correction keeps it unless it is explicitly replaced or removed, withdrawing keeps it, and every value is in the history", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  await item(page, "Caries on the occlusal surface").getByRole("button", { name: "Correct" }).click();
  let f = page.getByRole("form", { name: "Correct this diagnosis" });
  await expect(f.getByLabel(/Keep it/)).toBeChecked();
  await f.getByLabel("Diagnosis").fill("Deep caries on the occlusal surface");
  await f.getByLabel("Why is this being corrected?").fill("Clarified at review");
  await f.getByRole("button", { name: "Save correction" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis corrected.");
  await expect(item(page, "Deep caries on the occlusal surface")).toContainText("Treatment plan reference (unresolved): plan-2026-07");     // left alone, so kept

  await item(page, "Deep caries on the occlusal surface").getByRole("button", { name: "Correct" }).click();
  f = page.getByRole("form", { name: "Correct this diagnosis" });
  await f.getByLabel("Replace it").check();
  await f.getByLabel("Reference", { exact: true }).fill("plan-2026-08");
  await f.getByLabel("Why is this being corrected?").fill("Moved to the revised plan");
  await f.getByRole("button", { name: "Save correction" }).click();
  await expect(item(page, "Deep caries on the occlusal surface")).toContainText("plan-2026-08");

  await item(page, "Deep caries on the occlusal surface").getByRole("button", { name: "Correct" }).click();
  f = page.getByRole("form", { name: "Correct this diagnosis" });
  await f.getByLabel("Remove it").check();
  await f.getByLabel("Why is this being corrected?").fill("Entered against the wrong plan");
  await f.getByRole("button", { name: "Save correction" }).click();
  await expect(item(page, "Deep caries on the occlusal surface")).toContainText("No treatment plan reference.");

  const [d] = await listOf(page, "ann");
  const history = (await api(page, "get", `/api/diagnoses/${d.id}/history`)).body.versions;
  expect(history.map((v: any) => [v.changeType, v.treatmentPlanReference, v.reason])).toEqual([["Recorded", "plan-2026-07", null], ["Corrected", "plan-2026-07", "Clarified at review"], ["Corrected", "plan-2026-08", "Moved to the revised plan"], ["Corrected", null, "Entered against the wrong plan"]]);
  await item(page, "Deep caries on the occlusal surface").getByRole("button", { name: "History" }).click();
  await expect(page.getByRole("table", { name: /History of this diagnosis/ })).toBeVisible();
  await shot(page, "05-history.png");
  evidence.reference = { keptWhenLeftAlone: true, replacedOnlyWhenAsked: true, removedOnlyWhenAsked: true, historyHasEveryValue: true };
});

test("WITHDRAWING: it asks why, keeps the diagnosis and its reference, hides it from the working list and shows it on request", async () => {
  const page = sessions.Dentist.page;
  await api(page, "post", D("bo"), entry("bo", "bo-1", { label: "Gingivitis", treatmentPlanReference: "plan-bo-1" }));
  await gotoTab(page, "bo");
  await item(page, "Gingivitis").getByRole("button", { name: "Withdraw" }).click();
  const f = page.getByRole("form", { name: "Withdraw this diagnosis" });
  await expect(f.getByRole("button", { name: "Withdraw diagnosis" })).toBeDisabled();
  await f.getByLabel("Why is this diagnosis being withdrawn?").fill("Entered on the wrong patient");
  await f.getByRole("button", { name: "Withdraw diagnosis" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis withdrawn.");
  await expect(page.getByRole("listitem", { name: "Diagnosis: Gingivitis" })).toHaveCount(0);
  await page.getByLabel("Show withdrawn diagnoses").check();
  const li = item(page, "Gingivitis");
  await expect(li).toContainText("Withdrawn");
  await expect(li).toContainText("Entered on the wrong patient");
  await expect(li).toContainText("Treatment plan reference (unresolved): plan-bo-1");
  await expect(li.getByRole("button", { name: "Correct" })).toHaveCount(0);
  await shot(page, "06-withdrawn.png");
  const all = await listOf(page, "bo", true);
  expect([all.length, all[0].status, all[0].treatmentPlanReference]).toEqual([1, "Withdrawn", "plan-bo-1"]);           // never deleted
  expect(await listOf(page, "bo")).toEqual([]);
  evidence.withdrawn = { keptWithReference: true, correctionRefused: true };
});

test("RETRIES: the same key and entry returns one diagnosis, a different entry under it is refused, eight simultaneous saves make one, and a dropped connection makes exactly one", async () => {
  const page = sessions.Dentist.page;
  const first = await api(page, "post", D("cy"), entry("cy", "visit-a", { treatmentPlanReference: "plan-cy-1" }));
  const again = await api(page, "post", D("cy"), entry("cy", "visit-a", { label: "  Chronic periodontitis ", treatmentPlanReference: " plan-cy-1 " }));
  expect([first.status, again.status, again.body.id === first.body.id]).toEqual([200, 200, true]);
  const other = await api(page, "post", D("cy"), entry("cy", "visit-a", { label: "Something else" }));
  expect([other.status, other.body.error]).toEqual([409, "idempotency_key_reused"]);
  const races = await Promise.all(Array.from({ length: 8 }, () => api(page, "post", D("cy"), entry("cy", "double-click", { label: "Race diagnosis" }))));
  expect(new Set(races.map((r) => r.body.id)).size).toBe(1);
  const before = (await listOf(page, "cy")).length;

  await gotoTab(page, "cy");
  const f = recordForm(page);
  await f.getByLabel("Diagnosis").fill("Dropped connection diagnosis");
  let dropOnce = true;
  await page.route("**/api/patients/*/diagnoses", async (route) => {
    if (route.request().method() === "POST" && dropOnce) { dropOnce = false; await route.fetch(); await route.abort("connectionreset"); }       // the server stores it, the answer is lost
    else await route.continue();
  });
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  await expect(page.getByText("Not saved: the connection dropped. What you entered is still here; save again to retry.")).toBeVisible();
  await expect(f.getByLabel("Diagnosis")).toHaveValue("Dropped connection diagnosis");
  await shot(page, "07-dropped.png");
  await f.getByRole("button", { name: "Record diagnosis" }).click();
  await expect(statusLine(page)).toHaveText("Diagnosis recorded.");
  await page.unroute("**/api/patients/*/diagnoses");
  expect((await listOf(page, "cy")).length).toBe(before + 1);                                         // stored once, though sent twice
  evidence.retries = { sameKey: "one diagnosis", differentEntry: 409, eightSimultaneous: "one diagnosis", droppedConnection: "kept entries, one diagnosis on retry" };
});

test("STALE CORRECTION: a diagnosis changed by someone else shows the conflict banner and keeps what was typed; after reloading the correction goes through", async () => {
  const dentist = sessions.Dentist.page;
  const hygienist = sessions.Hygienist.page;
  await gotoTab(dentist, "cy");
  await item(dentist, "Race diagnosis").getByRole("button", { name: "Correct" }).click();
  const f = dentist.getByRole("form", { name: "Correct this diagnosis" });
  await f.getByLabel("Diagnosis").fill("Race diagnosis, mine");
  await f.getByLabel("Why is this being corrected?").fill("Dentist's review");
  const d = (await listOf(hygienist, "cy")).find((x: any) => x.label === "Race diagnosis");
  expect((await api(hygienist, "post", `/api/diagnoses/${d.id}/correct`, { rowVersion: d.rowVersion, label: "Race diagnosis, hygienist's", reason: "First to save" })).status).toBe(200);
  await f.getByRole("button", { name: "Save correction" }).click();
  await expect(dentist.getByText("Someone else changed this while you were editing")).toBeVisible();
  await expect(f.getByLabel("Diagnosis")).toHaveValue("Race diagnosis, mine");
  await shot(dentist, "08-conflict.png");
  expect((await api(dentist, "get", `/api/diagnoses/${d.id}`)).body.label).toBe("Race diagnosis, hygienist's");               // the first change stands; nothing was merged
  await dentist.getByRole("button", { name: "Reload current version" }).click();
  await expect(dentist.getByText("Someone else changed this while you were editing")).toBeHidden();
  await dentist.getByRole("form", { name: "Correct this diagnosis" }).getByRole("button", { name: "Save correction" }).click();
  await expect(statusLine(dentist)).toHaveText("Diagnosis corrected.");
  expect((await api(dentist, "get", `/api/diagnoses/${d.id}`)).body.label).toBe("Race diagnosis, mine");
  evidence.stale = { conflictBanner: true, typedKept: true, firstChangeStands: true };
});

test("TRUST: every record, correction and withdrawal is in the audit log with the user and a time, and nothing diagnosed is in it", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=1000")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const mine = entries.filter((e) => e.eventType.startsWith("Diagnosis"));
  const count = (t: string) => mine.filter((e) => e.eventType === t).length;
  const diagnoses = (await listOf(sessions.Dentist.page, "ann", true)).length + (await listOf(sessions.Dentist.page, "bo", true)).length + (await listOf(sessions.Dentist.page, "cy", true)).length;
  expect(count("DiagnosisRecorded")).toBe(diagnoses);                                                // one per diagnosis, none for refused or repeated saves
  expect(count("DiagnosisCorrected")).toBe(5);                                                       // three on Ann's, two on Cy's
  expect(count("DiagnosisWithdrawn")).toBe(1);
  expect(mine.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = mine.map((e) => e.details).join(" ").toLowerCase();
  for (const secret of ["caries", "gingivitis", "periodontitis", "plan-", "sensitive", "wrong patient", "tooth", "16"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["DiagnosisRecorded", "DiagnosisCorrected", "DiagnosisWithdrawn"].map((t) => [t, count(t)]));
});

test("ROLES: the dentist and hygienist can record; the assistant can only read; front desk and billing cannot see it; anonymous callers are refused; nothing is saved without a token", async () => {
  const hygienist = sessions.Hygienist.page;
  const saved = await api(hygienist, "post", D("bo"), entry("bo", "hyg-1", { label: "Recorded by the hygienist" }));
  expect(saved.status).toBe(200);
  expect(saved.body.recordedByName).toBe(NAMES.Hygienist);

  const assistant = sessions.Assistant.page;
  await gotoTab(assistant, "bo");
  await expect(assistant.getByRole("form", { name: "Record a diagnosis" })).toHaveCount(0);
  await expect(statusLine(assistant)).toHaveText("You can read this but your role cannot change it.");
  await expect(item(assistant, "Recorded by the hygienist")).toBeVisible();
  await expect(item(assistant, "Recorded by the hygienist").getByRole("button", { name: "Correct" })).toHaveCount(0);
  expect((await api(assistant, "post", D("bo"), entry("bo", "asst-1"))).status).toBe(403);
  expect((await api(assistant, "post", `/api/diagnoses/${saved.body.id}/withdraw`, { rowVersion: saved.body.rowVersion, reason: "x" })).status).toBe(403);

  for (const role of ["FrontDesk", "Billing"]) {
    const page = sessions[role].page;
    expect((await api(page, "get", D("bo"))).status, `${role} read`).toBe(403);
    expect((await api(page, "post", D("bo"), entry("bo", `${role}-1`))).status, `${role} write`).toBe(403);
    await page.goto(`/patients/${ids.bo}`);
    await expect(page.getByRole("link", { name: "Diagnoses" })).toHaveCount(0);
  }
  const anon = await admin.context().browser()!.newContext();
  const anonPage = await anon.newPage();
  expect((await anonPage.request.get(`${D("bo")}`)).status()).toBe(401);
  expect((await anonPage.request.post(`${D("bo")}`, { data: {} })).status()).toBe(401);
  await anon.close();
  const noToken = await sessions.Dentist.page.request.post(D("bo"), { data: entry("bo", "no-csrf") });
  expect(noToken.ok()).toBe(false);
  expect((await listOf(sessions.Dentist.page, "bo")).map((x: any) => x.label)).toEqual(["Recorded by the hygienist"]);
  evidence.roles = { dentist: "record, correct, withdraw", hygienist: "record", assistant: "read only (403 on write)", frontDesk: "403, no tab", billing: "403, no tab", anonymous: 401, noCsrfToken: "refused" };
});

test("ACCESSIBILITY AND TABLET: axe finds no critical or serious issue on the list, with problems shown, with a correction and the history open, in both themes; at 768 px the page does not scroll sideways", async () => {
  const page = sessions.Dentist.page;
  await gotoTab(page, "ann");
  await scanBothThemes(page, "list");
  await recordForm(page).getByRole("button", { name: "Record diagnosis" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Some entries need correcting" })).toBeVisible();
  await scanBothThemes(page, "problems-shown");
  await gotoTab(page, "cy");
  await item(page, "Dropped connection diagnosis").getByRole("button", { name: "Correct" }).click();
  await item(page, "Dropped connection diagnosis").getByRole("button", { name: "History" }).click();
  await expect(page.getByRole("table", { name: /History of this diagnosis/ })).toBeVisible();
  await scanBothThemes(page, "correction-and-history");

  await page.setViewportSize({ width: 768, height: 1024 });
  await gotoTab(page, "ann");
  await expect(recordForm(page)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await shot(page, "09-tablet.png");
  await scanBothThemes(page, "tablet");
  await page.setViewportSize({ width: 1280, height: 720 });
  evidence.accessibility = { scanned: ["list", "problems-shown", "correction-and-history", "tablet"], tabletNoHorizontalPageScroll: true };
});
