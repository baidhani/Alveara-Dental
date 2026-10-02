import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-003-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Like the other real-backend specs it is excluded from the default mocked run (playwright.config.ts) and run with
// playwright.workspace.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as a front-desk user would: duplicate warning without a silent merge, household and
// guarantor held independently, active/inactive preserved, a concurrent edit that cannot overwrite, keyboard-first
// registration, and switching between two patients with no stale identity - then scans the screens with axe in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.WORKSPACE_E2E_OUT ?? path.join(process.cwd(), "workspace-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

let adminContext: BrowserContext;
let admin: Page;
let deskContext: BrowserContext;
let desk: Page;
let deskUsername: string;
const ids: Record<string, string> = {};

async function csrf(page: Page) {
  return (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
}

async function createUser(role: string, username: string) {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: "front-desk-pass-1!" } });
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
  await page.waitForTimeout(300); // let finite colour transitions finish so axe samples settled colours
}

/** Scans the page as it currently is, in both themes. */
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

async function fillPatient(page: Page, p: { first: string; last: string; dob: string; phone: string; email?: string }) {
  await page.getByLabel("First name *").fill(p.first);
  await page.getByLabel("Last name *").fill(p.last);
  await page.getByLabel("Date of birth *").fill(p.dob);
  await page.getByLabel("Phone *").fill(p.phone);
  if (p.email) await page.getByLabel("Email").fill(p.email);
  await page.getByLabel("Address *").fill("1 Main St");
  await page.getByLabel("City *").fill("Austin");
  await page.getByLabel("State *").fill("TX");
  await page.getByLabel("Postal code *").fill("78701");
}

async function registerViaUi(p: { first: string; last: string; dob: string; phone: string; email?: string }, key: string) {
  await desk.goto("/patients/register");
  await fillPatient(desk, p);
  await desk.getByRole("button", { name: "Register patient" }).click();
  const done = desk.getByRole("status", { name: "Registration complete" });
  await expect(done).toContainText(`${p.first} ${p.last}`);
  const id = (await done.getByText(/^Patient ID:/).innerText()).replace("Patient ID:", "").trim();
  ids[key] = id;
  return id;
}

const header = (page: Page) => page.getByRole("region", { name: "Patient context" });

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  const stamp = Date.now();
  deskUsername = `desk-${stamp}`;
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  await createUser("FrontDesk", deskUsername);
  await createUser("FrontDesk", `desk2-${stamp}`);
  await createUser("Dentist", `dentist-${stamp}`);
  evidence.users = { frontDesk: deskUsername, secondFrontDesk: `desk2-${stamp}`, dentist: `dentist-${stamp}` };
  (evidence as Record<string, unknown>).stamp = stamp;
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, deskUsername, "front-desk-pass-1!");
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "patient-workspace-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  await deskContext.close();
});

test("registers a family and a guarantor through the form (STORY-003's flow, now with the workspace links)", async () => {
  await registerViaUi({ first: "Mia", last: "Lee", dob: "1980-01-01", phone: "555-010-0001", email: "mia@example.test" }, "mia");
  await expect(desk.getByRole("link", { name: "Open patient workspace" })).toHaveAttribute("href", `/patients/${ids.mia}`);
  await expect(desk.getByRole("link", { name: "Add household or guarantor" })).toHaveAttribute("href", `/patients/${ids.mia}/household`);
  await registerViaUi({ first: "Cal", last: "Lee", dob: "2015-01-01", phone: "555-010-0001" }, "cal"); // shares Mia's phone: a family, not a duplicate
  await registerViaUi({ first: "Gus", last: "Hale", dob: "1950-01-01", phone: "555-010-0003" }, "gus");
  await registerViaUi({ first: "Ann", last: "Park", dob: "1985-03-09", phone: "555-010-0100" }, "ann");
  evidence.relativesSharingAPhoneWereNotWarned = true;
});

