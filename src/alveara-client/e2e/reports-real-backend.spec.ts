import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N006 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server. Five appointments are booked (two cancelled) and a payment is recorded through the real API. Each role
// sees only the report groups it holds; the schedule, collections and measurement numbers on the screen equal an INDEPENDENT count made straight in SQL; a number opens the records behind it; a CSV is downloaded and
// holds the screen's numbers; the export and the run are recorded in the audit log; a baseline is set once and compared honestly (no change is shown where the samples are too small, and nothing is called an
// improvement); the honest states (nothing to report, a range that is too long, a role without access) are shown; the print layout leaves off the menu and the filters; the screens are scanned in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.REPORTS_E2E_OUT ?? path.join(process.cwd(), "reports-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {}, sql: {}, screen: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
const DAY = "2030-01-14"; // a Monday far in the future
const pad = (n: number) => String(n).padStart(2, "0");
const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const daysFromToday = (n: number) => iso(new Date(Date.now() + n * 86_400_000));
let adminContext: BrowserContext;
let admin: Page;
type Ctx = { context: BrowserContext; page: Page };
const sessions: Record<string, Ctx> = {};
let stamp = 0;
const ids: Record<string, string> = {};

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function call(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, any> };
}

function sql(statement: string) {
  const db = process.env.E2E_DB_NAME;
  if (!db) throw new Error("E2E_DB_NAME must name the database the API is using");
  return execFileSync("sqlcmd", ["-S", sqlServer, "-d", db, "-I", "-b", "-h", "-1", "-W", "-Q", `SET NOCOUNT ON; ${statement}`], { stdio: "pipe" }).toString().trim();
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

const report = (id: string, from: string, to: string) => `/reports/${id}?from=${from}&to=${to}`;
const groupsOf = async (page: Page) => page.getByRole("navigation", { name: "Reports" }).getByRole("heading", { level: 2 }).allTextContents();
const card = (page: Page, label: string) => page.getByRole("list", { name: "Summary" }).getByRole("listitem").filter({ hasText: label });

async function download(page: Page, button: string): Promise<{ name: string; text: string }> {
  const [file] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: button }).click()]);
  return { name: file.suggestedFilename(), text: readFileSync((await file.path())!, "utf8") };
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["FrontDesk", "Billing", "Dentist", "OfficeManager"]) {
    ids[role] = await createUser(role, `${role.toLowerCase()}-${stamp}`);
    const context = await adminContext.browser()!.newContext();
    const page = await context.newPage();
    await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
    sessions[role] = { context, page };
  }
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "reports-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a practice, five appointments on one day (two cancelled) and a 40 payment on an account holding 100", async () => {
  const desk = sessions.FrontDesk.page;
  expect((await call(admin, "post", "/api/config/locations", { name: "Main Office" })).status).toBe(201);
  ids.op1 = (await call(admin, "post", "/api/config/operatories", { name: "Op 1" })).body.id as string;
  ids.exam = (await call(admin, "post", "/api/config/appointment-types", { name: "Exam", defaultDurationMinutes: 60 })).body.id as string;
  const staff = await call(admin, "post", "/api/config/staff", { displayName: "Dr. Rivera", jobTitle: "Dentist" });
  ids.rivera = (await call(admin, "post", "/api/config/providers", { staffProfileId: staff.body.id, specialty: "General dentistry" })).body.id as string;
  const current = (await (await admin.request.get(`/api/config/providers/${ids.rivera}/availability`)).json()) as { revision: number };
  const windows = [1, 2, 3, 4, 5].map((d) => ({ dayOfWeek: d, startLocal: "08:00", endLocal: "17:00" }));
  expect((await call(admin, "put", `/api/config/providers/${ids.rivera}/availability`, { windows, revision: current.revision })).status).toBe(200);

  const people: [string, string, string, string][] = [["Ann", "Arrival", "1985-03-09", "555-010-0100"], ["Bo", "Bridge", "1990-01-01", "555-020-0200"], ["Cy", "Crown", "1975-05-05", "555-030-0300"], ["Di", "Denture", "1980-07-07", "555-040-0400"], ["Ed", "Enamel", "1970-02-02", "555-050-0500"]];
  const booked: { id: string; rowVersion: string }[] = [];
  for (const [i, [first, last, dob, phone]] of people.entries()) {
    const p = await call(desk, "post", "/api/patients", { firstName: first, lastName: last, dateOfBirth: dob, sex: "Female", phone, addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `reports-e2e-${first}-${stamp}` });
    expect(p.status).toBe(201);
    ids[first.toLowerCase()] = p.body.id as string;
    const a = await call(desk, "post", "/api/appointments", { patientId: p.body.id, providerId: ids.rivera, operatoryId: ids.op1, appointmentTypeId: ids.exam, startLocal: `${DAY}T${pad(8 + i)}:00` }, { "Idempotency-Key": `reports-appt-${i}-${stamp}` });
    expect(a.status, JSON.stringify(a.body)).toBe(201);
    booked.push({ id: a.body.id as string, rowVersion: a.body.rowVersion as string });
  }
  for (const a of booked.slice(0, 2)) expect((await call(desk, "post", `/api/appointments/${a.id}/cancel`, { reason: "Patient asked", rowVersion: a.rowVersion })).status).toBe(200);

  const billing = sessions.Billing.page;
  const account = async () => (await (await billing.request.get(`/api/patients/${ids.ann}/billing`)).json()) as { rowVersion: string };
  // An account opens only with a charge or an imported opening balance, so the 100 starting line is seeded straight into this walkthrough's own database; the payment goes through the real API.
  sql(`DECLARE @a uniqueidentifier = NEWID(); INSERT INTO BillingAccounts (Id, PatientId, CreatedAtUtc, CreatedByUserId) VALUES (@a, '${ids.ann}', SYSDATETIMEOFFSET(), '${ids.Billing}'); `
    + `INSERT INTO BillingEntries (Id, AccountId, PatientId, EntryNumber, Kind, Amount, Reason, IdempotencyKey, RecordedAtUtc, RecordedByUserId) VALUES (NEWID(), @a, '${ids.ann}', 1, 'Adjustment', 100, 'Walkthrough balance', 'rep-adj-${stamp}', SYSDATETIMEOFFSET(), '${ids.Billing}')`);
  const payment = await call(billing, "post", `/api/patients/${ids.ann}/billing/payments`, { amount: 40, method: "Cash", note: null, rowVersion: (await account()).rowVersion, idempotencyKey: `rep-pay-${stamp}` });
  expect(payment.status, `payment: ${JSON.stringify(payment.body)}`).toBe(200);

  evidence.sql = { appointments: sql("SELECT COUNT(*) FROM Appointments"), cancelled: sql("SELECT COUNT(*) FROM Appointments WHERE Status = 'Cancelled'"), paymentsReceived: sql("SELECT CAST(-SUM(Amount) AS varchar(20)) FROM BillingEntries WHERE Kind = 'Payment'") };
  expect(evidence.sql).toEqual({ appointments: "5", cancelled: "2", paymentsReceived: "40.00" });
});

