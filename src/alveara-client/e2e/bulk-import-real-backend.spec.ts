import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, utimesSync, writeFileSync } from "node:fs";
import path from "node:path";
import { sqlServer } from "./support/sqlServer";

// ALV-018-C02 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server.
// Excluded from the default mocked run (playwright.config.ts) and run with playwright.bulk-import.config.ts through scripts/Invoke-RealBackendWalkthrough.ps1 (see docs/testing/REAL_BACKEND_E2E.md).
// A dentist is refused everywhere. The administrator sends a malformed file (refused whole, with its problems), then a patient file with a rejected row, an exact repeat and a likely duplicate: mapping, dry-run
// (nothing written), the duplicate decided by a person, the commit, the reconciliation, the error report. The same file sent again registers nobody. An appointment file goes through the real scheduler (a clash, an
// unknown provider and a past date are rejected and nothing is double-booked). An opening-balance file becomes the first ledger entries and the real ledger agrees; a second balance for the same patient is refused.
// A front-desk user who has been granted only BulkImportData can prepare a balance batch but not commit it. A commit that stopped is retried from the screen and finishes. The stray-file sweep keeps every batch's
// own file. The audit log names who and when and no patient. The independent check at the end reads the database directly.

test.describe.configure({ mode: "serial", timeout: 300_000 });

const OUT = process.env.BULK_IMPORT_E2E_OUT ?? path.join(process.cwd(), "bulk-import-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {}, summaries: {}, verification: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "import-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future (an imported appointment must be in the future)
const OPENING_REASON = "Opening balance migrated from an import.";
const COMMIT_WAIT = 120_000; // the durable queue is polled every 10 s, a commit takes a few rounds of it

let adminContext: BrowserContext;
let admin: Page;
let dentistContext: BrowserContext;
let dentist: Page;
let deskContext: BrowserContext;
let desk: Page;
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
}

function sql(statement: string): string {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using (the independent check and the stuck-commit demonstration need it)");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", `SET NOCOUNT ON; ${statement}`], { stdio: "pipe" }).toString().trim();
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
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

async function registerPatient(first: string, last: string, dateOfBirth: string, phone: string): Promise<string> {
  const r = await api(admin, "post", "/api/patients", {
    firstName: first, lastName: last, dateOfBirth, sex: "Female", phone, email: null, addressLine1: "4 Pine St", addressLine2: null, city: "Austin", state: "TX", postalCode: "78701",
  }, { "Idempotency-Key": `imp-${first}-${last}-${stamp}` });
  expect(r.status, `register ${first} ${last}`).toBe(201);
  return r.body.id as string;
}

type Kind = "Patients" | "Appointments" | "Opening balances";

/** Sends a CSV through the real screen and returns the new batch's id (from the address the page opens). */
async function sendFile(page: Page, kind: Kind, label: string, content: string): Promise<string> {
  await page.goto("/admin/data-import");
  await expect(page.getByRole("heading", { name: "Data import" })).toBeVisible();
  await page.getByRole("radio", { name: kind }).check();
  await page.getByLabel(/Where the file came from/).fill(`${label} ${stamp}`);
  await page.getByLabel(/CSV file/).setInputFiles({ name: "import.csv", mimeType: "text/csv", buffer: Buffer.from(content, "utf-8") });
  await page.getByRole("button", { name: "Check and upload the file" }).click();
  await page.waitForURL(/\/admin\/data-import\/[0-9a-f-]{36}$/);
  return page.url().split("/").pop()!;
}

/** Saves the suggested mapping and runs the dry-run; returns the summary sentence the page shows. */
async function mapAndDryRun(page: Page): Promise<string> {
  await expect(page.getByRole("heading", { name: "Step 2: match the columns" })).toBeVisible();
  await page.getByRole("button", { name: "Save the mapping" }).click();
  await expect(page.getByText("Mapping saved.")).toBeVisible();
  await page.getByRole("button", { name: /^Run the dry-run$/ }).click();
  const summary = page.locator(".alv-import__summary").first();
  await expect(summary).toContainText(/rows?: \d+ valid/);
  return (await summary.innerText()).replace(/\s+/g, " ").trim();
}

const showRows = async (page: Page, status: string) => {
  await page.getByLabel("Show").selectOption(status);
  await expect(page.getByRole("table", { name: "Row results" })).toBeVisible();
};

