import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { existsSync, mkdirSync, renameSync, utimesSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-010-C01 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server -> real files on disk.
// Excluded from the default mocked run and run with playwright.documents-library.config.ts (see docs/testing/REAL_BACKEND_E2E.md).
// The office manager imports documents tied to an encounter, a tooth and an oral region and finds them again with the library filters and the thumbnail view; a document is made inactive with a reason (hidden, never
// deleted, history kept) and shown and reactivated again; the dentist reads but cannot change anything; the storage page checks every stored file, reports a file taken from disk as missing (the library says so, opening it is
// refused, nothing is invented) and intact again when it is put back; a stray file nothing points to is listed only once it is old enough, removed only after a selection and a confirmation, and logged; and none of it
// puts a file name, a reason or a patient into the log.

test.describe.configure({ mode: "serial" });

const OUT = process.env.DOCUMENTS_LIBRARY_E2E_OUT ?? path.join(process.cwd(), "documents-library-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
const sessions: Record<string, { context: BrowserContext; page: Page }> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const PNG_1X1 = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const png = (salt = "") => Buffer.concat([PNG_1X1, Buffer.from(salt)]);
const pdf = (salt = "") => Buffer.from(`%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n${salt}`);

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", statement], { stdio: "pipe" }).toString().trim();
}

/** The folder the walkthrough's API keeps stored files in (see scripts/Invoke-RealBackendWalkthrough.ps1). */
const storageRoot = () => path.join(process.env.ALVERA_DATA_ROOT ?? "C:\\.alvera-data", "Test-Data", "Storage", `walkthrough-${process.env.E2E_DB_NAME}`);
const blobFile = (title: string) => path.join(storageRoot(), sql(`SET NOCOUNT ON; SELECT LOWER(REPLACE(CONVERT(varchar(36), BlobId), '-', '')) FROM Documents WHERE OriginalFileName = N'${title}'`));

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
    expect(blocking.map((v) => `${v.id}:${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`), `${label} (${theme}): critical/serious axe violations`).toEqual([]);
  }
  await setTheme(page, "light");
}