test("ACCESS: each role sees only the report groups it holds, and a report it does not hold is refused on the page and by the server", async () => {
  const groups: Record<string, string[]> = { FrontDesk: ["Operational"], Billing: ["Financial"], Dentist: ["Clinical"], OfficeManager: ["Operational", "Financial", "Clinical", "Measurement"] };
  for (const [role, expected] of Object.entries(groups)) {
    const page = sessions[role].page;
    await page.goto("/reports");
    await expect(page.getByRole("heading", { name: "Reports", level: 1 })).toBeVisible();
    expect(await groupsOf(page), role).toEqual(expected);
  }
  const traces = () => Number(sql("SELECT COUNT(*) FROM AuditLogEntries WHERE EventType IN ('ReportViewed','ReportExported')"));
  const before = traces();                      // the pages above ran reports for the roles that may see them; what follows must add nothing
  const billing = sessions.Billing.page;
  await billing.goto(report("schedule", DAY, DAY));
  await expect(billing.getByText("That report is not available to you")).toBeVisible();
  await shot(billing, "01-billing-not-available.png");
  expect((await billing.request.get(`/api/reports/schedule?from=${DAY}&to=${DAY}`)).status()).toBe(403);
  expect((await billing.request.get(`/api/reports/schedule/export?from=${DAY}&to=${DAY}`)).status()).toBe(403);
  expect((await sessions.Dentist.page.request.get(`/api/reports/production?from=${DAY}&to=${DAY}`)).status()).toBe(403);
  expect((await sessions.FrontDesk.page.request.get(`/api/reports/measurement/metrics?from=${DAY}&to=${DAY}`)).status()).toBe(403);
  expect(traces()).toBe(before);                // every refusal left no report trace
});