async function commitValidRows(page: Page, count: number) {
  await page.getByRole("checkbox", { name: /I have checked the results/ }).check();
  await page.getByRole("button", { name: `Commit ${count} ${count === 1 ? "row" : "rows"}` }).click();
  await expect(page.getByRole("heading", { name: "What happened" })).toBeVisible({ timeout: COMMIT_WAIT });
  await expect(page.getByText("Reconciled", { exact: true })).toBeVisible({ timeout: COMMIT_WAIT });
}

const ledger = async (patientId: string) => (await api(admin, "get", `/api/patients/${patientId}/billing`)).body as any;
const verification = async (patientId: string) => (await api(admin, "get", `/api/patients/${patientId}/billing/verification`)).body as any;
const findPatients = async (query: string) => (await api(admin, "get", `/api/patients?q=${encodeURIComponent(query)}&includeInactive=false&take=25`)).body as any[];
const batch = async (id: string) => (await api(admin, "get", `/api/import-batches/${id}`)).body as any;

const PATIENT_HEADER = "First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip";
const APPOINTMENT_HEADER = "Patient First,Patient Last,DOB,Provider,Operatory,Type,Start,Duration,Notes";
const BALANCE_HEADER = "Patient First,Patient Last,DOB,Amount,As Of,Currency";
const csv = (header: string, ...rows: string[]) => `${header}\r\n${rows.join("\r\n")}\r\n`;

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext({ acceptDownloads: true });
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  await createUser("Dentist", `dentist-${stamp}`);
  await createUser("FrontDesk", `desk-${stamp}`);
  dentistContext = await browser.newContext();
  dentist = await dentistContext.newPage();
  await signIn(dentist, `dentist-${stamp}`, PASSWORD);
  deskContext = await browser.newContext();
  desk = await deskContext.newPage();
  await signIn(desk, `desk-${stamp}`, PASSWORD);
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "bulk-import-evidence.json"), JSON.stringify({ ...evidence, ids }, null, 2));
  await dentistContext?.close();
  await deskContext?.close();
  await adminContext?.close();
});

