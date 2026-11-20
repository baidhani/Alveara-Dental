import { defineConfig, devices } from "@playwright/test";

// Declared viewports for ALV-N001's "usable at agreed desktop and tablet
// widths" acceptance item (N001-R01-02). These are the two widths every
// e2e.spec.ts test runs at.
export const DESKTOP_VIEWPORT = { width: 1280, height: 800 };
export const TABLET_VIEWPORT = { width: 768, height: 1024 };

// ALV-N021 A2 (F-064): the Component Showcase exists only in a development build, so the two specs that drive it run against the
// development server; every other spec stays on the production build (which e2e/production-build.spec.ts checks has no showcase).
// The real-backend specs need a real API + database (their own configs); a project-level testIgnore REPLACES this list, so each project repeats it.
const REAL_BACKEND_SPECS = ["auth-real-backend.spec.ts", "patient-registration-real-backend.spec.ts", "patient-workspace-real-backend.spec.ts", "forms-real-backend.spec.ts", "schedule-real-backend.spec.ts", "calendar-real-backend.spec.ts", "patient-flow-real-backend.spec.ts", "visit-board-real-backend.spec.ts", "clinical-real-backend.spec.ts", "clinical-companion-real-backend.spec.ts", "safety-real-backend.spec.ts", "odontogram-real-backend.spec.ts", "odontogram-longitudinal-real-backend.spec.ts", "perio-real-backend.spec.ts", "perio-sessions-real-backend.spec.ts", "diagnoses-real-backend.spec.ts", "diagnoses-structure-real-backend.spec.ts", "procedures-real-backend.spec.ts", "treatment-plans-real-backend.spec.ts", "treatment-plans-complete-real-backend.spec.ts", "billing-real-backend.spec.ts", "billing-ledger-real-backend.spec.ts", "completion-adoption-real-backend.spec.ts", "prescriptions-real-backend.spec.ts", "prescription-workflow-real-backend.spec.ts", "completion-real-backend.spec.ts", "billing-statements-real-backend.spec.ts", "documents-real-backend.spec.ts", "documents-library-real-backend.spec.ts", "notifications-real-backend.spec.ts", "permission-policy-real-backend.spec.ts", "permission-policy-admin-real-backend.spec.ts", "follow-ups-real-backend.spec.ts", "work-queue-real-backend.spec.ts", "external-cases-real-backend.spec.ts", "patient-merge-real-backend.spec.ts", "patient-portability-real-backend.spec.ts", "patient-export-package-real-backend.spec.ts", "bulk-import-real-backend.spec.ts", "reports-real-backend.spec.ts", "practice-time-zones-real-backend.spec.ts", "encounter-chart-real-backend.spec.ts", "finalized-encounter-real-backend.spec.ts", "a3-display-real-backend.spec.ts", "treatment-plan-integrity-real-backend.spec.ts", "billing-integrity-real-backend.spec.ts", "identity-audit-integrity-real-backend.spec.ts"];
const DEV_ONLY_SPECS = ["shell.spec.ts", "notification-contrast.spec.ts"];
const DEV_URL = "http://localhost:4174";

export default defineConfig({
  testDir: "./e2e",
  // auth-real-backend.spec.ts needs a real API + database and runs via playwright.auth.config.ts (docs/testing/REAL_BACKEND_E2E.md); the rest run against the static build with mocked, authenticated API responses.
  testIgnore: REAL_BACKEND_SPECS,
  fullyParallel: true,
  reporter: [["list"]],
  use: {
    baseURL: "http://localhost:4173",
  },
  projects: [
    {
      name: "desktop",
      testIgnore: [...REAL_BACKEND_SPECS, ...DEV_ONLY_SPECS],
      use: { ...devices["Desktop Chrome"], viewport: DESKTOP_VIEWPORT },
    },
    {
      name: "tablet",
      testIgnore: [...REAL_BACKEND_SPECS, ...DEV_ONLY_SPECS],
      use: { ...devices["Desktop Chrome"], viewport: TABLET_VIEWPORT },
    },
    {
      name: "desktop-dev",
      testMatch: DEV_ONLY_SPECS,
      use: { ...devices["Desktop Chrome"], viewport: DESKTOP_VIEWPORT, baseURL: DEV_URL },
    },
    {
      name: "tablet-dev",
      testMatch: DEV_ONLY_SPECS,
      use: { ...devices["Desktop Chrome"], viewport: TABLET_VIEWPORT, baseURL: DEV_URL },
    },
  ],
  webServer: [
    {
      command: "npm run preview -- --port 4173",
      url: "http://localhost:4173",
      reuseExistingServer: false,
      timeout: 30_000,
    },
    {
      command: "npm run dev -- --port 4174 --strictPort",
      url: DEV_URL,
      reuseExistingServer: false,
      timeout: 60_000,
    },
  ],
});
