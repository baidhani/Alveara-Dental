import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { execFileSync } from "node:child_process";
import { sqlServer } from "./support/sqlServer";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-009-C02 demo, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server, with the real background pass (every 3 s here, 300 s by default).
// An office manager adds a lab vendor and a referral provider; a dentist starts a referral (the form is checked first), sends it with a past date and links a patient document; the case is overdue, so within seconds the
// system makes an unassigned chase task that the front desk finds in the work queue and follows back to the case; recording the report and completing the case closes the task by itself; a lab case goes out, comes
// back and is sent back for a remake (which needs a note); a change by someone else is explained; a document whose file goes missing is shown as a broken link; a role that only reads has no buttons and billing sees
// nothing; the audit trail names the actions; the screens are scanned in both themes.

test.describe.configure({ mode: "serial" });

const OUT = process.env.EXTERNAL_CASES_E2E_OUT ?? path.join(process.cwd(), "external-cases-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "plan-pass-1!";
const sessions: Record<string, { context: BrowserContext; page: Page }> = {};
let adminContext: BrowserContext;
let admin: Page;
let stamp = 0;
const ids: Record<string, string> = {};
const names: Record<string, string> = {};
const PROVIDER = "Riverside Endodontics";
const VENDOR = "Smile Dental Lab";
const REASON = "Retreatment of tooth 19";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;

async function api(page: Page, method: "post" | "put" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as any };
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

/** A date input value some days from today on this machine's clock (the browser runs here too). */
function dayInput(daysFromToday: number): string {
  const d = new Date(Date.now() + daysFromToday * 86_400_000);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** Waits for the real background pass (every 3 s here) rather than sleeping a fixed time. */
async function until(check: () => boolean, what: string, seconds = 45) {
  const end = Date.now() + seconds * 1000;
  while (Date.now() < end) { if (check()) return; await new Promise((r) => setTimeout(r, 1000)); }
  throw new Error(`Timed out waiting for ${what}`);
}

const referralId = () => sql(`SELECT TOP 1 CONVERT(varchar(36), Id) FROM ReferralCases WHERE PatientId = '${ids.ann}' ORDER BY CreatedAtUtc`);
const chaseTasks = (kind: string) => sql(`SELECT CONCAT(Status, ',', ClosedBySystem, ',', CASE WHEN AssigneeUserId IS NULL THEN 'unassigned' ELSE 'assigned' END, ',', Priority) FROM FollowUpTasks WHERE Kind = N'${kind}'`).split(/\r?\n/).filter(Boolean);

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  for (const role of ["OfficeManager", "Dentist", "Hygienist", "FrontDesk", "Billing"]) {
    names[role] = `${role.toLowerCase()}-${stamp}`;
    ids[role] = await createUser(role, names[role]);
    const context = await adminContext.browser()!.newContext();
    const page = await context.newPage();
    await signIn(page, names[role], PASSWORD);
    sessions[role] = { context, page };
  }
  const patient = await api(sessions.FrontDesk.page, "post", "/api/patients", { firstName: "Ann", lastName: "Alder", dateOfBirth: "1985-03-09", sex: "Female", phone: "555-010-0100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `ec-ann-${stamp}` });
  expect(patient.status).toBe(201);
  ids.ann = patient.body.id as string;
  evidence.stamp = stamp;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "external-cases-evidence.json"), JSON.stringify(evidence, null, 2));
  for (const s of Object.values(sessions)) await s.context.close();
  await adminContext?.close();
});

test("SETUP: a patient with a radiograph already in the document library", async () => {
  ids.doc = "e0000000-0000-4000-8000-" + String(stamp).padStart(12, "0").slice(-12);
  sql(`INSERT INTO Documents (Id, PatientId, CategoryId, DocumentDate, Description, OriginalFileName, DetectedType, ContentType, SizeBytes, Sha256, BlobId, ImportedByUserId, ImportedAtUtc, IdempotencyKey, RequestHash)
       VALUES ('${ids.doc}', '${ids.ann}', 'd0c00000-0000-0000-0000-000000000001', '2026-10-01', N'Panoramic film', N'pano.png', N'png', N'image/png', 2048, REPLICATE('a', 64), NEWID(), '${ids.Dentist}', SYSDATETIMEOFFSET(), N'doc-${stamp}', REPLICATE('a', 64))`);
  expect(sql(`SELECT COUNT(*) FROM Documents WHERE PatientId = '${ids.ann}'`)).toBe("1");
});

