import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Page, Route } from "@playwright/test";

// ALV-N004 R06: the backup page's warning text (a restore-wizard check that does not pass but does not block) and its status alerts must pass
// axe's colour-contrast rule in both themes. Before R06 the warning check line was 3.5:1 on the page surface (light theme). The tests assert the
// elements really rendered, so an empty scan cannot pass. API responses are mocked with the same shapes the page's own unit tests use.

const settings = { scheduleEnabled: true, scheduleIntervalHours: 24, retentionCount: 7, destinationDirectory: "D:\Backups", destinationIsDefault: false, recoveryKeyConfigured: true, recoveryKeyFingerprint: "abcdef0123456789abcdef", recoveryKeyConfiguredAtUtc: "2026-09-01T10:00:00Z", requiredSuccessfulVerifications: 2, verificationCadenceDays: 30, rowVersion: "AAAA" };
const record = { id: "b1", kind: "Manual", status: "Succeeded", startedAtUtc: "2026-09-30T08:00:00Z", completedAtUtc: "2026-09-30T08:01:00Z", fileName: "alveara-backup-1.abk", sizeBytes: 5242880, sha256: "ff", includedAssetClasses: ["database", "documents", "dataProtectionKeys"], schemaMigration: "2026", appVersion: "1", failureCode: null, failureMessage: null, verificationStatus: "HashVerified", verifiedAtUtc: "2026-09-30T08:01:00Z", verificationFailureCode: null, restoreProven: false, archiveDefectCode: null };
// untrusted + overdue: the page shows its warning alerts
const status = { settings, lastSuccess: record, lastFailure: null, latestAttemptFailed: false, successfulVerificationCount: 0, trusted: false, lastFullVerificationAtUtc: null, verificationOverdue: true, requiredAssetClasses: ["database", "documents", "dataProtectionKeys"], lastSuccessCoversAllAssetClasses: true, missingAssetClasses: [] };

const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

async function mockBackupPage(page: Page, theme: "light" | "dark") {
  await page.addInitScript((t) => localStorage.setItem("alveara-theme", t), theme);
  await page.route("**/api/auth/permissions", (route) => json(route, { username: "e2e-admin", role: "Admin", permissions: ["ViewBackupStatus", "ManageBackups"], sessionExpiresAtUtc: new Date(Date.now() + 8 * 3600_000).toISOString() }));
  await page.route("**/api/auth/csrf-token", (route) => json(route, { token: "e2e-csrf" }));
  await page.route("**/api/health", (route) => json(route, { status: "ok" }));
  await page.route(/\/api\/backup\/status(\?.*)?$/, (route) => json(route, status));
  await page.route(/\/api\/backup\/history(\?.*)?$/, (route) => json(route, [record]));
  await page.route(/\/api\/backup\/restore-drills(\?.*)?$/, (route) => json(route, []));
  await page.route(/\/api\/backup\/archives(\?.*)?$/, (route) => json(route, []));
  await page.route(/\/api\/backup\/notifications(\?.*)?$/, (route) => json(route, []));
  await page.route(/\/api\/backup\/(archives|backups\/[^/]+)\/preflight/, (route) =>
    json(route, { canRestore: true, checks: [
      { name: "archive_present", passed: true, detail: "The backup file is present.", blocking: true },
      { name: "disk_space", passed: false, detail: "Free space is below the recommended margin.", blocking: false },
      { name: "recovery_material_matches", passed: false, detail: "This recovery key does not belong to this backup.", blocking: true },
    ] })
  );
}

async function scan(page: Page, selector: string) {
  const results = await new AxeBuilder({ page }).include(selector).withRules(["color-contrast"]).analyze();
  expect(results.violations, JSON.stringify(results.violations.map((v) => v.nodes.map((n) => n.any.map((a) => a.message))), null, 2)).toEqual([]);
}

for (const theme of ["light", "dark"] as const) {
  test(`the backup warning alerts and the warning/blocking check lines pass the colour-contrast rule in ${theme} mode`, async ({ page }) => {
    await mockBackupPage(page, theme);
    await page.goto("/admin/backup");
    await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
    await expect(page.locator(".alv-backup-alert--warning").first()).toBeVisible();
    await scan(page, ".alv-backup-status");

    await page.getByRole("button", { name: /Verify or restore/ }).click();
    const dialog = page.getByRole("dialog", { name: "Restore wizard" });
    await dialog.getByLabel("Or paste the key file contents").fill("-----BEGIN PGP PRIVATE KEY BLOCK-----");
    await dialog.getByLabel("Recovery passphrase").fill("my recovery passphrase");
    await dialog.getByLabel("Your current password").fill("my-password");
    await dialog.getByRole("button", { name: /Check compatibility/ }).click();
    await expect(dialog.locator(".alv-backup-check--warn")).toBeVisible();
    await expect(dialog.locator(".alv-backup-check--fail")).toBeVisible();
    await scan(page, ".alv-backup-checks");
  });
}
