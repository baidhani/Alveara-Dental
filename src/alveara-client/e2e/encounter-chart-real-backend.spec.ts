import { test, expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { createAccount } from "./support/testAccounts";

// ALV-N022 A1, un-mocked: a real Chromium against the real Vite proxy -> real Alveara.Api -> real SQL Server (run with playwright.encounter-chart.config.ts against an API
// the caller started; see docs/testing/REAL_BACKEND_E2E.md). The encounter writes allergies to the patient's ONE chart; the one safety banner refreshes after each write
// without a reload; adding an allergy already on the chart never makes a second record - a resolved match is reactivated only by an explicit status the user picks
// (nothing preselected), and cancelling changes nothing (Addendum 02).

test.describe.configure({ mode: "serial" });

const OUT = process.env.ENCOUNTER_CHART_E2E_OUT ?? path.join(process.cwd(), "encounter-chart-e2e-out");
mkdirSync(OUT, { recursive: true });
const evidence: Record<string, unknown> = { axe: {} };
const shot = (page: Page, name: string) => page.screenshot({ path: path.join(OUT, name), fullPage: true });

const PASSWORD = "chart-pass-1!";
const contexts: BrowserContext[] = [];
let admin: Page;
let dentist: Page;
let annId = "";
let stamp = 0;

const csrf = async (page: Page) => (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
async function api(page: Page, method: "post" | "get", url: string, data?: unknown, headers: Record<string, string> = {}) {
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

async function session(browser: Browser, role: string): Promise<Page> {
  const token = await csrf(admin);
  const reg = await createAccount(admin.request, { username: `${role.toLowerCase()}-${stamp}`, password: PASSWORD, role: "Unassigned" });
  expect(reg.ok(), `register ${role}`).toBeTruthy();
  const { id } = await reg.json();
  expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": token }, data: { role } })).ok()).toBeTruthy();
  expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } })).ok()).toBeTruthy();
  const context = await browser.newContext();
  contexts.push(context);
  const page = await context.newPage();
  await signIn(page, `${role.toLowerCase()}-${stamp}`, PASSWORD);
  return page;
}

async function scanBothThemes(page: Page, label: string) {
  for (const theme of ["light", "dark"] as const) {
    await page.getByRole("button", { name: /Light|Dark/ }).waitFor();
    if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await page.waitForTimeout(300);
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
    const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    (evidence.axe as Record<string, unknown>)[`${label}-${theme}`] = { blocking: blocking.length, passes: results.passes.length };
    expect(blocking.map((v) => `${v.id}:${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`), `${label} (${theme})`).toEqual([]);
  }
}

const allergies = (page: Page) => page.getByRole("region", { name: "Allergies" });
const banner = (page: Page) => page.getByRole("region", { name: "Patient safety", exact: true });
const chartAllergies = async () => ((await api(dentist, "get", `/api/patients/${annId}/clinical-record`)).body.sections as any[]).find((s) => s.kind === "Allergy").items as any[];

