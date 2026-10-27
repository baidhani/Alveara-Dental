import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

// ALV-018-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server (snapshot isolation on, as a production database has it).
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.patient-export-package.config.ts through scripts/Invoke-RealBackendWalkthrough.ps1 (see docs/testing/REAL_BACKEND_E2E.md).
// The administrator registers patients and imports real files, merges one record into another, and opens the Export patient data screen. A dentist is refused everywhere and has no menu entry. The administrator creates
// one patient's package, a readable record from the patient's page and one from the screen, and every patient's package; each download is saved so its ZIP can be checked afterwards (manifest, every hash, the document
// bytes: see the evidence step). The saved readable page is opened in the browser and makes no outside request. The administrator then grants the dentist only the readable-record permission and the dentist sees
// exactly that. The history and the audit log name who and when and no patient. Finally 200 patients are registered and the real time of a one-patient and an all-patients package is recorded.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PATIENT_EXPORT_PACKAGE_E2E_OUT ?? path.join(process.cwd(), "patient-export-package-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {}, downloads: {}, timing: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
const RADIOGRAPH = "d0c00000-0000-0000-0000-000000000001";
const PNG_1X1 = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const png = (salt: string) => Buffer.concat([PNG_1X1, Buffer.from(salt)]);
const pdf = (salt: string) => Buffer.from(`%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n${salt}`);

let adminContext: BrowserContext;
let admin: Page;
let dentistContext: BrowserContext;
let dentist: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const saved: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
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

const sha256 = (file: string) => createHash("sha256").update(readFileSync(file)).digest("hex");

let sequence = 0;

/** Each patient gets a different birth date and phone number, so the registration's duplicate check has no reason to hold anyone back. */
async function register(page: Page, first: string, last: string, key: string): Promise<string> {
  const n = sequence++;
  const dateOfBirth = `${1950 + (n % 40)}-${String(1 + (n % 12)).padStart(2, "0")}-${String(10 + (n % 18)).padStart(2, "0")}`;
  const phone = `555-${String(100 + (n % 800))}-${String(1000 + n).padStart(4, "0")}`;
  const r = await api(page, "post", "/api/patients", {
    firstName: first, lastName: last, dateOfBirth, sex: "Female", phone, email: null, addressLine1: "4 Pine St", addressLine2: null, city: "Austin", state: "TX", postalCode: "78701",
  }, { "Idempotency-Key": key });
  expect(r.status, `register ${first} ${last}`).toBe(201);
  return r.body.id as string;
}

async function importDocument(patientId: string, name: string, mimeType: string, buffer: Buffer, key: string) {
  const response = await admin.request.post(`/api/patients/${patientId}/documents`, {
    headers: { "X-CSRF-Token": await csrf(admin), "Idempotency-Key": key },
    multipart: { file: { name, mimeType, buffer }, categoryId: RADIOGRAPH, documentDate: "2026-10-01", description: "Imported for the export walkthrough." },
  });
  expect(response.ok(), `import ${name}: ${response.status()}`).toBeTruthy();
}

async function chooseScreenPatient(page: Page, lastName: string) {
  await page.getByLabel("Patient", { exact: true }).fill(lastName);
  await page.getByRole("button", { name: new RegExp(lastName) }).click();
}

const historyRows = (page: Page) => page.getByRole("table", { name: "Export history, newest first" }).getByRole("row");
const savedLine = (what: string) => new RegExp(`^Saved alveara-${what}-[0-9a-f]{32}\\.(zip|html)\\. The export was logged \\(export [0-9a-f-]{36}\\)\\.$`);

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext({ acceptDownloads: true });
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username: `dentist-${stamp}`, password: PASSWORD } });
  expect(reg.ok()).toBeTruthy();
  ids.dentistUser = (await reg.json()).id as string;
  expect((await admin.request.put(`/api/auth/${ids.dentistUser}/role`, { headers: { "X-CSRF-Token": token }, data: { role: "Dentist" } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${ids.dentistUser}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  dentistContext = await browser.newContext({ acceptDownloads: true });
  dentist = await dentistContext.newPage();
  await signIn(dentist, `dentist-${stamp}`, PASSWORD);
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "patient-export-package-evidence.json"), JSON.stringify({ ...evidence, ids, saved }, null, 2));
  await dentistContext?.close();
  await adminContext?.close();
});

