import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-N030 S21: config for server-states-real-backend.spec.ts only (the not-installed and upgrade-required screens; run with -DatabaseState NotInstalled
 * or UpgradeRequired). Run it with scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.server-states.config.ts
 * -DatabaseState NotInstalled (or UpgradeRequired): the runner starts ChainHost under Test; Vite proxies /api to it.
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "server-states-real-backend.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: {
    baseURL: "http://localhost:5173",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npm run dev -- --port 5173",
    url: "http://localhost:5173",
    reuseExistingServer: true,
    timeout: 30_000,
  },
});
