import { defineConfig, devices } from "@playwright/test";

/**
 * Gate B (B6): runs ONE generated tablet-width walkthrough (gate-b/tablet-generated/<TABLET_SPEC>.tablet.spec.ts, produced by
 * gate-b/make-tablet-specs.mjs) against the real Vite dev server proxying /api to a real dotnet API the caller already started - the same
 * shape as playwright.calendar.config.ts and friends. Set TABLET_SPEC to the walkthrough name, e.g. calendar-real-backend.
 */
const spec = process.env.TABLET_SPEC;
if (!spec) throw new Error("set TABLET_SPEC, e.g. TABLET_SPEC=calendar-real-backend");

export default defineConfig({
  testDir: "./tablet-generated",
  testMatch: `${spec}.tablet.spec.ts`,
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
