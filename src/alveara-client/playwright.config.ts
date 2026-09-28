import { defineConfig, devices } from "@playwright/test";

// Declared viewports for ALV-N001's "usable at agreed desktop and tablet
// widths" acceptance item (N001-R01-02). These are the two widths every
// e2e.spec.ts test runs at.
export const DESKTOP_VIEWPORT = { width: 1280, height: 800 };
export const TABLET_VIEWPORT = { width: 768, height: 1024 };

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  reporter: [["list"]],
  use: {
    baseURL: "http://localhost:4173",
  },
  projects: [
    {
      name: "desktop",
      use: { ...devices["Desktop Chrome"], viewport: DESKTOP_VIEWPORT },
    },
    {
      name: "tablet",
      use: { ...devices["Desktop Chrome"], viewport: TABLET_VIEWPORT },
    },
  ],
  webServer: {
    command: "npm run preview -- --port 4173",
    url: "http://localhost:4173",
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
