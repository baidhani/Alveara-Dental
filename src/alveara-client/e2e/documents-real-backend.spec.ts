import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// STORY-010 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run and run with playwright.documents.config.ts against an API the caller started (see docs/testing/REAL_BACKEND_E2E.md).
// An office manager imports a PNG, a PDF and a text file through the Documents tab; a file the patient already has is warned about and added only on "Add anyway", then recorded as a duplicate; a program, a
// blocked type, a file that is not the type it claims and an empty file are refused with nothing stored; an edit recategorizes and a stale edit is explained; a signed form appears read-only and is unchanged by
// a template edit; files are served as downloads except PDF/PNG/JPEG under a restrictive policy; the dentist reads but cannot import; the administrator manages categories, the size limit and the blocked list;
// every action is in the log with who and when and none of it names a file or a description; the history numbers have no gap; a failing log write imports nothing and the same click works once it is back;
// and the database itself refuses to change or delete an imported file.

test.describe.configure({ mode: "serial" });

const OUT = process.env.DOCUMENTS_E2E_OUT ?? path.join(process.cwd(), "documents-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};

// A real 1x1 PNG, a minimal PDF, a text file, a Windows program header and a TIFF header, each made unique where a test needs a different file.
const PNG_1X1 = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const png = (salt = "") => Buffer.concat([PNG_1X1, Buffer.from(salt)]);
const pdf = (salt = "") => Buffer.from(`%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n${salt}`);
const program = () => { const b = Buffer.alloc(0x100); b.write("MZ"); b.writeUInt32LE(0x80, 0x3c); b.write("PE\0\0", 0x80, "binary"); return b; };

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}, withCsrf = true) {
  const h = method === "get" || !withCsrf ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" || method === "delete" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the failed-log and immutability demonstrations need it)");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", statement], { stdio: "pipe" }).toString();
}

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
const rowOf = (page: Page, name: string) => page.getByRole("table", { name: "Documents" }).getByRole("row").filter({ hasText: name });
const libraryOf = async (page: Page) => (await api(page, "get", `/api/patients/${ids.ann}/documents`)).body.entries as any[];
const documentsOf = async (page: Page) => (await libraryOf(page)).filter((e) => e.kind === "Document");
const auditOf = async (type: string) => ((await api(admin, "get", "/api/auth/audit-log?take=1000")).body as any[]).filter((e) => e.eventType === type);

async function gotoDocuments(page: Page) {
  await page.goto(`/patients/${ids.ann}/documents`);
  await expect(page.getByRole("heading", { name: "Documents", exact: true })).toBeVisible();
  await expect(page.getByText("Loading documents…")).toHaveCount(0);
}

/** Fills and submits the import form. */
async function importFile(page: Page, file: { name: string; mimeType: string; buffer: Buffer }, o: { category?: string; date?: string; description?: string } = {}) {
  const form = importForm(page);
  await form.getByLabel("File").setInputFiles(file);
  await form.getByLabel("Category").selectOption({ label: o.category ?? "Radiograph" });
  await form.getByLabel("Date of document").fill(o.date ?? "2026-10-01");
  await form.getByLabel("Description (optional)").fill(o.description ?? "");
  await form.getByRole("button", { name: "Import document" }).click();
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
  writeFileSync(path.join(OUT, "documents-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a patient exists and has no documents", async () => {
  const r = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `docs-ann-${stamp}` });
  expect(r.status).toBe(201);
  ids.ann = r.body.id as string;
  expect(await libraryOf(admin)).toHaveLength(0);
});

test("THE SCREEN: the office manager sees an empty library, the size limit and the import form", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  await expect(page.getByText("No documents yet")).toBeVisible();
  await expect(importForm(page)).toContainText("Largest file: 100 MB");
  await shot(page, "01-empty-library.png");
  await scanBothThemes(page, "empty-library");
});