test("PARTIES: the office manager adds a referral provider and a lab vendor; a dentist cannot", async () => {
  const mgr = sessions.OfficeManager.page;
  await mgr.goto("/admin/external-parties");
  await expect(mgr.getByRole("heading", { name: "Outside parties" })).toBeVisible();
  await mgr.getByRole("button", { name: "Add the party" }).click();
  await expect(mgr.getByRole("alert")).toContainText("Write the name");                                         // checked before anything is sent
  await mgr.getByLabel("Kind").selectOption("ReferralProvider");
  await mgr.getByLabel(/^Name/).fill(PROVIDER);
  await mgr.getByLabel(/^Contact details/).fill("555-030-0300");
  await mgr.getByRole("button", { name: "Add the party" }).click();
  await expect(mgr.getByText(`Added: ${PROVIDER}.`)).toBeVisible();
  await mgr.getByLabel("Kind").selectOption("LabVendor");
  await mgr.getByLabel(/^Name/).fill(VENDOR);
  await mgr.getByRole("button", { name: "Add the party" }).click();
  await expect(mgr.getByText(`Added: ${VENDOR}.`)).toBeVisible();
  await shot(mgr, "01-parties.png");
  await scanBothThemes(mgr, "outside-parties");
  expect((await api(sessions.Dentist.page, "post", "/api/external-parties", { kind: "LabVendor", name: "Not allowed" })).status).toBe(403);
  expect(sql("SELECT COUNT(*) FROM ExternalParties")).toBe("2");
});

test("REFERRAL: the dentist starts it (the form is checked first), sends it with a past date and links the radiograph", async () => {
  const dr = sessions.Dentist.page;
  await dr.goto(`/patients/${ids.ann}/external-cases`);
  await expect(dr.getByRole("heading", { name: "Referrals and lab cases" })).toBeVisible();
  await expect(dr.getByText("No referrals or lab cases")).toBeVisible();
  await dr.getByRole("button", { name: "New referral" }).click();
  await dr.getByRole("button", { name: "Save the referral" }).click();
  await expect(dr.getByText("Choose who the patient is being referred to.")).toBeVisible();
  await expect(dr.getByText(/^Write the reason for the referral/).first()).toBeVisible();
  expect(sql("SELECT COUNT(*) FROM ReferralCases")).toBe("0");
  await dr.getByLabel(/^Refer to/).selectOption({ label: PROVIDER });
  await dr.getByLabel(/^Reason for the referral/).fill(REASON);
  await dr.getByLabel(/^Follow up by/).fill(dayInput(-1));                                                    // already due: yesterday
  await shot(dr, "02-new-referral.png");
  await dr.getByRole("button", { name: "Save the referral" }).click();
  await expect(dr.getByText(`Saved: ${REASON}.`)).toBeVisible();
  expect(sql("SELECT COUNT(*) FROM ReferralCases")).toBe("1");
  expect(chaseTasks("ReferralFollowUp")).toHaveLength(0);                                                      // a draft has not gone out: no chase

  await dr.getByRole("link", { name: REASON }).click();
  await expect(dr.getByRole("heading", { name: REASON })).toBeVisible();
  await dr.getByRole("button", { name: "Mark as sent" }).click();
  await dr.getByLabel("Date it went out").fill(dayInput(-4));
  await dr.getByRole("button", { name: "Mark as sent" }).last().click();
  await expect(dr.getByText("Moved to Sent.")).toBeVisible();
  await expect(dr.getByText("Overdue")).toBeVisible();                                                         // gone out, past its follow-up date: said in words
  await dr.getByLabel(/Link another of this patient/).selectOption(ids.doc);
  await dr.getByRole("button", { name: "Link document" }).click();
  await expect(dr.getByText("Document linked.")).toBeVisible();
  await expect(dr.getByText("pano.png")).toBeVisible();
  await shot(dr, "03-referral-detail.png");
  expect(sql(`SELECT COUNT(*) FROM Documents WHERE PatientId = '${ids.ann}'`)).toBe("1");                     // linked, not copied
  expect(sql("SELECT COUNT(*) FROM ExternalCaseDocuments")).toBe("1");
});

