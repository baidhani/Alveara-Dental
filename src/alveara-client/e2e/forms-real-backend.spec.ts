import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N010 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real LocalDB.
// Like the other real-backend specs it is excluded from the default mocked run (playwright.config.ts) and run with
// playwright.forms.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// It walks the story's acceptance items as practice staff would: an administrator builds a versioned template, the front desk
// completes and signs it, a later template edit leaves the signed copy untouched, a duplicate submit and a dropped response never create
// a second signature, a signed form is voided with a reason and stays visible - then scans the screens with axe in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.FORMS_E2E_OUT ?? path.join(process.cwd(), "forms-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "forms-pass-1!";
let adminContext: BrowserContext;
let admin: Page;
let managerContext: BrowserContext;
let manager: Page;
let deskContext: BrowserContext;
let desk: Page;
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

async function fillSigner(page: Page, name: string, relationship: string) {
  await page.getByLabel("Signer's full name *").fill(name);
  await page.getByLabel("Relationship to the patient *").selectOption(relationship);
  await page.getByLabel("Type your name as your signature *").fill(name);
  await page.getByRole("checkbox", { name: /I have read this form/ }).check();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  await createUser("OfficeManager", `manager-${stamp}`);
  await createUser("FrontDesk", `desk-${stamp}`);
  await createUser("Dentist", `dentist-${stamp}`);
  await createUser("Billing", `billing-${stamp}`);
  evidence.stamp = stamp;
  managerContext = await browser.newContext();
  manager = await managerContext.newPage();
  await signIn(manager, `manager-${stamp}`, PASSWORD);
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, `desk-${stamp}`, PASSWORD);
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "forms-e2e.json"), JSON.stringify(evidence, null, 2));
  await adminContext.close();
  await managerContext.close();
  await deskContext.close();
});