test("IMPORT: a PNG, a PDF and a text file are imported through the screen, each with the right category, date, size and importer", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  const files = [
    { file: { name: "bitewing.png", mimeType: "image/png", buffer: png("a") }, category: "Radiograph", description: "Bitewing, left side." },
    { file: { name: "referral.pdf", mimeType: "application/pdf", buffer: pdf("a") }, category: "Referral letter", description: "Letter to the periodontist." },
    { file: { name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from("plain text notes, nothing verified about this type") }, category: "Correspondence", description: "" },
  ];
  for (const f of files) {
    await importFile(page, f.file, { category: f.category, description: f.description });
    await expect(page.getByRole("status").filter({ hasText: `Imported ${f.file.name}` })).toBeVisible();
    await expect(rowOf(page, f.file.name)).toContainText(f.category);
  }
  await expect(rowOf(page, "bitewing.png")).toContainText(`OfficeManager ${stamp}`);      // the importer is named
});

test("IMPORT (checked against the server): what is stored is what was sent, linked to the right patient and category", async () => {
  const docs = await documentsOf(admin);
  expect(docs.map((d) => d.title).sort()).toEqual(["bitewing.png", "notes.txt", "referral.pdf"]);
  const png1 = docs.find((d) => d.title === "bitewing.png")!.document;
  expect([png1.patientId, png1.categoryName, png1.detectedType, png1.typeVerified, png1.documentDate, png1.description]).toEqual([ids.ann, "Radiograph", "png", true, "2026-10-01", "Bitewing, left side."]);
  expect(docs.find((d) => d.title === "notes.txt")!.document.typeVerified).toBe(false);          // "type not verified" for any type that is not PDF, PNG, JPEG or TIFF
  ids.png = png1.id;
  ids.pdf = docs.find((d) => d.title === "referral.pdf")!.id;
  ids.txt = docs.find((d) => d.title === "notes.txt")!.id;
  evidence.imported = docs.map((d) => ({ title: d.title, category: d.categoryName, type: d.detectedType, bytes: d.sizeBytes }));
});

test("SERVING: every file is a download except PDF and PNG, which are shown under a restrictive policy; nothing names where a file is stored", async () => {
  const page = sessions.OfficeManager.page;
  const get = async (id: string, query = "") => page.request.get(`/api/documents/${id}/content${query}`);
  const asPng = await get(ids.png);
  expect([asPng.headers()["content-type"], asPng.headers()["content-disposition"]?.split(";")[0], asPng.headers()["x-content-type-options"], asPng.headers()["content-security-policy"]]).toEqual(["image/png", "inline", "nosniff", "sandbox"]);
  const asPdf = await get(ids.pdf);
  expect([asPdf.headers()["content-type"], asPdf.headers()["content-disposition"]?.split(";")[0], asPdf.headers()["content-security-policy"]]).toEqual(["application/pdf", "inline", "sandbox"]);
  const asTxt = await get(ids.txt);
  expect([asTxt.headers()["content-type"], asTxt.headers()["content-disposition"]?.split(";")[0], asTxt.headers()["x-content-type-options"]]).toEqual(["application/octet-stream", "attachment", "nosniff"]);
  expect((await get(ids.png, "?download=true")).headers()["content-disposition"]).toMatch(/^attachment/);
  const listing = JSON.stringify(await libraryOf(page));
  expect(listing).not.toMatch(/blob|Storage|\.alvera-data/i);

  // the PNG, opened as the page itself under the sandbox policy, still renders
  const popup = await sessions.OfficeManager.context.newPage();
  await popup.goto(`/api/documents/${ids.png}/content`);
  expect(await popup.locator("img").evaluate((i: HTMLImageElement) => i.naturalWidth)).toBe(1);
  await popup.close();
  evidence.serving = { png: "inline+sandbox", pdf: "inline+sandbox", txt: "attachment", tiffAndOthers: "download only (backend tests)" };
});

