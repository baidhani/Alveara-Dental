import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-001-C01 R02: config for auth-real-backend.spec.ts ONLY (see that file's header comment for
 * why it's separate from playwright.config.ts). Starts the real Vite dev server, which proxies
 * /api to the real application the runner already started (ALV-N030: ChainHost under the Test
 * environment with test support, on 127.0.0.1; there is no bootstrap secret any more) - unlike
 * playwright.config.ts's webServer, there is no backend to auto-start here since it's a real
 * ASP.NET Core process with a real database, not a static build.
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "auth-real-backend.spec.ts",
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