const importForm = (page: Page) => page.getByRole("form", { name: "Import a document" });
const filterForm = (page: Page) => page.getByRole("search", { name: "Filter documents" });
const rowOf = (page: Page, name: string) => page.getByRole("table", { name: "Documents" }).getByRole("row").filter({ hasText: name });
const entries = async (page: Page, query = "") => (await api(page, "get", `/api/patients/${ids.ann}/documents${query}`)).body.entries as any[];
const titles = async (page: Page, query = "") => (await entries(page, query)).filter((e) => e.kind === "Document").map((e) => e.title).sort();
const auditOf = async (type: string) => ((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).filter((e) => e.eventType === type);
const documentId = async (title: string) => (await entries(admin)).find((e) => e.title === title)?.id as string;

async function gotoDocuments(page: Page) {
  await page.goto(`/patients/${ids.ann}/documents`);
  await expect(page.getByRole("heading", { name: "Documents", exact: true })).toBeVisible();
  await expect(page.getByText("Loading documents…")).toHaveCount(0);
}

async function applyFilters(page: Page, set: (form: ReturnType<typeof filterForm>) => Promise<void>) {
  await set(filterForm(page));
  await filterForm(page).getByRole("button", { name: "Apply filters" }).click();
}

async function importFile(page: Page, file: { name: string; mimeType: string; buffer: Buffer }, o: { category?: string; encounter?: RegExp; tooth?: string; region?: string; description?: string } = {}) {
  const form = importForm(page);
  await form.getByLabel("File").setInputFiles(file);
  await form.getByLabel("Category").selectOption({ label: o.category ?? "Radiograph" });
  await form.getByLabel("Date of document").fill("2026-10-01");
  await form.getByLabel("Description (optional)").fill(o.description ?? "");
  if (o.encounter) await form.getByLabel("Encounter (optional)").selectOption({ index: 1 });
  if (o.tooth) await form.getByLabel("Tooth (optional)").selectOption(o.tooth);
  if (o.region) await form.getByLabel("Oral region (optional)").selectOption({ index: 1 });
  await form.getByRole("button", { name: "Import document" }).click();
  await expect(page.getByRole("status").filter({ hasText: `Imported ${file.name}` })).toBeVisible();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["OfficeManager", "Dentist", "FrontDesk"]) {
    ids[`${role}User`] = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    expect((await api(admin, "post", "/api/config/staff", { displayName: `${role} ${stamp}`, userAccountId: ids[`${role}User`] })).status, `staff profile for ${role}`).toBe(201);
    const context = await browser.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "documents-library-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a patient with one encounter and an empty library", async () => {
  const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `lib-ann-${stamp}` });
  expect(r.status).toBe(201);
  ids.ann = r.body.id as string;
  const enc = await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/encounters`, {}, { "Idempotency-Key": `lib-enc-${stamp}` });
  expect(enc.status).toBe(201);
  ids.encounter = enc.body.id as string;
  expect(await entries(admin)).toHaveLength(0);
});

test("IMPORT: documents are tied to an encounter, a tooth and an oral region through the screen, and the server holds exactly that", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  await importFile(page, { name: "tooth16.png", mimeType: "image/png", buffer: png("t16") }, { encounter: /./, tooth: "16", description: "Periapical, upper right first molar." });
  await importFile(page, { name: "region.png", mimeType: "image/png", buffer: png("reg") }, { region: "x", description: "Whole lower jaw." });
  await importFile(page, { name: "plain.pdf", mimeType: "application/pdf", buffer: pdf("plain") }, { category: "Referral letter", description: "No tooth, no encounter." });
  await expect(rowOf(page, "tooth16.png")).toContainText("Tooth");
  await expect(rowOf(page, "tooth16.png")).toContainText("Encounter");
  await shot(page, "01-library-with-context.png");

  const docs = (await entries(admin)).filter((e) => e.kind === "Document");
  const t16 = docs.find((d) => d.title === "tooth16.png").document;
  expect([t16.encounterId, t16.toothKey, t16.regionKey ?? null, t16.status]).toEqual([ids.encounter, "16", null, "Active"]);
  const region = docs.find((d) => d.title === "region.png").document;
  expect([region.encounterId ?? null, region.toothKey ?? null, region.status]).toEqual([null, null, "Active"]);
  expect(region.regionKey).toBeTruthy();
});

test("FILTERS: by tooth, region, encounter and a search word, with a bad range explained beside its box and Clear bringing everything back", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  await applyFilters(page, async (f) => { await f.getByLabel("Tooth", { exact: true }).selectOption("16"); });
  await expect(rowOf(page, "tooth16.png")).toBeVisible();
  await expect(rowOf(page, "plain.pdf")).toHaveCount(0);
  expect(await titles(admin, "?toothKey=16")).toEqual(["tooth16.png"]);

  await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("Oral region").selectOption({ index: 1 }); });
  await expect(rowOf(page, "region.png")).toBeVisible();
  await expect(rowOf(page, "tooth16.png")).toHaveCount(0);

  await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("Encounter", { exact: true }).selectOption({ index: 1 }); });
  await expect(rowOf(page, "tooth16.png")).toBeVisible();
  await expect(rowOf(page, "region.png")).toHaveCount(0);

  await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("Search file names and descriptions").fill("lower jaw"); });
  await expect(rowOf(page, "region.png")).toBeVisible();
  await expect(rowOf(page, "plain.pdf")).toHaveCount(0);

  await applyFilters(page, async (f) => { await f.getByRole("button", { name: "Clear filters" }).click(); await f.getByLabel("From", { exact: true }).fill("2026-10-05"); await f.getByLabel("To", { exact: true }).fill("2026-10-01"); });
  await expect(filterForm(page).getByRole("alert")).toBeVisible();
  await shot(page, "02-filter-refused-range.png");

  await filterForm(page).getByRole("button", { name: "Clear filters" }).click();
  for (const name of ["tooth16.png", "region.png", "plain.pdf"]) await expect(rowOf(page, name)).toBeVisible();
  await scanBothThemes(page, "library-list");
});

test("THUMBNAILS: a picture is drawn small, anything else is named in words, and fetching a thumbnail leaves no trace in the log", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  const before = (await auditOf("DocumentOpened")).length;
  await page.getByLabel("Thumbnails").check();
  await expect(page.getByRole("img", { name: "Thumbnail of tooth16.png" })).toBeVisible();
  await expect.poll(() => page.getByRole("img", { name: "Thumbnail of tooth16.png" }).evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0)).toBe(true);
  await expect(page.getByRole("img", { name: "Thumbnail of plain.pdf" })).toHaveCount(0);
  await shot(page, "03-thumbnails.png");
  const png1 = await documentId("tooth16.png");
  const pdf1 = await documentId("plain.pdf");
  expect((await page.request.get(`/api/documents/${png1}/thumbnail`)).status()).toBe(200);
  expect((await page.request.get(`/api/documents/${pdf1}/thumbnail`)).status()).toBe(415);
  expect((await auditOf("DocumentOpened")).length).toBe(before);
  await scanBothThemes(page, "library-thumbnails");
  await page.getByLabel("List").check();
});

test("PREVIEW: Preview shows the picture on the screen", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  await rowOf(page, "tooth16.png").getByRole("button", { name: "Preview tooth16.png" }).click();
  await expect(page.getByRole("img", { name: "tooth16.png", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Close preview" }).click();
});

test("INACTIVATE: a reason is required; the document leaves the library but is kept, listed under Inactive with the reason, and reactivated", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  await rowOf(page, "plain.pdf").getByRole("button", { name: "Inactivate plain.pdf" }).click();
  const form = page.getByRole("form", { name: "Inactivate document" });
  await form.getByRole("button", { name: "Make inactive" }).click();
  await expect(form.getByRole("alert")).toBeVisible();                       // a reason is needed
  expect(await titles(admin)).toContain("plain.pdf");
  await form.getByLabel("Why is this document being made inactive?").fill("Scanned under the wrong patient.");
  await form.getByRole("button", { name: "Make inactive" }).click();
  await expect(rowOf(page, "plain.pdf")).toHaveCount(0);
  expect(await titles(admin)).not.toContain("plain.pdf");
  expect(await titles(admin, "?status=Inactive")).toEqual(["plain.pdf"]);

  await applyFilters(page, async (f) => { await f.getByLabel("Show", { exact: true }).selectOption("Inactive"); });
  await expect(rowOf(page, "plain.pdf")).toContainText("Inactive");
  await rowOf(page, "plain.pdf").getByRole("button", { name: "Details plain.pdf" }).click();
  await expect(page.getByRole("list", { name: "Status changes" })).toContainText("Scanned under the wrong patient.");
  await shot(page, "04-inactive-details.png");
  await page.getByRole("button", { name: "Close details" }).click();

  await rowOf(page, "plain.pdf").getByRole("button", { name: "Reactivate plain.pdf" }).click();
  await filterForm(page).getByRole("button", { name: "Clear filters" }).click();
  await expect(rowOf(page, "plain.pdf")).toBeVisible();
  await expect.poll(() => titles(admin)).toContain("plain.pdf");                  // the reactivation has reached the server
  expect(sql(`SET NOCOUNT ON; SELECT COUNT(*) FROM DocumentStatusChanges`)).toBe("2");
});

test("THE DENTIST reads but cannot make a document inactive, and the API refuses with 403", async () => {
  const page = sessions.Dentist.page;
  await gotoDocuments(page);
  await expect(rowOf(page, "tooth16.png")).toBeVisible();
  await expect(page.getByRole("button", { name: /^Inactivate / })).toHaveCount(0);
  const id = await documentId("tooth16.png");
  expect((await api(page, "post", `/api/documents/${id}/inactivate`, { rowVersion: "AAAAAAAAB9E=", reason: "no" })).status).toBe(403);
  expect((await api(page, "get", "/api/document-storage")).status).toBe(403);
});

test("STORAGE: the office manager checks every stored file and all are intact", async () => {
  const page = sessions.OfficeManager.page;
  await page.goto("/admin/document-storage");
  await expect(page.getByRole("heading", { name: "Document storage" })).toBeVisible();
  await page.getByRole("button", { name: "Check all stored files" }).click();
  await expect(page.locator("p.alv-documents__status")).toContainText("Checked 3 documents: 3 intact, 0 missing, 0 damaged.");
  await expect(page.getByText("No file needs attention")).toBeVisible();
  await shot(page, "05-storage-intact.png");
  await scanBothThemes(page, "storage-intact");
});

test("MISSING FILE: a file taken from disk is reported missing, the library says so, opening it is refused, and putting it back makes it intact", async () => {
  const page = sessions.OfficeManager.page;
  const file = blobFile("region.png");
  const parked = `${file}.parked`;
  expect(existsSync(file)).toBe(true);
  renameSync(file, parked);
  try {
    await page.goto("/admin/document-storage");
    await page.getByRole("button", { name: "Check all stored files" }).click();
    await expect(page.locator("p.alv-documents__status")).toContainText("1 missing");
    await expect(page.getByRole("table", { name: "Files that need attention" })).toContainText("region.png");
    await expect(page.getByRole("table", { name: "Files that need attention" })).toContainText("Missing");
    await shot(page, "06-storage-missing.png");
    await scanBothThemes(page, "storage-missing");

    await gotoDocuments(page);
    await expect(rowOf(page, "region.png")).toContainText("File missing");
    const id = await documentId("region.png");
    const opened = await page.request.get(`/api/documents/${id}/content`);
    expect(opened.status()).toBe(404);
    expect(((await opened.json()) as any).error).toBe("file_missing");
    expect(JSON.stringify(await opened.json())).not.toMatch(/Storage|alvera-data|walkthrough-/i);
    await shot(page, "07-library-missing.png");
  } finally {
    if (existsSync(parked)) renameSync(parked, file);
  }
  await page.goto("/admin/document-storage");
  await page.getByRole("button", { name: "Check region.png again" }).click();
  await expect(page.locator("p.alv-documents__status")).toContainText("region.png is intact");
  await expect(page.getByText("No file needs attention")).toBeVisible();
  expect((await page.request.get(`/api/documents/${await documentId("region.png")}/content`)).status()).toBe(200);
});

test("UNREFERENCED FILES: a stray file is listed once it is old, a fresh one is never listed, removal needs a selection and a confirmation, and is logged", async () => {
  const page = sessions.OfficeManager.page;
  const stray = path.join(storageRoot(), "0123456789abcdef0123456789abcdef");
  const fresh = path.join(storageRoot(), "fedcba9876543210fedcba9876543210");
  writeFileSync(stray, "left behind by an interrupted import");
  writeFileSync(fresh, "an import in progress");
  const old = new Date(Date.now() - 3 * 60 * 60 * 1000);
  utimesSync(stray, old, old);

  await page.goto("/admin/document-storage");
  const list = page.getByRole("list", { name: "Stored files nothing points to" });
  await expect(list).toContainText("01234567-89ab-cdef-0123-456789abcdef");
  await expect(list).not.toContainText("fedcba98-7654-3210-fedc-ba9876543210");        // too new: could be an import in progress
  await expect(page.getByRole("button", { name: "Remove selected files" })).toBeDisabled();
  await shot(page, "08-unreferenced-listed.png");

  await list.getByRole("checkbox").first().check();
  await page.getByRole("button", { name: "Remove selected files" }).click();
  const dialog = page.getByRole("alertdialog");
  await expect(dialog).toContainText("cannot be undone");
  await dialog.getByRole("button", { name: "Cancel" }).click();
  expect(existsSync(stray)).toBe(true);                                             // cancelling removes nothing

  await page.getByRole("button", { name: "Remove selected files" }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: /^Remove 1 file$/ }).click();
  await expect(page.locator("p.alv-documents__status")).toContainText("Removed 1 file.");
  expect(existsSync(stray)).toBe(false);
  expect(existsSync(fresh)).toBe(true);                                             // the new file is untouched
  await expect(page.getByText("No stored file is unreferenced")).toBeVisible();

  const removed = await auditOf("DocumentOrphanRemoved");
  expect(removed).toHaveLength(1);
  expect(removed[0].performedByUserAccountId).toBe(ids.OfficeManagerUser);
  expect(Number.isNaN(Date.parse(removed[0].timestampUtc))).toBe(false);
  expect(removed[0].targetUserAccountId).toBe("01234567-89ab-cdef-0123-456789abcdef");   // the removal is traceable by the opaque id of the file, which says nothing about its content
  expect((await page.request.get(`/api/documents/${await documentId("tooth16.png")}/content`)).status()).toBe(200);   // referenced files are never touched
});

test("THE LOG: every action is recorded with who and when, and none of it names a file, a reason or the patient", async () => {
  const log = JSON.stringify((await api(admin, "get", "/api/auth/audit-log?take=1000")).body);
  for (const type of ["DocumentInactivated", "DocumentReactivated", "DocumentStorageChecked", "DocumentOrphanRemoved"]) expect(log, type).toContain(type);
  for (const secret of ["tooth16.png", "region.png", "plain.pdf", "Scanned under the wrong patient", "Alder"]) expect(log, secret).not.toContain(secret);
  evidence.logChecked = true;
});

test("ACCESS: the front desk and the dentist cannot reach the storage page or its API", async () => {
  for (const role of ["FrontDesk", "Dentist"]) {
    const page = sessions[role].page;
    expect((await api(page, "post", "/api/document-storage/check")).status).toBe(403);
    expect((await api(page, "post", "/api/document-storage/orphans/remove", { blobIds: [] })).status).toBe(403);
    await page.goto("/admin/document-storage");
    await expect(page.getByRole("heading", { name: "Document storage" })).toHaveCount(0);
  }
});