test("DUPLICATE: the same file is warned about naming the existing document; Cancel imports nothing; Add anyway records a duplicate in the list, the history and the log", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  const before = (await documentsOf(admin)).length;
  await importFile(page, { name: "bitewing again.png", mimeType: "image/png", buffer: png("a") }, { description: "Same picture again." });
  const warning = page.getByRole("alert", { name: "This patient already has this file" });
  await expect(warning).toBeVisible();
  await expect(warning).toContainText("Radiograph");
  await shot(page, "02-duplicate-warning.png");
  await warning.getByRole("button", { name: "Cancel" }).click();
  await expect(warning).toHaveCount(0);
  expect((await documentsOf(admin)).length).toBe(before);                                  // nothing imported by the warning or the cancel

  await importFile(page, { name: "bitewing again.png", mimeType: "image/png", buffer: png("a") }, { description: "Same picture again." });
  await page.getByRole("alert", { name: "This patient already has this file" }).getByRole("button", { name: "Add anyway" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Imported bitewing again.png" })).toBeVisible();
  await expect(rowOf(page, "bitewing again.png")).toContainText("Duplicate");
  const docs = await documentsOf(admin);
  expect(docs.length).toBe(before + 1);
  const copy = docs.find((d) => d.title === "bitewing again.png")!;
  expect(copy.document.duplicateOfDocumentId).toBe(ids.png);
  const detail = (await api(admin, "get", `/api/documents/${copy.id}`)).body as any;
  expect(detail.history.map((h: any) => h.kind)).toEqual(["ImportedAsDuplicate"]);
  expect(detail.history[0].relatedDocumentId).toBe(ids.png);
  const audit = await auditOf("DocumentDuplicateImported");
  expect(audit).toHaveLength(1);
  expect(audit[0].performedByUserAccountId).toBe(ids.OfficeManagerUser);
  ids.copy = copy.id;
  evidence.duplicate = { warned: true, cancelledImportedNothing: true, recordedAsDuplicate: true };
});

test("REFUSALS: a program, a blocked type, a mislabelled file and an empty file are each refused beside the file box and nothing is stored", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  const before = (await documentsOf(admin)).length;
  const cases: Array<[string, { name: string; mimeType: string; buffer: Buffer }, RegExp]> = [
    ["a blocked type", { name: "setup.exe", mimeType: "application/octet-stream", buffer: png("x") }, /This type of file cannot be imported/],
    ["a web page", { name: "page.html", mimeType: "text/html", buffer: Buffer.from("<html></html>") }, /This type of file cannot be imported/],
    ["a program under a harmless name", { name: "invoice.pdf", mimeType: "application/pdf", buffer: program() }, /This file is a program/],
    ["a PNG named .pdf", { name: "scan.pdf", mimeType: "application/pdf", buffer: png("y") }, /not the type its name says/],
    ["an empty file", { name: "empty.png", mimeType: "image/png", buffer: Buffer.alloc(0) }, /The file is empty/],
  ];
  for (const [label, file, message] of cases) {
    await importFile(page, file);
    await expect(importForm(page).getByText(message), label).toBeVisible();
    await expect(importForm(page).getByLabel("File"), label).toHaveAccessibleDescription(message);
  }
  await shot(page, "03-refusal.png");
  expect((await documentsOf(admin)).length).toBe(before);
  expect(await auditOf("DocumentImported")).toHaveLength(3);                           // the three real imports only: no refusal left a log entry
  evidence.refusals = cases.map(([l]) => l);
});

test("EDIT: recategorizing and editing the description is saved with a history row and a log entry; a stale edit is explained and nothing is overwritten", async () => {
  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  await rowOf(page, "notes.txt").getByRole("button", { name: "Edit notes.txt" }).click();
  const form = page.getByRole("form", { name: "Edit document" });
  await form.getByLabel("Category").selectOption({ label: "Lab report" });
  await form.getByLabel("Description (optional)").fill("Lab values from the outside lab.");
  await form.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Saved changes to notes.txt" })).toBeVisible();
  await expect(rowOf(page, "notes.txt")).toContainText("Lab report");
  const detail = (await api(admin, "get", `/api/documents/${ids.txt}`)).body as any;
  expect(detail.history.map((h: any) => h.kind)).toEqual(["Imported", "Opened", "Categorized", "MetadataEdited"]);        // "Opened" is the download in the SERVING step
  expect([detail.history[2].fromCategoryName, detail.history[2].toCategoryName]).toEqual(["Correspondence", "Lab report"]);
  expect(await auditOf("DocumentCategorized")).toHaveLength(1);

  // another user changes it while this form is open
  await rowOf(page, "notes.txt").getByRole("button", { name: "Edit notes.txt" }).click();
  const current = (await api(admin, "get", `/api/documents/${ids.txt}`)).body.document;
  const sneaky = await api(admin, "put", `/api/documents/${ids.txt}`, { categoryId: current.categoryId, documentDate: current.documentDate, description: "Changed by someone else.", rowVersion: current.rowVersion });
  expect(sneaky.status).toBe(200);
  await page.getByRole("form", { name: "Edit document" }).getByLabel("Description (optional)").fill("My stale edit.");
  await page.getByRole("form", { name: "Edit document" }).getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByText("Someone else changed this while you were editing")).toBeVisible();
  await shot(page, "04-stale-edit.png");
  expect(((await api(admin, "get", `/api/documents/${ids.txt}`)).body.document as any).description).toBe("Changed by someone else.");
  await page.getByRole("button", { name: "Reload current version" }).click();
  await expect(page.getByRole("form", { name: "Edit document" })).toHaveCount(0);
  evidence.edit = { history: detail.history.map((h: any) => h.kind), staleRefused: true };
});