test("CHASE: within seconds the system makes an unassigned task; the front desk finds it in the work queue and follows it to the case", async () => {
  await until(() => chaseTasks("ReferralFollowUp").length > 0, "the chase task");
  await new Promise((r) => setTimeout(r, 7000));                                                               // more passes: still exactly one
  expect(chaseTasks("ReferralFollowUp")).toEqual(["Open,0,unassigned,High"]);
  const fd = sessions.FrontDesk.page;
  await fd.goto("/work-queue");
  const row = fd.getByRole("row").filter({ hasText: `Referral to follow up: ${PROVIDER}` });
  await expect(row).toBeVisible();
  await expect(row).toContainText("Unassigned");
  await expect(row).toContainText("Overdue");
  await shot(fd, "04-queue-chase.png");
  await scanBothThemes(fd, "work-queue-chase");
  await row.getByRole("link", { name: "Open the case" }).click();
  await expect(fd.getByRole("heading", { name: REASON })).toBeVisible();
  await expect(fd.getByText(/a follow-up task is open in the/)).toBeVisible();
});

test("COMPLETE: recording the report and completing the case closes the chase task by itself", async () => {
  const dr = sessions.Dentist.page;
  await dr.goto(`/external-cases/referrals/${referralId()}`);
  await dr.getByRole("button", { name: "Report received" }).click();
  await dr.getByRole("button", { name: "Report received" }).last().click();
  await expect(dr.getByText("Moved to Received.")).toBeVisible();
  await dr.getByRole("button", { name: "Mark complete" }).click();
  await dr.getByLabel(/^Note/).fill("Report reviewed with the patient");
  await dr.getByRole("button", { name: "Mark complete" }).last().click();
  await expect(dr.getByText("Moved to Completed.")).toBeVisible();
  await expect(dr.getByRole("button", { name: "Edit" })).toHaveCount(0);                                       // a finished case takes no changes
  await shot(dr, "05-referral-completed.png");
  await until(() => chaseTasks("ReferralFollowUp")[0]?.startsWith("Completed,1") === true, "the system to close the chase task");
  await scanBothThemes(dr, "case-detail");
  const history = dr.getByRole("list", { name: "Case history" });
  await expect(history).toContainText("Draft to Sent by");
  await expect(history).toContainText("Received to Completed by");
});

test("LAB: a lab case goes out and back and is sent back for a remake (which needs a note); a change by someone else is explained", async () => {
  const dr = sessions.Dentist.page;
  await dr.goto(`/patients/${ids.ann}/external-cases`);
  await dr.getByRole("button", { name: "New lab case" }).click();
  await dr.getByLabel(/^Laboratory/).selectOption({ label: VENDOR });
  await dr.getByLabel(/^What is being made/).fill("Crown, zirconia");
  await dr.getByLabel(/^Tooth/).fill("3x");
  await dr.getByRole("button", { name: "Save the lab case" }).click();
  await expect(dr.getByText("Enter the tooth as its two-digit number, or leave it blank.")).toBeVisible();
  await dr.getByLabel(/^Tooth/).fill("36");
  await dr.getByLabel(/^Material/).fill("Zirconia");
  await dr.getByLabel(/^Shade/).fill("A2");
  await dr.getByRole("button", { name: "Save the lab case" }).click();
  await expect(dr.getByText("Saved: Crown, zirconia.")).toBeVisible();
  await dr.getByRole("link", { name: "Crown, zirconia" }).click();
  for (const [button, done] of [["Mark as sent to the lab", "Moved to Sent."], ["Mark as received back", "Moved to Received."]] as const) {
    await dr.getByRole("button", { name: button }).click();
    await dr.getByRole("button", { name: button }).last().click();
    await expect(dr.getByText(done)).toBeVisible();
  }
  await dr.getByRole("button", { name: "Send back for a remake" }).click();
  await dr.getByRole("button", { name: "Send back for a remake" }).last().click();
  await expect(dr.getByRole("alert")).toContainText("Write why the work is being sent back.");              // checked before anything is sent
  await dr.getByLabel(/^Reason/).fill("Margin open on the mesial");
  const labId = sql(`SELECT CONVERT(varchar(36), Id) FROM LabCases WHERE PatientId = '${ids.ann}'`);
  // someone else edits the case before this person saves: the change is explained and nothing is overwritten
  const other = sessions.FrontDesk.page;
  const current = (await api(other, "get", `/api/lab-cases/${labId}`)).body;
  expect((await api(other, "put", `/api/lab-cases/${labId}`, { partyId: current.partyId, title: current.summary, dueOn: null, toothKey: current.toothKey, treatmentPlanItemId: null, material: current.material, shade: current.shade, notes: "Rush job", rowVersion: current.rowVersion })).status).toBe(200);
  await dr.getByRole("button", { name: "Send back for a remake" }).last().click();
  await expect(dr.getByRole("alert")).toContainText("Someone else changed this case");
  expect(sql(`SELECT Status FROM LabCases WHERE Id = '${labId}'`)).toBe("Received");                           // nothing was stored by the stale attempt
  await shot(dr, "06-lab-stale.png");
  await dr.getByRole("button", { name: "Reload the case" }).click();
  await expect(dr.getByText("Reloaded the case.")).toBeVisible();
  await expect(dr.getByText("Rush job")).toBeVisible();
  // and with the current version the same move goes through, clearing the received date
  await dr.getByRole("button", { name: "Send back for a remake" }).click();
  await dr.getByLabel(/^Reason/).fill("Margin open on the mesial");
  await dr.getByRole("button", { name: "Send back for a remake" }).last().click();
  await expect(dr.getByText("Moved to Sent.")).toBeVisible();
  expect(sql(`SELECT CONCAT(Status, ',', CASE WHEN ReceivedOn IS NULL THEN 'noreceived' ELSE 'received' END) FROM LabCases WHERE Id = '${labId}'`)).toBe("Sent,noreceived");
});

