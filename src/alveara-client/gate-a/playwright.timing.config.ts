import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir: ".", testMatch: "gate-a-keygen-timing.spec.ts", workers: 1, reporter: [["list"]], timeout: 180_000,
  use: { baseURL: "http://localhost:5072" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