test("DUPLICATE WARNING: a likely duplicate is shown side by side and nothing is merged; an exact one is blocked", async () => {
  await desk.goto("/patients/register");
  await fillPatient(desk, { first: "Anna", last: "Park", dob: "1985-03-09", phone: "555-777-0000" }); // same birth date + last name as Ann Park
  await desk.getByRole("button", { name: "Register patient" }).click();

  const panel = desk.getByRole("region", { name: "These patients may be the same person" });
  await expect(panel).toBeVisible();
  await expect(panel.getByRole("columnheader", { name: "Existing: Ann Park" })).toBeVisible();
  await expect(panel).toContainText("Same last name and date of birth");
  await expect(desk.getByRole("status", { name: "Registration complete" })).toHaveCount(0); // not registered yet
  await shot(desk, "10-duplicate-comparison.png");
  await scanBothThemes(desk, "duplicate-comparison");

  // choose to register anyway: a human decided it is a different person
  await panel.getByRole("button", { name: /Register anyway/ }).click();
  const done = desk.getByRole("status", { name: "Registration complete" });
  await expect(done).toContainText("Anna Park");
  ids.anna = (await done.getByText(/^Patient ID:/).innerText()).replace("Patient ID:", "").trim();

  // the existing patient was not touched or merged
  const ann = await (await desk.request.get(`/api/patients/${ids.ann}`)).json();
  expect(ann).toMatchObject({ firstName: "Ann", lastName: "Park", phone: "555-010-0100" });
  expect(ids.anna).not.toBe(ids.ann);

  // an exact match (same name and birth date) is blocked outright
  await desk.getByRole("button", { name: "Register another patient" }).click();
  await fillPatient(desk, { first: "ann", last: "PARK", dob: "1985-03-09", phone: "555-999-9999" });
  await desk.getByRole("button", { name: "Register patient" }).click();
  const blocked = desk.getByRole("region", { name: "This patient is already registered" });
  await expect(blocked).toBeVisible();
  await expect(blocked.getByRole("button", { name: /Register anyway/ })).toHaveCount(0);
  await expect(desk.getByRole("alert")).toContainText(`Existing patient ID: ${ids.ann}`);
  evidence.duplicateHandling = { likelyWarnedThenOverridden: true, existingRecordUntouched: true, exactBlocked: true };
});

test("SEARCH: by name words, birth date and phone (any formatting); inactive hidden unless asked", async () => {
  await desk.goto("/patients");
  await desk.getByLabel("Search patients").fill("lee");
  await expect(desk.getByRole("link", { name: "Mia Lee" })).toBeVisible();
  await expect(desk.getByRole("link", { name: "Cal Lee" })).toBeVisible();
  await expect(desk.getByRole("link", { name: "Gus Hale" })).toHaveCount(0);

  await desk.getByLabel("Search patients").fill("1950-01-01");
  await expect(desk.getByRole("link", { name: "Gus Hale" })).toBeVisible();
  await expect(desk.getByRole("link", { name: "Mia Lee" })).toHaveCount(0);

  await desk.getByLabel("Search patients").fill("(555) 010 0003");
  await expect(desk.getByRole("link", { name: "Gus Hale" })).toBeVisible();
  await shot(desk, "11-search.png");
  await scanBothThemes(desk, "search");
  evidence.search = "name, birth date and differently formatted phone all found the right patient";
});

test("WORKSPACE: the header shows the identity and every screen keeps it; edits are saved with history", async () => {
  await desk.goto("/patients");
  await desk.getByLabel("Search patients").fill("mia");
  await desk.getByRole("link", { name: "Mia Lee" }).click();

  await expect(header(desk)).toContainText("Mia Lee");
  await expect(header(desk)).toContainText("DOB 1980-01-01");
  await expect(header(desk)).toContainText("555-010-0001");
  await expect(desk.getByLabel("First name *")).toHaveValue("Mia");
  await desk.getByRole("link", { name: "History" }).click();
  await expect(header(desk)).toContainText("Mia Lee"); // the patient stays in context across tabs
  await desk.getByRole("link", { name: "Details" }).click();

  await desk.getByLabel("City *").fill("Dallas");
  await desk.getByRole("button", { name: "Save changes" }).click();
  await expect(desk.getByLabel("City *")).toHaveValue("Dallas");
  await expect(desk.getByText("Unsaved changes")).toHaveCount(0);

  await desk.getByRole("link", { name: "History" }).click();
  const row = desk.getByRole("row").filter({ hasText: "City" });
  await expect(row).toContainText("Austin");
  await expect(row).toContainText("Dallas");
  await shot(desk, "12-history.png");
  await scanBothThemes(desk, "history");
  evidence.edit = "city Austin -> Dallas saved, recorded in history";
});