test("BROKEN LINK: a document whose file goes missing stays in the case, flagged", async () => {
  const dr = sessions.Dentist.page;
  const second = (await api(sessions.Dentist.page, "post", `/api/patients/${ids.ann}/referrals`, { partyId: sql(`SELECT CONVERT(varchar(36), Id) FROM ExternalParties WHERE Kind = N'ReferralProvider'`), reason: "Second opinion", dueOn: null, encounterId: null, notes: null }, { "Idempotency-Key": `ec-second-${stamp}` })).body.case;
  expect((await api(dr, "put", `/api/referrals/${second.id}/documents/${ids.doc}`)).status).toBe(200);
  sql(`INSERT INTO DocumentIntegrity (DocumentId, Status, CheckedAtUtc) VALUES ('${ids.doc}', N'Missing', SYSDATETIMEOFFSET())`);
  await dr.goto(`/external-cases/referrals/${second.id}`);
  await expect(dr.getByText("Broken link: The document's file is missing from storage.")).toBeVisible();
  await shot(dr, "07-broken-link.png");
});

test("ROLES: a role that only reads sees no buttons, and billing sees nothing", async () => {
  const hyg = sessions.Hygienist.page;
  await hyg.goto("/external-cases");
  await expect(hyg.getByRole("heading", { name: "Referrals and lab cases" })).toBeVisible();
  await hyg.getByRole("button", { name: /^All \(\d+\)$/ }).click();
  await expect(hyg.getByRole("link", { name: "Crown, zirconia" })).toBeVisible();
  await scanBothThemes(hyg, "case-list");
  await hyg.goto(`/patients/${ids.ann}/external-cases`);
  await expect(hyg.getByRole("button", { name: "New referral" })).toHaveCount(0);
  await expect(hyg.getByText(/can read referrals and lab cases but cannot start or change them/)).toBeVisible();
  await scanBothThemes(hyg, "patient-cases");
  const bill = sessions.Billing.page;
  expect((await api(bill, "get", "/api/external-cases")).status).toBe(403);
  expect((await api(bill, "get", `/api/patients/${ids.ann}/external-cases/timeline`)).status).toBe(403);
  expect((await api(sessions.Hygienist.page, "post", `/api/patients/${ids.ann}/referrals`, { partyId: ids.ann, reason: "x" }, { "Idempotency-Key": `ec-h-${stamp}` })).status).toBe(403);
});

test("AUDIT: the audit trail names every action and holds no reason or note", async () => {
  const types = sql(`SELECT DISTINCT EventType FROM AuditLogEntries WHERE EventType LIKE N'ExternalParty%' OR EventType LIKE N'ReferralCase%' OR EventType LIKE N'LabCase%' OR EventType LIKE N'CaseDocument%' ORDER BY EventType`).split(/\r?\n/);
  evidence.auditEventTypes = types;
  expect(types).toEqual(expect.arrayContaining(["CaseDocumentLinked", "ExternalPartyCreated", "LabCaseCreated", "LabCaseStatusChanged", "ReferralCaseCreated", "ReferralCaseStatusChanged"]));
  expect(Number(sql(`SELECT COUNT(*) FROM AuditLogEntries WHERE Details LIKE N'%${REASON}%' OR Details LIKE N'%Margin open%' OR Details LIKE N'%Report reviewed%'`))).toBe(0);
});
