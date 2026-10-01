import { defineConfig, devices } from "@playwright/test";
/** Same specification as playwright.auth.config.ts (e2e/auth-real-backend.spec.ts, unmodified) with longer assertion/test timeouts, to separate functional success from the 5 s default on RSA key generation. */
export default defineConfig({
  testDir: "../e2e", testMatch: "auth-real-backend.spec.ts", fullyParallel: false, workers: 1, reporter: [["list"]],
  timeout: 120_000, expect: { timeout: 30_000 },
  use: { baseURL: "http://localhost:5173" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: { command: "npm run dev -- --port 5173", url: "http://localhost:5173", reuseExistingServer: true, timeout: 30_000 },
});
