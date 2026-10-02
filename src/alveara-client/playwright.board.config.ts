import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-011-C01: config for visit-board-real-backend.spec.ts ONLY. Same shape as playwright.flow.config.ts: the real Vite dev server proxying /api to a real
 * dotnet API the caller already started (migrated database, AdminBootstrapSecret = E2E_BOOTSTRAP_SECRET or the "e2e-real-backend-secret" default,
 * AuthAttemptRateLimit__PermitLimit raised because the spec signs in several accounts from one address). The spec waits for the board's own 15-second
 * refresh once, so its timeout is longer than the others'.
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "visit-board-real-backend.spec.ts",
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
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
