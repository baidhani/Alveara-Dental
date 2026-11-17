import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-N022 A1: config for encounter-chart-real-backend.spec.ts ONLY. Same shape as playwright.clinical.config.ts: the real Vite dev server proxying /api to a real dotnet
 * API the caller already started (migrated database, AdminBootstrapSecret = E2E_BOOTSTRAP_SECRET or the "e2e-real-backend-secret" default).
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "encounter-chart-real-backend.spec.ts",
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
