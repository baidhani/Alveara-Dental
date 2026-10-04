import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-005-C01: config for clinical-companion-real-backend.spec.ts ONLY. Same shape as
 * playwright.auth.config.ts: the real Vite dev server proxying /api to a real dotnet API the caller
 * already started (migrated database, AdminBootstrapSecret = E2E_BOOTSTRAP_SECRET or the
 * "e2e-real-backend-secret" default, AuthAttemptRateLimit__PermitLimit raised because the spec signs
 * in several accounts from one address).
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "clinical-companion-real-backend.spec.ts",
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
