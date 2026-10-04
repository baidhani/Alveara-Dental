import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-005-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.clinical-companion.config.ts against an API the caller started (see
// docs/testing/REAL_BACKEND_E2E.md). It walks the story's acceptance items as a practice would: the longitudinal record is captured with attribution and a change keeps its
// history; "none known" / "unknown" / "not reviewed" stay distinguishable and invent nothing; a template shapes an encounter's SOAP and treatment notes and blocks signing until
// the required ones are written; vitals are recorded with the encounter and a mistake is voided, never edited; a signed note is locked; a finalized note cannot be overwritten
// and an amendment preserves the original and who wrote it - then it scans every new screen with axe in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.COMPANION_E2E_OUT ?? path.join(process.cwd(), "companion-e2e-out");
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

const card = (page: Page, name: string) => page.getByRole("region", { name: `Record: ${name}` });
const region = (page: Page, name: string) => page.getByRole("region", { name });
/** The encounter screen's save indicator (the same words also appear in the history list, so look at the indicator itself). */
const saved = (page: Page, text: string) => expect(page.locator("p.alv-clinical__status")).toHaveText(text, { timeout: 8000 });
const encounter = async (page: Page, id: string) => (await api(page, "get", `/api/encounters/${id}`)).body;
const record = async (page: Page) => (await api(page, "get", `/api/patients/${ids.ann}/clinical-record`)).body;
const section = (rec: any, kind: string) => rec.sections.find((s: any) => s.kind === kind);

async function startEncounter(page: Page) {
  await page.goto(`/patients/${ids.ann}/clinical`);
  await page.getByRole("button", { name: "Start an encounter" }).click();
  await expect(page.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();
  return page.url().split("/").pop()!;
}

async function addRecordItem(page: Page, sectionName: string, noun: string, fields: Record<string, string>) {
  await card(page, sectionName).getByRole("button", { name: `Add ${noun} to the record: ${sectionName}` }).click();
  for (const [label, value] of Object.entries(fields)) {
    const control = card(page, sectionName).getByLabel(label, { exact: true });
    if ((await control.evaluate((el) => el.tagName)) === "SELECT") await control.selectOption(value);
    else await control.fill(value);
  }
  await card(page, sectionName).getByRole("button", { name: `Add ${noun}`, exact: true }).click();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["Dentist", "Hygienist", "Assistant", "FrontDesk"]) {
    const userId = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    if (NAMES[role]) {
      const made = await api(admin, "post", "/api/config/staff", { displayName: `${NAMES[role]} ${stamp}`, userAccountId: userId });
      expect(made.status, `staff profile for ${role}`).toBe(201);
      NAMES[role] = `${NAMES[role]} ${stamp}`;
    }
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
  writeFileSync(path.join(OUT, "companion-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  for (const s of Object.values(sessions)) await s.context.close();
});

test("RECORD: allergies, medications and history are captured across encounters with who and when, and a change keeps what it replaced", async () => {
  const dentist = sessions.Dentist.page;
  await dentist.goto(`/patients/${ids.ann}/clinical`);
  await expect(dentist.getByRole("heading", { name: "Clinical record" })).toBeVisible();
  for (const name of ["Medical history", "Dental history", "Allergies", "Medications"]) await expect(card(dentist, name).getByText("Nobody has reviewed this section yet")).toBeVisible(); // nothing invented

  await addRecordItem(dentist, "Allergies", "allergy", { Name: "Penicillin", Reaction: "Hives", Severity: "Moderate" });
  await expect(card(dentist, "Allergies").getByText("Penicillin")).toBeVisible();
  await addRecordItem(dentist, "Medications", "medication", { Name: "Lisinopril", Dose: "10 mg", Frequency: "daily" });
  await addRecordItem(dentist, "Medical history", "medical history item", { Name: "Hypertension", Details: "Controlled with medication" });
  await expect(card(dentist, "Allergies").getByText(`Added by ${NAMES.Dentist} on`)).toBeVisible();      // attribution is a name
  await expect(card(dentist, "Allergies").getByText("Needs review", { exact: true })).toBeVisible();     // listed, but nobody has confirmed the list
  await shot(dentist, "01-record-captured.png");

  // the hygienist resolves the allergy (with a reason) and corrects the medication; both are kept in the history
  const hyg = sessions.Hygienist.page;
  await hyg.goto(`/patients/${ids.ann}/clinical`);
  await card(hyg, "Allergies").getByRole("button", { name: "Change status of Penicillin" }).click();
  await card(hyg, "Allergies").getByLabel("Status of Penicillin").selectOption("Resolved");
  await card(hyg, "Allergies").getByLabel("Reason (optional)").fill("Tolerated a challenge dose");
  await card(hyg, "Allergies").getByRole("button", { name: "Save status" }).click();
  await expect(card(hyg, "Allergies").getByText("Resolved", { exact: true })).toBeVisible();
  await card(hyg, "Medications").getByRole("button", { name: "Correct details of Lisinopril" }).click();
  await card(hyg, "Medications").getByLabel("Dose", { exact: true }).fill("20 mg");
  await card(hyg, "Medications").getByRole("button", { name: "Save changes" }).click();
  await expect(card(hyg, "Medications").getByText("Dose: 20 mg")).toBeVisible();

  await card(hyg, "Allergies").getByText("History", { exact: true }).click();
  await expect(card(hyg, "Allergies").getByText("Status changed")).toBeVisible();
  await expect(card(hyg, "Allergies").getByText(/Reason: Tolerated a challenge dose/)).toBeVisible();
  await shot(hyg, "02-record-history.png");

  const rec = await record(dentist);
  const allergy = section(rec, "Allergy").items[0];
  const history = (await api(dentist, "get", `/api/clinical-record/items/${allergy.id}/history`)).body;
  expect(history.versions.map((v: any) => [v.changeType, v.status, v.actorName])).toEqual([["Added", "Active", NAMES.Dentist], ["StatusChanged", "Resolved", NAMES.Hygienist]]);
  const med = section(rec, "Medication").items[0];
  const medHistory = (await api(dentist, "get", `/api/clinical-record/items/${med.id}/history`)).body;
  expect(medHistory.versions.map((v: any) => v.dose)).toEqual(["10 mg", "20 mg"]); // what it said before is still there
  evidence.record = { allergyHistory: history.versions.map((v: any) => [v.changeType, v.status, v.actorName]), medicationDoses: medHistory.versions.map((v: any) => v.dose), attributedByName: true };

  await scanBothThemes(hyg, "clinical-record");
  await card(hyg, "Allergies").getByRole("button", { name: "Entered in error: Penicillin" }).click();
  await scanBothThemes(hyg, "clinical-record-remove-form-open");
  await card(hyg, "Allergies").getByRole("button", { name: "Cancel" }).click();
});

test("UNKNOWN / NOT REVIEWED / NONE KNOWN: three different statements, none of them an invented item; a confirmed list goes stale when an item changes", async () => {
  const page = sessions.Dentist.page;
  await page.goto(`/patients/${ids.ann}/clinical`);
  await card(page, "Dental history").getByRole("button", { name: "None known: Dental history" }).click();
  await expect(card(page, "Dental history").getByText(/nothing known to report/)).toBeVisible();
  await expect(card(page, "Dental history").getByText(`Reviewed by ${NAMES.Dentist} on`)).toBeVisible();
  await expect(card(page, "Medical history").getByRole("button", { name: /None known/ })).toHaveCount(0); // it has an item: that statement is not offered
  await shot(page, "03-none-known.png");

  // a clinician can also say it could not be established; that is not the same statement
  const rec1 = await record(page);
  expect(section(rec1, "DentalHistory").status).toBe("NoneKnown");
  expect(section(rec1, "DentalHistory").items).toHaveLength(0);
  await card(page, "Dental history").getByRole("button", { name: "Withdraw the statement: Dental history" }).click();
  await expect(card(page, "Dental history").getByText("Nobody has reviewed this section yet")).toBeVisible();
  await card(page, "Dental history").getByRole("button", { name: "Unknown: Dental history" }).click();
  await expect(card(page, "Dental history").getByText(/it could not be established/)).toBeVisible();
  const rec2 = await record(page);
  expect([section(rec2, "DentalHistory").status, section(rec2, "DentalHistory").items.length]).toEqual(["Unknown", 0]);

  // adding an item withdraws "unknown" (it would contradict it) and the section then needs review
  await addRecordItem(page, "Dental history", "dental history item", { Name: "Root canal, lower left, 2019" });
  await expect(card(page, "Dental history").getByText("Needs review", { exact: true })).toBeVisible();
  expect(section(await record(page), "DentalHistory").status).toBe("NeedsReview");

  // confirm the medication list; then the hygienist changes it and it needs review again
  await card(page, "Medications").getByRole("button", { name: "Confirm the medications list is current" }).click();
  await expect(card(page, "Medications").getByText(`Reviewed by ${NAMES.Dentist} on`)).toBeVisible();
  expect(section(await record(page), "Medication").status).toBe("Reviewed");
  const hyg = sessions.Hygienist.page;
  await hyg.goto(`/patients/${ids.ann}/clinical`);
  await card(hyg, "Medications").getByRole("button", { name: "Change status of Lisinopril" }).click();
  await card(hyg, "Medications").getByLabel("Status of Lisinopril").selectOption("Inactive");
  await card(hyg, "Medications").getByRole("button", { name: "Save status" }).click();
  await expect(card(hyg, "Medications").getByText(/Changed since .* reviewed it on/)).toBeVisible();
  expect(section(await record(page), "Medication").status).toBe("NeedsReview");
  evidence.semantics = { noneKnown: "NoneKnown with 0 items", unknown: "Unknown with 0 items", notReviewed: "NotReviewed (no statement)", addingAnItemWithdrawsTheStatement: true, confirmedListGoesStale: true };

  // a stale edit of an item: the second clinician is refused with the conflict banner and what they typed survives the reload
  const first = sessions.Dentist.page;
  const second = hyg;
  await first.reload();
  await expect(card(first, "Medical history").getByText("Hypertension")).toBeVisible();
  await second.goto(`/patients/${ids.ann}/clinical`);
  await card(second, "Medical history").getByRole("button", { name: "Correct details of Hypertension" }).click();
  await card(second, "Medical history").getByLabel("Details", { exact: true }).fill("Stable on lisinopril");
  await card(first, "Medical history").getByRole("button", { name: "Change status of Hypertension" }).click();
  await card(first, "Medical history").getByLabel("Status of Hypertension").selectOption("Inactive");
  await card(first, "Medical history").getByRole("button", { name: "Save status" }).click();
  await expect(card(first, "Medical history").getByText("Inactive", { exact: true })).toBeVisible();
  await card(second, "Medical history").getByRole("button", { name: "Save changes" }).click();
  await expect(second.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(second, "04-record-stale-edit.png");
  await scanBothThemes(second, "record-conflict-banner");
  await second.getByRole("button", { name: /reload/i }).click();
  await expect(second.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  await expect(card(second, "Medical history").getByLabel("Details", { exact: true })).toHaveValue("Stable on lisinopril"); // typed values survived the in-place reload
  await card(second, "Medical history").getByRole("button", { name: "Save changes" }).click();
  await expect(second.getByText("Item changes saved.")).toBeVisible(); // the form closed: the save finished (the text alone would match the open textarea)
  await expect(card(second, "Medical history").getByText(/Stable on lisinopril/)).toBeVisible();
  const hypertension = section(await record(page), "MedicalHistory").items[0];
  expect([hypertension.status, hypertension.detail]).toEqual(["Inactive", "Stable on lisinopril"]); // both changes stand
  evidence.recordConcurrency = { staleEditRefused: true, otherChangeStood: true, typedValuesSurvivedReload: true };
});

test("TEMPLATE: a dentist configures a clinical note template; a hygienist can read the list but cannot configure", async () => {
  const dentist = sessions.Dentist.page;
  await dentist.goto(`/patients/${ids.ann}/clinical`);
  await dentist.getByRole("link", { name: "Note templates" }).click();
  await expect(dentist.getByRole("heading", { name: "Note templates" })).toBeVisible();
  await dentist.getByRole("button", { name: "New template" }).click();
  await dentist.getByLabel("Template name").fill(`SOAP visit ${stamp}`);
  await dentist.getByLabel("Description").first().fill("Standard visit note");
  for (const [name, starter, required] of [["Subjective", "Chief complaint:", true], ["Objective", "", false], ["Assessment", "", false], ["Plan", "", true], ["Treatment note", "", false]] as const) {
    await dentist.getByLabel(`Include ${name}`).check();
    if (required) await dentist.getByLabel(`${name} is required before signing`).check();
    if (starter) await dentist.getByLabel(`Starter text for ${name}`).fill(starter);
  }
  await scanBothThemes(dentist, "template-form");
  await dentist.getByRole("button", { name: "Create template" }).click();
  await expect(dentist.getByText(`Template SOAP visit ${stamp} created.`)).toBeVisible();
  await expect(dentist.getByText(/Subjective \(required\), Objective, Assessment, Plan \(required\), Treatment note/)).toBeVisible();
  await shot(dentist, "05-templates.png");
  await scanBothThemes(dentist, "templates-list");

  const dup = await api(dentist, "post", "/api/clinical/templates", { name: `soap VISIT ${stamp}`, sections: [{ section: "Plan", required: false }] });
  expect([dup.status, dup.body.error]).toEqual([409, "template_name_taken"]);

  const hyg = sessions.Hygienist.page;
  await hyg.goto(`/patients/${ids.ann}/clinical/templates`);
  await expect(hyg.getByText(`SOAP visit ${stamp}`)).toBeVisible();
  await expect(hyg.getByRole("button", { name: /New template|Change template|Take out of use/ })).toHaveCount(0);
  const denied = await api(hyg, "post", "/api/clinical/templates", { name: "Not allowed", sections: [{ section: "Plan", required: false }] });
  expect([denied.status, denied.body.required]).toEqual([403, "ManageClinicalTemplates"]);
  const templates = (await api(dentist, "get", "/api/clinical/templates")).body as any[];
  ids.template = templates.find((t) => t.name === `SOAP visit ${stamp}`).id;
  evidence.template = { created: true, duplicateNameRefused: 409, hygienistCannotConfigure: 403, ownedByClinicalDomain: "api/clinical/templates, ManageClinicalTemplates" };
});

test("ENCOUNTER: a template shapes the SOAP and treatment notes; required notes block signing and are named; vitals are recorded with the encounter and a mistake is voided", async () => {
  const page = sessions.Dentist.page;
  ids.enc1 = await startEncounter(page);
  // the four documentation sections (STORY-005) - completed with "reviewed, none reported" so no fact is invented
  for (const [name] of [["Medical history"], ["Dental history"], ["Allergies"], ["Medications"]]) await region(page, name).getByRole("button", { name: `Reviewed - none reported: ${name}` }).click();
  await expect(region(page, "Medications").getByText("Reviewed - none reported", { exact: true })).toBeVisible();

  await page.getByLabel("Start from a template").selectOption({ label: `SOAP visit ${stamp}` });
  await page.getByRole("button", { name: "Use template" }).click();
  await expect(page.getByText(`Template: SOAP visit ${stamp}`)).toBeVisible();
  await expect(page.getByLabel("Subjective (required by the template)")).toHaveValue("Chief complaint:");
  await expect(page.getByText("Starter text only - write the note.")).toBeVisible();
  await shot(page, "06-template-applied.png");

  // signing is blocked, and the review names the notes
  await page.getByRole("button", { name: "Review and finalize" }).click();
  const review = page.getByRole("region", { name: "Review before finalizing" });
  await expect(review.getByText(/Needs attention - write this note/)).toHaveCount(2);
  await expect(review.getByRole("button", { name: "Sign note" })).toBeDisabled();
  await shot(page, "07-review-required-notes.png");
  const early = await api(page, "post", `/api/encounters/${ids.enc1}/sign`, { rowVersion: (await encounter(page, ids.enc1)).rowVersion });
  expect([early.status, early.body.error]).toEqual([409, "documentation_incomplete"]);
  expect(Object.keys(early.body.fieldErrors).sort()).toEqual(["note:Plan", "note:Subjective"]);
  await page.getByRole("button", { name: "Keep editing" }).click();

  // notes save themselves: on losing focus, and when typing pauses
  const subjective = page.getByLabel("Subjective (required by the template)");
  await subjective.fill("Chief complaint: pain on the lower left for two days.");
  await subjective.blur();
  await saved(page, "Subjective saved.");
  const plan = page.getByLabel("Plan (required by the template)");
  await plan.fill("Endodontic treatment, tooth 36.");
  await saved(page, "Plan saved."); // the autosave, with no click and no blur
  await page.getByLabel("Add a note section").selectOption({ label: "Progress note" });
  await page.getByRole("button", { name: "Add section" }).click();
  await page.getByLabel("Progress note", { exact: true }).fill("Patient comfortable after anaesthesia.");
  await page.getByLabel("Progress note", { exact: true }).blur();
  await saved(page, "Progress note saved.");

  // vitals: a wrong reading is voided with a reason, never edited
  await page.getByLabel("Systolic (mmHg)").fill("190");
  await page.getByLabel("Diastolic (mmHg)").fill("110");
  await page.getByLabel("Pulse (beats per minute)").fill("96");
  await page.getByRole("button", { name: "Record vital signs" }).click();
  await expect(page.getByText(/BP 190\/110 mmHg · Pulse 96 bpm/)).toBeVisible();
  await page.getByRole("button", { name: /^Void the reading measured/ }).click();
  await page.getByLabel("Why is this reading being voided?").fill("Typed into the wrong patient");
  await page.getByRole("button", { name: "Void reading" }).click();
  await expect(page.getByText("Voided", { exact: true })).toBeVisible();
  await page.getByLabel("Systolic (mmHg)").fill("122");
  await page.getByLabel("Diastolic (mmHg)").fill("78");
  await page.getByLabel("Pulse (beats per minute)").fill("68");
  await page.getByLabel("Temperature (°C)").fill("36.8");
  await page.getByRole("button", { name: "Record vital signs" }).click();
  await expect(page.getByText(/BP 122\/78 mmHg · Pulse 68 bpm · Temperature 36\.8 °C/)).toBeVisible();
  await shot(page, "08-notes-and-vitals.png");

  const e = await encounter(page, ids.enc1);
  expect(e.notes.map((n: any) => n.section)).toEqual(["Subjective", "Objective", "Assessment", "Plan", "Progress", "Treatment"]); // the template's five plus the progress note added by hand
  expect(e.notes.find((n: any) => n.section === "Plan").body).toBe("Endodontic treatment, tooth 36.");
  expect(e.vitals.map((v: any) => [v.systolicMmHg, v.isVoided])).toEqual([[190, true], [122, false]]);
  expect(e.vitals[0].voidReason).toBe("Typed into the wrong patient");
  expect(e.vitals.every((v: any) => v.recordedByName === NAMES.Dentist)).toBe(true);
  expect(e.missingNotes).toEqual([]);
  expect(e.readyToSign).toBe(true);
  await page.reload();
  await expect(page.getByLabel("Plan (required by the template)")).toHaveValue("Endodontic treatment, tooth 36.");
  evidence.encounter = { templateApplied: true, signBlockedUntilRequiredNotesWritten: ["note:Plan", "note:Subjective"], notes: e.notes.map((n: any) => n.section), vitals: e.vitals.map((v: any) => ({ bp: `${v.systolicMmHg}/${v.diastolicMmHg}`, voided: v.isVoided, by: v.recordedByName })) };

  await scanBothThemes(page, "encounter-notes-and-vitals");
  await page.getByRole("button", { name: "Review and finalize" }).click();
  await scanBothThemes(page, "signing-review-ready");
});

test("SIGNING: a signed note is locked (screen and API) until it is unsigned or finalized, and the signature is attributed", async () => {
  const page = sessions.Dentist.page;
  await expect(page.getByRole("button", { name: "Sign note" })).toBeEnabled();
  await page.getByRole("button", { name: "Sign note" }).click();
  await expect(page.getByText("Signed - awaiting finalize")).toBeVisible();
  await expect(page.getByText(new RegExp(`Signed by ${NAMES.Dentist} on`))).toBeVisible();
  await expect(page.getByRole("textbox")).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Record vital signs|Use template|Add section|Add medication|Reviewed - none reported/ })).toHaveCount(0);
  await shot(page, "09-signed-locked.png");
  await scanBothThemes(page, "signed-locked");

  const signed = await encounter(page, ids.enc1);
  expect([signed.status, signed.isSigned, signed.signedByName]).toEqual(["Draft", true, NAMES.Dentist]);
  const attempts = [
    await api(page, "put", `/api/encounters/${ids.enc1}/notes/Plan`, { body: "Changed while signed", rowVersion: signed.rowVersion }),
    await api(page, "post", `/api/encounters/${ids.enc1}/vitals`, { pulseBpm: 80, rowVersion: signed.rowVersion }, { "Idempotency-Key": `late-${stamp}` }),
    await api(page, "post", `/api/encounters/${ids.enc1}/entries`, { kind: "Allergy", name: "Added while signed", rowVersion: signed.rowVersion }),
  ];
  expect(attempts.map((a) => [a.status, a.body.error])).toEqual([[409, "encounter_signed"], [409, "encounter_signed"], [409, "encounter_signed"]]);
  expect((await encounter(page, ids.enc1)).rowVersion).toBe(signed.rowVersion);

  // unsign (the hygienist may), edit, sign again
  const hyg = sessions.Hygienist.page;
  await hyg.goto(`/patients/${ids.ann}/clinical/${ids.enc1}`);
  await hyg.getByRole("button", { name: "Unsign to keep editing" }).click();
  await saved(hyg, "Signature withdrawn.");
  await expect(hyg.getByLabel("Plan (required by the template)")).toBeVisible();
  await hyg.getByLabel("Treatment note", { exact: true }).fill("Access cavity prepared, canals located."); // the template included it
  await hyg.getByLabel("Treatment note", { exact: true }).blur();
  await saved(hyg, "Treatment note saved.");
  await hyg.getByRole("button", { name: "Review and finalize" }).click();
  await hyg.getByRole("button", { name: "Sign note" }).click();
  await expect(hyg.getByText("Signed - awaiting finalize")).toBeVisible();

  // keyboard only: finalize with the keyboard
  const dentistAgain = sessions.Dentist.page;
  await dentistAgain.goto(`/patients/${ids.ann}/clinical/${ids.enc1}`);
  await expect(dentistAgain.getByText("Signed - awaiting finalize")).toBeVisible();
  await dentistAgain.getByRole("button", { name: "Review and finalize" }).focus();
  await dentistAgain.keyboard.press("Enter");
  await dentistAgain.getByRole("button", { name: "Finalize encounter" }).focus();
  await dentistAgain.keyboard.press("Enter");
  await expect(dentistAgain.getByText("Finalized", { exact: true }).first()).toBeVisible();
  const done = await encounter(dentistAgain, ids.enc1);
  expect([done.status, done.signedByName, done.finalizedByName]).toEqual(["Finalized", NAMES.Hygienist, NAMES.Dentist]); // who signed and who finalized are both kept
  evidence.signing = { lockedWhileSigned: ["encounter_signed", "encounter_signed", "encounter_signed"], unsignedByAnotherClinician: true, signedBy: done.signedByName, finalizedBy: done.finalizedByName, keyboardOnlyFinalize: true };
});

test("FINALIZED + AMENDMENT: a finalized note cannot be overwritten; an amendment preserves the original, who wrote it, when and what it amends", async () => {
  const page = sessions.Dentist.page;
  const original = await encounter(page, ids.enc1);
  const attempts = [
    await api(page, "put", `/api/encounters/${ids.enc1}/notes/Plan`, { body: "Overwritten", rowVersion: original.rowVersion }),
    await api(page, "put", `/api/encounters/${ids.enc1}/notes/Assessment`, { body: "New", rowVersion: original.rowVersion }),
    await api(page, "post", `/api/encounters/${ids.enc1}/vitals`, { pulseBpm: 60, rowVersion: original.rowVersion }, { "Idempotency-Key": `final-${stamp}` }),
    await api(page, "post", `/api/encounters/${ids.enc1}/unsign`, { rowVersion: original.rowVersion }),
    await api(page, "post", `/api/encounters/${ids.enc1}/template`, { templateId: ids.template, rowVersion: original.rowVersion }),
  ];
  expect(attempts.map((a) => a.body.error)).toEqual(["encounter_finalized", "encounter_finalized", "encounter_finalized", "encounter_finalized", "encounter_finalized"]);

  await page.goto(`/patients/${ids.ann}/clinical/${ids.enc1}`);
  await expect(page.getByRole("heading", { name: "Addenda" })).toBeVisible();
  await expect(page.getByText(new RegExp(`Original finalized .* by ${NAMES.Dentist}\\.`))).toBeVisible();
  await expect(page.getByText("Endodontic treatment, tooth 36.")).toBeVisible();
  await page.getByLabel("What does this amend? (optional)").selectOption({ label: "Plan" });
  await page.getByLabel("New addendum").fill("Plan changed: refer to endodontics instead.");
  await page.getByRole("button", { name: "Add addendum" }).click();
  await expect(page.getByText("Plan changed: refer to endodontics instead.")).toBeVisible();
  await expect(page.getByText("Amends: Plan")).toBeVisible();
  await expect(page.getByText(new RegExp(`^Added .* by ${NAMES.Dentist}$`))).toBeVisible();
  await shot(page, "10-amendment-timeline.png");

  const after = await encounter(page, ids.enc1);
  expect(after.notes).toEqual(original.notes);                    // every note exactly as finalized
  expect(after.vitals).toEqual(original.vitals);
  expect(after.rowVersion).toBe(original.rowVersion);
  expect(after.addenda.map((a: any) => [a.section, a.createdByName, a.text])).toEqual([["Plan", NAMES.Dentist, "Plan changed: refer to endodontics instead."]]);
  expect(after.addenda[0].createdAtUtc).toBeTruthy();
  evidence.amendment = { finalizedRefused: attempts.map((a) => a.body.error), originalUnchanged: true, rowVersionUnchanged: true, addendum: { section: "Plan", by: after.addenda[0].createdByName } };
  await scanBothThemes(page, "finalized-amendment-timeline");

  // a response lost after the server stored the amendment: the typed text and section stay, and the retry adds nothing more
  let lost = 0;
  await page.route("**/api/encounters/*/addenda", async (route) => {
    if (route.request().method() === "POST" && lost === 0) {
      lost++;
      await route.fetch();
      await route.abort("failed");
    } else await route.continue();
  });
  await page.getByLabel("What does this amend? (optional)").selectOption({ label: "Vital signs" });
  await page.getByLabel("New addendum").fill("Blood pressure re-checked and confirmed.");
  await page.getByRole("button", { name: "Add addendum" }).click();
  await expect(page.getByText(/not certain the addendum was saved/)).toBeVisible();
  await expect(page.getByLabel("New addendum")).toHaveValue("Blood pressure re-checked and confirmed.");
  await expect(page.getByLabel("What does this amend? (optional)")).toHaveValue("Vitals");
  await page.getByRole("button", { name: "Add addendum" }).click();
  await expect(page.getByText("Amends: Vital signs")).toBeVisible();
  await page.unroute("**/api/encounters/*/addenda");
  expect((await encounter(page, ids.enc1)).addenda.filter((a: any) => a.text.startsWith("Blood pressure"))).toHaveLength(1);
  evidence.amendmentFailurePath = { responseLostAfterStore: true, typedTextAndSectionKept: true, addedExactlyOnce: true };
});

test("CONCURRENCY: a stale note save is refused with the conflict banner and the typed note survives the reload", async () => {
  const first = sessions.Dentist.page;
  const second = sessions.Hygienist.page;
  ids.enc2 = await startEncounter(first);
  await second.goto(`/patients/${ids.ann}/clinical/${ids.enc2}`);
  await expect(second.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();
  await first.getByLabel("Add a note section").selectOption({ label: "Progress note" });
  await first.getByRole("button", { name: "Add section" }).click();
  await first.getByLabel("Progress note", { exact: true }).fill("First clinician's note");
  await first.getByLabel("Progress note", { exact: true }).blur();
  await saved(first, "Progress note saved.");

  await second.getByLabel("Add a note section").selectOption({ label: "Treatment note" });
  await second.getByRole("button", { name: "Add section" }).click();
  await second.getByLabel("Treatment note", { exact: true }).fill("Second clinician's note");
  await second.getByLabel("Treatment note", { exact: true }).blur();
  await expect(second.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(second, "11-note-stale.png");
  expect((await encounter(first, ids.enc2)).notes.map((n: any) => n.section)).toEqual(["Progress"]); // nothing was overwritten

  await second.getByRole("button", { name: /reload/i }).click();
  await expect(second.getByText("Someone else changed this while you were editing")).toHaveCount(0);
  await expect(second.getByLabel("Treatment note", { exact: true })).toHaveValue("Second clinician's note"); // typed text survived
  await expect(second.getByLabel("Progress note", { exact: true })).toHaveValue("First clinician's note");   // and the other change is now shown
  await second.getByRole("button", { name: "Save treatment note now" }).click();
  await saved(second, "Treatment note saved."); // the indicator, not the other box's "Saved." text
  expect((await encounter(first, ids.enc2)).notes.map((n: any) => n.section)).toEqual(["Progress", "Treatment"]);
  evidence.noteConcurrency = { staleSaveRefused: true, typedTextSurvived: true, savedAfterReload: true };
});

test("ACCESS: front desk cannot read or write any of it; an assistant reads but gets no controls", async () => {
  const desk = sessions.FrontDesk.page;
  const probes = await Promise.all([
    api(desk, "get", `/api/patients/${ids.ann}/clinical-record`),
    api(desk, "get", "/api/clinical/templates"),
    api(desk, "put", `/api/encounters/${ids.enc2}/notes/Plan`, { body: "x", rowVersion: "AAAA" }),
    api(desk, "post", `/api/patients/${ids.ann}/clinical-record/items`, { kind: "Allergy", name: "Nope" }),
  ]);
  expect(probes.map((p) => p.status)).toEqual([403, 403, 403, 403]);

  const assistant = sessions.Assistant.page;
  await assistant.goto(`/patients/${ids.ann}/clinical`);
  await expect(assistant.getByRole("heading", { name: "Clinical record" })).toBeVisible();
  await expect(assistant.getByText("Penicillin")).toBeVisible();
  await expect(assistant.getByRole("button", { name: /to the record|Confirm|None known|Correct details|Change status|Entered in error|Start an encounter/ })).toHaveCount(0);
  await assistant.goto(`/patients/${ids.ann}/clinical/${ids.enc1}`);
  await expect(assistant.getByText("Endodontic treatment, tooth 36.")).toBeVisible();
  await expect(assistant.getByRole("textbox", { name: /Plan|Treatment|Subjective/ })).toHaveCount(0);
  await expect(assistant.getByRole("button", { name: /Record vital signs|Sign note|Unsign|Void/ })).toHaveCount(0);
  expect((await api(assistant, "post", `/api/encounters/${ids.enc2}/sign`, { rowVersion: "AAAA" })).status).toBe(403);
  expect((await api(assistant, "get", `/api/patients/${ids.ann}/clinical-record`)).status).toBe(200);
  evidence.access = { frontDesk: probes.map((p) => p.status), assistant: { read: 200, write: 403, controls: 0 } };
});

test("TRUST: every save, sign, finalize and amend is in the audit log with the user and a time, and no clinical content is in it", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=500")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const clinical = entries.filter((e) => e.eventType.startsWith("Encounter") || e.eventType.startsWith("ClinicalRecord") || e.eventType.startsWith("ClinicalSection") || e.eventType.startsWith("ClinicalTemplate"));
  const by = (t: string) => clinical.filter((e) => e.eventType === t);
  expect(by("ClinicalRecordItemAdded").length).toBeGreaterThanOrEqual(4);
  expect(by("ClinicalRecordItemStatusChanged").length).toBeGreaterThanOrEqual(3);
  expect(by("ClinicalRecordItemChanged").length).toBeGreaterThanOrEqual(2);
  expect(by("ClinicalSectionReviewed").length).toBeGreaterThanOrEqual(3);
  expect(by("ClinicalTemplateCreated").length).toBe(1);
  expect(by("EncounterNoteSaved").length).toBeGreaterThanOrEqual(5);
  expect(by("EncounterTemplateApplied").length).toBe(1);
  expect(by("EncounterVitalsRecorded").length).toBe(2);
  expect(by("EncounterVitalsVoided").length).toBe(1);
  expect(by("EncounterSigned").length).toBe(2);
  expect(by("EncounterUnsigned").length).toBe(1);
  expect(by("EncounterFinalized").length).toBe(1);
  expect(by("EncounterAddendumAdded").length).toBe(2);
  expect(clinical.every((e) => e.performedByUserAccountId && e.timestampUtc)).toBe(true);
  const text = clinical.map((e) => e.details).join(" ");
  for (const secret of ["Penicillin", "Lisinopril", "Hypertension", "Hives", "Chief complaint", "Endodontic", "tooth 36", "Tolerated a challenge", "wrong patient", "190", "110", "Plan changed", "Access cavity", "Stable on"]) expect(text, `audit must not contain ${secret}`).not.toContain(secret);
  evidence.audit = Object.fromEntries(["ClinicalRecordItemAdded", "ClinicalRecordItemChanged", "ClinicalRecordItemStatusChanged", "ClinicalSectionReviewed", "ClinicalTemplateCreated", "EncounterNoteSaved", "EncounterTemplateApplied", "EncounterVitalsRecorded", "EncounterVitalsVoided", "EncounterSigned", "EncounterUnsigned", "EncounterFinalized", "EncounterAddendumAdded"].map((t) => [t, by(t).length]));
});
