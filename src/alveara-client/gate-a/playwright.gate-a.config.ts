import { defineConfig, devices } from "@playwright/test";

/** Gate A evidence run: real Vite dev server proxying to a real API the caller started (same setup as playwright.auth.config.ts). */
export default defineConfig({
  testDir: ".",
  testMatch: "gate-a-browser.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: { baseURL: "http://localhost:5173" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: { command: "npm run dev -- --port 5173", url: "http://localhost:5173", reuseExistingServer: true, timeout: 30_000 },
});