test("SETUP: three patients, real document files, and one record merged into another", async () => {
  ids.ann = await register(admin, "Ann", `Rivera${stamp}`, `ex-ann-${stamp}`);
  ids.bo = await register(admin, "Bo", `Stone${stamp}`, `ex-bo-${stamp}`);
  ids.cy = await register(admin, "Cy", `Moss${stamp}`, `ex-cy-${stamp}`);
  await importDocument(ids.ann, "panoramic.png", "image/png", png(`ann-${stamp}`), `ex-doc1-${stamp}`);
  await importDocument(ids.ann, "referral.pdf", "application/pdf", pdf(`ann-${stamp}`), `ex-doc2-${stamp}`);
  await importDocument(ids.bo, "bitewing.png", "image/png", png(`bo-${stamp}`), `ex-doc3-${stamp}`);
  const preview = (await api(admin, "get", `/api/patient-merges/preview?sourceId=${ids.cy}&survivorId=${ids.bo}`)).body;
  const merge = await api(admin, "post", "/api/patient-merges", {
    sourcePatientId: ids.cy, survivorPatientId: ids.bo, reason: "Registered twice", sourceRowVersion: preview.sourceRowVersion, survivorRowVersion: preview.survivorRowVersion,
    fieldChoices: Object.fromEntries((preview.conflicts as any[]).map((c) => [c.field, "Source"])), confirmed: true, confirmIdentityDifference: true,
  }, { "Idempotency-Key": `ex-merge-${stamp}` });
  expect(merge.status).toBe(200);
});

