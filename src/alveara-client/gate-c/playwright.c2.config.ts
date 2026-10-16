import { defineConfig, devices } from "@playwright/test";

/**
 * Gate C C2: config for gate-c-c2-safety-points.spec.ts ONLY. Same shape as safety
 * (see playwright.safety.config.ts): the real Vite dev server proxying /api to a real dotnet API the caller
 * already started (migrated database, AdminBootstrapSecret = E2E_BOOTSTRAP_SECRET or the
 * "e2e-real-backend-secret" default, AuthAttemptRateLimit__PermitLimit raised because the spec signs
 * in several accounts from one address).
 */
export default defineConfig({
  testDir: ".",
  testMatch: "gate-c-c2-safety-points.spec.ts",
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
