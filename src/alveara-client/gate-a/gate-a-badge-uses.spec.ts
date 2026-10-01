import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";
import { writeFileSync, mkdirSync } from "node:fs";
import path from "node:path";

/**
 * ALV-001-C01 R07 downstream revalidation: every page that renders the shared `.alv-status-badge` class with REAL data
 * (security users list and detail, configuration entity rows, backup history/drills/notifications), scanned with axe in
 * both themes after transitions settle, plus hover/keyboard-focus states. Seeds data through the real API first so the
 * badges actually exist on the page. Threshold: zero critical/serious violations.
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

test("badge uses: users, user detail, configuration rows and backup history have no critical/serious axe violations", async ({ page }) => {
  test.setTimeout(240_000);
  const password = "badge-uses-password-1!";
  const username = `badge-root-${Date.now()}`;
  expect((await page.request.post("/api/auth/bootstrap-admin", { data: { username, password, secret: process.env.E2E_BOOTSTRAP_SECRET ?? "e2e-real-backend-secret" } })).ok()).toBeTruthy();
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  const { token } = await (await page.request.get("/api/auth/csrf-token")).json();
  const H = { "X-CSRF-Token": token };

  // a disabled user as well as enabled ones, so both badge variants are on the list
  const other = await (await page.request.post("/api/auth/register", { data: { username: `badge-other-${Date.now()}`, password } })).json();
  expect((await page.request.put(`/api/auth/${other.id}/role`, { headers: H, data: { role: "Dentist" } })).ok()).toBeTruthy();
  // configuration rows (shared ConfigEntityPanel): an active and an inactive operatory and appointment type
  expect((await page.request.post("/api/config/locations", { headers: H, data: { name: "Main Office" } })).ok()).toBeTruthy();
  for (const [kind, body] of [["operatories", (n: string) => ({ name: n })], ["appointment-types", (n: string) => ({ name: n, defaultDurationMinutes: 30 })]] as const) {
    const a = await (await page.request.post(`/api/config/${kind}`, { headers: H, data: body("Active one") })).json();
    const b = await (await page.request.post(`/api/config/${kind}`, { headers: H, data: body("Retired one") })).json();
    expect(a.id).toBeTruthy();
    const off = await page.request.put(`/api/config/${kind}/${b.id}/active`, { headers: H, data: { isActive: false, rowVersion: b.rowVersion } });
    expect(off.ok(), `deactivate ${kind}: ${off.status()} ${await off.text()}`).toBeTruthy();
  }
  // backup history: one successful backup (badges for result and verification state)
  expect((await page.request.post("/api/backup/recovery-key", { headers: H, data: { currentPassword: password, replaceExisting: false }, timeout: 120_000 })).ok()).toBeTruthy();
  expect((await page.request.post("/api/backup/backups", { headers: H, timeout: 180_000 })).ok()).toBeTruthy();

  const results: unknown[] = [];
  let blockingTotal = 0;
  const targets: { name: string; open: () => Promise<void>; badge: string }[] = [
    { name: "/admin/users", open: async () => { await page.goto("/admin/users"); await expect(page.locator(".alv-status-badge").first()).toBeVisible(); }, badge: ".alv-status-badge" },
    { name: "/admin/users/:id", open: async () => { await page.goto("/admin/users"); await page.getByRole("link", { name: "View" }).first().click(); await expect(page.locator(".alv-status-badge").first()).toBeVisible(); }, badge: ".alv-status-badge" },
    { name: "/admin/configuration (operatories)", open: async () => { await page.goto("/admin/configuration"); await page.getByRole("tab", { name: "Operatories" }).click(); await expect(page.locator(".alv-status-badge").first()).toBeVisible(); }, badge: ".alv-status-badge" },
    { name: "/admin/configuration (appointment types)", open: async () => { await page.goto("/admin/configuration"); await page.getByRole("tab", { name: "Appointment types" }).click(); await expect(page.locator(".alv-status-badge").first()).toBeVisible(); }, badge: ".alv-status-badge" },
    { name: "/admin/backup", open: async () => { await page.goto("/admin/backup"); await expect(page.locator(".alv-status-badge").first()).toBeVisible(); }, badge: ".alv-status-badge" },
  ];
  for (const t of targets) {
    for (const theme of ["light", "dark"] as const) {
      await t.open();
      if ((await page.locator("html").getAttribute("data-theme")) !== theme) await page.getByRole("button", { name: /Light|Dark/ }).click();
      await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
      await settle(page);
      const badges = await page.locator(t.badge).count();
      const states: Record<string, number> = {};
      for (const state of ["default", "hover", "focus"] as const) {
        if (state === "hover") await page.locator("main a, main button").first().hover();
        if (state === "focus") { await page.keyboard.press("Tab"); }
        await settle(page);
        const axe = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
        const blocking = axe.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
        states[state] = blocking.length;
        blockingTotal += blocking.length;
        if (blocking.length) results.push({ page: t.name, theme, state, violations: blocking.map((v) => ({ id: v.id, nodes: v.nodes.map((n) => n.target) })) });
      }
      results.push({ page: t.name, theme, badgesOnPage: badges, blockingByState: states });
      expect(badges, `${t.name} must actually show badges so the scan is meaningful`).toBeGreaterThan(0);
    }
  }
  writeFileSync(path.join(OUT, "badge-uses-axe-results.json"), JSON.stringify({ blockingTotal, results }, null, 2));
  expect(blockingTotal, JSON.stringify(results.filter((r) => (r as { violations?: unknown }).violations))).toBe(0);
});