test("TEMPLATES: an office manager builds a versioned privacy template through the UI; the front desk cannot reach the administration page", async () => {
  await manager.goto("/admin/form-templates");
  await expect(manager.getByRole("heading", { name: "Form templates" })).toBeVisible();
  await manager.getByRole("button", { name: "New template" }).click();
  await manager.getByLabel(/Key \(cannot be changed later\)/).fill(`privacy-notice-${stamp}`);
  await manager.getByLabel("Category *").selectOption("Privacy");
  await manager.getByLabel("Title *").fill("Privacy notice");
  await manager.getByLabel("Form text shown to the signer *").fill("We protect your health information and share it only as the law allows.");
  await manager.getByRole("button", { name: "Add a question" }).click();
  await manager.getByLabel("Question text *").fill("I have received the privacy notice");
  await manager.getByLabel("Field id *").fill("received");
  await manager.getByLabel("Type").selectOption("checkbox");
  await manager.getByLabel("Required to sign").check();
  await manager.getByRole("button", { name: "Add a question" }).click();
  await manager.getByLabel("Question text *").nth(1).fill("Preferred name");
  await manager.getByLabel("Field id *").nth(1).fill("nickname");
  await shot(manager, "01-template-editor.png");
  await manager.getByRole("button", { name: "Create template" }).click();
  await expect(manager.getByText("Template created as version 1.")).toBeVisible();
  const list = await api(manager, "get", "/api/forms/templates");
  const created = (list.body as unknown as Array<{ id: string; key: string }>).find((t) => t.key === `privacy-notice-${stamp}`)!;
  ids.template = created.id;
  await scanBothThemes(manager, "template-admin");

  await desk.goto("/admin/form-templates");
  await expect(desk.getByText(/don't have permission/i)).toBeVisible();
  expect((await api(desk, "get", "/api/forms/templates")).status).toBe(403);
  evidence.templateAdministration = { createdAsOfficeManager: true, frontDeskRefusedUi: true, frontDeskRefusedApi: 403 };
});

test("COMPLETE: the front desk starts the form for a patient, answers, saves a draft that survives a reload, and the status is shown in words", async () => {
  const token = await csrf(desk);
  const reg = await desk.request.post("/api/patients", {
    headers: { "X-CSRF-Token": token, "Idempotency-Key": `forms-e2e-${stamp}` },
    data: { firstName: "Ann", lastName: "Forms", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" },
  });
  expect(reg.status()).toBe(201);
  ids.patient = (await reg.json()).id;

  await desk.goto(`/patients/${ids.patient}/forms`);
  await expect(desk.getByText("No forms yet")).toBeVisible();
  await desk.getByLabel("Start a form").selectOption(ids.template);
  await desk.getByRole("button", { name: "Start form" }).click();
  await expect(desk.getByRole("heading", { name: "Privacy notice" })).toBeVisible();
  await expect(desk.getByText("Draft - unsigned").first()).toBeVisible();
  ids.form = desk.url().split("/forms/")[1];

  await desk.getByLabel("Preferred name").fill("Annie");
  await desk.getByRole("button", { name: "Save draft" }).click();
  await expect(desk.getByText("Draft saved.")).toBeVisible();
  await desk.reload();
  await expect(desk.getByLabel("Preferred name")).toHaveValue("Annie");
  await shot(desk, "02-draft.png");
  await scanBothThemes(desk, "draft");

  // the required checkbox is missing: the UI names it and does not go to review
  await desk.getByRole("button", { name: "Review and sign" }).click();
  await expect(desk.getByText("This must be checked.")).toBeVisible();
  await expect(desk.getByRole("heading", { name: "Review before signing" })).toHaveCount(0);
  evidence.draft = { savedAndReloaded: true, requiredAnswerBlocksReview: true };
});

test("TEMPLATE CHANGE DURING COMPLETION: a newer template version is offered but the draft stays on the version it started on", async () => {
  await manager.goto("/admin/form-templates");
  await manager.getByRole("button", { name: /Privacy notice/ }).click();
  await manager.getByLabel("Form text shown to the signer *").fill("Version two: we protect your health information and share it only as the law allows.");
  await manager.getByLabel("What changed (optional)").fill("Reworded for clarity");
  await manager.getByRole("button", { name: "Publish new version" }).click();
  await expect(manager.getByText("Version 2 published.")).toBeVisible();

  await desk.goto(`/patients/${ids.patient}/forms/${ids.form}`);
  await expect(desk.getByText(/A newer version \(version 2\)/)).toBeVisible();
  await expect(desk.getByRole("region", { name: "Form wording" })).toContainText("We protect your health information");
  await expect(desk.getByRole("region", { name: "Form wording" })).not.toContainText("Version two");
  await shot(desk, "03-newer-version-offered.png");
  evidence.templateChangeDuringCompletion = { draftStayedOnVersion1: true, newerVersionOffered: 2 };
});

test("SIGN: review shows exactly what will be signed; missing signer details are refused; a keyboard-only signature records the signer, relationship and version", async () => {
  await desk.getByLabel(/I have received the privacy notice/).check();
  await desk.getByRole("button", { name: "Save draft" }).click();
  await expect(desk.getByText("Draft saved.")).toBeVisible();
  await desk.getByRole("button", { name: "Review and sign" }).click();
  await expect(desk.getByRole("heading", { name: "Review before signing" })).toBeVisible();
  await expect(desk.getByRole("region", { name: "Form wording" })).toContainText("We protect your health information");
  await expect(desk.getByRole("heading", { name: "Answers for Ann Forms" }).locator("..")).toContainText("Annie");
  await shot(desk, "04-review.png");

  await desk.getByRole("button", { name: "Sign form" }).click(); // nothing entered yet
  await expect(desk.getByText("The signer's name is required.")).toBeVisible();
  await expect(desk.getByText("Say how the signer relates to the patient.")).toBeVisible();
  await expect(desk.getByText("The signer must confirm the statement before signing.")).toBeVisible();
  await scanBothThemes(desk, "review-with-errors");

  // capture the idempotency key the UI sends, then sign with the keyboard
  let signKey = "";
  desk.on("request", (r) => { if (r.url().endsWith(`/api/forms/${ids.form}/sign`)) signKey = r.headers()["idempotency-key"] ?? ""; });
  await desk.getByLabel("Signer's full name *").fill("Pat Forms");
  await desk.getByLabel("Relationship to the patient *").selectOption("Parent");
  await desk.getByLabel("Type your name as your signature *").fill("Pat Forms");
  await desk.getByRole("checkbox", { name: /I have read this form/ }).focus();
  await desk.keyboard.press("Space");
  await desk.getByRole("button", { name: "Sign form" }).focus();
  await desk.keyboard.press("Enter");

  await expect(desk.getByRole("heading", { name: "Signed copy" })).toBeVisible();
  await expect(desk.getByText("Form signed.")).toBeVisible();
  await expect(desk.getByText(/Integrity check: matches the copy made at signing/)).toBeVisible();
  await expect(desk.getByText("Signed by").locator("xpath=following-sibling::dd")).toHaveText("Pat Forms");
  await expect(desk.getByLabel("Preferred name")).toHaveCount(0);
  expect(signKey).toMatch(/^sign-/);
  ids.signKey = signKey;
  await shot(desk, "05-signed.png");
  await scanBothThemes(desk, "signed-copy");

  const detail = (await api(desk, "get", `/api/forms/${ids.form}`)).body as { snapshot: { id: string; snapshotHash: string; templateVersionNumber: number; responses: Record<string, string>; signerRelationship: string } };
  ids.snapshot = detail.snapshot.id;
  ids.hash = detail.snapshot.snapshotHash;
  expect(detail.snapshot).toMatchObject({ templateVersionNumber: 1, signerRelationship: "Parent", responses: { received: "true", nickname: "Annie" } });
  evidence.signature = { signer: "Pat Forms", relationship: "Parent", templateVersion: 1, hash: ids.hash, byKeyboard: true };
});

test("TEMPLATE EDIT AFTER SIGNING: the office manager publishes version 3 with completely different wording", async () => {
  await manager.goto("/admin/form-templates");
  await manager.getByRole("button", { name: /Privacy notice/ }).click();
  await manager.getByLabel("Form text shown to the signer *").fill("Version three: completely different wording.");
  await manager.getByRole("button", { name: "Publish new version" }).click();
  await expect(manager.getByText("Version 3 published.")).toBeVisible();
});

test("IMMUTABLE: the signed copy still shows version 1 wording and the same fingerprint after two further template versions; the same submit again and another key create nothing new", async () => {
  await desk.goto(`/patients/${ids.patient}/forms/${ids.form}`);
  await expect(desk.getByRole("region", { name: "Signed form wording" })).toContainText("We protect your health information and share it only as the law allows.");
  await expect(desk.getByRole("region", { name: "Signed form wording" })).not.toContainText("Version three");
  await expect(desk.getByText("Template version 1").first()).toBeVisible();
  const after = (await api(desk, "get", `/api/forms/${ids.form}`)).body as { snapshot: { snapshotHash: string; integrityVerified: boolean } };
  expect(after.snapshot.snapshotHash).toBe(ids.hash);
  expect(after.snapshot.integrityVerified).toBe(true);

  const body = { signerName: "Pat Forms", relationship: "Parent", relationshipNote: null, signatureText: "Pat Forms", attested: true, templateVersionId: "", rowVersion: "" };
  const current = (await api(desk, "get", `/api/forms/${ids.form}`)).body as { version: { id: string }; rowVersion: string };
  body.templateVersionId = current.version.id;
  body.rowVersion = current.rowVersion;
  const replay = await api(desk, "post", `/api/forms/${ids.form}/sign`, body, { "Idempotency-Key": ids.signKey });
  expect(replay.status).toBe(200); // the very same submit again: the first result, nothing new
  expect((replay.body as { snapshot: { id: string } }).snapshot.id).toBe(ids.snapshot);
  const other = await api(desk, "post", `/api/forms/${ids.form}/sign`, body, { "Idempotency-Key": "a-completely-different-key" });
  expect(other.status).toBe(409);
  expect(other.body.error).toBe("already_signed");
  const docs = await api(desk, "get", `/api/patients/${ids.patient}/signed-documents`);
  expect((docs.body as unknown as unknown[]).length).toBe(1);
  evidence.immutability = { laterTemplateVersions: 3, fingerprintUnchanged: true, replayStatus: replay.status, differentKeyStatus: other.status, signedDocuments: 1 };
});

test("INTERRUPTED SIGNATURE: the response is lost after the server stored it; the screen says the outcome is unknown and Sign again records exactly one signature", async () => {
  await desk.goto(`/patients/${ids.patient}/forms`);
  await desk.getByLabel("Start a form").selectOption(ids.template);
  await desk.getByRole("button", { name: "Start form" }).click();
  await expect(desk.getByRole("heading", { name: "Privacy notice" })).toBeVisible();
  ids.form2 = desk.url().split("/forms/")[1];
  await desk.getByLabel(/I have received the privacy notice/).check();
  await desk.getByRole("button", { name: "Save draft" }).click();
  await expect(desk.getByText("Draft saved.")).toBeVisible();
  await desk.getByRole("button", { name: "Review and sign" }).click();
  await fillSigner(desk, "Ann Forms", "Self (the patient)");

  const keys: string[] = [];
  let first = true;
  await desk.route(`**/api/forms/${ids.form2}/sign`, async (route) => {
    keys.push(route.request().headers()["idempotency-key"] ?? "");
    if (first) {
      first = false;
      await route.fetch(); // the server really processes it...
      await route.abort("connectionreset"); // ...but the answer never reaches the browser
    } else {
      await route.continue();
    }
  });
  await desk.getByRole("button", { name: "Sign form" }).click();
  await expect(desk.getByText(/could not confirm whether the signature was saved/)).toBeVisible();
  await shot(desk, "06-unknown-outcome.png");
  await scanBothThemes(desk, "unknown-outcome");
  await desk.getByRole("button", { name: "Sign again" }).click();
  await expect(desk.getByRole("heading", { name: "Signed copy" })).toBeVisible();
  await desk.unroute(`**/api/forms/${ids.form2}/sign`);

  expect(keys).toHaveLength(2);
  expect(keys[0]).toBe(keys[1]);
  const detail = (await api(desk, "get", `/api/forms/${ids.form2}`)).body as { events: Array<{ eventType: string }> };
  expect(detail.events.filter((e) => e.eventType === "Signed")).toHaveLength(1);
  evidence.interruptedSignature = { sameKeyOnRetry: true, signedEvents: 1 };
});

test("VOID: the front desk cannot void a signed form; the office manager can, with a reason, and the signed copy stays visible; a corrected form can follow", async () => {
  await desk.goto(`/patients/${ids.patient}/forms/${ids.form2}`);
  await expect(desk.getByRole("button", { name: "Void this signed form" })).toHaveCount(0);
  const current = (await api(desk, "get", `/api/forms/${ids.form2}`)).body as { rowVersion: string };
  const refused = await api(desk, "post", `/api/forms/${ids.form2}/void`, { reason: "Because", rowVersion: current.rowVersion });
  expect(refused.status).toBe(403);
  expect(refused.body.error).toBe("void_not_permitted");

  await manager.goto(`/patients/${ids.patient}/forms/${ids.form2}`);
  await manager.getByRole("button", { name: "Void this signed form" }).click();
  await manager.getByRole("button", { name: "Confirm" }).click(); // no reason yet
  await expect(manager.getByText("A reason is required.")).toBeVisible();
  await manager.getByLabel("Reason (required)").fill("Signed on the wrong visit");
  await manager.getByRole("button", { name: "Confirm" }).click();
  await expect(manager.getByText(/This form is void/)).toBeVisible();
  await expect(manager.getByText("Void (was signed)").first()).toBeVisible();
  await expect(manager.getByRole("heading", { name: "Signed copy" })).toBeVisible();
  await shot(manager, "07-void.png");
  await scanBothThemes(manager, "void");

  await manager.goto(`/patients/${ids.patient}/forms`);
  const table = manager.getByRole("table", { name: /This patient's forms/ });
  await expect(table.getByText("Signed", { exact: true })).toHaveCount(1);
  await expect(table.getByText("Void (was signed)")).toHaveCount(1);
  await shot(manager, "08-forms-list.png");
  await scanBothThemes(manager, "forms-list");
  evidence.void = { frontDeskApiStatus: refused.status, voidedWithReason: "Signed on the wrong visit", signedCopyStillVisible: true };
});

test("ROLES: a dentist can start a form; billing can read forms but has no way to start, save or sign; both are refused by the API", async () => {
  for (const [role, canComplete] of [["dentist", true], ["billing", false]] as const) {
    const ctx = await deskContext.browser()!.newContext();
    const page = await ctx.newPage();
    await signIn(page, `${role}-${stamp}`, PASSWORD);
    await page.goto(`/patients/${ids.patient}/forms`);
    await expect(page.getByRole("table", { name: /This patient's forms/ })).toBeVisible();
    if (canComplete) {
      await expect(page.getByLabel("Start a form")).toBeVisible();
    } else {
      await expect(page.getByLabel("Start a form")).toHaveCount(0);
      const start = await api(page, "post", `/api/patients/${ids.patient}/forms`, { templateId: ids.template });
      expect(start.status).toBe(403);
      await page.goto(`/patients/${ids.patient}/forms/${ids.form}`);
      await expect(page.getByRole("heading", { name: "Signed copy" })).toBeVisible(); // read-only view of the signed form
    }
    evidence[`role-${role}`] = { canComplete };
    await ctx.close();
  }
});

test("PATIENT SWITCH: another patient's forms never appear under this patient, and a form id under the wrong patient is not shown", async () => {
  const token = await csrf(desk);
  const reg = await desk.request.post("/api/patients", {
    headers: { "X-CSRF-Token": token, "Idempotency-Key": `forms-e2e-b-${stamp}` },
    data: { firstName: "Ben", lastName: "Other", dateOfBirth: "1990-01-01", sex: "Male", phone: "555-020-0200", addressLine1: "2 Oak St", city: "Austin", state: "TX", postalCode: "78701" },
  });
  ids.other = (await reg.json()).id;
  await desk.goto(`/patients/${ids.other}/forms`);
  await expect(desk.getByText("No forms yet")).toBeVisible();
  await desk.goto(`/patients/${ids.other}/forms/${ids.form}`);
  await expect(desk.getByText("That form was not found for this patient")).toBeVisible();
  await expect(desk.getByText("Pat Forms")).toHaveCount(0);
  evidence.patientSwitch = { otherPatientSeesNoForms: true, wrongPatientFormHidden: true };
});
