import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir: ".", testMatch: "gate-a-audit-keyboard.spec.ts", fullyParallel: false, workers: 1, reporter: [["list"]], timeout: 180_000,
  use: { baseURL: "http://localhost:5173" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: { command: "npm run dev -- --port 5173", url: "http://localhost:5173", reuseExistingServer: true, timeout: 30_000 },
});
