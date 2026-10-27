import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-018-C01: config for patient-export-package-real-backend.spec.ts ONLY. Same shape as playwright.patient-portability.config.ts: the real Vite dev server proxying /api to a real dotnet API the caller
 * already started (migrated database with snapshot isolation on, AdminBootstrapSecret = E2E_BOOTSTRAP_SECRET or the "e2e-real-backend-secret" default, AuthAttemptRateLimit__PermitLimit raised because the spec
 * signs in several accounts from one address). Run it with scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.patient-export-package.config.ts -OutputVariable PATIENT_EXPORT_PACKAGE_E2E_OUT.
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "patient-export-package-real-backend.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: { baseURL: "http://localhost:5173" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npm run dev -- --port 5173",
    url: "http://localhost:5173",
    reuseExistingServer: true,
    timeout: 30_000,
  },
});
