import { defineConfig, devices } from "@playwright/test";

// Declared viewports for ALV-N001's "usable at agreed desktop and tablet
// widths" acceptance item (N001-R01-02). These are the two widths every
// e2e.spec.ts test runs at.
export const DESKTOP_VIEWPORT = { width: 1280, height: 800 };
export const TABLET_VIEWPORT = { width: 768, height: 1024 };

export default defineConfig({
  testDir: "./e2e",
  // auth-real-backend.spec.ts needs a real API + database and runs via playwright.auth.config.ts (docs/testing/REAL_BACKEND_E2E.md); the rest run against the static build with mocked, authenticated API responses.
  testIgnore: ["auth-real-backend.spec.ts", "patient-registration-real-backend.spec.ts", "patient-workspace-real-backend.spec.ts", "forms-real-backend.spec.ts", "schedule-real-backend.spec.ts", "calendar-real-backend.spec.ts", "patient-flow-real-backend.spec.ts", "visit-board-real-backend.spec.ts", "clinical-real-backend.spec.ts", "clinical-companion-real-backend.spec.ts", "safety-real-backend.spec.ts", "odontogram-real-backend.spec.ts", "odontogram-longitudinal-real-backend.spec.ts", "perio-real-backend.spec.ts", "perio-sessions-real-backend.spec.ts"],
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