test("SETUP: the practice is configured and three patients are registered", async () => {
  const post = (url: string, data: unknown) => api(admin, "post", url, data);
  expect((await post("/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op1 = (await post("/api/config/operatories", { name: "Op 1" })).body.id;
  ids.op2 = (await post("/api/config/operatories", { name: "Op 2" })).body.id;
  ids.exam = (await post("/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id;
  const staff = await post("/api/config/staff", { displayName: "Dr. Rivera", jobTitle: "Dentist" });
  ids.provider = (await post("/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" })).body.id;
  const current = (await api(admin, "get", `/api/config/providers/${ids.provider}/availability`)).body;
  const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
  expect((await api(admin, "put", `/api/config/providers/${ids.provider}/availability`, { windows, revision: current.revision })).status).toBe(200);
  ids.ann = await registerPatient("Ann", "Rivera", "1950-01-10", "555-100-1000");
  ids.bo = await registerPatient("Bo", "Moss", "1962-03-04", "555-300-3000");
  ids.cy = await registerPatient("Cy", "Lee", "1980-05-05", "555-500-5000");
  expect(await api(admin, "get", "/api/import-batches").then((r) => r.body)).toEqual([]);
});

test("PERMISSION: a dentist has no menu entry, is refused the page by address, and every call is refused", async () => {
  await dentist.goto("/");
  await expect(dentist.getByRole("link", { name: "Data import" })).toHaveCount(0);
  await dentist.goto("/admin/data-import");
  await expect(dentist.getByText(/don't have permission/i)).toBeVisible();
  await shot(dentist, "01-dentist-refused.png");
  expect((await dentist.request.get("/api/import-batches")).status()).toBe(403);
  const sweep = await api(dentist, "post", "/api/import-batches/orphan-sweep");
  expect(sweep.status).toBe(403);
  evidence.dentist = { menuEntry: false, page: "permission denied", apiList: 403, apiSweep: sweep.status };
});

test("FILE: a malformed file is refused whole with its place in the file, and no batch is made", async () => {
  await admin.goto("/admin/data-import");
  await expect(admin.getByRole("link", { name: "Data import" })).toBeVisible();
  await admin.getByLabel(/Where the file came from/).fill(`Broken ${stamp}`);
  await admin.getByLabel(/CSV file/).setInputFiles({ name: "broken.csv", mimeType: "text/csv", buffer: Buffer.from(`${PATIENT_HEADER}\r\nDi,Wu,1990-09-09\r\n`) });
  await admin.getByRole("button", { name: "Check and upload the file" }).click();

  await expect(admin.getByText("The file was not accepted. Correct the problems listed and choose it again.")).toBeVisible();
  await expect(admin.getByRole("list", { name: "Problems in the file" })).toContainText("Row 2");
  await shot(admin, "02-malformed-file-refused.png");
  await scanBothThemes(admin, "list-with-problems");
  expect(admin.url()).toMatch(/\/admin\/data-import$/);
  expect((await api(admin, "get", "/api/import-batches")).body).toEqual([]);
  evidence.malformedFile = { refused: true, batchesAfter: 0 };
});

test("PATIENTS: mapping, a dry-run that writes nothing, a duplicate decided by a person, the commit, the reconciliation and the error report", async () => {
  const file = csv(PATIENT_HEADER,
    "Di,Wu,1990-09-09,555-700-7000,,4 Pine St,Austin,TX,78701",
    "Ed,Park,1971-02-02,555-222-2222,,4 Pine St,Austin,TX,78701",
    ",Quinn,1985-07-07,555-333-3333,,4 Pine St,Austin,TX,78701",
    "Ann,Rivera,1950-01-10,555-100-1000,,4 Pine St,Austin,TX,78701",
    "Anna,Rivera,1950-01-10,555-999-9999,,4 Pine St,Austin,TX,78701");
  ids.patientBatch = await sendFile(admin, "Patients", "Old system patients", file);
  await expect(admin.getByRole("heading", { name: `Patients: Old system patients ${stamp}` })).toBeVisible();
  await expect(admin.getByLabel("Field for column First Name")).toHaveValue("firstName");
  await shot(admin, "03-patients-mapping.png");

  const before = (await findPatients("Wu")).length + (await findPatients("Park")).length;
  const summary = await mapAndDryRun(admin);
  expect(summary).toContain("5 rows: 2 valid, 1 rejected, 1 skipped, 1 waiting for a decision.");
  expect((await findPatients("Wu")).length + (await findPatients("Park")).length, "the dry-run registered nobody").toBe(before);
  (evidence.summaries as Record<string, string>).patientsDryRun = summary;
  await showRows(admin, "Rejected");
  await expect(admin.getByRole("table", { name: "Row results" })).toContainText("firstName: First name is required.");
  await shot(admin, "04-patients-dry-run-rejected-rows.png");

  const [download] = await Promise.all([admin.waitForEvent("download"), admin.getByRole("button", { name: "Download the error report (CSV)" }).click()]);
  const reportPath = path.join(OUT, "patients-error-report.csv");
  await download.saveAs(reportPath);
  const report = readFileSync(reportPath, "utf-8");
  expect(report).toContain("First name is required.");
  for (const name of ["Quinn", "Rivera", "Anna"]) expect(report, `the error report must not repeat ${name} from the file`).not.toContain(name);
  expect(readFileSync(reportPath).subarray(0, 3)).toEqual(Buffer.from([0xef, 0xbb, 0xbf]));

  await expect(admin.getByRole("heading", { name: "Likely duplicates" })).toBeVisible();
  await expect(admin.getByRole("link", { name: "Open registered patient 1 (new tab)" })).toHaveAttribute("href", new RegExp(`/patients/${ids.ann}$`));
  await shot(admin, "05-patients-duplicate-queue.png");
  await admin.getByRole("button", { name: "This is a different person: register them" }).click();
  await expect(admin.getByText("No rows are waiting for a decision.")).toBeVisible();

  await expect(admin.getByRole("radio", { name: /All or nothing/ })).toBeDisabled();
  await expect(admin.getByText(/Every row must be valid or skipped/)).toBeVisible();
  await expect(admin.getByText(/This will write/)).toContainText("This will write 3 rows to the patient records.");
  await shot(admin, "06-patients-commit-step.png");
  await scanBothThemes(admin, "patients-batch");
  await commitValidRows(admin, 3);
  await expect(admin.getByText("Every row and every record is accounted for.")).toBeVisible();
  await shot(admin, "07-patients-reconciled.png");

  expect((await findPatients("Wu")).map((p) => p.firstName)).toEqual(["Di"]);
  expect((await findPatients("Park")).map((p) => p.firstName)).toEqual(["Ed"]);
  expect((await findPatients("Rivera")).map((p) => p.firstName).sort()).toEqual(["Ann", "Anna"]); // Anna was registered by the person's decision; Ann was not registered twice
  expect(await findPatients("Quinn")).toEqual([]);
  const done = await batch(ids.patientBatch);
  expect([done.status, done.commitOutcome, done.acceptedRows, done.rejectedRows, done.skippedRows]).toEqual(["Committed", "CompletedWithExceptions", 3, 1, 1]);
  evidence.patients = { registered: ["Di", "Ed", "Anna"], rejected: "Quinn", skippedExact: "Ann", status: done.status, outcome: done.commitOutcome };
});

test("PATIENTS AGAIN: the same file sent a second time registers nobody", async () => {
  const file = csv(PATIENT_HEADER,
    "Di,Wu,1990-09-09,555-700-7000,,4 Pine St,Austin,TX,78701",
    "Ed,Park,1971-02-02,555-222-2222,,4 Pine St,Austin,TX,78701",
    ",Quinn,1985-07-07,555-333-3333,,4 Pine St,Austin,TX,78701",
    "Ann,Rivera,1950-01-10,555-100-1000,,4 Pine St,Austin,TX,78701",
    "Anna,Rivera,1950-01-10,555-999-9999,,4 Pine St,Austin,TX,78701");
  ids.patientRepeat = await sendFile(admin, "Patients", "Old system patients again", file);
  expect(ids.patientRepeat).not.toBe(ids.patientBatch);

  const summary = await mapAndDryRun(admin);

  expect(summary).toContain("5 rows: 0 valid, 1 rejected, 4 skipped, 0 waiting for a decision.");
  await expect(admin.getByText("No row is valid, so there is nothing to commit.")).toBeVisible();
  await expect(admin.getByRole("button", { name: "Commit 0 rows" })).toBeDisabled();
  await shot(admin, "08-patients-again-nothing-to-commit.png");
  expect((await findPatients("Wu")).length).toBe(1);
  (evidence.summaries as Record<string, string>).patientsAgain = summary;
});

test("APPOINTMENTS: valid rows go through the real scheduler; a clash, an unknown provider and a past date are rejected; nothing is double-booked", async () => {
  const file = csv(APPOINTMENT_HEADER,
    `Ann,Rivera,1950-01-10,Dr. Rivera,Op 1,Exam,${DAY} 09:00,,`,
    `Bo,Moss,1962-03-04,Dr. Rivera,Op 2,Exam,${DAY} 09:30,,`,
    `Bo,Moss,1962-03-04,Dr. Rivera,Op 1,Exam,${DAY} 11:00,,`,
    `Cy,Lee,1980-05-05,Dr. Nobody,Op 1,Exam,${DAY} 13:00,,`,
    "Cy,Lee,1980-05-05,Dr. Rivera,Op 1,Exam,2020-01-06 09:00,,");
  ids.apptBatch = await sendFile(admin, "Appointments", "Old system appointments", file);
  const summary = await mapAndDryRun(admin);
  expect(summary).toContain("5 rows: 2 valid, 3 rejected, 0 skipped, 0 waiting for a decision.");
  (evidence.summaries as Record<string, string>).appointmentsDryRun = summary;
  await showRows(admin, "Rejected");
  const rows = admin.getByRole("table", { name: "Row results" });
  await expect(rows).toContainText("The provider is booked then");
  await expect(rows).toContainText("The start time has passed");
  await shot(admin, "09-appointments-dry-run-rejected.png");
  await expect(admin.getByRole("radio", { name: /All or nothing/ })).toBeDisabled();
  await expect(admin.getByText(/Appointments are recorded one at a time/)).toBeVisible();

  await commitValidRows(admin, 2);
  await shot(admin, "10-appointments-reconciled.png");

  // the imported appointments really occupy the schedule: the server refuses a booking on top of either
  const clash = (time: string, op: string) => api(admin, "post", "/api/appointments", { patientId: ids.cy, providerId: ids.provider, operatoryId: op, appointmentTypeId: ids.exam, startLocal: `${DAY}T${time}` }, { "Idempotency-Key": `imp-clash-${time}-${stamp}` });
  const first = await clash("09:30", ids.op2);
  const second = await clash("11:00", ids.op2);
  expect(first.status).toBeGreaterThanOrEqual(400);
  expect(second.status).toBeGreaterThanOrEqual(400);
  expect(JSON.stringify([first.body, second.body])).toMatch(/provider/i);
  const done = await batch(ids.apptBatch);
  expect([done.status, done.acceptedRows, done.rejectedRows]).toEqual(["Committed", 2, 3]);
  evidence.appointments = { accepted: 2, rejected: 3, directBookingOnTopOfImported: [first.status, second.status] };
});

test("BALANCES: valid rows become the first ledger entries and the real ledger agrees with the batch; a credit and an unknown patient are rejected", async () => {
  const file = csv(BALANCE_HEADER,
    "Ann,Rivera,1950-01-10,125.50,,",
    "Bo,Moss,1962-03-04,40.00,,",
    "Cy,Lee,1980-05-05,-5.00,,",
    "Zed,Ghost,1999-01-01,10.00,,");
  ids.balanceBatch = await sendFile(admin, "Opening balances", "Old system balances", file);
  const summary = await mapAndDryRun(admin);
  expect(summary).toContain("4 rows: 2 valid, 2 rejected, 0 skipped, 0 waiting for a decision.");
  expect(summary).toContain("$165.50");
  (evidence.summaries as Record<string, string>).balancesDryRun = summary;
  await showRows(admin, "Rejected");
  const rows = admin.getByRole("table", { name: "Row results" });
  await expect(rows).toContainText("A credit cannot be imported");
  await expect(rows).toContainText("No registered patient has this name and date of birth");
  await shot(admin, "11-balances-dry-run.png");
  await expect(admin.getByText(/This will write/)).toContainText("a total of $165.50");
  expect((await ledger(ids.ann)).hasAccount, "the dry-run wrote no ledger entry").toBe(false);

  await commitValidRows(admin, 2);
  await expect(admin.getByText(/held on the ledgers for this batch/)).toContainText("$165.50");
  await shot(admin, "12-balances-reconciled.png");
  await scanBothThemes(admin, "balances-reconciled");

  const ann = await ledger(ids.ann);
  const bo = await ledger(ids.bo);
  expect([ann.balanceOwed, ann.entries.length, ann.entries[0].kind, ann.entries[0].entryNumber, ann.entries[0].reason]).toEqual([125.5, 1, "Adjustment", 1, OPENING_REASON]);
  expect([bo.balanceOwed, bo.entries.length, bo.entries[0].kind]).toEqual([40, 1, "Adjustment"]);
  expect((await ledger(ids.cy)).hasAccount).toBe(false);
  expect([(await verification(ids.ann)).consistent, (await verification(ids.bo)).consistent]).toEqual([true, true]);
  evidence.balances = { ann: ann.balanceOwed, bo: bo.balanceOwed, cyHasAccount: false, ledgerVerification: "consistent" };
});

test("BALANCES AGAIN: a second balance for a patient who now has a ledger entry is refused, never added", async () => {
  ids.balanceRepeat = await sendFile(admin, "Opening balances", "Old system balances again", csv(BALANCE_HEADER, "Ann,Rivera,1950-01-10,99.00,,"));
  const summary = await mapAndDryRun(admin);

  expect(summary).toContain("1 row: 0 valid, 1 rejected, 0 skipped, 0 waiting for a decision.");
  await showRows(admin, "Rejected");
  await expect(admin.getByRole("table", { name: "Row results" })).toContainText("The patient's account already has entries");
  await shot(admin, "13-balance-account-not-empty.png");
  expect((await ledger(ids.ann)).balanceOwed).toBe(125.5);
  (evidence.summaries as Record<string, string>).balancesAgain = summary;
});

test("PERMISSION GATE: a front-desk user granted only BulkImportData prepares a balance batch but cannot commit it, on the screen or by the API", async () => {
  const version = (await api(admin, "get", "/api/permissions/effective")).body.version as number;
  const grant = await api(admin, "put", "/api/permissions/overrides", { role: "FrontDesk", permission: "BulkImportData", effect: "Grant", reason: "Walkthrough", expectedVersion: version, confirmed: true }, { "Idempotency-Key": `imp-grant-${stamp}` });
  expect(grant.status).toBe(200);

  ids.stuckBatch = await sendFile(desk, "Opening balances", "Front desk balance", csv(BALANCE_HEADER, "Cy,Lee,1980-05-05,10.00,,"));
  const summary = await mapAndDryRun(desk);
  expect(summary).toContain("1 row: 1 valid, 0 rejected, 0 skipped, 0 waiting for a decision.");
  await expect(desk.getByText(/also needs the Manage billing permission/).first()).toBeVisible();
  await expect(desk.getByRole("checkbox", { name: /I have checked the results/ })).toBeDisabled();
  await expect(desk.getByRole("button", { name: "Commit 1 row" })).toBeDisabled();
  await shot(desk, "14-front-desk-cannot-commit-balances.png");

  const direct = await api(desk, "post", `/api/import-batches/${ids.stuckBatch}/commit`, { policy: "CommitValidRows", expectedValidRows: 1 });
  expect([direct.status, direct.body.error]).toEqual([403, "billing_permission_required"]);
  expect((await ledger(ids.cy)).hasAccount, "nothing was written").toBe(false);
  expect((await batch(ids.stuckBatch)).status).toBe("DryRunComplete");
  evidence.permissionGate = { screenBlocked: true, apiStatus: direct.status, apiError: direct.body.error };
});

test("RETRY: a commit that stopped is shown as stopped and finishes when retried from the screen", async () => {
  // The jobs of a commit are lost (the documented stuck state): the batch says Committing and no job of its round exists.
  const admins = sql(`SELECT Id FROM UserAccounts WHERE Username = N'admin-${stamp}'`);
  sql(`UPDATE ImportBatches SET Status = N'Committing', Policy = N'CommitValidRows', CommitRound = 1, CommitStartedAtUtc = SYSDATETIMEOFFSET(), CommitConfirmedByUserId = '${admins}' WHERE Id = '${ids.stuckBatch}'`);
  expect(sql(`SELECT COUNT(*) FROM BackgroundJobs WHERE IdempotencyKey LIKE N'import-commit%${ids.stuckBatch.replace(/-/g, "")}%'`)).toBe("0");

  await admin.goto(`/admin/data-import/${ids.stuckBatch}`);
  await expect(admin.getByText(/The commit stopped before it finished/)).toBeVisible();
  await expect(admin.getByText("Commit stopped", { exact: true })).toBeVisible();
  await shot(admin, "15-commit-stopped.png");
  await admin.getByRole("button", { name: "Retry the commit" }).click();
  await expect(admin.getByRole("heading", { name: "What happened" })).toBeVisible({ timeout: COMMIT_WAIT });
  await expect(admin.getByText("Reconciled", { exact: true })).toBeVisible({ timeout: COMMIT_WAIT });
  await shot(admin, "16-retried-and-reconciled.png");

  const cy = await ledger(ids.cy);
  expect([cy.balanceOwed, cy.entries.length, cy.entries[0].reason]).toEqual([10, 1, OPENING_REASON]);
  const done = await batch(ids.stuckBatch);
  expect([done.status, done.commitRound, done.canRetry]).toEqual(["Committed", 2, false]);
  evidence.retry = { statusAfter: done.status, round: done.commitRound, cyBalance: cy.balanceOwed };
});

test("SWEEP: the sweep keeps every batch's file and, when it can reach the storage folder, removes only an old file no batch refers to", async () => {
  const total = ((await api(admin, "get", "/api/import-batches")).body as any[]).length;
  const storage = process.env.E2E_STORAGE_ROOT;
  const imports = storage ? path.join(storage, "imports") : null;
  const orphan = "0f0f0f0f0f0f4f0f8f0f0f0f0f0f0f01";
  const young = "0f0f0f0f0f0f4f0f8f0f0f0f0f0f0f02";
  if (imports) {
    mkdirSync(imports, { recursive: true });
    writeFileSync(path.join(imports, orphan), "stray");
    writeFileSync(path.join(imports, young), "stray");
    const old = new Date(Date.now() - 48 * 3600 * 1000);
    utimesSync(path.join(imports, orphan), old, old);
  }

  await admin.goto("/admin/data-import");
  await admin.getByRole("button", { name: "Remove stray files" }).click();
  await admin.getByRole("button", { name: "Yes, remove stray files" }).click();
  const expected = imports ? `Looked at ${total + 2} file(s): removed 1, kept ${total + 1}.` : `Looked at ${total} file(s): removed 0, kept ${total}.`;
  await expect(admin.getByText(expected)).toBeVisible();
  await shot(admin, "17-sweep.png");
  if (imports) {
    expect(existsSync(path.join(imports, orphan)), "the old stray file is gone").toBe(false);
    expect(existsSync(path.join(imports, young)), "the young stray file is kept").toBe(true);
  }
  evidence.sweep = { batchFiles: total, strayFileRemovalExercised: !!imports };
  await scanBothThemes(admin, "list-with-batches");
});

test("AUDIT: the log names who and when for every import step and carries no patient, no date of birth and no file value", async () => {
  const log = ((await api(admin, "get", "/api/auth/audit-log?take=500")).body as any[]).filter((e) => /^Import/.test(e.eventType) || e.eventType === "BillingOpeningBalanceRecorded");
  const count = (type: string) => log.filter((e) => e.eventType === type).length;
  expect(count("ImportBatchCreated")).toBe(6);
  expect(count("ImportFileRejected")).toBe(1);
  expect(count("ImportDryRunCompleted")).toBe(6);
  expect(count("ImportCommitStarted")).toBe(3);
  expect(count("ImportCommitRetried")).toBe(1);
  expect(count("ImportRowDecided")).toBe(1);
  expect(count("BillingOpeningBalanceRecorded")).toBe(3);
  const text = log.map((e) => `${e.details ?? ""} ${e.reason ?? ""}`).join("\n");
  for (const word of ["Wu", "Park", "Quinn", "Anna", "Rivera", "Moss", "Lee", "Ghost", "1950-01-10", "1962-03-04", "555-"]) expect(text, `the import audit entries must not contain ${word}`).not.toMatch(new RegExp(`\\b${word}`));
  await admin.goto("/admin/audit-log");
  await expect(admin.getByRole("heading", { name: /Audit/i }).first()).toBeVisible();
  await shot(admin, "18-audit-log.png");
  evidence.audit = Object.fromEntries(["ImportBatchCreated", "ImportFileRejected", "ImportDryRunCompleted", "ImportRowDecided", "ImportCommitStarted", "ImportCommitRetried", "BillingOpeningBalanceRecorded"].map((t) => [t, count(t)]));
});

test("INDEPENDENT CHECK: the database itself agrees with everything the screens showed", async () => {
  const q = (statement: string) => sql(statement);
  const check = {
    importedPatients: Number(q("SELECT COUNT(*) FROM Patients WHERE RegistrationKey LIKE N'import-%'")),
    importedAppointments: Number(q("SELECT COUNT(*) FROM Appointments WHERE ScheduleKey LIKE N'import-appt-%'")),
    importedLedgerEntries: Number(q(`SELECT COUNT(*) FROM BillingEntries WHERE Reason = N'${OPENING_REASON}'`)),
    importedLedgerTotal: Number(q(`SELECT COALESCE(SUM(Amount), 0) FROM BillingEntries WHERE Reason = N'${OPENING_REASON}'`)),
    entryNumberOnePerAccount: Number(q(`SELECT COUNT(*) FROM BillingEntries WHERE Reason = N'${OPENING_REASON}' AND EntryNumber = 1`)),
    entriesWithoutBatchMark: Number(q(`SELECT COUNT(*) FROM BillingEntries WHERE Reason = N'${OPENING_REASON}' AND CorrelationId IS NULL`)),
    batchesCommitted: Number(q("SELECT COUNT(*) FROM ImportBatches WHERE Status = N'Committed'")),
    batchesOpen: Number(q("SELECT COUNT(*) FROM ImportBatches WHERE Status IN (N'DryRunComplete', N'Uploaded', N'Committing')")),
    acceptedRowsInAllBatches: Number(q("SELECT COUNT(*) FROM ImportBatchRows WHERE Status = N'Accepted'")),
    acceptedRowsWithoutResult: Number(q("SELECT COUNT(*) FROM ImportBatchRows WHERE Status = N'Accepted' AND ResultRecordId IS NULL")),
    patientsNamedQuinn: Number(q("SELECT COUNT(*) FROM Patients WHERE LastName = N'Quinn'")),
    jobsStillPending: Number(q("SELECT COUNT(*) FROM BackgroundJobs WHERE JobType = N'import.commit' AND Status IN (0, 1)"))   // 0 Pending, 1 InProgress,
  };
  evidence.verification = check;
  expect(check).toEqual({
    importedPatients: 3, importedAppointments: 2, importedLedgerEntries: 3, importedLedgerTotal: 175.5, entryNumberOnePerAccount: 3, entriesWithoutBatchMark: 0,
    batchesCommitted: 4, batchesOpen: 2, acceptedRowsInAllBatches: 3 + 2 + 2 + 1, acceptedRowsWithoutResult: 0, patientsNamedQuinn: 0, jobsStillPending: 0,
  });
});
