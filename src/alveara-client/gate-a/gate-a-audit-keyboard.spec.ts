import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";
import { writeFileSync, mkdirSync } from "node:fs";
import path from "node:path";

/**
 * ALV-002-C01 R04 evidence: the audit viewer's scroll region against the REAL API with representative, overflowing audit
 * data. (1) axe in both themes (rule scrollable-region-focusable must not fire; zero critical/serious), (2) the region is
 * reachable by Tab, (3) the focus indicator is visible, (4) arrow keys actually scroll it horizontally, (5) permission
 * gating is unchanged (a role without ViewAuditLog is still denied).
 */
const OUT = process.env.GATE_A_OUT ?? path.join(process.cwd(), "gate-a-out");
mkdirSync(OUT, { recursive: true });

async function settle(page: Page) {
  await page.evaluate(async () => {
    const finite = document.getAnimations().filter((a) => Number.isFinite(a.effect?.getComputedTiming().endTime as number));
    await Promise.race([Promise.all(finite.map((a) => a.finished.catch(() => undefined))), new Promise((r) => setTimeout(r, 3000))]);
  });
  await page.waitForTimeout(400);
}

test("audit viewer scroll region: keyboard reachable, visibly focused, scrollable by keyboard, axe-clean in both themes", async ({ page, browser }) => {
  test.setTimeout(180_000);
  const password = "audit-keyboard-password-1!";
  const username = `audit-root-${Date.now()}`;
  expect((await page.request.post("/api/auth/bootstrap-admin", { data: { username, password, secret: process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret" } })).ok()).toBeTruthy();
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  const { token } = await (await page.request.get("/api/auth/csrf-token")).json();

  // representative audit volume: registrations, role changes and enable events produce real audit rows
  for (let i = 0; i < 8; i++) {
    const u = await (await page.request.post("/api/auth/register", { data: { username: `audit-user-${Date.now()}-${i}`, password } })).json();
    await page.request.put(`/api/auth/${u.id}/role`, { headers: { "X-CSRF-Token": token }, data: { role: "Dentist" } });
    await page.request.put(`/api/auth/${u.id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } });
  }

  await page.setViewportSize({ width: 560, height: 800 }); // narrower than the table, so it genuinely overflows horizontally
  const report: Record<string, unknown> = {};
  for (const theme of ["light", "dark"] as const) {
    await page.goto("/admin/audit-log");
    await expect(page.getByRole("region", { name: "Audit log entries (scrollable table)" })).toBeVisible();
    if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await settle(page);
    const region = page.getByRole("region", { name: "Audit log entries (scrollable table)" });

    const overflow = await region.evaluate((el) => ({ scrollWidth: el.scrollWidth, clientWidth: el.clientWidth }));
    expect(overflow.scrollWidth, "the table must overflow so keyboard scrolling is meaningful").toBeGreaterThan(overflow.clientWidth);

    // (2) reachable by keyboard
    await page.locator("main").first().focus().catch(() => undefined);
    let reached = false;
    for (let i = 0; i < 40 && !reached; i++) {
      await page.keyboard.press("Tab");
      reached = await region.evaluate((el) => el === document.activeElement);
    }
    expect(reached, "Tab reaches the scroll region").toBe(true);

    // (3) visible focus indicator
    const outline = await region.evaluate((el) => { const s = getComputedStyle(el); return { style: s.outlineStyle, width: s.outlineWidth }; });
    expect(outline.style).not.toBe("none");
    expect(parseFloat(outline.width)).toBeGreaterThanOrEqual(2);

    // (4) arrow keys scroll it
    const before = await region.evaluate((el) => el.scrollLeft);
    for (let i = 0; i < 6; i++) await page.keyboard.press("ArrowRight");
    await page.waitForTimeout(300);
    const after = await region.evaluate((el) => el.scrollLeft);
    expect(after, "ArrowRight scrolls the region").toBeGreaterThan(before);

    // (1) axe, focused and unfocused
    const axe = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
    const blocking = axe.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(axe.violations.some((v) => v.id === "scrollable-region-focusable")).toBe(false);
    expect(blocking, JSON.stringify(blocking)).toEqual([]);
    report[theme] = { overflow, reachedByTab: reached, outline, scrollLeftBefore: before, scrollLeftAfter: after, axeViolations: axe.violations.map((v) => ({ id: v.id, impact: v.impact })), blocking: blocking.length };
  }

  // (5) permission gating unchanged: a Dentist is still denied the page and the API
  const dentistName = `audit-dentist-${Date.now()}`;
  const d = await (await page.request.post("/api/auth/register", { data: { username: dentistName, password } })).json();
  await page.request.put(`/api/auth/${d.id}/role`, { headers: { "X-CSRF-Token": token }, data: { role: "Dentist" } });
  await page.request.put(`/api/auth/${d.id}/enabled`, { headers: { "X-CSRF-Token": token }, data: { enabled: true } });
  const ctx = await browser.newContext({ viewport: { width: 560, height: 800 } });
  const p2 = await ctx.newPage();
  await p2.goto("/login");
  await p2.getByLabel("Username").fill(dentistName);
  await p2.getByLabel("Password").fill(password);
  await p2.getByRole("button", { name: "Sign in" }).click();
  await expect(p2.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  await p2.goto("/admin/audit-log");
  await expect(p2.getByText(/don't have permission/i)).toBeVisible();
  await expect(p2.getByRole("region", { name: "Audit log entries (scrollable table)" })).toHaveCount(0);
  expect((await p2.request.get("/api/auth/audit-log?take=1")).status()).toBe(403);
  report.permissionGating = { dentistPageDenied: true, dentistApiStatus: 403 };
  await ctx.close();

  writeFileSync(path.join(OUT, "audit-keyboard-results.json"), JSON.stringify(report, null, 2));
});