test("SIGNED FORM: a signed form appears in the library read-only and is unchanged when its template is edited afterwards", async () => {
  const admin1 = admin;
  const template = await api(admin1, "post", "/api/forms/templates", { key: `privacy-${stamp}`, category: "Privacy", title: "Privacy notice", body: "We protect your health information.", fields: [{ id: "received", label: "I have received the notice", kind: "checkbox", required: true }], changeNote: null });
  expect(template.status).toBe(201);
  ids.template = template.body.template.id;
  const started = await api(admin1, "post", `/api/patients/${ids.ann}/forms`, { templateId: ids.template });
  expect(started.status).toBeLessThan(300);
  const formId = (started.body.detail ?? started.body).summary.id as string;
  let detail = (await api(admin1, "get", `/api/forms/${formId}`)).body as any;
  const saved = await api(admin1, "put", `/api/forms/${formId}/responses`, { responses: { received: "true" }, rowVersion: detail.rowVersion });
  expect(saved.status).toBe(200);
  detail = (await api(admin1, "get", `/api/forms/${formId}`)).body;
  const signed = await api(admin1, "post", `/api/forms/${formId}/sign`, { signerName: "Ann Alder", relationship: "Self", relationshipNote: null, signatureText: "Ann Alder", attested: true, templateVersionId: detail.version.id, rowVersion: detail.rowVersion }, { "Idempotency-Key": `sign-docs-${stamp}` });
  expect(signed.status).toBeLessThan(300);
  const hash = ((await api(admin1, "get", `/api/forms/${formId}`)).body as any).snapshot.snapshotHash as string;

  const page = sessions.OfficeManager.page;
  await gotoDocuments(page);
  const row = rowOf(page, "Privacy notice");
  await expect(row).toContainText("Signed form");
  await expect(row.getByRole("button", { name: /Edit/ })).toHaveCount(0);
  await expect(row.getByRole("link", { name: "View form Privacy notice" })).toBeVisible();
  await shot(page, "05-library-with-signed-form.png");

  const tpl = (await api(admin1, "get", `/api/forms/templates/${ids.template}`)).body as any;
  const edit = await api(admin1, "put", `/api/forms/templates/${ids.template}`, { key: `privacy-${stamp}`, category: "Privacy", title: "Privacy notice v2", body: "Completely different words.", fields: [{ id: "received", label: "Changed question", kind: "checkbox", required: true }], changeNote: "edit", rowVersion: tpl.template.rowVersion });
  expect(edit.status).toBeLessThan(300);
  const after = (await api(admin1, "get", `/api/forms/${formId}`)).body as any;
  expect(after.snapshot.snapshotHash).toBe(hash);
  expect(after.snapshot.integrityVerified).toBe(true);
  await gotoDocuments(page);
  await expect(rowOf(page, "Privacy notice")).toContainText("Signed form");           // still listed under the title it was signed with
  evidence.signedForm = { listedReadOnly: true, hashUnchangedAfterTemplateEdit: true };
});

