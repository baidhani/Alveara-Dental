import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-N030 S14: config for account-bootstrap-real-backend.spec.ts only (first-run setup page, Add staff member with the temporary password, the forced
 * password change, the Practice Owner notices). Run it with scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.account-bootstrap.config.ts
 * -OutputVariable ACCOUNT_BOOTSTRAP_E2E_OUT: the runner starts ChainHost under Test on a fresh database; Vite proxies /api to it.
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "account-bootstrap-real-backend.spec.ts",
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
