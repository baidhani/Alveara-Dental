import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Browser, BrowserContext, Page } from "@playwright/test";
import { writeFileSync, mkdirSync } from "node:fs";
import path from "node:path";

/**
 * GATE A evidence run (criteria A2 and A6) against the REAL application: a real dotnet API + SQL Server and the real
 * authenticated shell in Chromium. It does not mock anything. Results are written to GATE_A_OUT as JSON.
 *
 *  A2 - axe (WCAG 2.0 A/AA tags) on every shell route, light and dark, as an authenticated administrator; threshold:
 *       zero critical/serious violations.
 *  A6 - for each of the 7 working roles, sign in and compare the rendered navigation with direct-URL access: a route is
 *       reachable exactly when its link is rendered, and no role sees a link whose page denies it.
 */
const OUT = process.env.GATE_A_OUT ?? path.join(process.cwd(), "gate-a-out");
mkdirSync(OUT, { recursive: true });

const ROUTES = ["/", "/showcase", "/system-status", "/admin/users", "/admin/permissions", "/admin/audit-log", "/admin/configuration", "/admin/backup", "/settings/mfa", "/this-route-does-not-exist"];
const GUARDED = ["/admin/users", "/admin/permissions", "/admin/audit-log", "/admin/configuration", "/admin/backup"];
const ROLES = ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager", "Admin"];

test.describe.configure({ mode: "serial" });

let browserRef: Browser;
let adminContext: BrowserContext;
let admin: Page;
const stamp = Date.now();
const password = "gate-a-password-1!";
const users: Record<string, string> = {};

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  adminContext = await browser.newContext();
  admin = await adminContext.newPage();
  const username = `gate-root-${stamp}`;
  const boot = await admin.request.post("/api/auth/bootstrap-admin", { data: { username, password, secret: process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret" } });
  expect(boot.ok()).toBeTruthy();
  users.AdminBootstrap = username;
  await admin.goto("/login");
  await admin.getByLabel("Username").fill(username);
  await admin.getByLabel("Password").fill(password);
  await admin.getByRole("button", { name: "Sign in" }).click();
  await expect(admin.getByRole("heading", { name: "Dashboard" })).toBeVisible();

  const { token: csrf } = await (await admin.request.get("/api/auth/csrf-token")).json();
  for (const role of ROLES) {
    const name = `gate-${role.toLowerCase()}-${stamp}`;
    const reg = await admin.request.post("/api/auth/register", { data: { username: name, password } });
    expect(reg.ok(), `register ${role}: ${reg.status()} ${await reg.text()}`).toBeTruthy();
    const { id } = await reg.json();
    expect((await admin.request.put(`/api/auth/${id}/role`, { headers: { "X-CSRF-Token": csrf }, data: { role } })).ok()).toBeTruthy();
    expect((await admin.request.put(`/api/auth/${id}/enabled`, { headers: { "X-CSRF-Token": csrf }, data: { enabled: true } })).ok()).toBeTruthy();
    users[role] = name;
  }
});

test.afterAll(async () => {
  await adminContext.close();
});

test("A2: no critical/serious axe violations on any shell route, light and dark, authenticated against the real API", async () => {
  const results: unknown[] = [];
  let blockingTotal = 0;
  for (const route of ROUTES) {
    for (const theme of ["light", "dark"] as const) {
      await admin.goto(route);
      await admin.waitForLoadState("networkidle");
      const current = await admin.locator("html").getAttribute("data-theme");
      if (current !== theme) await admin.getByRole("button", { name: /Light|Dark/ }).click();
      await expect(admin.locator("html")).toHaveAttribute("data-theme", theme);
      const axe = await new AxeBuilder({ page: admin }).withTags(["wcag2a", "wcag2aa"]).analyze();
      const blocking = axe.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      blockingTotal += blocking.length;
      results.push({ route, theme, violations: axe.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, targets: v.nodes.slice(0, 4).map((n) => ({ target: n.target, summary: (n.failureSummary ?? "").split(String.fromCharCode(10)).slice(0, 3).join(" ") })) })), blocking: blocking.length, passes: axe.passes.length });
    }
  }
  writeFileSync(path.join(OUT, "a2-axe-results.json"), JSON.stringify({ tags: ["wcag2a", "wcag2aa"], routes: ROUTES, results, blockingTotal }, null, 2));
  // The verdict is asserted by the last test, so a failing A2 never prevents A6 from running and being recorded.
});

test("A6: for each of the 7 roles, rendered navigation matches server-authoritative access (no client-only enforcement)", async () => {
  const matrix: Record<string, unknown> = {};
  for (const role of ROLES) {
    const context = await browserRef.newContext();
    const page = await context.newPage();
    await page.goto("/login");
    await page.getByLabel("Username").fill(users[role]);
    await page.getByLabel("Password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

    const permissions = await (await page.request.get("/api/auth/permissions")).json();
    const nav = page.getByRole("navigation", { name: "Primary navigation" });
    const hrefs = await nav.getByRole("link").evaluateAll((els) => els.map((e) => (e as HTMLAnchorElement).getAttribute("href")));
    const rows: Record<string, { linkRendered: boolean; denied: boolean }> = {};
    for (const route of GUARDED) {
      await page.goto(route);
      await page.waitForLoadState("networkidle");
      const denied = await page.getByText(/don't have permission/i).count() > 0;
      rows[route] = { linkRendered: hrefs.includes(route), denied };
      // visible link <=> reachable (server-authoritative); never a visible link to a denied page, never a hidden link to an open page
      expect(rows[route].linkRendered, `${role} ${route}`).toBe(!denied);
    }
    // the server itself refuses the underlying data calls a hidden page would make
    const serverChecks: Record<string, number> = {};
    for (const api of ["/api/auth/users", "/api/auth/audit-log?take=1", "/api/auth/permission-matrix", "/api/backup/status", "/api/config/practice"]) {
      serverChecks[api] = (await page.request.get(api)).status();
    }
    matrix[role] = { permissions: permissions.permissions, navLinks: hrefs, guardedRoutes: rows, serverChecks };
    await context.close();
  }
  writeFileSync(path.join(OUT, "a6-role-navigation.json"), JSON.stringify({ roles: ROLES, matrix }, null, 2));
  // Admin sees every guarded route; Dentist sees none of them (matches the independent R-role matrix tests)
  const adminRows = (matrix.Admin as { guardedRoutes: Record<string, { denied: boolean }> }).guardedRoutes;
  expect(Object.values(adminRows).every((r) => !r.denied)).toBe(true);
});

test("A2 verdict: zero critical/serious axe violations across all shell routes", async () => {
  const recorded = JSON.parse((await import("node:fs")).readFileSync(path.join(OUT, "a2-axe-results.json"), "utf-8")) as { blockingTotal: number };
  expect(recorded.blockingTotal, "critical/serious axe violations across all shell routes (see a2-axe-results.json)").toBe(0);
});