test("PERMISSIONS: the dentist reads the library but has no import form and the API refuses an import (403); the front desk likewise; only the administrator reaches the settings", async () => {
  for (const role of ["Dentist", "FrontDesk"]) {
    const page = sessions[role].page;
    await gotoDocuments(page);
    await expect(importForm(page)).toHaveCount(0);
    await expect(page.getByRole("button", { name: /^Edit / })).toHaveCount(0);
    await expect(rowOf(page, "bitewing.png").getByRole("link", { name: "Download bitewing.png" })).toBeVisible();
    const r = await page.request.post(`/api/patients/${ids.ann}/documents`, { headers: { "X-CSRF-Token": await csrf(page), "Idempotency-Key": `deny-${role}-${stamp}` }, multipart: { file: { name: "x.png", mimeType: "image/png", buffer: png("deny") }, categoryId: "00000000-0000-0000-0000-000000000000", documentDate: "2026-10-01" } });
    expect(r.status(), `${role} import`).toBe(403);
  }
  const om = sessions.OfficeManager.page;
  expect((await api(om, "get", "/api/document-settings")).status).toBe(403);
  await om.goto("/admin/documents");
  await expect(om.getByRole("heading", { name: "Document settings" })).toHaveCount(0);
  expect((await api(sessions.Dentist.page, "post", "/api/document-settings/blocked-extensions", { extension: "swf" })).status).toBe(403);
  const noCsrf = await sessions.OfficeManager.page.request.post(`/api/patients/${ids.ann}/documents`, { headers: { "Idempotency-Key": `nocsrf-${stamp}` }, multipart: { file: { name: "x.png", mimeType: "image/png", buffer: png("nocsrf") } } });
  expect(noCsrf.status()).toBe(400);
  expect((await documentsOf(admin)).length).toBe(4);
  evidence.permissions = { dentistImport: 403, frontDeskImport: 403, officeManagerSettings: 403, missingCsrf: 400 };
});

