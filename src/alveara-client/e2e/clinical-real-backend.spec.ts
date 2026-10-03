import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-005 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Like the other real-backend specs it is excluded from the default mocked run (playwright.config.ts) and run with playwright.clinical.config.ts against an API the
// caller started (see docs/testing/REAL_BACKEND_E2E.md). It walks the story's acceptance items as a practice would: a dentist documents an encounter (medical and
// dental history, an allergy, a medication), an incomplete note cannot be finalized, a finalized note is immutable, an addendum is added beside the untouched original,
// every change is in the audit log with user and time, and the people who must not read it cannot - then it scans the screens with axe in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.CLINICAL_E2E_OUT ?? path.join(process.cwd(), "clinical-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "clinical-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function createUser(role: string, username: string) {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
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
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, unknown> };
}

const region = (page: Page, name: string) => page.getByRole("region", { name });

async function addEntry(page: Page, section: string, noun: string, fields: Record<string, string>) {
  await region(page, section).getByRole("button", { name: `Add ${noun}: ${section}` }).click();
  for (const [label, value] of Object.entries(fields)) {
    const control = region(page, section).getByLabel(label);
    if ((await control.evaluate((el) => el.tagName)) === "SELECT") await control.selectOption(value);
    else await control.fill(value);
  }
  await region(page, section).getByRole("button", { name: `Add ${noun}`, exact: true }).click();
}