test("WHO MAY: the API refuses an anonymous caller and a dentist on every export route, and the dentist has no menu entry and no screen", async () => {
  const anonymous = await adminContext.browser()!.newContext();
  for (const url of [`/api/patient-exports/patients/${ids.ann}/package`, "/api/patient-exports/all-patients/package", `/api/patient-exports/patients/${ids.ann}/readable-record`])
    expect((await anonymous.request.post(url)).status(), `anonymous ${url}`).toBe(401);
  for (const url of ["/api/patient-exports/history", "/api/patient-exports/overview"]) expect((await anonymous.request.get(url)).status(), `anonymous ${url}`).toBe(401);
  await anonymous.close();

  const refused: Record<string, number> = {};
  for (const url of [`/api/patient-exports/patients/${ids.ann}/package`, "/api/patient-exports/all-patients/package", `/api/patient-exports/patients/${ids.ann}/readable-record`])
    refused[url] = (await api(dentist, "post", url, {})).status;
  for (const url of ["/api/patient-exports/history", "/api/patient-exports/overview"]) refused[url] = (await api(dentist, "get", url)).status;
  expect(Object.values(refused)).toEqual([403, 403, 403, 403, 403]);

  await dentist.goto("/admin/exports");
  await expect(dentist.getByText(/don't have permission/i)).toBeVisible();
  await expect(dentist.getByRole("link", { name: "Export patient data" })).toHaveCount(0);
  await dentist.goto(`/patients/${ids.ann}`);
  await expect(dentist.getByRole("heading", { name: "Patient workspace" })).toBeVisible();
  await expect(dentist.getByRole("button", { name: "Download readable record" })).toHaveCount(0);
  await expect(dentist.getByRole("button", { name: "Export patient file" })).toHaveCount(0);
  evidence.access = { dentistRefused: refused, anonymous: [401, 401, 401, 401, 401] };
  await admin.goto("/");
  await expect(admin.getByRole("link", { name: "Export patient data" })).toBeVisible();
});

test("SCREEN: the administrator sees what a package holds and leaves out, the limits, how the database is read and the empty history", async () => {
  await admin.goto("/admin/exports");
  await expect(admin.getByRole("heading", { name: "Export patient data" })).toBeVisible();
  await expect(admin.getByText("No exports yet.")).toBeVisible();
  await expect(admin.getByRole("radio", { name: "One patient" })).toBeChecked();
  await expect(admin.getByRole("button", { name: "Create package", exact: true })).toBeDisabled();           // no patient chosen yet
  await admin.getByText("What is in a package and what is left out").click();
  await expect(admin.getByText("Left out on purpose")).toBeVisible();
  await expect(admin.getByText(/Audit log\./).first()).toBeVisible();
  await admin.getByLabel("Patient folder names").selectOption("NameAndId");
  await expect(admin.getByText(/Folder names will contain patient names/)).toBeVisible();
  await shot(admin, "01-export-screen-name-folders-warning.png");
  await admin.getByLabel("Patient folder names").selectOption("IdOnly");
  await admin.getByRole("radio", { name: "All patients" }).click();
  await expect(admin.getByText("The export reads the whole database as it was at one moment.")).toBeVisible();     // snapshot isolation is on in this database
  await shot(admin, "02-export-screen-all-patients.png");
  await admin.getByRole("radio", { name: "One patient" }).click();
  await scanBothThemes(admin, "export-screen");
});

test("ONE PATIENT: the administrator creates a package from the screen; it is saved, logged and listed in the history", async () => {
  await admin.goto("/admin/exports");
  await chooseScreenPatient(admin, `Rivera${stamp}`);
  const [download] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Create package", exact: true }).click()]);
  expect(download.suggestedFilename()).toMatch(/^alveara-patient-package-[0-9a-f]{32}\.zip$/);
  saved.onePatientPackage = path.join(OUT, "one-patient-package.zip");
  await download.saveAs(saved.onePatientPackage);
  await expect(admin.getByText(savedLine("patient-package"))).toBeVisible();
  const rows = historyRows(admin);
  await expect(rows).toHaveCount(2);                                                                    // the heading row and the export
  await expect(rows.nth(1)).toContainText("Complete");
  await expect(rows.nth(1)).toContainText("Package with documents");
  await expect(rows.nth(1)).toContainText(sha256(saved.onePatientPackage).slice(0, 12));                    // the file check shown is the hash of the file that was saved
  await shot(admin, "03-one-patient-package-saved.png");
  (evidence.downloads as Record<string, unknown>).onePatientPackage = { file: "one-patient-package.zip", sha256: sha256(saved.onePatientPackage) };
});

test("READABLE RECORD: from the patient's page and from the screen; the saved page opens in the browser, runs no script and makes no outside request", async () => {
  await admin.goto(`/patients/${ids.ann}`);
  await expect(admin.getByRole("button", { name: "Export patient file" })).toBeVisible();               // STORY-018's button is still there
  const [download] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Download readable record" }).click()]);
  expect(download.suggestedFilename()).toMatch(/^alveara-readable-record-[0-9a-f]{32}\.html$/);
  saved.readableFromPatientPage = path.join(OUT, "readable-record-from-patient-page.html");
  await download.saveAs(saved.readableFromPatientPage);
  await expect(admin.getByText(/^Saved alveara-readable-record-[0-9a-f]{32}\.html\. The export was logged\.$/)).toBeVisible();
  await shot(admin, "04-patient-page-readable-record-button.png");

  const viewer = await adminContext.newPage();
  const requests: string[] = [];
  viewer.on("request", (r) => requests.push(r.url()));
  await viewer.goto(pathToFileURL(saved.readableFromPatientPage).href);
  await expect(viewer.getByRole("heading", { level: 1, name: `Ann Rivera${stamp}` })).toBeVisible();
  await expect(viewer.getByRole("heading", { level: 2, name: "Treatment plans" })).toBeVisible();
  await expect(viewer.getByText(/This page lists documents by name only/)).toBeVisible();
  await expect(viewer.getByText("panoramic.png")).toBeVisible();                                         // listed by name, the file itself is only in the package
  expect(await viewer.locator("script").count()).toBe(0);
  expect(await viewer.locator('meta[http-equiv="Content-Security-Policy"]').getAttribute("content")).toContain("default-src 'none'");
  expect(requests.filter((u) => !u.startsWith("file:"))).toEqual([]);
  await viewer.screenshot({ path: path.join(OUT, "05-readable-record-opened.png"), fullPage: true });
  await viewer.close();

  await admin.goto("/admin/exports");
  await chooseScreenPatient(admin, `Stone${stamp}`);
  await admin.getByRole("radio", { name: /Readable record only/ }).click();
  await expect(admin.getByLabel("Patient folder names")).toHaveCount(0);
  const [second] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Download readable record" }).click()]);
  saved.readableFromScreen = path.join(OUT, "readable-record-from-screen.html");
  await second.saveAs(saved.readableFromScreen);
  await expect(admin.getByText(savedLine("readable-record"))).toBeVisible();
  await expect(historyRows(admin).nth(1)).toContainText("Readable record");
  (evidence.downloads as Record<string, unknown>).readableRecords = [
    { file: "readable-record-from-patient-page.html", sha256: sha256(saved.readableFromPatientPage) },
    { file: "readable-record-from-screen.html", sha256: sha256(saved.readableFromScreen) },
  ];
});

test("ALL PATIENTS: the administrator creates every patient's package from the screen and the time it took is recorded", async () => {
  await admin.goto("/admin/exports");
  await admin.getByRole("radio", { name: "All patients" }).click();
  await expect(admin.getByText(/patients, with .* of document files/)).toBeVisible();
  const started = Date.now();
  const [download] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Create package for all patients" }).click()]);
  const elapsed = Date.now() - started;
  expect(download.suggestedFilename()).toMatch(/^alveara-all-patients-package-[0-9a-f]{32}\.zip$/);
  saved.allPatientsPackage = path.join(OUT, "all-patients-package.zip");
  await download.saveAs(saved.allPatientsPackage);
  await expect(admin.getByText(savedLine("all-patients-package"))).toBeVisible();
  await expect(historyRows(admin).nth(1)).toContainText("All patients");
  await shot(admin, "06-all-patients-package-saved.png");
  (evidence.downloads as Record<string, unknown>).allPatientsPackage = { file: "all-patients-package.zip", sha256: sha256(saved.allPatientsPackage), screenMilliseconds: elapsed };
  const history = (await api(admin, "get", "/api/patient-exports/history")).body as any[];
  const row = history.find((h) => h.scope === "AllPatients");
  expect(row.result).toBe("Complete");
  expect(row.patientCount).toBe((await api(admin, "get", "/api/patient-exports/overview")).body.patientCount);        // the patients not merged away, and only those
  expect(row.sha256).toBe(sha256(saved.allPatientsPackage));
});

test("PERMISSIONS: granting the dentist only the readable-record permission gives the dentist exactly that and nothing more", async () => {
  const version = (await api(admin, "get", "/api/permissions/effective")).body.version as number;
  const grant = await api(admin, "put", "/api/permissions/overrides", { role: "Dentist", permission: "ExportPatientReadableRecord", effect: "Grant", reason: "Walkthrough", expectedVersion: version, confirmed: true }, { "Idempotency-Key": `ex-grant-${stamp}` });
  expect(grant.status).toBe(200);

  await dentist.goto(`/patients/${ids.ann}`);
  await expect(dentist.getByRole("button", { name: "Download readable record" })).toBeVisible();
  await expect(dentist.getByRole("button", { name: "Export patient file" })).toHaveCount(0);
  await dentist.goto("/admin/exports");
  await expect(dentist.getByRole("heading", { name: "Export patient data" })).toBeVisible();
  await expect(dentist.getByRole("radio", { name: "All patients" })).toHaveCount(0);
  await expect(dentist.getByLabel("Include document files (scans, images, letters)")).toHaveCount(0);
  await chooseScreenPatient(dentist, `Rivera${stamp}`);
  const [download] = await Promise.all([dentist.waitForEvent("download"), dentist.getByRole("button", { name: "Download readable record" }).click()]);
  saved.dentistReadable = path.join(OUT, "readable-record-by-dentist.html");
  await download.saveAs(saved.dentistReadable);
  await expect(dentist.getByText("You see the exports you made.")).toBeVisible();
  await expect(historyRows(dentist)).toHaveCount(2);                                                    // only the dentist's own export
  await shot(dentist, "07-dentist-readable-record-only.png");

  expect((await api(dentist, "post", "/api/patient-exports/all-patients/package", {})).status).toBe(403);
  expect((await api(dentist, "post", `/api/patient-exports/patients/${ids.ann}/package`, {})).status).toBe(403);
  const own = (await api(dentist, "get", "/api/patient-exports/history")).body as any[];
  expect(own).toHaveLength(1);
  expect(own[0].requestedBy).toBeTruthy();
  evidence.permissions = { dentistAllPatients: 403, dentistPackage: 403, dentistOwnHistoryRows: own.length };
});

test("HISTORY AND AUDIT: every export names who and when and no patient, and the audit log holds the three kinds of event", async () => {
  const history = (await api(admin, "get", "/api/patient-exports/history")).body as any[];
  expect(history.length).toBeGreaterThanOrEqual(4);
  expect(new Set(history.map((h) => h.kind))).toEqual(new Set(["Package", "ReadableRecord"]));
  expect(history.every((h) => h.result === "Complete" && !h.interrupted)).toBeTruthy();
  const text = JSON.stringify(history);
  for (const name of [`Rivera${stamp}`, `Stone${stamp}`, `Moss${stamp}`, "Ann", "Bo "]) expect(text.includes(name), `history must not contain ${name}`).toBeFalsy();

  const audit = ((await api(admin, "get", "/api/auth/audit-log?take=500")).body as any[]).filter((e) => ["PatientDataPackageExported", "AllPatientDataPackageExported", "PatientReadableRecordExported"].includes(e.eventType));
  expect(new Set(audit.map((e) => e.eventType))).toEqual(new Set(["PatientDataPackageExported", "AllPatientDataPackageExported", "PatientReadableRecordExported"]));
  expect(JSON.stringify(audit)).not.toContain(`Rivera${stamp}`);
  await admin.goto("/admin/audit-log");
  await expect(admin.getByRole("heading", { name: /Audit log/i }).first()).toBeVisible();
  await shot(admin, "08-audit-log.png");
  evidence.history = { lines: history.length, auditEvents: audit.length };
});

test("STORY-018 STILL WORKS: the original Export patient file button downloads the JSON file in the documented layout", async () => {
  await admin.goto(`/patients/${ids.ann}`);
  const [download] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Export patient file" }).click()]);
  expect(download.suggestedFilename()).toMatch(/^alveara-patient-[0-9a-f]{32}\.json$/);
  saved.original = path.join(OUT, "original-story-018-export.json");
  await download.saveAs(saved.original);
  const file = JSON.parse(readFileSync(saved.original, "utf8"));
  expect(file.format).toBe("alveara.patient-export");
  expect(file.formatVersion).toBe(1);
  expect(file.source.sourcePatientId).toBe(ids.ann);
});

test("TIMING: 200 patients are registered and the real time of a one-patient and an all-patients package is recorded", async () => {
  test.setTimeout(600_000);
  for (let batch = 0; batch < 10; batch++) {
    await Promise.all(Array.from({ length: 20 }, (_, i) => register(admin, "Load", `Volume${stamp}n${batch * 20 + i}`, `ex-load-${stamp}-${batch * 20 + i}`)));
  }
  const patientCount = (await api(admin, "get", "/api/patient-exports/overview")).body.patientCount as number;
  expect(patientCount).toBeGreaterThanOrEqual(202);

  const post = async (url: string, body: unknown) => {
    const started = Date.now();
    const response = await admin.request.post(url, { headers: { "X-CSRF-Token": await csrf(admin) }, data: body, timeout: 600_000 });
    const bytes = (await response.body()).length;
    return { status: response.status(), milliseconds: Date.now() - started, bytes, result: response.headers()["x-export-result"] };
  };
  const runs: unknown[] = [];
  for (let attempt = 1; attempt <= 2; attempt++) {
    const one = await post(`/api/patient-exports/patients/${ids.ann}/package`, { includeDocumentFiles: true });
    const all = await post("/api/patient-exports/all-patients/package", { includeDocumentFiles: true });
    expect(one.status).toBe(200);
    expect(all.status).toBe(200);
    expect(all.result).toBe("Complete");
    runs.push({ attempt, onePatientPackage: one, allPatientsPackage: all });
  }
  (evidence.timing as Record<string, unknown>).patients = patientCount;
  (evidence.timing as Record<string, unknown>).runs = runs;
  const last = (await api(admin, "get", "/api/patient-exports/history?limit=1")).body as any[];
  expect(last[0].patientCount).toBe(patientCount);
  expect(last[0].problemCount).toBe(0);
});