test("SETTINGS: the administrator adds and renames categories, sets the size limit and blocks and unblocks a type; built-in types are locked", async () => {
  const page = admin;
  await page.goto("/admin/documents");
  await expect(page.getByRole("heading", { name: "Document settings" })).toBeVisible();
  await page.getByLabel("New category name").fill("Consent copy");
  await page.getByRole("button", { name: "Add category" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Added Consent copy" })).toBeVisible();
  await page.getByRole("button", { name: "Rename Consent copy" }).click();
  await page.getByLabel("New name for Consent copy").fill("Signed consent copy");
  await page.getByRole("button", { name: "Save name" }).click();
  await expect(page.getByRole("table", { name: "Categories" })).toContainText("Signed consent copy");

  await page.getByLabel("Largest file (MB)").fill("0");
  await page.getByRole("button", { name: "Save size limit" }).click();
  await expect(page.getByText(/Enter a whole number of megabytes from 1 to 1024/)).toBeVisible();
  await page.getByLabel("Largest file (MB)").fill("1");
  await page.getByRole("button", { name: "Save size limit" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Size limit saved." })).toBeVisible();

  const list = page.getByRole("list", { name: "Blocked file types" });
  await expect(list.getByText("Locked (built in)")).toHaveCount(15);
  await expect(list.getByRole("button", { name: /Unblock \.exe/ })).toHaveCount(0);
  await page.getByLabel("File type to block").fill(".SWF");
  await page.getByRole("button", { name: "Block file type" }).click();
  await expect(list).toContainText(".swf");
  await shot(page, "06-settings.png");
  await scanBothThemes(page, "settings");

  // the new rules apply on the next import
  const om = sessions.OfficeManager.page;
  await gotoDocuments(om);
  await expect(importForm(om)).toContainText("Largest file: 1 MB");
  await expect(importForm(om).getByLabel("Category")).toContainText("Signed consent copy");
  await importFile(om, { name: "movie.swf", mimeType: "application/x-shockwave-flash", buffer: Buffer.from("FWS-fake") });
  await expect(importForm(om).getByText(/This type of file cannot be imported/)).toBeVisible();
  await importFile(om, { name: "big.png", mimeType: "image/png", buffer: Buffer.concat([PNG_1X1, Buffer.alloc(1_100_000, 7)]) });
  await expect(importForm(om).getByText(/larger than the 1 MB limit/)).toBeVisible();
  expect((await documentsOf(admin)).length).toBe(4);

  await page.getByRole("button", { name: "Unblock .swf" }).click();
  await expect(list).not.toContainText(".swf");
  await page.getByLabel("Largest file (MB)").fill("100");
  await page.getByRole("button", { name: "Save size limit" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Size limit saved." })).toBeVisible();
  const logged = ["DocumentCategoryAdded", "DocumentCategoryRenamed", "DocumentSizeLimitChanged", "BlockedExtensionAdded", "BlockedExtensionRemoved"];
  for (const type of logged) expect((await auditOf(type)).length, type).toBeGreaterThanOrEqual(1);
  evidence.settings = { categoryAdded: true, limitChecked: true, blockedAndUnblocked: true, builtInsLocked: 15 };
});

test("FAILURE: when the log cannot be written nothing is imported and the screen says so; once it is back the same click works", async () => {
  const page = sessions.OfficeManager.page;
  const before = (await documentsOf(admin)).length;
  sql("CREATE TRIGGER TR_E2E_RefuseDocAudit ON AuditLogEntries INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'DocumentImported') THROW 59040, 'audit unavailable', 1; INSERT INTO AuditLogEntries SELECT * FROM inserted; END");
  try {
    await gotoDocuments(page);
    await importFile(page, { name: "while-down.png", mimeType: "image/png", buffer: png("down") });
    await expect(importForm(page).getByRole("alert").filter({ hasText: "nothing was imported" })).toBeVisible();
    await shot(page, "07-log-unavailable.png");
    expect((await documentsOf(admin)).length).toBe(before);
  } finally {
    sql("DROP TRIGGER TR_E2E_RefuseDocAudit");
  }
  await importForm(page).getByRole("button", { name: "Import document" }).click();           // the same click, the same save key
  await expect(page.getByRole("status").filter({ hasText: "Imported while-down.png" })).toBeVisible();
  expect((await documentsOf(admin)).length).toBe(before + 1);
  evidence.failurePath = { importedWhileDown: 0, importedAfterRecovery: 1 };
});

test("TRUST: every document action is in the log with the user and a time, none names a file, a description or a patient, and the history numbers have no gap", async () => {
  const types = ["DocumentImported", "DocumentDuplicateImported", "DocumentCategorized", "DocumentMetadataEdited", "DocumentCategoryAdded", "DocumentSizeLimitChanged", "BlockedExtensionAdded"];
  const entries = (await Promise.all(types.map(auditOf))).flat();
  expect(entries.length).toBeGreaterThan(8);
  expect(entries.every((e) => e.performedByUserAccountId && !Number.isNaN(Date.parse(e.timestampUtc)))).toBe(true);
  const text = JSON.stringify(entries);
  expect(text).not.toMatch(/bitewing|referral\.pdf|notes\.txt|periodontist|outside lab|Alder|Same picture|while-down|\.png|\.pdf/i);
  expect(text).not.toContain(ids.ann);
  const numbers = sql("SELECT EventNumber FROM DocumentEvents ORDER BY EventNumber").split(/\r?\n/).map((s) => s.trim()).filter((s) => /^\d+$/.test(s)).map(Number);
  expect(numbers.length).toBeGreaterThanOrEqual(7);
  expect(numbers).toEqual(numbers.map((_, i) => i + 1));                                    // 1..N, no gap, no reuse
  evidence.trust = { auditEntries: entries.length, eventNumbers: numbers.length, gapFree: true, nameOrPatientInLog: false };
});

test("IMMUTABILITY: the database itself refuses to change or delete an imported file or its history", async () => {
  const refused = (statement: string) => { try { sql(statement); return false; } catch (e) { return /imported document|append-only|never/i.test(String((e as { stdout?: Buffer }).stdout ?? e)); } };
  expect(refused(`UPDATE Documents SET Sha256 = '${"b".repeat(64)}' WHERE Id = '${ids.png}'`)).toBe(true);
  expect(refused(`UPDATE Documents SET OriginalFileName = 'renamed.png' WHERE Id = '${ids.png}'`)).toBe(true);
  expect(refused(`DELETE FROM Documents WHERE Id = '${ids.png}'`)).toBe(true);
  expect(refused("UPDATE DocumentEvents SET Kind = 'Opened'")).toBe(true);
  expect(refused("DELETE FROM DocumentEvents")).toBe(true);
  expect(refused("DELETE FROM BlockedFileExtensions WHERE Extension = 'exe'")).toBe(true);
  evidence.immutability = { updateFile: "refused", deleteFile: "refused", history: "refused", builtInBlocked: "refused" };
});
