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

const ALL_ROUTES = ["/", "/showcase", "/system-status", "/admin/users", "/admin/permissions", "/admin/audit-log", "/admin/configuration", "/admin/backup", "/settings/mfa", "/this-route-does-not-exist"];
const ROUTES = process.env.GATE_A_ROUTES ? process.env.GATE_A_ROUTES.split(",") : ALL_ROUTES;
const GUARDED = ["/admin/users", "/admin/permissions", "/admin/audit-log", "/admin/configuration", "/admin/backup"];
const ROLES = ["Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager", "Admin"];

/** Wait for every finite animation/transition to finish and the page to stop changing before measuring. */
async function settle(page: Page) {
  await page.evaluate(async () => {
    const finite = document.getAnimations().filter((a) => Number.isFinite(a.effect?.getComputedTiming().endTime as number)); // infinite loaders never finish
    await Promise.race([Promise.all(finite.map((a) => a.finished.catch(() => undefined))), new Promise((r) => setTimeout(r, 3000))]);
  });
  await page.waitForTimeout(400);
}

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
  test.setTimeout(300_000);
  const results: unknown[] = [];
  let blockingTotal = 0;
  for (const route of ROUTES) {
    for (const theme of ["light", "dark"] as const) {
      await admin.goto(route);
      await admin.waitForLoadState("networkidle");
      const current = await admin.locator("html").getAttribute("data-theme");
      if (current !== theme) await admin.getByRole("button", { name: /Light|Dark/ }).click();
      await expect(admin.locator("html")).toHaveAttribute("data-theme", theme);
      await settle(admin); // finite CSS transitions (e.g. button background 0.12s) must be finished or axe samples blended colours
      const axe = await new AxeBuilder({ page: admin }).withTags(["wcag2a", "wcag2aa"]).analyze();
      const blocking = axe.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      blockingTotal += blocking.length;
      // interaction states: hovered and keyboard-focused controls on the page (default state was scanned above)
      const states: Record<string, number> = {};
      for (const state of ["hover", "focus"] as const) {
        const target = admin.locator("main a, main button, main [tabindex]").first();
        if (await target.count()) {
          if (state === "hover") await target.hover(); else { await admin.keyboard.press("Tab"); await target.focus(); }
          await settle(admin);
          const r2 = await new AxeBuilder({ page: admin }).withTags(["wcag2a", "wcag2aa"]).analyze();
          states[state] = r2.violations.filter((v) => v.impact === "critical" || v.impact === "serious").length;
          blockingTotal += states[state];
        }
      }
      results.push({ route, theme, stateScans: states, violations: axe.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, targets: v.nodes.slice(0, 4).map((n) => ({ target: n.target, summary: (n.failureSummary ?? "").split(String.fromCharCode(10)).slice(0, 3).join(" ") })) })), blocking: blocking.length, passes: axe.passes.length });
    }
  }
  writeFileSync(path.join(OUT, "a2-axe-results.json"), JSON.stringify({ tags: ["wcag2a", "wcag2aa"], routes: ROUTES, results, blockingTotal }, null, 2));
  // The verdict is asserted by the last test, so a failing A2 never prevents A6 from running and being recorded.
});

test("A6: for each of the 7 roles, rendered navigation matches server-authoritative access (no client-only enforcement)", async () => {
  test.setTimeout(300_000);
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
    // expected status per endpoint is derived from the role's OWN permission list (server-authoritative), so a regression fails automatically
    const REQUIRES: Record<string, string> = {
      "/api/auth/users": "ManageUsers", "/api/auth/audit-log?take=1": "ViewAuditLog", "/api/auth/permission-matrix": "ViewPermissionMatrix",
      "/api/backup/status": "ViewBackupStatus", "/api/config/practice": "ManagePracticeConfiguration",
    };
    for (const [api, permission] of Object.entries(REQUIRES)) {
      serverChecks[api] = (await page.request.get(api)).status();
      expect(serverChecks[api], `${role} GET ${api} (requires ${permission})`).toBe(permissions.permissions.includes(permission) ? 200 : 403);
    }
    // each guarded page's link/route is allowed exactly when the role holds that page's permission
    const PAGE_REQUIRES: Record<string, string> = { "/admin/users": "ManageUsers", "/admin/permissions": "ViewPermissionMatrix", "/admin/audit-log": "ViewAuditLog", "/admin/configuration": "ManagePracticeConfiguration", "/admin/backup": "ViewBackupStatus" };
    for (const route of GUARDED) {
      expect(rows[route].denied, `${role} ${route} requires ${PAGE_REQUIRES[route]}`).toBe(!permissions.permissions.includes(PAGE_REQUIRES[route]));
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
