import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";

// ALV-N021 A3 (briefing section 9 "real browser, two zones"): one Chicago practice, two browsers in different zones (America/Chicago and Asia/Tokyo, which
// is 15 hours ahead, so its calendar day differs from Chicago's for most of the day). Real Chromium -> real Vite proxy -> real Alveara.Api -> real SQL
// Server (run with playwright.practice-time.config.ts, see docs/testing/REAL_BACKEND_E2E.md). In BOTH browsers:
// - the session carries the practice zone (America/Chicago);
// - a follow-up due time typed as 2030-01-14 09:30 is stored as the same instant (15:30 UTC) and shown back as 9:30 AM on 1/14/2030;
// - a report's "Last day" defaults to the practice's today (computed here in America/Chicago), whatever the browser's own day is.

test.describe.configure({ mode: "serial" });

const OUT = process.env.PRACTICE_TIME_E2E_OUT ?? path.join(process.cwd(), "practice-time-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = {};
const PASSWORD = "practice-time-pass-1!";
const PRACTICE_ZONE = "America/Chicago";
const BROWSER_ZONES = ["America/Chicago", "Asia/Tokyo"] as const;
let stamp = 0;
let adminContext: BrowserContext;
let admin: Page;
let patientId = "";

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
const plain = (s: string | null | undefined) => (s ?? "").replace(/ /g, " ");

/** The calendar day in a zone, computed in this test process (Latin digits, Gregorian), to compare with what the page shows. */
function dayIn(zone: string, now = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-US-u-nu-latn-ca-gregory", { timeZone: zone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(now);
  const get = (t: string) => parts.find((p) => p.type === t)!.value;
  return `${get("year")}-${get("month")}-${get("day")}`;
}

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

async function createUser(role: string, username: string) {
  const token = await csrf(admin);
  const reg = await admin.request.post("/api/auth/register", { data: { username, password: PASSWORD } });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  adminContext = await browser.newContext({ timezoneId: PRACTICE_ZONE });
  admin = await adminContext.newPage();
  const secret = process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret";
  expect((await admin.request.post("/api/auth/bootstrap-admin", { data: { username: `admin-${stamp}`, password: "admin-password-1!", secret } })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  const patient = await admin.request.post("/api/patients", {
    headers: { "X-CSRF-Token": await csrf(admin), "Idempotency-Key": `pt-${stamp}` },
    data: { firstName: "Ann", lastName: `Zone${stamp}`, dateOfBirth: "1985-03-14", sex: "Female", phone: "5125550100", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" },
  });
  expect(patient.ok(), await patient.text()).toBeTruthy();
  patientId = (await patient.json()).id;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "practice-time-evidence.json"), JSON.stringify(evidence, null, 2));
  await adminContext?.close();
});

for (const browserZone of BROWSER_ZONES) {
  test(`A browser in ${browserZone} shows and enters times in the practice zone (${PRACTICE_ZONE})`, async ({ browser }) => {
    const username = `desk-${browserZone.replace("/", "-").toLowerCase()}-${stamp}`;
    await createUser("FrontDesk", username);
    const context = await browser.newContext({ timezoneId: browserZone });
    const page = await context.newPage();
    try {
      await signIn(page, username, PASSWORD);
      const browserDay = await page.evaluate(() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; });

      // the session carries the practice zone
      const session = await (await page.request.get("/api/auth/permissions")).json();
      expect(session.practiceTimeZoneId).toBe(PRACTICE_ZONE);

      // a typed due time is practice-local text: stored as the same instant and shown back as typed, in either browser zone
      const title = `Zone check ${browserZone}`;
      await page.goto(`/patients/${patientId}/follow-ups`);
      await page.getByRole("button", { name: "New follow-up" }).click();
      await page.getByLabel(/^Title/).fill(title);
      await page.getByLabel(/^Due/).fill("2030-01-14T09:30");
      await page.getByRole("button", { name: "Create follow-up" }).click();
      const item = page.getByRole("list", { name: /follow-ups$/ }).getByRole("listitem").filter({ hasText: title });   // the open follow-ups list (as follow-ups-real-backend.spec.ts)
      await expect(item).toBeVisible();
      const shown = plain(await item.textContent());
      expect(shown).toContain("1/14/2030, 9:30:00 AM");
      const list = await (await page.request.get(`/api/patients/${patientId}/follow-ups?status=all`)).json();
      const task = (list.items as { title: string; dueAtUtc: string }[]).find((t) => t.title === title)!;
      expect(new Date(task.dueAtUtc).toISOString()).toBe("2030-01-14T15:30:00.000Z");
      await page.screenshot({ path: path.join(OUT, `follow-up-${browserZone.replace("/", "-")}.png`), fullPage: true });

      // "today" is the practice's day, not the browser's
      const practiceDay = dayIn(PRACTICE_ZONE);
      await page.goto("/reports/schedule");
      await expect(page.getByLabel("Last day")).toHaveValue(practiceDay);
      await page.screenshot({ path: path.join(OUT, `report-today-${browserZone.replace("/", "-")}.png`), fullPage: true });

      evidence[browserZone] = { session: session.practiceTimeZoneId, browserDay, practiceDay, sameDay: browserDay === practiceDay, followUpShown: "1/14/2030, 9:30:00 AM", storedDueAtUtc: task.dueAtUtc };
    } finally {
      await context.close();
    }
  });
}