async function addAllergy(page: Page, name: string) {
  await allergies(page).getByRole("button", { name: "Add allergy: Allergies" }).click();
  await allergies(page).getByLabel("Name").fill(name);
  await allergies(page).getByRole("button", { name: "Add allergy", exact: true }).click();
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  stamp = Date.now();
  const adminContext = await browser.newContext();
  contexts.push(adminContext);
  admin = await adminContext.newPage();
  expect((await createAccount(admin.request, { username: `admin-${stamp}`, password: "admin-password-1!", role: "Admin" })).ok()).toBeTruthy();
  await signIn(admin, `admin-${stamp}`, "admin-password-1!");
  const desk = await session(browser, "FrontDesk");
  dentist = await session(browser, "Dentist");
  const r = await api(desk, "post", "/api/patients", { firstName: "Ann", lastName: `Chart${stamp}`, dateOfBirth: "1985-03-09", sex: "Female", phone: "555-030-0300", addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701" }, { "Idempotency-Key": `reg-chart-${stamp}` });
  expect(r.status).toBe(201);
  annId = r.body.id;
});

test.afterAll(async () => {
  writeFileSync(path.join(OUT, "encounter-chart-e2e.json"), JSON.stringify(evidence, null, 2));
  for (const c of contexts) await c.close();
});

test("ONE RECORD: an encounter writes the allergy to the chart, the one banner refreshes without a reload, and a resolved match is reactivated only by the user's explicit choice", async () => {
  const page = dentist;
  await page.goto(`/patients/${annId}/clinical`);
  await page.getByRole("button", { name: "Start an encounter" }).click();
  await expect(page.getByRole("heading", { name: /^Encounter on/ })).toBeVisible();
  await expect(page.getByRole("region", { name: "Patient safety for this encounter" })).toHaveCount(0); // one banner (OD-05)
  await expect(banner(page)).toContainText("No alerts, active allergies or current medications are recorded.");

  // 1. added in the encounter -> on the chart; the banner refreshes in place
  await addAllergy(page, "  Penicillin  ");
  await expect(page.getByText("Allergies: saved to the chart.")).toBeVisible();
  await expect(banner(page)).toContainText("1 active allergy");
  expect((await chartAllergies()).map((i) => [i.name, i.status])).toEqual([["Penicillin", "Active"]]);

  // 2. resolved from the encounter (the user picks the status)
  await allergies(page).getByRole("button", { name: "Change status of Penicillin" }).click();
  await allergies(page).getByLabel(/New status for Penicillin/).selectOption("Resolved");
  await allergies(page).getByRole("button", { name: "Save status" }).click();
  await expect(banner(page)).not.toContainText("active allergy");

  // 3. added again (different case and spacing): pointed to the resolved record; cancel changes nothing
  await addAllergy(page, "penicillin ");
  const notice = allergies(page).getByRole("alert");
  await expect(notice).toContainText("Penicillin is already on the chart as resolved. Nothing was added.");
  await shot(page, "01-resolved-match.png");
  await scanBothThemes(page, "resolved-match-notice");
  await notice.getByRole("button", { name: "Cancel" }).click();
  await expect(allergies(page).getByRole("alert")).toHaveCount(0);
  await expect(allergies(page).getByLabel("Name")).toHaveValue("penicillin "); // a refused save keeps what was typed (the add form stays open)
  expect((await chartAllergies()).map((i) => [i.name, i.status])).toEqual([["Penicillin", "Resolved"]]);

  // 4. sent again from the still-open form, then reactivated: nothing preselected, nothing saved until the user chooses
  await allergies(page).getByRole("button", { name: "Add allergy", exact: true }).click();
  await allergies(page).getByRole("alert").getByRole("button", { name: "Reactivate or update the existing item" }).click();
  const choice = allergies(page).getByLabel(/New status for Penicillin/);
  await expect(choice).toHaveValue("");
  await expect(allergies(page).getByRole("button", { name: "Save status" })).toBeDisabled();
  await choice.selectOption("Active");
  await allergies(page).getByRole("button", { name: "Save status" }).click();
  await expect(banner(page)).toContainText("1 active allergy");
  await shot(page, "02-reactivated.png");

  const items = await chartAllergies();
  expect(items.map((i) => [i.name, i.status])).toEqual([["Penicillin", "Active"]]); // still ONE record
  const encounterUrl = page.url().replace(/^.*\/clinical\//, "/api/encounters/");

  // 5. the Safety tab shows the same chart, reached in-app from the banner (no reload)
  // the refused add left its form open with the typed text, so leaving first asks to discard it; dismissing keeps the user (and the text) on the encounter
  const prompts: string[] = [];
  page.once("dialog", (d) => { prompts.push(d.type()); void d.dismiss(); });
  await banner(page).getByRole("link", { name: "Open safety details" }).click();
  await expect.poll(() => prompts).toEqual(["confirm"]);
  await expect(page).toHaveURL(/\/clinical\//);
  page.once("dialog", (d) => void d.accept());                                      // if closing the form asks to discard the text, the user agrees
  await allergies(page).getByRole("button", { name: "Cancel" }).click();          // the user closes the form
  await expect(allergies(page).getByLabel("Name")).toHaveCount(0);
  const loads: string[] = [];
  page.on("load", () => loads.push(page.url()));
  await banner(page).getByRole("link", { name: "Open safety details" }).click();
  await expect(page).toHaveURL(new RegExp(`/patients/${annId}/safety`));
  await expect(page.getByRole("main").getByText("Penicillin").first()).toBeVisible();
  expect(loads).toEqual([]); // client-side navigation: no page load happened
  await shot(page, "03-safety-tab.png");

  const enc = (await api(page, "get", encounterUrl)).body;
  const here = enc.sections.find((s: any) => s.kind === "Allergy").recordedHere.map((p: any) => p.changeType);
  expect(here).toEqual(["Added", "StatusChanged", "StatusChanged"]);
  evidence.oneRecord = { records: items.length, provenance: here, cancelChangedNothing: true, nothingPreselected: true, bannerRefreshedWithoutReload: true, safetyTabShowsItWithoutReload: true, leavingWithTypedTextAsksFirst: true };
});