test("SCHEDULE: the numbers equal an independent SQL count, a number opens its records, and the CSV holds the same numbers", async () => {
  const page = sessions.FrontDesk.page;
  await page.goto(report("schedule", DAY, DAY));
  await expect(page.getByRole("heading", { name: "Schedule", level: 2 })).toBeVisible();
  await expect(card(page, "Appointments")).toContainText("5");
  await expect(card(page, "Cancelled")).toContainText("2");
  await expect(card(page, "Scheduled")).toContainText("3");
  evidence.screen = { ...(evidence.screen as object), schedule: { appointments: 5, cancelled: 2, scheduled: 3 } };
  await shot(page, "02-schedule.png");

  await page.getByRole("button", { name: `2, show the records: ${DAY}, cancelled` }).click();
  const records = page.getByRole("region", { name: /^Records: / });
  await expect(records.getByRole("row")).toHaveCount(3);                       // the header and the two cancelled appointments
  await expect(records).toContainText("Cancelled");
  await shot(page, "03-schedule-records.png");
  await records.getByRole("button", { name: "Close the list" }).click();
  await scanBothThemes(page, "schedule");

  const file = await download(page, "Download CSV");
  expect(file.name).toBe(`schedule_${DAY}_to_${DAY}.csv`);
  expect(file.text).toContain("Report,schedule\r\n");
  expect(file.text).toContain("Totals - Appointments,5\r\n");
  expect(file.text).toContain("Totals - Cancelled,2\r\n");
  await expect(page.getByText(`Downloaded schedule_${DAY}_to_${DAY}.csv.`)).toBeVisible();
  expect(file.text).not.toMatch(/Arrival|Bridge|Crown|Denture|Enamel/);          // no patient name is in the file
});

test("AUDIT: the run and the export are two separate records, with counts and no names", async () => {
  expect(sql("SELECT COUNT(*) FROM AuditLogEntries WHERE EventType = 'ReportExported'")).toBe("1");
  expect(Number(sql("SELECT COUNT(*) FROM AuditLogEntries WHERE EventType = 'ReportViewed'"))).toBeGreaterThanOrEqual(2);       // the report, then the records behind a number (and the export's own run)
  const details = sql("SELECT Details FROM AuditLogEntries WHERE EventType = 'ReportExported'");
  expect(details).toMatch(/^Report schedule exported as CSV: 1 day\(s\), \d+ line\(s\)\.$/);
  expect(sql("SELECT COUNT(*) FROM AuditLogEntries WHERE EventType IN ('ReportViewed','ReportExported') AND (Details LIKE '%Arrival%' OR Details LIKE '%Ann%' OR Details LIKE '%Patient asked%')")).toBe("0");
  evidence.audit = { exportDetails: details };
});

test("COLLECTIONS: money equals the ledger, the proof agrees with it, and the print layout leaves off the menu and the filters", async () => {
  const page = sessions.Billing.page;
  const from = daysFromToday(-3);
  const to = daysFromToday(1);
  await page.goto(report("collections", from, to));
  await expect(page.getByRole("heading", { name: "Collections and payments", level: 2 })).toBeVisible();
  await expect(card(page, "Received")).toContainText("$40.00");
  await expect(card(page, "Net collections")).toContainText("$40.00");
  await expect(page.getByText(/The lines add up to the ledger: .* checked against an independent sum of the same entries\./)).toBeVisible();
  await expect(page.getByText(/The lines do not agree with the ledger/)).toHaveCount(0);
  await shot(page, "04-collections.png");
  await scanBothThemes(page, "collections");

  await page.emulateMedia({ media: "print" });
  await expect(page.getByRole("navigation", { name: "Reports" })).toBeHidden();
  await expect(page.getByRole("form", { name: "Report filters" })).toBeHidden();
  await expect(page.getByRole("button", { name: "Download CSV" })).toBeHidden();
  await expect(page.getByRole("button", { name: "Print" })).toBeHidden();
  await shot(page, "05-collections-print.png");
  await expect(page.getByRole("list", { name: "Summary" })).toBeVisible();                  // the report itself prints (the first run of this walkthrough found a blank page here)
  await expect(page.getByText(/The lines add up to the ledger/)).toBeVisible();
  await page.emulateMedia({ media: "screen" });
  await expect(page.getByRole("navigation", { name: "Reports" })).toBeVisible();

  const file = await download(page, "Download CSV");
  const received = /Payments received,(-?[0-9.]+)\r\n/.exec(file.text);
  expect(received && Number(received[1])).toBe(40);
  expect(file.text).toMatch(/Reconciled,Yes/);
  evidence.screen = { ...(evidence.screen as object), collections: { received: 40 } };
});