test("CONCURRENCY: a stale edit cannot silently overwrite another user's change", async ({ browser }) => {
  await desk.goto(`/patients/${ids.cal}`);
  await expect(desk.getByLabel("City *")).toHaveValue("Austin");

  // a second front-desk user saves a change to the same patient first
  const ctx2 = await browser.newContext();
  const other = await ctx2.newPage();
  await signIn(other, `desk2-${evidence.stamp}`, "front-desk-pass-1!");
  await other.goto(`/patients/${ids.cal}`);
  await other.getByLabel("City *").fill("Houston");
  await other.getByRole("button", { name: "Save changes" }).click();
  await expect(other.getByRole("status").filter({ hasText: "Patient details saved." })).toBeVisible(); // wait for the save to land before closing their session
  await ctx2.close();

  // the first user, still holding the old version, tries to save
  await desk.getByLabel("City *").fill("Waco");
  await desk.getByRole("button", { name: "Save changes" }).click();
  await expect(desk.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(desk, "13-conflict.png");
  await scanBothThemes(desk, "conflict-banner");

  const stored = await (await desk.request.get(`/api/patients/${ids.cal}`)).json();
  expect(stored.city).toBe("Houston"); // the other user's change survived

  await desk.getByRole("button", { name: "Reload current version" }).click();
  await expect(desk.getByLabel("City *")).toHaveValue("Houston");
  evidence.concurrency = { staleSaveRefused: true, otherUsersChangeKept: true, reloadShowsCurrent: true };
});

test("HOUSEHOLD AND GUARANTOR are independent: Cal is in Mia's household, but Gus (outside it) is Cal's guarantor", async () => {
  await desk.goto(`/patients/${ids.mia}/household`);
  await expect(desk.getByText("This patient is not in a household.")).toBeVisible();

  // add Cal to Mia's household
  await desk.getByLabel("Choose a patient to add").fill("cal");
  await desk.getByRole("button", { name: /Cal Lee/ }).click();
  await desk.getByLabel("Their relationship to this household").selectOption("Child");
  await desk.getByRole("button", { name: "Add to household" }).click();
  const members = desk.getByRole("table", { name: "Household members" });
  await expect(members).toContainText("Cal Lee");
  await expect(members).toContainText("Mia Lee (this patient)");

  // Gus, who is not in the household, becomes Cal's guarantor
  await desk.goto(`/patients/${ids.cal}/household`);
  await expect(header(desk)).toContainText("Cal Lee");
  await desk.getByLabel("Choose a guarantor").fill("gus");
  await desk.getByRole("button", { name: /Gus Hale/ }).click();
  await desk.getByRole("button", { name: "Set guarantor" }).click();
  await expect(desk.getByTestId("current-guarantor")).toContainText("Gus Hale is responsible for this patient.");
  await expect(header(desk)).toContainText("Guarantor: Gus Hale");

  // independence, read back from the API: Cal's household has Mia and Cal but not Gus
  const cal = await (await desk.request.get(`/api/patients/${ids.cal}`)).json();
  expect(cal.household.members.map((m: { displayName: string }) => m.displayName).sort()).toEqual(["Cal Lee", "Mia Lee"]);
  expect(cal.guarantor.displayName).toBe("Gus Hale");
  const gus = await (await desk.request.get(`/api/patients/${ids.gus}`)).json();
  expect(gus.household).toBeNull();
  expect(gus.guaranteeFor.map((p: { displayName: string }) => p.displayName)).toEqual(["Cal Lee"]);
  const mia = await (await desk.request.get(`/api/patients/${ids.mia}`)).json();
  expect(mia.guarantor).toBeNull(); // Mia is in the household but responsible for herself
  await shot(desk, "14-household-guarantor.png");
  await scanBothThemes(desk, "household-guarantor");
  evidence.independence = { calHousehold: ["Cal Lee", "Mia Lee"], calGuarantor: "Gus Hale", gusHousehold: null, miaGuarantor: null };
});

test("INVALID RELATIONSHIPS are refused with the server's reason shown next to the control", async () => {
  // a guarantor must be responsible for themselves: Cal has a guarantor, so Cal cannot be Mia's
  await desk.goto(`/patients/${ids.mia}/household`);
  await desk.getByLabel("Choose a guarantor").fill("cal");
  await desk.getByRole("button", { name: /Cal Lee/ }).click();
  await desk.getByRole("button", { name: "Set guarantor" }).click();
  await expect(desk.getByRole("alert")).toContainText("guarantor of their own");
  await expect(desk.getByTestId("current-guarantor")).toContainText("responsible for themselves");

  // a guarantor of others cannot be inactivated
  desk.once("dialog", (d) => d.accept());
  await desk.goto(`/patients/${ids.gus}`);
  await desk.getByRole("button", { name: "Inactivate patient" }).click();
  await expect(desk.getByRole("alert")).toContainText("guarantor for 1 active patient");
  await expect(header(desk)).not.toContainText("Inactive");
  evidence.invalidRelationships = ["guarantor_with_own_guarantor_refused", "guarantor_in_use_inactivation_refused"];
});

test("ACTIVE/INACTIVE: an inactivated patient is kept, hidden from default search, flagged in the header, and can be reactivated", async () => {
  desk.once("dialog", (d) => d.accept());
  await desk.goto(`/patients/${ids.ann}`);
  await desk.getByRole("button", { name: "Inactivate patient" }).click();
  await expect(header(desk)).toContainText("Inactive");

  // later edits do not reactivate them
  await desk.getByLabel("City *").fill("Plano");
  await desk.getByRole("button", { name: "Save changes" }).click();
  await expect(desk.getByLabel("City *")).toHaveValue("Plano");
  await expect(header(desk)).toContainText("Inactive");

  await desk.goto("/patients");
  await desk.getByLabel("Search patients").fill("park");
  await expect(desk.getByRole("link", { name: "Anna Park" })).toBeVisible();
  await expect(desk.getByRole("link", { name: "Ann Park" })).toHaveCount(0);
  await desk.getByLabel("Include inactive patients").check();
  await expect(desk.getByRole("link", { name: "Ann Park" })).toBeVisible();
  await shot(desk, "15-inactive-search.png");

  await desk.goto(`/patients/${ids.ann}`);
  await desk.getByRole("button", { name: "Reactivate patient" }).click();
  await expect(header(desk)).not.toContainText("Inactive");
  evidence.activeState = "inactivated, preserved through an edit, hidden then shown by the filter, reactivated";
});

test("PATIENT SWITCH: moving from Mia to Cal on a slow connection never shows Mia's identity or data under Cal", async () => {
  await desk.goto(`/patients/${ids.mia}/household`);
  await expect(header(desk)).toContainText("Mia Lee");
  await expect(desk.getByRole("table", { name: "Household members" })).toBeVisible();

  // make Cal's record slow to arrive
  await desk.route(`**/api/patients/${ids.cal}`, async (route) => {
    await new Promise((r) => setTimeout(r, 1500));
    await route.continue();
  });
  await desk.getByRole("table", { name: "Household members" }).getByRole("link", { name: "Cal Lee" }).click();

  // while Cal loads, nothing of Mia's is on screen
  await expect(header(desk)).toContainText("Loading patient…");
  await expect(header(desk)).not.toContainText("Mia");
  await expect(desk.locator("main")).not.toContainText("Mia Lee");
  await expect(desk.locator("main")).not.toContainText("555-010-0001");
  await shot(desk, "16-switch-loading.png");

  await expect(header(desk)).toContainText("Cal Lee", { timeout: 10_000 });
  await expect(header(desk)).not.toContainText("Mia");
  await expect(desk.locator("main")).not.toContainText("Mia Lee (this patient)");
  await desk.unroute(`**/api/patients/${ids.cal}`);

  // and Close patient drops the context entirely
  await header(desk).getByRole("button", { name: "Close patient" }).click();
  await expect(header(desk)).toContainText("No patient selected");
  evidence.patientSwitch = { duringLoadMiaVisible: false, afterLoadCalShown: true };
});

test("CONFIGURABLE REQUIREMENTS: a practice manager can require email; the form marks it, the server enforces it, and relaxing it restores the old behavior", async () => {
  await admin.goto("/admin/patient-registration");
  await admin.getByLabel("An email address").check();
  await admin.getByRole("button", { name: "Save requirements" }).click();
  await expect(admin.getByText("Registration requirements saved.")).toBeVisible();
  await shot(admin, "17-registration-settings.png");
  await scanBothThemes(admin, "registration-settings");

  // the front-desk form now marks Email as required and prompts for it
  await desk.goto("/patients/register");
  await expect(desk.getByLabel("Email *")).toBeVisible();
  await fillPatient(desk, { first: "Req", last: "Test", dob: "1999-09-09", phone: "555-050-0500" });
  await desk.getByRole("button", { name: "Register patient" }).click();
  await expect(desk.getByText("Email is required.")).toBeVisible();
  await expect(desk.getByLabel("Email *")).toBeFocused();
  await expect(desk.getByRole("status", { name: "Registration complete" })).toHaveCount(0);
  await shot(desk, "18-email-required-prompt.png");

  // the SERVER enforces it independently of the form: a direct call without an email is refused with the same message
  const token = await csrf(desk);
  const direct = await desk.request.post("/api/patients", {
    headers: { "X-CSRF-Token": token, "Idempotency-Key": "k-direct" },
    data: { firstName: "Req", lastName: "Test", dateOfBirth: "1999-09-09", phone: "555-050-0500", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" },
  });
  expect(direct.status()).toBe(400);
  expect((await direct.json()).fieldErrors.email).toBe("Email is required.");

  // an existing patient without an email is asked for one the next time they are edited
  await desk.goto(`/patients/${ids.gus}`);
  await desk.getByLabel("City *").fill("Plano");
  await desk.getByRole("button", { name: "Save changes" }).click();
  await expect(desk.getByText("Email is required.")).toBeVisible();
  await desk.getByLabel("Email *").fill("gus@example.test");
  await desk.getByRole("button", { name: "Save changes" }).click();
  await expect(desk.getByRole("status").filter({ hasText: "Patient details saved." })).toBeVisible();

  // relaxing the requirement restores STORY-003's behavior
  await admin.goto("/admin/patient-registration");
  await admin.getByLabel("An email address").uncheck();
  await admin.getByRole("button", { name: "Save requirements" }).click();
  await expect(admin.getByText("Registration requirements saved.")).toBeVisible();
  await desk.goto("/patients/register");
  await expect(desk.getByLabel("Email")).toBeVisible();
  await expect(desk.getByLabel("Email *")).toHaveCount(0);
  evidence.configurableRequirements = { formMarkedRequired: true, serverRefusedDirectCall: true, existingPatientPromptedOnEdit: true, relaxedRestoresOptional: true };
});

test("KEYBOARD-FIRST: a patient can be registered and found without a mouse", async () => {
  await desk.goto("/patients/register");
  await desk.getByLabel("First name *").focus();
  // One field = type then Tab. Chromium's date input has an extra tab stop (its calendar button) after the year segment.
  const keys = ["Kay", "", "Board", "12311990", "", "", "555-020-0200", "", "2 Elm St", "", "Dallas", "TX", "75001"];
  for (const text of keys) {
    if (text) await desk.keyboard.type(text);
    await desk.keyboard.press("Tab");
  }
  await desk.keyboard.press("Enter");
  await expect(desk.getByRole("status", { name: "Registration complete" })).toContainText("Kay Board");

  await desk.goto("/patients");
  await expect(desk.getByLabel("Search patients")).toBeFocused(); // search is ready to type into on arrival
  await desk.keyboard.type("board");
  await expect(desk.getByRole("link", { name: "Kay Board" })).toBeVisible();
  await desk.keyboard.press("Tab"); // include-inactive checkbox
  await desk.keyboard.press("Tab"); // first result
  await expect(desk.getByRole("link", { name: "Kay Board" })).toBeFocused();
  await desk.keyboard.press("Enter");
  await expect(header(desk)).toContainText("Kay Board");
  evidence.keyboardFirst = "registered, searched and opened a patient using only the keyboard";
});

test("AUDIT: registrations, edits, status, household and guarantor changes are all in the audit trail with user and time, no patient details", async () => {
  const entries = (await (await admin.request.get("/api/auth/audit-log?take=200")).json()) as { eventType: string; performedByUserAccountId: string | null; timestampUtc: string; details: string }[];
  const byType = (t: string) => entries.filter((e) => e.eventType === t);
  expect(byType("PatientRegistered").length).toBeGreaterThanOrEqual(6);
  for (const t of ["PatientUpdated", "PatientStatusChanged", "PatientHouseholdChanged", "PatientGuarantorChanged", "PatientRegistrationSettingsChanged"]) expect(byType(t).length, t).toBeGreaterThan(0);
  for (const e of entries.filter((x) => x.eventType.startsWith("Patient"))) {
    expect(e.performedByUserAccountId).toBeTruthy();
    expect(Date.now() - Date.parse(e.timestampUtc)).toBeLessThan(30 * 60_000);
    expect(e.details).not.toMatch(/Mia|Cal|Gus|Ann|Anna|Kay|Lee|Hale|Park|Board|555-|example\.test|Dallas|Houston|Plano|Waco|gus@/);
  }
  evidence.audit = Object.fromEntries(["PatientRegistered", "PatientUpdated", "PatientStatusChanged", "PatientHouseholdChanged", "PatientGuarantorChanged", "PatientRegistrationSettingsChanged"].map((t) => [t, byType(t).length]));
});

test("PERMISSIONS: a dentist can find and read patients but has no register or edit controls, and the API refuses their writes", async ({ browser }) => {
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  await signIn(page, String((evidence.users as Record<string, string>).dentist), "front-desk-pass-1!");
  await expect(page.getByRole("link", { name: "Patients" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Register Patient" })).toHaveCount(0);

  await page.goto(`/patients/${ids.cal}`);
  await expect(header(page)).toContainText("Cal Lee");
  await expect(page.getByText("You can view this patient but not change them.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
  await expect(page.getByLabel("First name *")).toBeDisabled();
  await page.goto(`/patients/${ids.cal}/household`);
  await expect(page.getByRole("button", { name: "Set guarantor" })).toHaveCount(0);
  await page.goto("/patients");
  await expect(page.getByRole("link", { name: "Register new patient" })).toHaveCount(0);
  await page.goto("/patients/register");
  await expect(page.getByText(/don't have permission/i)).toBeVisible();

  const token = await csrf(page);
  const cal = await (await page.request.get(`/api/patients/${ids.cal}`)).json();
  const put = await page.request.put(`/api/patients/${ids.cal}`, { headers: { "X-CSRF-Token": token }, data: { ...cal, city: "Nope", rowVersion: cal.rowVersion } });
  expect(put.status()).toBe(403);
  const stored = await (await desk.request.get(`/api/patients/${ids.cal}`)).json();
  expect(stored.city).toBe("Houston");
  evidence.dentist = { canSearchAndRead: true, registerControls: false, editControls: false, apiWriteStatus: put.status() };
  await ctx.close();
});

test("ACCESSIBILITY: the registration form, details and not-found states have no critical/serious axe violations in both themes", async () => {
  await desk.goto("/patients/register");
  await expect(desk.getByRole("heading", { name: "Register patient" })).toBeVisible();
  await scanBothThemes(desk, "register-form");

  await desk.goto(`/patients/${ids.cal}`);
  await expect(desk.getByLabel("First name *")).toHaveValue("Cal");
  await scanBothThemes(desk, "workspace-details");

  await desk.goto("/patients/00000000-0000-0000-0000-000000000000");
  await expect(desk.getByText("That patient was not found", { exact: false }).first()).toBeVisible();
  await scanBothThemes(desk, "workspace-not-found");
});