async function startEncounter(page: Page, patientId: string) {
  await page.goto(`/patients/${patientId}/clinical`);
  await page.getByRole("button", { name: "Start an encounter" }).click();
  await expect(page.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();
  return page.url().split("/").pop()!;
}

const encounter = async (page: Page, id: string) => (await api(page, "get", `/api/encounters/${id}`)).body as any;

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager"]) {
    await createUser(role, `${role.toLowerCase()}-${stamp}`);
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  for (const [key, first, last, dob, phone] of [["ann", "Ann", "Clinical", "1985-03-09", "555-010-0100"], ["ben", "Ben", "Other", "1990-01-01", "555-020-0200"]]) {
    const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `reg-${key}-${stamp}` });
    expect(r.status).toBe(201);
    ids[key] = r.body.id as string;
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "clinical-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("ACCESS: the clinical team sees the Clinical tab; front desk, billing and the practice manager do not and cannot read it; an assistant reads but cannot write", async () => {
  const dentist = sessions.Dentist.page;
  await dentist.goto(`/patients/${ids.ann}`);
  await expect(dentist.getByRole("link", { name: "Clinical", exact: true })).toBeVisible();

  const denied: Record<string, unknown> = {};
  for (const role of ["FrontDesk", "Billing", "OfficeManager"]) {
    const page = sessions[role].page;
    await page.goto(`/patients/${ids.ann}`);
    await expect(page.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Clinical", exact: true })).toHaveCount(0);
    await page.goto(`/patients/${ids.ann}/clinical`);
    await expect(page.getByText(/don't have permission/i).first()).toBeVisible();
    const read = await api(page, "get", `/api/patients/${ids.ann}/encounters`);
    const write = await api(page, "post", `/api/patients/${ids.ann}/encounters`, {});
    expect([read.status, write.status]).toEqual([403, 403]);
    denied[role] = { tab: false, page: "denied", read: read.status, write: write.status };
  }

  const assistant = sessions.Assistant.page;
  await assistant.goto(`/patients/${ids.ann}/clinical`);
  await expect(assistant.getByRole("heading", { name: "Clinical documentation" })).toBeVisible();
  await expect(assistant.getByRole("button", { name: "Start an encounter" })).toHaveCount(0);
  expect((await api(assistant, "post", `/api/patients/${ids.ann}/encounters`, {})).status).toBe(403);
  expect((await api(assistant, "get", `/api/patients/${ids.ann}/encounters`)).status).toBe(200);
  evidence.access = { clinicalTeamSeesTab: true, ...denied, assistant: { read: 200, write: 403, startButton: false } };
});

test("ACCEPTANCE 1: a dentist documents an encounter - medical and dental history, an allergy and a medication - each saved as it is made", async () => {
  const page = sessions.Dentist.page;
  ids.enc1 = await startEncounter(page, ids.ann);
  await expect(page.getByText("Draft - not finalized").first()).toBeVisible();
  await shot(page, "01-new-draft.png");

  await addEntry(page, "Medical history", "medical history item", { Name: "Hypertension", Details: "Controlled with medication" });
  await expect(region(page, "Medical history").getByText("Hypertension")).toBeVisible();
  await addEntry(page, "Dental history", "dental history item", { Name: "Root canal, lower left, 2019" });
  await addEntry(page, "Allergies", "allergy", { Name: "Penicillin", Reaction: "Hives", Severity: "Moderate" });
  await addEntry(page, "Medications", "medication", { Name: "Lisinopril", Dose: "10 mg", Frequency: "daily" });
  await expect(region(page, "Allergies").getByText("Reaction: Hives · Severity: Moderate")).toBeVisible();
  await expect(region(page, "Medications").getByText("Dose: 10 mg · Frequency: daily")).toBeVisible();
  await expect(page.getByText("Medications entry saved.")).toBeVisible();
  await shot(page, "02-documented.png");

  const stored = await encounter(page, ids.enc1);
  const byKind = Object.fromEntries(stored.sections.map((s: any) => [s.kind, s.entries.map((e: any) => e.name)]));
  expect(byKind).toEqual({ MedicalHistory: ["Hypertension"], DentalHistory: ["Root canal, lower left, 2019"], Allergy: ["Penicillin"], Medication: ["Lisinopril"] });
  expect(stored.isComplete).toBe(true);
  expect(stored.sections.every((s: any) => s.entries.every((e: any) => e.createdByUserId && e.createdAtUtc))).toBe(true);
  evidence.documented = { sections: byKind, complete: stored.isComplete, attributed: true };

  await scanBothThemes(page, "draft-editor");
  await region(page, "Allergies").getByRole("button", { name: "Add allergy: Allergies" }).click();
  await scanBothThemes(page, "draft-entry-form-open");
  await region(page, "Allergies").getByRole("button", { name: "Cancel" }).click();
});

test("INCOMPLETE DOCUMENTATION: a note with sections still not addressed cannot be finalized; 'reviewed - none reported' completes it without inventing an entry", async () => {
  const page = sessions.Dentist.page;
  ids.enc2 = await startEncounter(page, ids.ann);
  await addEntry(page, "Medical history", "medical history item", { Name: "Asthma" });

  await page.getByRole("button", { name: "Review and finalize" }).click();
  const review = page.getByRole("region", { name: "Review before finalizing" });
  await expect(review.getByText(/Needs attention/)).toHaveCount(3);
  await expect(review.getByRole("button", { name: "Finalize encounter" })).toBeDisabled();
  await shot(page, "03-review-incomplete.png");
  await scanBothThemes(page, "finalize-review-incomplete");

  // the API refuses too, naming the sections
  const draft = await encounter(page, ids.enc2);
  const refused = await api(page, "post", `/api/encounters/${ids.enc2}/finalize`, { rowVersion: draft.rowVersion });
  expect([refused.status, refused.body.error]).toEqual([409, "documentation_incomplete"]);
  expect(Object.keys(refused.body.fieldErrors as object).sort()).toEqual(["Allergy", "DentalHistory", "Medication"]);

  await page.getByRole("button", { name: "Keep editing" }).click();
  for (const section of ["Dental history", "Allergies", "Medications"]) await region(page, section).getByRole("button", { name: `Reviewed - none reported: ${section}` }).click();
  await expect(region(page, "Medications").getByText("Reviewed - none reported", { exact: true })).toBeVisible();
  await expect(region(page, "Allergies").getByRole("button", { name: /Add allergy/ })).toHaveCount(0); // cannot add while marked
  await page.getByRole("button", { name: "Review and finalize" }).click();
  await expect(page.getByRole("region", { name: "Review before finalizing" }).getByText(/Needs attention/)).toHaveCount(0);
  evidence.incomplete = { refused: 409, code: "documentation_incomplete", sections: Object.keys(refused.body.fieldErrors as object), noneReportedCompletesIt: true };
});

test("ACCEPTANCE 2: a finalized encounter cannot be changed, and an addendum is added beside the untouched original", async () => {
  const page = sessions.Dentist.page; // still on enc2's review
  await page.getByRole("button", { name: "Finalize encounter" }).click();
  await expect(page.getByText("Finalized", { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/can no longer be changed/)).toBeVisible();
  await expect(page.getByRole("button", { name: /^Add (medical|dental|allergy|medication)|Change|Remove|Review and finalize|Reviewed - none reported/ })).toHaveCount(0);

  const original = await encounter(page, ids.enc2);
  expect(original.status).toBe("Finalized");
  const addAttempt = await api(page, "post", `/api/encounters/${ids.enc2}/entries`, { kind: "Allergy", name: "Added later", rowVersion: original.rowVersion });
  expect([addAttempt.status, addAttempt.body.error]).toEqual([409, "encounter_finalized"]);
  const markAttempt = await api(page, "post", `/api/encounters/${ids.enc2}/sections/Allergy/clear-review`, { rowVersion: original.rowVersion });
  expect([markAttempt.status, markAttempt.body.error]).toEqual([409, "encounter_finalized"]);

  await page.getByLabel("New addendum").fill("Patient later confirmed the allergy list is complete.");
  await page.getByRole("button", { name: "Add addendum" }).click();
  await expect(page.getByText("Patient later confirmed the allergy list is complete.")).toBeVisible();
  await expect(page.getByLabel("New addendum")).toHaveValue("");
  await shot(page, "04-finalized-with-addendum.png");

  const after = await encounter(page, ids.enc2);
  expect(after.sections).toEqual(original.sections);          // the original entries and reviews are exactly as they were
  expect(after.rowVersion).toBe(original.rowVersion);         // the encounter row itself was never touched
  expect(after.status).toBe("Finalized");
  expect(after.addenda.map((a: any) => a.text)).toEqual(["Patient later confirmed the allergy list is complete."]);
  expect(after.addenda[0].createdByUserId && after.addenda[0].createdAtUtc).toBeTruthy();
  evidence.amendment = { finalizedRefusesChanges: ["encounter_finalized", "encounter_finalized"], originalUnchanged: true, rowVersionUnchanged: true, addenda: after.addenda.length };

  await scanBothThemes(page, "finalized-with-addendum");

  // the clinical team reads it; the original and the addendum are both there
  const hygienist = sessions.Hygienist.page;
  await hygienist.goto(`/patients/${ids.ann}/clinical/${ids.enc2}`);
  await expect(hygienist.getByText("Patient later confirmed the allergy list is complete.")).toBeVisible();
  await expect(region(hygienist, "Medical history").getByText("Asthma")).toBeVisible();
});

test("DATA LOSS: a response lost after the server stored an addendum keeps the typed text and a retry cannot add it twice", async () => {
  const page = sessions.Dentist.page;
  await page.goto(`/patients/${ids.ann}/clinical/${ids.enc2}`);
  await expect(page.getByRole("heading", { name: "Addenda" })).toBeVisible();
  let lost = 0;
  await page.route("**/api/encounters/*/addenda", async (route) => {
    if (route.request().method() === "POST" && lost === 0) {
      lost++;
      await route.fetch(); // the server receives and STORES the addendum...
      await route.abort("failed"); // ...but the answer never reaches the browser
    } else await route.continue();
  });
  await page.getByLabel("New addendum").fill("Sent once, stored once.");
  await page.getByRole("button", { name: "Add addendum" }).click();
  await expect(page.getByText(/not certain the addendum was saved/)).toBeVisible();
  await expect(page.getByLabel("New addendum")).toHaveValue("Sent once, stored once.");
  await shot(page, "05-dropped-response.png");

  await page.getByRole("button", { name: "Add addendum" }).click();
  await expect(page.getByText("Sent once, stored once.", { exact: true })).toBeVisible();
  await page.unroute("**/api/encounters/*/addenda");
  const after = await encounter(page, ids.enc2);
  expect(after.addenda.filter((a: any) => a.text === "Sent once, stored once.")).toHaveLength(1);
  evidence.droppedResponse = { storedBeforeTheAnswerWasLost: true, retryAddedItAgain: false, addendaWithThatText: 1 };
});

test("CONCURRENCY: a second clinician's stale screen is refused with the conflict banner; what they typed survives the reload and then saves", async () => {
  const first = sessions.Dentist.page;
  const second = sessions.Hygienist.page;
  ids.enc3 = await startEncounter(first, ids.ann);
  await second.goto(`/patients/${ids.ann}/clinical/${ids.enc3}`);
  await expect(second.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();

  await addEntry(first, "Allergies", "allergy", { Name: "Latex" }); // moves the version under the second clinician's feet
  await region(second, "Medications").getByRole("button", { name: "Add medication: Medications" }).click();
  await region(second, "Medications").getByLabel("Name").fill("Aspirin");
  await region(second, "Medications").getByRole("button", { name: "Add medication", exact: true }).click();

  await expect(second.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(second, "06-stale-edit.png");
  let current = await encounter(first, ids.enc3);
  expect(current.sections.find((s: any) => s.kind === "Medication").entries).toHaveLength(0); // nothing was overwritten
  expect(current.sections.find((s: any) => s.kind === "Allergy").entries.map((e: any) => e.name)).toEqual(["Latex"]);
  await scanBothThemes(second, "conflict-banner");

  await second.getByRole("button", { name: /reload/i }).click();
  await expect(second.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  await expect(region(second, "Allergies").getByText("Latex")).toBeVisible(); // the reload shows the other clinician's change
  await expect(region(second, "Medications").getByLabel("Name")).toHaveValue("Aspirin"); // and the typed value survived (refreshed in place)
  await region(second, "Medications").getByRole("button", { name: "Add medication", exact: true }).click();
  await expect(region(second, "Medications").getByText("Aspirin")).toBeVisible();
  current = await encounter(first, ids.enc3);
  expect(current.sections.find((s: any) => s.kind === "Medication").entries.map((e: any) => e.name)).toEqual(["Aspirin"]);
  evidence.concurrency = { staleEditRefused: true, otherChangeStood: true, typedValuesSurvivedReload: true, savedAfterReload: true };
});

test("KEYBOARD: an allergy can be recorded without a mouse", async () => {
  const page = sessions.Dentist.page;
  await page.goto(`/patients/${ids.ann}/clinical/${ids.enc3}`);
  await expect(page.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();
  await region(page, "Dental history").getByRole("button", { name: "Add dental history item: Dental history" }).focus();
  await page.keyboard.press("Enter");
  await page.keyboard.type("Extraction 2021");
  await page.keyboard.press("Enter");
  await expect(region(page, "Dental history").getByText("Extraction 2021")).toBeVisible();
  evidence.keyboard = { entryRecordedWithKeyboardOnly: true };
});

test("SCOPE: another patient's list never shows this patient's encounters, and an encounter opened under the wrong patient is not found", async () => {
  const page = sessions.Dentist.page;
  await page.goto(`/patients/${ids.ben}/clinical`);
  await expect(page.getByText("No encounters documented yet")).toBeVisible();
  expect(((await api(page, "get", `/api/patients/${ids.ben}/encounters`)).body as unknown as unknown[]).length).toBe(0);
  await page.goto(`/patients/${ids.ben}/clinical/${ids.enc2}`);
  await expect(page.getByText("That encounter was not found")).toBeVisible();
  await expect(page.getByText("Asthma")).toHaveCount(0);

  await page.goto(`/patients/${ids.ann}/clinical`);
  await expect(page.getByRole("heading", { name: "Clinical documentation" })).toBeVisible();
  await expect(page.getByRole("link", { name: /Encounter on/ })).toHaveCount(3);
  await shot(page, "07-encounter-list.png");
  await scanBothThemes(page, "encounter-list");
  evidence.scope = { otherPatientList: 0, wrongPatientEncounter: "not found", annEncounters: 3 };
});

test("TRUST: every change is in the audit log with the user and a time, and no clinical content is in it", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const clinical = entries.filter((e) => e.eventType.startsWith("Encounter"));
  const by = (t: string) => clinical.filter((e) => e.eventType === t);
  expect(by("EncounterStarted").length).toBe(3);
  expect(by("EncounterEntryAdded").length).toBeGreaterThanOrEqual(8);
  expect(by("EncounterSectionReviewed").length).toBe(3);
  expect(by("EncounterFinalized").length).toBe(1);
  expect(by("EncounterAddendumAdded").length).toBe(2);
  expect(clinical.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = clinical.map((e) => e.details).join(" ");
  for (const secret of ["Penicillin", "Hypertension", "Lisinopril", "Hives", "Asthma", "Latex", "Aspirin", "Patient later confirmed", "Sent once"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["EncounterStarted", "EncounterEntryAdded", "EncounterEntryChanged", "EncounterSectionReviewed", "EncounterFinalized", "EncounterAddendumAdded"].map((t) => [t, by(t).length]));
});