test("HONEST STATES: nothing to report, a range that is too long, and a day the server refuses are said in words", async () => {
  const page = sessions.FrontDesk.page;
  await page.goto(report("schedule", "2031-05-01", "2031-05-03"));
  await expect(page.getByText("Nothing to report")).toBeVisible();
  await shot(page, "06-nothing-to-report.png");
  await page.goto(report("schedule", "2025-01-01", "2027-01-01"));
  await expect(page.getByText("That range is longer than a report can cover. Choose a shorter one.")).toBeVisible();
  await shot(page, "07-range-too-long.png");
  expect(sql("SELECT COUNT(*) FROM AuditLogEntries WHERE EventType = 'ReportViewed' AND Details LIKE '%731 day%'")).toBe("0");      // a refused range records nothing
});

test("MEASUREMENT: the metrics equal the SQL count, a baseline is set once, and the comparison shows no change where it cannot be honest", async () => {
  const page = sessions.OfficeManager.page;
  await page.goto(report("measurement", DAY, DAY));
  const table = page.getByRole("table", { name: "Measured metrics for the period" });
  await expect(table.getByRole("row", { name: /^Cancellations 2 5 40\.0% Version 1/ })).toBeVisible();
  await expect(page.getByText("There is no baseline yet, so nothing can be compared and no change is shown.")).toBeVisible();
  await shot(page, "08-measurement-no-baseline.png");

  await expect(page.getByRole("main")).toHaveCount(1);                       // one main landmark on the page (the hub used to nest a second one inside the shell's)
  const form = page.getByRole("form", { name: "Set a baseline" });
  await form.getByLabel("Name").fill("Walkthrough baseline");
  await form.getByLabel("First day").fill("2030-01-01");
  await form.getByLabel("Last day").fill("2030-01-31");
  await expect(form.getByRole("alert")).toContainText("A baseline must be a period that is already over");
  await expect(form.getByRole("button", { name: "Save the baseline" })).toBeDisabled();
  expect(sql("SELECT COUNT(*) FROM ReportBaselines")).toBe("0");

  await form.getByLabel("First day").fill("2020-01-01");
  await form.getByLabel("Last day").fill("2020-01-31");
  await form.getByRole("button", { name: "Save the baseline" }).click();
  await expect(page.getByText('Baseline "Walkthrough baseline" saved for 2020-01-01 to 2020-01-31.')).toBeVisible();
  expect(sql("SELECT COUNT(*) FROM ReportBaselines")).toBe("1");
  expect(sql("SELECT COUNT(*) FROM ReportBaselineMetrics")).toBe("7");
  expect(sql("SELECT COUNT(*) FROM AuditLogEntries WHERE EventType = 'ReportBaselineSet'")).toBe("1");

  const comparison = page.getByRole("table", { name: "Each metric in this period against the baseline" });
  await expect(comparison.getByRole("row", { name: /Cancellations/ })).toContainText("No change shown");
  await expect(comparison.getByRole("row", { name: /Cancellations/ })).toContainText("too few records");
  await expect(page.getByText("A difference between two periods shows that the numbers differ. It does not show what caused the difference.")).toBeVisible();
  expect(await page.locator(".alv-rep__main").innerText()).not.toMatch(/improv|better|worse/i);
  await expect(page.getByRole("heading", { name: "What cannot be measured yet" })).toBeVisible();
  await expect(page.getByText(/a product target \(a 20 to 30 percent reduction\), not a measurement/)).toBeVisible();
  await shot(page, "09-measurement-compared.png");
  await scanBothThemes(page, "measurement");

  const file = await download(page, "Download comparison CSV");
  expect(file.text).toContain("Report,measurement-comparison\r\n");
  expect(file.text.replace("Lower is better", "")).not.toMatch(/improv|better|worse/i);      // the metric's direction is a column header; no value or sentence calls a change good or bad
  evidence.screen = { ...(evidence.screen as object), measurement: { cancellations: "2 of 5", baselines: 1, metricsStored: 7 } };
});
